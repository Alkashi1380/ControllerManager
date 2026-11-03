namespace ControllerManager
{
    // Raccourci clavier personnalisé : 0..n modificateurs + une touche (ex. « Ctrl+Shift+S », « F5 », « Alt+Tab »)
    public sealed record KeyCombo(IReadOnlyList<byte> Modifiers, byte Key)
    {
        // Préfixe stocké dans buttonMapping pour distinguer un raccourci personnalisé d'une action prédéfinie
        public const string ActionPrefix = "Touche:";

        private const byte VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;

        private static readonly Dictionary<string, byte> Modifier = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = VK_CONTROL, ["Control"] = VK_CONTROL, ["Ctl"] = VK_CONTROL,
            ["Shift"] = VK_SHIFT, ["Maj"] = VK_SHIFT,
            ["Alt"] = VK_MENU,
            ["Win"] = VK_LWIN, ["Windows"] = VK_LWIN
        };

        private static readonly Dictionary<string, byte> Named = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Enter"] = 0x0D, ["Entrée"] = 0x0D, ["Entree"] = 0x0D,
            ["Esc"] = 0x1B, ["Escape"] = 0x1B, ["Echap"] = 0x1B, ["Échap"] = 0x1B,
            ["Tab"] = 0x09,
            ["Space"] = 0x20, ["Espace"] = 0x20,
            ["Backspace"] = 0x08, ["Retour"] = 0x08,
            ["Delete"] = 0x2E, ["Del"] = 0x2E, ["Suppr"] = 0x2E,
            ["Insert"] = 0x2D, ["Inser"] = 0x2D,
            ["Home"] = 0x24, ["Début"] = 0x24, ["Debut"] = 0x24,
            ["End"] = 0x23, ["Fin"] = 0x23,
            ["PageUp"] = 0x21, ["PgUp"] = 0x21,
            ["PageDown"] = 0x22, ["PgDn"] = 0x22,
            ["Up"] = 0x26, ["Haut"] = 0x26,
            ["Down"] = 0x28, ["Bas"] = 0x28,
            ["Left"] = 0x25, ["Gauche"] = 0x25,
            ["Right"] = 0x27, ["Droite"] = 0x27,
            ["PrintScreen"] = 0x2C, ["ImprEcran"] = 0x2C,
            ["Pause"] = 0x13
        };

        // Touches « étendues » : sans ce drapeau, Windows les confond avec leur équivalent du pavé numérique
        private static readonly HashSet<byte> Extended = new() { 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, VK_LWIN };

        public static bool IsExtended(byte vk) => Extended.Contains(vk);

        public static bool TryParse(string? text, out KeyCombo? combo)
        {
            combo = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var tokens = text.Split('+', StringSplitOptions.TrimEntries);
            var modifiers = new List<byte>();
            byte? key = null;

            foreach (var token in tokens)
            {
                if (token.Length == 0) return false;

                if (Modifier.TryGetValue(token, out byte mod))
                {
                    if (!modifiers.Contains(mod)) modifiers.Add(mod);
                    continue;
                }

                if (key.HasValue || !TryParseKey(token, out byte vk)) return false; // une seule touche non modificatrice
                key = vk;
            }

            if (!key.HasValue)
            {
                // Un modificateur seul est une touche à part entière (ex. « Maintenir : Shift » pour courir)
                if (modifiers.Count != 1) return false;
                combo = new KeyCombo(Array.Empty<byte>(), modifiers[0]);
                return true;
            }
            combo = new KeyCombo(modifiers, key.Value);
            return true;
        }

        private static bool TryParseKey(string token, out byte vk)
        {
            vk = 0;
            if (Named.TryGetValue(token, out vk)) return true;

            if (token.Length == 1)
            {
                char c = char.ToUpperInvariant(token[0]);
                if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                {
                    vk = (byte)c; // VK_A..VK_Z et VK_0..VK_9 valent leur code ASCII
                    return true;
                }
                return false;
            }

            if ((token[0] == 'F' || token[0] == 'f') && int.TryParse(token.AsSpan(1), out int n) && n is >= 1 and <= 24)
            {
                vk = (byte)(0x70 + n - 1);
                return true;
            }
            return false;
        }
    }
}
