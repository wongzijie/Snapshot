using System.Windows;
using System.Windows.Media;

namespace Snapshot;

public partial class TextResultWindow : Window
{
    private readonly bool _translationRequested;

    public event Action? OpenSettingsRequested;

    public TextResultWindow(bool translationRequested)
    {
        _translationRequested = translationRequested;
        InitializeComponent();

        if (translationRequested)
        {
            Title = "Snapshot OCR 与翻译";
            TitleText.Text = "正在识别并翻译";
            SubtitleText.Text = "图片在本机识别；仅识别出的文字会发送给翻译服务";
            TranslationCard.Visibility = Visibility.Visible;
            TranslationTextBox.Text = "等待 OCR 完成…";
        }
    }

    public void SetRecognition(OcrRecognitionResult result)
    {
        SourceTextBox.Text = string.IsNullOrWhiteSpace(result.Text)
            ? "没有识别到文字。"
            : result.Text;
        LanguageText.Text = $"{result.LanguageName} · {result.LanguageTag}";
        FooterStatusText.Text = string.IsNullOrWhiteSpace(result.Text)
            ? "识别完成，但选区中没有检测到文字。"
            : $"识别完成 · {result.Text.Length:N0} 个字符";

        if (!_translationRequested)
        {
            TitleText.Text = "文字识别完成";
        }
    }

    public void SetTranslationLoading()
    {
        TranslationCard.Visibility = Visibility.Visible;
        TranslationTextBox.Text = "正在翻译…";
        TranslationStatusText.Text = string.Empty;
        OpenSettingsButton.Visibility = Visibility.Collapsed;
        FooterStatusText.Text = "OCR 已完成，正在连接翻译服务…";
    }

    public void SetTranslation(TranslationResult result, string targetLanguageName)
    {
        TranslationCard.Visibility = Visibility.Visible;
        TranslationTextBox.Text = result.Text;
        TargetLanguageText.Text = targetLanguageName;
        TranslationStatusText.Text = string.Empty;
        OpenSettingsButton.Visibility = Visibility.Collapsed;
        TitleText.Text = "识别与翻译完成";
        FooterStatusText.Text = $"翻译完成 · {result.Text.Length:N0} 个字符";
    }

    public void SetError(string message, bool translationError, bool canOpenSettings = false)
    {
        if (translationError)
        {
            TranslationCard.Visibility = Visibility.Visible;
            TranslationTextBox.Text = string.Empty;
            TranslationStatusText.Text = message;
            OpenSettingsButton.Visibility =
                canOpenSettings ? Visibility.Visible : Visibility.Collapsed;
            TitleText.Text = "OCR 已完成，翻译未完成";
        }
        else
        {
            SourceTextBox.Text = string.Empty;
            LanguageText.Text = string.Empty;
            TitleText.Text = "文字识别失败";
        }

        FooterStatusText.Foreground = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(209, 67, 67));
        FooterStatusText.Text = message;
    }

    private void CopySource_Click(object sender, RoutedEventArgs e)
    {
        CopyText(SourceTextBox.Text);
    }

    private void CopyTranslation_Click(object sender, RoutedEventArgs e)
    {
        CopyText(TranslationTextBox.Text);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsRequested?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
            FooterStatusText.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(102, 112, 133));
            FooterStatusText.Text = "文字已复制到剪贴板。";
        }
        catch (Exception exception)
        {
            FooterStatusText.Text = "复制失败：" + exception.Message;
        }
    }
}
