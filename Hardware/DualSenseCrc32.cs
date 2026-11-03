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

        public static uint ComputeOutputCrc(byte[] packet74Bytes)
        {
            uint crc = 0xFFFFFFFF;
            crc = (crc >> 8) ^ Table[(crc ^ 0xA2) & 0xFF];
            for (int i = 0; i < 74; i++)
            {
                crc = (crc >> 8) ^ Table[(crc ^ packet74Bytes[i]) & 0xFF];
            }
            return ~crc;
        }
    }
}
