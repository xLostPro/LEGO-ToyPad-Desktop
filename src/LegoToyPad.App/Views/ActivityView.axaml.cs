using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using LegoToyPad.App.ViewModels;

namespace LegoToyPad.App.Views;

public partial class ActivityView : UserControl
{
    private ActivityViewModel? _viewModel;

    public ActivityView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null) _viewModel.Shell.Log.CollectionChanged -= OnLogChanged;
        _viewModel = DataContext as ActivityViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Shell.Log.CollectionChanged += OnLogChanged;
            ScrollToEnd();
        }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) ScrollToEnd();
    }

    // Keep the newest entry in view.
    private void ScrollToEnd() => Dispatcher.UIThread.Post(() =>
    {
        if (_viewModel?.Shell.Log is { Count: > 0 } log) LogList.ScrollIntoView(log[^1]);
    }, DispatcherPriority.Background);
}
