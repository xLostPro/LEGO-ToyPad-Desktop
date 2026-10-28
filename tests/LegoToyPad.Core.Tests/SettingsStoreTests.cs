using LegoToyPad.Core;

namespace LegoToyPad.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"toypad-settings-{Guid.NewGuid():N}", "settings.json");

    [Fact]
    public void SaveAndLoad_RoundTripsSettings()
    {
        var store = new SettingsStore(_path);
        var original = new UserSettings("Dark", 1.3);

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal(1.3, loaded.Zoom);
    }

    [Fact]
    public void Load_ClampsAndRoundsZoomValues()
    {
        var store = new SettingsStore(_path);

        store.Save(new UserSettings("Light", 3.5));
        Assert.Equal(2.0, store.Load().Zoom);

        store.Save(new UserSettings("Light", 0.3));
        Assert.Equal(0.8, store.Load().Zoom);

        store.Save(new UserSettings("Light", 1.26));
        Assert.Equal(1.3, store.Load().Zoom);
    }

    [Fact]
    public void Load_NormalizesInvalidTheme()
    {
        var store = new SettingsStore(_path);

        store.Save(new UserSettings("Blue", 1.0));
        Assert.Equal("System", store.Load().Theme);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{not valid json");

        var store = new SettingsStore(_path);
        var loaded = store.Load();

        Assert.Equal("System", loaded.Theme);
        Assert.Equal(1.0, loaded.Zoom);
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsDefaults()
    {
        var store = new SettingsStore(_path);
        var loaded = store.Load();

        Assert.Equal("System", loaded.Theme);
        Assert.Equal(1.0, loaded.Zoom);
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
