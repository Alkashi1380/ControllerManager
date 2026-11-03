using System;
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
using Microsoft.Win32;
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
        public string name { get; set; } = "Jeux / Natif (Aucun raccourci)";
        public string exe { get; set; } = "default";
        public double deadzone { get; set; } = 0.08;
        public string leftStickMode { get; set; } = "native";
        public string rightStickMode { get; set; } = "native";
        public double mouseSensitivity { get; set; } = 14.0;
        public bool touchpadMouseEnabled { get; set; } = true;
        public double touchpadSensitivity { get; set; } = 1.2;
        public string l2TriggerMode { get; set; } = "off";
        public int l2TriggerForce { get; set; } = 180;
        public string r2TriggerMode { get; set; } = "off";
        public int r2TriggerForce { get; set; } = 180;
        public LightbarColor color { get; set; } = new();
        public Dictionary<string, string> buttonMapping { get; set; } = new();
    }

    public class LightbarColor
    {
        public byte r { get; set; } = 0;
        public byte g { get; set; } = 102;
        public byte b { get; set; } = 255;
    }

    public class ControllerSlotInfo
    {
        public int index { get; set; }
        public string type { get; set; } = "XBOX";
        public string label { get; set; } = "Manette";
    }

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

    public class HudOverlayForm : Form
    {
        private string _profileText = "Jeux / Natif";
        private string _batteryText = "100%";
        private string _modeText = "PS5 Direct";
        private Color _accentColor = Color.FromArgb(59, 130, 246);

        public HudOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(280, 54);
            BackColor = Color.FromArgb(14, 14, 18);
            Opacity = 0.88;
            DoubleBuffered = true;

            var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Location = new Point(screen.Right - Width - 20, 20);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020 | 0x00000008 | 0x00000080 | 0x08000000;
                return cp;
            }
        }

        public void UpdateHud(string profile, string battery, string mode, Color accent)
        {
            _profileText = profile;
            _batteryText = battery;
            _modeText = mode;
            _accentColor = accent;
            if (IsHandleCreated && Visible)
            {
                BeginInvoke(new Action(Invalidate));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var borderPen = new Pen(_accentColor, 2f);
            g.DrawRectangle(borderPen, 1, 1, Width - 2, Height - 2);

            using var barBrush = new SolidBrush(_accentColor);
            g.FillRectangle(barBrush, 0, 0, 6, Height);

            using var titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            using var subFont = new Font("Consolas", 8.5f, FontStyle.Regular);
            using var whiteBrush = new SolidBrush(Color.White);
            using var mutedBrush = new SolidBrush(Color.FromArgb(165, 175, 190));

            g.DrawString($"🎮 {_profileText}", titleFont, whiteBrush, new PointF(14, 7));
            g.DrawString($"🔋 {_batteryText}  •  {_modeText}", subFont, mutedBrush, new PointF(14, 29));
        }
    }

    public static class VirtualInput
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        public const byte VK_VOLUME_MUTE = 0xAD;
        public const byte VK_VOLUME_DOWN = 0xAE;
        public const byte VK_VOLUME_UP = 0xAF;
        public const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        public const byte VK_MEDIA_PREV_TRACK = 0xB1;
        public const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        public const byte VK_SPACE = 0x20;
        public const byte VK_RETURN = 0x0D;
        public const byte VK_ESCAPE = 0x1B;
        public const byte VK_MENU = 0x12;
        public const byte VK_TAB = 0x09;
        public const byte VK_LWIN = 0x5B;
        public const byte VK_D = 0x44;
        public const byte VK_SNAPSHOT = 0x2C;
        public const byte VK_F11 = 0x7A;

        public const byte VK_LEFT = 0x25;
        public const byte VK_UP = 0x26;
        public const byte VK_RIGHT = 0x27;
        public const byte VK_DOWN = 0x28;
        public const byte VK_Z = 0x5A;
        public const byte VK_Q = 0x51;
        public const byte VK_S = 0x53;

        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;

        public static void MoveMouseRelative(int dx, int dy)
        {
            if (dx != 0 || dy != 0)
                mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, 0);
        }

        public static void ScrollMouseWheel(int amount)
        {
            if (amount != 0)
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, amount, 0);
        }

        public static void LeftClick()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }

        public static void RightClick()
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
        }

        public static void SetKeyState(byte vk, bool down, ref bool prevDown)
        {
            if (down && !prevDown)
                keybd_event(vk, 0, 0, 0);
            else if (!down && prevDown)
                keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
            prevDown = down;
        }

        // Appui + relâchement : sans KEYUP, Windows garde la touche « enfoncée » dans son état clavier
        public static void TapKey(byte vk)
        {
            keybd_event(vk, 0, 0, 0);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
        }

        public static void ExecuteAction(string action)
        {
            switch (action)
            {
                case "Volume +": TapKey(VK_VOLUME_UP); break;
                case "Volume -": TapKey(VK_VOLUME_DOWN); break;
                case "Mute": TapKey(VK_VOLUME_MUTE); break;
                case "Play / Pause": TapKey(VK_MEDIA_PLAY_PAUSE); break;
                case "Piste Suivante": TapKey(VK_MEDIA_NEXT_TRACK); break;
                case "Piste Précédente": TapKey(VK_MEDIA_PREV_TRACK); break;
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
                case "Plein Écran (F11)":
                    keybd_event(VK_F11, 0, 0, 0);
                    keybd_event(VK_F11, 0, KEYEVENTF_KEYUP, 0);
                    break;
                case "Clic Gauche Souris":
                    LeftClick();
                    break;
                case "Clic Droit Souris":
                    RightClick();
                    break;
                case "Clic Molette (Milieu)":
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN | MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
                    break;
                case "Capture d'écran (Impr. Écran)":
                    keybd_event(VK_SNAPSHOT, 0, 0, 0);
                    keybd_event(VK_SNAPSHOT, 0, KEYEVENTF_KEYUP, 0);
                    break;
                case "Afficher le Bureau (Win + D)":
                    keybd_event(VK_LWIN, 0, 0, 0);
                    keybd_event(VK_D, 0, 0, 0);
                    keybd_event(VK_D, 0, KEYEVENTF_KEYUP, 0);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
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

    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ToolStripMenuItem _batteryMenuItem;
        private readonly ToolStripMenuItem _controllersRootMenuItem;
        private readonly ToolStripMenuItem _profilesRootMenuItem;
        private readonly ToolStripMenuItem _mappingToggleMenuItem;
        private readonly ToolStripMenuItem _hudToggleMenuItem;
        private readonly ToolStripMenuItem _startupToggleMenuItem;
        private readonly ToolStripMenuItem _driverToolsMenuItem;
        private readonly System.Windows.Forms.Timer _backgroundTimer;
        private Form? _dashboardForm;
        private WebView2? _webView;
        private HudOverlayForm? _hudForm;

        private List<GamepadProfile> _profiles = new();
        private string _currentProfileName = "";
        private bool _autoSwitchEnabled = false;
        private bool _mappingEnabled = true;
        private bool _hudEnabled = false;
        private bool _isDashboardFocused = false;
        private bool _muteLedActive = false;
        private string _lastDetectedExe = "explorer.exe";

        private List<ControllerSlotInfo> _connectedControllers = new();
        private int _selectedWebGpIndex = 99;
        private string _selectedControllerType = "PS5";

        private HidDevice? _activeSonyDevice;
        private HidStream? _activeSonyStream;
        private string _activeSonyPath = "";
        private readonly object _sonyLock = new();
        private DateTime _lastWebUiPush = DateTime.MinValue;
        private DateTime _lastDeviceScan = DateTime.MinValue;

        private double _mouseCarryX = 0;
        private double _mouseCarryY = 0;
        private int _scrollCooldown = 0;
        private readonly bool[] _stickKeyStates = new bool[8];

        // États Pavé Tactile Multipoint (1 doigt = Souris/Clic Gauche, 2 doigts = Scroll/Clic Droit)
        private bool _prevTouch1 = false;
        private bool _prevTouch2 = false;
        private int _prevT1X = 0;
        private int _prevT1Y = 0;
        private int _prevT2Y = 0;
        private double _tpScrollCarry = 0;
        private DateTime _touchStartTime = DateTime.MinValue;
        private int _touchTotalMove = 0;
        private int _maxFingersDuringTouch = 0;

        // Clic physique du pavé : largeur 0..1919, clic droit seulement au-delà de la zone neutre centrale
        private const int TouchpadRightClickMinX = 1060;
        private bool _prevPhysTouchClick = false;
        private bool _touchClickLatchedRight = false;

        private int _lastBatteryPercent = 100;
        private bool _lastBatteryCharging = false;
        private DateTime _lastBatteryCheck = DateTime.MinValue;

        private ushort _prevButtons;
        private double _prevLT;
        private double _prevRT;
        private readonly bool[] _prevSonyButtons = new bool[20];
        private readonly System.Threading.Thread _inputPollingThread;

        private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "GamepadOSManager";
        // Données utilisateur hors du dossier de l'exe (inscriptible même installé dans Program Files)
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ControllerManager");
        private static readonly string WebViewDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerManager", "WebView2");
        private static readonly HashSet<string> AllowedWindowsTools = new(StringComparer.OrdinalIgnoreCase)
        {
            "joy.cpl", "devmgmt.msc", "ms-settings:bluetooth"
        };
        private string ProfilesFilePath => Path.Combine(AppDataDir, "profiles.json");
        private string LegacyProfilesFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles.json");

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public TrayApplicationContext(bool startMinimized)
        {
            LoadProfilesFromDisk();
            _currentProfileName = _profiles.FirstOrDefault()?.name ?? "Jeux / Natif (Aucun raccourci)";

            // Abonnement nécessaire pour que Gamepad.Gamepads se remplisse (utilisé uniquement pour la batterie Xbox précise)
            Gamepad.GamepadAdded += (_, _) => { };

            var contextMenu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("🔧 Ouvrir le Dashboard", null, (_, _) => ShowDashboard())
            {
                Font = new Font(Control.DefaultFont, FontStyle.Bold)
            };
            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new ToolStripSeparator());

            _batteryMenuItem = new ToolStripMenuItem("🔋 Batterie : En attente...") { Enabled = false };
            contextMenu.Items.Add(_batteryMenuItem);

            _mappingToggleMenuItem = new ToolStripMenuItem("⌨️ Raccourcis & Sticks : Actifs")
            {
                Checked = _mappingEnabled
            };
            _mappingToggleMenuItem.Click += (_, _) =>
            {
                _mappingEnabled = !_mappingEnabled;
                UpdateMappingToggleMenu();
                SyncStateToWebView();
            };
            contextMenu.Items.Add(_mappingToggleMenuItem);

            _hudToggleMenuItem = new ToolStripMenuItem("📺 Afficher Overlay HUD en jeu")
            {
                Checked = _hudEnabled
            };
            _hudToggleMenuItem.Click += (_, _) => ToggleHudOverlay(!_hudEnabled);
            contextMenu.Items.Add(_hudToggleMenuItem);

            _startupToggleMenuItem = new ToolStripMenuItem("🚀 Lancer au démarrage de Windows")
            {
                Checked = IsStartupEnabled()
            };
            _startupToggleMenuItem.Click += (_, _) =>
            {
                SetStartupEnabled(!IsStartupEnabled());
                _startupToggleMenuItem.Checked = IsStartupEnabled();
                SyncStateToWebView();
            };
            contextMenu.Items.Add(_startupToggleMenuItem);

            _controllersRootMenuItem = new ToolStripMenuItem("🕹️ Manette Active : Auto");
            contextMenu.Items.Add(_controllersRootMenuItem);
            RebuildTrayControllersMenu();

            _profilesRootMenuItem = new ToolStripMenuItem($"🎮 Profil : {_currentProfileName}");
            contextMenu.Items.Add(_profilesRootMenuItem);
            RebuildTrayProfilesMenu();

            _driverToolsMenuItem = new ToolStripMenuItem("🛠️ Outils Pilotes & Reset Manettes");
            _driverToolsMenuItem.DropDownItems.Add("🟢 Relâcher Gâchettes L2/R2 (Remettre par défaut)", null, (_, _) => ResetTriggersToDefault());
            _driverToolsMenuItem.DropDownItems.Add("🔄 Reset Flux HID & Réveiller PS5 Bluetooth", null, (_, _) => ResetHidAndWakeSony());
            _driverToolsMenuItem.DropDownItems.Add("🧹 Purger Conflits (Fermer DS4Windows / InputMapper)", null, (_, _) => KillConflictingApps(false));
            _driverToolsMenuItem.DropDownItems.Add("🎛️ Ouvrir Contrôleurs de jeu Windows (joy.cpl)", null, (_, _) => OpenWindowsTool("joy.cpl"));
            _driverToolsMenuItem.DropDownItems.Add("⚙️ Ouvrir Gestionnaire de périphériques", null, (_, _) => OpenWindowsTool("devmgmt.msc"));
            _driverToolsMenuItem.DropDownItems.Add("📶 Paramètres Bluetooth Windows", null, (_, _) => OpenWindowsTool("ms-settings:bluetooth"));
            contextMenu.Items.Add(_driverToolsMenuItem);

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("❌ Quitter complètement", null, (_, _) => ExitThread());

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = $"Gamepad Manager - {_currentProfileName}",
                ContextMenuStrip = contextMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => ShowDashboard();

            _backgroundTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _backgroundTimer.Tick += BackgroundMonitoringTick;
            _backgroundTimer.Start();

            _inputPollingThread = new System.Threading.Thread(InputPollingLoop) { IsBackground = true };
            _inputPollingThread.Start();

            if (!startMinimized)
            {
                ShowDashboard();
            }
        }

        private void ResetTriggersToDefault()
        {
            var prof = _profiles.FirstOrDefault(p =>
                string.Equals(p.name, _currentProfileName, StringComparison.OrdinalIgnoreCase));
            var c = prof?.color ?? new LightbarColor { r = 0, g = 102, b = 255 };

            if (prof != null)
            {
                prof.l2TriggerMode = "off";
                prof.r2TriggerMode = "off";
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
            if (_selectedControllerType == "XBOX" && XInput.FindSlot(_selectedWebGpIndex) >= 0)
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
                catch { }
            }
        }

        private void ApplyActiveProfileHardwareEffects()
        {
            var prof = _profiles.FirstOrDefault(p =>
                string.Equals(p.name, _currentProfileName, StringComparison.OrdinalIgnoreCase));
            if (prof == null) return;

            var c = prof.color ?? new LightbarColor { r = 0, g = 102, b = 255 };
            SendDualSenseOutputReport(
                c.r, c.g, c.b,
                prof.l2TriggerMode ?? "off", prof.l2TriggerForce,
                prof.r2TriggerMode ?? "off", prof.r2TriggerForce
            );
        }

        // --- GESTION MULTIPOINT DU PAVÉ TACTILE (1 DOIGT = SOURIS/CLIC GAUCHE, 2 DOIGTS = SCROLL/CLIC DROIT) ---
        private void ProcessDualSenseMultiTouchpad(bool t1Active, int t1X, int t1Y, bool t2Active, int t2X, int t2Y)
        {
            var prof = _profiles.FirstOrDefault(p =>
                string.Equals(p.name, _currentProfileName, StringComparison.OrdinalIgnoreCase));

            if (prof == null || !prof.touchpadMouseEnabled || !_mappingEnabled)
            {
                _prevTouch1 = t1Active;
                _prevTouch2 = t2Active;
                return;
            }

            int currentFingers = (t1Active ? 1 : 0) + (t2Active ? 1 : 0);

            if (currentFingers > 0)
            {
                if (!_prevTouch1 && !_prevTouch2)
                {
                    _touchStartTime = DateTime.Now;
                    _touchTotalMove = 0;
                    _maxFingersDuringTouch = currentFingers;
                    _prevT1X = t1X;
                    _prevT1Y = t1Y;
                    _prevT2Y = t2Y;
                }
                else
                {
                    if (currentFingers > _maxFingersDuringTouch)
                        _maxFingersDuringTouch = currentFingers;

                    // CAS A : 2 DOIGTS POSÉS -> DÉFILEMENT VERTICAL (SCROLL MOLETTE)
                    if (t1Active && t2Active)
                    {
                        if (_prevTouch1 && _prevTouch2)
                        {
                            int dy = t1Y - _prevT1Y;
                            _touchTotalMove += Math.Abs(dy);

                            if (Math.Abs(dy) > 2 && Math.Abs(dy) < 200 && !_isDashboardFocused)
                            {
                                double sens = prof.touchpadSensitivity > 0.1 ? prof.touchpadSensitivity : 1.2;
                                _tpScrollCarry += (-dy) * 1.3 * sens;
                                int wheelSteps = (int)_tpScrollCarry;
                                if (Math.Abs(wheelSteps) >= 15)
                                {
                                    VirtualInput.ScrollMouseWheel(wheelSteps);
                                    _tpScrollCarry -= wheelSteps;
                                }
                            }
                        }
                        _prevT1X = t1X;
                        _prevT1Y = t1Y;
                        _prevT2Y = t2Y;
                    }
                    // CAS B : 1 SEUL DOIGT POSÉ -> DÉPLACEMENT CURSEUR SOURIS
                    else if (t1Active && !t2Active && _maxFingersDuringTouch == 1)
                    {
                        if (_prevTouch1)
                        {
                            int dx = t1X - _prevT1X;
                            int dy = t1Y - _prevT1Y;
                            int dist = Math.Abs(dx) + Math.Abs(dy);
                            _touchTotalMove += dist;

                            if (dist > 1 && dist < 240 && !_isDashboardFocused)
                            {
                                double sens = prof.touchpadSensitivity > 0.1 ? prof.touchpadSensitivity : 1.2;
                                int moveX = (int)Math.Round(dx * sens);
                                int moveY = (int)Math.Round(dy * sens);
                                VirtualInput.MoveMouseRelative(moveX, moveY);
                            }
                        }
                        _prevT1X = t1X;
                        _prevT1Y = t1Y;
                    }
                }
            }
            else if (_prevTouch1 || _prevTouch2)
            {
                // Tous les doigts viennent d'être relevés : détection du Tap-to-Click (1 doigt = Gauche, 2 doigts = Droit)
                double elapsedMs = (DateTime.Now - _touchStartTime).TotalMilliseconds;
                if (elapsedMs < 230 && _touchTotalMove < 28 && !_isDashboardFocused)
                {
                    if (_maxFingersDuringTouch >= 2)
                        VirtualInput.RightClick();
                    else
                        VirtualInput.LeftClick();
                }
                _maxFingersDuringTouch = 0;
            }

            _prevTouch1 = t1Active;
            _prevTouch2 = t2Active;
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                return key?.GetValue(AppRegistryName) != null;
            }
            catch { return false; }
        }

        private void SetStartupEnabled(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key == null) return;
                if (enable)
                {
                    string exePath = Application.ExecutablePath;
                    key.SetValue(AppRegistryName, $"\"{exePath}\" --tray");
                }
                else
                {
                    key.DeleteValue(AppRegistryName, false);
                }
            }
            catch { }
        }

        private void ToggleHudOverlay(bool enable)
        {
            _hudEnabled = enable;
            _hudToggleMenuItem.Checked = _hudEnabled;

            if (_hudEnabled)
            {
                if (_hudForm == null || _hudForm.IsDisposed)
                    _hudForm = new HudOverlayForm();
                RefreshHudOverlayContent();
                _hudForm.Show();
            }
            else
            {
                _hudForm?.Hide();
            }
            SyncStateToWebView();
        }

        private void RefreshHudOverlayContent()
        {
            if (_hudForm == null || _hudForm.IsDisposed || !_hudEnabled) return;
            var prof = _profiles.FirstOrDefault(p => string.Equals(p.name, _currentProfileName, StringComparison.OrdinalIgnoreCase));
            var c = prof?.color ?? new LightbarColor { r = 59, g = 130, b = 246 };
            _hudForm.UpdateHud(
                _currentProfileName,
                $"{_lastBatteryPercent}%{(_lastBatteryCharging ? " ⚡" : "")}",
                _selectedControllerType,
                Color.FromArgb(c.r, c.g, c.b)
            );
        }

        private void UpdateMappingToggleMenu()
        {
            _mappingToggleMenuItem.Checked = _mappingEnabled;
            _mappingToggleMenuItem.Text = _mappingEnabled
                ? "⌨️ Raccourcis & Sticks : Actifs"
                : "🔇 Raccourcis & Sticks : En Pause (Mode Test)";
        }

        private void ProcessAnalogSticksMapping(float lx, float ly, float rx, float ry)
        {
            if (!_mappingEnabled || _isDashboardFocused)
                return;

            var prof = _profiles.FirstOrDefault(p =>
                string.Equals(p.name, _currentProfileName, StringComparison.OrdinalIgnoreCase));
            if (prof == null) return;

            double dz = prof.deadzone > 0.01 ? prof.deadzone : 0.08;
            double sens = prof.mouseSensitivity > 1 ? prof.mouseSensitivity : 14.0;

            ApplySingleStickAction(prof.leftStickMode, lx, ly, dz, sens, isLeftStick: true);
            ApplySingleStickAction(prof.rightStickMode, rx, ry, dz, sens, isLeftStick: false);
        }

        private void ApplySingleStickAction(string mode, float x, float y, double dz, double sens, bool isLeftStick)
        {
            if (string.IsNullOrEmpty(mode) || mode == "native") return;

            double mag = Math.Sqrt(x * x + y * y);
            float fx = mag > dz ? x : 0f;
            float fy = mag > dz ? y : 0f;

            if (mode == "mouse")
            {
                if (mag > dz)
                {
                    // Les diagonales XInput dépassent 1.0 (jusqu'à ~1.26 mesuré) : on borne pour garder une vitesse uniforme
                    double norm = Math.Min(1.0, (mag - dz) / (1.0 - dz));
                    double speed = (norm * 0.45 + norm * norm * 0.55) * sens;
                    double dirX = x / mag;
                    double dirY = y / mag;

                    _mouseCarryX += dirX * speed;
                    _mouseCarryY += dirY * speed;

                    int moveX = (int)_mouseCarryX;
                    int moveY = (int)_mouseCarryY;
                    _mouseCarryX -= moveX;
                    _mouseCarryY -= moveY;

                    VirtualInput.MoveMouseRelative(moveX, moveY);
                }
            }
            else if (mode == "scroll")
            {
                if (_scrollCooldown > 0) _scrollCooldown--;
                if (Math.Abs(fy) > 0.25f && _scrollCooldown <= 0)
                {
                    int wheel = (int)(-fy * 90);
                    VirtualInput.ScrollMouseWheel(wheel);
                    _scrollCooldown = 3;
                }
            }
            else if (mode == "wasd" && isLeftStick)
            {
                VirtualInput.SetKeyState(VirtualInput.VK_Z, fy < -0.4f, ref _stickKeyStates[0]);
                VirtualInput.SetKeyState(VirtualInput.VK_S, fy > 0.4f, ref _stickKeyStates[1]);
                VirtualInput.SetKeyState(VirtualInput.VK_Q, fx < -0.4f, ref _stickKeyStates[2]);
                VirtualInput.SetKeyState(VirtualInput.VK_D, fx > 0.4f, ref _stickKeyStates[3]);
            }
            else if (mode == "arrows")
            {
                VirtualInput.SetKeyState(VirtualInput.VK_UP, fy < -0.4f, ref _stickKeyStates[4]);
                VirtualInput.SetKeyState(VirtualInput.VK_DOWN, fy > 0.4f, ref _stickKeyStates[5]);
                VirtualInput.SetKeyState(VirtualInput.VK_LEFT, fx < -0.4f, ref _stickKeyStates[6]);
                VirtualInput.SetKeyState(VirtualInput.VK_RIGHT, fx > 0.4f, ref _stickKeyStates[7]);
            }
        }

        // Le chemin d'une manette Bluetooth contient le GUID du service HID Bluetooth, pas forcément "bth"
        private const string BluetoothHidServiceGuid = "{00001124-0000-1000-8000-00805f9b34fb}";

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
            catch { }

            ApplyActiveProfileHardwareEffects();

            _trayIcon.ShowBalloonTip(2000, "Reset HID & Bluetooth effectué",
                "Le flux de la manette PS5 et ses gâchettes adaptatives ont été réinitialisés.",
                ToolTipIcon.Info);

            SendHardwareInventoryToWeb();
        }

        private void KillConflictingApps(bool killSteam)
        {
            int killed = 0;
            string[] targets = killSteam
                ? new[] { "DS4Windows", "steam", "InputMapper", "HidHideClient" }
                : new[] { "DS4Windows", "InputMapper" };

            foreach (var name in targets)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(); killed++; } catch { }
                }
            }

            ResetHidAndWakeSony();
            _trayIcon.ShowBalloonTip(2000, "Nettoyage des couches d'entrée",
                killed > 0 ? $"{killed} émulateur(s) arrêté(s)." : "Aucun émulateur tiers actif détecté.",
                ToolTipIcon.Info);
        }

        private void OpenWindowsTool(string command)
        {
            try
            {
                Process.Start(new ProcessStartInfo(command) { UseShellExecute = true });
            }
            catch { }
        }

        private void SendHardwareInventoryToWeb()
        {
            try
            {
                var sonyHidList = DeviceList.Local.GetHidDevices(0x054C)
                    .Where(d => d.ProductID == 0x0CE6 || d.ProductID == 0x0DF2 || d.ProductID == 0x05C4 || d.ProductID == 0x09CC)
                    .Select(d => new
                    {
                        name = d.ProductID == 0x0CE6 ? "Sony DualSense (PS5)" : "Sony DualShock 4 (PS4)",
                        pid = d.ProductID.ToString("X4"),
                        mode = IsBluetoothHid(d) ? "Bluetooth (Sans-fil)" : "USB (Câblé)",
                        maxReport = d.GetMaxInputReportLength(),
                        path = IsBluetoothHid(d) ? "Bluetooth BTHENUM" : "Port USB Direct",
                        isPreferred = (d.DevicePath == _activeSonyPath)
                    })
                    .ToList();

                int xinputCount = XInput.ConnectedCount();
                bool bothUsbAndBt = sonyHidList.Count >= 2;

                SendToWebView(new
                {
                    type = "hardwareInventory",
                    sonyDevices = sonyHidList,
                    xinputCount,
                    bothUsbAndBt
                });
            }
            catch { }
        }

        private void RebuildTrayControllersMenu()
        {
            _controllersRootMenuItem.DropDownItems.Clear();

            if (_connectedControllers.Count == 0)
            {
                var emptyItem = new ToolStripMenuItem("Aucune manette détectée") { Enabled = false };
                _controllersRootMenuItem.DropDownItems.Add(emptyItem);
                _controllersRootMenuItem.Text = "🕹️ Manette : Auto";
                return;
            }

            foreach (var ctrl in _connectedControllers)
            {
                bool isSel = ctrl.index == _selectedWebGpIndex;
                var item = new ToolStripMenuItem($"[Port #{ctrl.index}] {ctrl.label}")
                {
                    Checked = isSel
                };
                int targetIdx = ctrl.index;
                string targetType = ctrl.type;
                item.Click += (_, _) =>
                {
                    _selectedWebGpIndex = targetIdx;
                    _selectedControllerType = targetType;
                    _lastBatteryCheck = DateTime.MinValue;
                    RebuildTrayControllersMenu();
                    RefreshHudOverlayContent();
                    SendToWebView(new { type = "selectControllerFromTray", index = targetIdx });
                };
                _controllersRootMenuItem.DropDownItems.Add(item);
            }

            var current = _connectedControllers.FirstOrDefault(c => c.index == _selectedWebGpIndex) ?? _connectedControllers[0];
            _controllersRootMenuItem.Text = $"🕹️ Manette : #{current.index} {current.type}";
        }

        private void RebuildTrayProfilesMenu()
        {
            _profilesRootMenuItem.DropDownItems.Clear();

            var autoItem = new ToolStripMenuItem("🔄 Basculement Auto (Selon le jeu/app)")
            {
                Checked = _autoSwitchEnabled
            };
            autoItem.Click += (_, _) =>
            {
                _autoSwitchEnabled = !_autoSwitchEnabled;
                RebuildTrayProfilesMenu();
                SyncStateToWebView();
            };
            _profilesRootMenuItem.DropDownItems.Add(autoItem);
            _profilesRootMenuItem.DropDownItems.Add(new ToolStripSeparator());

            foreach (var prof in _profiles)
            {
                var item = new ToolStripMenuItem($"{prof.name} ({prof.exe})")
                {
                    Checked = string.Equals(prof.name, _currentProfileName, StringComparison.OrdinalIgnoreCase)
                };
                string targetName = prof.name;
                item.Click += (_, _) =>
                {
                    _autoSwitchEnabled = false;
                    ApplyProfile(targetName, notifyUser: true);
                };
                _profilesRootMenuItem.DropDownItems.Add(item);
            }

            string modeTag = _autoSwitchEnabled ? "Auto" : "Verrouillé";
            _profilesRootMenuItem.Text = $"🎮 Profil : {_currentProfileName} [{modeTag}]";
        }

        private void ApplyProfile(string profileName, bool notifyUser)
        {
            var matched = _profiles.FirstOrDefault(p =>
                string.Equals(p.name, profileName, StringComparison.OrdinalIgnoreCase));
            if (matched == null) return;

            bool changed = !string.Equals(_currentProfileName, matched.name, StringComparison.OrdinalIgnoreCase);
            _currentProfileName = matched.name;

            ApplyActiveProfileHardwareEffects();

            RebuildTrayProfilesMenu();
            RefreshHudOverlayContent();
            _trayIcon.Text = $"Gamepad Manager - {_currentProfileName}";

            if (notifyUser && changed)
            {
                _trayIcon.ShowBalloonTip(1500, "Profil Manette Activé",
                    $"Profil actif : {matched.name}", ToolTipIcon.Info);
            }

            SyncStateToWebView();
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
                Text = "Gamepad OS — Console Diagnostic, Périphériques & Profils",
                Size = new Size(1300, 900),
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(7, 7, 9)
            };

            _webView = new WebView2 { Dock = DockStyle.Fill };
            _dashboardForm.Controls.Add(_webView);

            _dashboardForm.FormClosing += (_, _) =>
            {
                _isDashboardFocused = false;
                _webView?.Dispose();
                _webView = null;
                _trayIcon.ShowBalloonTip(2000, "Mode Tray Actif",
                    "Dashboard fermé. Le contrôle souris/clavier, pavé tactile multipoint et profils restent actifs.",
                    ToolTipIcon.Info);
            };

            _dashboardForm.Show();

            var envOptions = new CoreWebView2EnvironmentOptions("--disable-features=HardwareMediaKeyHandling,GamepadButtonAxisEvents");
            Directory.CreateDirectory(WebViewDataDir);
            var env = await CoreWebView2Environment.CreateAsync(null, WebViewDataDir, envOptions);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                SyncStateToWebView();
                SendHardwareInventoryToWeb();
            };

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
        }

        private void InputPollingLoop()
        {
            byte[] sonyBuffer = new byte[128];
            byte[] drainBuffer = new byte[128];
            DateTime lastStickStep = DateTime.Now;
            int xSlot = -1;
            DateTime lastXInputScan = DateTime.MinValue;

            while (true)
            {
                // Interroger un slot XInput déconnecté est coûteux : on ne re-scanne qu'une fois par seconde
                if ((DateTime.Now - lastXInputScan).TotalMilliseconds > 1000)
                {
                    lastXInputScan = DateTime.Now;
                    xSlot = XInput.FindSlot(_selectedWebGpIndex);
                }

                if (_selectedControllerType == "PS5" || _selectedControllerType == "PS4" || _selectedWebGpIndex == 99 || xSlot < 0)
                {
                    if (PollSonyPersistentStream(sonyBuffer, drainBuffer, ref lastStickStep))
                    {
                        System.Threading.Thread.Sleep(1);
                        continue;
                    }
                }

                System.Threading.Thread.Sleep(8);
                if (xSlot < 0) continue;

                if (XInput.XInputGetState((uint)xSlot, out var state) != XInput.ERROR_SUCCESS)
                {
                    xSlot = -1;
                    continue;
                }

                var pad = state.Gamepad;
                ushort buttons = pad.wButtons;

                CheckButton(buttons, XInput.A, 0);
                CheckButton(buttons, XInput.B, 1);
                CheckButton(buttons, XInput.X, 2);
                CheckButton(buttons, XInput.Y, 3);
                CheckButton(buttons, XInput.LEFT_SHOULDER, 4);
                CheckButton(buttons, XInput.RIGHT_SHOULDER, 5);
                CheckButton(buttons, XInput.BACK, 8);
                CheckButton(buttons, XInput.START, 9);
                CheckButton(buttons, XInput.LEFT_THUMB, 10);
                CheckButton(buttons, XInput.RIGHT_THUMB, 11);
                CheckButton(buttons, XInput.DPAD_UP, 12);
                CheckButton(buttons, XInput.DPAD_DOWN, 13);
                CheckButton(buttons, XInput.DPAD_LEFT, 14);
                CheckButton(buttons, XInput.DPAD_RIGHT, 15);

                CheckTrigger(pad.bLeftTrigger / 255.0, ref _prevLT, 6);
                CheckTrigger(pad.bRightTrigger / 255.0, ref _prevRT, 7);

                if ((DateTime.Now - lastStickStep).TotalMilliseconds >= 8)
                {
                    lastStickStep = DateTime.Now;
                    ProcessAnalogSticksMapping(
                        Math.Max(-1f, pad.sThumbLX / 32767f),
                        -Math.Max(-1f, pad.sThumbLY / 32767f),
                        Math.Max(-1f, pad.sThumbRX / 32767f),
                        -Math.Max(-1f, pad.sThumbRY / 32767f)
                    );
                }

                _prevButtons = buttons;
            }
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

                    float lx = 0, ly = 0, rx = 0, ry = 0, l2Val = 0, r2Val = 0;
                    byte b0 = 0, b1 = 0, b2 = 0;
                    bool t1Active = false, t2Active = false;
                    int t1X = 0, t1Y = 0, t2X = 0, t2Y = 0;

                    if (isDualSense)
                    {
                        if (buf[0] == 0x31)
                        {
                            lx = (buf[2] - 128f) / 128f;
                            ly = (buf[3] - 128f) / 128f;
                            rx = (buf[4] - 128f) / 128f;
                            ry = (buf[5] - 128f) / 128f;
                            l2Val = buf[6] / 255f;
                            r2Val = buf[7] / 255f;
                            b0 = buf[9];
                            b1 = buf[10];
                            b2 = buf[11];

                            // Touchpad DualSense Bluetooth 0x31 : Doigt 1 (34..37), Doigt 2 (38..41)
                            if (read > 41)
                            {
                                t1Active = (buf[34] & 0x80) == 0;
                                t1X = ((buf[36] & 0x0F) << 8) | buf[35];
                                t1Y = (buf[37] << 4) | ((buf[36] & 0xF0) >> 4);

                                t2Active = (buf[38] & 0x80) == 0;
                                t2X = ((buf[40] & 0x0F) << 8) | buf[39];
                                t2Y = (buf[41] << 4) | ((buf[40] & 0xF0) >> 4);
                            }

                            // Batterie DualSense Bluetooth (octet 54)
                            if (read > 54)
                            {
                                int rawBat = buf[54] & 0x0F;
                                _lastBatteryCharging = ((buf[54] >> 4) & 0x0F) == 1;
                                _lastBatteryPercent = Math.Min(100, rawBat * 10 + 5);
                            }
                        }
                        else if (isBluetooth && buf[0] == 0x01)
                        {
                            lx = (buf[1] - 128f) / 128f;
                            ly = (buf[2] - 128f) / 128f;
                            rx = (buf[3] - 128f) / 128f;
                            ry = (buf[4] - 128f) / 128f;
                            b0 = buf[5];
                            b1 = buf[6];
                            b2 = buf[7];
                            l2Val = read > 8 ? buf[8] / 255f : ((b1 & 0x04) != 0 ? 1f : 0f);
                            r2Val = read > 9 ? buf[9] / 255f : ((b1 & 0x08) != 0 ? 1f : 0f);
                        }
                        else
                        {
                            // DualSense USB 0x01 : Doigt 1 (33..36), Doigt 2 (37..40)
                            lx = (buf[1] - 128f) / 128f;
                            ly = (buf[2] - 128f) / 128f;
                            rx = (buf[3] - 128f) / 128f;
                            ry = (buf[4] - 128f) / 128f;
                            l2Val = buf[5] / 255f;
                            r2Val = buf[6] / 255f;
                            b0 = buf[8];
                            b1 = buf[9];
                            b2 = buf[10];

                            if (read > 40)
                            {
                                t1Active = (buf[33] & 0x80) == 0;
                                t1X = ((buf[35] & 0x0F) << 8) | buf[34];
                                t1Y = (buf[36] << 4) | ((buf[35] & 0xF0) >> 4);

                                t2Active = (buf[37] & 0x80) == 0;
                                t2X = ((buf[39] & 0x0F) << 8) | buf[38];
                                t2Y = (buf[40] << 4) | ((buf[39] & 0xF0) >> 4);
                            }

                            // Batterie DualSense USB (octet 53)
                            if (read > 53)
                            {
                                int rawBat = buf[53] & 0x0F;
                                _lastBatteryCharging = ((buf[53] >> 4) & 0x0F) == 1;
                                _lastBatteryPercent = Math.Min(100, rawBat * 10 + 5);
                            }
                        }

                        ProcessDualSenseMultiTouchpad(t1Active, t1X, t1Y, t2Active, t2X, t2Y);
                    }
                    else
                    {
                        int off = (buf[0] == 0x11) ? 2 : 0;
                        lx = (buf[1 + off] - 128f) / 128f;
                        ly = (buf[2 + off] - 128f) / 128f;
                        rx = (buf[3 + off] - 128f) / 128f;
                        ry = (buf[4 + off] - 128f) / 128f;
                        b0 = buf[5 + off];
                        b1 = buf[6 + off];
                        b2 = buf[7 + off];
                        l2Val = buf[8 + off] / 255f;
                        r2Val = buf[9 + off] / 255f;
                    }

                    int hat = b0 & 0x0F;
                    bool dUp = (hat == 0 || hat == 1 || hat == 7);
                    bool dRight = (hat == 1 || hat == 2 || hat == 3);
                    bool dDown = (hat == 3 || hat == 4 || hat == 5);
                    bool dLeft = (hat == 5 || hat == 6 || hat == 7);

                    // Décodage des 4 nouveaux boutons PS5 :
                    // 16: Bouton PS (b2 & 0x01)
                    // 17: Clic Pavé Tactile Gauche (physClick + doigt à gauche ou au centre)
                    // 18: Clic Pavé Tactile Droit (physClick commencé avec le doigt à X > 1060, au-delà de la zone neutre)
                    // 19: Bouton Mute Mic (b2 & 0x04)
                    bool psButton = (b2 & 0x01) != 0;
                    bool physTouchClick = (b2 & 0x02) != 0;
                    bool muteMicButton = (b2 & 0x04) != 0;

                    // Le côté du clic est figé au début de l'appui : un doigt qui glisse pendant l'appui
                    // ne doit pas relâcher "gauche" pour déclencher "droit". La bande centrale compte comme gauche.
                    if (physTouchClick && !_prevPhysTouchClick)
                        _touchClickLatchedRight = t1Active && t1X > TouchpadRightClickMinX;
                    _prevPhysTouchClick = physTouchClick;

                    bool tpClickLeft = physTouchClick && !_touchClickLatchedRight;
                    bool tpClickRight = physTouchClick && _touchClickLatchedRight;

                    bool[] cur = new bool[20];
                    cur[0] = (b0 & 0x20) != 0; // Croix
                    cur[1] = (b0 & 0x40) != 0; // Rond
                    cur[2] = (b0 & 0x10) != 0; // Carré
                    cur[3] = (b0 & 0x80) != 0; // Triangle
                    cur[4] = (b1 & 0x01) != 0; // L1
                    cur[5] = (b1 & 0x02) != 0; // R1
                    cur[6] = (b1 & 0x04) != 0 || l2Val > 0.5f; // L2
                    cur[7] = (b1 & 0x08) != 0 || r2Val > 0.5f; // R2
                    cur[8] = (b1 & 0x10) != 0; // Share / Create
                    cur[9] = (b1 & 0x20) != 0; // Options
                    cur[10] = (b1 & 0x40) != 0; // L3
                    cur[11] = (b1 & 0x80) != 0; // R3
                    cur[12] = dUp;
                    cur[13] = dDown;
                    cur[14] = dLeft;
                    cur[15] = dRight;
                    cur[16] = psButton;        // 16: Bouton PS
                    cur[17] = tpClickLeft;     // 17: Clic Pavé Gauche
                    cur[18] = tpClickRight;    // 18: Clic Pavé Droit
                    cur[19] = muteMicButton;   // 19: Bouton Mute Mic

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
                            ExecuteMapping(i);
                        }
                        _prevSonyButtons[i] = cur[i];
                    }

                    if ((DateTime.Now - lastStickStep).TotalMilliseconds >= 8)
                    {
                        lastStickStep = DateTime.Now;
                        ProcessAnalogSticksMapping(lx, ly, rx, ry);
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
                catch
                {
                    try { _activeSonyStream?.Dispose(); } catch { }
                    _activeSonyStream = null;
                    _activeSonyDevice = null;
                    _activeSonyPath = "";
                    return false;
                }
            }
        }

        private void CheckButton(ushort current, ushort flag, int jsIndex)
        {
            bool isPressed = (current & flag) != 0;
            bool wasPressed = (_prevButtons & flag) != 0;
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
            if (!_mappingEnabled || _isDashboardFocused)
                return;

            var activeProfile = _profiles.FirstOrDefault(p =>
                string.Equals(p.name, _currentProfileName, StringComparison.OrdinalIgnoreCase));

            if (activeProfile != null &&
                activeProfile.buttonMapping != null &&
                activeProfile.buttonMapping.TryGetValue(jsIndex.ToString(), out string? action) &&
                !string.IsNullOrEmpty(action) &&
                action != "Défaut (Native)")
            {
                VirtualInput.ExecuteAction(action);
            }
        }

        private void CheckBatteryAndNotify()
        {
            if ((DateTime.Now - _lastBatteryCheck).TotalSeconds < 10)
                return;

            _lastBatteryCheck = DateTime.Now;

            try
            {
                int xSlot = XInput.FindSlot(_selectedWebGpIndex);
                if (_selectedControllerType == "XBOX" && xSlot >= 0)
                {
                    // Le rapport batterie WGI (précis au %) reste disponible en arrière-plan, contrairement aux lectures d'entrées
                    bool gotPrecise = false;
                    if (Gamepad.Gamepads.Count > 0)
                    {
                        var gp = (_selectedWebGpIndex >= 0 && _selectedWebGpIndex < Gamepad.Gamepads.Count)
                            ? Gamepad.Gamepads[_selectedWebGpIndex]
                            : Gamepad.Gamepads[0];

                        var report = gp.TryGetBatteryReport();
                        if (report != null && report.RemainingCapacityInMilliwattHours.HasValue && report.FullChargeCapacityInMilliwattHours.HasValue)
                        {
                            int full = report.FullChargeCapacityInMilliwattHours.Value;
                            if (full > 0)
                            {
                                _lastBatteryPercent = (int)Math.Round((double)report.RemainingCapacityInMilliwattHours.Value / full * 100);
                                gotPrecise = true;
                            }
                            _lastBatteryCharging = report.Status == Windows.System.Power.BatteryStatus.Charging;
                        }
                    }

                    // Repli XInput : seulement 4 niveaux (vide / faible / moyen / plein)
                    if (!gotPrecise &&
                        XInput.XInputGetBatteryInformation((uint)xSlot, 0, out var bat) == XInput.ERROR_SUCCESS &&
                        bat.BatteryType != XInput.BATTERY_TYPE_DISCONNECTED)
                    {
                        _lastBatteryPercent = bat.BatteryLevel switch { 0 => 5, 1 => 30, 2 => 65, _ => 100 };
                        _lastBatteryCharging = bat.BatteryType == XInput.BATTERY_TYPE_WIRED;
                    }
                }
            }
            catch { }

            if (_lastBatteryPercent >= 0)
            {
                _batteryMenuItem.Text = $"🔋 Batterie ({_selectedControllerType}) : {_lastBatteryPercent}% {(_lastBatteryCharging ? "(En charge)" : "")}";
                RefreshHudOverlayContent();
                SendToWebView(new { type = "battery", percent = _lastBatteryPercent, charging = _lastBatteryCharging });
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
                _lastDetectedExe = exeName;

                bool isSelf = string.Equals(proc.ProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);
                _isDashboardFocused = isSelf;
                if (isSelf) return;

                if (!_autoSwitchEnabled)
                {
                    SendToWebView(new
                    {
                        type = "activeProcess",
                        exe = exeName,
                        profileName = _currentProfileName,
                        autoSwitch = _autoSwitchEnabled,
                        mappingEnabled = _mappingEnabled
                    });
                    return;
                }

                var matched = _profiles.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(p.exe) &&
                    p.exe.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Any(e => string.Equals(e, exeName, StringComparison.OrdinalIgnoreCase)));

                if (matched != null && !string.Equals(matched.name, _currentProfileName, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyProfile(matched.name, notifyUser: true);
                }
                else
                {
                    SendToWebView(new
                    {
                        type = "activeProcess",
                        exe = exeName,
                        profileName = _currentProfileName,
                        autoSwitch = _autoSwitchEnabled,
                        mappingEnabled = _mappingEnabled
                    });
                }
            }
            catch { }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string? json = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string action = root.GetProperty("action").GetString() ?? "";

                if (action == "requestInitState")
                {
                    SyncStateToWebView();
                    SendHardwareInventoryToWeb();
                }
                else if (action == "setMappingEnabled")
                {
                    _mappingEnabled = root.GetProperty("enabled").GetBoolean();
                    UpdateMappingToggleMenu();
                    SyncStateToWebView();
                }
                else if (action == "setHudOverlay")
                {
                    bool en = root.GetProperty("enabled").GetBoolean();
                    ToggleHudOverlay(en);
                }
                else if (action == "setWindowsStartup")
                {
                    bool en = root.GetProperty("enabled").GetBoolean();
                    SetStartupEnabled(en);
                    _startupToggleMenuItem.Checked = IsStartupEnabled();
                    SyncStateToWebView();
                }
                else if (action == "refreshHardwareInventory")
                {
                    SendHardwareInventoryToWeb();
                }
                else if (action == "resetHidDevices")
                {
                    ResetHidAndWakeSony();
                }
                else if (action == "resetTriggersDefault")
                {
                    ResetTriggersToDefault();
                }
                else if (action == "killConflicts")
                {
                    bool includeSteam = root.TryGetProperty("includeSteam", out var sProp) && sProp.GetBoolean();
                    KillConflictingApps(includeSteam);
                }
                else if (action == "openWinTool")
                {
                    // Liste blanche : la page ne doit jamais pouvoir lancer un programme arbitraire
                    string tool = root.GetProperty("tool").GetString() ?? "";
                    if (AllowedWindowsTools.Contains(tool))
                        OpenWindowsTool(tool);
                }
                else if (action == "selectController")
                {
                    _selectedWebGpIndex = root.GetProperty("index").GetInt32();
                    _selectedControllerType = root.GetProperty("controllerType").GetString() ?? "PS5";
                    _lastBatteryCheck = DateTime.MinValue;
                    RebuildTrayControllersMenu();
                    RefreshHudOverlayContent();
                }
                else if (action == "updateControllerList")
                {
                    string rawList = root.GetProperty("controllers").GetRawText();
                    _connectedControllers = JsonSerializer.Deserialize<List<ControllerSlotInfo>>(rawList) ?? new();
                    if (root.TryGetProperty("activeIndex", out var actIdx) && actIdx.ValueKind == JsonValueKind.Number)
                    {
                        _selectedWebGpIndex = actIdx.GetInt32();
                        var cur = _connectedControllers.FirstOrDefault(c => c.index == _selectedWebGpIndex);
                        if (cur != null) _selectedControllerType = cur.type;
                    }
                    RebuildTrayControllersMenu();
                }
                else if (action == "selectProfile")
                {
                    string profName = root.GetProperty("profileName").GetString() ?? "";
                    if (root.TryGetProperty("autoSwitch", out var autoProp))
                    {
                        _autoSwitchEnabled = autoProp.GetBoolean();
                    }
                    ApplyProfile(profName, notifyUser: false);
                }
                else if (action == "setAutoSwitch")
                {
                    _autoSwitchEnabled = root.GetProperty("enabled").GetBoolean();
                    RebuildTrayProfilesMenu();
                    SyncStateToWebView();
                }
                else if (action == "syncProfiles")
                {
                    string rawProfiles = root.GetProperty("profiles").GetRawText();
                    Directory.CreateDirectory(AppDataDir);
                    File.WriteAllText(ProfilesFilePath, rawProfiles);
                    LoadProfilesFromDisk();

                    if (root.TryGetProperty("activeProfileName", out var activeProp))
                    {
                        string requestedActive = activeProp.GetString() ?? "";
                        if (!string.IsNullOrEmpty(requestedActive))
                            _currentProfileName = requestedActive;
                    }

                    ApplyActiveProfileHardwareEffects();
                    RebuildTrayProfilesMenu();
                    RefreshHudOverlayContent();
                    SyncStateToWebView();
                }
                else if (action == "applyDualSenseTriggers")
                {
                    string l2M = root.GetProperty("l2Mode").GetString() ?? "off";
                    int l2F = root.GetProperty("l2Force").GetInt32();
                    string r2M = root.GetProperty("r2Mode").GetString() ?? "off";
                    int r2F = root.GetProperty("r2Force").GetInt32();
                    var c = root.GetProperty("color");

                    SendDualSenseOutputReport(
                        (byte)c.GetProperty("r").GetInt32(),
                        (byte)c.GetProperty("g").GetInt32(),
                        (byte)c.GetProperty("b").GetInt32(),
                        l2M, l2F, r2M, r2F
                    );
                }
                else if (action == "testRumble")
                {
                    double left = root.GetProperty("left").GetDouble();
                    double right = root.GetProperty("right").GetDouble();

                    XInput.SetVibration(XInput.FindSlot(_selectedWebGpIndex), left, right);

                    var prof = _profiles.FirstOrDefault(p => string.Equals(p.name, _currentProfileName, StringComparison.OrdinalIgnoreCase));
                    var col = prof?.color ?? new LightbarColor { r = 0, g = 102, b = 255 };
                    SendDualSenseOutputReport(
                        col.r, col.g, col.b,
                        prof?.l2TriggerMode ?? "off", prof?.l2TriggerForce ?? 180,
                        prof?.r2TriggerMode ?? "off", prof?.r2TriggerForce ?? 180,
                        (byte)(left * 255), (byte)(right * 255)
                    );
                }
                else if (action == "updateLightbar")
                {
                    var c = root.GetProperty("color");
                    var prof = _profiles.FirstOrDefault(p => string.Equals(p.name, _currentProfileName, StringComparison.OrdinalIgnoreCase));
                    SendDualSenseOutputReport(
                        (byte)c.GetProperty("r").GetInt32(),
                        (byte)c.GetProperty("g").GetInt32(),
                        (byte)c.GetProperty("b").GetInt32(),
                        prof?.l2TriggerMode ?? "off", prof?.l2TriggerForce ?? 180,
                        prof?.r2TriggerMode ?? "off", prof?.r2TriggerForce ?? 180
                    );
                }
            }
            catch { }
        }

        private void SyncStateToWebView()
        {
            SendToWebView(new
            {
                type = "initState",
                profiles = _profiles,
                activeProfileName = _currentProfileName,
                autoSwitch = _autoSwitchEnabled,
                mappingEnabled = _mappingEnabled,
                hudEnabled = _hudEnabled,
                startupEnabled = IsStartupEnabled(),
                exe = _lastDetectedExe
            });
        }

        private void SendToWebView(object payload)
        {
            if (_webView != null && !_webView.IsDisposed)
            {
                try
                {
                    string json = JsonSerializer.Serialize(payload);
                    _webView.BeginInvoke(new Action(() =>
                    {
                        try { _webView.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
                    }));
                }
                catch { }
            }
        }

        private void LoadProfilesFromDisk()
        {
            // Repli sur l'ancien emplacement (à côté de l'exe) tant qu'aucune sauvegarde n'existe dans %APPDATA%
            string sourcePath = File.Exists(ProfilesFilePath) ? ProfilesFilePath : LegacyProfilesFilePath;
            if (File.Exists(sourcePath))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<List<GamepadProfile>>(File.ReadAllText(sourcePath));
                    if (loaded != null && loaded.Count > 0)
                    {
                        foreach (var p in loaded)
                        {
                            if (p.exe.Contains("msedge.exe", StringComparison.OrdinalIgnoreCase) ||
                                p.exe.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                p.exe = "vlc.exe";
                            }
                            if (string.IsNullOrEmpty(p.leftStickMode)) p.leftStickMode = "native";
                            if (string.IsNullOrEmpty(p.rightStickMode)) p.rightStickMode = "native";
                            if (p.mouseSensitivity <= 0) p.mouseSensitivity = 14.0;
                            if (p.touchpadSensitivity <= 0) p.touchpadSensitivity = 1.2;
                            if (string.IsNullOrEmpty(p.l2TriggerMode)) p.l2TriggerMode = "off";
                            if (string.IsNullOrEmpty(p.r2TriggerMode)) p.r2TriggerMode = "off";
                            if (p.l2TriggerForce <= 0) p.l2TriggerForce = 180;
                            if (p.r2TriggerForce <= 0) p.r2TriggerForce = 180;
                        }
                        _profiles = loaded;
                        return;
                    }
                }
                catch { }
            }

            _profiles = new List<GamepadProfile>
            {
                new()
                {
                    name = "Jeux / Natif (Aucun raccourci)",
                    exe = "cs2.exe, cod.exe, apex.exe",
                    deadzone = 0.04,
                    leftStickMode = "native",
                    rightStickMode = "native",
                    mouseSensitivity = 14.0,
                    touchpadMouseEnabled = false,
                    touchpadSensitivity = 1.2,
                    l2TriggerMode = "off",
                    l2TriggerForce = 180,
                    r2TriggerMode = "off",
                    r2TriggerForce = 180,
                    color = new LightbarColor { r = 239, g = 68, b = 68 },
                    buttonMapping = new Dictionary<string, string>()
                },
                new()
                {
                    name = "Multimédia (VLC + Trackpad PS5)",
                    exe = "vlc.exe",
                    deadzone = 0.08,
                    leftStickMode = "mouse",
                    rightStickMode = "scroll",
                    mouseSensitivity = 12.0,
                    touchpadMouseEnabled = true,
                    touchpadSensitivity = 1.3,
                    l2TriggerMode = "off",
                    l2TriggerForce = 150,
                    r2TriggerMode = "off",
                    r2TriggerForce = 150,
                    color = new LightbarColor { r = 6, g = 182, b = 212 },
                    buttonMapping = new Dictionary<string, string>
                    {
                        { "0", "Play / Pause" },
                        { "1", "Plein Écran (F11)" },
                        { "2", "Mute" },
                        { "4", "Piste Précédente" },
                        { "5", "Piste Suivante" },
                        { "12", "Volume +" },
                        { "13", "Volume -" },
                        { "17", "Play / Pause" },
                        { "18", "Plein Écran (F11)" },
                        { "19", "Mute" }
                    }
                },
                new()
                {
                    name = "Bureau (Trackpad 2 Doigts & Souris)",
                    exe = "calc.exe",
                    deadzone = 0.09,
                    leftStickMode = "mouse",
                    rightStickMode = "scroll",
                    mouseSensitivity = 15.0,
                    touchpadMouseEnabled = true,
                    touchpadSensitivity = 1.4,
                    l2TriggerMode = "off",
                    l2TriggerForce = 150,
                    r2TriggerMode = "off",
                    r2TriggerForce = 150,
                    color = new LightbarColor { r = 0, g = 102, b = 255 },
                    buttonMapping = new Dictionary<string, string>
                    {
                        { "0", "Clic Gauche Souris" },
                        { "1", "Clic Droit Souris" },
                        { "2", "Touche Entrée" },
                        { "3", "Echap" },
                        { "4", "Alt + Tab" },
                        { "16", "Afficher le Bureau (Win + D)" },
                        { "17", "Clic Gauche Souris" },
                        { "18", "Clic Droit Souris" },
                        { "19", "Mute" }
                    }
                }
            };
        }

        protected override void ExitThreadCore()
        {
            _backgroundTimer.Stop();
            _muteLedActive = false;
            try { SendDualSenseOutputReport(0, 0, 0, "off", 0, "off", 0); } catch { }
            lock (_sonyLock)
            {
                try { _activeSonyStream?.Dispose(); } catch { }
            }
            _hudForm?.Close();
            _trayIcon.Visible = false;
            _webView?.Dispose();
            base.ExitThreadCore();
        }
    }
}