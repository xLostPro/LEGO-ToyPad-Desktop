using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LegoToyPad.App.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() : this(string.Empty, string.Empty, "OK", false)
    {
    }

    public ConfirmDialog(string title, string message, string confirmText, bool destructive)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        if (destructive)
        {
            // Destructive actions must be confirmed deliberately: danger styling, and Enter lands on Cancel.
            ConfirmButton.Classes.Remove("accent");
            ConfirmButton.Classes.Add("danger");
            CancelButton.IsDefault = true;
        }
        else
        {
            ConfirmButton.IsDefault = true;
        }

        CancelButton.Click += (_, _) => Close(false);
        ConfirmButton.Click += (_, _) => Close(true);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        (CancelButton.IsDefault ? CancelButton : ConfirmButton).Focus();
    }
}
