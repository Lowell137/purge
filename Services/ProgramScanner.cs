using System.Globalization;
using System.Text.RegularExpressions;
using Kaldir.Models;
using Microsoft.Win32;

namespace Kaldir.Services;

public static class ProgramScanner
{
    public static List<AppEntry> Scan()
    {
        var list = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, label, view32) in new[]
        {
            (RegistryHive.LocalMachine, "HKLM", false),
            (RegistryHive.LocalMachine, "HKLM", true),
            (RegistryHive.CurrentUser, "HKCU", false),
            (RegistryHive.CurrentUser, "HKCU", true),
        })
        {
            try
            {
                using var bk = RegistryKey.OpenBaseKey(hive, view32 ? RegistryView.Registry32 : RegistryView.Registry64);
                using var root = bk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (root == null) continue;
                foreach (string sub in root.GetSubKeyNames())
                {
                    try
                    {
                        using var k = root.OpenSubKey(sub);
                        if (k == null) continue;
                        string name = (k.GetValue("DisplayName") as string ?? "").Trim();
                        if (name.Length == 0) continue;
                        if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if ((k.GetValue("ParentKeyName") as string ?? "").Length > 0) continue;
                        string rel = k.GetValue("ReleaseType") as string ?? "";
                        if (rel is "Update" or "Hotfix" or "Security Update" or "Update Rollup") continue;
                        string us = (k.GetValue("UninstallString") as string ?? "").Trim();
                        string qs = (k.GetValue("QuietUninstallString") as string ?? "").Trim();
                        if (us.Length == 0 && qs.Length == 0) continue;
                        if (Regex.IsMatch(name, @"^(KB\d{6,}|Update for |Security Update for |Hotfix )")) continue;

                        string ver = k.GetValue("DisplayVersion") as string ?? "";
                        // HKLM'de 64 ve 32 bit görünümü aynı kaydı gösterebilir: tekilleştir.
                        if (!seen.Add($"{label}|{name}|{ver}")) continue;

                        long size = 0;
                        if (k.GetValue("EstimatedSize") is int kb && kb > 0) size = kb * 1024L;
                        string loc = (k.GetValue("InstallLocation") as string ?? "").Trim().Trim('"');
                        if (loc.Length == 0) loc = GuessLocation(k.GetValue("DisplayIcon") as string, us);

                        list.Add(new AppEntry
                        {
                            Name = name,
                            Publisher = (k.GetValue("Publisher") as string ?? "").Trim(),
                            Version = ver,
                            InstallLocation = loc,
                            UninstallString = us,
                            QuietUninstallString = qs,
                            IconPath = IconFile(k.GetValue("DisplayIcon") as string),
                            RegistryHive = label,
                            Registry32 = view32,
                            RegistryKeyName = sub,
                            InstallDate = ParseDate(k.GetValue("InstallDate") as string),
                            SizeBytes = size,
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }
        return list;
    }

    static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParseExact(s.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && d.Year > 1990) return d;
        return null;
    }

    // InstallLocation boşsa DisplayIcon / kaldırıcı yolundan klasörü tahmin et. Sistem klasörlerini asla döndürme.
    static string GuessLocation(string? icon, string uninstall)
    {
        foreach (string? src in new[] { icon, uninstall })
        {
            string? p = ExtractPath(src);
            if (p == null) continue;
            try
            {
                string? dir = Path.GetDirectoryName(p);
                if (dir != null && Directory.Exists(dir) && !LeftoverScanner.IsProtectedRoot(dir)) return dir;
            }
            catch { }
        }
        return "";
    }

    static string IconFile(string? s)
    {
        string? p = ExtractPath(s);
        try { return p != null && File.Exists(p) ? p : ""; } catch { return ""; }
    }

    static string? ExtractPath(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.StartsWith('"')) { int e = s.IndexOf('"', 1); if (e > 1) return s[1..e]; }
        int comma = s.IndexOf(',');
        if (comma > 0 && File.Exists(s[..comma])) return s[..comma];
        int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return s[..(exe + 4)];
        return File.Exists(s) ? s : null;
    }

    public static bool StillRegistered(AppEntry e)
    {
        if (e.IsStore) return false;
        try
        {
            var hive = e.RegistryHive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            using var bk = RegistryKey.OpenBaseKey(hive, e.Registry32 ? RegistryView.Registry32 : RegistryView.Registry64);
            using var k = bk.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{e.RegistryKeyName}");
            return k != null;
        }
        catch { return false; }
    }

    public static long DirSize(string path)
    {
        long total = 0;
        try
        {
            var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", opt))
            {
                try { total += f.Length; } catch { }
            }
        }
        catch { }
        return total;
    }
}
