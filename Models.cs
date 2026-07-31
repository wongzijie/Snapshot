using System.Windows.Forms;

namespace Snapshot;

public enum CaptureMode
{
    Clipboard,
    SaveToFile
}

public sealed class HotkeyDefinition : IEquatable<HotkeyDefinition>
{
    public bool Control { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Windows { get; set; }
    public int Key { get; set; }

    public bool IsValid =>
        Key != (int)Keys.None &&
        Key != (int)Keys.ControlKey &&
        Key != (int)Keys.Menu &&
        Key != (int)Keys.ShiftKey &&
        Key != (int)Keys.LWin &&
        Key != (int)Keys.RWin &&
        (Control || Alt || Shift || Windows);

    public HotkeyDefinition Clone() => new()
    {
        Control = Control,
        Alt = Alt,
        Shift = Shift,
        Windows = Windows,
        Key = Key
    };

    public string ToDisplayString()
    {
        var parts = new List<string>(5);
        if (Control) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Windows) parts.Add("Win");
        if (Key != (int)Keys.None) parts.Add(GetKeyName((Keys)Key));
        return parts.Count == 0 ? "未设置" : string.Join(" + ", parts);
    }

    public bool Equals(HotkeyDefinition? other) =>
        other is not null &&
        Control == other.Control &&
        Alt == other.Alt &&
        Shift == other.Shift &&
        Windows == other.Windows &&
        Key == other.Key;

    public override bool Equals(object? obj) => Equals(obj as HotkeyDefinition);

    public override int GetHashCode() => HashCode.Combine(Control, Alt, Shift, Windows, Key);

    private static string GetKeyName(Keys key)
    {
        if (key is >= Keys.D0 and <= Keys.D9)
        {
            return ((char)('0' + (key - Keys.D0))).ToString();
        }

        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
        {
            return "Num " + (char)('0' + (key - Keys.NumPad0));
        }

        return key switch
        {
            Keys.Oemtilde => "`",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.OemOpenBrackets => "[",
            Keys.Oem6 => "]",
            Keys.Oem5 => "\\",
            Keys.Oem1 => ";",
            Keys.Oem7 => "'",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.Snapshot => "Print Screen",
            _ => key.ToString()
        };
    }
}

public sealed class AppSettings
{
    public HotkeyDefinition ClipboardHotkey { get; set; } = DefaultClipboardHotkey();
    public HotkeyDefinition SaveHotkey { get; set; } = DefaultSaveHotkey();
    public string SaveDirectory { get; set; } = KnownFolders.GetDownloadsPath();
    public long JpegQuality { get; set; } = 92;

    public static AppSettings CreateDefault() => new();

    public AppSettings Clone() => new()
    {
        ClipboardHotkey = ClipboardHotkey.Clone(),
        SaveHotkey = SaveHotkey.Clone(),
        SaveDirectory = SaveDirectory,
        JpegQuality = JpegQuality
    };

    public static HotkeyDefinition DefaultClipboardHotkey() => new()
    {
        Control = true,
        Alt = true,
        Key = (int)Keys.A
    };

    public static HotkeyDefinition DefaultSaveHotkey() => new()
    {
        Control = true,
        Alt = true,
        Key = (int)Keys.S
    };
}
