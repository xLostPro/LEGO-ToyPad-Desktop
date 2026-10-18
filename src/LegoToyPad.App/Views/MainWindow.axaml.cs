using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using LegoToyPad.App.ViewModels;

namespace LegoToyPad.App.Views;

public partial class MainWindow : Window, IUiHost
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyZoom(vm.Zoom);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Zoom) && sender is MainViewModel vm) ApplyZoom(vm.Zoom);
    }

    private void ApplyZoom(double zoom) => ZoomHost.LayoutTransform = new ScaleTransform(zoom, zoom);

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive) =>
        new ConfirmDialog(title, message, confirmText, destructive).ShowDialog<bool>(this);

    public async Task CopyToClipboardAsync(string text)
    {
        if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }
}
