using System;
using System.Threading;
using System.Windows;

namespace Ive.App;

public static class Program
{
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

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true; // a background agent must never crash the desktop session
            System.Diagnostics.Debug.WriteLine(e.Exception);
        };

        var host = new IveHost(app);
        host.Start();

        var listener = new Thread(() => { while (showEvent.WaitOne()) host.ShowMain(); }) { IsBackground = true };
        listener.Start();

        app.Run();
        host.Dispose();
    }
}
