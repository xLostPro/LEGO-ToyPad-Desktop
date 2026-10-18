using System.Text.Json;
using System.Text.Json.Serialization;

namespace LegoToyPad.Core.Library;

public sealed record LibraryEntry
{
    public required string LibraryId { get; init; }
    public required TokenKind Type { get; init; }
    public required uint Id { get; init; }
    public required string Name { get; init; }
    public string? World { get; init; }
    public int? Rebuild { get; init; }
    public string? SourceUid { get; init; }
    public DateTimeOffset SavedAt { get; init; }
}

/// <summary>
/// JSON file of saved tag identities so a read result can be written to other blank
/// tags later without the original tag present.
/// </summary>
public sealed class LibraryStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<LibraryEntry> _items;

    public LibraryStore(string filePath)
    {
        _filePath = filePath;
        _items = Load();
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "LegoToyPad",
        "library.json");

    public IReadOnlyList<LibraryEntry> Items
    {
        get { lock (_lock) return _items.ToList(); }
    }

    /// <summary>Saves an identity; returns the existing entry if the same type+id is already saved.</summary>
    public LibraryEntry Add(TagIdentity identity, string? sourceUid)
    {
        lock (_lock)
        {
            var existing = _items.FirstOrDefault(i => i.Type == identity.Kind && i.Id == identity.Id);
            if (existing is not null) return existing;

            var now = DateTimeOffset.Now;
            var entry = new LibraryEntry
            {
                LibraryId = $"{identity.Kind.ToString().ToLowerInvariant()}-{identity.Id}-{now.ToUnixTimeMilliseconds()}",
                Type = identity.Kind,
                Id = identity.Id,
                Name = identity.Name,
                World = identity.World,
                Rebuild = identity.Rebuild,
                SourceUid = sourceUid,
                SavedAt = now
            };
            _items.Insert(0, entry);
            Persist();
            return entry;
        }
    }

    public void Remove(string libraryId)
    {
        lock (_lock)
        {
            _items = _items.Where(i => i.LibraryId != libraryId).ToList();
            Persist();
        }
    }

    private List<LibraryEntry> Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(_filePath), LibraryJsonContext.Default.ListLibraryEntry) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_items, LibraryJsonContext.Default.ListLibraryEntry));
        File.Move(tempPath, _filePath, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<LibraryEntry>))]
internal sealed partial class LibraryJsonContext : JsonSerializerContext;
