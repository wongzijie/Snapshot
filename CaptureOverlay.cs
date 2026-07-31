using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Snapshot;

public sealed class CaptureOverlay : Form
{
    private const int ToolbarWidth = 310;
    private const int ToolbarHeight = 46;
    private const int GripSize = 7;
    private static readonly Color Accent = Color.FromArgb(7, 193, 96);

    private readonly DesktopSnapshot _snapshot;
    private readonly CaptureMode _mode;
    private readonly Font _uiFont = new("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _smallFont = new("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _buttonFont = new("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);

    private Rectangle _hovered;
    private Rectangle _selection;
    private Rectangle _toolbarBounds;
    private Rectangle _selectionAtPointerDown;
    private Point _pointerDownScreen;
    private bool _creating;
    private bool _locked;
    private AdjustMode _adjustMode;
    private bool _completionRaised;

    public event Action<Bitmap?>? CaptureCompleted;

    public CaptureOverlay(DesktopSnapshot snapshot, CaptureMode mode, bool showInTaskbar = false)
    {
        _snapshot = snapshot;
        _mode = mode;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = snapshot.VirtualBounds;
        Text = "Snapshot Capture Overlay";
        TopMost = true;
        ShowInTaskbar = showInTaskbar;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);

        var pointer = System.Windows.Forms.Cursor.Position;
        _hovered = _snapshot.FindTargetAt(pointer);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Focus();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;

        graphics.DrawImageUnscaled(_snapshot.Image, 0, 0);
        using (var shade = new SolidBrush(Color.FromArgb(118, 0, 0, 0)))
        {
            graphics.FillRectangle(shade, ClientRectangle);
        }

        var target = _creating || _locked ? _selection : _hovered;
        if (!target.IsEmpty)
        {
            DrawSelection(graphics, target, _locked);
        }

        DrawModeBadge(graphics);
        DrawHelp(graphics);

        if (_locked && !_selection.IsEmpty)
        {
            DrawToolbar(graphics);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Right)
        {
            CancelCapture();
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_locked)
        {
            if (_toolbarBounds.Contains(e.Location))
            {
                HandleToolbarClick(e.Location);
                return;
            }

            var screenPoint = ClientToScreen(e.Location);
            var hit = HitTestSelection(screenPoint);
            if (hit != AdjustMode.None)
            {
                _adjustMode = hit;
                _pointerDownScreen = screenPoint;
                _selectionAtPointerDown = _selection;
                Capture = true;
                return;
            }

            BeginNewSelection(screenPoint);
            return;
        }

        BeginNewSelection(ClientToScreen(e.Location));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var screenPoint = ClientToScreen(e.Location);

        if (_creating)
        {
            _selection = NormalizeRectangle(_pointerDownScreen, screenPoint);
            _selection = Rectangle.Intersect(_selection, _snapshot.VirtualBounds);
            Invalidate();
            return;
        }

        if (_adjustMode != AdjustMode.None)
        {
            _selection = AdjustSelection(screenPoint);
            Invalidate();
            return;
        }

        if (_locked)
        {
            Cursor = CursorFor(HitTestSelection(screenPoint));
            return;
        }

        var nextHovered = _snapshot.FindTargetAt(screenPoint);
        if (nextHovered != _hovered)
        {
            _hovered = nextHovered;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_creating)
        {
            _creating = false;
            Capture = false;

            if (_selection.Width < 5 && _selection.Height < 5)
            {
                _selection = _snapshot.FindTargetAt(ClientToScreen(e.Location));
            }

            _selection = Rectangle.Intersect(_selection, _snapshot.VirtualBounds);
            if (_selection.Width >= 2 && _selection.Height >= 2)
            {
                _locked = true;
                Cursor = Cursors.Default;
            }
            else
            {
                ResetSelection();
            }

            Invalidate();
            return;
        }

        if (_adjustMode != AdjustMode.None)
        {
            _adjustMode = AdjustMode.None;
            Capture = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_locked && _selection.Contains(ClientToScreen(e.Location)))
        {
            ConfirmCapture();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            ConfirmCapture();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            CancelCapture();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.R)
        {
            ResetSelection();
            e.Handled = true;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uiFont.Dispose();
        _smallFont.Dispose();
        _buttonFont.Dispose();
        _snapshot.Dispose();
        base.OnFormClosed(e);
    }

    private void BeginNewSelection(Point screenPoint)
    {
        _locked = false;
        _creating = true;
        _adjustMode = AdjustMode.None;
        _pointerDownScreen = screenPoint;
        _selection = Rectangle.Empty;
        Cursor = Cursors.Cross;
        Capture = true;
        Invalidate();
    }

    private void ResetSelection()
    {
        _locked = false;
        _creating = false;
        _adjustMode = AdjustMode.None;
        _selection = Rectangle.Empty;
        _hovered = _snapshot.FindTargetAt(System.Windows.Forms.Cursor.Position);
        Cursor = Cursors.Cross;
        Capture = false;
        Invalidate();
    }

    private void ConfirmCapture()
    {
        if (_completionRaised || !_locked || _selection.Width < 2 || _selection.Height < 2)
        {
            return;
        }

        _completionRaised = true;
        var source = new Rectangle(
            _selection.Left - _snapshot.VirtualBounds.Left,
            _selection.Top - _snapshot.VirtualBounds.Top,
            _selection.Width,
            _selection.Height);

        var result = new Bitmap(_selection.Width, _selection.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(result))
        {
            graphics.DrawImage(
                _snapshot.Image,
                new Rectangle(0, 0, result.Width, result.Height),
                source,
                GraphicsUnit.Pixel);
        }

        Hide();
        CaptureCompleted?.Invoke(result);
        Close();
    }

    private void CancelCapture()
    {
        if (_completionRaised)
        {
            return;
        }

        _completionRaised = true;
        Hide();
        CaptureCompleted?.Invoke(null);
        Close();
    }

    private void DrawSelection(Graphics graphics, Rectangle screenRectangle, bool drawHandles)
    {
        var local = ScreenToClient(screenRectangle);
        var source = new Rectangle(
            screenRectangle.Left - _snapshot.VirtualBounds.Left,
            screenRectangle.Top - _snapshot.VirtualBounds.Top,
            screenRectangle.Width,
            screenRectangle.Height);

        graphics.DrawImage(_snapshot.Image, local, source, GraphicsUnit.Pixel);
        using var outline = new Pen(Accent, 2f) { Alignment = PenAlignment.Inset };
        graphics.DrawRectangle(outline, local);

        var sizeText = $"{screenRectangle.Width} × {screenRectangle.Height}";
        var size = graphics.MeasureString(sizeText, _smallFont);
        var label = new RectangleF(
            local.Left,
            Math.Max(6, local.Top - size.Height - 7),
            size.Width + 14,
            size.Height + 4);
        using var labelBackground = new SolidBrush(Color.FromArgb(220, 27, 31, 36));
        FillRoundedRectangle(graphics, labelBackground, label, 5);
        graphics.DrawString(sizeText, _smallFont, Brushes.White, label.Left + 7, label.Top + 2);

        if (drawHandles)
        {
            foreach (var handle in GetHandleRectangles(local))
            {
                using var fill = new SolidBrush(Color.White);
                graphics.FillRectangle(fill, handle);
                using var border = new Pen(Accent, 1f);
                graphics.DrawRectangle(border, handle);
            }
        }
    }

    private void DrawModeBadge(Graphics graphics)
    {
        var text = _mode == CaptureMode.Clipboard
            ? "截图后复制到剪贴板"
            : "截图后保存 JPG";
        var measured = graphics.MeasureString(text, _uiFont);
        var badge = new RectangleF(18, 18, measured.Width + 36, 34);
        using var background = new SolidBrush(Color.FromArgb(225, 23, 27, 32));
        FillRoundedRectangle(graphics, background, badge, 8);
        using var dot = new SolidBrush(Accent);
        graphics.FillEllipse(dot, badge.Left + 12, badge.Top + 13, 8, 8);
        graphics.DrawString(text, _uiFont, Brushes.White, badge.Left + 26, badge.Top + 7);
    }

    private void DrawHelp(Graphics graphics)
    {
        var text = _locked
            ? "拖动选区可移动 · 拖动边缘可缩放 · 双击或 Enter 完成 · Esc 取消"
            : "单击选择窗口，或按住鼠标拖出自定义区域 · Esc 取消";
        var measured = graphics.MeasureString(text, _smallFont);
        var panel = new RectangleF(
            Math.Max(12, (ClientSize.Width - measured.Width - 24) / 2f),
            ClientSize.Height - measured.Height - 24,
            measured.Width + 24,
            measured.Height + 10);
        using var background = new SolidBrush(Color.FromArgb(205, 23, 27, 32));
        FillRoundedRectangle(graphics, background, panel, 7);
        graphics.DrawString(text, _smallFont, Brushes.White, panel.Left + 12, panel.Top + 5);
    }

    private void DrawToolbar(Graphics graphics)
    {
        _toolbarBounds = CalculateToolbarBounds();
        using var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
        var shadowBounds = _toolbarBounds;
        shadowBounds.Offset(0, 3);
        FillRoundedRectangle(graphics, shadow, shadowBounds, 9);

        using var background = new SolidBrush(Color.FromArgb(242, 30, 34, 39));
        FillRoundedRectangle(graphics, background, _toolbarBounds, 9);

        var reset = new Rectangle(_toolbarBounds.Left, _toolbarBounds.Top, 82, ToolbarHeight);
        var cancel = new Rectangle(reset.Right, _toolbarBounds.Top, 82, ToolbarHeight);
        var done = new Rectangle(cancel.Right, _toolbarBounds.Top, ToolbarWidth - 164, ToolbarHeight);

        using (var separator = new Pen(Color.FromArgb(65, 255, 255, 255), 1f))
        {
            graphics.DrawLine(separator, reset.Right, reset.Top + 9, reset.Right, reset.Bottom - 9);
            graphics.DrawLine(separator, cancel.Right, cancel.Top + 9, cancel.Right, cancel.Bottom - 9);
        }

        using (var doneBrush = new SolidBrush(Accent))
        {
            var doneFill = Rectangle.Inflate(done, -5, -5);
            FillRoundedRectangle(graphics, doneBrush, doneFill, 7);
        }

        DrawCenteredText(graphics, "重选  R", _smallFont, Brushes.White, reset);
        DrawCenteredText(graphics, "取消  Esc", _smallFont, Brushes.White, cancel);
        DrawCenteredText(
            graphics,
            _mode == CaptureMode.Clipboard ? "复制  Enter" : "保存 JPG  Enter",
            _buttonFont,
            Brushes.White,
            done);
    }

    private Rectangle CalculateToolbarBounds()
    {
        var localSelection = ScreenToClient(_selection);
        var x = Math.Clamp(localSelection.Right - ToolbarWidth, 8, Math.Max(8, ClientSize.Width - ToolbarWidth - 8));
        var below = localSelection.Bottom + 10;
        var y = below + ToolbarHeight <= ClientSize.Height - 8
            ? below
            : localSelection.Top - ToolbarHeight - 10;
        y = Math.Clamp(y, 8, Math.Max(8, ClientSize.Height - ToolbarHeight - 8));
        return new Rectangle(x, y, ToolbarWidth, ToolbarHeight);
    }

    private void HandleToolbarClick(Point point)
    {
        var relativeX = point.X - _toolbarBounds.Left;
        if (relativeX < 82)
        {
            ResetSelection();
        }
        else if (relativeX < 164)
        {
            CancelCapture();
        }
        else
        {
            ConfirmCapture();
        }
    }

    private AdjustMode HitTestSelection(Point screenPoint)
    {
        if (!_locked || _selection.IsEmpty)
        {
            return AdjustMode.None;
        }

        var nearLeft = Math.Abs(screenPoint.X - _selection.Left) <= GripSize;
        var nearRight = Math.Abs(screenPoint.X - _selection.Right) <= GripSize;
        var nearTop = Math.Abs(screenPoint.Y - _selection.Top) <= GripSize;
        var nearBottom = Math.Abs(screenPoint.Y - _selection.Bottom) <= GripSize;
        var inHorizontalSpan = screenPoint.X >= _selection.Left - GripSize &&
                               screenPoint.X <= _selection.Right + GripSize;
        var inVerticalSpan = screenPoint.Y >= _selection.Top - GripSize &&
                             screenPoint.Y <= _selection.Bottom + GripSize;

        if (nearLeft && nearTop) return AdjustMode.NorthWest;
        if (nearRight && nearTop) return AdjustMode.NorthEast;
        if (nearLeft && nearBottom) return AdjustMode.SouthWest;
        if (nearRight && nearBottom) return AdjustMode.SouthEast;
        if (nearTop && inHorizontalSpan) return AdjustMode.North;
        if (nearBottom && inHorizontalSpan) return AdjustMode.South;
        if (nearLeft && inVerticalSpan) return AdjustMode.West;
        if (nearRight && inVerticalSpan) return AdjustMode.East;
        if (_selection.Contains(screenPoint)) return AdjustMode.Move;
        return AdjustMode.None;
    }

    private Rectangle AdjustSelection(Point current)
    {
        var left = _selectionAtPointerDown.Left;
        var top = _selectionAtPointerDown.Top;
        var right = _selectionAtPointerDown.Right;
        var bottom = _selectionAtPointerDown.Bottom;
        var dx = current.X - _pointerDownScreen.X;
        var dy = current.Y - _pointerDownScreen.Y;

        if (_adjustMode == AdjustMode.Move)
        {
            var moved = _selectionAtPointerDown;
            moved.Offset(dx, dy);
            if (moved.Left < _snapshot.VirtualBounds.Left)
                moved.X = _snapshot.VirtualBounds.Left;
            if (moved.Top < _snapshot.VirtualBounds.Top)
                moved.Y = _snapshot.VirtualBounds.Top;
            if (moved.Right > _snapshot.VirtualBounds.Right)
                moved.X = _snapshot.VirtualBounds.Right - moved.Width;
            if (moved.Bottom > _snapshot.VirtualBounds.Bottom)
                moved.Y = _snapshot.VirtualBounds.Bottom - moved.Height;
            return moved;
        }

        if (_adjustMode is AdjustMode.West or AdjustMode.NorthWest or AdjustMode.SouthWest)
            left = Math.Clamp(current.X, _snapshot.VirtualBounds.Left, right - 2);
        if (_adjustMode is AdjustMode.East or AdjustMode.NorthEast or AdjustMode.SouthEast)
            right = Math.Clamp(current.X, left + 2, _snapshot.VirtualBounds.Right);
        if (_adjustMode is AdjustMode.North or AdjustMode.NorthWest or AdjustMode.NorthEast)
            top = Math.Clamp(current.Y, _snapshot.VirtualBounds.Top, bottom - 2);
        if (_adjustMode is AdjustMode.South or AdjustMode.SouthWest or AdjustMode.SouthEast)
            bottom = Math.Clamp(current.Y, top + 2, _snapshot.VirtualBounds.Bottom);

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Rectangle NormalizeRectangle(Point first, Point second) =>
        Rectangle.FromLTRB(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Max(first.X, second.X),
            Math.Max(first.Y, second.Y));

    private Point ClientToScreen(Point clientPoint) =>
        new(clientPoint.X + _snapshot.VirtualBounds.Left, clientPoint.Y + _snapshot.VirtualBounds.Top);

    private Rectangle ScreenToClient(Rectangle screenRectangle) =>
        new(
            screenRectangle.Left - _snapshot.VirtualBounds.Left,
            screenRectangle.Top - _snapshot.VirtualBounds.Top,
            screenRectangle.Width,
            screenRectangle.Height);

    private static Cursor CursorFor(AdjustMode mode) => mode switch
    {
        AdjustMode.Move => Cursors.SizeAll,
        AdjustMode.North or AdjustMode.South => Cursors.SizeNS,
        AdjustMode.East or AdjustMode.West => Cursors.SizeWE,
        AdjustMode.NorthWest or AdjustMode.SouthEast => Cursors.SizeNWSE,
        AdjustMode.NorthEast or AdjustMode.SouthWest => Cursors.SizeNESW,
        _ => Cursors.Default
    };

    private static IEnumerable<Rectangle> GetHandleRectangles(Rectangle rectangle)
    {
        const int size = 7;
        var half = size / 2;
        var centerX = rectangle.Left + rectangle.Width / 2;
        var centerY = rectangle.Top + rectangle.Height / 2;

        yield return new Rectangle(rectangle.Left - half, rectangle.Top - half, size, size);
        yield return new Rectangle(centerX - half, rectangle.Top - half, size, size);
        yield return new Rectangle(rectangle.Right - half, rectangle.Top - half, size, size);
        yield return new Rectangle(rectangle.Left - half, centerY - half, size, size);
        yield return new Rectangle(rectangle.Right - half, centerY - half, size, size);
        yield return new Rectangle(rectangle.Left - half, rectangle.Bottom - half, size, size);
        yield return new Rectangle(centerX - half, rectangle.Bottom - half, size, size);
        yield return new Rectangle(rectangle.Right - half, rectangle.Bottom - half, size, size);
    }

    private static void DrawCenteredText(
        Graphics graphics,
        string text,
        Font font,
        Brush brush,
        Rectangle bounds)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static void FillRoundedRectangle(
        Graphics graphics,
        Brush brush,
        Rectangle rectangle,
        int radius) =>
        FillRoundedRectangle(graphics, brush, new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), radius);

    private static void FillRoundedRectangle(
        Graphics graphics,
        Brush brush,
        RectangleF rectangle,
        float radius)
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

    private enum AdjustMode
    {
        None,
        Move,
        North,
        South,
        East,
        West,
        NorthWest,
        NorthEast,
        SouthWest,
        SouthEast
    }
}
