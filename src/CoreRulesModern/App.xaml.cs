using System.Windows;
using System.Windows.Threading;
using CoreRulesModern.Services;

namespace CoreRulesModern;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e) =>
        CrashLog.TryWrite(e.Exception, "UI thread");

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLog.TryWrite(e.Exception, "background task");
        e.SetObserved();
    }
}
