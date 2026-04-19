using System;
using System.Windows;
using System.Windows.Threading;

namespace XsheetMark;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError("UI thread", e.Exception);
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        ShowError("Background thread", e.ExceptionObject as Exception);
    }

    private static void ShowError(string context, Exception? ex)
    {
        if (ex is null) return;
        MessageBox.Show(
            $"{context}:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
            "xsheet-mark — unhandled exception",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
