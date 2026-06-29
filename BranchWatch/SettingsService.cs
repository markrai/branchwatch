using System.IO;
using System.Text.Json;

namespace BranchWatch;

public sealed class AppSettings
{
    public string? WatchedRepositoryPath { get; set; }
    public bool OverlayVisible { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public double OverlayFontSize { get; set; } = 42;
    public double OverlayOpacity { get; set; } = 0.85;
    public string OverlayPositionPreset { get; set; } = "top-right";
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsPath { get; }

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        SettingsPath = Path.Combine(appData, "BranchWatch", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
