using System.Diagnostics;
using System.Runtime.InteropServices;
using HidSharp;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using Windows.Gaming.Input;

namespace ControllerManager
{
    public partial class TrayApplicationContext : ApplicationContext
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
        // Le profil actif est repéré par son id (stable), pas par son nom (modifiable, pas forcément unique)
        private string _currentProfileId = "";

        private GamepadProfile? GetActiveProfile() =>
            _profiles.FirstOrDefault(p => p.id == _currentProfileId);

        private string ActiveProfileName => GetActiveProfile()?.name ?? "Jeux / Natif (Aucun raccourci)";

        // Résout un profil envoyé par la page : par id d'abord, puis par nom (anciennes versions de l'UI)
        private GamepadProfile? FindProfile(string? id, string? name) =>
            (!string.IsNullOrEmpty(id) ? _profiles.FirstOrDefault(p => p.id == id) : null) ??
            (!string.IsNullOrEmpty(name)
                ? _profiles.FirstOrDefault(p => string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase))
                : null);
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
            _currentProfileId = _profiles.FirstOrDefault()?.id ?? "";

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

            _profilesRootMenuItem = new ToolStripMenuItem($"🎮 Profil : {ActiveProfileName}");
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
                Text = $"Gamepad Manager - {ActiveProfileName}",
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
            var prof = GetActiveProfile();
            var c = prof?.color ?? new LightbarColor { r = 59, g = 130, b = 246 };
            _hudForm.UpdateHud(
                ActiveProfileName,
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
                    Checked = prof.id == _currentProfileId
                };
                string targetId = prof.id;
                item.Click += (_, _) =>
                {
                    _autoSwitchEnabled = false;
                    ApplyProfile(targetId, notifyUser: true);
                };
                _profilesRootMenuItem.DropDownItems.Add(item);
            }

            string modeTag = _autoSwitchEnabled ? "Auto" : "Verrouillé";
            _profilesRootMenuItem.Text = $"🎮 Profil : {ActiveProfileName} [{modeTag}]";
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
                        profileId = _currentProfileId,
                        profileName = ActiveProfileName,
                        autoSwitch = _autoSwitchEnabled,
                        mappingEnabled = _mappingEnabled
                    });
                    return;
                }

                var matched = _profiles.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(p.exe) &&
                    p.exe.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Any(e => string.Equals(e, exeName, StringComparison.OrdinalIgnoreCase)));

                if (matched != null && matched.id != _currentProfileId)
                {
                    ApplyProfile(matched.id, notifyUser: true);
                }
                else
                {
                    SendToWebView(new
                    {
                        type = "activeProcess",
                        exe = exeName,
                        profileId = _currentProfileId,
                        profileName = ActiveProfileName,
                        autoSwitch = _autoSwitchEnabled,
                        mappingEnabled = _mappingEnabled
                    });
                }
            }
            catch { }
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
