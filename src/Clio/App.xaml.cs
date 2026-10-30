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
    private bool _owner;

    /// <summary>
    /// "Clio.exe --quit" sets this, and the running Clio saves and exits. It is
    /// how Argus stops Clio without killing it mid-save, and leaves no dead
    /// icon in the tray the way a kill does.
    /// </summary>
    private const string QuitEvent = "Local\\ClioQuit";
    private EventWaitHandle? _quit;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--quit", StringComparer.OrdinalIgnoreCase))
        {
            if (EventWaitHandle.TryOpenExisting(QuitEvent, out var running)) running.Set();
            Shutdown();
            return;
        }

        _single = new Mutex(initiallyOwned: true, "Local\\ClioSingleInstance", out _owner);
        if (!_owner)
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

        _quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEvent);
        ThreadPool.RegisterWaitForSingleObject(_quit,
            (_, _) => Dispatcher.BeginInvoke(notes.Quit), null, Timeout.Infinite, executeOnlyOnce: true);

        // Off the UI thread: schtasks takes a moment and nothing waits on it.
        var wanted = Store.State.RunOnStartup;
        System.Threading.Tasks.Task.Run(() => Core.Startup.Sync(wanted));

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
        // A second instance that stood down never owned the mutex or the data.
        if (_owner) Store.Save();
        _tray?.Dispose();
        _quit?.Dispose();
        if (_owner) _single?.ReleaseMutex();
        _single?.Dispose();
        base.OnExit(e);
    }
}
