# Re-génération directe et propre du fichier Program.cs sous ce nom exact
program_cs_code = """using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using HidSharp;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Windows.Gaming.Input;

namespace ControllerManager
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            bool startMinimized = args.Contains("--tray");
            Application.Run(new TrayApplicationContext(startMinimized));
        }
    }

    public class GamepadProfile
    {
        public string name { get; set; } = "Default";
        public string exe { get; set; } = "explorer.exe";
        public double deadzone { get; set; } = 0.08;
        public LightbarColor color { get; set; } = new();
        public Dictionary<string, string> buttonMapping { get; set; } = new();
    }

    public class LightbarColor
    {
        public byte r { get; set; } = 0;
        public byte g { get; set; } = 0;
        public byte b { get; set; } = 255;
    }

    public static class VirtualInput
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        public const byte VK_VOLUME_MUTE = 0xAD;
        public const byte VK_VOLUME_DOWN = 0xAE;
        public const byte VK_VOLUME_UP = 0xAF;
        public const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        public const byte VK_SPACE = 0x20;
        public const byte VK_RETURN = 0x0D;
        public const byte VK_ESCAPE = 0x1B;
        public const byte VK_MENU = 0x12;
        public const byte VK_TAB = 0x09;

        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x02;
        public const uint MOUSEEVENTF_LEFTUP = 0x04;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
        public const uint MOUSEEVENTF_RIGHTUP = 0x10;

        public static void ExecuteAction(string action)
        {
            switch (action)
            {
                case "Volume +": keybd_event(VK_VOLUME_UP, 0, 0, 0); break;
                case "Volume -": keybd_event(VK_VOLUME_DOWN, 0, 0, 0); break;
                case "Mute": keybd_event(VK_VOLUME_MUTE, 0, 0, 0); break;
                case "Play / Pause": keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, 0); break;
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
                case "Clic Gauche Souris": 
                    mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, 0, 0, 0, 0); 
                    break;
                case "Clic Droit Souris": 
                    mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0); 
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

    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly System.Windows.Forms.Timer _backgroundTimer;
        private Form? _dashboardForm;
        private WebView2? _webView;

        private List<GamepadProfile> _profiles = new();
        private string _currentProfileName = "Default";
        private int _lastBatteryPercent = 100;
        private bool _alert20Sent = false;
        private bool _alert10Sent = false;

        private DateTime _lastBatteryCheck = DateTime.MinValue;
        private DateTime _gamepadConnectedSince = DateTime.MinValue;

        private GamepadButtons _prevButtons;
        private double _prevLT;
        private double _prevRT;
        private readonly System.Threading.Thread _inputPollingThread;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public TrayApplicationContext(bool startMinimized)
        {
            LoadProfilesFromDisk();

            Gamepad.GamepadAdded += (_, _) => _gamepadConnectedSince = DateTime.Now;
            if (Gamepad.Gamepads.Count > 0)
            {
                _gamepadConnectedSince = DateTime.Now;
            }

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("🔧 Ouvrir le Dashboard de Test", null, (_, _) => ShowDashboard());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("🔋 Batterie : En attente...", null)!.Enabled = false;
            contextMenu.Items.Add("🎮 Profil actif : Default", null)!.Enabled = false;
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("❌ Quitter complètement", null, (_, _) => ExitThread());

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Gamepad Manager (Actif en arrière-plan)",
                ContextMenuStrip = contextMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => ShowDashboard();

            _backgroundTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _backgroundTimer.Tick += BackgroundMonitoringTick;
            _backgroundTimer.Start();

            _inputPollingThread = new System.Threading.Thread(InputPollingLoop) { IsBackground = true };
            _inputPollingThread.Start();

            if (!startMinimized)
            {
                ShowDashboard();
            }
        }

        private async void ShowDashboard()
        {
            if (_dashboardForm != null && !_dashboardForm.IsDisposed)
            {
                _dashboardForm.Activate();
                return;
            }

            _dashboardForm = new Form
            {
                Text = "Gamepad Diagnostic & Profile Manager",
                Size = new Size(1180, 820),
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(9, 9, 11)
            };

            _webView = new WebView2 { Dock = DockStyle.Fill };
            _dashboardForm.Controls.Add(_webView);

            _dashboardForm.FormClosing += (_, e) =>
            {
                _webView?.Dispose();
                _webView = null;
                _trayIcon.ShowBalloonTip(2000, "Mode Tray Actif",
                    "Le Dashboard est fermé. La surveillance de la batterie et des profils continue en tâche de fond.",
                    ToolTipIcon.Info);
            };

            _dashboardForm.Show();

            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UI", "index.html");
            if (File.Exists(htmlPath))
            {
                _webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            }
        }

        private void BackgroundMonitoringTick(object? sender, EventArgs e)
        {
            CheckBatteryAndNotify();
            CheckForegroundGameAndSwitchProfile();
            CheckInputLayerConflicts();
        }

        private void InputPollingLoop()
        {
            while (true)
            {
                System.Threading.Thread.Sleep(16);
                var gp = Gamepad.Gamepads.FirstOrDefault();
                if (gp == null) continue;

                var reading = gp.GetCurrentReading();
                var buttons = reading.Buttons;

                CheckButton(buttons, GamepadButtons.A, 0);
                CheckButton(buttons, GamepadButtons.B, 1);
                CheckButton(buttons, GamepadButtons.X, 2);
                CheckButton(buttons, GamepadButtons.Y, 3);
                CheckButton(buttons, GamepadButtons.LeftShoulder, 4);
                CheckButton(buttons, GamepadButtons.RightShoulder, 5);
                CheckButton(buttons, GamepadButtons.View, 8);
                CheckButton(buttons, GamepadButtons.Menu, 9);
                CheckButton(buttons, GamepadButtons.LeftThumbstick, 10);
                CheckButton(buttons, GamepadButtons.RightThumbstick, 11);
                CheckButton(buttons, GamepadButtons.DPadUp, 12);
                CheckButton(buttons, GamepadButtons.DPadDown, 13);
                CheckButton(buttons, GamepadButtons.DPadLeft, 14);
                CheckButton(buttons, GamepadButtons.DPadRight, 15);

                CheckTrigger(reading.LeftTrigger, ref _prevLT, 6);
                CheckTrigger(reading.RightTrigger, ref _prevRT, 7);

                _prevButtons = buttons;
            }
        }

        private void CheckButton(GamepadButtons current, GamepadButtons flag, int jsIndex)
        {
            bool isPressed = current.HasFlag(flag);
            bool wasPressed = _prevButtons.HasFlag(flag);
            if (isPressed && !wasPressed)
                ExecuteMapping(jsIndex);
        }

        private void CheckTrigger(double currentValue, ref double prevValue, int jsIndex)
        {
            bool isPressed = currentValue > 0.5;
            bool wasPressed = prevValue > 0.5;
            if (isPressed && !wasPressed)
                ExecuteMapping(jsIndex);
            prevValue = currentValue;
        }

        private void ExecuteMapping(int jsIndex)
        {
            var activeProfile = _profiles.FirstOrDefault(p => p.name == _currentProfileName);
            if (activeProfile != null &&
                activeProfile.buttonMapping.TryGetValue(jsIndex.ToString(), out string action) &&
                action != "Défaut (Native)")
            {
                VirtualInput.ExecuteAction(action);
            }
        }

        private void CheckBatteryAndNotify()
        {
            if ((DateTime.Now - _lastBatteryCheck).TotalSeconds < 30)
                return;

            if (Gamepad.Gamepads.Count > 0 && (DateTime.Now - _gamepadConnectedSince).TotalSeconds < 8)
                return;

            _lastBatteryCheck = DateTime.Now;
            int percent = -1;
            bool isCharging = false;

            try
            {
                var gp = Gamepad.Gamepads.FirstOrDefault();
                if (gp != null)
                {
                    var report = gp.TryGetBatteryReport();
                    if (report != null && report.RemainingCapacityInMilliwattHours.HasValue && report.FullChargeCapacityInMilliwattHours.HasValue)
                    {
                        int full = report.FullChargeCapacityInMilliwattHours.Value;
                        if (full > 0)
                            percent = (int)Math.Round((double)report.RemainingCapacityInMilliwattHours.Value / full * 100);
                        isCharging = report.Status == Windows.System.Power.BatteryStatus.Charging;
                    }
                }
                else
                {
                    var ds4 = DeviceList.Local.GetHidDevices(0x054C)
                        .FirstOrDefault(d => d.ProductID == 0x05C4 || d.ProductID == 0x09CC);
                    if (ds4 != null && ds4.TryOpen(out HidStream stream))
                    {
                        using (stream)
                        {
                            stream.ReadTimeout = 50;
                            byte[] buf = new byte[ds4.GetMaxInputReportLength()];
                            int read = stream.Read(buf, 0, buf.Length);
                            if (read > 30 && buf[0] == 0x01)
                            {
                                int raw = buf[30] & 0x0F;
                                isCharging = (buf[30] & 0x10) != 0;
                                percent = Math.Min(100, raw * 10);
                            }
                        }
                    }
                }
            }
            catch { }

            if (percent >= 0)
            {
                _lastBatteryPercent = percent;
                _trayIcon.ContextMenuStrip!.Items[2].Text = $"🔋 Batterie : {percent}% {(isCharging ? "(En charge)" : "")}";
                _trayIcon.Text = $"Gamepad Manager - Batterie : {percent}%";

                if (!isCharging && percent <= 20 && !_alert20Sent)
                {
                    _alert20Sent = true;
                    _trayIcon.ShowBalloonTip(4000, "⚠️ Batterie Manette Faible (20%)",
                        "Pensez à brancher votre manette prochainement.", ToolTipIcon.Warning);
                }
                if (!isCharging && percent <= 10 && !_alert10Sent)
                {
                    _alert10Sent = true;
                    _trayIcon.ShowBalloonTip(5000, "🚨 Batterie Critique (10%)",
                        "Votre manette risque de s'éteindre.", ToolTipIcon.Error);
                    SendDS4LightbarHid(255, 0, 0);
                }
                if (isCharging && percent >= 95)
                {
                    _alert20Sent = _alert10Sent = false;
                }

                SendToWebView(new { type = "battery", percent, charging = isCharging });
            }
        }

        private void CheckForegroundGameAndSwitchProfile()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                string exeName = proc.ProcessName + ".exe";

                var matched = _profiles.FirstOrDefault(p =>
                    string.Equals(p.exe, exeName, StringComparison.OrdinalIgnoreCase))
                    ?? _profiles.FirstOrDefault();

                if (matched != null && matched.name != _currentProfileName)
                {
                    _currentProfileName = matched.name;
                    _trayIcon.ContextMenuStrip!.Items[3].Text = $"🎮 Profil actif : {matched.name}";

                    if (matched.color != null)
                        SendDS4LightbarHid(matched.color.r, matched.color.g, matched.color.b);

                    _trayIcon.ShowBalloonTip(2000, "Profil Manette Activé",
                        $"Profil '{matched.name}' appliqué pour {exeName}.", ToolTipIcon.Info);

                    SendToWebView(new { type = "activeProcess", exe = exeName, profileName = matched.name });
                }
            }
            catch { }
        }

        private void CheckInputLayerConflicts()
        {
            bool steamRunning = Process.GetProcessesByName("steam").Length > 0;
            bool ds4Running = Process.GetProcessesByName("DS4Windows").Length > 0;

            bool conflict = steamRunning && ds4Running;
            string msg = conflict
                ? "⚠️ Steam et DS4Windows tournent simultanément. Activez 'Hide DS4 Controller' dans DS4Windows ou désactivez PlayStation Configuration Support dans Steam pour éviter les doubles inputs."
                : "Aucune double couche conflictuelle détectée.";

            SendToWebView(new { type = "conflict", hasConflict = conflict, message = msg });
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string json = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string action = root.GetProperty("action").GetString() ?? "";

            if (action == "testRumble")
            {
                double left = root.GetProperty("left").GetDouble();
                double right = root.GetProperty("right").GetDouble();
                var gp = Gamepad.Gamepads.FirstOrDefault();
                if (gp != null)
                {
                    gp.Vibration = new GamepadVibration { LeftMotor = left, RightMotor = right };
                }
            }
            else if (action == "updateLightbar")
            {
                var c = root.GetProperty("color");
                SendDS4LightbarHid((byte)c.GetProperty("r").GetInt32(),
                                   (byte)c.GetProperty("g").GetInt32(),
                                   (byte)c.GetProperty("b").GetInt32());
            }
            else if (action == "syncProfiles")
            {
                string rawProfiles = root.GetProperty("profiles").GetRawText();
                File.WriteAllText("profiles.json", rawProfiles);
                LoadProfilesFromDisk();
            }
        }

        private void SendDS4LightbarHid(byte r, byte g, byte b)
        {
            if (Gamepad.Gamepads.Count > 0)
                return;

            try
            {
                var ds4 = DeviceList.Local.GetHidDevices(0x054C)
                    .FirstOrDefault(d => d.ProductID == 0x05C4 || d.ProductID == 0x09CC);

                if (ds4 != null && ds4.TryOpen(out HidStream stream))
                {
                    using (stream)
                    {
                        byte[] report = new byte[32];
                        report[0] = 0x05;
                        report[1] = 0xFF;
                        report[2] = 0x04;
                        report[6] = r;
                        report[7] = g;
                        report[8] = b;
                        stream.Write(report, 0, report.Length);
                    }
                }
            }
            catch { }
        }

        private void SendToWebView(object payload)
        {
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
            }
        }

        private void LoadProfilesFromDisk()
        {
            if (File.Exists("profiles.json"))
            {
                try
                {
                    _profiles = JsonSerializer.Deserialize<List<GamepadProfile>>(File.ReadAllText("profiles.json")) ?? new();
                    return;
                }
                catch { }
            }
            _profiles = new List<GamepadProfile>
            {
                new() { name = "Jeux (FPS/Action)", exe = "game.exe", deadzone = 0.05, color = new() { r = 255, g = 0, b = 0 }, buttonMapping = new() },
                new() { name = "Multimédia (VLC/Netflix)", exe = "vlc.exe", deadzone = 0.08, color = new() { r = 0, g = 255, b: 255 }, buttonMapping = new() { { "12", "Volume +" }, { "13", "Volume -" }, { "0", "Play / Pause" } } },
                new() { name = "Bureau (Navigation)", exe = "explorer.exe", deadzone = 0.10, color = new() { r = 0, g = 0, b = 255 }, buttonMapping = new() { { "0", "Clic Gauche Souris" }, { "1", "Clic Droit Souris" } } }
            };
        }

        protected override void ExitThreadCore()
        {
            _backgroundTimer.Stop();
            _trayIcon.Visible = false;
            _webView?.Dispose();
            base.ExitThreadCore();
        }
    }
}
"""

with open("Program.cs", "w", encoding="utf-8") as f:
    f.write(program_cs_code)

print("Program.cs re-generated successfully.")