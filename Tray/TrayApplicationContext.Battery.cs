using System.Media;

namespace ControllerManager
{
    public partial class TrayApplicationContext
    {
        private enum BatteryAlert { None, Low, Critical }

        private sealed record BatteryReading(int Percent, bool Charging)
        {
            public BatteryAlert Level => Charging || Percent > 20 ? BatteryAlert.None
                : Percent > 10 ? BatteryAlert.Low : BatteryAlert.Critical;
        }

        private volatile BatteryReading? _sonyBattery;
        private BatteryReading? _xboxBattery;
        private DateTime _lastXboxBatteryCheck = DateTime.MinValue;
        private int _xboxBatterySlot = -1;
        private BatteryAlert _lastSonyBatteryAlert;
        private BatteryAlert _lastXboxBatteryAlert;
        private HudOverlayForm? _batteryAlertForm;
        private System.Windows.Forms.Timer? _batteryAlertTimer;

        private void SetSonyBattery(int? percent, bool charging = false)
        {
            var previous = _sonyBattery;
            if (previous?.Percent == percent && (percent == null || previous?.Charging == charging)) return;

            var current = percent is int value ? new BatteryReading(value, charging) : null;
            _sonyBattery = current;
            if (current != null && (previous is { Level: > BatteryAlert.None }) != (current.Level > BatteryAlert.None))
                ApplyActiveProfileHardwareEffects();
            RunOnUiThread(UpdateBatteryIndicators);
        }

        private void CheckBatteryAndNotify()
        {
            int slot = _lastXInputSlot;
            if (slot != _xboxBatterySlot)
            {
                _xboxBatterySlot = slot;
                _xboxBattery = null;
                _lastXboxBatteryAlert = BatteryAlert.None;
                _lastXboxBatteryCheck = DateTime.MinValue;
                UpdateBatteryIndicators();
            }
            if (slot < 0) return;

            var now = DateTime.Now;
            double connectedSeconds = (now - _xboxConnectedSince).TotalSeconds;
            // Conserve le délai de stabilisation du pilote Bluetooth avant toute lecture de batterie.
            if (_xboxConnectedSince == DateTime.MinValue || connectedSeconds < 5 ||
                (now - _lastXboxBatteryCheck).TotalSeconds < 10) return;

            _lastXboxBatteryCheck = now;
            try
            {
                if (XInput.XInputGetBatteryInformation((uint)slot, 0, out var info) != XInput.ERROR_SUCCESS ||
                    info.BatteryType == XInput.BATTERY_TYPE_DISCONNECTED || _lastXInputSlot != slot) return;

                bool wired = info.BatteryType == XInput.BATTERY_TYPE_WIRED;
                int percent = wired ? 100 : info.BatteryLevel switch { 0 => 5, 1 => 30, 2 => 65, _ => 100 };
                if (percent == 5 && connectedSeconds < 12)
                {
                    // Un premier résultat « vide » peut être transitoire au réveil.
                    _lastXboxBatteryCheck = DateTime.MinValue;
                    return;
                }
                _xboxBattery = new BatteryReading(percent, wired);
                UpdateBatteryIndicators();
            }
            catch (Exception ex) { Log.Warn("Lecture de la batterie Xbox impossible", ex); }
        }

        private (BatteryReading? Reading, string Device) GetDisplayedBattery()
        {
            // L'alerte Sony reste visible même quand le diagnostic affiche la manette Xbox.
            var sony = _sonyBattery;
            if (sony is { Level: > BatteryAlert.None })
                return (sony, "PS5");
            if (_xboxBattery is { Level: > BatteryAlert.None } || SelectedControllerSource == SourceXbox)
                return (_xboxBattery, "Xbox");
            return (sony, "PS5");
        }

        private void UpdateBatteryIndicators()
        {
            if (_isShuttingDown) return;
            var (battery, device) = GetDisplayedBattery();
            string status = battery == null ? "En attente" : $"{battery.Percent}%{(battery.Charging ? " (En charge)" : "")}";
            string icon = battery is { Level: > BatteryAlert.None } ? "🪫" : "🔋";
            _batteryMenuItem.Text = $"{icon} Batterie {device} : {status}";
            UpdateTrayIcon();
            RefreshHudOverlayContent();
            UpdateTrayTooltipText();
            SendBatteryToWebView();
            NotifyBatteryAlert(_sonyBattery, "PS5", ref _lastSonyBatteryAlert);
            NotifyBatteryAlert(_xboxBattery, "Xbox", ref _lastXboxBatteryAlert);
        }

        private void SendBatteryToWebView()
        {
            var (battery, device) = GetDisplayedBattery();
            SendToWebView(new
            {
                type = "battery",
                percent = battery?.Percent,
                charging = battery?.Charging ?? false,
                device,
                lowBatteryAlert = battery is { Level: > BatteryAlert.None }
            });
        }

        private void NotifyBatteryAlert(BatteryReading? battery, string device, ref BatteryAlert lastAlert)
        {
            if (battery == null || battery.Level == BatteryAlert.None)
            {
                lastAlert = BatteryAlert.None;
                return;
            }
            if (battery.Level <= lastAlert) return;
            lastAlert = battery.Level;

            string severity = lastAlert == BatteryAlert.Critical ? "critique" : "faible";
            Log.Info($"Batterie {device} {severity} : {battery.Percent}%");
            ShowBatteryAlert($"{device} · Batterie {severity}", battery.Percent);
            SystemSounds.Exclamation.Play();
        }

        private void ShowBatteryAlert(string title, int percent)
        {
            _batteryAlertForm ??= new HudOverlayForm();
            _batteryAlertForm.UpdateHud(title, $"{percent}%", "Branchez la manette", Color.Red, lowBatteryAlert: true);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            _batteryAlertForm.Location = new Point(area.Right - _batteryAlertForm.Width - 20, area.Bottom - _batteryAlertForm.Height - 20);
            _batteryAlertForm.Show();

            if (_batteryAlertTimer == null)
            {
                _batteryAlertTimer = new System.Windows.Forms.Timer { Interval = 6000 };
                _batteryAlertTimer.Tick += (_, _) =>
                {
                    _batteryAlertTimer.Stop();
                    _batteryAlertForm.Hide();
                };
            }
            _batteryAlertTimer.Stop();
            _batteryAlertTimer.Start();
        }
    }
}
