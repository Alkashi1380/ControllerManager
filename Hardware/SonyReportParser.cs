namespace ControllerManager
{
    // État décodé d'une trame d'entrée DualSense / DualShock 4
    public sealed class SonyInputState
    {
        public float LX, LY, RX, RY, L2, R2;
        public byte B0, B1, B2;
        public bool T1Active, T2Active;
        public int T1X, T1Y, T2X, T2Y;

        // null quand la trame ne contient pas l'octet d'état batterie (DS4, mode Bluetooth simplifié)
        public int? BatteryPercent;
        public bool Charging;

        public bool PhysicalTouchClick => (B2 & 0x02) != 0;
    }

    // Décodage pur (sans accès matériel) des trames d'entrée Sony : testable avec des trames capturées
    public static class SonyReportParser
    {
        public const int ButtonCount = 20;

        public static SonyInputState Parse(byte[] buf, int read, bool isDualSense, bool isBluetooth)
        {
            var s = new SonyInputState();

            if (isDualSense)
            {
                if (buf[0] == 0x31)
                {
                    s.LX = (buf[2] - 128f) / 128f;
                    s.LY = (buf[3] - 128f) / 128f;
                    s.RX = (buf[4] - 128f) / 128f;
                    s.RY = (buf[5] - 128f) / 128f;
                    s.L2 = buf[6] / 255f;
                    s.R2 = buf[7] / 255f;
                    s.B0 = buf[9];
                    s.B1 = buf[10];
                    s.B2 = buf[11];

                    // Touchpad DualSense Bluetooth 0x31 : Doigt 1 (34..37), Doigt 2 (38..41)
                    if (read > 41)
                        ReadTouch(buf, 34, s);

                    // Batterie DualSense Bluetooth (octet 54)
                    if (read > 54)
                        ReadBattery(buf[54], s);
                }
                else if (isBluetooth && buf[0] == 0x01)
                {
                    s.LX = (buf[1] - 128f) / 128f;
                    s.LY = (buf[2] - 128f) / 128f;
                    s.RX = (buf[3] - 128f) / 128f;
                    s.RY = (buf[4] - 128f) / 128f;
                    s.B0 = buf[5];
                    s.B1 = buf[6];
                    s.B2 = buf[7];
                    s.L2 = read > 8 ? buf[8] / 255f : ((s.B1 & 0x04) != 0 ? 1f : 0f);
                    s.R2 = read > 9 ? buf[9] / 255f : ((s.B1 & 0x08) != 0 ? 1f : 0f);
                }
                else
                {
                    // DualSense USB 0x01 : Doigt 1 (33..36), Doigt 2 (37..40)
                    s.LX = (buf[1] - 128f) / 128f;
                    s.LY = (buf[2] - 128f) / 128f;
                    s.RX = (buf[3] - 128f) / 128f;
                    s.RY = (buf[4] - 128f) / 128f;
                    s.L2 = buf[5] / 255f;
                    s.R2 = buf[6] / 255f;
                    s.B0 = buf[8];
                    s.B1 = buf[9];
                    s.B2 = buf[10];

                    if (read > 40)
                        ReadTouch(buf, 33, s);

                    // Batterie DualSense USB (octet 53)
                    if (read > 53)
                        ReadBattery(buf[53], s);
                }
            }
            else
            {
                int off = (buf[0] == 0x11) ? 2 : 0;
                s.LX = (buf[1 + off] - 128f) / 128f;
                s.LY = (buf[2 + off] - 128f) / 128f;
                s.RX = (buf[3 + off] - 128f) / 128f;
                s.RY = (buf[4 + off] - 128f) / 128f;
                s.B0 = buf[5 + off];
                s.B1 = buf[6 + off];
                s.B2 = buf[7 + off];
                s.L2 = buf[8 + off] / 255f;
                s.R2 = buf[9 + off] / 255f;
            }

            return s;
        }

        // 20 boutons dans l'ordre de l'UI. Le côté du clic pavé est décidé par l'appelant (il dépend de l'appui en cours).
        public static bool[] DecodeButtons(SonyInputState s, bool touchClickIsRight)
        {
            int hat = s.B0 & 0x0F;
            bool click = s.PhysicalTouchClick;

            var cur = new bool[ButtonCount];
            cur[0] = (s.B0 & 0x20) != 0; // Croix
            cur[1] = (s.B0 & 0x40) != 0; // Rond
            cur[2] = (s.B0 & 0x10) != 0; // Carré
            cur[3] = (s.B0 & 0x80) != 0; // Triangle
            cur[4] = (s.B1 & 0x01) != 0; // L1
            cur[5] = (s.B1 & 0x02) != 0; // R1
            cur[6] = (s.B1 & 0x04) != 0 || s.L2 > 0.5f; // L2
            cur[7] = (s.B1 & 0x08) != 0 || s.R2 > 0.5f; // R2
            cur[8] = (s.B1 & 0x10) != 0; // Share / Create
            cur[9] = (s.B1 & 0x20) != 0; // Options
            cur[10] = (s.B1 & 0x40) != 0; // L3
            cur[11] = (s.B1 & 0x80) != 0; // R3
            cur[12] = hat == 0 || hat == 1 || hat == 7; // D-Haut
            cur[13] = hat == 3 || hat == 4 || hat == 5; // D-Bas
            cur[14] = hat == 5 || hat == 6 || hat == 7; // D-Gauche
            cur[15] = hat == 1 || hat == 2 || hat == 3; // D-Droit
            cur[16] = (s.B2 & 0x01) != 0;             // Bouton PS
            cur[17] = click && !touchClickIsRight;    // Clic Pavé Gauche
            cur[18] = click && touchClickIsRight;     // Clic Pavé Droit
            cur[19] = (s.B2 & 0x04) != 0;             // Bouton Mute Mic
            return cur;
        }

        private static void ReadTouch(byte[] buf, int o, SonyInputState s)
        {
            s.T1Active = (buf[o] & 0x80) == 0;
            s.T1X = ((buf[o + 2] & 0x0F) << 8) | buf[o + 1];
            s.T1Y = (buf[o + 3] << 4) | ((buf[o + 2] & 0xF0) >> 4);

            s.T2Active = (buf[o + 4] & 0x80) == 0;
            s.T2X = ((buf[o + 6] & 0x0F) << 8) | buf[o + 5];
            s.T2Y = (buf[o + 7] << 4) | ((buf[o + 6] & 0xF0) >> 4);
        }

        // Quartet bas : niveau 0..10 (par dizaines), quartet haut : 0 décharge, 1 en charge, 2 plein
        private static void ReadBattery(byte status, SonyInputState s)
        {
            s.BatteryPercent = Math.Min(100, (status & 0x0F) * 10 + 5);
            s.Charging = ((status >> 4) & 0x0F) == 1;
        }
    }
}
