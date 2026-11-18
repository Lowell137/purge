using Microsoft.UI.Xaml;

namespace Kaldir;

public partial class App : Application
{
    public static MainWindow? Main;

    public App()
    {
        // Arayüz dili: Windows Türkçe olsa bile WinUI'nin kendi metinleri (ipuçları, bağlam menüsü, seçiciler) İngilizce olsun.
        try { Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en-US"; } catch { }
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kaldir");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:s} {e.Exception}\n\n");
            }
            catch { }
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Main = new MainWindow();
        Main.Activate();
    }
}
