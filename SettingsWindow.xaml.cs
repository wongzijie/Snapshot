using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace Snapshot;

public partial class SettingsWindow : Window
{
    private readonly MainController _controller;
    private AppSettings _workingSettings;
    private bool _allowClose;

    public SettingsWindow(MainController controller)
    {
        _controller = controller;
        _workingSettings = controller.CurrentSettings;
        InitializeComponent();
        RenderSettings();
    }

    public void LoadSettings(AppSettings settings)
    {
        _workingSettings = settings.Clone();
        if (IsInitialized)
        {
            RenderSettings();
        }
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void RenderSettings()
    {
        ClipboardHotkeyBox.Text = _workingSettings.ClipboardHotkey.ToDisplayString();
        SaveHotkeyBox.Text = _workingSettings.SaveHotkey.ToDisplayString();
        SaveDirectoryBox.Text = _workingSettings.SaveDirectory;
        QualitySlider.Value = _workingSettings.JpegQuality;
        QualityValue.Text = _workingSettings.JpegQuality + "%";
        StatusText.Text = string.Empty;
    }

    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            textBox.SelectAll();
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(102, 112, 133));
            StatusText.Text = "现在按下新的组合键；按 Esc 可取消输入。";
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            Keyboard.ClearFocus();
            StatusText.Text = string.Empty;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var definition = new HotkeyDefinition
        {
            Control = modifiers.HasFlag(ModifierKeys.Control),
            Alt = modifiers.HasFlag(ModifierKeys.Alt),
            Shift = modifiers.HasFlag(ModifierKeys.Shift),
            Windows = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin),
            Key = KeyInterop.VirtualKeyFromKey(key)
        };

        if (!definition.IsValid)
        {
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(209, 67, 67));
            StatusText.Text = "请同时按住 Ctrl、Alt、Shift 或 Win 中的至少一个修饰键。";
            return;
        }

        if (sender is System.Windows.Controls.TextBox textBox &&
            string.Equals(textBox.Tag?.ToString(), "Clipboard", StringComparison.Ordinal))
        {
            _workingSettings.ClipboardHotkey = definition;
            ClipboardHotkeyBox.Text = definition.ToDisplayString();
        }
        else
        {
            _workingSettings.SaveHotkey = definition;
            SaveHotkeyBox.Text = definition.ToDisplayString();
        }

        Keyboard.ClearFocus();
        StatusText.Text = string.Empty;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择 JPG 截图的保存目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(SaveDirectoryBox.Text)
                ? SaveDirectoryBox.Text
                : KnownFolders.GetDownloadsPath()
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            SaveDirectoryBox.Text = dialog.SelectedPath;
        }
    }

    private void QualitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityValue is not null)
        {
            QualityValue.Text = Math.Round(e.NewValue) + "%";
        }
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        _workingSettings = AppSettings.CreateDefault();
        RenderSettings();
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(102, 112, 133));
        StatusText.Text = "已恢复默认值，点击“保存设置”后生效。";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(209, 67, 67));

        if (_workingSettings.ClipboardHotkey.Equals(_workingSettings.SaveHotkey))
        {
            StatusText.Text = "两种截图操作不能使用相同的快捷键。";
            return;
        }

        if (string.IsNullOrWhiteSpace(SaveDirectoryBox.Text))
        {
            StatusText.Text = "请选择截图保存目录。";
            return;
        }

        try
        {
            var directory = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(SaveDirectoryBox.Text.Trim()));
            Directory.CreateDirectory(directory);
            _workingSettings.SaveDirectory = directory;
        }
        catch (Exception exception)
        {
            StatusText.Text = "保存目录不可用：" + exception.Message;
            return;
        }

        _workingSettings.JpegQuality = (long)Math.Round(QualitySlider.Value);
        if (!_controller.TryApplySettings(_workingSettings, out var error))
        {
            StatusText.Text = error;
            return;
        }

        Hide();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings(_controller.CurrentSettings);
        Hide();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        LoadSettings(_controller.CurrentSettings);
        Hide();
    }
}
