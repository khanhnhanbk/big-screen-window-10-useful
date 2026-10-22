using System.Text.Json;

namespace WindowHelper;

public enum ZoneLayoutKind { Grid2x2, Grid3x2 }

public sealed class Settings
{
    public ZoneLayoutKind Layout { get; set; } = ZoneLayoutKind.Grid2x2;
    public bool FindMouseEnabled { get; set; } = true;

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowHelper");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // Corrupt file: fall back to defaults.
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not fatal; settings just won't persist.
        }
    }
}
