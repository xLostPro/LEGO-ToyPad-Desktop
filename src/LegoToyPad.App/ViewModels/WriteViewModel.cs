using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LegoToyPad.Core;
using LegoToyPad.Core.Data;

namespace LegoToyPad.App.ViewModels;

public sealed record TokenOption(TokenKind Kind, uint Id, string Name, string Detail)
{
    public string IdText => $"#{Id}";
    public string AccessibleName => $"{Name}, {Detail}, ID {Id}";
}

public sealed partial class WriteViewModel : PageViewModel
{
    private static readonly IReadOnlyList<TokenOption> AllCharacters = TokenDatabase.Characters
        .Where(c => !c.IsPlaceholder)
        .Select(c => new TokenOption(TokenKind.Character, c.Id, c.Name, c.World))
        .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private static readonly IReadOnlyList<TokenOption> AllVehicles = TokenDatabase.Vehicles
        .Where(v => !v.IsPlaceholder)
        .Select(v => new TokenOption(TokenKind.Vehicle, v.Id, v.Name.TrimStart('*', ' '), v.Rebuild == 0 ? "Vehicle / gadget" : $"Alternate build {v.Rebuild}"))
        .OrderBy(o => o.Id)
        .ToList();

    public const string AllWorldsOption = "All worlds";

    public static IReadOnlyList<string> AllWorlds { get; } = new[] { AllWorldsOption }
        .Concat(AllCharacters.Select(c => c.Detail).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(w => w, StringComparer.CurrentCultureIgnoreCase))
        .ToList();

    public IReadOnlyList<string> Worlds => AllWorlds;

    private string? _checkedUid;

    public WriteViewModel(MainViewModel main) : base(main, "Write", "IconWrite") => ApplyFilter();

    public ObservableCollection<TokenOption> Options { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVehicleMode))]
    public partial bool IsCharacterMode { get; set; } = true;

    public bool IsVehicleMode
    {
        get => !IsCharacterMode;
        set => IsCharacterMode = !value;
    }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedWorld { get; set; } = AllWorldsOption;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WriteCommand))]
    [NotifyPropertyChangedFor(nameof(SelectionText))]
    public partial TokenOption? Selected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WriteCommand))]
    public partial bool IsTagReady { get; set; }

    [ObservableProperty]
    public partial StatusMessage? CheckStatus { get; set; }

    [ObservableProperty]
    public partial StatusMessage? WriteStatus { get; set; }

    public string SelectionText => Selected is null ? "Nothing selected yet" : $"{Selected.Name} ({Selected.Detail}, ID {Selected.Id})";

    public string ResultCountText => Options.Count == 1 ? "1 match" : $"{Options.Count} matches";

    partial void OnIsCharacterModeChanged(bool value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedWorldChanged(string value) => ApplyFilter();

    public void UpdateTags(IReadOnlyList<ActiveTag> tags)
    {
        if (_checkedUid is not null && tags.All(t => t.Pad != Pad.Center || !string.Equals(t.Uid, _checkedUid, StringComparison.OrdinalIgnoreCase)))
        {
            _checkedUid = null;
            IsTagReady = false;
            CheckStatus = null;
        }
    }

    public override void OnNavigatedTo() => UpdateTags(Main.Service.ActiveTags);

    public void Select(TokenKind kind, uint id)
    {
        IsCharacterMode = kind == TokenKind.Character;
        SearchText = string.Empty;
        SelectedWorld = AllWorldsOption;
        Selected = Options.FirstOrDefault(o => o.Id == id);
        WriteStatus = null;
    }

    private void ApplyFilter()
    {
        var source = IsCharacterMode ? AllCharacters : AllVehicles;
        var query = SearchText.Trim();
        var keep = Selected;

        Options.Clear();
        foreach (var option in source)
        {
            if (IsCharacterMode && SelectedWorld != AllWorldsOption && !string.Equals(option.Detail, SelectedWorld, StringComparison.CurrentCultureIgnoreCase))
                continue;

            if (query.Length == 0
                || option.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || option.Detail.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || option.Id.ToString() == query)
            {
                Options.Add(option);
            }
        }

        Selected = keep is not null && Options.Contains(keep) ? keep : null;
        OnPropertyChanged(nameof(ResultCountText));
    }

    [RelayCommand]
    private async Task CheckTagAsync()
    {
        IsTagReady = false;
        WriteStatus = null;
        CheckStatus = new StatusMessage(Severity.Info, "Checking the tag on the center pad…");

        var result = await Main.RunAsync("Checking tag…", () => Main.Service.PrepareWriteAsync(),
            ex => CheckStatus = new StatusMessage(Severity.Error, "Couldn't check the tag", ex.Message));
        if (result is null) return;

        IsTagReady = result.Ok;
        _checkedUid = result.Ok ? result.Tag.Uid : null;
        CheckStatus = result.Ok
            ? new StatusMessage(Severity.Success, "Tag is ready to write", $"UID {result.Tag.Uid}")
            : new StatusMessage(Severity.Warning, "This tag can't be written", result.Reason);
    }

    private bool CanWrite => IsTagReady && Selected is not null;

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task WriteAsync()
    {
        var option = Selected!;
        var confirmed = await Main.Host.ConfirmAsync(
            "Write tag?",
            $"Write \"{option.Name}\" to the tag on the center pad? This overwrites the tag's character/vehicle data.",
            "Write tag",
            destructive: false);
        if (!confirmed) return;

        var result = await Main.RunAsync($"Writing {option.Name}…", () => Main.Service.CommitWriteAsync(option.Kind, option.Id, _checkedUid),
            ex => WriteStatus = new StatusMessage(Severity.Error, "Write failed", ex.Message));
        if (result is null) return;

        WriteStatus = result.Ok
            ? new StatusMessage(Severity.Success, $"Wrote {result.Name}", $"Verified pages 0x24 = {result.Page24}, 0x25 = {result.Page25}. Place a new blank tag and check it to write another.")
            : new StatusMessage(Severity.Error, "Write could not be verified", "The pages read back differently from what was written. See Activity for details.");

        // The tag is no longer blank; require a fresh check before the next write.
        IsTagReady = false;
        CheckStatus = null;
    }
}
