using CommunityToolkit.Mvvm.ComponentModel;

namespace LegoToyPad.App.ViewModels;

public abstract class ViewModelBase : ObservableObject;

public enum Severity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>An inline status banner (icon + title + optional detail).</summary>
public sealed record StatusMessage(Severity Severity, string Title, string? Detail = null)
{
    public bool IsSuccess => Severity == Severity.Success;
    public bool IsWarning => Severity == Severity.Warning;
    public bool IsError => Severity == Severity.Error;
    public bool IsInfo => Severity == Severity.Info;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}

/// <summary>UI-side services the view models need from the window.</summary>
public interface IUiHost
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive);

    Task CopyToClipboardAsync(string text);
}
