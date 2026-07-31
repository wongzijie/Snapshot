using System.Drawing;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Windows.Forms;

namespace Snapshot;

internal sealed class TransparentTextEditor : Control
{
    private static readonly Color OutlineColor = Color.FromArgb(220, 7, 193, 96);
    private Point _pointerDownScreen;
    private Point _locationAtPointerDown;
    private bool _moving;
    private bool _selecting;
    private string _displayText = string.Empty;
    private Point _caretLocation;
    private RectangleF[] _selectionRectangles = [];

    public TransparentTextEditor()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.IBeam;
        TabStop = false;
    }

    public event Action? FocusRequested;
    public event Action<Point>? TextSelectionStarted;
    public event Action<Point>? TextSelectionExtended;
    public event Action? PositionChangedByUser;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Rectangle MovementBounds { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string DisplayText
    {
        get => _displayText;
        set
        {
            _displayText = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point CaretLocation
    {
        get => _caretLocation;
        set
        {
            _caretLocation = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<RectangleF> SelectionRectangles
    {
        get => _selectionRectangles;
        set
        {
            _selectionRectangles = value.ToArray();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        if (_selectionRectangles.Length > 0)
        {
            using var selectionBrush = new SolidBrush(
                Color.FromArgb(150, 51, 133, 255));
            foreach (var rectangle in _selectionRectangles)
            {
                e.Graphics.FillRectangle(selectionBrush, rectangle);
            }
        }

        using var textBrush = new SolidBrush(ForeColor);
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.LineLimit
        };
        var textBounds = new RectangleF(
            3,
            3,
            Math.Max(1, ClientSize.Width - 6),
            Math.Max(1, ClientSize.Height - 6));
        e.Graphics.DrawString(
            string.IsNullOrEmpty(_displayText) ? "输入文字" : _displayText,
            Font,
            textBrush,
            textBounds,
            format);

        using var outline = new Pen(OutlineColor, 1f)
        {
            DashStyle = DashStyle.Dash
        };
        e.Graphics.DrawRectangle(
            outline,
            0,
            0,
            Math.Max(0, ClientSize.Width - 1),
            Math.Max(0, ClientSize.Height - 1));

        if (!string.IsNullOrEmpty(_displayText) &&
            _selectionRectangles.Length == 0)
        {
            var caretX = Math.Clamp(_caretLocation.X + 3, 2, Math.Max(2, ClientSize.Width - 2));
            var caretY = Math.Clamp(_caretLocation.Y + 3, 2, Math.Max(2, ClientSize.Height - 2));
            using var caret = new Pen(ForeColor, 1.5f);
            e.Graphics.DrawLine(
                caret,
                caretX,
                caretY,
                caretX,
                Math.Min(ClientSize.Height - 2, caretY + Font.Height));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        FocusRequested?.Invoke();
        _pointerDownScreen = PointToScreen(e.Location);
        _locationAtPointerDown = Location;
        _moving = IsMoveBorder(e.Location);
        _selecting = !_moving;
        Capture = true;
        if (_selecting)
        {
            TextSelectionStarted?.Invoke(e.Location);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Capture || e.Button != MouseButtons.Left)
        {
            Cursor = IsMoveBorder(e.Location) ? Cursors.SizeAll : Cursors.IBeam;
            return;
        }

        if (_selecting)
        {
            TextSelectionExtended?.Invoke(e.Location);
            return;
        }

        var currentScreen = PointToScreen(e.Location);
        var dx = currentScreen.X - _pointerDownScreen.X;
        var dy = currentScreen.Y - _pointerDownScreen.Y;
        if (!_moving || Math.Abs(dx) + Math.Abs(dy) < 2)
        {
            return;
        }

        var left = Math.Clamp(
            _locationAtPointerDown.X + dx,
            MovementBounds.Left,
            Math.Max(MovementBounds.Left, MovementBounds.Right - Width));
        var top = Math.Clamp(
            _locationAtPointerDown.Y + dy,
            MovementBounds.Top,
            Math.Max(MovementBounds.Top, MovementBounds.Bottom - Height));
        Location = new Point(left, top);
        PositionChangedByUser?.Invoke();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            Capture = false;
            _moving = false;
            _selecting = false;
            Cursor = IsMoveBorder(e.Location) ? Cursors.SizeAll : Cursors.IBeam;
        }
    }

    private bool IsMoveBorder(Point point)
    {
        const int borderGrabWidth = 7;
        return point.X <= borderGrabWidth ||
               point.Y <= borderGrabWidth ||
               point.X >= ClientSize.Width - borderGrabWidth - 1 ||
               point.Y >= ClientSize.Height - borderGrabWidth - 1;
    }
}
