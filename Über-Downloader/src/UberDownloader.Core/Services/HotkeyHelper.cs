using System;

namespace UberDownloader.Core.Services;

public static class HotkeyHelper
{
    public static bool TryParse(string? hotkeyText, out uint fsModifiers, out uint vk)
    {
        fsModifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(hotkeyText)) return false;

        var parts = hotkeyText.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        string keyPart = string.Empty;

        foreach (var p in parts)
        {
            string part = p.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                fsModifiers |= (uint)NativeMethods.MOD_CONTROL;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                fsModifiers |= (uint)NativeMethods.MOD_ALT;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                fsModifiers |= (uint)NativeMethods.MOD_SHIFT;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                fsModifiers |= (uint)NativeMethods.MOD_WIN;
            }
            else
            {
                keyPart = part;
            }
        }

        if (string.IsNullOrEmpty(keyPart)) return false;

        // F1 à F24
        if (keyPart.Length >= 2 && keyPart.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(keyPart.Substring(1), out int fNum) && fNum >= 1 && fNum <= 24)
        {
            vk = (uint)(0x70 + (fNum - 1));
            return true;
        }

        // Touches spéciales courantes
        if (keyPart.Equals("Space", StringComparison.OrdinalIgnoreCase) || keyPart.Equals("Espace", StringComparison.OrdinalIgnoreCase)) { vk = 0x20; return true; }
        if (keyPart.Equals("Tab", StringComparison.OrdinalIgnoreCase)) { vk = 0x09; return true; }
        if (keyPart.Equals("Esc", StringComparison.OrdinalIgnoreCase) || keyPart.Equals("Escape", StringComparison.OrdinalIgnoreCase)) { vk = 0x1B; return true; }
        if (keyPart.Equals("Enter", StringComparison.OrdinalIgnoreCase) || keyPart.Equals("Return", StringComparison.OrdinalIgnoreCase)) { vk = 0x0D; return true; }

        // Si lettre unique A-Z
        if (keyPart.Length == 1 && char.IsLetter(keyPart[0]))
        {
            vk = (uint)char.ToUpperInvariant(keyPart[0]);
            return true;
        }

        // Si chiffre unique 0-9
        if (keyPart.Length == 1 && char.IsDigit(keyPart[0]))
        {
            vk = (uint)(0x30 + (keyPart[0] - '0'));
            return true;
        }

        return false;
    }
}
