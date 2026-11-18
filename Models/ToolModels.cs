using System.ComponentModel;

namespace Kaldir.Models;

public class ToolTile
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Sub { get; set; } = "";
    public string Glyph { get; set; } = "";
}

// Onay kutulu liste diyaloğu için genel satır (güncellemeler, Bloatware, Windows güncellemeleri...).
// IsSelected bildirimli: "Select all" kutuları ekranda da işaretlesin.
public class CheckItem : INotifyPropertyChanged
{
    public string Label { get; set; } = "";
    public string Sub { get; set; } = "";
    bool _sel;
    public bool IsSelected { get => _sel; set { if (_sel == value) return; _sel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
    public object? Tag { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
}

// Easy Uninstall seçicisindeki açık pencere satırı.
public class WindowItem
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string ExeName => Path.GetFileName(ExePath);
    public Microsoft.UI.Xaml.Media.ImageSource? Icon { get; set; }
}
