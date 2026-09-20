namespace Shruti.Platform.Windows;

// Records one chord. Waiting for release prevents a captured key from activating
// a dialog button or the newly configured dictation trigger.
public sealed class WindowsShortcutRecorder(bool allowSingleKey)
{
    private readonly HashSet<uint> _pressed = [];
    private readonly HashSet<uint> _seen = [];
    private uint? _mainKey;
    private string? _candidate;

    public string DisplayText { get; private set; } = "Press your shortcut";
    public string? Gesture { get; private set; }
    public string? Error { get; private set; }
    public bool IsComplete { get; private set; }
    public bool IsCancelled { get; private set; }

    public void Update(uint key, bool isDown)
    {
        if (IsComplete) return;
        if (!isDown)
        {
            _pressed.Remove(key);
            if (_pressed.Count != 0 || _seen.Count == 0) return;

            IsComplete = true;
            if (IsCancelled) return;
            if (_mainKey is null && allowSingleKey &&
                WindowsHotkeyParser.TryParseHoldShortcut(DisplayText, out WindowsHotkey? chord, out _))
            {
                _candidate = chord!.Gesture;
            }
            if (_mainKey is null && allowSingleKey && _seen.Count == 1 && _seen.Contains(0xA3))
            {
                _candidate = "RightControl";
            }
            if (_candidate is null && Error is null)
            {
                Error = allowSingleKey ? "Use two modifiers, such as Ctrl + Win, or a modifier and a key." : "Use a modifier and a key, such as Ctrl + Space.";
            }
            Gesture = Error is null ? _candidate : null;
            return;
        }

        if (!_pressed.Add(key)) return;
        _seen.Add(key);
        uint modifiers = _pressed.Aggregate(0u, (value, pressed) => value | ModifierFor(pressed));
        if (ModifierFor(key) != 0)
        {
            if (_mainKey is not null)
            {
                Error = "Hold the modifiers first, then press one key.";
            }
            else
            {
                DisplayText = FormatModifiers(modifiers);
            }
            return;
        }

        if (key == 0x1B && modifiers == 0)
        {
            IsCancelled = true;
            return;
        }
        if (_mainKey is not null)
        {
            Error = "Use one key with your modifiers.";
            return;
        }
        _mainKey = key;
        if (!WindowsVirtualKey.TryFormat(key, out string? name))
        {
            Error = "That key is not supported. Try a letter, number, or function key.";
            return;
        }
        DisplayText = modifiers == 0 ? name! : $"{FormatModifiers(modifiers)}+{name}";
        if (modifiers == 0)
        {
            if (allowSingleKey && key is >= 0x70 and <= 0x87)
                _candidate = name;
            else
                Error = "Include Ctrl, Alt, Shift, or Win with this key.";
        }
        else if (WindowsHotkeyParser.TryParse(DisplayText, out WindowsHotkey? hotkey, out string? error))
        {
            _candidate = hotkey!.Gesture;
        }
        else
        {
            Error = error;
        }
    }

    private static uint ModifierFor(uint key) => key switch
    {
        0x11 or 0xA2 or 0xA3 => WindowsHotkeyParser.ControlModifier,
        0x12 or 0xA4 or 0xA5 => WindowsHotkeyParser.AltModifier,
        0x10 or 0xA0 or 0xA1 => WindowsHotkeyParser.ShiftModifier,
        0x5B or 0x5C => WindowsHotkeyParser.WindowsModifier,
        _ => 0
    };

    private static string FormatModifiers(uint modifiers)
    {
        var names = new List<string>();
        if ((modifiers & WindowsHotkeyParser.ControlModifier) != 0) names.Add("Ctrl");
        if ((modifiers & WindowsHotkeyParser.AltModifier) != 0) names.Add("Alt");
        if ((modifiers & WindowsHotkeyParser.ShiftModifier) != 0) names.Add("Shift");
        if ((modifiers & WindowsHotkeyParser.WindowsModifier) != 0) names.Add("Win");
        return string.Join('+', names);
    }
}
