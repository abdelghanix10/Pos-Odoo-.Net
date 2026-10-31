using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace OdooPosLogin;

public partial class App : Application
{
    private static void LogError(string source, Exception? ex)
    {
        string message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex?.ToString() ?? "Unknown exception"}\n";
        Console.Error.WriteLine(message);
        try
        {
            File.AppendAllText("app_crash.log", message);
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_crash.log"), message);
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            File.AppendAllText("startup.log", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] OnStartup entered\n");
        }
        catch { }

        // Catch unhandled exceptions on the UI thread
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        // Catch unhandled exceptions on non-UI threads
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Catch unobserved task exceptions
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("DispatcherUnhandledException", e.Exception);
        try
        {
            MessageBox.Show($"Application Error:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", "Pos Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }

        // If the main window hasn't even loaded yet, do not keep a zombie process alive
        if (MainWindow == null || !MainWindow.IsLoaded)
        {
            e.Handled = false;
        }
        else
        {
            e.Handled = true;
        }
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogError("AppDomain_UnhandledException", e.ExceptionObject as Exception);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError("TaskScheduler_UnobservedTaskException", e.Exception);
        e.SetObserved();
    }
}
