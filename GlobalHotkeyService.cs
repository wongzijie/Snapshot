using System.Windows.Interop;

namespace Snapshot;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int ClipboardHotkeyId = 0x5341;
    private const int SaveHotkeyId = 0x5342;

    private readonly HwndSource _messageSource;
    private bool _registered;
    private bool _disposed;

    public event Action<CaptureMode>? HotkeyPressed;

    public GlobalHotkeyService()
    {
        var parameters = new HwndSourceParameters("Snapshot.GlobalHotkeys")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
            Width = 0,
            Height = 0
        };
        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(MessageHook);
    }

    public bool TryRegister(AppSettings settings, out string error)
    {
        UnregisterAll();

        if (!settings.ClipboardHotkey.IsValid || !settings.SaveHotkey.IsValid)
        {
            error = "快捷键必须包含至少一个修饰键（Ctrl、Alt、Shift 或 Win）和一个普通按键。";
            return false;
        }

        if (settings.ClipboardHotkey.Equals(settings.SaveHotkey))
        {
            error = "两套快捷键不能相同。";
            return false;
        }

        if (!Register(ClipboardHotkeyId, settings.ClipboardHotkey))
        {
            error = $"无法注册“{settings.ClipboardHotkey.ToDisplayString()}”。它可能已被其他程序占用。";
            return false;
        }

        if (!Register(SaveHotkeyId, settings.SaveHotkey))
        {
            NativeMethods.UnregisterHotKey(_messageSource.Handle, ClipboardHotkeyId);
            error = $"无法注册“{settings.SaveHotkey.ToDisplayString()}”。它可能已被其他程序占用。";
            return false;
        }

        _registered = true;
        error = string.Empty;
        return true;
    }

    public void UnregisterAll()
    {
        if (_messageSource.Handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_messageSource.Handle, ClipboardHotkeyId);
            NativeMethods.UnregisterHotKey(_messageSource.Handle, SaveHotkeyId);
        }

        _registered = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_registered)
        {
            UnregisterAll();
        }

        _messageSource.RemoveHook(MessageHook);
        _messageSource.Dispose();
        GC.SuppressFinalize(this);
    }

    private IntPtr MessageHook(
        IntPtr window,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != NativeMethods.WmHotkey)
        {
            return IntPtr.Zero;
        }

        var id = wParam.ToInt32();
        if (id == ClipboardHotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(CaptureMode.Clipboard);
        }
        else if (id == SaveHotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(CaptureMode.SaveToFile);
        }

        return IntPtr.Zero;
    }

    private bool Register(int id, HotkeyDefinition definition)
    {
        var modifiers = NativeMethods.ModNoRepeat;
        if (definition.Control) modifiers |= NativeMethods.ModControl;
        if (definition.Alt) modifiers |= NativeMethods.ModAlt;
        if (definition.Shift) modifiers |= NativeMethods.ModShift;
        if (definition.Windows) modifiers |= NativeMethods.ModWin;

        return NativeMethods.RegisterHotKey(
            _messageSource.Handle,
            id,
            modifiers,
            (uint)definition.Key);
    }
}
