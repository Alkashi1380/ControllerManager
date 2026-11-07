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
            if (dst == null || offset < 0 || offset + 11 > dst.Length) return;
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

        private sealed record PendingSonyOutput(
            byte R, byte G, byte B,
            string L2Mode, int L2Force,
            string R2Mode, int R2Force,
            byte RumbleLeft, byte RumbleRight);

        private volatile PendingSonyOutput? _pendingSonyOutput;
        private PendingSonyOutput? _lastSentSonyOutput;
        private PendingSonyOutput? _lastFailedSonyOutput;
        private HidStream? _lastSonyOutputStream;
        private bool _lastSentMuteLed;
        private int _sonyBluetoothSequence;
        private HidStream? _sonyLightbarStream;
        private DateTime _lastSonyOutputWrite = DateTime.MinValue;
        private readonly AutoResetEvent _sonyOutputSignal = new(false);
        private Thread? _sonyOutputWriterThread;
        private readonly object _sonyWriterInitLock = new();

        private void EnsureSonyOutputWriterThread()
        {
            if (_sonyOutputWriterThread != null) return;
            lock (_sonyWriterInitLock)
            {
                if (_sonyOutputWriterThread != null) return;
                _sonyOutputWriterThread = new Thread(SonyOutputWriterLoop)
                {
                    IsBackground = true,
                    Name = "SonyHidOutputWriter"
                };
                _sonyOutputWriterThread.Start();
            }
        }

        private void SendDualSenseOutputReport(
            byte r, byte g, byte b,
            string l2Mode, int l2Force,
            string r2Mode, int r2Force,
            byte rumbleLeft = 0, byte rumbleRight = 0)
        {
            // Mise en file d'attente non-bloquante : un thread d'écriture dédié (séparé du thread Read)
            // applique le rapport sur _activeSonyStream.
            // Critique sous Windows (WinHidStream) : Read() avec ReadTimeout=0 appelle Win32 CancelIo(handle),
            // qui annule toutes les opérations I/O (y compris WriteFile) lancées par le MÊME thread et coupe le Bluetooth.
            // En isolant Write() sur son propre thread dédié, CancelIo() du lecteur n'interrompt jamais WriteFile().
            EnsureSonyOutputWriterThread();
            _pendingSonyOutput = new PendingSonyOutput(r, g, b, l2Mode, l2Force, r2Mode, r2Force, rumbleLeft, rumbleRight);
            _sonyOutputSignal.Set();
        }

        private void FlushSonyOutputImmediate(
            byte r, byte g, byte b,
            string l2Mode, int l2Force,
            string r2Mode, int r2Force,
            byte rumbleLeft = 0, byte rumbleRight = 0)
        {
            _pendingSonyOutput = new PendingSonyOutput(r, g, b, l2Mode, l2Force, r2Mode, r2Force, rumbleLeft, rumbleRight);
            WritePendingSonyOutputOnce(forceImmediate: true);
        }

        private void SonyOutputWriterLoop()
        {
            while (!_isShuttingDown)
            {
                _sonyOutputSignal.WaitOne(100);
                if (_isShuttingDown) break;
                if (_pendingSonyOutput == null) continue;
                WritePendingSonyOutputOnce(forceImmediate: false);
            }
        }

        private void WritePendingSonyOutputOnce(bool forceImmediate)
        {
            var pending = _pendingSonyOutput;
            if (pending == null) return;
            var request = pending;

            HidDevice? dev;
            HidStream? targetStream;
            lock (_sonyLock)
            {
                dev = _activeSonyDevice;
                targetStream = _activeSonyStream;
            }
            if (dev == null || targetStream == null) return;

            try
            {
                bool isBluetooth = IsBluetoothHid(dev);
                double minIntervalMs = isBluetooth ? 180.0 : 12.0;
                double elapsed = (DateTime.Now - _lastSonyOutputWrite).TotalMilliseconds;
                if (!forceImmediate && elapsed < minIntervalMs)
                {
                    int waitMs = (int)Math.Ceiling(minIntervalMs - elapsed);
                    if (waitMs > 0 && waitMs <= 200)
                        Thread.Sleep(waitMs);
                    // Relire la dernière requête coalescée après l'attente
                    pending = _pendingSonyOutput ?? pending;
                }

                Interlocked.CompareExchange(ref _pendingSonyOutput, null, pending);
                request = pending;
                if (!_isShuttingDown && _sonyBattery is { Level: > BatteryAlert.None })
                    pending = pending with { R = 255, G = 0, B = 0 };

                if (!forceImmediate &&
                    ReferenceEquals(_lastSonyOutputStream, targetStream) &&
                    _lastSentSonyOutput != null &&
                    _lastSentSonyOutput == pending &&
                    _lastSentMuteLed == _muteLedActive &&
                    pending.RumbleLeft == 0 && pending.RumbleRight == 0)
                {
                    return;
                }

                _lastSonyOutputWrite = DateTime.Now;
                bool muteLed = _muteLedActive;

                byte r = pending.R, g = pending.G, b = pending.B;
                // Tant que l'application ne se ferme pas, garde un minimum de luminosité si (0,0,0) est reçu par erreur
                if (!_isShuttingDown && r == 0 && g == 0 && b == 0)
                {
                    b = 8;
                }
                string l2Mode = pending.L2Mode, r2Mode = pending.R2Mode;
                int l2Force = pending.L2Force, r2Force = pending.R2Force;
                byte rumbleLeft = pending.RumbleLeft, rumbleRight = pending.RumbleRight;

                if (dev.ProductID == 0x05C4 || dev.ProductID == 0x09CC)
                {
                    if (!isBluetooth)
                    {
                        byte[] ds4Rep = new byte[32];
                        ds4Rep[0] = 0x05;
                        ds4Rep[1] = 0x07;
                        ds4Rep[4] = rumbleRight;
                        ds4Rep[5] = rumbleLeft;
                        ds4Rep[6] = r;
                        ds4Rep[7] = g;
                        ds4Rep[8] = b;
                        targetStream.Write(ds4Rep, 0, ds4Rep.Length);
                    }
                    else
                    {
                        byte[] ds4Bt = new byte[78];
                        ds4Bt[0] = 0x11;
                        ds4Bt[1] = 0xC0;
                        ds4Bt[3] = 0x07;
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
                        targetStream.Write(ds4Bt, 0, ds4Bt.Length);
                    }
                    _lastSentSonyOutput = pending;
                    _lastSonyOutputStream = targetStream;
                    _lastFailedSonyOutput = null;
                    _lastSentMuteLed = muteLed;
                    return;
                }

                byte[] common = new byte[DualSenseOutputReport.PayloadLength];
                common[0] = 0x0F;
                common[1] = 0x15;
                common[2] = rumbleRight;
                common[3] = rumbleLeft;
                common[8] = (byte)(muteLed ? 0x01 : 0x00);

                BuildTriggerEffectBlock(common, 10, r2Mode, r2Force);
                BuildTriggerEffectBlock(common, 21, l2Mode, l2Force);

                common[43] = 0x04;
                common[44] = r;
                common[45] = g;
                common[46] = b;

                if (isBluetooth && !ReferenceEquals(_sonyLightbarStream, targetStream) && !forceImmediate)
                {
                    _sonyBluetoothSequence = 0;
                    byte[] reset = DualSenseOutputReport.ResetBluetoothLightbar(_sonyBluetoothSequence++);
                    targetStream.Write(reset, 0, reset.Length);
                    _sonyLightbarStream = targetStream;
                }

                byte[] report = DualSenseOutputReport.Encode(common, isBluetooth, _sonyBluetoothSequence++);
                targetStream.Write(report, 0, report.Length);
                _lastSentSonyOutput = pending;
                _lastSonyOutputStream = targetStream;
                _lastFailedSonyOutput = null;
                _lastSentMuteLed = muteLed;
            }
            catch (Exception ex)
            {
                Log.Warn("Envoi du rapport de sortie Sony impossible", ex);
                // Retente une fois sur le même flux sans écraser une commande plus récente.
                if (!forceImmediate && ReferenceEquals(_activeSonyStream, targetStream) &&
                    !ReferenceEquals(_lastFailedSonyOutput, request))
                {
                    _lastFailedSonyOutput = request;
                    Interlocked.CompareExchange(ref _pendingSonyOutput, request, null);
                }
            }
        }

        private void ApplyActiveProfileHardwareEffects()
        {
            var prof = GetActiveProfileForSource(SourceSony);
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

        // Appelé sous _sonyLock lors d'une perte de connexion ou d'un reset explicite.
        private void CloseSonyInputStream()
        {
            lock (_inputStateLock)
                ResetControllerInput(SourceSony);

            try { _activeSonyStream?.Dispose(); } catch { }
            _activeSonyStream = null;
            _activeSonyDevice = null;
            _activeSonyPath = "";
            _lastSentSonyOutput = null;
            _lastFailedSonyOutput = null;
            _lastSonyOutputStream = null;
            _sonyLightbarStream = null;
            SetSonyBattery(null);
            _sonyDeviceListDirty = true;
        }

        private void ResetHidAndWakeSony()
        {
            lock (_sonyLock)
            {
                CloseSonyInputStream();
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

        private void RepairXboxWirelessDisconnect()
        {
            try
            {
                // Coupe toute vibration résiduelle sur les 4 slots XInput
                for (int i = 0; i < 4; i++)
                    XInput.SetVibration(i, 0, 0);

                // Désactive l'économie d'énergie Windows (PnPCapabilities = 24) sur les adaptateurs Bluetooth,
                // récepteurs Xbox Wireless et périphériques HID Bluetooth LE (cause n°1 de l'extinction après 2 s),
                // et redémarre le service Bluetooth si l'application a les droits ou via élévation PowerShell.
                string psCommand =
                    "$ErrorActionPreference='SilentlyContinue'; " +
                    "Get-PnpDevice | Where-Object { $_.FriendlyName -match 'Xbox|Bluetooth|XINPUT|GIP|BTHLE|Game Controller' } | ForEach-Object { " +
                    "  $p = \"HKLM:\\SYSTEM\\CurrentControlSet\\Enum\\$($_.InstanceId)\\Device Parameters\"; " +
                    "  if (Test-Path $p) { Set-ItemProperty -Path $p -Name 'PnPCapabilities' -Value 24 -Type DWord -Force; " +
                    "  Set-ItemProperty -Path $p -Name 'SelectiveSuspendEnabled' -Value 0 -Type DWord -Force; " +
                    "  Set-ItemProperty -Path $p -Name 'EnhancedPowerManagementEnabled' -Value 0 -Type DWord -Force } " +
                    "}; " +
                    "powercfg /SETACVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0; " +
                    "powercfg /SETDCVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0; " +
                    "powercfg /SETACTIVE SCHEME_CURRENT";

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"{psCommand}\"",
                    UseShellExecute = !ElevationHelper.IsCurrentProcessElevated,
                    CreateNoWindow = ElevationHelper.IsCurrentProcessElevated
                };
                if (!ElevationHelper.IsCurrentProcessElevated)
                    psi.Verb = "runas";

                try
                {
                    using var proc = System.Diagnostics.Process.Start(psi);
                    proc?.WaitForExit(6000);
                }
                catch (Exception ex)
                {
                    Log.Warn("Élévation refusée ou erreur lors du correctif d'alimentation Xbox", ex);
                }

                int xSlot = XInput.FindSlot(_selectedWebGpIndex);
                if (xSlot >= 0)
                {
                    SetControllerSelection(xSlot, "XBOX");
                    _xboxConnectedSince = DateTime.Now;
                    RebuildTrayControllersMenu();
                    RefreshHudOverlayContent();
                    SendToWebView(new { type = "selectControllerFromTray", index = xSlot });
                }

                _trayIcon.ShowBalloonTip(4000, "🎮 Correctif Anti-Extinction Xbox appliqué",
                    "Suspension sélective USB/Bluetooth et mise en veille des pilotes Xbox désactivées. " +
                    "Si la manette clignote encore en Bluetooth, retirez-la de Paramètres Bluetooth puis ré-appairez-la.",
                    ToolTipIcon.Info);

                SendHardwareInventoryToWeb();
            }
            catch (Exception ex)
            {
                Log.Warn("Réparation Xbox incomplète", ex);
            }
        }

        private bool PollSonyPersistentStream(byte[] buf, byte[] drainBuf, ref DateTime lastStickStep)
        {
            lock (_sonyLock)
            {
                try
                {
                    // Si aucun flux Sony n'est ouvert :
                    // 1. Ne JAMAIS scanner HidSharp en boucle (DeviceList.Local.GetHidDevices interroge tous les
                    //    périphériques HID de Windows et fait tomber le handshake Bluetooth LE de la manette Xbox).
                    // 2. Attendre au moins 4,5 s après TOUT événement PnP HID (_lastHidChangeEvent) et 6 s après
                    //    l'allumage d'une manette Xbox (_xboxConnectedSince) avant d'interroger HidSharp.
                    if (_activeSonyStream == null)
                    {
                        if (!_sonyDeviceListDirty)
                            return false;

                        double sinceHidEvent = (DateTime.Now - _lastHidChangeEvent).TotalMilliseconds;
                        if (sinceHidEvent >= 0 && sinceHidEvent < 4500)
                            return false;

                        double sinceXboxConn = _xboxConnectedSince == DateTime.MinValue
                            ? 99999
                            : (DateTime.Now - _xboxConnectedSince).TotalMilliseconds;
                        if (sinceXboxConn >= 0 && sinceXboxConn < 6000)
                            return false;

                        double sinceLastScan = (DateTime.Now - _lastDeviceScan).TotalMilliseconds;
                        if (sinceLastScan < 5000)
                            return false;
                    }

                    if (_activeSonyStream == null)
                    {
                        _lastDeviceScan = DateTime.Now;
                        _sonyDeviceListDirty = false;
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
                            _lastSentSonyOutput = null;
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
                    if (read == 0)
                    {
                        CloseSonyInputStream();
                        return false;
                    }
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

                    // Garde d'intégrité Bluetooth : rejette toute trame 78 octets dont le CRC-32 (0xA1) est corrompu
                    if ((buf[0] == 0x31 || buf[0] == 0x11) && read >= 78 && !DualSenseCrc32.IsValidInputReport(buf))
                    {
                        Log.Warn("Trame Bluetooth Sony ignorée (CRC-32 invalide)");
                        return true;
                    }

                    var state = SonyReportParser.Parse(buf, read, isDualSense, isBluetooth);
                    if (state.BatteryPercent is int percent)
                        SetSonyBattery(percent, state.Charging);

                    bool physTouchClick = state.PhysicalTouchClick;
                    bool[] cur;
                    bool buttonChanged;
                    float lx = state.LX, ly = state.LY, rx = state.RX, ry = state.RY, l2Val = state.L2, r2Val = state.R2;
                    bool t1Active = state.T1Active, t2Active = state.T2Active;
                    int t1X = state.T1X, t1Y = state.T1Y, t2X = state.T2X, t2Y = state.T2Y;

                    lock (_inputStateLock)
                    {
                        bool enabled = CanUseController(SourceSony);
                        if (enabled && isDualSense)
                            ProcessDualSenseMultiTouchpad(t1Active, t1X, t1Y, t2Active, t2X, t2Y);

                        // Le côté du clic reste figé jusqu'au relâchement physique.
                        if (physTouchClick && !_prevPhysTouchClick)
                            _touchClickLatchedRight = t1Active && t1X > TouchpadRightClickMinX;
                        _prevPhysTouchClick = physTouchClick;

                        cur = SonyReportParser.DecodeButtons(state, _touchClickLatchedRight);
                        buttonChanged = ProcessControllerButtons(cur, SourceSony, enabled);
                        if (enabled && (DateTime.Now - lastStickStep).TotalMilliseconds >= 8)
                        {
                            lastStickStep = DateTime.Now;
                            ProcessAnalogSticksMapping(lx, ly, rx, ry, SourceSony);
                        }
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
                    CloseSonyInputStream();
                    return false;
                }
            }
        }
    }
}
