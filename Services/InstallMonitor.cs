using System.Text.Json;
using Kaldir.Models;
using Microsoft.Win32;

namespace Kaldir.Services;

public class MonitorSnapshot
{
    public DateTime Time { get; set; }
    public List<string> Dirs { get; set; } = new();
    public List<string> Keys { get; set; } = new();
    public List<string> Services { get; set; } = new();
    public List<string> Tasks { get; set; } = new();
    public List<string> RunValues { get; set; } = new();
}

public class MonitorLog
{
    public string Name { get; set; } = "";
    public DateTime Time { get; set; }
    public List<LeftoverItem> Items { get; set; } = new();
}

// Canlı izleme imzalı bir çekirdek sürücüsü ister (dosya/süreç/kayıt filtresi); onun yerine:
// kullanıcı modunda eşdeğeri, kurulumdan önce ve sonra bilinen konumların anlık görüntüsünü alıp farkı günlüğe yazmaktır.
public static class InstallMonitor
{
    static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kaldir", "Monitor");
    static string Pending => Path.Combine(Root, "pending.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    static readonly HashSet<string> NoiseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Temp", "Packages", "CrashDumps", "D3DSCache", "Comms", "ConnectedDevices", "PlaceholderTileLogoFolder", "VirtualStore",
        "History", "IconCache", "Package Cache", "WindowsApps", "Publishers", "Diagnostics", "Windows",
    };

    public static bool HasPending => File.Exists(Pending);

