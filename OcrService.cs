using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Snapshot;

public sealed record OcrLanguageOption(
    string LanguageTag,
    string DisplayName);

public sealed record OcrRecognitionResult(
    string Text,
    string LanguageTag,
    string LanguageName,
    IReadOnlyList<OcrTextRegion> Regions);

public sealed record OcrTextRegion(
    string Text,
    RectangleF Bounds);

public sealed class OcrService
{
    public IReadOnlyList<OcrLanguageOption> GetAvailableLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(language => new OcrLanguageOption(
                    language.LanguageTag,
                    string.IsNullOrWhiteSpace(language.NativeName)
                        ? language.DisplayName
                        : language.NativeName))
                .OrderBy(language => language.DisplayName, StringComparer.CurrentCulture)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public async Task<OcrRecognitionResult> RecognizeAsync(
        Bitmap image,
        string? preferredLanguageTag,
        CancellationToken cancellationToken = default)
    {
        var engine = CreateEngine(preferredLanguageTag);
        if (engine is null)
        {
            throw new InvalidOperationException(
                "Windows 没有可用的 OCR 语言。请在“设置 → 时间和语言 → 语言和区域”中安装至少一个语言包。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var prepared = PrepareImage(image, OcrEngine.MaxImageDimension);
        using var memory = new MemoryStream();
        prepared.Save(memory, ImageFormat.Png);
        var bytes = memory.ToArray();

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(softwareBitmap);
        cancellationToken.ThrowIfCancellationRequested();

        var scaleX = image.Width / (float)prepared.Width;
        var scaleY = image.Height / (float)prepared.Height;
        var regions = result.Lines
            .Select(line => CreateRegion(line, scaleX, scaleY))
            .Where(region => region is not null)
            .Select(region => region!)
            .ToArray();
        var language = engine.RecognizerLanguage;
        return new OcrRecognitionResult(
            result.Text?.Trim() ?? string.Empty,
            language.LanguageTag,
            string.IsNullOrWhiteSpace(language.NativeName)
                ? language.DisplayName
                : language.NativeName,
            regions);
    }

    private static OcrTextRegion? CreateRegion(
        OcrLine line,
        float scaleX,
        float scaleY)
    {
        if (string.IsNullOrWhiteSpace(line.Text) || line.Words.Count == 0)
        {
            return null;
        }

        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        foreach (var word in line.Words)
        {
            var bounds = word.BoundingRect;
            left = Math.Min(left, bounds.X);
            top = Math.Min(top, bounds.Y);
            right = Math.Max(right, bounds.X + bounds.Width);
            bottom = Math.Max(bottom, bounds.Y + bounds.Height);
        }

        if (right <= left || bottom <= top)
        {
            return null;
        }

        return new OcrTextRegion(
            line.Text.Trim(),
            new RectangleF(
                (float)left * scaleX,
                (float)top * scaleY,
                (float)(right - left) * scaleX,
                (float)(bottom - top) * scaleY));
    }

    private static OcrEngine? CreateEngine(string? preferredLanguageTag)
    {
        if (!string.IsNullOrWhiteSpace(preferredLanguageTag))
        {
            try
            {
                var language = new Language(preferredLanguageTag);
                if (OcrEngine.IsLanguageSupported(language))
                {
                    return OcrEngine.TryCreateFromLanguage(language);
                }
            }
            catch
            {
                // A language pack can be removed after Snapshot has saved the setting.
            }
        }

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static Bitmap PrepareImage(Bitmap source, uint maximumDimension)
    {
        var largest = Math.Max(source.Width, source.Height);
        if (largest <= maximumDimension)
        {
            return new Bitmap(source);
        }

        var scale = maximumDimension / (double)largest;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(result);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, width, height),
            new Rectangle(0, 0, source.Width, source.Height),
            GraphicsUnit.Pixel);
        return result;
    }
}
