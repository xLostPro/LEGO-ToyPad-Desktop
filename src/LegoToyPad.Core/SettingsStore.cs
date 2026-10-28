using System.Text.Json;
using System.Text.Json.Serialization;

namespace LegoToyPad.Core;

public sealed record UserSettings(string Theme = "System", double Zoom = 1.0);

public sealed class SettingsStore
{
    private readonly string _filePath;

    public SettingsStore(string filePath) => _filePath = filePath;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "LegoToyPad",
        "settings.json");

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new UserSettings();
            var settings = JsonSerializer.Deserialize(File.ReadAllText(_filePath), SettingsJsonContext.Default.UserSettings);
            if (settings is null) return new UserSettings();
            var zoom = Math.Clamp(Math.Round(settings.Zoom, 1), 0.8, 2.0);
            var theme = settings.Theme is "Light" or "Dark" ? settings.Theme : "System";
            return new UserSettings(theme, zoom);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.UserSettings));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore persistence failures on read-only filesystems.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UserSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
