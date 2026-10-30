using System.Windows;
using System.Windows.Threading;

namespace OdooPosLogin;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch unhandled exceptions on the UI thread and prevent process exit
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        // Catch unhandled exceptions on non-UI threads
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Catch unobserved task exceptions
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        System.Diagnostics.Debug.WriteLine($"[Pos UI UnhandledException] {e.Exception}");
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[Pos AppDomain UnhandledException] {e.ExceptionObject}");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        System.Diagnostics.Debug.WriteLine($"[Pos TaskScheduler UnobservedException] {e.Exception}");
    }
}
