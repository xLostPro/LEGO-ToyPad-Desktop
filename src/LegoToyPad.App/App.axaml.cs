using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LegoToyPad.App.ViewModels;
using LegoToyPad.App.Views;
using LegoToyPad.Core;
using LegoToyPad.Core.Library;
using LegoToyPad.Core.Transport;

namespace LegoToyPad.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // --demo runs against a simulated portal so the app can be explored without hardware.
            IHidTransportFactory transport = desktop.Args?.Contains("--demo") == true
                ? SimulatedToyPad.CreateDemo()
                : new HidSharpTransportFactory();
            var service = new ToyPadService(transport);
            var window = new MainWindow();
            window.DataContext = new MainViewModel(
                service,
                new LibraryStore(LibraryStore.DefaultPath),
                window,
                new SettingsStore(SettingsStore.DefaultPath));
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => service.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
