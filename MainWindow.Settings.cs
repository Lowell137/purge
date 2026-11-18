using System.Diagnostics;
using Kaldir.Models;
using Kaldir.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kaldir;

// Settings sayfası: tema, pencere malzemesi (Mica / Mica Alt / Acrylic / Solid), kaldırma varsayılanları, kısayol, veri klasörleri.
public sealed partial class MainWindow
{
    bool _settingsReady;
    static AppSettings S => AppSettings.Current;

    void InitSettings(bool admin)
    {
        Select(ThemeBox, S.Theme);
        Select(BackdropBox, S.Backdrop);
        SetSilent.IsOn = S.SilentUninstall;
        SetLeftovers.IsOn = S.ScanLeftoversAfter;
        SetRestore.IsOn = S.RestorePoint && admin;
        SetRestore.IsEnabled = admin;
        RestoreHint.Text = admin ? "Windows makes a restore point before each uninstall" : "Needs administrator rights: use Restart as administrator";
        SetHotKey.IsOn = S.HotKeyEnabled;

        // Programs sayfasındaki onay kutuları varsayılanlarını buradan alır; oradaki değişiklik de kaydedilir.
        SilentChk.IsChecked = S.SilentUninstall;
        LeftoverChk.IsChecked = S.ScanLeftoversAfter;
        RestoreChk.IsChecked = S.RestorePoint && admin;
        SilentChk.Click += (_, _) => { S.SilentUninstall = SilentChk.IsChecked == true; SetSilent.IsOn = S.SilentUninstall; S.Save(); };
        LeftoverChk.Click += (_, _) => { S.ScanLeftoversAfter = LeftoverChk.IsChecked == true; SetLeftovers.IsOn = S.ScanLeftoversAfter; S.Save(); };
        RestoreChk.Click += (_, _) => { S.RestorePoint = RestoreChk.IsChecked == true; SetRestore.IsOn = S.RestorePoint; S.Save(); };

        string ver = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        AboutText.Text = $"Version {ver} · uninstaller with silent removal, leftover scan and system tools";

        ApplyTheme(S.Theme);
        ApplyBackdrop(S.Backdrop);
        _settingsReady = true;
    }

    static void Select(ComboBox box, string tag)
    {
        foreach (var o in box.Items)
            if (o is ComboBoxItem it && (string)it.Tag == tag) { box.SelectedItem = it; return; }
        box.SelectedIndex = 0;
    }

    void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady || ThemeBox.SelectedItem is not ComboBoxItem it) return;
        S.Theme = (string)it.Tag; S.Save(); ApplyTheme(S.Theme);
    }

    void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady || BackdropBox.SelectedItem is not ComboBoxItem it) return;
        S.Backdrop = (string)it.Tag; S.Save(); ApplyBackdrop(S.Backdrop);
    }

    void ApplyTheme(string theme)
    {
        Root.RequestedTheme = theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        // Başlık çubuğu düğmeleri (küçült/büyüt/kapat) temayla uyumlu kalsın
        try
        {
            AppWindow.TitleBar.PreferredTheme = theme switch
            {
                "Light" => Microsoft.UI.Windowing.TitleBarTheme.Light,
                "Dark" => Microsoft.UI.Windowing.TitleBarTheme.Dark,
                _ => Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode,
            };
        }
        catch { }
        ApplyBackdrop(S.Backdrop);   // Solid zemini temaya göre yeniden boya
    }

    void ApplyBackdrop(string kind)
    {
        SystemBackdrop = kind switch
        {
            "MicaAlt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            "Acrylic" => new DesktopAcrylicBackdrop(),
            "Solid" => null,
            _ => new MicaBackdrop { Kind = MicaKind.Base },
        };
        Root.Background = kind == "Solid"
            ? (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    void OnSettingToggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        if (sender == SetSilent) { S.SilentUninstall = SetSilent.IsOn; SilentChk.IsChecked = SetSilent.IsOn; }
        else if (sender == SetLeftovers) { S.ScanLeftoversAfter = SetLeftovers.IsOn; LeftoverChk.IsChecked = SetLeftovers.IsOn; }
        else if (sender == SetRestore) { S.RestorePoint = SetRestore.IsOn; RestoreChk.IsChecked = SetRestore.IsOn; }
        else if (sender == SetHotKey) { S.HotKeyEnabled = SetHotKey.IsOn; ApplyHotKey(); }
        S.Save();
    }

    // Kısayol açık/kapalı: kapatınca kaydı sil, açınca yeniden dene (başka program tutuyorsa K/J'ye düşer).
    void ApplyHotKey()
    {
        HotKeyHost.Unregister(Hwnd);
        _hotKey = S.HotKeyEnabled ? HotKeyHost.Register(Hwnd, () => DispatcherQueue.TryEnqueue(async () => await EasyUninstallFromForegroundAsync())) : null;
        HotKeyHint.Text = !S.HotKeyEnabled ? "Off" : _hotKey != null ? $"{_hotKey} on any app window opens the uninstall prompt for it" : "Every hotkey we try is taken by another program";
        var easy = _tiles.FirstOrDefault(t => t.Id == "easy");
        if (easy != null)
        {
            easy.Sub = !S.HotKeyEnabled ? "Hotkey off · pick a window" : _hotKey != null ? $"{_hotKey} on any app window" : "Pick a window (hotkey unavailable)";
            ToolsGrid.ItemsSource = null; ToolsGrid.ItemsSource = _tiles;
        }
    }

    void OnOpenData(object sender, RoutedEventArgs e)
    {
        string dir = AppSettings.DataDir;
        string target = (sender as Button)?.Tag as string switch
        {
            "history" => Path.Combine(dir, "history.tsv"),
            "backups" => LeftoverCleaner.BackupRoot,
            "monitor" => Path.Combine(dir, "Monitor"),
            _ => dir,
        };
        try
        {
            if (target.EndsWith(".tsv") && !File.Exists(target)) { StatusText.Text = "No uninstall history yet"; return; }
            if (!target.EndsWith(".tsv")) Directory.CreateDirectory(target);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError("Could not open it", ex.Message); }
    }
}
