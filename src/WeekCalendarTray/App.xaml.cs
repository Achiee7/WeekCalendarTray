using System.Windows;

namespace WeekCalendarTray;

public partial class App : System.Windows.Application
{
    private TrayApplicationController? _trayController;
    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _instanceMutex = new Mutex(true, @"Local\WeekCalendarTray", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) => AppDiagnostics.Log("Unhandled UI exception", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                AppDiagnostics.Log("Unhandled process exception", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppDiagnostics.Log("Unobserved background task", args.Exception);
            args.SetObserved();
        };
        ThemeManager.Initialize();
        await ThemeManager.Initialization;
        if (Dispatcher.HasShutdownStarted) return;
        AppPaths.RemoveLegacyTokenDirectory();
        _trayController = new TrayApplicationController();
        _trayController.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayController?.Dispose();
        ThemeManager.Shutdown();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
