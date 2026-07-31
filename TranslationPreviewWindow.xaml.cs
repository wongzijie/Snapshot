using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapshot;

public partial class TranslationPreviewWindow : Window
{
    private readonly Bitmap _translatedImage;

    public event Action<Bitmap>? CopyImageRequested;
    public event Action<Bitmap>? SaveImageRequested;

    public TranslationPreviewWindow(
        Bitmap translatedImage,
        RegionTranslationResult result)
    {
        _translatedImage = translatedImage;
        InitializeComponent();
        PreviewImage.Source = CreateBitmapSource(translatedImage);

        var languageName = TranslationLanguageOptions.GetDisplayName(
            result.TargetLanguage);
        SubtitleText.Text =
            $"已将 {result.Regions.Count:N0} 个文字区域覆盖翻译为{languageName}";
        StatusText.Text = "译文已按原文字位置回绘；可复制整张译图或保存为 JPG。";
    }

    public void SetStatus(string message, bool isError = false)
    {
        StatusText.Foreground = new SolidColorBrush(
            isError
                ? System.Windows.Media.Color.FromRgb(255, 126, 126)
                : System.Windows.Media.Color.FromRgb(185, 193, 201));
        StatusText.Text = message;
    }

    protected override void OnClosed(EventArgs e)
    {
        _translatedImage.Dispose();
        base.OnClosed(e);
    }

    private static BitmapSource CreateBitmapSource(Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        memory.Position = 0;
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.StreamSource = memory;
        source.EndInit();
        source.Freeze();
        return source;
    }

    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        CopyImageRequested?.Invoke(_translatedImage);
    }

    private void SaveImage_Click(object sender, RoutedEventArgs e)
    {
        SaveImageRequested?.Invoke(_translatedImage);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
