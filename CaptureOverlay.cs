using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Snapshot;

public sealed class CaptureOverlay : Form
{
    private const int ToolbarWidth = 730;
    private const int ToolbarHeight = 50;
    private const int GripSize = 7;
    private static readonly Color Accent = Color.FromArgb(7, 193, 96);
    private static readonly Color[] AnnotationColors =
    [
        Color.FromArgb(255, 77, 79),
        Color.FromArgb(255, 196, 0),
        Color.FromArgb(7, 193, 96),
        Color.FromArgb(24, 144, 255),
        Color.White
    ];
    private static readonly float[] StrokeWidths = [3f, 5f, 8f];
    private static readonly float[] TextFontSizes = [12f, 16f, 20f, 28f, 36f, 48f];

    private readonly DesktopSnapshot _snapshot;
    private readonly CaptureMode _mode;
    private readonly AnnotationDocument _annotations = new();
    private readonly List<ToolbarItem> _toolbarItems = [];
    private readonly Font _uiFont = new("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _smallFont = new("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _buttonFont = new("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _symbolFont = new("Segoe UI Symbol", 13f, FontStyle.Regular, GraphicsUnit.Point);

    private Rectangle _hovered;
    private Rectangle _selection;
    private Rectangle _toolbarBounds;
    private Rectangle _selectionAtPointerDown;
    private Point _pointerDownScreen;
    private bool _creating;
    private bool _locked;
    private AdjustMode _adjustMode;
    private bool _completionRaised;
    private AnnotationTool _activeTool;
    private List<PointF>? _activePoints;
    private PointF _activeStart;
    private PointF _activeEnd;
    private bool _drawingAnnotation;
    private int _colorIndex;
    private int _widthIndex = 1;
    private Bitmap? _mosaicBitmap;
    private TransparentTextEditor? _inlineTextEditor;
    private TextBox? _inlineTextInput;
    private TextAnnotation? _editingTextAnnotation;
    private PointF _inlineTextLocation;
    private string _textFontFamily = "Microsoft YaHei UI";
    private float _textFontSize = 20f;
    private FontStyle _textFontStyle = FontStyle.Regular;
    private Color _textColor = AnnotationColors[0];
    private int _textSizeIndex = 2;
    private bool _fontDialogOpen;

    public event Action<Bitmap?>? CaptureCompleted;
    public event Action<Bitmap, CaptureAnalysisAction>? AnalysisRequested;

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

        _hovered = _snapshot.FindTargetAt(System.Windows.Forms.Cursor.Position);
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
            if (_activeTool != AnnotationTool.None)
            {
                if (_selection.Contains(screenPoint))
                {
                    BeginAnnotation(screenPoint);
                }

                return;
            }

            if (!_annotations.IsEmpty)
            {
                return;
            }

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

        if (_drawingAnnotation)
        {
            UpdateActiveAnnotation(screenPoint);
            Invalidate();
            return;
        }

        if (_creating)
        {
            _selection = Rectangle.Intersect(
                NormalizeRectangle(_pointerDownScreen, screenPoint),
                _snapshot.VirtualBounds);
            Invalidate();
            return;
        }

        if (_adjustMode != AdjustMode.None)
        {
            _selection = AdjustSelection(screenPoint);
            InvalidateMosaic();
            Invalidate();
            return;
        }

        if (_locked)
        {
            Cursor = _activeTool != AnnotationTool.None && _selection.Contains(screenPoint)
                ? Cursors.Cross
                : CursorFor(HitTestSelection(screenPoint));
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

        if (_drawingAnnotation)
        {
            FinishAnnotation();
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
        if (_activeTool == AnnotationTool.None &&
            _locked &&
            _selection.Contains(ClientToScreen(e.Location)))
        {
            ConfirmCapture();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_inlineTextInput?.Focused == true)
        {
            return;
        }

        if (e.Control && e.KeyCode == Keys.Z)
        {
            _annotations.Undo();
            Invalidate();
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Y)
        {
            _annotations.Redo();
            Invalidate();
            e.Handled = true;
            return;
        }

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
        else if (_locked && e.KeyCode == Keys.P)
        {
            ToggleTool(AnnotationTool.Pen);
        }
        else if (_locked && e.KeyCode == Keys.A)
        {
            ToggleTool(AnnotationTool.Arrow);
        }
        else if (_locked && e.KeyCode == Keys.T)
        {
            ToggleTool(AnnotationTool.Text);
        }
        else if (_locked && e.KeyCode == Keys.M)
        {
            ToggleTool(AnnotationTool.Mosaic);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        CancelInlineTextEditor();
        _uiFont.Dispose();
        _smallFont.Dispose();
        _buttonFont.Dispose();
        _symbolFont.Dispose();
        _mosaicBitmap?.Dispose();
        _snapshot.Dispose();
        base.OnFormClosed(e);
    }

    private void BeginNewSelection(Point screenPoint)
    {
        CancelInlineTextEditor();
        _locked = false;
        _creating = true;
        _adjustMode = AdjustMode.None;
        _activeTool = AnnotationTool.None;
        _annotations.Clear();
        InvalidateMosaic();
        _pointerDownScreen = screenPoint;
        _selection = Rectangle.Empty;
        Cursor = Cursors.Cross;
        Capture = true;
        Invalidate();
    }

    private void ResetSelection()
    {
        CancelInlineTextEditor();
        _locked = false;
        _creating = false;
        _adjustMode = AdjustMode.None;
        _activeTool = AnnotationTool.None;
        _annotations.Clear();
        _activePoints = null;
        _drawingAnnotation = false;
        InvalidateMosaic();
        _selection = Rectangle.Empty;
        _hovered = _snapshot.FindTargetAt(System.Windows.Forms.Cursor.Position);
        Cursor = Cursors.Cross;
        Capture = false;
        Invalidate();
    }

    private void BeginAnnotation(Point screenPoint)
    {
        var relative = ToSelectionPoint(screenPoint);
        if (_activeTool == AnnotationTool.Text)
        {
            StartInlineTextEditor(screenPoint);
            return;
        }

        _drawingAnnotation = true;
        _activeStart = relative;
        _activeEnd = relative;
        _activePoints = [relative];
        Capture = true;
    }

    private void StartInlineTextEditor(Point screenPoint)
    {
        CommitInlineTextEditor();

        var selection = ScreenToClient(_selection);
        var relative = ToSelectionPoint(screenPoint);
        var existing = _annotations.HitTestText(relative);
        var click = new Point(
            screenPoint.X - _snapshot.VirtualBounds.Left,
            screenPoint.Y - _snapshot.VirtualBounds.Top);
        const int initialWidth = 260;
        const int initialHeight = 58;
        if (existing is not null)
        {
            _editingTextAnnotation = existing;
            _textFontFamily = existing.FontFamily;
            _textFontSize = existing.FontSize;
            _textFontStyle = existing.FontStyle;
            _textColor = existing.Color;
            _textSizeIndex = FindClosestTextSizeIndex(_textFontSize);
        }

        var desiredWidth = existing is null
            ? initialWidth
            : Math.Max(40, (int)Math.Ceiling(existing.LayoutSize.Width + 6));
        var desiredHeight = existing is null
            ? initialHeight
            : Math.Max(28, (int)Math.Ceiling(existing.LayoutSize.Height + 6));
        var desiredLeft = existing is null
            ? click.X - 3
            : selection.Left + (int)Math.Round(existing.Location.X) - 3;
        var desiredTop = existing is null
            ? click.Y - 3
            : selection.Top + (int)Math.Round(existing.Location.Y) - 3;
        var left = Math.Clamp(
            desiredLeft,
            selection.Left,
            Math.Max(selection.Left, selection.Right - Math.Min(desiredWidth, selection.Width)));
        var top = Math.Clamp(
            desiredTop,
            selection.Top,
            Math.Max(selection.Top, selection.Bottom - Math.Min(desiredHeight, selection.Height)));

        var editor = new TransparentTextEditor
        {
            DisplayText = existing?.Text ?? string.Empty,
            ForeColor = _textColor,
            Font = CreateTextFont(),
            Location = new Point(left, top),
            MovementBounds = selection,
            Size = new Size(
                Math.Max(40, Math.Min(desiredWidth, selection.Right - left)),
                Math.Max(28, Math.Min(desiredHeight, selection.Bottom - top)))
        };
        var input = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            BorderStyle = BorderStyle.None,
            Font = CreateTextFont(),
            Location = new Point(ClientSize.Width + 200, ClientSize.Height + 200),
            Multiline = true,
            ScrollBars = ScrollBars.None,
            ShortcutsEnabled = true,
            Size = editor.Size,
            TabStop = false,
            Text = existing?.Text ?? string.Empty,
            WordWrap = true
        };

        _inlineTextEditor = editor;
        _inlineTextInput = input;
        UpdateInlineTextLocation();
        input.TextChanged += (_, _) =>
        {
            if (_inlineTextEditor != editor)
            {
                return;
            }

            editor.DisplayText = input.Text;
            ResizeInlineTextEditor();
            SyncInlineTextCaret();
        };
        input.KeyDown += InlineTextEditorOnKeyDown;
        input.KeyUp += (_, _) => SyncInlineTextCaret();
        input.MouseUp += (_, _) => SyncInlineTextCaret();
        input.Leave += (_, _) => QueueInlineTextCommit(editor, input);
        var selectionAnchor = input.TextLength;
        editor.FocusRequested += () =>
        {
            input.Focus();
        };
        editor.TextSelectionStarted += point =>
        {
            input.Focus();
            selectionAnchor = GetInlineTextCharIndex(input, point);
            input.Select(selectionAnchor, 0);
            SyncInlineTextCaret();
        };
        editor.TextSelectionExtended += point =>
        {
            var current = GetInlineTextCharIndex(input, point);
            var start = Math.Min(selectionAnchor, current);
            input.Select(start, Math.Abs(current - selectionAnchor));
            SyncInlineTextCaret();
        };
        editor.PositionChangedByUser += () =>
        {
            UpdateInlineTextLocation();
            input.Focus();
            Invalidate();
        };

        Controls.Add(input);
        Controls.Add(editor);
        editor.BringToFront();
        input.Select(input.TextLength, 0);
        input.Focus();
        SyncInlineTextCaret();
        Invalidate();
    }

    private void QueueInlineTextCommit(
        TransparentTextEditor editor,
        TextBox input)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (_inlineTextEditor == editor &&
                _inlineTextInput == input &&
                !input.Focused &&
                !_fontDialogOpen)
            {
                CommitInlineTextEditor();
            }
        });
    }

