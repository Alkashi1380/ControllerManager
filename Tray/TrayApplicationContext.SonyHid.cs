using HidSharp;

namespace ControllerManager
{
    public partial class TrayApplicationContext
    {
        private void ResetTriggersToDefault()
        {
            var prof = GetActiveProfile();
            var c = prof?.color ?? new LightbarColor { r = 0, g = 102, b = 255 };

            if (prof != null)
            {
                prof.l2TriggerMode = "off";
                prof.r2TriggerMode = "off";
                // Sinon les anciens réglages reviennent au prochain chargement de profiles.json
                SaveProfilesToDisk();
            }

            SendDualSenseOutputReport(c.r, c.g, c.b, "off", 0, "off", 0);
            SyncStateToWebView();
        }

        private void BuildTriggerEffectBlock(byte[] dst, int offset, string mode, int force)
        {
            byte strength = (byte)Math.Clamp(force, 0, 255);
            for (int i = 0; i < 11; i++) dst[offset + i] = 0x00;

            switch (mode)
            {
                case "continuous":
                    dst[offset + 0] = 0x01;
                    dst[offset + 1] = 0x10;
                    dst[offset + 2] = strength;
                    break;

                case "section":
                    dst[offset + 0] = 0x02;
                    dst[offset + 1] = 0x25;
                    dst[offset + 2] = 0x95;
                    dst[offset + 3] = strength;
                    break;

                case "vibration":
                    dst[offset + 0] = 0x06;
                    dst[offset + 1] = 0x14;
                    dst[offset + 2] = (byte)Math.Clamp(strength / 4, 10, 63);
                    dst[offset + 3] = 0x20;
                    break;

                default:
                    dst[offset + 0] = 0x05;
                    break;
            }
        }

