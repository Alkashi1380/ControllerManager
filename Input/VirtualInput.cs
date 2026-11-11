using System.Runtime.InteropServices;

namespace ControllerManager
{
    public static class VirtualInput
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        public const byte VK_VOLUME_MUTE = 0xAD;
        public const byte VK_VOLUME_DOWN = 0xAE;
        public const byte VK_VOLUME_UP = 0xAF;
        public const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        public const byte VK_MEDIA_PREV_TRACK = 0xB1;
        public const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        public const byte VK_SPACE = 0x20;
        public const byte VK_RETURN = 0x0D;
        public const byte VK_ESCAPE = 0x1B;
        public const byte VK_MENU = 0x12;
        public const byte VK_TAB = 0x09;
        public const byte VK_LWIN = 0x5B;
        public const byte VK_D = 0x44;
        public const byte VK_SNAPSHOT = 0x2C;
        public const byte VK_F11 = 0x7A;

        public const byte VK_LEFT = 0x25;
        public const byte VK_UP = 0x26;
        public const byte VK_RIGHT = 0x27;
        public const byte VK_DOWN = 0x28;

        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;

        public static void MoveMouseRelative(int dx, int dy)
        {
            if (dx != 0 || dy != 0)
                mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, 0);
        }

        public static void ScrollMouseWheel(int amount)
        {
            if (amount != 0)
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, amount, 0);
        }

        public static void LeftClick()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }

        public static void RightClick()
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
        }

        public static void SetKeyState(byte vk, bool down, ref bool prevDown)
        {
            if (down != prevDown) SetHeldKey(vk, 0, down);
            prevDown = down;
        }

        // Codes de scan = position physique des touches (W/A/S/D en QWERTY = Z/Q/S/D en AZERTY)
        public const byte SCAN_W = 0x11;
        public const byte SCAN_A = 0x1E;
        public const byte SCAN_S = 0x1F;
        public const byte SCAN_D = 0x20;
        public const uint KEYEVENTF_SCANCODE = 0x0008;

        // Comme SetKeyState mais par position physique : indépendant de la disposition du clavier
        public static void SetScanKeyState(byte scanCode, bool down, ref bool prevDown)
        {
            if (down != prevDown) SetHeldKey(0, scanCode, down);
            prevDown = down;
        }

        // Plusieurs actions peuvent maintenir la même touche. Seul le dernier relâchement envoie KEYUP.
        private static readonly Dictionary<(byte Vk, byte Scan), int> HeldKeys = new();

        private static void SetHeldKey(byte vk, byte scan, bool down)
        {
            lock (HeldKeys)
            {
                var key = (vk, scan);
                int count = HeldKeys.GetValueOrDefault(key);
                uint flags = scan == 0 ? KeyFlags(vk) : KEYEVENTF_SCANCODE;
                if (down)
                {
                    if (count == 0) keybd_event(vk, scan, flags, 0);
                    HeldKeys[key] = count + 1;
                }
                else if (count == 1)
                {
                    keybd_event(vk, scan, flags | KEYEVENTF_KEYUP, 0);
                    HeldKeys.Remove(key);
                }
                else if (count > 1)
                {
                    HeldKeys[key] = count - 1;
                }
            }
        }

        // Appui + relâchement : sans KEYUP, Windows garde la touche « enfoncée » dans son état clavier
        public static void TapKey(byte vk)
        {
            lock (HeldKeys)
            {
                uint flags = KeyFlags(vk);
                keybd_event(vk, 0, flags, 0);
                if (!HeldKeys.ContainsKey((vk, 0)))
                    keybd_event(vk, 0, flags | KEYEVENTF_KEYUP, 0);
            }
        }

        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint INPUT_KEYBOARD = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // L'union doit avoir la taille de MOUSEINPUT (le plus grand membre) pour que sizeof(INPUT) soit correct
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] private MouseInputPadding mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInputPadding
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // Exposé pour les tests : Windows rejette SendInput si la taille ne vaut pas 40 (x64) / 28 (x86)
        public static int InputStructSize => Marshal.SizeOf<INPUT>();

        // Tape un texte quelconque (accents, emoji…) indépendamment de la disposition du clavier
        public static void SendUnicodeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var inputs = new List<INPUT>(text.Length * 2);
            foreach (char ch in text.Replace("\r\n", "\n"))
            {
                if (ch == '\n')
                {
                    TapKey(0x0D); // Entrée : le caractère \n seul n'est pas interprété par toutes les applis
                    continue;
                }
                inputs.Add(UnicodeInput(ch, false));
                inputs.Add(UnicodeInput(ch, true));
            }
            if (inputs.Count > 0)
                SendInput((uint)inputs.Count, inputs.ToArray(), InputStructSize);
        }

        private static INPUT UnicodeInput(char ch, bool keyUp) => new()
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0) }
            }
        };

        // Maintien d'une combinaison (action « Maintenir ») : modificateurs puis touche, relâchés en ordre inverse
        public static void PressKeyCombo(KeyCombo combo)
        {
            foreach (var mod in combo.Modifiers)
                SetHeldKey(mod, 0, true);
            SetHeldKey(combo.Key, 0, true);
        }

        public static void ReleaseKeyCombo(KeyCombo combo)
        {
            SetHeldKey(combo.Key, 0, false);
            for (int i = combo.Modifiers.Count - 1; i >= 0; i--)
                SetHeldKey(combo.Modifiers[i], 0, false);
        }

        // Modificateurs enfoncés dans l'ordre, touche tapée, puis modificateurs relâchés dans l'ordre inverse
        public static void SendKeyCombo(KeyCombo combo)
        {
            lock (HeldKeys)
            {
                foreach (var mod in combo.Modifiers)
                    SetHeldKey(mod, 0, true);
                TapKey(combo.Key);
                for (int i = combo.Modifiers.Count - 1; i >= 0; i--)
                    SetHeldKey(combo.Modifiers[i], 0, false);
            }
        }

        private static uint KeyFlags(byte vk) => KeyCombo.IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0;

        public static void ExecuteAction(string action)
        {
            if (action.StartsWith(KeyCombo.ActionPrefix, StringComparison.Ordinal))
            {
                if (KeyCombo.TryParse(action.Substring(KeyCombo.ActionPrefix.Length), out var combo) && combo != null)
                    SendKeyCombo(combo);
                return;
            }

            switch (action)
            {
                case "Volume +": TapKey(VK_VOLUME_UP); break;
                case "Volume -": TapKey(VK_VOLUME_DOWN); break;
                case "Mute": TapKey(VK_VOLUME_MUTE); break;
                case "Play / Pause": TapKey(VK_MEDIA_PLAY_PAUSE); break;
                case "Piste Suivante": TapKey(VK_MEDIA_NEXT_TRACK); break;
                case "Piste Précédente": TapKey(VK_MEDIA_PREV_TRACK); break;
                case "Touche Espace (Saut)": TapKey(VK_SPACE); break;
                case "Touche Entrée": TapKey(VK_RETURN); break;
                case "Echap": TapKey(VK_ESCAPE); break;
                case "Plein Écran (F11)": TapKey(VK_F11); break;
                case "Clic Gauche Souris":
                    LeftClick();
                    break;
                case "Clic Droit Souris":
                    RightClick();
                    break;
                case "Clic Molette (Milieu)":
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN | MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
                    break;
                case "Capture d'écran (Impr. Écran)": TapKey(VK_SNAPSHOT); break;
                case "Afficher le Bureau (Win + D)":
                    SendKeyCombo(new KeyCombo(new[] { VK_LWIN }, VK_D));
                    break;
                case "Alt + Tab":
                    SendKeyCombo(new KeyCombo(new[] { VK_MENU }, VK_TAB));
                    break;
            }
        }
    }
}
