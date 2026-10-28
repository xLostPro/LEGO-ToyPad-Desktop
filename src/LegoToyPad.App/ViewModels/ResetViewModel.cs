using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LegoToyPad.Core;

namespace LegoToyPad.App.ViewModels;

public sealed partial class ResetViewModel(MainViewModel main) : PageViewModel(main, "Reset", "IconReset")
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial ResetPreview? Preview { get; set; }

    [ObservableProperty]
    public partial bool ClearPasswordPage { get; set; }

    [ObservableProperty]
    public partial StatusMessage? Status { get; set; }

    public bool HasPreview => Preview is not null;

    public void UpdateTags(IReadOnlyList<ActiveTag> tags)
    {
        if (Preview is not null && tags.All(t => t.Pad != Pad.Center || !string.Equals(t.Uid, Preview.Tag.Uid, StringComparison.OrdinalIgnoreCase)))
        {
            Preview = null;
        }
    }

    public override void OnNavigatedTo() => UpdateTags(Main.Service.ActiveTags);

    [RelayCommand]
    private async Task PreviewAsync()
    {
        Status = null;
        Preview = await Main.RunAsync("Reading tag…", () => Main.Service.PrepareResetAsync(),
            ex => Status = new StatusMessage(Severity.Error, "Couldn't read the tag", ex.Message));
    }

    [RelayCommand(CanExecute = nameof(HasPreview))]
    private async Task ResetAsync()
    {
        var pages = ClearPasswordPage ? "0x24–0x26 and the password page 0x2B" : "0x24–0x26";
        var confirmed = await Main.Host.ConfirmAsync(
            "Reset tag?",
            $"This erases pages {pages} on the center tag, so it will no longer be recognised as a character or vehicle. This can't be undone.",
            "Reset tag",
            destructive: true);
        if (!confirmed) return;

        var expectedUid = Preview?.Tag.Uid;
        var result = await Main.RunAsync("Resetting tag…", () => Main.Service.CommitResetAsync(ClearPasswordPage, expectedUid),
            ex => Status = new StatusMessage(Severity.Error, "Reset failed", ex.Message));
        if (result is null) return;

        var detail = result.Failures.Count == 0 ? $"Pages {string.Join(", ", result.ResetPages)} now read 00000000." : string.Join("\n", result.Failures);
        Status = result.Status switch
        {
            ResetStatus.Success => new StatusMessage(Severity.Success, "Tag reset", detail),
            ResetStatus.Partial => new StatusMessage(Severity.Warning, "Gameplay pages reset, but the password page couldn't be cleared", detail),
            _ => new StatusMessage(Severity.Error, "Reset failed", detail)
        };
        Preview = null;
    }
}