    static IEnumerable<string> DirRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
    }

    static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.EnumerateDirectories(path).ToList(); } catch { return Array.Empty<string>(); }
    }

    public static MonitorSnapshot Take()
    {
        var s = new MonitorSnapshot { Time = DateTime.Now };

        foreach (string root in DirRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (string d1 in SafeDirs(root))
            {
                s.Dirs.Add(d1);
                foreach (string d2 in SafeDirs(d1)) s.Dirs.Add(d2);
            }
        }

        foreach (var (hive, label) in new[] { (Registry.LocalMachine, "HKLM"), (Registry.CurrentUser, "HKCU") })
        {
            foreach (string sw in new[] { "SOFTWARE", @"SOFTWARE\WOW6432Node" })
            {
                try
                {
                    using var k = hive.OpenSubKey(sw);
                    if (k == null) continue;
                    foreach (string n1 in k.GetSubKeyNames())
                    {
                        if (n1.Equals("Classes", StringComparison.OrdinalIgnoreCase) || n1.Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase)) continue;
                        s.Keys.Add($@"{label}\{sw}\{n1}");
                        try
                        {
                            using var k1 = k.OpenSubKey(n1);
                            if (k1 != null) foreach (string n2 in k1.GetSubKeyNames()) s.Keys.Add($@"{label}\{sw}\{n1}\{n2}");
                        }
                        catch { }
                    }
                }
                catch { }
            }
            foreach (string up in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                try
                {
                    using var k = hive.OpenSubKey(up);
                    if (k != null) foreach (string n in k.GetSubKeyNames()) s.Keys.Add($@"{label}\{up}\{n}");
                }
                catch { }
            }
            try
            {
                using var run = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                if (run != null) foreach (string vn in run.GetValueNames()) s.RunValues.Add($@"{label}\SOFTWARE\Microsoft\Windows\CurrentVersion\Run|{vn}");
            }
            catch { }
        }

        try
        {
            using var svc = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (svc != null) s.Services.AddRange(svc.GetSubKeyNames());
        }
        catch { }

        try
        {
            string tasks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
            if (Directory.Exists(tasks))
            {
                var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                foreach (string f in Directory.EnumerateFiles(tasks, "*", opt)) s.Tasks.Add("\\" + Path.GetRelativePath(tasks, f));
            }
        }
        catch { }
        return s;
    }

    public static void Start()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Pending, JsonSerializer.Serialize(Take(), Json));
    }

    public static void Cancel() { try { File.Delete(Pending); } catch { } }

    // Önceki anlık görüntüyle şimdikini karşılaştırır, günlüğü kaydeder.
    public static MonitorLog Finish(string name)
    {
        var before = JsonSerializer.Deserialize<MonitorSnapshot>(File.ReadAllText(Pending))!;
        var after = Take();
        var log = Diff(before, after, name);
        Directory.CreateDirectory(Root);
        string file = Path.Combine(Root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Sanitize(name)}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(log, Json));
        Cancel();
        return log;
    }

    static string Sanitize(string s) => string.Concat(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ')).Trim().Replace(' ', '_');

    public static MonitorLog Diff(MonitorSnapshot before, MonitorSnapshot after, string name)
    {
        var log = new MonitorLog { Name = name, Time = DateTime.Now };

        // En üstteki yeni klasör/anahtar yeter: altındakiler onunla birlikte gider.
        static List<string> TopMost(IEnumerable<string> added)
        {
            var set = new HashSet<string>(added, StringComparer.OrdinalIgnoreCase);
            return set.Where(p => !set.Any(o => o.Length < p.Length && p.StartsWith(o + "\\", StringComparison.OrdinalIgnoreCase))).OrderBy(p => p).ToList();
        }

        var oldDirs = new HashSet<string>(before.Dirs, StringComparer.OrdinalIgnoreCase);
        foreach (string d in TopMost(after.Dirs.Where(d => !oldDirs.Contains(d))))
        {
            if (d.Split('\\').Any(seg => NoiseNames.Contains(seg))) continue;
            if (LeftoverScanner.IsProtectedRoot(d)) continue;
            log.Items.Add(new LeftoverItem { Kind = "Folder", Path = d, SizeBytes = ProgramScanner.DirSize(d) });
        }

        var oldKeys = new HashSet<string>(before.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (string k in TopMost(after.Keys.Where(k => !oldKeys.Contains(k))))
            log.Items.Add(new LeftoverItem { Kind = "Registry key", Path = ToBase64View(k) });

        var oldSvc = new HashSet<string>(before.Services, StringComparer.OrdinalIgnoreCase);
        foreach (string sv in after.Services.Where(x => !oldSvc.Contains(x))) log.Items.Add(new LeftoverItem { Kind = "Service", Path = sv });

        var oldTasks = new HashSet<string>(before.Tasks, StringComparer.OrdinalIgnoreCase);
        foreach (string t in after.Tasks.Where(x => !oldTasks.Contains(x))) log.Items.Add(new LeftoverItem { Kind = "Scheduled task", Path = t });

        var oldRun = new HashSet<string>(before.RunValues, StringComparer.OrdinalIgnoreCase);
        foreach (string r in after.RunValues.Where(x => !oldRun.Contains(x)))
        {
            int bar = r.IndexOf('|');
            log.Items.Add(new LeftoverItem { Kind = "Registry value", Path = r[..bar], ValueName = r[(bar + 1)..] });
        }
        return log;
    }

    static string ToBase64View(string k) => k;   // anahtarlar zaten gerçek yolla (WOW6432Node dahil) saklandı

    public static List<(string file, string name, DateTime time, int count)> Logs()
    {
        var res = new List<(string, string, DateTime, int)>();
        if (!Directory.Exists(Root)) return res;
        foreach (string f in Directory.EnumerateFiles(Root, "*.json").Where(f => !f.EndsWith("pending.json")).OrderByDescending(f => f))
        {
            try
            {
                var l = JsonSerializer.Deserialize<MonitorLog>(File.ReadAllText(f));
                if (l != null) res.Add((f, l.Name, l.Time, l.Items.Count));
            }
            catch { }
        }
        return res;
    }

    public static MonitorLog? Load(string file)
    {
        try { return JsonSerializer.Deserialize<MonitorLog>(File.ReadAllText(file)); } catch { return null; }
    }
}
