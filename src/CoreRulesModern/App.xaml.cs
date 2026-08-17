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
        DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.TryWrite(e.Exception, "UI thread");
        MessageBox.Show(
            $"Core Rules Library encountered an unexpected error.\n\nDiagnostic details were written to:\n{CrashLog.LogPath}\n\n{e.Exception.Message}",
            "Core Rules Library error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(-1);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLog.TryWrite(e.Exception, "background task");
        e.SetObserved();
    }
}
