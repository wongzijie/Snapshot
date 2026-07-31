using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace Snapshot;

public sealed class MainController : IDisposable
{
    private readonly System.Windows.Application _application;
    private readonly SettingsService _settingsService;
    private readonly ScreenCaptureService _screenCaptureService = new();
    private readonly OcrService _ocrService = new();
    private readonly TranslatorService _translatorService = new();
    private readonly GlobalHotkeyService _hotkeys = new();
    private readonly Icon _trayIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly HashSet<TextResultWindow> _resultWindows = [];
    private readonly HashSet<TranslationPreviewWindow> _translationWindows = [];

    private AppSettings _settings;
    private SettingsWindow? _settingsWindow;
    private CaptureOverlay? _overlay;
    private string? _lastSavedFile;
    private bool _disposed;
    private bool _exiting;

    public MainController(
        System.Windows.Application application,
        SettingsService settingsService,
        AppSettings settings)
    {
        _application = application;
        _settingsService = settingsService;
        _settings = settings;
        _trayIcon = IconFactory.CreateTrayIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Snapshot 截图",
            Visible = true
        };

        _hotkeys.HotkeyPressed += StartCapture;
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_lastSavedFile) && File.Exists(_lastSavedFile))
            {
                OpenInExplorer(_lastSavedFile, selectFile: true);
            }
        };
    }

    public AppSettings CurrentSettings => _settings.Clone();

    public void Start(bool showSettings = false, CaptureMode? initialCapture = null)
    {
        AppLog.Write($"Controller start. showSettings={showSettings}, initialCapture={initialCapture}");
        BuildTrayMenu();

        if (!_hotkeys.TryRegister(_settings, out var error))
        {
            AppLog.Write("Hotkey registration failed: " + error);
            ShowSettings();
            System.Windows.MessageBox.Show(
                error + "\n\n请设置新的快捷键。",
                "Snapshot 快捷键冲突",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AppLog.Write("Hotkeys registered successfully.");
        if (showSettings)
        {
            _application.Dispatcher.BeginInvoke(ShowSettings);
        }
        else if (initialCapture.HasValue)
        {
            _application.Dispatcher.BeginInvoke(() => StartCapture(initialCapture.Value, showInTaskbar: true));
        }

        ShowBalloon(
            "Snapshot 已在后台运行",
            $"复制截图：{_settings.ClipboardHotkey.ToDisplayString()}\n" +
            $"保存 JPG：{_settings.SaveHotkey.ToDisplayString()}",
            ToolTipIcon.Info,
            2200);
    }

    public bool TryApplySettings(AppSettings candidate, out string error)
    {
        if (!candidate.ClipboardHotkey.IsValid || !candidate.SaveHotkey.IsValid)
        {
            error = "每套快捷键都必须包含至少一个修饰键和一个普通按键。";
            return false;
        }

        if (candidate.ClipboardHotkey.Equals(candidate.SaveHotkey))
        {
            error = "“复制到剪贴板”和“保存 JPG”不能使用同一套快捷键。";
            return false;
        }

        var previous = _settings.Clone();
        if (!_hotkeys.TryRegister(candidate, out error))
        {
            _hotkeys.TryRegister(previous, out _);
            return false;
        }

        try
        {
            _settingsService.Save(candidate);
        }
        catch (Exception exception)
        {
            _hotkeys.TryRegister(previous, out _);
            error = "设置无法保存：" + exception.Message;
            return false;
        }

        _settings = candidate.Clone();
        BuildTrayMenu();
        ShowBalloon(
            "设置已保存",
            $"复制：{_settings.ClipboardHotkey.ToDisplayString()} · " +
            $"保存：{_settings.SaveHotkey.ToDisplayString()}",
            ToolTipIcon.Info,
            1800);
        error = string.Empty;
        return true;
    }

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
        }

        _settingsWindow.LoadSettings(_settings);
        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
        _settingsWindow.Topmost = true;
        _settingsWindow.Topmost = false;
        _settingsWindow.Focus();
    }

    public void Exit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _overlay?.Close();
        _overlay = null;
        foreach (var resultWindow in _resultWindows.ToArray())
        {
            resultWindow.Close();
        }

        _resultWindows.Clear();
        foreach (var translationWindow in _translationWindows.ToArray())
        {
            translationWindow.Close();
        }

        _translationWindows.Clear();
        _settingsWindow?.ForceClose();
        _settingsWindow = null;
        Dispose();
        _application.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotkeys.HotkeyPressed -= StartCapture;
        _hotkeys.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
        GC.SuppressFinalize(this);
    }

    private void StartCapture(CaptureMode mode)
    {
        StartCapture(mode, showInTaskbar: false);
    }

    private void StartCapture(CaptureMode mode, bool showInTaskbar)
    {
        AppLog.Write($"StartCapture requested. mode={mode}, overlayActive={_overlay is not null}");
        if (_overlay is not null || _exiting)
        {
            return;
        }

        try
        {
            AppLog.Write("Capturing desktop background.");
            var snapshot = _screenCaptureService.CaptureDesktop();
            AppLog.Write($"Desktop captured: {snapshot.VirtualBounds}.");
            var overlay = new CaptureOverlay(snapshot, mode, showInTaskbar);
            _overlay = overlay;
            overlay.CaptureCompleted += image => HandleCaptureCompleted(overlay, image, mode);
            overlay.AnalysisRequested += (image, action) =>
                HandleAnalysisRequested(overlay, image, action);
            overlay.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(_overlay, overlay))
                {
                    _overlay = null;
                }
            };
            overlay.Show();
            overlay.Activate();
            AppLog.Write("Capture overlay shown.");
        }
        catch (Exception exception)
        {
            AppLog.Write("Capture failed: " + exception);
            _overlay = null;
            ShowBalloon("截图失败", exception.Message, ToolTipIcon.Error, 3000);
        }
    }

    private async void HandleAnalysisRequested(
        CaptureOverlay overlay,
        Bitmap image,
        CaptureAnalysisAction action)
    {
        if (ReferenceEquals(_overlay, overlay))
        {
            _overlay = null;
        }

        using (image)
        {
            var translationRequested = action == CaptureAnalysisAction.Translate;
            var resultWindow = new TextResultWindow(translationRequested);
            _resultWindows.Add(resultWindow);
            resultWindow.Closed += (_, _) => _resultWindows.Remove(resultWindow);
            resultWindow.OpenSettingsRequested += ShowSettings;
            resultWindow.Show();
            resultWindow.Activate();

            try
            {
                AppLog.Write(
                    $"OCR requested. translate={translationRequested}, size={image.Width}x{image.Height}");
                var recognition = await _ocrService.RecognizeAsync(
                    image,
                    _settings.OcrLanguageTag);
                if (!resultWindow.IsLoaded)
                {
                    return;
                }

                resultWindow.SetRecognition(recognition);
                if (!translationRequested || string.IsNullOrWhiteSpace(recognition.Text))
                {
                    return;
                }

                resultWindow.SetTranslationLoading();
                try
                {
                    var translation = await _translatorService.TranslateRegionsAsync(
                        recognition.Regions,
                        _settings);
                    if (!resultWindow.IsLoaded)
                    {
                        return;
                    }

                    if (translation.Regions.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "已识别到文字，但没有取得可回绘的文字区域。请稍微扩大截图范围后重试。");
                    }

                    var translatedImage = TranslationImageComposer.Compose(
                        image,
                        translation.Regions);
                    ShowTranslationPreview(translatedImage, translation);
                    resultWindow.Close();
                }
                catch (TranslatorConfigurationException exception)
                {
                    AppLog.Write("Translator is not configured: " + exception.Message);
                    if (resultWindow.IsLoaded)
                    {
                        resultWindow.SetError(
                            exception.Message,
                            translationError: true,
                            canOpenSettings: true);
                    }
                }
                catch (Exception exception)
                {
                    AppLog.Write("Translation failed: " + exception);
                    if (resultWindow.IsLoaded)
                    {
                        resultWindow.SetError(
                            exception.Message,
                            translationError: true);
                    }
                }
            }
            catch (Exception exception)
            {
                AppLog.Write("OCR failed: " + exception);
                if (resultWindow.IsLoaded)
                {
                    resultWindow.SetError(
                        exception.Message,
                        translationError: false);
                }
            }
        }
    }

    private void ShowTranslationPreview(
        Bitmap translatedImage,
        RegionTranslationResult result)
    {
        TranslationPreviewWindow? preview = null;
        try
        {
            preview = new TranslationPreviewWindow(translatedImage, result);
            _translationWindows.Add(preview);
            preview.Closed += (_, _) => _translationWindows.Remove(preview);
            preview.CopyImageRequested += image =>
            {
                try
                {
                    CopyToClipboard(image);
                    preview.SetStatus("译图已复制到剪贴板，可直接粘贴到其他应用。");
                }
                catch (Exception exception)
                {
                    preview.SetStatus("复制失败：" + exception.Message, isError: true);
                }
            };
            preview.SaveImageRequested += image =>
            {
                try
                {
                    _lastSavedFile = SaveJpeg(image);
                    preview.SetStatus($"译图已保存：{_lastSavedFile}");
                    ShowBalloon(
                        "译图已保存",
                        $"{Path.GetFileName(_lastSavedFile)}\n点击通知可在文件夹中查看。",
                        ToolTipIcon.Info,
                        2500);
                }
                catch (Exception exception)
                {
                    preview.SetStatus("保存失败：" + exception.Message, isError: true);
                }
            };
            preview.Show();
            preview.Activate();
        }
        catch
        {
            if (preview is not null)
            {
                _translationWindows.Remove(preview);
            }

            translatedImage.Dispose();
            throw;
        }
    }

    private void HandleCaptureCompleted(CaptureOverlay overlay, Bitmap? image, CaptureMode mode)
    {
        if (ReferenceEquals(_overlay, overlay))
        {
            _overlay = null;
        }

        if (image is null)
        {
            AppLog.Write($"Capture cancelled. mode={mode}");
            return;
        }

        using (image)
        {
            AppLog.Write($"Capture completed. mode={mode}, size={image.Width}x{image.Height}");
            try
            {
                if (mode == CaptureMode.Clipboard)
                {
                    CopyToClipboard(image);
                    ShowBalloon(
                        "已复制到剪贴板",
                        $"{image.Width} × {image.Height}，可直接粘贴到其他应用。",
                        ToolTipIcon.Info,
                        1500);
                }
                else
                {
                    _lastSavedFile = SaveJpeg(image);
                    AppLog.Write("JPG saved successfully: " + Path.GetFileName(_lastSavedFile));
                    ShowBalloon(
                        "截图已保存",
                        $"{Path.GetFileName(_lastSavedFile)}\n点击通知可在文件夹中查看。",
                        ToolTipIcon.Info,
                        2500);
                }
            }
            catch (Exception exception)
            {
                ShowBalloon("截图处理失败", exception.Message, ToolTipIcon.Error, 3500);
            }
        }
    }

    private static void CopyToClipboard(Bitmap image)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                using var copy = new Bitmap(image);
                var dataObject = new System.Windows.Forms.DataObject();
                dataObject.SetData(System.Windows.Forms.DataFormats.Bitmap, true, copy);
                System.Windows.Forms.Clipboard.SetDataObject(dataObject, true);
                return;
            }
            catch (ExternalException exception)
            {
                lastError = exception;
                Thread.Sleep(45 + attempt * 35);
            }
        }

        throw new InvalidOperationException("剪贴板正被其他程序占用，请稍后重试。", lastError);
    }

    private string SaveJpeg(Bitmap image)
    {
        var directory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(_settings.SaveDirectory)
                ? KnownFolders.GetDownloadsPath()
                : _settings.SaveDirectory);
        Directory.CreateDirectory(directory);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var path = Path.Combine(directory, $"Screenshot_{timestamp}.jpg");
        var counter = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"Screenshot_{timestamp}_{counter++}.jpg");
        }

        var codec = ImageCodecInfo.GetImageEncoders()
            .First(item => item.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality,
            Math.Clamp(_settings.JpegQuality, 60, 100));
        image.Save(path, codec, parameters);
        return path;
    }

    private void BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(
            $"截图到剪贴板    {_settings.ClipboardHotkey.ToDisplayString()}",
            null,
            (_, _) => StartCapture(CaptureMode.Clipboard));
        menu.Items.Add(
            $"截图并保存 JPG    {_settings.SaveHotkey.ToDisplayString()}",
            null,
            (_, _) => StartCapture(CaptureMode.SaveToFile));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("设置…", null, (_, _) => ShowSettings());
        menu.Items.Add("打开截图保存文件夹", null, (_, _) =>
            OpenInExplorer(_settings.SaveDirectory, selectFile: false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 Snapshot", null, (_, _) => Exit());

        var previous = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = menu;
        previous?.Dispose();
    }

    private void ShowBalloon(
        string title,
        string text,
        ToolTipIcon icon,
        int timeout)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(timeout);
    }

    private static void OpenInExplorer(string path, bool selectFile)
    {
        var fullPath = Path.GetFullPath(path);
        if (!selectFile)
        {
            Directory.CreateDirectory(fullPath);
        }

        var arguments = selectFile
            ? $"/select,\"{fullPath}\""
            : $"\"{fullPath}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments)
        {
            UseShellExecute = true
        });
    }
}