        private void SendDualSenseOutputReport(
            byte r, byte g, byte b,
            string l2Mode, int l2Force,
            string r2Mode, int r2Force,
            byte rumbleLeft = 0, byte rumbleRight = 0)
        {
            // En mode multi-manettes la PS5 reste active : on lui envoie toujours couleur et gâchettes
            if (!_multiControllerEnabled && _selectedControllerType == "XBOX" && XInput.FindSlot(_selectedWebGpIndex) >= 0)
                return;

            lock (_sonyLock)
            {
                try
                {
                    var dev = _activeSonyDevice ?? FindBestSonyHidDevice();
                    if (dev == null) return;

                    bool isBluetooth = IsBluetoothHid(dev);

                    if (dev.ProductID == 0x05C4 || dev.ProductID == 0x09CC)
                    {
                        Stream? ds4Stream = _activeSonyStream;
                        bool ds4OpenedTemp = false;
                        if (ds4Stream == null && dev.TryOpen(out HidStream ds4Temp))
                        {
                            ds4Stream = ds4Temp;
                            ds4OpenedTemp = true;
                        }
                        if (ds4Stream == null) return;

                        try
                        {
                            if (!isBluetooth)
                            {
                                // DS4 USB : rapport 0x05 (32 octets)
                                byte[] ds4Rep = new byte[32];
                                ds4Rep[0] = 0x05;
                                ds4Rep[1] = 0x07; // Flags : Rumble | Lightbar | Flash
                                ds4Rep[4] = rumbleRight;
                                ds4Rep[5] = rumbleLeft;
                                ds4Rep[6] = r;
                                ds4Rep[7] = g;
                                ds4Rep[8] = b;
                                ds4Stream.Write(ds4Rep, 0, ds4Rep.Length);
                            }
                            else
                            {
                                // DS4 Bluetooth : rapport 0x11 (78 octets) + CRC32 (même algorithme que la DualSense)
                                byte[] ds4Bt = new byte[78];
                                ds4Bt[0] = 0x11;
                                ds4Bt[1] = 0xC0; // HID + CRC activés
                                ds4Bt[3] = 0x07; // Flags : Rumble | Lightbar | Flash
                                ds4Bt[6] = rumbleRight;
                                ds4Bt[7] = rumbleLeft;
                                ds4Bt[8] = r;
                                ds4Bt[9] = g;
                                ds4Bt[10] = b;

                                uint ds4Crc = DualSenseCrc32.ComputeOutputCrc(ds4Bt);
                                ds4Bt[74] = (byte)(ds4Crc & 0xFF);
                                ds4Bt[75] = (byte)((ds4Crc >> 8) & 0xFF);
                                ds4Bt[76] = (byte)((ds4Crc >> 16) & 0xFF);
                                ds4Bt[77] = (byte)((ds4Crc >> 24) & 0xFF);
                                ds4Stream.Write(ds4Bt, 0, ds4Bt.Length);
                            }
                        }
                        finally
                        {
                            if (ds4OpenedTemp) ds4Stream.Dispose();
                        }
                        return;
                    }

                    byte[] common = new byte[47];
                    // Flags 0 : Rumble + R2 + L2
                    common[0] = 0x0F;
                    // Flags 1 : 0x01 (Mic Mute LED) | 0x04 (Lightbar RGB) | 0x10 (Player Indicator LEDs)
                    common[1] = 0x15;
                    common[2] = rumbleRight;
                    common[3] = rumbleLeft;
                    // Octet 8 : Mic Mute LED (1 = Allumé orange, 0 = Éteint)
                    common[8] = (byte)(_muteLedActive ? 0x01 : 0x00);

                    BuildTriggerEffectBlock(common, 10, r2Mode, r2Force);
                    BuildTriggerEffectBlock(common, 21, l2Mode, l2Force);

                    // Octet 43 : LEDs Joueur sous le pavé tactile (0x04 = LED centrale allumée)
                    common[43] = 0x04;
                    common[44] = r;
                    common[45] = g;
                    common[46] = b;

                    Stream? targetStream = _activeSonyStream;
                    bool openedTemp = false;
                    if (targetStream == null && dev.TryOpen(out HidStream tempStream))
                    {
                        targetStream = tempStream;
                        openedTemp = true;
                    }

                    if (targetStream == null) return;

                    try
                    {
                        if (!isBluetooth)
                        {
                            byte[] usbPacket = new byte[48];
                            usbPacket[0] = 0x02;
                            Buffer.BlockCopy(common, 0, usbPacket, 1, 47);
                            targetStream.Write(usbPacket, 0, usbPacket.Length);
                        }
                        else
                        {
                            byte[] btPacket = new byte[78];
                            btPacket[0] = 0x31;
                            btPacket[1] = 0x02;
                            Buffer.BlockCopy(common, 0, btPacket, 2, 47);

                            uint crc = DualSenseCrc32.ComputeOutputCrc(btPacket);
                            btPacket[74] = (byte)(crc & 0xFF);
                            btPacket[75] = (byte)((crc >> 8) & 0xFF);
                            btPacket[76] = (byte)((crc >> 16) & 0xFF);
                            btPacket[77] = (byte)((crc >> 24) & 0xFF);

                            targetStream.Write(btPacket, 0, btPacket.Length);
                        }
                    }
                    finally
                    {
                        if (openedTemp) targetStream.Dispose();
                    }
                }
                catch (Exception ex) { Log.Warn("Envoi du rapport de sortie Sony impossible", ex); }
            }
        }

        private void ApplyActiveProfileHardwareEffects()
        {
            var prof = GetActiveProfile();
            if (prof == null) return;

            var c = prof.color ?? new LightbarColor { r = 0, g = 102, b = 255 };
            SendDualSenseOutputReport(
                c.r, c.g, c.b,
                prof.l2TriggerMode ?? "off", prof.l2TriggerForce,
                prof.r2TriggerMode ?? "off", prof.r2TriggerForce
            );
        }

        private static bool IsBluetoothHid(HidDevice d) =>
            d.DevicePath.Contains(BluetoothHidServiceGuid, StringComparison.OrdinalIgnoreCase) ||
            d.DevicePath.Contains("bth", StringComparison.OrdinalIgnoreCase) ||
            d.GetMaxInputReportLength() > 64 ||
            d.GetMaxOutputReportLength() > 64;

