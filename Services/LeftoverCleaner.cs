using System.Diagnostics;
using Kaldir.Models;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace Kaldir.Services;

// Silmeden önce kayıt defteri anahtarlarını .reg olarak yedekler, dosya/klasörleri Geri Dönüşüm Kutusu'na yollar.
public static class LeftoverCleaner
{
    public static string BackupRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kaldir", "Backups");

    public static async Task<(int ok, List<string> errors, string backupDir)> CleanAsync(IEnumerable<LeftoverItem> items)
    {
        string dir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        int ok = 0, n = 0;
        var errors = new List<string>();

        foreach (var it in items)
        {
            try
            {
                switch (it.Kind)
                {
                    case "Folder":
                        if (Directory.Exists(it.Path))
                            FileSystem.DeleteDirectory(it.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        break;
                    case "File":
                        if (File.Exists(it.Path))
                            FileSystem.DeleteFile(it.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        break;
                    case "Registry key":
                        Directory.CreateDirectory(dir);
                        await ExportAsync(it.Path, Path.Combine(dir, $"{++n:000}.reg"));
                        DeleteKey(it.Path);
                        break;
                    case "Registry value":
                        Directory.CreateDirectory(dir);
                        await ExportAsync(it.Path, Path.Combine(dir, $"{++n:000}.reg"));
                        DeleteValue(it.Path, it.ValueName);
                        break;
                    case "Service":
                        Directory.CreateDirectory(dir);
                        await ExportAsync($@"HKLM\SYSTEM\CurrentControlSet\Services\{it.Path}", Path.Combine(dir, $"{++n:000}-service.reg"));
                        await RunHiddenAsync("sc.exe", $"stop \"{it.Path}\"");
                        if (await RunHiddenAsync("sc.exe", $"delete \"{it.Path}\"") != 0) throw new IOException("sc delete failed (administrator needed)");
                        break;
                    case "Scheduled task":
                        if (await RunHiddenAsync("schtasks.exe", $"/delete /tn \"{it.Path}\" /f") != 0) throw new IOException("schtasks delete failed (administrator needed)");
                        break;
                }
                ok++;
            }
            catch (Exception ex) { errors.Add($"{it.Path}: {ex.Message}"); }
        }
        return (ok, errors, Directory.Exists(dir) ? dir : "");
    }

    // Uninstall kaydını (kaldırıcısı bozuk/eksik programlar için) yedekleyip siler.
    public static async Task<string?> RemoveRegistration(AppEntry e)
    {
        try
        {
            Directory.CreateDirectory(BackupRoot);
            string dir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-entry");
            Directory.CreateDirectory(dir);
            string path = e.RegistryPath;
            await ExportAsync(path, Path.Combine(dir, "uninstall.reg"));
            DeleteKey(path);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    static async Task<int> RunHiddenAsync(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        using var p = Process.Start(psi);
        if (p == null) return -1;
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    // Kaldırma geçmişi: ne zaman neyin kaldırıldığını yerelde tut (yalnız ad, sürüm, yöntem).
    public static void LogHistory(string name, string version, string how, string outcome)
    {
        try
        {
            string dir = Path.GetDirectoryName(BackupRoot)!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "history.tsv"), $"{DateTime.Now:s}\t{name}\t{version}\t{how}\t{outcome}\n");
        }
        catch { }
    }

    static async Task ExportAsync(string path, string file)
    {
        // Değer için üst anahtarın tamamını dışa aktarmak yeterli geri dönüş sağlar.
        var psi = new ProcessStartInfo("reg.exe", $"export \"{path}\" \"{file}\" /y") { CreateNoWindow = true, UseShellExecute = false };
        using var p = Process.Start(psi);
        if (p != null) await p.WaitForExitAsync();
        if (!File.Exists(file)) throw new IOException("Backup failed, nothing was deleted");
    }

    static (RegistryKey baseKey, string sub) Open(string path)
    {
        int i = path.IndexOf('\\');
        string h = path[..i];
        string sub = path[(i + 1)..];
        var hive = h == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        return (RegistryKey.OpenBaseKey(hive, RegistryView.Registry64), sub);
    }

    static void DeleteKey(string path)
    {
        var (bk, sub) = Open(path);
        using (bk) bk.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
    }

    static void DeleteValue(string path, string value)
    {
        var (bk, sub) = Open(path);
        using (bk)
        {
            using var k = bk.OpenSubKey(sub, writable: true);
            k?.DeleteValue(value, throwOnMissingValue: false);
        }
    }
}
