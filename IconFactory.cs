using System.Drawing;
using System.Drawing.Drawing2D;

namespace Snapshot;

internal static class IconFactory
{
    public static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(7, 193, 96));
            graphics.FillRoundedRectangle(background, new Rectangle(1, 1, 30, 30), 7);

            using var pen = new Pen(Color.White, 2.4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            graphics.DrawLine(pen, 8, 13, 8, 8);
            graphics.DrawLine(pen, 8, 8, 13, 8);
            graphics.DrawLine(pen, 19, 8, 24, 8);
            graphics.DrawLine(pen, 24, 8, 24, 13);
            graphics.DrawLine(pen, 8, 19, 8, 24);
            graphics.DrawLine(pen, 8, 24, 13, 24);
            graphics.DrawLine(pen, 19, 24, 24, 24);
            graphics.DrawLine(pen, 24, 19, 24, 24);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static void FillRoundedRectangle(
        this Graphics graphics,
        Brush brush,
        Rectangle rectangle,
        int radius)
    {
        var diameter = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
