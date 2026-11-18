using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Kaldir.Models;

namespace Kaldir.Services;

public enum UninstallResult { Done, StillPresent, Failed, NoCommand, NeedsUi }
public enum InstallerKind { Unknown, Msi, Inno, Nsis, Squirrel, Burn, Winget, Chromium }

public record PlannedCommand(string Command, bool Silent, InstallerKind Kind, string How);

public static class Uninstaller
{
    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // ---- Kaldırıcı türü tespiti ----

    // Komutun çalıştırılabilir yolunu ayıkla: tırnaklıysa tırnak içi, değilse ilk ".exe"ye kadar.
    public static (string exe, string args) Split(string cmd)
    {
        cmd = cmd.Trim();
        if (cmd.StartsWith('"'))
        {
            int e = cmd.IndexOf('"', 1);
            if (e > 0) return (cmd[1..e], cmd[(e + 1)..].Trim());
        }
        int x = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (x > 0) return (cmd[..(x + 4)], cmd[(x + 4)..].Trim());
        int sp = cmd.IndexOf(' ');
        return sp > 0 ? (cmd[..sp], cmd[(sp + 1)..].Trim()) : (cmd, "");
    }

    static bool FileHas(string path, params string[] needles)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length > 60_000_000) return false;
            byte[] data = File.ReadAllBytes(path);
            foreach (string n in needles)
                if (data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(n)) >= 0) return true;
        }
        catch { }
        return false;
    }

    public static InstallerKind Detect(AppEntry e, string exe, string args)
    {
        string file = Path.GetFileName(exe);
        if (file.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
            return InstallerKind.Msi;
        if (file.Equals("winget", StringComparison.OrdinalIgnoreCase) || file.Equals("winget.exe", StringComparison.OrdinalIgnoreCase))
            return InstallerKind.Winget;
        if (file.Equals("setup.exe", StringComparison.OrdinalIgnoreCase) && args.Contains("--uninstall", StringComparison.OrdinalIgnoreCase)
            && exe.Contains(@"\Installer\", StringComparison.OrdinalIgnoreCase))
            return InstallerKind.Chromium;
        if (e.RegistryKeyName.EndsWith("_is1", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(file, @"^unins\d*\.exe$", RegexOptions.IgnoreCase))
            return InstallerKind.Inno;
        if (file.Equals("Update.exe", StringComparison.OrdinalIgnoreCase) && args.Contains("--uninstall", StringComparison.OrdinalIgnoreCase))
            return InstallerKind.Squirrel;
        if (FileHas(exe, "Nullsoft")) return InstallerKind.Nsis;
        if (FileHas(exe, "Inno Setup")) return InstallerKind.Inno;
        if (FileHas(exe, ".wixburn")) return InstallerKind.Burn;
        return InstallerKind.Unknown;
    }

    // ---- Komut planı ----

    public static PlannedCommand Plan(AppEntry e, bool wantSilent)
    {
        string normal = e.UninstallString.Length > 0 ? e.UninstallString : e.QuietUninstallString;
        if (normal.Length == 0) return new("", false, InstallerKind.Unknown, "no command");

        // MSI kayıtlarında "MsiExec.exe /I{GUID}" yazar: /I bakım ekranı açar, kaldırmak için /X gerekir.
        string baseCmd = Regex.IsMatch(normal, @"msiexec(\.exe)?""?\s+/I", RegexOptions.IgnoreCase)
            ? Regex.Replace(normal, @"(msiexec(\.exe)?""?\s+)/I", "$1/X", RegexOptions.IgnoreCase)
            : normal;

        if (!wantSilent)
        {
            var (ex0, ar0) = Split(baseCmd);
            return new(baseCmd, false, Detect(e, ex0, ar0), "normal");
        }

        // Kayıtta hazır sessiz komut varsa en güvenilir olan o.
        if (e.QuietUninstallString.Length > 0)
        {
            var (qx, qa) = Split(e.QuietUninstallString);
            return new(e.QuietUninstallString, true, Detect(e, qx, qa), "registry quiet command");
        }

        var (exe, args) = Split(baseCmd);
        var kind = Detect(e, exe, args);
        string q(string x) => x.Contains(' ') && !x.StartsWith('"') ? $"\"{x}\"" : x;

        switch (kind)
        {
            case InstallerKind.Msi:
            {
                string c = Regex.Replace(baseCmd, @"\s+/q[nbrf+-]*\b", "", RegexOptions.IgnoreCase);
                c = Regex.Replace(c, @"\s+/norestart\b", "", RegexOptions.IgnoreCase);
                return new(c + " /qn /norestart", true, kind, "MSI /qn");
            }
            case InstallerKind.Inno:
                return new($"{q(exe)} {args} /VERYSILENT /SUPPRESSMSGBOXES /NORESTART".Replace("  ", " "), true, kind, "Inno /VERYSILENT");
            case InstallerKind.Nsis:
                return new($"{q(exe)} {args} /S".Replace("  ", " "), true, kind, "NSIS /S");
            case InstallerKind.Squirrel:
                return new($"{q(exe)} --uninstall -s", true, kind, "Squirrel -s");
            case InstallerKind.Winget:
                return new(baseCmd + " --silent --disable-interactivity", true, kind, "winget --silent");
            case InstallerKind.Chromium:
                return new(baseCmd + " --force-uninstall", true, kind, "Chromium --force-uninstall");
            case InstallerKind.Burn:
            {
                string a = args.Contains("/uninstall", StringComparison.OrdinalIgnoreCase) ? args : (args + " /uninstall").Trim();
                return new($"{q(exe)} {a} /quiet /norestart", true, kind, "WiX Burn /quiet");
            }
            default:
                return new(baseCmd, false, kind, "no known silent switch");
        }
    }

    // ---- Çalıştırma ----

    // Çalışan süreçler kaldırmayı bozar (kilitli dosyalar): kurulum klasöründen çalışanları kapat. Sistem klasörlerine dokunma.
    public static int KillProcessesIn(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || LeftoverScanner.IsProtectedRoot(folder) || !LeftoverScanner.IsUnderInstallRoot(folder)) return 0;
        string root = folder.TrimEnd('\\') + "\\";
        int n = 0, me = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == me) continue;
                string? path = p.MainModule?.FileName;
                if (path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { p.Kill(true); n++; }
            }
            catch { }
            finally { p.Dispose(); }
        }
        return n;
    }

    public static async Task<(UninstallResult res, string msg, bool silent)> RunAsync(AppEntry e, bool wantSilent, bool allowUi = false)
    {
        if (e.IsStore)
        {
            string? err = await StoreScanner.RemoveAsync(e.PackageFullName);
            return err == null ? (UninstallResult.Done, "", true) : (UninstallResult.Failed, err, true);
        }

        var plan = Plan(e, wantSilent);
        if (plan.Command.Length == 0) return (UninstallResult.NoCommand, "No uninstall command", false);
        // Sessiz yolu olmayan kaldırıcı pencere açar: politika "pencere yok" ise hiç çalıştırma, çağıran karar versin.
        if (!plan.Silent && !allowUi) return (UninstallResult.NeedsUi, "No silent mode known", false);

        try
        {
            if (plan.Silent) KillProcessesIn(e.InstallLocation);

            // cmd.exe araya girmez (konsol penceresi çıkmasın): komutu doğrudan, gizli pencereyle başlat.
            var (exe, args) = Split(plan.Command);
            exe = Environment.ExpandEnvironmentVariables(exe);
            args = Environment.ExpandEnvironmentVariables(args);
            bool elevate = plan.Silent && e.RegistryHive == "HKLM" && !IsAdmin();   // makine geneli kurulum: UAC bir kez, ama kaldırıcı penceresi yok

            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = elevate,
                CreateNoWindow = true,
                WindowStyle = plan.Silent ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            };
            if (elevate) psi.Verb = "runas";

            Process? proc;
            try { proc = Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception)
            {
                // .bat/.cmd gibi doğrudan başlamayanlar için gizli cmd köprüsü.
                psi = new ProcessStartInfo("cmd.exe", $"/s /c \"{plan.Command}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                proc = Process.Start(psi);
            }
            using var _p = proc;
            if (proc == null) return (UninstallResult.Failed, "Process did not start", plan.Silent);
            await proc.WaitForExitAsync();

            // NSIS, Squirrel gibi kaldırıcılar kendini geçici klasöre kopyalayıp hemen çıkar; kaydın silinmesini bekle.
            int polls = plan.Silent && plan.Kind is InstallerKind.Nsis or InstallerKind.Squirrel ? 180 : 20;
            for (int i = 0; i < polls; i++)
            {
                if (!ProgramScanner.StillRegistered(e)) return (UninstallResult.Done, "", plan.Silent);
                await Task.Delay(500);
            }
            return (UninstallResult.StillPresent, proc.ExitCode == 0 ? "Entry still registered" : $"Exit code {proc.ExitCode}", plan.Silent);
        }
        catch (Exception ex) { return (UninstallResult.Failed, ex.Message, plan.Silent); }
    }

    // Yalnız yönetici iken çalışır; Windows 24 saatte bir geri yükleme noktasına izin verir.
    public static async Task<bool> CreateRestorePointAsync(string description)
    {
        if (!IsAdmin()) return false;
        try
        {
            string safe = description.Replace("'", "");
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description 'Purge: {safe}' -RestorePointType APPLICATION_UNINSTALL -ErrorAction Stop\"")
            { CreateNoWindow = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
