using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Ive.App;

public static class Program
{
    private static string LogPath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IVE");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "crash.log");
        }
    }

    private static void Log(string where, Exception e)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:s}] {where}\n{e}\n\n"); } catch { /* logging must never throw */ }
    }

    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, @"Local\Ive.App.SingleInstance", out var first);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Ive.App.Show");
        if (!first)
        {
            showEvent.Set(); // tell the running instance to surface its window
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("AppDomain", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true; // a background agent must never crash the desktop session
            Log("Dispatcher", e.Exception);
        };

        IveHost host = null;
        try
        {
            host = new IveHost(app);
            host.Start();
        }
        catch (Exception e)
        {
            Log("Startup", e);
            System.Windows.MessageBox.Show(
                "IVE could not start:\n\n" + e.Message + "\n\nDetails were saved to:\n" + LogPath,
                "IVE", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var listener = new Thread(() => { while (showEvent.WaitOne()) host.ShowMain(); }) { IsBackground = true };
        listener.Start();

        app.Run();
        host.Dispose();
    }
}
