using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LegoToyPad.Core;
using LegoToyPad.Core.Library;

namespace LegoToyPad.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const int MaxLogEntries = 2000;
    private const double MinZoom = 0.8;
    private const double MaxZoom = 2.0;

    private readonly SettingsStore? _settings;
    private string _theme = "System";

    public MainViewModel(ToyPadService service, LibraryStore library, IUiHost host, SettingsStore? settings = null)
    {
        Service = service;
        Library = library;
        Host = host;
        _settings = settings;

        Portal = new PortalViewModel(this);
        Write = new WriteViewModel(this);
        Reset = new ResetViewModel(this);
        LibraryPage = new LibraryViewModel(this);
        Activity = new ActivityViewModel(this);
        Pages = [Portal, Write, Reset, LibraryPage, Activity];
        SelectedPage = Portal;

        if (_settings is not null)
        {
            var saved = _settings.Load();
            Zoom = saved.Zoom;
            SetTheme(saved.Theme);
        }

        service.Log += entry => Dispatcher.UIThread.Post(() => AddLog(entry));
        service.TagsChanged += tags => Dispatcher.UIThread.Post(() => OnTagsChanged(tags));
        service.ConnectionChanged += (connected, error) => Dispatcher.UIThread.Post(() => OnConnectionChanged(connected, error));
    }

    public ToyPadService Service { get; }
    public LibraryStore Library { get; }
    public IUiHost Host { get; }

    public PortalViewModel Portal { get; }
    public WriteViewModel Write { get; }
    public ResetViewModel Reset { get; }
    public LibraryViewModel LibraryPage { get; }
    public ActivityViewModel Activity { get; }
    public IReadOnlyList<PageViewModel> Pages { get; }

    public ObservableCollection<LogEntryViewModel> Log { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnectPrompt), nameof(ShowPage))]
    public partial PageViewModel SelectedPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText), nameof(IsDisconnected), nameof(ShowConnectPrompt), nameof(ShowPage))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand), nameof(RescanCommand))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool IsConnecting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? BusyText { get; set; }

    [ObservableProperty]
    public partial StatusMessage? ConnectionError { get; set; }

    [ObservableProperty]
    public partial LogEntryViewModel? LastLog { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    public partial double Zoom { get; set; } = 1.0;

    partial void OnZoomChanged(double value) => _settings?.Save(new UserSettings(_theme, Zoom));

    public bool IsDisconnected => !IsConnected;
    public bool IsIdle => !IsBusy;
    public bool ShowConnectPrompt => SelectedPage.RequiresDevice && !IsConnected;
    public bool ShowPage => !ShowConnectPrompt;
    public string ConnectionText => IsConnected ? "ToyPad connected" : IsConnecting ? "Connecting…" : "Not connected";
    public string ZoomText => $"{Zoom:P0}";

    partial void OnSelectedPageChanged(PageViewModel value) => value?.OnNavigatedTo();

    partial void OnIsConnectingChanged(bool value) => OnPropertyChanged(nameof(ConnectionText));

    public void Navigate(PageViewModel page) => SelectedPage = page;

    // ------------------------------------------------------------------
    // Connection
    // ------------------------------------------------------------------

    private bool CanConnect => !IsConnected && !IsConnecting;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsConnecting = true;
        ConnectionError = null;
        try
        {
            await Task.Run(() => Service.ConnectAsync());
        }
        catch (Exception ex)
        {
            ConnectionError = new StatusMessage(Severity.Error, "Couldn't connect to the ToyPad", ex.Message);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void Disconnect() => Service.Disconnect();

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task RescanAsync() => RunAsync("Scanning the portal…", () => Service.RescanAsync());

    private void OnConnectionChanged(bool connected, string? error)
    {
        IsConnected = connected;
        if (!connected && error is not null && !IsConnecting)
            ConnectionError = new StatusMessage(Severity.Error, "The ToyPad was disconnected", error);
        if (!connected) OnTagsChanged([]);
    }

    private void OnTagsChanged(IReadOnlyList<ActiveTag> tags)
    {
        Portal.UpdateTags(tags);
        Write.UpdateTags(tags);
        Reset.UpdateTags(tags);
    }

    // ------------------------------------------------------------------
    // Busy / errors
    // ------------------------------------------------------------------

    /// <summary>Runs a device operation off the UI thread with a busy indicator; returns null and logs on failure.</summary>
    public async Task<T?> RunAsync<T>(string busyText, Func<Task<T>> operation, Action<Exception>? onError = null)
    {
        IsBusy = true;
        BusyText = busyText;
        try
        {
            return await Task.Run(operation);
        }
        catch (Exception ex)
        {
            AddLog(new LogEntry(LogLevel.Error, ex.Message, DateTimeOffset.Now));
            onError?.Invoke(ex);
            return default;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    public Task RunAsync(string busyText, Func<Task> operation, Action<Exception>? onError = null) =>
        RunAsync<bool>(busyText, async () =>
        {
            await operation();
            return true;
        }, onError);

    private void AddLog(LogEntry entry)
    {
        var vm = new LogEntryViewModel(entry);
        Log.Add(vm);
        if (Log.Count > MaxLogEntries) Log.RemoveAt(0);
        LastLog = vm;
    }

    public void ClearLog()
    {
        Log.Clear();
        LastLog = null;
    }

    // ------------------------------------------------------------------
    // View options
    // ------------------------------------------------------------------

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(MaxZoom, Math.Round(Zoom + 0.1, 1));

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(MinZoom, Math.Round(Zoom - 0.1, 1));

    [RelayCommand]
    private void ResetZoom() => Zoom = 1.0;

    [RelayCommand]
    private void SetTheme(string variant)
    {
        _theme = variant is "Light" or "Dark" ? variant : "System";
        _settings?.Save(new UserSettings(_theme, Zoom));
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = _theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    [RelayCommand]
    private void GoTo(PageViewModel page) => Navigate(page);
}
