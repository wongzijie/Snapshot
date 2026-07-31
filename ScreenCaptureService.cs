using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Snapshot;

public sealed class DesktopSnapshot : IDisposable
{
    private readonly IReadOnlyList<Rectangle> _windowBounds;

    public Bitmap Image { get; }
    public Rectangle VirtualBounds { get; }

    public DesktopSnapshot(Bitmap image, Rectangle virtualBounds, IReadOnlyList<Rectangle> windowBounds)
    {
        Image = image;
        VirtualBounds = virtualBounds;
        _windowBounds = windowBounds;
    }

    public Rectangle FindTargetAt(Point screenPoint)
    {
        foreach (var bounds in _windowBounds)
        {
            if (bounds.Contains(screenPoint))
            {
                return bounds;
            }
        }

        foreach (var screen in Screen.AllScreens)
        {
            if (screen.Bounds.Contains(screenPoint))
            {
                return screen.Bounds;
            }
        }

        return Rectangle.Empty;
    }

    public void Dispose() => Image.Dispose();
}

public sealed class ScreenCaptureService
{
    public DesktopSnapshot CaptureDesktop()
    {
        var virtualBounds = SystemInformation.VirtualScreen;
        if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
        {
            throw new InvalidOperationException("没有检测到可截图的显示器。");
        }

        var bitmap = new Bitmap(
            virtualBounds.Width,
            virtualBounds.Height,
            PixelFormat.Format32bppPArgb);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                virtualBounds.Left,
                virtualBounds.Top,
                0,
                0,
                virtualBounds.Size,
                CopyPixelOperation.SourceCopy);
        }

        var windowBounds = EnumerateSelectableWindows(virtualBounds);
        AppLog.Write($"Detected {windowBounds.Count} selectable windows.");
        return new DesktopSnapshot(bitmap, virtualBounds, windowBounds);
    }

    private static IReadOnlyList<Rectangle> EnumerateSelectableWindows(Rectangle virtualBounds)
    {
        var results = new List<Rectangle>();
        var shellWindow = NativeMethods.GetShellWindow();

        NativeMethods.EnumWindows((window, _) =>
        {
            if (window == shellWindow ||
                !NativeMethods.IsWindowVisible(window) ||
                NativeMethods.IsIconic(window) ||
                NativeMethods.GetWindowTextLength(window) == 0)
            {
                return true;
            }

            if (NativeMethods.DwmGetWindowAttribute(
                    window,
                    NativeMethods.DwmwaCloaked,
                    out int cloaked,
                    sizeof(int)) == 0 &&
                cloaked != 0)
            {
                return true;
            }

            var extendedStyle = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64();
            if ((extendedStyle & (NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate)) != 0)
            {
                return true;
            }

            NativeMethods.Rect nativeRect;
            var gotDwmBounds = NativeMethods.DwmGetWindowAttribute(
                window,
                NativeMethods.DwmwaExtendedFrameBounds,
                out nativeRect,
                Marshal.SizeOf<NativeMethods.Rect>()) == 0;

            if (!gotDwmBounds && !NativeMethods.GetWindowRect(window, out nativeRect))
            {
                return true;
            }

            var rectangle = Rectangle.Intersect(nativeRect.ToRectangle(), virtualBounds);
            if (rectangle.Width >= 32 && rectangle.Height >= 32 && !results.Contains(rectangle))
            {
                results.Add(rectangle);
            }

            return true;
        }, IntPtr.Zero);

        return results;
    }
}
