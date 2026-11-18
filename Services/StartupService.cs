using Microsoft.Win32;

namespace Kaldir.Services;

public class StartupEntry
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Location { get; set; } = "";     // "HKCU Run", "HKLM Run", "Startup folder (current user)" ...
    public bool Enabled { get; set; }
    public bool NeedsAdmin { get; set; }

    // StartupApproved eşlemesi
    public RegistryHive Hive { get; set; }
    public string ApprovedKind { get; set; } = "Run";   // Run | Run32 | StartupFolder
    public string ApprovedName { get; set; } = "";      // değer adı (Run) veya dosya adı (klasör)
}

// Windows'un kendi Başlangıç uygulamaları sayfasıyla aynı mekanizma: değer silinmez, StartupApproved altında 02 (açık) / 03 (kapalı) yazılır.
public static class StartupService
{
    const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static List<StartupEntry> Scan()
    {
        var list = new List<StartupEntry>();

        foreach (var (hive, label, view, kind, loc) in new[]
        {
            (RegistryHive.CurrentUser, "HKCU", RegistryView.Registry64, "Run", RunPath),
            (RegistryHive.LocalMachine, "HKLM", RegistryView.Registry64, "Run", RunPath),
            (RegistryHive.LocalMachine, "HKLM", RegistryView.Registry32, "Run32", RunPath),
        })
        {
            try
            {
                using var bk = RegistryKey.OpenBaseKey(hive, view);
                using var run = bk.OpenSubKey(loc);
                if (run == null) continue;
                foreach (string vn in run.GetValueNames())
                {
                    string cmd = run.GetValue(vn)?.ToString() ?? "";
                    list.Add(new StartupEntry
                    {
                        Name = vn,
                        Command = cmd,
                        Location = $"{label} Run" + (kind == "Run32" ? " (32-bit)" : ""),
                        Enabled = IsEnabled(hive, kind, vn),
                        NeedsAdmin = hive == RegistryHive.LocalMachine,
                        Hive = hive, ApprovedKind = kind, ApprovedName = vn,
                    });
                }
            }
            catch { }
        }

        foreach (var (folder, hive, label) in new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "Startup folder (current user)"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "Startup folder (all users)"),
        })
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string f in Directory.EnumerateFiles(folder))
                {
                    string fn = Path.GetFileName(f);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new StartupEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Command = f,
                        Location = label,
                        Enabled = IsEnabled(hive, "StartupFolder", fn),
                        NeedsAdmin = hive == RegistryHive.LocalMachine,
                        Hive = hive, ApprovedKind = "StartupFolder", ApprovedName = fn,
                    });
                }
            }
            catch { }
        }
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static bool IsEnabled(RegistryHive hive, string kind, string name)
    {
        try
        {
            using var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = bk.OpenSubKey(Approved + kind);
            if (k?.GetValue(name) is byte[] b && b.Length > 0)
                return (b[0] & 1) == 0;   // 02/06 açık, 03 kapalı (bit 0 = devre dışı)
        }
        catch { }
        return true;   // kayıt yoksa Windows açık sayar
    }

    public static string? SetEnabled(StartupEntry e, bool enabled)
    {
        try
        {
            using var bk = RegistryKey.OpenBaseKey(e.Hive, RegistryView.Registry64);
            using var k = bk.CreateSubKey(Approved + e.ApprovedKind, writable: true);
            var data = new byte[12];
            data[0] = enabled ? (byte)2 : (byte)3;
            if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            k.SetValue(e.ApprovedName, data, RegistryValueKind.Binary);
            e.Enabled = enabled;
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
