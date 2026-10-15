using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace PresenceSync.App;

public partial class App : Application
{
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PresenceSync", "logs");

    private Mutex? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\PresenceSync.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                new RedactingFormatter("{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"),
                Path.Combine(LogDirectory, "presencesync-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();
        Log.Information("PresenceSync {Version} startet", typeof(App).Assembly.GetName().Version);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Fatal(args.ExceptionObject as Exception, "Unbehandelte Ausnahme");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unbeobachtete Task-Ausnahme");
            args.SetObserved();
        };

        base.OnStartup(e);
        Theme.ApplySystem();
        _controller = new AppController(this);
        _controller.Start();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _controller?.OnSessionEnding(e.ReasonSessionEnding.ToString());
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        Log.Information("PresenceSync beendet");
        Log.CloseAndFlush();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unbehandelte Ausnahme (UI)");
        e.Handled = true;
    }
}
