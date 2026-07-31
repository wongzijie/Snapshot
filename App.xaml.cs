using System.Threading;
using System.Windows;

namespace Snapshot;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private MainController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Write("Application started. Arguments: " + string.Join(" ", e.Args));
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _singleInstanceMutex = new Mutex(true, @"Local\Snapshot.ScreenCapture.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(
                "Snapshot 已经在运行，请在系统托盘中找到绿色截图图标。",
                "Snapshot",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        _controller = new MainController(this, settingsService, settings);
        var showSettings = e.Args.Any(argument =>
            string.Equals(argument, "--settings", StringComparison.OrdinalIgnoreCase));
        CaptureMode? initialCapture = e.Args.Any(argument =>
            string.Equals(argument, "--capture-clipboard", StringComparison.OrdinalIgnoreCase))
            ? CaptureMode.Clipboard
            : e.Args.Any(argument =>
                string.Equals(argument, "--capture-save", StringComparison.OrdinalIgnoreCase))
                ? CaptureMode.SaveToFile
                : null;
        _controller.Start(showSettings, initialCapture);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
