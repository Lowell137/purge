using System.Security.Cryptography;

namespace Kaldir.Services;

// "File Shredder": dosya içeriğini üzerine yazıp siler. SSD'lerde aşınma dengeleme yüzünden üzerine yazma kesin garanti vermez
// (arayüzde de söylenir); kesin çözüm diskin şifrelenmesidir.
public static class Shredder
{
    public static bool IsForbidden(string path)
    {
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\');
            if (full.Length <= 3) return true;   // sürücü kökü
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (full.Equals(win, StringComparison.OrdinalIgnoreCase) || full.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
            if (full.Equals(profile, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var sf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
                                       Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.Desktop,
                                       Environment.SpecialFolder.MyDocuments })
                if (full.Equals(Environment.GetFolderPath(sf).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch { return true; }
    }

    public static IEnumerable<string> Expand(IEnumerable<string> paths)
    {
        foreach (string p in paths)
        {
            if (File.Exists(p)) yield return p;
            else if (Directory.Exists(p))
                foreach (string f in Directory.EnumerateFiles(p, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    yield return f;
        }
    }

    public static void ShredFile(string path, int passes = 3)
    {
        var fi = new FileInfo(path);
        fi.Attributes = FileAttributes.Normal;
        long len = fi.Length;
        if (len > 0)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough);
            byte[] buf = new byte[1 << 20];
            for (int pass = 0; pass < passes; pass++)
            {
                fs.Position = 0;
                long left = len;
                while (left > 0)
                {
                    int n = (int)Math.Min(buf.Length, left);
                    if (pass == passes - 1) Array.Clear(buf, 0, n);       // son geçiş sıfır
                    else RandomNumberGenerator.Fill(buf.AsSpan(0, n));     // önceki geçişler rastgele
                    fs.Write(buf, 0, n);
                    left -= n;
                }
                fs.Flush(true);
            }
            fs.SetLength(0);
        }
        // Ad izi de kalmasın: rastgele adla yeniden adlandır, sonra sil.
        string dir = Path.GetDirectoryName(path)!;
        string renamed = Path.Combine(dir, Path.GetRandomFileName());
        try { File.Move(path, renamed); path = renamed; } catch { }
        File.Delete(path);
    }

    public static (int ok, List<string> errors) Run(IEnumerable<string> roots, int passes, Action<string>? progress = null)
    {
        int ok = 0;
        var errors = new List<string>();
        var rootList = roots.ToList();
        foreach (string f in Expand(rootList).ToList())
        {
            try { progress?.Invoke(Path.GetFileName(f)); ShredFile(f, passes); ok++; }
            catch (Exception ex) { errors.Add($"{f}: {ex.Message}"); }
        }
        // Boşalan klasörleri kaldır (içinde silinemeyen dosya kalmışsa dokunma).
        foreach (string d in rootList.Where(Directory.Exists))
        {
            try
            {
                foreach (string sub in Directory.EnumerateDirectories(d, "*", SearchOption.AllDirectories).OrderByDescending(s => s.Length))
                    try { Directory.Delete(sub); } catch { }
                Directory.Delete(d);
            }
            catch { }
        }
        return (ok, errors);
    }
}
