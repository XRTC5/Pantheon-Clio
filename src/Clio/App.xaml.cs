using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Clio.Core;

namespace Clio;

public partial class App : Application
{
    /// <summary>Two Clios would each write notes.json and the last one would win.</summary>
    private Mutex? _single;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(initiallyOwned: true, "Local\\ClioSingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        // Software rendering, as in Argus, where it took private bytes from
        // 139MB to 42MB: Direct3D loads both GPU vendors' user-mode drivers on
        // this dual-GPU laptop to accelerate windows that almost never move.
        // AllowsTransparency forces software for each note anyway.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("unhandled exception", args.ExceptionObject as Exception);

        Store.Load();

        var notes = new Notes();
        notes.Start();

        _tray = new TrayIcon(notes.Toggle, notes.Menu);

        // Startup pages - XAML parsing, JIT, the JSON load - are never touched
        // again. Trim once everything has settled, not before.
        var settle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            Native.TrimWorkingSet();
        };
        settle.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Store.Save();
        _tray?.Dispose();
        _single?.ReleaseMutex();
        _single?.Dispose();
        base.OnExit(e);
    }
}
