using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => Refresh();

    public bool HasAnySaved => Main.Library.Items.Count > 0;
    public bool HasNoMatches => HasAnySaved && Entries.Count == 0;
    public bool IsEmpty => !HasAnySaved;

    public void Refresh()
    {
        Entries.Clear();
        var query = SearchText.Trim();
        foreach (var entry in Main.Library.Items)
        {
            if (!string.IsNullOrEmpty(query))
            {
                var matches = entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                              (entry.World is not null && entry.World.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
                              entry.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!matches) continue;
            }
            Entries.Add(new LibraryEntryViewModel(entry));
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasAnySaved));
        OnPropertyChanged(nameof(HasNoMatches));
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
