using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using LegoToyPad.App.ViewModels;
using LegoToyPad.App.Views;
using LegoToyPad.Core;
using LegoToyPad.Core.Library;
using LegoToyPad.Core.Transport;

namespace LegoToyPad.App.Tests;

/// <summary>
/// Drives the real window against the simulated ToyPad. Set SCREENSHOT_DIR to also save
/// a PNG of each step (light and dark) for reviewing the UI.
/// </summary>
public sealed class UiFlowTests : IDisposable
{
    private readonly string _libraryDir = Path.Combine(Path.GetTempPath(), $"toypad-ui-{Guid.NewGuid():N}");
    private static readonly string? ScreenshotDir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");

    private sealed class AutoConfirmHost : IUiHost
    {
        public List<string> Confirmations { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive)
        {
            Confirmations.Add(title);
            return Task.FromResult(true);
        }

        public Task CopyToClipboardAsync(string text) => Task.CompletedTask;
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task FullFlow_ReadSaveWriteReset(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        var pad = SimulatedToyPad.CreateDemo();
        var service = new ToyPadService(pad, startupDelay: TimeSpan.Zero);
        var host = new AutoConfirmHost();
        var window = new MainWindow { Width = 1280, Height = 880 };
        var vm = new MainViewModel(service, new LibraryStore(Path.Combine(_libraryDir, "library.json")), host);
        window.DataContext = vm;
        window.Show();

        Capture(window, theme, "01-connect");

        await vm.ConnectCommand.ExecuteAsync(null);
        await WaitUntil(() => service.ActiveTags.Count == 3 && service.ActiveTags.All(t => t.State != IdentifyState.Pending));
        Assert.True(vm.IsConnected);
        Assert.Equal("Batman", Assert.Single(vm.Portal.Left.Tags).Name);
        Assert.Equal("Police Car", Assert.Single(vm.Portal.Right.Tags).Name);
        Capture(window, theme, "02-portal");

        await vm.Portal.ReadAllCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Portal.Results.Count);
        var batman = vm.Portal.Results.Single(r => r.Heading == "Batman");
        Assert.True(batman.PasswordMatches);
        batman.SaveToLibraryCommand.Execute(null);
        Assert.Single(vm.LibraryPage.Entries);
        Capture(window, theme, "03-read-results");

        batman.WriteCopyCommand.Execute(null);
        Assert.Same(vm.Write, vm.SelectedPage);
        Assert.Equal("Batman", vm.Write.Selected?.Name);
        await vm.Write.CheckTagCommand.ExecuteAsync(null);
        Assert.True(vm.Write.IsTagReady, vm.Write.CheckStatus?.Detail);
        Capture(window, theme, "04-write-ready");

        await vm.Write.WriteCommand.ExecuteAsync(null);
        Assert.Equal(Severity.Success, vm.Write.WriteStatus?.Severity);
        await WaitUntil(() => vm.Portal.Center.Tags.FirstOrDefault()?.Name == "Batman");
        Capture(window, theme, "05-write-done");

        vm.Navigate(vm.Reset);
        await vm.Reset.PreviewCommand.ExecuteAsync(null);
        Assert.True(vm.Reset.HasPreview);
        Capture(window, theme, "06-reset-preview");
        await vm.Reset.ResetCommand.ExecuteAsync(null);
        Assert.Equal(Severity.Success, vm.Reset.Status?.Severity);
        Assert.Equal(["Write tag?", "Reset tag?"], host.Confirmations);
        Capture(window, theme, "07-reset-done");

        vm.Navigate(vm.LibraryPage);
        Capture(window, theme, "08-library");

        vm.Navigate(vm.Activity);
        Capture(window, theme, "09-activity");

        vm.Zoom = 1.4;
        vm.Navigate(vm.Portal);
        Capture(window, theme, "10-portal-large-text");

        var dialog = new ConfirmDialog("Reset tag?", "This erases pages 0x24–0x26 on the center tag, so it will no longer be recognised as a character or vehicle. This can't be undone.", "Reset tag", destructive: true);
        dialog.Show();
        Capture(dialog, theme, "11-confirm-dialog");
        dialog.Close();

        service.Disconnect();
        window.Close();
    }

    [AvaloniaFact]
    public async Task UnplugShowsConnectPromptWithError()
    {
        var pad = SimulatedToyPad.CreateDemo();
        var service = new ToyPadService(pad, startupDelay: TimeSpan.Zero);
        var vm = new MainViewModel(service, new LibraryStore(Path.Combine(_libraryDir, "library.json")), new AutoConfirmHost());
        await vm.ConnectCommand.ExecuteAsync(null);

        pad.Unplug();
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsConnected);
        Assert.True(vm.ShowConnectPrompt);
        Assert.Equal("The ToyPad was disconnected", vm.ConnectionError?.Title);
    }

    [Fact]
    public void SettingsPersistenceAndWorldFilter()
    {
        var settingsPath = Path.Combine(_libraryDir, "settings.json");
        var settingsStore = new SettingsStore(settingsPath);
        var pad = SimulatedToyPad.CreateDemo();
        var service = new ToyPadService(pad, startupDelay: TimeSpan.Zero);
        var library = new LibraryStore(Path.Combine(_libraryDir, "library.json"));
        var vm = new MainViewModel(service, library, new AutoConfirmHost(), settingsStore);

        vm.ZoomInCommand.Execute(null);
        Assert.Equal(1.1, vm.Zoom);
        vm.SetThemeCommand.Execute("Dark");

        var vm2 = new MainViewModel(service, library, new AutoConfirmHost(), settingsStore);
        Assert.Equal(1.1, vm2.Zoom);

        Assert.Contains(WriteViewModel.AllWorldsOption, vm2.Write.Worlds);
        Assert.Contains("DC Comics", vm2.Write.Worlds);
        vm2.Write.SelectedWorld = "DC Comics";
        Assert.All(vm2.Write.Options, o => Assert.Equal("DC Comics", o.Detail));

        library.Add(new TagIdentity(TokenKind.Character, 1, "Batman", "DC Comics"), null);
        library.Add(new TagIdentity(TokenKind.Character, 2, "Gandalf", "Lord of the Rings"), null);
        vm2.LibraryPage.Refresh();
        Assert.Equal(2, vm2.LibraryPage.Entries.Count);

        vm2.LibraryPage.SearchText = "Batman";
        Assert.Single(vm2.LibraryPage.Entries);
        Assert.Equal("Batman", vm2.LibraryPage.Entries[0].Name);
        Assert.False(vm2.LibraryPage.HasNoMatches);

        vm2.LibraryPage.SearchText = "NonExistentTag";
        Assert.Empty(vm2.LibraryPage.Entries);
        Assert.True(vm2.LibraryPage.HasNoMatches);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Condition not reached.");
    }

    private static void Capture(Avalonia.Controls.TopLevel topLevel, string theme, string name)
    {
        Dispatcher.UIThread.RunJobs();
        if (ScreenshotDir is null) return;

        Directory.CreateDirectory(ScreenshotDir);
        using var frame = topLevel.CaptureRenderedFrame();
        frame?.Save(Path.Combine(ScreenshotDir, $"{name}-{theme}.png"), PngBitmapEncoderOptions.Default);
    }

    public void Dispose()
    {
        if (Directory.Exists(_libraryDir)) Directory.Delete(_libraryDir, recursive: true);
    }
}
