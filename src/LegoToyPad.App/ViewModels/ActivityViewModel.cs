using CommunityToolkit.Mvvm.Input;

namespace LegoToyPad.App.ViewModels;

/// <summary>The step-by-step device log (same output the CLI printed).</summary>
public sealed partial class ActivityViewModel(MainViewModel main) : PageViewModel(main, "Activity", "IconActivity")
{
    public MainViewModel Shell => Main;

    public override bool RequiresDevice => false;

    [RelayCommand]
    private Task CopyAsync() => Main.Host.CopyToClipboardAsync(string.Join(Environment.NewLine, Main.Log));

    [RelayCommand]
    private void Clear() => Main.ClearLog();
}
