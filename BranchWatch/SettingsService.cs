using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace BranchWatch;

public enum RepositoryWatchMode
{
    PinnedRepo,
    WorkspaceRepo
}

public sealed class AppSettings
{
    public string? WatchedRepositoryPath { get; set; }
    public string? WorkspaceRootPath { get; set; }
    public RepositoryWatchMode WatchMode { get; set; } = RepositoryWatchMode.PinnedRepo;
    public bool WorkspaceFileActivityEnabled { get; set; } = true;
    public int WorkspaceDiscoveryMaxDepth { get; set; } = 2;
    public bool OverlayVisible { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public double OverlayFontSize { get; set; } = 42;
    public double OverlayScale { get; set; } = 1.0;
    public double OverlayOpacity { get; set; } = 0.85;
    public double OverlayForegroundOpacity { get; set; } = 1.0;
    public string OverlayPositionPreset { get; set; } = "top-right";
    public bool OverlayShowOutline { get; set; } = true;
    public bool OverlayShowRepositoryName { get; set; }
    public bool OverlayRepositoryFullPath { get; set; }
    public bool ShowWorkspaceActivityReason { get; set; }
    public string OverlayFontColor { get; set; } = "#FFFFFF";
}

public static class OverlaySettings
{
    public const double MinOpacity = 0.2;
    public const double MaxOpacity = 0.85;
    public const double MinForegroundOpacity = 0.2;
    public const double MaxForegroundOpacity = 1.0;
    public const double BaseFontSize = 42;
    public const double BasePaddingHorizontal = 18;
    public const double BasePaddingVertical = 8;
    public const double MinScale = 0.5;
    public const double MaxScale = 1.0;

    public static double ClampOpacity(double opacity) => Math.Clamp(opacity, MinOpacity, MaxOpacity);

    public static double ClampForegroundOpacity(double opacity) =>
        Math.Clamp(opacity, MinForegroundOpacity, MaxForegroundOpacity);

    public static double ClampScale(double scale) => Math.Clamp(scale, MinScale, MaxScale);

    public static MediaColor ParseFontColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return MediaColors.White;
        }

        try
        {
            var normalized = hex.Trim();
            if (!normalized.StartsWith('#'))
            {
                normalized = "#" + normalized;
            }

            return (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(normalized)!;
        }
        catch
        {
            return MediaColors.White;
        }
    }

    public static string ToHexColor(MediaColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new RepositoryWatchModeJsonConverter() }
    };

    public string SettingsPath { get; }

    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BranchWatch",
            "settings.json"))
    {
    }

    internal SettingsService(string settingsPath)
    {
        SettingsPath = settingsPath;
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
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.OverlayOpacity = OverlaySettings.ClampOpacity(settings.OverlayOpacity);
            settings.OverlayForegroundOpacity = OverlaySettings.ClampForegroundOpacity(settings.OverlayForegroundOpacity);
            settings.OverlayScale = OverlaySettings.ClampScale(settings.OverlayScale);
            return settings;
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

internal sealed class RepositoryWatchModeJsonConverter : JsonConverter<RepositoryWatchMode>
{
    public override RepositoryWatchMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return string.Equals(reader.GetString(), nameof(RepositoryWatchMode.WorkspaceRepo), StringComparison.OrdinalIgnoreCase)
                ? RepositoryWatchMode.WorkspaceRepo
                : RepositoryWatchMode.PinnedRepo;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value))
        {
            return value == (int)RepositoryWatchMode.WorkspaceRepo
                ? RepositoryWatchMode.WorkspaceRepo
                : RepositoryWatchMode.PinnedRepo;
        }

        return RepositoryWatchMode.PinnedRepo;
    }

    public override void Write(
        Utf8JsonWriter writer,
        RepositoryWatchMode value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
