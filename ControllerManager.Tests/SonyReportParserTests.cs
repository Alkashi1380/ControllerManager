using ControllerManager;
using Xunit;

namespace ControllerManager.Tests
{
    public class SonyReportParserTests
    {
        // Trames Bluetooth 0x31 réelles capturées sur une DualSense (PID 0CE6), au repos
        // A : sur batterie (état 0x00) — B : câble USB branché, en charge (état 0x11)
        private const string FrameBatteryHex =
            "31-41-80-7B-80-7E-00-00-01-08-00-00-00-D4-98-07-D1-08-00-03-00-F7-FF-C1-FF-C1-1F-C8-FD-1C-C5-CF-" +
            "24-05-80-00-00-00-80-00-00-00-00-09-09-00-00-00-00-00-D4-CB-CF-24-00-00-00-7F-80-75-BC-4C-00-EC-" +
            "8E-00-00-00-00-00-00-00-00-00-0E-5B-ED-CB";

        private const string FrameChargingHex =
            "31-F1-80-7C-80-7E-00-00-01-08-00-00-00-6F-07-17-D1-0A-00-05-00-F6-FF-2D-FA-8A-1E-01-07-24-4B-9D-" +
            "9D-13-B4-E9-83-04-B1-3C-D6-25-D9-09-09-00-00-00-00-00-58-50-9D-9D-11-10-00-BB-9D-AC-07-6E-DF-4F-" +
            "60-00-00-00-00-00-00-00-00-00-A9-34-D9-52";

        private static byte[] Hex(string s) => s.Split('-').Select(h => Convert.ToByte(h, 16)).ToArray();

        [Theory]
        [InlineData(FrameBatteryHex)]
        [InlineData(FrameChargingHex)]
        public void RealBluetoothFrames_HaveValidInputCrc(string hex)
        {
            // Valide l'implémentation CRC32 (préfixe 0xA1) sur le CRC calculé par la manette elle-même
            Assert.True(DualSenseCrc32.IsValidInputReport(Hex(hex)));
        }

        [Fact]
        public void CorruptedFrame_FailsCrc()
        {
            var frame = Hex(FrameBatteryHex);
            frame[10] ^= 0x20;
            Assert.False(DualSenseCrc32.IsValidInputReport(frame));
        }

        [Fact]
        public void RealFrameAtRest_DecodesCenteredSticksAndNoButtons()
        {
            var buf = Hex(FrameBatteryHex);
            var s = SonyReportParser.Parse(buf, buf.Length, isDualSense: true, isBluetooth: true);

            Assert.InRange(s.LX, -0.05f, 0.05f);
            Assert.InRange(s.LY, -0.05f, 0.05f);
            Assert.InRange(s.RX, -0.05f, 0.05f);
            Assert.InRange(s.RY, -0.05f, 0.05f);
            Assert.Equal(0f, s.L2);
            Assert.Equal(0f, s.R2);
            Assert.False(s.T1Active);
            Assert.False(s.T2Active);

            var buttons = SonyReportParser.DecodeButtons(s, touchClickIsRight: false);
            Assert.Equal(SonyReportParser.ButtonCount, buttons.Length);
            Assert.All(buttons, Assert.False); // hat 0x08 = croix au neutre
        }

        [Fact]
        public void RealFrames_DecodeBatteryAndChargingState()
        {
            var onBattery = SonyReportParser.Parse(Hex(FrameBatteryHex), 78, true, true);
            Assert.Equal(5, onBattery.BatteryPercent);
            Assert.False(onBattery.Charging);

            var charging = SonyReportParser.Parse(Hex(FrameChargingHex), 78, true, true);
            Assert.Equal(15, charging.BatteryPercent);
            Assert.True(charging.Charging);
        }

        [Theory]
        [InlineData(0x01, 15, false)]
        [InlineData(0x11, 15, true)]
        [InlineData(0x21, 100, false)]
        [InlineData(0xA0, null, false)]
        [InlineData(0xF0, null, false)]
        public void BatteryStatus_DistinguishesLowFullAndInvalidReadings(int status, int? percent, bool charging)
        {
            var frame = Hex(FrameBatteryHex);
            frame[54] = (byte)status;
            var state = SonyReportParser.Parse(frame, frame.Length, isDualSense: true, isBluetooth: true);
            Assert.Equal(percent, state.BatteryPercent);
            Assert.Equal(charging, state.Charging);
        }

        [Fact]
        public void DualSenseBluetoothOutput_UsesSequenceTagPayloadAndCrc()
        {
            var payload = new byte[DualSenseOutputReport.PayloadLength];
            payload[1] = 0x04;
            payload[44] = 255;

            var bluetooth = DualSenseOutputReport.Encode(payload, bluetooth: true, sequence: 31);
            Assert.Equal(78, bluetooth.Length);
            Assert.Equal(new byte[] { 0x31, 0xF0, 0x10, 0x00, 0x04 }, bluetooth[..5]);
            Assert.Equal(new byte[] { 255, 0, 0 }, bluetooth[47..50]);
            Assert.Equal(new byte[] { 0x01, 0x10, 0x61, 0xAB }, bluetooth[74..]);

            var usb = DualSenseOutputReport.Encode(payload, bluetooth: false);
            Assert.Equal(48, usb.Length);
            Assert.Equal(0x02, usb[0]);
            Assert.Equal(payload, usb[1..]);
        }

