using System.Drawing;
using System.Drawing.Drawing2D;

namespace Snapshot;

internal enum AnnotationTool
{
    None,
    Pen,
    Arrow,
    Text,
    Mosaic
}

internal sealed class AnnotationDocument
{
    private readonly List<AnnotationElement> _elements = [];
    private readonly Stack<AnnotationChange> _undo = [];
    private readonly Stack<AnnotationChange> _redo = [];

    public bool IsEmpty => _elements.Count == 0;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool ContainsMosaic => _elements.Any(element => element is MosaicAnnotation);

    public void Add(AnnotationElement element)
    {
        var index = _elements.Count;
        _elements.Add(element);
        _undo.Push(new AddAnnotationChange(index, element));
        _redo.Clear();
    }

    public void ReplaceText(TextAnnotation original, TextAnnotation replacement)
    {
        var index = _elements.IndexOf(original);
        if (index < 0)
        {
            Add(replacement);
            return;
        }

        _elements[index] = replacement;
        _undo.Push(new ReplaceAnnotationChange(index, original, replacement));
        _redo.Clear();
    }

    public TextAnnotation? HitTestText(PointF point)
    {
        for (var index = _elements.Count - 1; index >= 0; index--)
        {
            if (_elements[index] is TextAnnotation text &&
                RectangleF.Inflate(text.Bounds, 6, 6).Contains(point))
            {
                return text;
            }
        }

        return null;
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var change = _undo.Pop();
        change.Undo(_elements);
        _redo.Push(change);
    }

    public void Redo()
    {
        if (_redo.Count > 0)
        {
            var change = _redo.Pop();
            change.Redo(_elements);
            _undo.Push(change);
        }
    }

    public void Clear()
    {
        _elements.Clear();
        _undo.Clear();
        _redo.Clear();
    }

    public void Draw(
        Graphics graphics,
        Bitmap? mosaicBitmap,
        AnnotationElement? excludedElement = null)
    {
        foreach (var element in _elements)
        {
            if (!ReferenceEquals(element, excludedElement))
            {
                element.Draw(graphics, mosaicBitmap);
            }
        }
    }

    private abstract record AnnotationChange
    {
        public abstract void Undo(List<AnnotationElement> elements);
        public abstract void Redo(List<AnnotationElement> elements);
    }

    private sealed record AddAnnotationChange(
        int Index,
        AnnotationElement Element) : AnnotationChange
    {
        public override void Undo(List<AnnotationElement> elements)
        {
            elements.Remove(Element);
        }

        public override void Redo(List<AnnotationElement> elements)
        {
            elements.Insert(Math.Min(Index, elements.Count), Element);
        }
    }

    private sealed record ReplaceAnnotationChange(
        int Index,
        AnnotationElement Before,
        AnnotationElement After) : AnnotationChange
    {
        public override void Undo(List<AnnotationElement> elements)
        {
            if (Index < elements.Count)
            {
                elements[Index] = Before;
            }
        }

        public override void Redo(List<AnnotationElement> elements)
        {
            if (Index < elements.Count)
            {
                elements[Index] = After;
            }
        }
    }
}

internal abstract class AnnotationElement
{
    public abstract void Draw(Graphics graphics, Bitmap? mosaicBitmap);
}

internal sealed class PenAnnotation(
    IEnumerable<PointF> points,
    Color color,
    float width) : AnnotationElement
{
    private readonly PointF[] _points = points.ToArray();

    public override void Draw(Graphics graphics, Bitmap? mosaicBitmap)
    {
        using var pen = new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        if (_points.Length == 1)
        {
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(
                brush,
                _points[0].X - width / 2,
                _points[0].Y - width / 2,
                width,
                width);
        }
        else if (_points.Length > 1)
        {
            graphics.DrawLines(pen, _points);
        }
    }
}

internal sealed class ArrowAnnotation(
    PointF start,
    PointF end,
    Color color,
    float width) : AnnotationElement
{
    public override void Draw(Graphics graphics, Bitmap? mosaicBitmap)
    {
        using var pen = new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLine(pen, start, end);

        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var headLength = Math.Max(12f, width * 4.2f);
        var spread = Math.PI / 7;
        var first = new PointF(
            end.X - headLength * (float)Math.Cos(angle - spread),
            end.Y - headLength * (float)Math.Sin(angle - spread));
        var second = new PointF(
            end.X - headLength * (float)Math.Cos(angle + spread),
            end.Y - headLength * (float)Math.Sin(angle + spread));

        using var brush = new SolidBrush(color);
        graphics.FillPolygon(brush, [end, first, second]);
    }
}

internal sealed class TextAnnotation(
    PointF location,
    string text,
    Color color,
    string fontFamily,
    float fontSize,
    FontStyle fontStyle,
    SizeF layoutSize) : AnnotationElement
{
    public PointF Location => location;
    public string Text => text;
    public Color Color => color;
    public string FontFamily => fontFamily;
    public float FontSize => fontSize;
    public FontStyle FontStyle => fontStyle;
    public SizeF LayoutSize => layoutSize;
    public RectangleF Bounds => new(location, layoutSize);

    public override void Draw(Graphics graphics, Bitmap? mosaicBitmap)
    {
        using var font = new Font(
            fontFamily,
            fontSize,
            fontStyle,
            GraphicsUnit.Point);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.LineLimit
        };
        graphics.DrawString(
            text,
            font,
            brush,
            new RectangleF(location, layoutSize),
            format);
    }
}

internal sealed class MosaicAnnotation(
    IEnumerable<PointF> points,
    float brushWidth) : AnnotationElement
{
    private readonly PointF[] _points = points.ToArray();

    public override void Draw(Graphics graphics, Bitmap? mosaicBitmap)
    {
        if (mosaicBitmap is null || _points.Length == 0)
        {
            return;
        }

        using var path = new GraphicsPath();
        if (_points.Length == 1)
        {
            path.AddEllipse(
                _points[0].X - brushWidth / 2,
                _points[0].Y - brushWidth / 2,
                brushWidth,
                brushWidth);
        }
        else
        {
            path.AddLines(_points);
            using var widenPen = new Pen(Color.Black, brushWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            path.Widen(widenPen);
        }

        var state = graphics.Save();
        try
        {
            graphics.SetClip(path, CombineMode.Intersect);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImageUnscaled(mosaicBitmap, 0, 0);
        }
        finally
        {
            graphics.Restore(state);
        }
    }
}

internal static class MosaicBitmapFactory
{
    public static Bitmap Create(Bitmap source, int blockSize = 12)
    {
        var smallWidth = Math.Max(1, (int)Math.Ceiling(source.Width / (double)blockSize));
        var smallHeight = Math.Max(1, (int)Math.Ceiling(source.Height / (double)blockSize));

        using var small = new Bitmap(smallWidth, smallHeight);
        using (var smallGraphics = Graphics.FromImage(small))
        {
            smallGraphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            smallGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            smallGraphics.DrawImage(
                source,
                new Rectangle(0, 0, smallWidth, smallHeight),
                new Rectangle(0, 0, source.Width, source.Height),
                GraphicsUnit.Pixel);
        }

        var result = new Bitmap(source.Width, source.Height);
        using (var graphics = Graphics.FromImage(result))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(
                small,
                new Rectangle(0, 0, result.Width, result.Height),
                new Rectangle(0, 0, small.Width, small.Height),
                GraphicsUnit.Pixel);
        }

        return result;
    }
}
