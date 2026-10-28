using LegoToyPad.Core.Library;

namespace LegoToyPad.Core.Tests;

public class LibraryStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"toypad-lib-{Guid.NewGuid():N}", "library.json");

    [Fact]
    public void AddPersistsAndDeduplicates()
    {
        var store = new LibraryStore(_path);
        var batman = new TagIdentity(TokenKind.Character, 1, "Batman", "DC Comics");

        var first = store.Add(batman, "04A1B2C3D4E580");
        var second = store.Add(batman, null);
        store.Add(new TagIdentity(TokenKind.Vehicle, 1000, "Police Car", Rebuild: 0), null);

        Assert.Same(first, second);
        var reloaded = new LibraryStore(_path).Items;
        Assert.Equal(2, reloaded.Count);
        Assert.Equal("Police Car", reloaded[0].Name);
        Assert.Equal(TokenKind.Character, reloaded[1].Type);

        store.Remove(first.LibraryId);
        Assert.Single(new LibraryStore(_path).Items);
    }

    [Fact]
    public void ContainsReturnsTrueForSavedIdentity()
    {
        var store = new LibraryStore(_path);
        var batman = new TagIdentity(TokenKind.Character, 1, "Batman", "DC Comics");
        var gandalf = new TagIdentity(TokenKind.Character, 2, "Gandalf", "Lord of the Rings");

        Assert.False(store.Contains(batman));
        store.Add(batman, null);
        Assert.True(store.Contains(batman));
        Assert.False(store.Contains(gandalf));
    }

    [Fact]
    public void CorruptFileStartsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{not json");
        Assert.Empty(new LibraryStore(_path).Items);
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
