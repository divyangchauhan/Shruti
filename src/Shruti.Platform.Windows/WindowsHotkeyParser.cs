using System.Globalization;

namespace Shruti.Platform.Windows;

public static class WindowsHotkeyParser
{
    public const uint AltModifier = 0x0001;
    public const uint ControlModifier = 0x0002;
    public const uint ShiftModifier = 0x0004;
    public const uint WindowsModifier = 0x0008;

    private static readonly HashSet<string> ReservedGestures = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alt+Escape",
        "Alt+Tab",
        "Ctrl+Alt+Delete",
        "Win+D",
        "Win+L"
    };

    public static bool TryParse(
        string? gesture,
        out WindowsHotkey? hotkey,
        out string? error)
    {
        hotkey = null;
        error = null;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            error = "A global hotkey is required when the global hotkey trigger is enabled.";
            return false;
        }

        string[] parts = gesture
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            error = "A global hotkey must include a modifier and a key.";
            return false;
        }

        uint modifiers = 0;
        uint? virtualKey = null;
        string? canonicalKey = null;

        foreach (string part in parts)
        {
            if (TryGetModifier(part, out uint modifier, out string? canonicalModifier))
            {
                if ((modifiers & modifier) != 0)
                {
                    error = $"The {canonicalModifier} modifier is specified more than once.";
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (virtualKey is not null || !WindowsVirtualKey.TryParse(part, out uint key, out string? parsedCanonicalKey))
            {
                error = $"'{part}' is not a supported global hotkey key.";
                return false;
            }

            virtualKey = key;
            canonicalKey = parsedCanonicalKey!;
        }

        if (modifiers == 0 || virtualKey is null)
        {
            error = "A global hotkey must include a modifier and a key.";
            return false;
        }

        string canonicalGesture = CreateCanonicalGesture(modifiers, canonicalKey!);
        if (ReservedGestures.Contains(canonicalGesture))
        {
            error = $"{canonicalGesture} is reserved by Windows and cannot be used as a global hotkey.";
            return false;
        }

        hotkey = new WindowsHotkey(modifiers, virtualKey.Value, canonicalGesture);
        return true;
    }

    private static string CreateCanonicalGesture(uint modifiers, string key)
    {
        var parts = new List<string>(5);
        if ((modifiers & ControlModifier) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & AltModifier) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ShiftModifier) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & WindowsModifier) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(key);
        return string.Join('+', parts);
    }

    private static bool TryGetModifier(string value, out uint modifier, out string? canonicalValue)
    {
        modifier = value.ToUpperInvariant() switch
        {
            "ALT" => AltModifier,
            "CTRL" or "CONTROL" => ControlModifier,
            "SHIFT" => ShiftModifier,
            "WIN" or "WINDOWS" => WindowsModifier,
            _ => 0
        };

        canonicalValue = modifier switch
        {
            AltModifier => "Alt",
            ControlModifier => "Ctrl",
            ShiftModifier => "Shift",
            WindowsModifier => "Win",
            _ => null
        };

        return modifier != 0;
    }
}

public static class WindowsVirtualKey
{
    private static readonly IReadOnlyDictionary<string, (uint Key, string Canonical)> NamedKeys =
        new Dictionary<string, (uint Key, string Canonical)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = (0x20, "Space"),
            ["Tab"] = (0x09, "Tab"),
            ["Enter"] = (0x0D, "Enter"),
            ["Return"] = (0x0D, "Enter"),
            ["Escape"] = (0x1B, "Escape"),
            ["Esc"] = (0x1B, "Escape"),
            ["Delete"] = (0x2E, "Delete"),
            ["RightControl"] = (0xA3, "RightControl"),
            ["RightCtrl"] = (0xA3, "RightControl"),
            ["LeftControl"] = (0xA2, "LeftControl"),
            ["LeftCtrl"] = (0xA2, "LeftControl")
        };

    public static bool TryParse(string? value, out uint key, out string? canonicalValue)
    {
        key = 0;
        canonicalValue = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        if (NamedKeys.TryGetValue(normalized, out (uint Key, string Canonical) namedKey))
        {
            key = namedKey.Key;
            canonicalValue = namedKey.Canonical;
            return true;
        }

        if (normalized.Length == 1 && char.IsAsciiLetterOrDigit(normalized[0]))
        {
            char character = char.ToUpperInvariant(normalized[0]);
            key = character;
            canonicalValue = character.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (normalized.Length is 2 or 3 &&
            normalized[0] is 'F' or 'f' &&
            int.TryParse(normalized[1..], CultureInfo.InvariantCulture, out int functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            key = checked((uint)(0x70 + functionNumber - 1));
            canonicalValue = $"F{functionNumber}";
            return true;
        }

        return false;
    }
}
