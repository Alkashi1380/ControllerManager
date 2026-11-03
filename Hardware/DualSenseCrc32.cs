namespace ControllerManager
{
    public static class DualSenseCrc32
    {
        private static readonly uint[] Table = new uint[256];

        static DualSenseCrc32()
        {
            const uint poly = 0xEDB88320;
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ poly : crc >> 1;
                Table[i] = crc;
            }
        }

        // Préfixes HID Bluetooth intégrés au CRC : 0xA1 = trame d'entrée, 0xA2 = trame de sortie
        public const byte InputSeed = 0xA1;
        public const byte OutputSeed = 0xA2;

        public static uint ComputeOutputCrc(byte[] packet74Bytes) => Compute(OutputSeed, packet74Bytes, 74);

        public static uint Compute(byte seed, byte[] data, int length)
        {
            uint crc = 0xFFFFFFFF;
            crc = (crc >> 8) ^ Table[(crc ^ seed) & 0xFF];
            for (int i = 0; i < length; i++)
            {
                crc = (crc >> 8) ^ Table[(crc ^ data[i]) & 0xFF];
            }
            return ~crc;
        }

        // Vérifie le CRC little-endian placé à la fin d'une trame Bluetooth de 78 octets
        public static bool IsValidInputReport(byte[] report78Bytes) =>
            report78Bytes.Length >= 78 &&
            Compute(InputSeed, report78Bytes, 74) == BitConverter.ToUInt32(report78Bytes, 74);
    }
}
