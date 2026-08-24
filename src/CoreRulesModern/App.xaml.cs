using System.Windows;
using System.Windows.Threading;
using CoreRulesModern.Services;

namespace CoreRulesModern;

public partial class App : Application
{
    public App()
    {
        // WebView2 otherwise paints its default white surface before its WPF
        // DefaultBackgroundColor property takes effect. Set the opaque parchment
        // colour before any controller is created to prevent that first-frame flash.
        Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "FFF5E8C8");
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
