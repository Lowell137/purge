using System.ComponentModel;

namespace Kaldir.Models;

public class AppEntry : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string UninstallString { get; set; } = "";
    public string QuietUninstallString { get; set; } = "";
    public string IconPath { get; set; } = "";

    Microsoft.UI.Xaml.Media.ImageSource? _icon;
    public Microsoft.UI.Xaml.Media.ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new(nameof(Icon)));
            PropertyChanged?.Invoke(this, new(nameof(GlyphVisibility)));
        }
    }
    public Microsoft.UI.Xaml.Visibility GlyphVisibility => _icon == null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    // HKLM\... / HKCU\... : Uninstall anahtarının tam yolu, 32 bit görünümü için "|32" son eki yok, hive+view ayrı tutulur.
    public string RegistryHive { get; set; } = "";   // "HKLM" | "HKCU"
    public bool Registry32 { get; set; }
    public string RegistryKeyName { get; set; } = "";
    public string PackageFullName { get; set; } = "";
    public bool IsStore => PackageFullName.Length > 0;
    public DateTime? InstallDate { get; set; }

    long _size;
    public long SizeBytes
    {
        get => _size;
        set { _size = value; PropertyChanged?.Invoke(this, new(nameof(SizeBytes))); PropertyChanged?.Invoke(this, new(nameof(SizeText))); }
    }

    public string SizeText => _size <= 0 ? "" : Fmt(_size);
    public string DateText => InstallDate?.ToString("yyyy-MM-dd") ?? "";
    public string SourceText => IsStore ? "Store" : "Desktop";
    public string SubText => string.Join(" · ", new[] { Publisher, Version }.Where(s => s.Length > 0));
    public string Glyph => IsStore ? "" : "";

    public string RegistryPath => IsStore ? "" : $@"{RegistryHive}\SOFTWARE\{(Registry32 ? @"WOW6432Node\" : "")}Microsoft\Windows\CurrentVersion\Uninstall\{RegistryKeyName}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string Fmt(long b)
    {
        string[] u = { "B", "KB", "MB", "GB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{v:0} {u[i]}" : $"{v:0.#} {u[i]}";
    }
}

public class LeftoverItem
{
    // Folder | File | Registry key | Registry value | Service | Scheduled task
    public string Kind { get; set; } = "";
    public string Path { get; set; } = "";
    public string ValueName { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsSelected { get; set; } = true;
    public string SizeText => SizeBytes <= 0 ? "" : AppEntry.Fmt(SizeBytes);
    public string Glyph => Kind switch { "Folder" => "", "File" => "", _ => "" };
}