    private void InlineTextEditorOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            CommitInlineTextEditor();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            CancelInlineTextEditor();
            Focus();
        }
    }

    private void ResizeInlineTextEditor()
    {
        var editor = _inlineTextEditor;
        var input = _inlineTextInput;
        if (editor is null ||
            input is null ||
            editor.IsDisposed ||
            input.IsDisposed)
        {
            return;
        }

        var selection = ScreenToClient(_selection);
        var maxWidth = Math.Max(40, Math.Min(520, selection.Right - editor.Left));
        var maxHeight = Math.Max(28, selection.Bottom - editor.Top);
        var content = string.IsNullOrEmpty(input.Text) ? "输入文字" : input.Text + " ";
        var measured = TextRenderer.MeasureText(
            content,
            editor.Font,
            new Size(Math.Max(20, maxWidth - 16), 10000),
            TextFormatFlags.TextBoxControl |
            TextFormatFlags.WordBreak |
            TextFormatFlags.NoPadding);
        editor.Size = new Size(
            Math.Min(maxWidth, Math.Max(180, measured.Width + 18)),
            Math.Min(maxHeight, Math.Max(44, measured.Height + 18)));
        input.Size = editor.Size;
        SyncInlineTextCaret();
    }

    private void CommitInlineTextEditor()
    {
        var editor = _inlineTextEditor;
        var input = _inlineTextInput;
        if (editor is null || input is null)
        {
            return;
        }

        _inlineTextEditor = null;
        _inlineTextInput = null;
        var editedAnnotation = _editingTextAnnotation;
        _editingTextAnnotation = null;
        var text = input.Text.TrimEnd();
        var fontFamily = editor.Font.FontFamily.Name;
        var fontSize = editor.Font.SizeInPoints;
        var fontStyle = editor.Font.Style;
        var color = editor.ForeColor;
        var layoutSize = new SizeF(
            Math.Max(1, editor.ClientSize.Width - 6),
            Math.Max(1, editor.ClientSize.Height - 6));
        Controls.Remove(editor);
        Controls.Remove(input);
        editor.Dispose();
        input.Dispose();

        if (!string.IsNullOrWhiteSpace(text))
        {
            var replacement = new TextAnnotation(
                _inlineTextLocation,
                text,
                color,
                fontFamily,
                fontSize,
                fontStyle,
                layoutSize);
            if (editedAnnotation is null)
            {
                _annotations.Add(replacement);
            }
            else
            {
                _annotations.ReplaceText(editedAnnotation, replacement);
            }
        }

        Focus();
        Invalidate();
    }

    private void CancelInlineTextEditor()
    {
        var editor = _inlineTextEditor;
        var input = _inlineTextInput;
        if (editor is null && input is null)
        {
            return;
        }

        _inlineTextEditor = null;
        _inlineTextInput = null;
        _editingTextAnnotation = null;
        if (editor is not null)
        {
            Controls.Remove(editor);
            editor.Dispose();
        }

        if (input is not null)
        {
            Controls.Remove(input);
            input.Dispose();
        }

        Invalidate();
    }

    private void UpdateInlineTextLocation()
    {
        if (_inlineTextEditor is null)
        {
            return;
        }

        var selection = ScreenToClient(_selection);
        _inlineTextLocation = new PointF(
            _inlineTextEditor.Left - selection.Left + 3,
            _inlineTextEditor.Top - selection.Top + 3);
    }

    private void SyncInlineTextCaret()
    {
        var editor = _inlineTextEditor;
        var input = _inlineTextInput;
        if (editor is null || input is null || input.IsDisposed)
        {
            return;
        }

        editor.CaretLocation = input.GetPositionFromCharIndex(
            Math.Clamp(input.SelectionStart, 0, input.TextLength));
        editor.SelectionRectangles = CreateSelectionRectangles(input);
    }

    private static int GetInlineTextCharIndex(TextBox input, Point editorPoint)
    {
        var inputPoint = new Point(
            Math.Max(0, editorPoint.X - 3),
            Math.Max(0, editorPoint.Y - 3));
        return Math.Clamp(
            input.GetCharIndexFromPosition(inputPoint),
            0,
            input.TextLength);
    }

    private static IReadOnlyList<RectangleF> CreateSelectionRectangles(
        TextBox input)
    {
        if (input.SelectionLength <= 0 || input.TextLength == 0)
        {
            return [];
        }

        var start = Math.Clamp(input.SelectionStart, 0, input.TextLength);
        var end = Math.Clamp(
            start + input.SelectionLength,
            start,
            input.TextLength);
        var lines = new Dictionary<int, (int Left, int Right)>();
        for (var index = start; index < end; index++)
        {
            if (input.Text[index] is '\r' or '\n')
            {
                continue;
            }

            var first = input.GetPositionFromCharIndex(index);
            var second = input.GetPositionFromCharIndex(index + 1);
            var width = second.Y == first.Y
                ? Math.Max(2, second.X - first.X)
                : Math.Max(
                    2,
                    TextRenderer.MeasureText(
                        input.Text[index].ToString(),
                        input.Font,
                        Size.Empty,
                        TextFormatFlags.NoPadding).Width);
            if (lines.TryGetValue(first.Y, out var line))
            {
                lines[first.Y] = (
                    Math.Min(line.Left, first.X),
                    Math.Max(line.Right, first.X + width));
            }
            else
            {
                lines[first.Y] = (first.X, first.X + width);
            }
        }

        return lines
            .OrderBy(line => line.Key)
            .Select(line => new RectangleF(
                line.Value.Left + 3,
                line.Key + 3,
                Math.Max(2, line.Value.Right - line.Value.Left),
                Math.Max(2, input.Font.Height)))
            .ToArray();
    }

    private Font CreateTextFont() =>
        new(
            _textFontFamily,
            _textFontSize,
            _textFontStyle,
            GraphicsUnit.Point);

    private void ApplyInlineTextStyle()
    {
        var editor = _inlineTextEditor;
        var input = _inlineTextInput;
        if (editor is null ||
            input is null ||
            editor.IsDisposed ||
            input.IsDisposed)
        {
            return;
        }

        ApplyTextFont(editor);
        ApplyTextFont(input);
        editor.ForeColor = _textColor;
        ResizeInlineTextEditor();
        input.Focus();
        SyncInlineTextCaret();
    }

    private void ApplyTextFont(Control control)
    {
        if (string.Equals(
                control.Font.FontFamily.Name,
                _textFontFamily,
                StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(control.Font.SizeInPoints - _textFontSize) < 0.05f &&
            control.Font.Style == _textFontStyle)
        {
            return;
        }

        var previous = control.Font;
        var replacement = CreateTextFont();
        control.Font = replacement;
        if (!ReferenceEquals(control.Font, replacement))
        {
            replacement.Dispose();
        }

        if (!ReferenceEquals(previous, control.Font))
        {
            previous.Dispose();
        }
    }

    private void OpenTextFontDialog()
    {
        using var dialog = new FontDialog
        {
            Font = CreateTextFont(),
            Color = _textColor,
            ShowColor = true,
            ShowEffects = true,
            FontMustExist = true,
            MinSize = 8,
            MaxSize = 96
        };

        _fontDialogOpen = true;
        try
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _textFontFamily = dialog.Font.FontFamily.Name;
            _textFontSize = dialog.Font.SizeInPoints;
            _textFontStyle = dialog.Font.Style;
            _textColor = dialog.Color;
            _textSizeIndex = FindClosestTextSizeIndex(_textFontSize);
            ApplyInlineTextStyle();
            Invalidate();
        }
        finally
        {
            _fontDialogOpen = false;
            _inlineTextInput?.Focus();
        }
    }

    private static int FindClosestTextSizeIndex(float size)
    {
        var bestIndex = 0;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < TextFontSizes.Length; index++)
        {
            var distance = Math.Abs(TextFontSizes[index] - size);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private void UpdateActiveAnnotation(Point screenPoint)
    {
        var clamped = new Point(
            Math.Clamp(screenPoint.X, _selection.Left, _selection.Right - 1),
            Math.Clamp(screenPoint.Y, _selection.Top, _selection.Bottom - 1));
        var relative = ToSelectionPoint(clamped);
        _activeEnd = relative;

        if (_activeTool is AnnotationTool.Pen or AnnotationTool.Mosaic)
        {
            if (_activePoints is null ||
                _activePoints.Count == 0 ||
                DistanceSquared(_activePoints[^1], relative) >= 4f)
            {
                _activePoints ??= [];
                _activePoints.Add(relative);
            }
        }
    }

    private void FinishAnnotation()
    {
        var element = CreateActiveAnnotation();
        if (element is not null)
        {
            _annotations.Add(element);
        }

        _drawingAnnotation = false;
        _activePoints = null;
        Capture = false;
        Invalidate();
    }

    private AnnotationElement? CreateActiveAnnotation()
    {
        var color = AnnotationColors[_colorIndex];
        var width = StrokeWidths[_widthIndex];
        return _activeTool switch
        {
            AnnotationTool.Pen when _activePoints is { Count: > 0 } =>
                new PenAnnotation(_activePoints, color, width),
            AnnotationTool.Arrow when DistanceSquared(_activeStart, _activeEnd) >= 16f =>
                new ArrowAnnotation(_activeStart, _activeEnd, color, width),
            AnnotationTool.Mosaic when _activePoints is { Count: > 0 } =>
                new MosaicAnnotation(_activePoints, 24f + width * 2),
            _ => null
        };
    }

    private void ConfirmCapture()
    {
        if (_completionRaised || !_locked || _selection.Width < 2 || _selection.Height < 2)
        {
            return;
        }

        CommitInlineTextEditor();
        _completionRaised = true;
        var result = CreateSelectionBitmap(includeAnnotations: true);
        Hide();
        CaptureCompleted?.Invoke(result);
        Close();
    }

    private void RequestAnalysis(CaptureAnalysisAction action)
    {
        if (_completionRaised || !_locked || _selection.Width < 2 || _selection.Height < 2)
        {
            return;
        }

        CommitInlineTextEditor();
        _completionRaised = true;
        var result = CreateSelectionBitmap(includeAnnotations: false);
        Hide();
        if (AnalysisRequested is { } handler)
        {
            handler(result, action);
        }
        else
        {
            result.Dispose();
        }

        Close();
    }

    private void CancelCapture()
    {
        if (_completionRaised)
        {
            return;
        }

        CancelInlineTextEditor();
        _completionRaised = true;
        Hide();
        CaptureCompleted?.Invoke(null);
        Close();
    }

    private Bitmap CreateSelectionBitmap(bool includeAnnotations)
    {
        var source = new Rectangle(
            _selection.Left - _snapshot.VirtualBounds.Left,
            _selection.Top - _snapshot.VirtualBounds.Top,
            _selection.Width,
            _selection.Height);
        var result = new Bitmap(_selection.Width, _selection.Height, PixelFormat.Format24bppRgb);
        if (DeviceDpi > 0)
        {
            result.SetResolution(DeviceDpi, DeviceDpi);
        }

        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(
            _snapshot.Image,
            new Rectangle(0, 0, result.Width, result.Height),
            source,
            GraphicsUnit.Pixel);

        if (includeAnnotations && !_annotations.IsEmpty)
        {
            ConfigureAnnotationGraphics(graphics);
            _annotations.Draw(
                graphics,
                EnsureMosaicBitmap(),
                _editingTextAnnotation);
        }

        return result;
    }

    private Bitmap? EnsureMosaicBitmap()
    {
        if (!_annotations.ContainsMosaic &&
            !(_drawingAnnotation && _activeTool == AnnotationTool.Mosaic))
        {
            return null;
        }

        if (_mosaicBitmap is null)
        {
            using var cleanSelection = CreateSelectionBitmap(includeAnnotations: false);
            _mosaicBitmap = MosaicBitmapFactory.Create(cleanSelection);
        }

        return _mosaicBitmap;
    }

    private void InvalidateMosaic()
    {
        _mosaicBitmap?.Dispose();
        _mosaicBitmap = null;
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

        if (drawHandles && (!_annotations.IsEmpty || _drawingAnnotation))
        {
            var state = graphics.Save();
            try
            {
                graphics.SetClip(local);
                graphics.TranslateTransform(local.Left, local.Top);
                ConfigureAnnotationGraphics(graphics);
                _annotations.Draw(
                    graphics,
                    EnsureMosaicBitmap(),
                    _editingTextAnnotation);
                CreateActiveAnnotation()?.Draw(graphics, EnsureMosaicBitmap());
            }
            finally
            {
                graphics.Restore(state);
            }
        }

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

        if (drawHandles && _annotations.IsEmpty && _activeTool == AnnotationTool.None)
        {
            foreach (var handle in GetHandleRectangles(local))
            {
                graphics.FillRectangle(Brushes.White, handle);
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
        var text = !_locked
            ? "单击选择窗口，或按住鼠标拖出自定义区域 · Esc 取消"
            : _activeTool switch
            {
                AnnotationTool.Pen => "拖动绘制画笔 · P 切换工具 · Ctrl+Z 撤销",
                AnnotationTool.Arrow => "拖动绘制箭头 · A 切换工具 · Ctrl+Z 撤销",
                AnnotationTool.Text => "单击空白处新建文字 · 单击已有文字再次编辑 · 拖动文字框可移动",
                AnnotationTool.Mosaic => "在需要隐藏的位置拖动 · M 切换工具 · Ctrl+Z 撤销",
                _ when !_annotations.IsEmpty => "可继续选择标注工具 · Ctrl+Z 撤销 · Enter 完成",
                _ => "拖动选区可移动或缩放 · 选择工具可标注 · Enter 完成"
            };
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
        BuildToolbarItems();

        using var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
        var shadowBounds = _toolbarBounds;
        shadowBounds.Offset(0, 3);
        FillRoundedRectangle(graphics, shadow, shadowBounds, 9);

        using var background = new SolidBrush(Color.FromArgb(247, 30, 34, 39));
        FillRoundedRectangle(graphics, background, _toolbarBounds, 9);

        foreach (var item in _toolbarItems)
        {
            DrawToolbarItem(graphics, item);
        }
    }

    private void BuildToolbarItems()
    {
        _toolbarItems.Clear();
        var x = _toolbarBounds.Left + 6;
        var y = _toolbarBounds.Top + 5;
        const int height = ToolbarHeight - 10;

        AddToolbarItem(ref x, y, height, 46, ToolbarAction.Pen, "画笔");
        AddToolbarItem(ref x, y, height, 46, ToolbarAction.Arrow, "箭头");
        AddToolbarItem(ref x, y, height, 46, ToolbarAction.Text, "文字");
        AddToolbarItem(ref x, y, height, 54, ToolbarAction.Mosaic, "马赛克");
        AddToolbarItem(ref x, y, height, 38, ToolbarAction.Color, string.Empty);
        AddToolbarItem(ref x, y, height, 48, ToolbarAction.Width, "粗细");
        AddToolbarItem(ref x, y, height, 60, ToolbarAction.Font, "字体");
        AddToolbarItem(ref x, y, height, 38, ToolbarAction.Undo, "↶");
        AddToolbarItem(ref x, y, height, 38, ToolbarAction.Redo, "↷");
        AddToolbarItem(ref x, y, height, 44, ToolbarAction.Ocr, "OCR");
        AddToolbarItem(ref x, y, height, 50, ToolbarAction.Translate, "翻译");
        AddToolbarItem(ref x, y, height, 48, ToolbarAction.Reset, "重选");
        AddToolbarItem(ref x, y, height, 48, ToolbarAction.Cancel, "取消");
        AddToolbarItem(
            ref x,
            y,
            height,
            108,
            ToolbarAction.Confirm,
            _mode == CaptureMode.Clipboard ? "复制" : "保存 JPG");
    }

    private void AddToolbarItem(
        ref int x,
        int y,
        int height,
        int width,
        ToolbarAction action,
        string text)
    {
        _toolbarItems.Add(new ToolbarItem(
            action,
            new Rectangle(x, y, width, height),
            text));
        x += width;
    }

    private void DrawToolbarItem(Graphics graphics, ToolbarItem item)
    {
        var active = item.Action switch
        {
            ToolbarAction.Pen => _activeTool == AnnotationTool.Pen,
            ToolbarAction.Arrow => _activeTool == AnnotationTool.Arrow,
            ToolbarAction.Text => _activeTool == AnnotationTool.Text,
            ToolbarAction.Mosaic => _activeTool == AnnotationTool.Mosaic,
            _ => false
        };
        var enabled = item.Action switch
        {
            ToolbarAction.Undo => _annotations.CanUndo,
            ToolbarAction.Redo => _annotations.CanRedo,
            ToolbarAction.Font => _activeTool == AnnotationTool.Text,
            _ => true
        };

        if (active)
        {
            using var activeBrush = new SolidBrush(Color.FromArgb(75, Accent));
            FillRoundedRectangle(graphics, activeBrush, Rectangle.Inflate(item.Bounds, -3, -3), 6);
        }

        if (item.Action == ToolbarAction.Confirm)
        {
            using var doneBrush = new SolidBrush(Accent);
            FillRoundedRectangle(graphics, doneBrush, Rectangle.Inflate(item.Bounds, -3, -2), 7);
        }

        if (item.Action == ToolbarAction.Color)
        {
            var color = _activeTool == AnnotationTool.Text
                ? _textColor
                : AnnotationColors[_colorIndex];
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(
                brush,
                item.Bounds.Left + 12,
                item.Bounds.Top + 11,
                18,
                18);
            using var border = new Pen(Color.FromArgb(160, 255, 255, 255), 1f);
            graphics.DrawEllipse(
                border,
                item.Bounds.Left + 12,
                item.Bounds.Top + 11,
                18,
                18);
            return;
        }

        if (item.Action == ToolbarAction.Width)
        {
            if (_activeTool == AnnotationTool.Text)
            {
                using var sizeBrush = new SolidBrush(Color.White);
                DrawCenteredText(
                    graphics,
                    $"{_textFontSize:0}pt",
                    _smallFont,
                    sizeBrush,
                    item.Bounds);
                return;
            }

            var width = StrokeWidths[_widthIndex];
            using var pen = new Pen(Color.White, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(
                pen,
                item.Bounds.Left + 12,
                item.Bounds.Top + item.Bounds.Height / 2,
                item.Bounds.Right - 12,
                item.Bounds.Top + item.Bounds.Height / 2);
            return;
        }

        var alpha = enabled ? 255 : 82;
        using var textBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
        var font = item.Action is ToolbarAction.Undo or ToolbarAction.Redo
            ? _symbolFont
            : item.Action == ToolbarAction.Confirm
                ? _buttonFont
                : _smallFont;
        DrawCenteredText(graphics, item.Text, font, textBrush, item.Bounds);
    }

    private Rectangle CalculateToolbarBounds()
    {
        var localSelection = ScreenToClient(_selection);
        var x = Math.Clamp(
            localSelection.Right - ToolbarWidth,
            8,
            Math.Max(8, ClientSize.Width - ToolbarWidth - 8));
        var below = localSelection.Bottom + 10;
        var y = below + ToolbarHeight <= ClientSize.Height - 8
            ? below
            : localSelection.Top - ToolbarHeight - 10;
        y = Math.Clamp(y, 8, Math.Max(8, ClientSize.Height - ToolbarHeight - 8));
        return new Rectangle(x, y, ToolbarWidth, ToolbarHeight);
    }

    private void HandleToolbarClick(Point point)
    {
        var item = _toolbarItems.FirstOrDefault(candidate => candidate.Bounds.Contains(point));
        switch (item.Action)
        {
            case ToolbarAction.Pen:
                ToggleTool(AnnotationTool.Pen);
                break;
            case ToolbarAction.Arrow:
                ToggleTool(AnnotationTool.Arrow);
                break;
            case ToolbarAction.Text:
                ToggleTool(AnnotationTool.Text);
                break;
            case ToolbarAction.Mosaic:
                ToggleTool(AnnotationTool.Mosaic);
                break;
            case ToolbarAction.Color:
                _colorIndex = (_colorIndex + 1) % AnnotationColors.Length;
                if (_activeTool == AnnotationTool.Text)
                {
                    _textColor = AnnotationColors[_colorIndex];
                    ApplyInlineTextStyle();
                }
                Invalidate();
                break;
            case ToolbarAction.Width:
                if (_activeTool == AnnotationTool.Text)
                {
                    _textSizeIndex = (_textSizeIndex + 1) % TextFontSizes.Length;
                    _textFontSize = TextFontSizes[_textSizeIndex];
                    ApplyInlineTextStyle();
                }
                else
                {
                    _widthIndex = (_widthIndex + 1) % StrokeWidths.Length;
                }
                Invalidate();
                break;
            case ToolbarAction.Font:
                if (_activeTool == AnnotationTool.Text)
                {
                    OpenTextFontDialog();
                }
                break;
            case ToolbarAction.Undo:
                CommitInlineTextEditor();
                _annotations.Undo();
                Invalidate();
                break;
            case ToolbarAction.Redo:
                CommitInlineTextEditor();
                _annotations.Redo();
                Invalidate();
                break;
            case ToolbarAction.Ocr:
                RequestAnalysis(CaptureAnalysisAction.Ocr);
                break;
            case ToolbarAction.Translate:
                RequestAnalysis(CaptureAnalysisAction.Translate);
                break;
            case ToolbarAction.Reset:
                ResetSelection();
                break;
            case ToolbarAction.Cancel:
                CancelCapture();
                break;
            case ToolbarAction.Confirm:
                ConfirmCapture();
                break;
        }
    }

    private void ToggleTool(AnnotationTool tool)
    {
        if (_activeTool == AnnotationTool.Text)
        {
            CommitInlineTextEditor();
        }

        _activeTool = _activeTool == tool ? AnnotationTool.None : tool;
        Cursor = _activeTool == AnnotationTool.None ? Cursors.Default : Cursors.Cross;
        Invalidate();
    }

    private AdjustMode HitTestSelection(Point screenPoint)
    {
        if (!_locked ||
            _selection.IsEmpty ||
            !_annotations.IsEmpty ||
            _activeTool != AnnotationTool.None)
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

    private PointF ToSelectionPoint(Point screenPoint) =>
        new(screenPoint.X - _selection.Left, screenPoint.Y - _selection.Top);

    private static float DistanceSquared(PointF first, PointF second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private static void ConfigureAnnotationGraphics(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
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
        FillRoundedRectangle(
            graphics,
            brush,
            new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height),
            radius);

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

    private readonly record struct ToolbarItem(
        ToolbarAction Action,
        Rectangle Bounds,
        string Text);

    private enum ToolbarAction
    {
        None,
        Pen,
        Arrow,
        Text,
        Mosaic,
        Color,
        Width,
        Font,
        Undo,
        Redo,
        Ocr,
        Translate,
        Reset,
        Cancel,
        Confirm
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
