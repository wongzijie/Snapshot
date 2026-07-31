using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Snapshot;

public static class TranslationImageComposer
{
    private const string PreferredFontFamily = "Microsoft YaHei UI";

    public static Bitmap Compose(
        Bitmap source,
        IReadOnlyList<TranslatedTextRegion> translatedRegions)
    {
        var result = new Bitmap(
            source.Width,
            source.Height,
            PixelFormat.Format24bppRgb);
        if (source.HorizontalResolution > 0 && source.VerticalResolution > 0)
        {
            result.SetResolution(
                source.HorizontalResolution,
                source.VerticalResolution);
        }

        using var graphics = Graphics.FromImage(result);
        graphics.DrawImageUnscaled(source, 0, 0);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var layers = translatedRegions
            .Where(region =>
                !string.IsNullOrWhiteSpace(region.TranslatedText) &&
                region.Bounds.Width > 0 &&
                region.Bounds.Height > 0)
            .Select(region =>
            {
                var target = CreateTargetRectangle(region.Bounds, source.Size);
                return new TranslationLayer(
                    region.TranslatedText.Trim(),
                    target,
                    FindBackgroundColor(source, region.Bounds));
            })
            .Where(layer => layer.Bounds.Width >= 2 && layer.Bounds.Height >= 2)
            .ToArray();

        foreach (var layer in layers)
        {
            using var background = new SolidBrush(layer.Background);
            graphics.FillRectangle(background, layer.Bounds);
        }

        foreach (var layer in layers)
        {
            DrawTranslatedText(graphics, layer);
        }

        return result;
    }

    private static RectangleF CreateTargetRectangle(RectangleF source, Size imageSize)
    {
        var horizontalPadding = Math.Clamp(source.Height * 0.12f, 1.5f, 5f);
        var verticalPadding = Math.Clamp(source.Height * 0.08f, 1f, 3f);
        var left = Math.Max(0, source.Left - horizontalPadding);
        var top = Math.Max(0, source.Top - verticalPadding);
        var right = Math.Min(imageSize.Width, source.Right + horizontalPadding);
        var bottom = Math.Min(imageSize.Height, source.Bottom + verticalPadding);
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static void DrawTranslatedText(Graphics graphics, TranslationLayer layer)
    {
        var foreground = GetContrastingColor(layer.Background);
        using var brush = new SolidBrush(foreground);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.LineLimit
        };
        using var font = CreateFittedFont(
            graphics,
            layer.Text,
            layer.Bounds.Size,
            format);
        graphics.DrawString(layer.Text, font, brush, layer.Bounds, format);
    }

    private static Font CreateFittedFont(
        Graphics graphics,
        string text,
        SizeF available,
        StringFormat format)
    {
        var size = Math.Clamp(available.Height * 0.86f, 7f, 72f);
        while (size > 7f)
        {
            var candidate = new Font(
                PreferredFontFamily,
                size,
                FontStyle.Regular,
                GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(
                text,
                candidate,
                new SizeF(Math.Max(1, available.Width), 10000),
                format);
            if (measured.Width <= available.Width + 1 &&
                measured.Height <= available.Height + 1)
            {
                return candidate;
            }

            candidate.Dispose();
            size -= Math.Max(0.5f, size * 0.08f);
        }

        return new Font(
            PreferredFontFamily,
            7f,
            FontStyle.Regular,
            GraphicsUnit.Pixel);
    }

    private static Color FindBackgroundColor(Bitmap source, RectangleF textBounds)
    {
        var expansion = Math.Clamp(textBounds.Height * 0.45f, 3f, 12f);
        var outer = Rectangle.Intersect(
            Rectangle.FromLTRB(
                (int)Math.Floor(textBounds.Left - expansion),
                (int)Math.Floor(textBounds.Top - expansion),
                (int)Math.Ceiling(textBounds.Right + expansion),
                (int)Math.Ceiling(textBounds.Bottom + expansion)),
            new Rectangle(0, 0, source.Width, source.Height));
        var inner = Rectangle.Intersect(
            Rectangle.FromLTRB(
                (int)Math.Floor(textBounds.Left),
                (int)Math.Floor(textBounds.Top),
                (int)Math.Ceiling(textBounds.Right),
                (int)Math.Ceiling(textBounds.Bottom)),
            new Rectangle(0, 0, source.Width, source.Height));
        var buckets = new Dictionary<int, ColorBucket>();
        var step = Math.Max(1, Math.Min(outer.Width, outer.Height) / 24);

        for (var y = outer.Top; y < outer.Bottom; y += step)
        {
            for (var x = outer.Left; x < outer.Right; x += step)
            {
                if (!inner.Contains(x, y))
                {
                    AddColorSample(buckets, source.GetPixel(x, y));
                }
            }
        }

        if (buckets.Count == 0)
        {
            for (var y = inner.Top; y < inner.Bottom; y += step)
            {
                for (var x = inner.Left; x < inner.Right; x += step)
                {
                    AddColorSample(buckets, source.GetPixel(x, y));
                }
            }
        }

        var bucket = buckets.Values
            .OrderByDescending(candidate => candidate.Count)
            .FirstOrDefault();
        return bucket is null || bucket.Count == 0
            ? Color.White
            : Color.FromArgb(
                bucket.Red / bucket.Count,
                bucket.Green / bucket.Count,
                bucket.Blue / bucket.Count);
    }

    private static void AddColorSample(
        Dictionary<int, ColorBucket> buckets,
        Color color)
    {
        var key = (color.R / 24 << 16) |
                  (color.G / 24 << 8) |
                  color.B / 24;
        if (!buckets.TryGetValue(key, out var bucket))
        {
            bucket = new ColorBucket();
            buckets[key] = bucket;
        }

        bucket.Count++;
        bucket.Red += color.R;
        bucket.Green += color.G;
        bucket.Blue += color.B;
    }

    private static Color GetContrastingColor(Color background)
    {
        var luminance =
            (0.2126 * background.R +
             0.7152 * background.G +
             0.0722 * background.B) / 255;
        return luminance > 0.53 ? Color.FromArgb(24, 27, 31) : Color.White;
    }

    private sealed record TranslationLayer(
        string Text,
        RectangleF Bounds,
        Color Background);

    private sealed class ColorBucket
    {
        public int Count { get; set; }
        public int Red { get; set; }
        public int Green { get; set; }
        public int Blue { get; set; }
    }
}
