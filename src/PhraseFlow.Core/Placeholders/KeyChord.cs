using System.Text;

namespace PhraseFlow.Core.Placeholders;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A key press with optional modifiers, e.g. <c>Ctrl+Shift+S</c>, <c>Enter</c> or <c>F5</c>.
/// Either <see cref="VirtualKey"/> (Windows virtual-key code) or <see cref="Character"/> is set.
/// </summary>
public sealed record KeyChord(KeyModifiers Modifiers, int VirtualKey, char? Character = null)
{
    private static readonly Dictionary<string, int> NamedKeys = BuildNamedKeys();

    private static readonly Dictionary<int, string> KeyNames = NamedKeys
        .GroupBy(kv => kv.Value)
        .ToDictionary(g => g.Key, g => g.First().Key);

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        var parts = new List<string>();
        if (value.EndsWith("++", StringComparison.Ordinal))
        {
            parts.AddRange(value[..^2].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            parts.Add("+");
        }
        else if (value == "+")
        {
            parts.Add("+");
        }
        else
        {
            parts.AddRange(value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (parts.Count == 0)
        {
            return false;
        }

        var modifiers = KeyModifiers.None;
        for (var i = 0; i < parts.Count - 1; i++)
        {
            var modifier = ParseModifier(parts[i]);
            if (modifier == KeyModifiers.None)
            {
                return false;
            }

            modifiers |= modifier;
        }

        var keyText = parts[^1];
        if (NamedKeys.TryGetValue(keyText, out var vk))
        {
            chord = new KeyChord(modifiers, vk);
            return true;
        }

        if (keyText.Length == 1)
        {
            var c = keyText[0];
            if (char.IsAsciiLetter(c) || char.IsAsciiDigit(c))
            {
                chord = new KeyChord(modifiers, char.ToUpperInvariant(c));
                return true;
            }

            chord = new KeyChord(modifiers, 0, c);
            return true;
        }

        // A bare modifier, e.g. {key:Win}, presses that modifier key alone.
        var lone = ParseModifier(keyText);
        if (lone != KeyModifiers.None && modifiers == KeyModifiers.None)
        {
            chord = new KeyChord(KeyModifiers.None, lone switch
            {
                KeyModifiers.Ctrl => 0x11,
                KeyModifiers.Alt => 0x12,
                KeyModifiers.Shift => 0x10,
                _ => 0x5B,
            });
            return true;
        }

        return false;
    }

    private static KeyModifiers ParseModifier(string text) => text.ToLowerInvariant() switch
    {
        "ctrl" or "control" or "ctl" => KeyModifiers.Ctrl,
        "alt" or "menu" => KeyModifiers.Alt,
        "shift" => KeyModifiers.Shift,
        "win" or "windows" or "meta" or "super" or "cmd" => KeyModifiers.Win,
        _ => KeyModifiers.None,
    };

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Ctrl))
        {
            sb.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(KeyModifiers.Alt))
        {
            sb.Append("Alt+");
        }

        if (Modifiers.HasFlag(KeyModifiers.Shift))
        {
            sb.Append("Shift+");
        }

        if (Modifiers.HasFlag(KeyModifiers.Win))
        {
            sb.Append("Win+");
        }

        if (Character is { } c)
        {
            sb.Append(c);
        }
        else if (KeyNames.TryGetValue(VirtualKey, out var name))
        {
            sb.Append(name);
        }
        else if (VirtualKey is >= '0' and <= '9' or >= 'A' and <= 'Z')
        {
            sb.Append((char)VirtualKey);
        }
        else
        {
            sb.Append($"VK{VirtualKey:X2}");
        }

        return sb.ToString();
    }

    private static Dictionary<string, int> BuildNamedKeys()
    {
        // The first name listed for a code is used when formatting.
        var keys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Enter"] = 0x0D, ["Return"] = 0x0D,
            ["Tab"] = 0x09,
            ["Esc"] = 0x1B, ["Escape"] = 0x1B,
            ["Space"] = 0x20, ["Spacebar"] = 0x20,
            ["Backspace"] = 0x08, ["BS"] = 0x08, ["Back"] = 0x08,
            ["Delete"] = 0x2E, ["Del"] = 0x2E,
            ["Insert"] = 0x2D, ["Ins"] = 0x2D,
            ["Home"] = 0x24,
            ["End"] = 0x23,
            ["PgUp"] = 0x21, ["PageUp"] = 0x21,
            ["PgDn"] = 0x22, ["PageDown"] = 0x22,
            ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
            ["CapsLock"] = 0x14, ["NumLock"] = 0x90, ["ScrollLock"] = 0x91,
            ["PrintScreen"] = 0x2C, ["PrtSc"] = 0x2C,
            ["Pause"] = 0x13, ["Break"] = 0x13,
            ["Apps"] = 0x5D, ["ContextMenu"] = 0x5D,
            ["VolumeUp"] = 0xAF, ["VolumeDown"] = 0xAE, ["VolumeMute"] = 0xAD,
            ["MediaPlayPause"] = 0xB3, ["MediaNext"] = 0xB0, ["MediaPrev"] = 0xB1, ["MediaStop"] = 0xB2,
            ["Plus"] = 0xBB, ["Minus"] = 0xBD, ["Comma"] = 0xBC, ["Period"] = 0xBE,
            ["NumPad0"] = 0x60, ["NumPad1"] = 0x61, ["NumPad2"] = 0x62, ["NumPad3"] = 0x63, ["NumPad4"] = 0x64,
            ["NumPad5"] = 0x65, ["NumPad6"] = 0x66, ["NumPad7"] = 0x67, ["NumPad8"] = 0x68, ["NumPad9"] = 0x69,
            ["Multiply"] = 0x6A, ["Add"] = 0x6B, ["Subtract"] = 0x6D, ["Decimal"] = 0x6E, ["Divide"] = 0x6F,
        };
        for (var i = 1; i <= 24; i++)
        {
            keys[$"F{i}"] = 0x70 + i - 1;
        }

        return keys;
    }
}
