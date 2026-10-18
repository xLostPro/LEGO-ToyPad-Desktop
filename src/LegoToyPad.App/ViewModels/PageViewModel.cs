using Avalonia.Controls;
using Avalonia.Media;

namespace LegoToyPad.App.ViewModels;

/// <summary>A page reachable from the navigation rail.</summary>
public abstract class PageViewModel(MainViewModel main, string title, string iconKey) : ViewModelBase
{
    protected MainViewModel Main { get; } = main;

    public string Title { get; } = title;

    public StreamGeometry? Icon =>
        Avalonia.Application.Current?.TryFindResource(iconKey, out var value) == true ? value as StreamGeometry : null;

    /// <summary>Pages that talk to the ToyPad show a "connect" prompt while disconnected.</summary>
    public virtual bool RequiresDevice => true;

    public virtual void OnNavigatedTo()
    {
    }
}
