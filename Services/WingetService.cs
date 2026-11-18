using System.Diagnostics;
using System.Text;

namespace Kaldir.Services;

public class UpgradeItem
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Current { get; set; } = "";
    public string Available { get; set; } = "";
}

// "Update outdated software": winget'in yükseltme listesini okuyup seçilenleri sessizce günceller.
public static class WingetService
{
    // winget bir App Execution Alias'tır (%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe). Purge'ün devraldığı PATH'te
    // bu klasör olmayabilir (ör. başka bir süreçten başlatıldıysa); bu yüzden önce tam yolu dene.
    // Takma ad bazı ortamlarda "path not found" ile başlamıyor (2026-11-18'de görüldü); paketin kendi winget.exe'si çalışıyor.
    static string? _path;
    static string WingetPath() => _path ??= ResolveWinget();

    static string ResolveWinget()
    {
        try
        {
            var pm = new Windows.Management.Deployment.PackageManager();
            var pkg = pm.FindPackagesForUser("", "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe")
                        .OrderByDescending(p => p.Id.Version.Major).ThenByDescending(p => p.Id.Version.Minor).FirstOrDefault();
            string? exe = pkg != null ? Path.Combine(pkg.InstalledLocation.Path, "winget.exe") : null;
            if (exe != null && File.Exists(exe)) return exe;
        }
        catch { }
        string alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(alias) ? alias : "winget";
    }

    public static async Task<(int code, string output)> RunAsync(string args, int timeoutMs = 180000)
    {
        try
        {
            var psi = new ProcessStartInfo(WingetPath(), args)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            if (p == null) return (-1, "winget did not start");
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeoutMs);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return (-2, "winget timed out"); }
            return (p.ExitCode, await outTask + await errTask);
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, "winget is not installed"); }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    public static async Task<(List<UpgradeItem> items, string? error)> ListUpgradesAsync()
    {
        var (code, output) = await RunAsync("upgrade --accept-source-agreements --disable-interactivity");
        if (code == -1 || code == -2) return (new(), output);
        return (Parse(output), null);
    }

    // Sütun konumlarını başlık satırından alan basit tablo ayrıştırıcı.
    public static List<UpgradeItem> Parse(string output)
    {
        var res = new List<UpgradeItem>();
        var lines = output.Replace("\r", "").Split('\n');
        int h = Array.FindIndex(lines, l => l.TrimStart().StartsWith("Name") && l.Contains("Id") && l.Contains("Version"));
        if (h < 0) return res;
        string head = lines[h];
        int iName = head.IndexOf("Name"), iId = head.IndexOf("Id"), iVer = head.IndexOf("Version");
        int iAv = head.IndexOf("Available");
        int iSrc = head.IndexOf("Source");
        if (iId < 0 || iVer < 0 || iAv < 0) return res;
        string Cut(string l, int a, int b) { if (a >= l.Length) return ""; b = b < 0 || b > l.Length ? l.Length : b; return l[a..b].Trim(); }

        for (int i = h + 2; i < lines.Length; i++)
        {
            string l = lines[i];
            if (string.IsNullOrWhiteSpace(l)) break;
            if (l.Contains("upgrades available") || l.StartsWith("The following") || l.TrimStart().StartsWith("-")) break;
            var it = new UpgradeItem
            {
                Name = Cut(l, iName, iId),
                Id = Cut(l, iId, iVer),
                Current = Cut(l, iVer, iAv),
                Available = Cut(l, iAv, iSrc),
            };
            if (it.Id.Length > 0 && it.Name.Length > 0) res.Add(it);
        }
        return res;
    }

    public static async Task<(bool ok, string msg)> UpgradeAsync(string id)
    {
        var (code, output) = await RunAsync($"upgrade --id \"{id}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", 900000);
        return (code == 0, code == 0 ? "" : $"exit {code}");
    }
}
