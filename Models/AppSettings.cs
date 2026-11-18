using System.Text.Json;

namespace Kaldir.Models;

// Kullanıcı ayarları: %LOCALAPPDATA%\Kaldir\settings.json. Dosya yoksa ya da bozuksa varsayılanlar.
public sealed class AppSettings
{
    public string Theme { get; set; } = "System";      // System | Light | Dark
    public string Backdrop { get; set; } = "Mica";     // Mica | MicaAlt | Acrylic | Solid
    public bool SilentUninstall { get; set; } = true;
    public bool ScanLeftoversAfter { get; set; } = true;
    public bool RestorePoint { get; set; } = false;
    public bool HotKeyEnabled { get; set; } = true;

    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kaldir");
    static string FilePath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Current { get; private set; } = Load();

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }
}
