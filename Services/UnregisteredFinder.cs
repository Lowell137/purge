using Kaldir.Models;

namespace Kaldir.Services;

// Stubborn Program Remover: uninstall kaydı bulunmayan programlar. Hazır bir veritabanı yerine
// yaygın kurulum köklerinde (Program Files, AppData\Programs) hiçbir kayıtlı programa bağlanmayan klasörleri listeler.
public static class UnregisteredFinder
{
    static readonly HashSet<string> SystemFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common Files", "Windows NT", "WindowsApps", "ModifiableWindowsApps", "Windows Defender", "Windows Defender Advanced Threat Protection",
        "Windows Mail", "Windows Media Player", "Windows Photo Viewer", "Windows Portable Devices", "Windows Security", "WindowsPowerShell",
        "Internet Explorer", "Microsoft", "Microsoft.NET", "dotnet", "Reference Assemblies", "MSBuild", "Uninstall Information",
        "Microsoft Update Health Tools", "PackageManagement", "Windows Sidebar", "Application Verifier", "IIS", "IIS Express",
        "WindowsApps", "Edge", "EdgeWebView", "EdgeCore", "Temp", "Packages",
        "Windows Kits", "Microsoft SDKs", "Microsoft Visual Studio", "WSL", "Windows Sidebar", "Windows Media Player",
        "Microsoft Office", "Microsoft Analysis Services", "MSBuild", "HTML Help Workshop", "Windows Identity Foundation",
    };

    // MSI'nin kurduğu klasörler: Windows bunları Installer\Folders altında tutar (InstallLocation yazmayan MSI'lar için tek güvenilir iz).
    static HashSet<string> MsiFolders()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var k = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders");
            if (k != null) foreach (string n in k.GetValueNames()) set.Add(n.TrimEnd('\\') + "\\");
        }
        catch { }
        return set;
    }

    public static List<LeftoverItem> Find(IEnumerable<AppEntry> registered)
    {
        var apps = registered.Where(a => !a.IsStore).ToList();
        var msi = MsiFolders();
        var res = new List<LeftoverItem>();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (string root in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(root).ToList(); } catch { continue; }
            foreach (string dir in dirs)
            {
                string leaf = Path.GetFileName(dir);
                if (SystemFolders.Contains(leaf)) continue;
                try { if (File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint)) continue; } catch { }
                string pre = dir.TrimEnd('\\') + "\\";
                if (msi.Any(m => m.StartsWith(pre, StringComparison.OrdinalIgnoreCase))) continue;
                if (IsCovered(dir, leaf, apps)) continue;
                long size = ProgramScanner.DirSize(dir);
                res.Add(new LeftoverItem { Kind = "Folder", Path = dir, SizeBytes = size, IsSelected = false });
            }
        }
        return res.OrderByDescending(i => i.SizeBytes).ToList();
    }

    static bool IsCovered(string dir, string leaf, List<AppEntry> apps)
    {
        string prefix = dir.TrimEnd('\\') + "\\";
        string leafNorm = LeftoverScanner.Norm(leaf);
        foreach (var a in apps)
        {
            if (a.InstallLocation.Length > 3 && (a.InstallLocation.TrimEnd('\\') + "\\").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            if (a.IconPath.Length > 0 && a.IconPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            if (a.UninstallString.Contains(prefix, StringComparison.OrdinalIgnoreCase) || a.QuietUninstallString.Contains(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            string core = LeftoverScanner.Core(a.Name);
            if (leafNorm.Length >= 4 && core.Length >= 4 && (core == leafNorm || (leafNorm.Length >= 6 && core.StartsWith(leafNorm, StringComparison.Ordinal)))) return true;
            // Kısa klasör adları: "Go" ↔ "Go Programming Language", "WSL" ↔ "Windows Subsystem for Linux" (baş harfler)
            var words = a.Name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (leafNorm.Length >= 2 && words.Length > 0 && LeftoverScanner.Norm(words[0]) == leafNorm) return true;
            string initials = string.Concat(words.Where(w => w.Length > 0 && char.IsUpper(w[0])).Select(w => char.ToLowerInvariant(w[0])));
            if (leafNorm.Length >= 2 && initials == leafNorm) return true;
            string pub = LeftoverScanner.Norm(a.Publisher);
            if (pub.Length >= 4 && pub == leafNorm) return true;   // yayıncı klasörü: <kök>\<Yayıncı>\<Ürün>
        }
        return false;
    }
}
