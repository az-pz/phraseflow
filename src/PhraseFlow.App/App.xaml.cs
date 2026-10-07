using System.Windows;
using System.Windows.Threading;
using PhraseFlow.App.Services;
using PhraseFlow.Core.Storage;

namespace PhraseFlow.App;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var requestExit = e.Args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase));
        _instance = SingleInstance.TryAcquire(requestExit);
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        var paths = DataPaths.Resolve(AppContext.BaseDirectory);
        Log.Initialize(paths.LogFile);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        _controller = new AppController(paths);
        var launchedAtSignIn = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        _controller.Start(showWindow: !(launchedAtSignIn && _controller.Settings.StartMinimized));
        _instance.Listen(message =>
        {
            if (message == SingleInstance.ExitMessage)
            {
                Dispatcher.BeginInvoke(_controller.Exit);
            }
            else
            {
                _controller.ShowMainWindow();
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(e.Exception.Message, "PhraseFlow – unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
