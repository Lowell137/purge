using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Kaldir.Services;

public class UpdateItem
{
    public string Kb { get; set; } = "";
    public string Description { get; set; } = "";
    public string InstalledOn { get; set; } = "";
}

// "Windows Updates Uninstall": kurulu güncellemeleri listeler, seçilenleri wusa ile kaldırır.
public static class WindowsUpdateService
{
    public static async Task<List<UpdateItem>> ListAsync()
    {
        var res = new List<UpdateItem>();
        try
        {
            string ps = "Get-HotFix | ForEach-Object { [pscustomobject]@{ Kb = $_.HotFixID; Desc = $_.Description; On = $(if ($_.InstalledOn) { $_.InstalledOn.ToString('yyyy-MM-dd') } else { '' }) } } | ConvertTo-Json -Compress";
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{ps}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            if (p == null) return res;
            string json = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            json = json.Trim();
            if (json.Length == 0) return res;
            using var doc = JsonDocument.Parse(json);
            IEnumerable<JsonElement> els = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new[] { doc.RootElement.Clone() };
            foreach (var e in els)
                res.Add(new UpdateItem
                {
                    Kb = e.GetProperty("Kb").GetString() ?? "",
                    Description = e.GetProperty("Desc").GetString() ?? "",
                    InstalledOn = e.GetProperty("On").GetString() ?? "",
                });
        }
        catch { }
        return res.Where(u => u.Kb.Length > 0).OrderByDescending(u => u.InstalledOn).ToList();
    }

    // wusa yönetici ister; yönetici değilsek UAC tetiklenir, pencere gizli kalır.
    public static async Task<(bool ok, string msg)> UninstallAsync(string kb)
    {
        string num = new string(kb.Where(char.IsDigit).ToArray());
        if (num.Length == 0) return (false, "bad KB id");
        try
        {
            bool admin = Uninstaller.IsAdmin();
            var psi = new ProcessStartInfo("wusa.exe", $"/uninstall /kb:{num} /quiet /norestart")
            {
                UseShellExecute = !admin, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            };
            if (!admin) psi.Verb = "runas";
            using var p = Process.Start(psi);
            if (p == null) return (false, "wusa did not start");
            await p.WaitForExitAsync();
            return p.ExitCode switch
            {
                0 => (true, ""),
                3010 => (true, "removed, restart required"),
                _ => (false, $"wusa exit {p.ExitCode} (cumulative updates often cannot be removed this way)"),
            };
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
