using System.Drawing;
using System.Runtime.InteropServices;

namespace Snapshot;

internal static class NativeMethods
{
    internal const int WmHotkey = 0x0312;
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;
    internal const uint ModNoRepeat = 0x4000;
    internal const int DwmwaExtendedFrameBounds = 9;
    internal const int DwmwaCloaked = 14;
    internal const int GwlExStyle = -20;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExNoActivate = 0x08000000L;

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle() =>
            Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        IntPtr hWnd,
        int attribute,
        out Rect value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        IntPtr hWnd,
        int attribute,
        out int value,
        int valueSize);

    [DllImport("shell32.dll")]
    internal static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid folderId,
        uint flags,
        IntPtr token,
        out IntPtr path);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr icon);
}

public static class KnownFolders
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string GetDownloadsPath()
    {
        IntPtr pathPointer = IntPtr.Zero;
        try
        {
            if (NativeMethods.SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out pathPointer) == 0 &&
                pathPointer != IntPtr.Zero)
            {
                return Marshal.PtrToStringUni(pathPointer)!;
            }
        }
        catch
        {
            // Fall through to the conventional location.
        }
        finally
        {
            if (pathPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }
}
