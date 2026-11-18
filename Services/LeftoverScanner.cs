using System.Text.RegularExpressions;
using Kaldir.Models;
using Microsoft.Win32;

namespace Kaldir.Services;

// Kaldırma sonrası kalıntı tarama. Bilinçli olarak muhafazakâr: yalnız uygulama adıyla (tam ya da uzun önek) eşleşen
// klasör/anahtarları ve uygulamanın kendi kurulum klasörüne işaret eden Run kayıtlarını önerir. Hiçbiri otomatik silinmez.
public static class LeftoverScanner
{
    static readonly string[] Protected =
    {
        "windows", "microsoft", "common files", "windowsapps", "system32", "program files", "programdata", "users",
        "windows nt", "internet explorer", "microsoft shared", "intel", "nvidia corporation", "nvidia", "amd", "realtek",
        "packages", "classes", "wow6432node", "software", "default", "public", "temp", "windows defender",
    };

    public static bool IsProtectedRoot(string path)
    {
        string p = path.TrimEnd('\\');
        foreach (var root in Roots())
            if (string.Equals(p, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
        return p.StartsWith(win, StringComparison.OrdinalIgnoreCase) || p.Length <= 3;
    }

    // Kurulum klasörü olabilecek kökün (kökün kendisi değil) altında mı? Sistem, kullanıcı klasörleri ve kökün kendisi hariç.
    public static bool IsUnderInstallRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsProtectedRoot(path)) return false;
        string p = path.TrimEnd('\\') + "\\";
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        })
        {
            string r = root.TrimEnd('\\') + "\\";
            if (p.Length > r.Length && p.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            {
                string first = p[r.Length..].Split('\\')[0];
                if (!Protected.Contains(first.ToLowerInvariant())) return true;
            }
        }
        return false;
    }

    // Yolun kurulum köklerinden birinin hemen altındaki klasörü (ve varsa bir alt seviyesini) döndür: "<kök>\<ad>", "<kök>\<yayıncı>\<ad>".
    public static IEnumerable<string> InstallFolderCandidates(string path)
    {
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        })
        {
            string r = root.TrimEnd('\\') + "\\";
            if (!path.StartsWith(r, StringComparison.OrdinalIgnoreCase)) continue;
            string[] parts = path[r.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;   // dosya doğrudan kökte: klasör yok
            if (Protected.Contains(parts[0].ToLowerInvariant())) continue;
            yield return r + parts[0];
            if (parts.Length >= 3) yield return r + parts[0] + "\\" + parts[1];
        }
    }

    static IEnumerable<string> Roots()
    {
        var f = Environment.SpecialFolder.ProgramFiles;
        yield return Environment.GetFolderPath(f);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }

    public static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");

    // "Foo App (64-bit) 1.2.3" -> "fooapp"
    public static string Core(string name)
    {
        string n = Regex.Replace(name, @"\([^)]*\)", "");   // "(x64 edition)", "(Windows port)" gibi ekleri at
        n = Regex.Replace(n, @"\b(v|version)?\s*\d+(\.\d+)+\b", "", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"\b(x64|x86|64-bit|32-bit)\b", "", RegexOptions.IgnoreCase);
        return Norm(n);
    }

    static bool NameMatches(string childName, string core)
    {
        string c = Norm(childName);
        if (c.Length == 0 || core.Length < 4) return false;
        if (Protected.Contains(childName.ToLowerInvariant())) return false;
        if (c == core) return true;
        return core.Length >= 6 && c.StartsWith(core, StringComparison.Ordinal);
    }

    public static List<LeftoverItem> Scan(AppEntry app, bool includeInstallLocation = true)
    {
        var res = new List<LeftoverItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string core = Core(app.Name);
        string pubCore = Norm(app.Publisher);

        void AddDir(string path)
        {
            if (!seen.Add(path) || IsProtectedRoot(path)) return;
            res.Add(new LeftoverItem { Kind = "Folder", Path = path, SizeBytes = ProgramScanner.DirSize(path) });
        }

        // 1) Kurulum klasörü hâlâ duruyorsa.
        if (includeInstallLocation && app.InstallLocation.Length > 0 && Directory.Exists(app.InstallLocation) && !IsProtectedRoot(app.InstallLocation))
            AddDir(app.InstallLocation);

        // 2) Bilinen kökler altında ada uyan klasör/dosyalar (bir seviye).
        foreach (string root in Roots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (string dir in Directory.EnumerateDirectories(root))
                {
                    string leaf = Path.GetFileName(dir);
                    if (NameMatches(leaf, core)) { AddDir(dir); continue; }
                    // Yayıncı klasörü altında ürün klasörü: <root>\<Publisher>\<App>
                    if (pubCore.Length >= 4 && Norm(leaf) == pubCore && !Protected.Contains(leaf.ToLowerInvariant()))
                    {
                        try
                        {
                            foreach (string sub in Directory.EnumerateDirectories(dir))
                                if (NameMatches(Path.GetFileName(sub), core)) AddDir(sub);
                        }
                        catch { }
                    }
                }
                foreach (string file in Directory.EnumerateFiles(root, "*.lnk"))
                {
                    if (NameMatches(Path.GetFileNameWithoutExtension(file), core) && seen.Add(file))
                        res.Add(new LeftoverItem { Kind = "File", Path = file, SizeBytes = new FileInfo(file).Length });
                }
            }
            catch { }
        }

        // 3) Kayıt defteri: HKCU/HKLM Software\<Ad> ve Software\<Yayıncı>\<Ad>.
        foreach (var (hive, label) in new[] { (RegistryHive.CurrentUser, "HKCU"), (RegistryHive.LocalMachine, "HKLM") })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var bk = RegistryKey.OpenBaseKey(hive, view);
                    using var sw = bk.OpenSubKey("SOFTWARE");
                    if (sw == null) continue;
                    foreach (string sub in sw.GetSubKeyNames())
                    {
                        if (NameMatches(sub, core)) AddKey(res, seen, label, view, "SOFTWARE\\" + sub);
                        else if (pubCore.Length >= 4 && Norm(sub) == pubCore && !Protected.Contains(sub.ToLowerInvariant()))
                        {
                            using var pk = sw.OpenSubKey(sub);
                            if (pk == null) continue;
                            foreach (string child in pk.GetSubKeyNames())
                                if (NameMatches(child, core)) AddKey(res, seen, label, view, "SOFTWARE\\" + sub + "\\" + child);
                        }
                    }
                }
                catch { }
            }
        }

        // 4) Hâlâ duran Uninstall kaydı (kaldırıcı çalıştı ama kayıt kaldı).
        // (Yalnız kaldırma sonrası taramada: kurulu uygulamanın kendi kaydı "kalıntı" sayılmaz.)
        if (includeInstallLocation && !app.IsStore && ProgramScanner.StillRegistered(app))
            AddKey(res, seen, app.RegistryHive, app.Registry32 ? RegistryView.Registry32 : RegistryView.Registry64,
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{app.RegistryKeyName}");

        // 5) Kurulum klasörüne işaret eden başlangıç (Run) kayıtları.
        if (app.InstallLocation.Length > 3)
        {
            foreach (var (hive, label) in new[] { (RegistryHive.CurrentUser, "HKCU"), (RegistryHive.LocalMachine, "HKLM") })
            {
                try
                {
                    using var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var run = bk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    if (run == null) continue;
                    foreach (string vn in run.GetValueNames())
                    {
                        string data = run.GetValue(vn) as string ?? "";
                        if (data.Contains(app.InstallLocation, StringComparison.OrdinalIgnoreCase))
                            res.Add(new LeftoverItem { Kind = "Registry value", Path = $@"{label}\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", ValueName = vn });
                    }
                }
                catch { }
            }
        }

        // 6) Sistem servisleri ve zamanlanmış görevler. Yalnız kurulum klasörüne işaret edenler (ad tahmini yok).
        if (app.InstallLocation.Length > 3 && !IsProtectedRoot(app.InstallLocation))
        {
            string loc = app.InstallLocation.TrimEnd('\\') + "\\";
            try
            {
                using var svcRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (svcRoot != null)
                    foreach (string sn in svcRoot.GetSubKeyNames())
                    {
                        try
                        {
                            using var sk = svcRoot.OpenSubKey(sn);
                            string img = (sk?.GetValue("ImagePath") as string ?? "").Replace("\"", "");
                            if (img.Contains(loc, StringComparison.OrdinalIgnoreCase))
                                res.Add(new LeftoverItem { Kind = "Service", Path = sn });
                        }
                        catch { }
                    }
            }
            catch { }
            try
            {
                string tasks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
                if (Directory.Exists(tasks))
                {
                    var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                    foreach (string tf in Directory.EnumerateFiles(tasks, "*", opt))
                    {
                        try
                        {
                            if (new FileInfo(tf).Length > 200_000) continue;
                            if (File.ReadAllText(tf).Contains(loc, StringComparison.OrdinalIgnoreCase))
                                res.Add(new LeftoverItem { Kind = "Scheduled task", Path = "\\" + Path.GetRelativePath(tasks, tf) });
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // 7) Sistem izleri: App Paths, SharedDLLs, Installer\Folders,
        //    Tracing, MuiCache, güvenlik duvarı ilkeleri ve CLSID. Hepsi yalnız kurulum klasörüne işaret edenleri ekler.
        if (app.InstallLocation.Length > 3 && !IsProtectedRoot(app.InstallLocation))
            ScanSystemTraces(app, res, seen);

        return res;
    }

    static void ScanSystemTraces(AppEntry app, List<LeftoverItem> res, HashSet<string> seen)
    {
        string loc = app.InstallLocation.TrimEnd('\\') + "\\";
        int budget = 300;   // tek bir uygulama için makul üst sınır
        bool In(string? s) => !string.IsNullOrEmpty(s) && s.Contains(loc, StringComparison.OrdinalIgnoreCase);

        void ValuesByName(RegistryKey root, string label, string sub)
        {
            try
            {
                using var k = root.OpenSubKey(sub);
                if (k == null) return;
                foreach (string vn in k.GetValueNames())
                    if (budget > 0 && In(vn) && seen.Add($"V|{label}\\{sub}|{vn}")) { res.Add(new LeftoverItem { Kind = "Registry value", Path = label + "\\" + sub, ValueName = vn }); budget--; }
            }
            catch { }
        }
        void ValuesByData(RegistryKey root, string label, string sub)
        {
            try
            {
                using var k = root.OpenSubKey(sub);
                if (k == null) return;
                foreach (string vn in k.GetValueNames())
                    if (budget > 0 && In(k.GetValue(vn)?.ToString()) && seen.Add($"V|{label}\\{sub}|{vn}")) { res.Add(new LeftoverItem { Kind = "Registry value", Path = label + "\\" + sub, ValueName = vn }); budget--; }
            }
            catch { }
        }
        void KeyIf(string full) { if (budget > 0 && seen.Add(full)) { res.Add(new LeftoverItem { Kind = "Registry key", Path = full }); budget--; } }

        var hklm = Registry.LocalMachine; var hkcu = Registry.CurrentUser;
        const string cv = @"SOFTWARE\Microsoft\Windows\CurrentVersion";
        const string cv32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion";

        // SharedDLLs ve Installer\Folders
        ValuesByName(hklm, "HKLM", cv + @"\SharedDLLs");
        ValuesByName(hklm, "HKLM", cv32 + @"\SharedDLLs");
        ValuesByName(hklm, "HKLM", cv + @"\Installer\Folders");
        // MuiCache
        ValuesByName(hkcu, "HKCU", @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache");
        // Güvenlik duvarı: kurallar (veri içinde "App=<yol>") ve yetkili uygulama listesi (ad = yol)
        const string fw = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
        ValuesByData(hklm, "HKLM", fw + @"\FirewallRules");
        ValuesByName(hklm, "HKLM", fw + @"\StandardProfile\AuthorizedApplications\List");
        ValuesByName(hklm, "HKLM", fw + @"\PublicProfile\AuthorizedApplications\List");

        // App Paths: App Paths\<exe> varsayılan değeri ya da Path değeri kurulum klasörüne gidiyorsa
        foreach (var (root, label, sub) in new[] { (hklm, "HKLM", cv + @"\App Paths"), (hklm, "HKLM", cv32 + @"\App Paths"), (hkcu, "HKCU", cv + @"\App Paths") })
        {
            try
            {
                using var ap = root.OpenSubKey(sub);
                if (ap == null) continue;
                foreach (string name in ap.GetSubKeyNames())
                {
                    using var k = ap.OpenSubKey(name);
                    if (In(k?.GetValue("")?.ToString()) || In(k?.GetValue("Path")?.ToString())) KeyIf($"{label}\\{sub}\\{name}");
                }
            }
            catch { }
        }

        // CLSID: InprocServer32 / LocalServer32 kurulum klasöründeki bir dosyayı gösteriyorsa kayıt artık yetim
        foreach (var (root, label, sub) in new[] { (hklm, "HKLM", @"SOFTWARE\Classes\CLSID"), (hklm, "HKLM", @"SOFTWARE\WOW6432Node\Classes\CLSID"), (hkcu, "HKCU", @"Software\Classes\CLSID") })
        {
            try
            {
                using var cl = root.OpenSubKey(sub);
                if (cl == null) continue;
                foreach (string guid in cl.GetSubKeyNames())
                {
                    if (budget <= 0) break;
                    foreach (string srv in new[] { "InprocServer32", "LocalServer32" })
                    {
                        using var k = cl.OpenSubKey($@"{guid}\{srv}");
                        if (k != null && In(k.GetValue("")?.ToString())) { KeyIf($"{label}\\{sub}\\{guid}"); break; }
                    }
                }
            }
            catch { }
        }

        // Tracing: Tracing\<exe>_RASAPI32 ve _RASMANCS, exe adı kurulum klasöründeki bir dosyaysa
        try
        {
            if (Directory.Exists(app.InstallLocation))
            {
                var exes = new HashSet<string>(Directory.EnumerateFiles(app.InstallLocation, "*.exe").Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase);
                using var tr = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Tracing");
                if (tr != null)
                    foreach (string name in tr.GetSubKeyNames())
                    {
                        int u = name.LastIndexOf('_');
                        if (u > 0 && exes.Contains(name[..u])) KeyIf(@"HKLM\SOFTWARE\Microsoft\Tracing\" + name);
                    }
            }
        }
        catch { }
    }

    static void AddKey(List<LeftoverItem> res, HashSet<string> seen, string hive, RegistryView view, string sub)
    {
        // 32 bit görünümü WOW6432Node altına düşer; aynı fiziksel anahtarı iki kez listelememek için gerçek yolu kullan.
        string full = hive + "\\" + (view == RegistryView.Registry32 && !sub.StartsWith(@"SOFTWARE\Microsoft", StringComparison.OrdinalIgnoreCase)
            && hive == "HKLM" ? sub.Replace("SOFTWARE\\", "SOFTWARE\\WOW6432Node\\") : sub);
        if (!seen.Add(full)) return;
        res.Add(new LeftoverItem { Kind = "Registry key", Path = full });
    }
}
