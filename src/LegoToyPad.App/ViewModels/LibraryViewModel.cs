using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LegoToyPad.Core;
using LegoToyPad.Core.Library;

namespace LegoToyPad.App.ViewModels;

public sealed class LibraryEntryViewModel(LibraryEntry entry)
{
    public LibraryEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Detail => Entry.Type == TokenKind.Character
        ? $"Character · {Entry.World} · ID {Entry.Id}"
        : $"Vehicle / gadget · ID {Entry.Id}";
    public string Saved => $"Saved {Entry.SavedAt.LocalDateTime:d MMM yyyy}";
    public string AccessibleName => $"{Name}, {Detail}";
}

public sealed partial class LibraryViewModel : PageViewModel
{
    public LibraryViewModel(MainViewModel main) : base(main, "Library", "IconLibrary") => Refresh();

    public ObservableCollection<LibraryEntryViewModel> Entries { get; } = [];

    public override bool RequiresDevice => false;

    public bool IsEmpty => Entries.Count == 0;

    public void Refresh()
    {
        Entries.Clear();
        foreach (var entry in Main.Library.Items) Entries.Add(new LibraryEntryViewModel(entry));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void UseForWrite(LibraryEntryViewModel item)
    {
        Main.Write.Select(item.Entry.Type, item.Entry.Id);
        Main.Navigate(Main.Write);
    }

    [RelayCommand]
    private async Task RemoveAsync(LibraryEntryViewModel item)
    {
        var confirmed = await Main.Host.ConfirmAsync("Remove from library?", $"Remove \"{item.Name}\" from your library? Tags you've already written aren't affected.", "Remove", destructive: true);
        if (!confirmed) return;

        Main.Library.Remove(item.Entry.LibraryId);
        Refresh();
    }
}
