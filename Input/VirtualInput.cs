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
            if (down && !prevDown)
                keybd_event(vk, 0, 0, 0);
            else if (!down && prevDown)
                keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
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
            if (down && !prevDown)
                keybd_event(0, scanCode, KEYEVENTF_SCANCODE, 0);
            else if (!down && prevDown)
                keybd_event(0, scanCode, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, 0);
            prevDown = down;
        }

        // Appui + relâchement : sans KEYUP, Windows garde la touche « enfoncée » dans son état clavier
        public static void TapKey(byte vk)
        {
            keybd_event(vk, 0, 0, 0);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
        }

        public static void ExecuteAction(string action)
        {
            switch (action)
            {
                case "Volume +": TapKey(VK_VOLUME_UP); break;
                case "Volume -": TapKey(VK_VOLUME_DOWN); break;
                case "Mute": TapKey(VK_VOLUME_MUTE); break;
                case "Play / Pause": TapKey(VK_MEDIA_PLAY_PAUSE); break;
                case "Piste Suivante": TapKey(VK_MEDIA_NEXT_TRACK); break;
                case "Piste Précédente": TapKey(VK_MEDIA_PREV_TRACK); break;
                case "Touche Espace (Saut)":
                    keybd_event(VK_SPACE, 0, 0, 0);
                    keybd_event(VK_SPACE, 0, KEYEVENTF_KEYUP, 0);
                    break;
                case "Touche Entrée":
                    keybd_event(VK_RETURN, 0, 0, 0);
                    keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, 0);
                    break;
                case "Echap":
                    keybd_event(VK_ESCAPE, 0, 0, 0);
                    keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, 0);
                    break;
                case "Plein Écran (F11)":
                    keybd_event(VK_F11, 0, 0, 0);
                    keybd_event(VK_F11, 0, KEYEVENTF_KEYUP, 0);
                    break;
                case "Clic Gauche Souris":
                    LeftClick();
                    break;
                case "Clic Droit Souris":
                    RightClick();
                    break;
                case "Clic Molette (Milieu)":
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN | MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
                    break;
                case "Capture d'écran (Impr. Écran)":
                    keybd_event(VK_SNAPSHOT, 0, 0, 0);
                    keybd_event(VK_SNAPSHOT, 0, KEYEVENTF_KEYUP, 0);
                    break;
                case "Afficher le Bureau (Win + D)":
                    keybd_event(VK_LWIN, 0, 0, 0);
                    keybd_event(VK_D, 0, 0, 0);
                    keybd_event(VK_D, 0, KEYEVENTF_KEYUP, 0);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
                    break;
                case "Alt + Tab":
                    keybd_event(VK_MENU, 0, 0, 0);
                    keybd_event(VK_TAB, 0, 0, 0);
                    keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, 0);
                    keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);
                    break;
            }
        }
    }
}
