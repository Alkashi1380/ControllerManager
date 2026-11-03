using System.Runtime.InteropServices;

namespace ControllerManager
{
    // XInput : contrairement à Windows.Gaming.Input, livre les entrées même quand l'appli n'a pas le focus (mode Tray / en jeu)
    public static class XInput
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_BATTERY_INFORMATION
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        [DllImport("xinput1_4.dll")]
        public static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput1_4.dll")]
        public static extern uint XInputSetState(uint dwUserIndex, ref XINPUT_VIBRATION pVibration);

        [DllImport("xinput1_4.dll")]
        public static extern uint XInputGetBatteryInformation(uint dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);

        public const uint ERROR_SUCCESS = 0;

        public const ushort DPAD_UP = 0x0001;
        public const ushort DPAD_DOWN = 0x0002;
        public const ushort DPAD_LEFT = 0x0004;
        public const ushort DPAD_RIGHT = 0x0008;
        public const ushort START = 0x0010;
        public const ushort BACK = 0x0020;
        public const ushort LEFT_THUMB = 0x0040;
        public const ushort RIGHT_THUMB = 0x0080;
        public const ushort LEFT_SHOULDER = 0x0100;
        public const ushort RIGHT_SHOULDER = 0x0200;
        public const ushort A = 0x1000;
        public const ushort B = 0x2000;
        public const ushort X = 0x4000;
        public const ushort Y = 0x8000;

        public const byte BATTERY_TYPE_DISCONNECTED = 0x00;
        public const byte BATTERY_TYPE_WIRED = 0x01;

        public static bool IsConnected(int slot) =>
            slot >= 0 && slot < 4 && XInputGetState((uint)slot, out _) == ERROR_SUCCESS;

        // Slot préféré s'il est connecté, sinon le premier slot connecté, sinon -1
        public static int FindSlot(int preferredIndex)
        {
            if (IsConnected(preferredIndex)) return preferredIndex;
            for (int i = 0; i < 4; i++)
                if (IsConnected(i)) return i;
            return -1;
        }

        public static int ConnectedCount()
        {
            int n = 0;
            for (int i = 0; i < 4; i++)
                if (IsConnected(i)) n++;
            return n;
        }

        public static void SetVibration(int slot, double left, double right)
        {
            if (slot < 0) return;
            var vib = new XINPUT_VIBRATION
            {
                wLeftMotorSpeed = (ushort)(Math.Clamp(left, 0, 1) * 65535),
                wRightMotorSpeed = (ushort)(Math.Clamp(right, 0, 1) * 65535)
            };
            XInputSetState((uint)slot, ref vib);
        }
    }
}
