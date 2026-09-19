namespace Shruti.Platform.Windows;

// Tracks physical hook events, including suppressed keys whose async key state
// Windows does not update. Release completes only after every chord key is up.
public sealed class WindowsModifierChord(uint requiredModifiers)
{
    private readonly HashSet<uint> _pressed = [];
    private readonly HashSet<uint> _suppressed = [];
    public bool IsPressed { get; private set; }

    public bool Update(uint key, bool down)
    {
        if (down)
        {
            _pressed.Add(key);
            uint modifiers = _pressed.Aggregate(0u, (mask, value) => mask | ModifierFor(value));
            if (!IsPressed && modifiers == requiredModifiers && _pressed.All(value => ModifierFor(value) != 0))
            {
                IsPressed = true;
                _suppressed.Add(key);
            }
            return _suppressed.Contains(key);
        }

        _pressed.Remove(key);
        if (!_pressed.Any(value => (ModifierFor(value) & requiredModifiers) != 0)) IsPressed = false;
        return _suppressed.Remove(key);
    }

    private static uint ModifierFor(uint key) => key switch
    {
        0x11 or 0xA2 or 0xA3 => WindowsHotkeyParser.ControlModifier,
        0x12 or 0xA4 or 0xA5 => WindowsHotkeyParser.AltModifier,
        0x10 or 0xA0 or 0xA1 => WindowsHotkeyParser.ShiftModifier,
        0x5B or 0x5C => WindowsHotkeyParser.WindowsModifier,
        _ => 0
    };
}
