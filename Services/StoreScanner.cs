using Kaldir.Models;
using Windows.Management.Deployment;

namespace Kaldir.Services;

public static class StoreScanner
{
    public static List<AppEntry> Scan()
    {
        var list = new List<AppEntry>();
        try
        {
            var pm = new PackageManager();
            foreach (var p in pm.FindPackagesForUser(""))
            {
                try
                {
                    if (p.IsFramework || p.IsResourcePackage || p.IsBundle) continue;
                    if (p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System) continue;
                    string name;
                    try { name = p.DisplayName; } catch { name = ""; }
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:")) name = p.Id.Name;
                    string pub;
                    try { pub = p.PublisherDisplayName; } catch { pub = ""; }
                    var v = p.Id.Version;
                    string loc = "";
                    try { loc = p.InstalledPath; } catch { }
                    list.Add(new AppEntry
                    {
                        Name = name,
                        Publisher = pub,
                        Version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                        PackageFullName = p.Id.FullName,
                        InstallLocation = loc,
                        InstallDate = p.InstalledDate.UtcDateTime.ToLocalTime(),
                    });
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    public static async Task<string?> RemoveAsync(string fullName)
    {
        try
        {
            var r = await new PackageManager().RemovePackageAsync(fullName, RemovalOptions.None);
            return r.ExtendedErrorCode == null ? null : r.ErrorText;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
