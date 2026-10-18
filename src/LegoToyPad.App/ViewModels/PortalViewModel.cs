using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LegoToyPad.Core;

namespace LegoToyPad.App.ViewModels;

public sealed partial class PortalViewModel : PageViewModel
{
    public PortalViewModel(MainViewModel main) : base(main, "Portal", "IconPortal")
    {
        Left = new PadViewModel(Pad.Left, "Left pad", "Read only", this);
        Center = new PadViewModel(Pad.Center, "Center pad", "Read & write", this);
        Right = new PadViewModel(Pad.Right, "Right pad", "Read only", this);
    }

    public PadViewModel Left { get; }
    public PadViewModel Center { get; }
    public PadViewModel Right { get; }

    public ObservableCollection<ReadResultViewModel> Results { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadAllCommand))]
    public partial int TagCount { get; set; }

    public bool HasResults => Results.Count > 0;

    public string TagSummary => TagCount switch
    {
        0 => "No tags on the portal",
        1 => "1 tag on the portal",
        _ => $"{TagCount} tags on the portal"
    };

    partial void OnTagCountChanged(int value) => OnPropertyChanged(nameof(TagSummary));

    public void UpdateTags(IReadOnlyList<ActiveTag> tags)
    {
        Left.Update(tags);
        Center.Update(tags);
        Right.Update(tags);
        TagCount = tags.Count;
    }

    [RelayCommand]
    private async Task ReadTagAsync(TagViewModel tag)
    {
        var result = await Main.RunAsync($"Reading {tag.Tag.PadName} tag…", () => Main.Service.ReadTagAsync(tag.Tag.Pad, tag.Tag.Index));
        if (result is not null) ShowResults([result]);
    }

    private bool CanReadAll => TagCount > 0;

    [RelayCommand(CanExecute = nameof(CanReadAll))]
    private async Task ReadAllAsync()
    {
        var results = await Main.RunAsync("Reading all tags…", async () =>
        {
            var list = new List<TagReadResult>();
            foreach (var tag in Main.Service.ActiveTags)
            {
                try
                {
                    list.Add(await Main.Service.ReadTagAsync(tag.Pad, tag.Index));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Already logged by the service; keep reading the rest.
                }
            }
            return list;
        });
        if (results is not null) ShowResults(results);
    }

    [RelayCommand]
    private void ClearResults()
    {
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
    }

    private void ShowResults(IEnumerable<TagReadResult> results)
    {
        Results.Clear();
        foreach (var r in results) Results.Add(new ReadResultViewModel(r, Main));
        OnPropertyChanged(nameof(HasResults));
    }
}

/// <summary>One physical pad (LEFT/CENTER/RIGHT) and the tags currently on it.</summary>
public sealed class PadViewModel(Pad pad, string title, string capability, PortalViewModel portal) : ViewModelBase
{
    public Pad Pad { get; } = pad;
    public string Title { get; } = title;
    public string Capability { get; } = capability;
    public bool IsWritable => Pad == Pad.Center;
    public PortalViewModel Portal { get; } = portal;
    public ObservableCollection<TagViewModel> Tags { get; } = [];
    public bool IsEmpty => Tags.Count == 0;
    public string AccessibleSummary => IsEmpty ? $"{Title}: empty" : $"{Title}: {string.Join(", ", Tags.Select(t => t.Name))}";

    public void Update(IReadOnlyList<ActiveTag> all)
    {
        var mine = all.Where(t => t.Pad == Pad).ToList();
        Tags.Clear();
        foreach (var tag in mine) Tags.Add(new TagViewModel(tag));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(AccessibleSummary));
    }
}

public sealed class TagViewModel(ActiveTag tag)
{
    public ActiveTag Tag { get; } = tag;

    public string Name => Tag.State switch
    {
        IdentifyState.Pending => "Identifying…",
        IdentifyState.Unrecognized => "Blank or unknown tag",
        _ => Tag.Identity!.Name
    };

    public string Detail => Tag.Identity is { } id
        ? id.Kind == TokenKind.Character ? id.World ?? "Character" : "Vehicle / gadget"
        : $"Slot {Tag.Index}";

    public string Uid => Tag.Uid;
    public bool IsPending => Tag.State == IdentifyState.Pending;
    public string AccessibleName => $"{Name}, {Detail}, UID {Uid}. Press to read.";
}

public sealed partial class ReadResultViewModel(TagReadResult result, MainViewModel main) : ViewModelBase
{
    public TagReadResult Result { get; } = result;

    public string Heading => Result.Identity?.Name ?? "Not recognized";
    public string Subheading => Result.Identity?.Description ?? "Blank, unknown token, or custom data.";
    public string Location => $"{Result.PadName} pad · slot {Result.Index}";
    public string Uid => Result.Uid;
    public bool IsIdentified => Result.Identity is not null;
    public bool PasswordMatches => Result.Password.Matches;
    public string PasswordText => Result.Password.Matches
        ? "Password matches the UID (genuine-style tag)"
        : $"Password {Result.Password.Stored} ≠ expected {Result.Password.Expected}";
    public IReadOnlyList<PageValue> Pages => Result.Pages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveText))]
    public partial bool IsSaved { get; set; }

    public string SaveText => IsSaved ? "Saved to library" : "Save to library";

    [ObservableProperty]
    public partial string? Dump { get; set; }

    [RelayCommand]
    private void SaveToLibrary()
    {
        if (Result.Identity is null) return;
        main.Library.Add(Result.Identity, Result.Uid);
        main.LibraryPage.Refresh();
        IsSaved = true;
    }

    [RelayCommand]
    private void WriteCopy()
    {
        if (Result.Identity is null) return;
        main.Write.Select(Result.Identity.Kind, Result.Identity.Id);
        main.Navigate(main.Write);
    }

    [RelayCommand]
    private async Task DumpPagesAsync()
    {
        var pages = await main.RunAsync("Dumping raw pages…", () => main.Service.DumpPagesAsync(Result.Pad, Result.Index));
        if (pages is null) return;

        var sb = new StringBuilder();
        foreach (var p in pages) sb.AppendLine($"0x{p.Page:X2}  {p.Hex ?? "read failed"}");
        Dump = sb.ToString().TrimEnd();
    }

    [RelayCommand]
    private Task CopyDumpAsync() => main.Host.CopyToClipboardAsync(Dump ?? string.Empty);
}