        private HidDevice? FindBestSonyHidDevice()
        {
            var sonyDevs = DeviceList.Local.GetHidDevices(0x054C)
                .Where(d => d.ProductID == 0x0CE6 || d.ProductID == 0x0DF2 || d.ProductID == 0x05C4 || d.ProductID == 0x09CC)
                .ToList();

            if (sonyDevs.Count == 0) return null;

            var usbDev = sonyDevs.FirstOrDefault(d => !IsBluetoothHid(d));
            if (usbDev != null) return usbDev;

            return sonyDevs[0];
        }

        private void ResetHidAndWakeSony()
        {
            lock (_sonyLock)
            {
                try { _activeSonyStream?.Dispose(); } catch { }
                _activeSonyStream = null;
                _activeSonyDevice = null;
                _activeSonyPath = "";
                _lastDeviceScan = DateTime.MinValue;
            }

            try
            {
                var sonyDevs = DeviceList.Local.GetHidDevices(0x054C)
                    .Where(d => d.ProductID == 0x0CE6 || d.ProductID == 0x0DF2 || d.ProductID == 0x05C4 || d.ProductID == 0x09CC)
                    .ToList();

                foreach (var dev in sonyDevs)
                {
                    if (dev.TryOpen(out HidStream s))
                    {
                        using (s)
                        {
                            byte[] calib = new byte[64];
                            calib[0] = 0x05;
                            try { s.GetFeature(calib); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Reset HID Sony incomplet", ex); }

            ApplyActiveProfileHardwareEffects();

            _trayIcon.ShowBalloonTip(2000, "Reset HID & Bluetooth effectué",
                "Le flux de la manette PS5 et ses gâchettes adaptatives ont été réinitialisés.",
                ToolTipIcon.Info);

            SendHardwareInventoryToWeb();
        }

        private bool PollSonyPersistentStream(byte[] buf, byte[] drainBuf, ref DateTime lastStickStep)
        {
            lock (_sonyLock)
            {
                try
                {
                    // Sans flux ouvert, on ne ré-énumère le bus HID qu'une fois par seconde (évite un scan toutes les 8 ms)
                    double sinceLastScan = (DateTime.Now - _lastDeviceScan).TotalMilliseconds;
                    if (_activeSonyStream == null && sinceLastScan < 1000)
                        return false;

                    if (_activeSonyStream == null || sinceLastScan > 2500)
                    {
                        _lastDeviceScan = DateTime.Now;
                        var bestDev = FindBestSonyHidDevice();
                        if (bestDev == null)
                        {
                            try { _activeSonyStream?.Dispose(); } catch { }
                            _activeSonyStream = null;
                            _activeSonyDevice = null;
                            _activeSonyPath = "";
                            return false;
                        }

                        if (_activeSonyStream == null || _activeSonyPath != bestDev.DevicePath)
                        {
                            try { _activeSonyStream?.Dispose(); } catch { }
                            _activeSonyDevice = bestDev;
                            _activeSonyPath = bestDev.DevicePath;

                            if (!_activeSonyDevice.TryOpen(out _activeSonyStream))
                            {
                                _activeSonyStream = null;
                                return false;
                            }
                            Log.Info($"Manette Sony connectée : PID {bestDev.ProductID:X4}, {(IsBluetoothHid(bestDev) ? "Bluetooth" : "USB")}");

                            try
                            {
                                byte[] feat = new byte[64];
                                feat[0] = 0x05;
                                _activeSonyStream.GetFeature(feat);
                            }
                            catch { }

                            ApplyActiveProfileHardwareEffects();
                        }
                    }

                    int maxLen = Math.Min(buf.Length, _activeSonyDevice!.GetMaxInputReportLength());

                    _activeSonyStream!.ReadTimeout = 4;
                    int read = _activeSonyStream.Read(buf, 0, maxLen);
                    if (read < 9) return true;

                    _activeSonyStream.ReadTimeout = 0;
                    int drained = 0;
                    while (drained < 15)
                    {
                        try
                        {
                            int rNext = _activeSonyStream.Read(drainBuf, 0, maxLen);
                            if (rNext >= 9)
                            {
                                Buffer.BlockCopy(drainBuf, 0, buf, 0, rNext);
                                read = rNext;
                                drained++;
                            }
                            else break;
                        }
                        catch (TimeoutException)
                        {
                            break;
                        }
                    }

                    bool isDualSense = (_activeSonyDevice.ProductID == 0x0CE6 || _activeSonyDevice.ProductID == 0x0DF2);
                    bool isBluetooth = IsBluetoothHid(_activeSonyDevice) || buf[0] == 0x31 || buf[0] == 0x11;

                    var state = SonyReportParser.Parse(buf, read, isDualSense, isBluetooth);
                    if (state.BatteryPercent.HasValue)
                    {
                        _lastBatteryPercent = state.BatteryPercent.Value;
                        _lastBatteryCharging = state.Charging;
                    }
                    if (isDualSense)
                        ProcessDualSenseMultiTouchpad(state.T1Active, state.T1X, state.T1Y, state.T2Active, state.T2X, state.T2Y);

                    // Le côté du clic est figé au début de l'appui : un doigt qui glisse pendant l'appui
                    // ne doit pas relâcher "gauche" pour déclencher "droit". La bande centrale compte comme gauche.
                    bool physTouchClick = state.PhysicalTouchClick;
                    if (physTouchClick && !_prevPhysTouchClick)
                        _touchClickLatchedRight = state.T1Active && state.T1X > TouchpadRightClickMinX;
                    _prevPhysTouchClick = physTouchClick;

                    bool[] cur = SonyReportParser.DecodeButtons(state, _touchClickLatchedRight);
                    float lx = state.LX, ly = state.LY, rx = state.RX, ry = state.RY, l2Val = state.L2, r2Val = state.R2;
                    bool t1Active = state.T1Active, t2Active = state.T2Active;
                    int t1X = state.T1X, t1Y = state.T1Y, t2X = state.T2X, t2Y = state.T2Y;

                    HandlePauseCombo(cur[16] && cur[9], SourceSony); // PS + Options

                    bool buttonChanged = false;
                    for (int i = 0; i < 20; i++)
                    {
                        if (cur[i] != _prevSonyButtons[i]) buttonChanged = true;
                        if (cur[i] && !_prevSonyButtons[i])
                        {
                            // Si appui sur le bouton Mute Mic (19), basculer aussi la LED orange du bouton
                            if (i == 19)
                            {
                                _muteLedActive = !_muteLedActive;
                                ApplyActiveProfileHardwareEffects();
                            }
                            ExecuteMapping(i, SourceSony);
                        }
                        else if (!cur[i] && _prevSonyButtons[i])
                        {
                            ReleaseMapping(i, SourceSony);
                        }
                        _prevSonyButtons[i] = cur[i];
                    }

                    if ((DateTime.Now - lastStickStep).TotalMilliseconds >= 8)
                    {
                        lastStickStep = DateTime.Now;
                        ProcessAnalogSticksMapping(lx, ly, rx, ry, SourceSony);
                    }

                    if (_webView != null && (buttonChanged || (DateTime.Now - _lastWebUiPush).TotalMilliseconds >= 16))
                    {
                        _lastWebUiPush = DateTime.Now;
                        SendToWebView(new
                        {
                            type = "sonyHidState",
                            mode = isBluetooth ? "Bluetooth Sans-Fil" : "USB Câblé",
                            lx,
                            ly,
                            rx,
                            ry,
                            l2 = l2Val,
                            r2 = r2Val,
                            t1Active,
                            t1X,
                            t1Y,
                            t2Active,
                            t2X,
                            t2Y,
                            physTouchClick,
                            battery = _lastBatteryPercent,
                            charging = _lastBatteryCharging,
                            buttons = cur
                        });
                    }

                    return true;
                }
                catch (TimeoutException)
                {
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Info($"Manette Sony perdue ({ex.GetType().Name}), reconnexion au prochain scan");
                    try { _activeSonyStream?.Dispose(); } catch { }
                    _activeSonyStream = null;
                    _activeSonyDevice = null;
                    _activeSonyPath = "";
                    return false;
                }
            }
        }
    }
}
