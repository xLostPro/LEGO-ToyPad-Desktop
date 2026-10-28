using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace LegoToyPad.Core.Data;

public sealed record Character(uint Id, string Name, string World)
{
    public bool IsPlaceholder =>
        Name.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
        Name.Equals("Future Update", StringComparison.OrdinalIgnoreCase) ||
        Name.StartsWith("Test ", StringComparison.OrdinalIgnoreCase);
}

public sealed record Vehicle(uint Id, string Name, int Rebuild, int UpgradeMap)
{
    public bool IsPlaceholder =>
        Name.Equals("unknown", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Bundled character and vehicle/gadget lists (same data as the CLI's charactermap/tokenmap JSON).</summary>
public static class TokenDatabase
{
    public static IReadOnlyList<Character> Characters { get; } = Load("charactermap.json", DataJsonContext.Default.ListCharacter);

    public static IReadOnlyList<Vehicle> Vehicles { get; } = Load("tokenmap.json", DataJsonContext.Default.ListVehicle);

    public static Character? FindCharacter(uint id) => Characters.FirstOrDefault(c => c.Id == id);

    public static Vehicle? FindVehicle(uint id) => Vehicles.FirstOrDefault(v => v.Id == id);

    private static IReadOnlyList<T> Load<T>(string fileName, JsonTypeInfo<List<T>> typeInfo)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"LegoToyPad.Core.Data.{fileName}")
            ?? throw new InvalidOperationException($"Missing embedded resource {fileName}");
        return JsonSerializer.Deserialize(stream, typeInfo) ?? [];
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<Character>))]
[JsonSerializable(typeof(List<Vehicle>))]
internal sealed partial class DataJsonContext : JsonSerializerContext;
