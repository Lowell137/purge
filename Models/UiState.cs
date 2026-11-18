using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace Kaldir.Models;

// Satır şablonları (DataTemplate) VisualState setter'larına kapalı; sütun genişlikleri buradan x:Bind ile okunur
// ve pencere genişliği değişince başlık satırıyla birlikte güncellenir.
public sealed class UiState : INotifyPropertyChanged
{
    public static UiState Current { get; } = new();

    GridLength _size = new(90), _date = new(100), _type = new(80), _loc = new(200);
    public GridLength LocCol { get => _loc; private set { _loc = value; Raise(nameof(LocCol)); } }
    public GridLength SizeCol { get => _size; private set { _size = value; Raise(nameof(SizeCol)); } }
    public GridLength DateCol { get => _date; private set { _date = value; Raise(nameof(DateCol)); } }
    public GridLength TypeCol { get => _type; private set { _type = value; Raise(nameof(TypeCol)); } }

    // Liste kartının genişliğine göre: geniş = hepsi, orta = Tarih/Tür gizli, dar = Boyut da gizli.
    public void ApplyWidth(double listWidth)
    {
        bool showDate = listWidth >= 640, showSize = listWidth >= 420;
        SizeCol = showSize ? new GridLength(90) : new GridLength(0);
        DateCol = showDate ? new GridLength(100) : new GridLength(0);
        TypeCol = showDate ? new GridLength(80) : new GridLength(0);
        LocCol = showDate ? new GridLength(200) : new GridLength(0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
