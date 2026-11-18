using System.Diagnostics;
using Kaldir.Models;
using Microsoft.VisualBasic.FileIO;

namespace Kaldir.Services;

// "Software Relocator": kurulu programı başka bir sürücüye taşır ve eski yola bir klasör bağlantısı (junction) bırakır,
// böylece program, kısayollar ve kayıt eski yoldan çalışmaya devam eder.
public static class Relocator
{
    static async Task<int> RunHiddenAsync(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        using var p = Process.Start(psi);
        if (p == null) return -1;
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    public static bool IsJunction(string path)
    {
        try { return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint); } catch { return false; }
    }

    public static string? CanMove(AppEntry app)
    {
        if (app.IsStore) return "Store apps cannot be moved.";
        string src = app.InstallLocation.TrimEnd('\\');
        if (src.Length == 0 || !Directory.Exists(src)) return "No install folder known for this program.";
        if (IsJunction(src)) return "This program was already moved (its folder is a link).";
        if (!LeftoverScanner.IsUnderInstallRoot(src)) return "The install folder is not in a standard location (Program Files, AppData, ProgramData).";
        return null;
    }

    // Döndürülen metin hata; null = başarı.
    public static async Task<string?> MoveAsync(string src, string destParent, Action<string>? progress = null)
    {
        src = src.TrimEnd('\\');
        string dst = Path.Combine(destParent, Path.GetFileName(src));
        string old = src + ".kaldir-old";
        try
        {
            if (!Directory.Exists(src)) return "Source folder is missing.";
            if (Directory.Exists(dst) && Directory.EnumerateFileSystemEntries(dst).Any()) return $"'{dst}' already exists and is not empty.";
            if (dst.StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase)) return "Destination is inside the source folder.";
            if (Directory.Exists(old)) return $"'{old}' exists from an earlier attempt; remove it first.";

            progress?.Invoke("Closing running processes");
            Uninstaller.KillProcessesIn(src);

            progress?.Invoke("Copying files");
            Directory.CreateDirectory(dst);
            int rc = await RunHiddenAsync("robocopy.exe", $"\"{src}\" \"{dst}\" /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP");
            if (rc >= 8) { TryDelete(dst); return $"Copy failed (robocopy exit {rc})."; }

            progress?.Invoke("Verifying copy");
            var (c1, s1) = Count(src);
            var (c2, s2) = Count(dst);
            if (c1 != c2 || s1 != s2) { TryDelete(dst); return $"Copy verification failed ({c1} files/{s1} bytes vs {c2}/{s2}); nothing was changed."; }

            progress?.Invoke("Linking old location");
            Directory.Move(src, old);
            int mk = await RunHiddenAsync("cmd.exe", $"/c mklink /J \"{src}\" \"{dst}\"");
            if (mk != 0 || !IsJunction(src))
            {
                try { Directory.Move(old, src); } catch { }   // geri al
                TryDelete(dst);
                return "Could not create the folder link (administrator rights may be needed). Nothing was changed.";
            }

            progress?.Invoke("Removing the old copy");
            try { FileSystem.DeleteDirectory(old, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }
            catch { /* eski kopya kalırsa zararsız; kullanıcı elle silebilir */ }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    static (int files, long bytes) Count(string dir)
    {
        int n = 0; long b = 0;
        var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", opt)) { n++; try { b += f.Length; } catch { } }
        return (n, b);
    }

    static void TryDelete(string dir) { try { Directory.Delete(dir, true); } catch { } }
}
