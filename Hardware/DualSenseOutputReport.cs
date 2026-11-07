using System.Buffers.Binary;

namespace ControllerManager
{
    public static class DualSenseOutputReport
    {
        public const int PayloadLength = 47;

        public static byte[] Encode(ReadOnlySpan<byte> payload, bool bluetooth, int sequence = 0)
        {
            if (payload.Length != PayloadLength)
                throw new ArgumentException("Charge utile DualSense invalide.", nameof(payload));

            // Sous Windows, HidSharp complète la trame USB à la taille du descripteur HID.
            if (!bluetooth)
            {
                byte[] usb = new byte[1 + PayloadLength];
                usb[0] = 0x02;
                payload.CopyTo(usb.AsSpan(1));
                return usb;
            }

            byte[] packet = new byte[78];
            packet[0] = 0x31;
            packet[1] = (byte)((sequence & 0x0F) << 4);
            packet[2] = 0x10;
            payload.CopyTo(packet.AsSpan(3));
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(74), DualSenseCrc32.ComputeOutputCrc(packet));
            return packet;
        }

        public static byte[] ResetBluetoothLightbar(int sequence)
        {
            Span<byte> payload = stackalloc byte[PayloadLength];
            payload.Clear();
            payload[38] = 0x02; // Autorise le réglage initial de la barre lumineuse.
            payload[41] = 0x02; // Éteint la couleur imposée par l'animation de connexion.
            return Encode(payload, bluetooth: true, sequence);
        }
    }
}