        [Fact]
        public void DualSenseBluetoothLightbarReset_SetsSetupFlagAndCrc()
        {
            var reset = DualSenseOutputReport.ResetBluetoothLightbar(0);
            Assert.Equal(new byte[] { 0x31, 0x00, 0x10 }, reset[..3]);
            Assert.Equal(0x02, reset[41]);
            Assert.Equal(0x02, reset[44]);
            Assert.Equal(new byte[] { 0x43, 0xE6, 0x31, 0x36 }, reset[74..]);
        }

        [Fact]
        public void UsbFrame_DecodesButtonsTouchAndBatteryAtUsbOffsets()
        {
            var buf = new byte[64];
            buf[0] = 0x01;
            buf[1] = 255; buf[2] = 0; buf[3] = 128; buf[4] = 128; // LX à droite, LY en haut
            buf[5] = 255; buf[6] = 0;                             // L2 à fond
            buf[8] = 0x20 | 0x02;                                 // Croix + hat 2 (droite)
            buf[9] = 0x20;                                        // Options
            buf[10] = 0x01 | 0x02;                                // PS + clic pavé
            // Doigt 1 posé en X=1500, Y=300
            buf[33] = 0x05; buf[34] = 1500 & 0xFF; buf[35] = (byte)(((300 & 0x0F) << 4) | (1500 >> 8)); buf[36] = 300 >> 4;
            buf[37] = 0x80;                                       // doigt 2 absent
            buf[53] = 0x2A;                                       // plein (2), niveau 10

            var s = SonyReportParser.Parse(buf, 64, isDualSense: true, isBluetooth: false);
            Assert.True(s.LX > 0.99f);
            Assert.Equal(-1f, s.LY);
            Assert.Equal(1f, s.L2);
            Assert.True(s.T1Active);
            Assert.Equal(1500, s.T1X);
            Assert.Equal(300, s.T1Y);
            Assert.False(s.T2Active);
            Assert.Equal(100, s.BatteryPercent);
            Assert.False(s.Charging); // 2 = plein, pas « en charge »

            var b = SonyReportParser.DecodeButtons(s, touchClickIsRight: true);
            Assert.True(b[0]);   // Croix
            Assert.True(b[6]);   // L2 (analogique > 0.5)
            Assert.True(b[9]);   // Options
            Assert.True(b[15]);  // D-Droit
            Assert.False(b[12]);
            Assert.True(b[16]);  // PS
            Assert.False(b[17]); // clic pavé gauche
            Assert.True(b[18]);  // clic pavé droit
        }

        [Theory]
        [InlineData(0, true, false, false, false)]  // haut
        [InlineData(1, true, true, false, false)]   // haut-droite
        [InlineData(4, false, false, true, false)]  // bas
        [InlineData(6, false, false, false, true)]  // gauche
        [InlineData(7, true, false, false, true)]   // haut-gauche
        [InlineData(8, false, false, false, false)] // neutre
        public void Hat_MapsToDpadButtons(int hat, bool up, bool right, bool down, bool left)
        {
            var b = SonyReportParser.DecodeButtons(new SonyInputState { B0 = (byte)hat }, false);
            Assert.Equal(up, b[12]);
            Assert.Equal(down, b[13]);
            Assert.Equal(left, b[14]);
            Assert.Equal(right, b[15]);
        }

        [Fact]
        public void Ds4BluetoothFrame_UsesTwoByteOffset()
        {
            var buf = new byte[78];
            buf[0] = 0x11;
            buf[3] = 0; buf[4] = 255; // LX gauche, LY bas
            buf[7] = 0x40 | 0x08;     // Rond, hat neutre
            buf[10] = 200;            // L2

            var s = SonyReportParser.Parse(buf, 78, isDualSense: false, isBluetooth: true);
            Assert.Equal(-1f, s.LX);
            Assert.True(s.LY > 0.99f);
            Assert.Null(s.BatteryPercent);
            var b = SonyReportParser.DecodeButtons(s, false);
            Assert.True(b[1]);
            Assert.True(b[6]);
        }

        [Fact]
        public void NullOrTruncatedFrames_DoNotThrowAndReturnSafeState()
        {
            var fromNull = SonyReportParser.Parse(null, 64, isDualSense: true, isBluetooth: false);
            Assert.Equal(0f, fromNull.LX);
            Assert.False(fromNull.T1Active);

            // Trame DualSense Bluetooth 0x31 tronquée à 9 octets (moins que les 12 requis)
            var shortBt = new byte[] { 0x31, 0x02, 0x80, 0x80, 0x80, 0x80, 0x00, 0x00, 0x00 };
            var sBt = SonyReportParser.Parse(shortBt, shortBt.Length, isDualSense: true, isBluetooth: true);
            Assert.Equal(0f, sBt.LX);

            // Trame DS4 Bluetooth 0x11 tronquée à 10 octets (moins que 10 + offset 2 = 12 requis)
            var shortDs4 = new byte[10];
            shortDs4[0] = 0x11;
            var sDs4 = SonyReportParser.Parse(shortDs4, shortDs4.Length, isDualSense: false, isBluetooth: true);
            Assert.Equal(0f, sDs4.LX);
        }
    }
}
