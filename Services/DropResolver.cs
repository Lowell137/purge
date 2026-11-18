using Kaldir.Models;

namespace Kaldir.Services;

// Sürüklenen dosya/klasör/kısayoldan kayıtlı programı bulur.
public static class DropResolver
{
    // .lnk ise hedefine çöz; değilse yolu olduğu gibi döndür.
    public static string ResolveShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
        try
        {
            Type? t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return path;
            dynamic sh = Activator.CreateInstance(t)!;
            dynamic lnk = sh.CreateShortcut(path);
            string target = (string)lnk.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? path : target;
        }
        catch { return path; }
    }

    static bool Under(string path, string root)
    {
        if (root.Length < 4) return false;
        root = root.TrimEnd('\\') + "\\";
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path.TrimEnd('\\') + "\\", root, StringComparison.OrdinalIgnoreCase);
    }

    public static AppEntry? Find(string path, IEnumerable<AppEntry> apps)
    {
        AppEntry? best = null;
        int bestScore = 0;
        foreach (var a in apps)
        {
            int score = 0;
            if (a.InstallLocation.Length > 3 && Under(path, a.InstallLocation))
                score = 1000 + a.InstallLocation.Length;          // en derin kurulum klasörü kazanır
            else if (a.IconPath.Length > 0 && string.Equals(a.IconPath, path, StringComparison.OrdinalIgnoreCase))
                score = 500;
            else if (!a.IsStore && (a.UninstallString.Contains(path, StringComparison.OrdinalIgnoreCase)
                                 || a.QuietUninstallString.Contains(path, StringComparison.OrdinalIgnoreCase)))
                score = 400;
            if (score > bestScore) { bestScore = score; best = a; }
        }
        if (best != null) return best;

        // Yedek yol: MSI gibi kayıtlarda InstallLocation/DisplayIcon boş. Yolun kurulum kökü altındaki klasör adı program adıyla örtüşüyorsa eşle.
        foreach (string folder in LeftoverScanner.InstallFolderCandidates(path))
        {
            string leaf = LeftoverScanner.Norm(Path.GetFileName(folder));
            if (leaf.Length < 4) continue;
            foreach (var a in apps)
            {
                if (a.IsStore) continue;
                string core = LeftoverScanner.Core(a.Name);
                if (core.Length < 4) continue;
                bool hit = core == leaf || (leaf.Length >= 6 && core.StartsWith(leaf, StringComparison.Ordinal));
                if (hit && (best == null || a.Name.Length < best.Name.Length)) best = a;
            }
            if (best != null)
            {
                if (best.InstallLocation.Length == 0) best.InstallLocation = folder;   // kalıntı taramasında kullanılsın
                return best;
            }
        }
        return null;
    }

    // Eşleşme yoksa, bırakılan öğeden kalıntı taraması için geçici bir giriş üret. Kurulum klasörü yalnız bilinen
    // kurulum köklerinin (Program Files, AppData...) altındaysa kullanılır: İndirilenler/Belgeler gibi klasörler asla önerilmez.
    public static AppEntry Synthetic(string path)
    {
        bool isDir = Directory.Exists(path);
        string name = isDir ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path);
        string folder = isDir ? path : (Path.GetDirectoryName(path) ?? "");
        return new AppEntry
        {
            Name = name,
            InstallLocation = LeftoverScanner.IsUnderInstallRoot(folder) ? folder : "",
        };
    }
}
