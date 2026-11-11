using System.Diagnostics;
using System.Runtime.InteropServices;
using HidSharp;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

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
        private readonly ToolStripMenuItem _multiControllerMenuItem;
        private readonly ToolStripMenuItem _driverToolsMenuItem;
        private readonly System.Windows.Forms.Timer _backgroundTimer;
        private Form? _dashboardForm;
        private WebView2? _webView;
        private HudOverlayForm? _hudForm;
        private volatile bool _isShuttingDown = false;
        private bool _isOpeningDashboard = false;

        // volatile : lus par le thread d'entrées pendant que le thread UI les remplace (on remplace la liste entière, jamais en place)
        private volatile List<GamepadProfile> _profiles = new();
        // Le profil actif est séparé par manette (Sony PS5/PS4 vs Manette Xbox) pour que les deux manettes ne se marchent jamais dessus
        private volatile string _currentProfileId = "";
        private volatile string _currentXboxProfileId = "";

        private GamepadProfile? GetActiveProfileForSource(int source)
        {
            string targetId = source == SourceXbox ? _currentXboxProfileId : _currentProfileId;
            return _profiles.FirstOrDefault(p => p.id == targetId)
                   ?? _profiles.FirstOrDefault(p => p.id == _currentProfileId)
                   ?? _profiles.FirstOrDefault();
        }

        private GamepadProfile? GetActiveProfile() =>
            _selectedControllerType == "XBOX"
                ? GetActiveProfileForSource(SourceXbox)
                : GetActiveProfileForSource(SourceSony);

        private string ActiveProfileName => GetActiveProfile()?.name ?? "Jeux / Natif (Aucun raccourci)";

        // Résout un profil envoyé par la page : par id d'abord, puis par nom (anciennes versions de l'UI)
        private GamepadProfile? FindProfile(string? id, string? name) =>
            (!string.IsNullOrEmpty(id) ? _profiles.FirstOrDefault(p => p.id == id) : null) ??
            (!string.IsNullOrEmpty(name)
                ? _profiles.FirstOrDefault(p => string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase))
                : null);
        private bool _autoSwitchEnabled = false;
        private volatile bool _mappingEnabled = true;
        private bool _hudEnabled = false;
        private volatile bool _isDashboardFocused = false;
        private volatile bool _muteLedActive = false;
        private string _lastDetectedExe = "explorer.exe";

        private List<ControllerSlotInfo> _connectedControllers = new();
        private volatile int _selectedWebGpIndex = 99;
        private volatile string _selectedControllerType = "PS5";

        private HidDevice? _activeSonyDevice;
        private HidStream? _activeSonyStream;
        private string _activeSonyPath = "";
        private readonly object _sonyLock = new();
        private DateTime _lastWebUiPush = DateTime.MinValue;
        private DateTime _lastDeviceScan = DateTime.MinValue;
        private volatile bool _sonyDeviceListDirty = true;
        private DateTime _lastHidChangeEvent = DateTime.MinValue;
        private DateTime _xboxConnectedSince = DateTime.MinValue;

        private double _mouseCarryX = 0;
        private double _mouseCarryY = 0;
        // État des sticks séparé par manette (index SourceSony / SourceXbox) : deux manettes actives ne se marchent pas dessus
        private const int SourceSony = 0;
        private const int SourceXbox = 1;
        private readonly int[] _scrollCooldown = new int[4]; // [manette * 2 + stick]
        private readonly bool[][] _stickKeyStates = { new bool[16], new bool[16] }; // [manette][stick * 8 + touche]

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

        private SynchronizationContext? _uiContext;

        // Les menus et bulles WinForms ne doivent être touchés que depuis le thread UI
        private void RunOnUiThread(Action action)
        {
            if (_uiContext != null) _uiContext.Post(_ => action(), null);
            else action();
        }

        private ushort _prevButtons;
        private double _prevLT;
        private double _prevRT;
        private readonly bool[] _prevSonyButtons = new bool[20];
        private readonly System.Threading.Thread _inputPollingThread;

        private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "GamepadOSManager";
        private const string SettingsRegistryKey = @"SOFTWARE\ControllerManager";

        // Sony + Xbox pilotent en même temps (sinon seule la manette sélectionnée agit). Mémorisé dans HKCU.
        private volatile bool _multiControllerEnabled = ReadMultiControllerSetting();

        private static bool ReadMultiControllerSetting()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryKey, false);
                var val = key?.GetValue("MultiController");
                return val is int v ? v != 0 : true;
            }
            catch { return true; }
        }

        private void SetMultiControllerEnabled(bool enabled)
        {
            lock (_inputStateLock)
            {
                _multiControllerEnabled = enabled;
                if (!enabled)
                    ReleaseControllerOutputs(SelectedControllerSource == SourceSony ? SourceXbox : SourceSony);
            }
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(SettingsRegistryKey);
                key.SetValue("MultiController", enabled ? 1 : 0, RegistryValueKind.DWord);
            }
            catch (Exception ex) { Log.Warn("Sauvegarde du mode multi-manettes impossible", ex); }
            Log.Info($"Mode multi-manettes {(enabled ? "activé" : "désactivé")}");
        }
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

        // Notification Windows à chaque changement de fenêtre active : bascule de profil immédiate
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        // Référence conservée : sinon le GC libère le délégué et Windows appelle un pointeur invalide
        private WinEventDelegate? _foregroundHookProc;
        private IntPtr _foregroundHook = IntPtr.Zero;

        public TrayApplicationContext(bool startMinimized)
        {
            LoadProfilesFromDisk();
            _currentProfileId = _profiles.FirstOrDefault()?.id ?? "";
            _currentXboxProfileId = _currentProfileId;

            // Écoute les changements de périphériques HID au lieu de scanner tout le bus HID toutes les secondes
            // (un scan HidSharp pendant le handshake Bluetooth LE d'une manette Xbox peut faire tomber la liaison GATT)
            DeviceList.Local.Changed += (_, _) =>
            {
                _sonyDeviceListDirty = true;
                _lastHidChangeEvent = DateTime.Now;
            };

            var contextMenu = new ContextMenuStrip();
            // La création du premier contrôle installe le contexte WinForms : on le garde pour le thread d'entrées
            _uiContext = SynchronizationContext.Current;
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

            _multiControllerMenuItem = new ToolStripMenuItem("🎮🎮 Toutes les manettes actives (PS + Xbox)")
            {
                Checked = _multiControllerEnabled
            };
            _multiControllerMenuItem.Click += (_, _) =>
            {
                SetMultiControllerEnabled(!_multiControllerEnabled);
                _multiControllerMenuItem.Checked = _multiControllerEnabled;
                SyncStateToWebView();
            };
            contextMenu.Items.Add(_multiControllerMenuItem);

            if (!ElevationHelper.IsCurrentProcessElevated)
            {
                contextMenu.Items.Add("🛡️ Relancer en administrateur (jeux admin)", null, (_, _) =>
                {
                    Log.Info("Relance en administrateur demandée");
                    if (ElevationHelper.RestartElevated())
                        ExitThread();
                });
            }

            _controllersRootMenuItem = new ToolStripMenuItem("🕹️ Manette Active : Auto");
            contextMenu.Items.Add(_controllersRootMenuItem);
            RebuildTrayControllersMenu();

            _profilesRootMenuItem = new ToolStripMenuItem($"🎮 Profil : {ActiveProfileName}");
            contextMenu.Items.Add(_profilesRootMenuItem);
            RebuildTrayProfilesMenu();

            _driverToolsMenuItem = new ToolStripMenuItem("🛠️ Outils Pilotes & Reset Manettes");
            _driverToolsMenuItem.DropDownItems.Add("🟢 Relâcher Gâchettes L2/R2 (Remettre par défaut)", null, (_, _) => ResetTriggersToDefault());
            _driverToolsMenuItem.DropDownItems.Add("🔄 Reset Flux HID & Réveiller PS5 Bluetooth", null, (_, _) => ResetHidAndWakeSony());
            _driverToolsMenuItem.DropDownItems.Add("🎮 Réparer Extinction / Déconnexion Manette Xbox (Bluetooth & Éco. Énergie)", null, (_, _) => RepairXboxWirelessDisconnect());
            _driverToolsMenuItem.DropDownItems.Add("🧹 Purger Conflits (Fermer DS4Windows / InputMapper)", null, (_, _) => KillConflictingApps(false));
            _driverToolsMenuItem.DropDownItems.Add("🎛️ Ouvrir Contrôleurs de jeu Windows (joy.cpl)", null, (_, _) => OpenWindowsTool("joy.cpl"));
            _driverToolsMenuItem.DropDownItems.Add("⚙️ Ouvrir Gestionnaire de périphériques", null, (_, _) => OpenWindowsTool("devmgmt.msc"));
            _driverToolsMenuItem.DropDownItems.Add("📶 Paramètres Bluetooth Windows", null, (_, _) => OpenWindowsTool("ms-settings:bluetooth"));
            _driverToolsMenuItem.DropDownItems.Add("📄 Ouvrir le dossier des journaux", null, (_, _) =>
            {
                Directory.CreateDirectory(Log.LogDirectory);
                OpenWindowsTool(Log.LogDirectory);
            });
            contextMenu.Items.Add(_driverToolsMenuItem);

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("❌ Quitter complètement", null, (_, _) => ExitThread());

            _trayIcon = new NotifyIcon
            {
                Icon = TrayIconRenderer.Create(Color.FromArgb(0, 102, 255), paused: false),
                ContextMenuStrip = contextMenu,
                Visible = true
            };
            SetTrayText($"Gamepad Manager - {ActiveProfileName}");
            _trayIcon.DoubleClick += (_, _) => ShowDashboard();
            UpdateTrayIcon();

            _backgroundTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _backgroundTimer.Tick += BackgroundMonitoringTick;
            _backgroundTimer.Start();

            // Le timer de 2 s reste en filet de sécurité si le hook ne peut pas être installé
            _foregroundHookProc = (_, _, _, _, _, _, _) => CheckForegroundGameAndSwitchProfile();
            _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                _foregroundHookProc, 0, 0, WINEVENT_OUTOFCONTEXT);
            if (_foregroundHook == IntPtr.Zero)
                Log.Warn("Hook de fenêtre active indisponible, détection du jeu toutes les 2 s");

            _actionRunner.LaunchHandler = HandleLaunchRequest;
            _actionRunner.ProfileCycleHandler = delta => RunOnUiThread(() => CycleProfile(delta));
            for (int i = 0; i < _controllerActions.Length; i++)
            {
                int source = i;
                _controllerActions[i].LaunchHandler = (target, rawAction) => HandleLaunchRequest(target, rawAction, source);
                _controllerActions[i].ProfileCycleHandler = delta =>
                    RunOnUiThread(() => { if (CanUseController(source)) CycleProfile(delta, source); });
            }
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
            catch (Exception ex) { Log.Warn("Modification du démarrage automatique impossible", ex); }
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
            var (battery, device) = GetDisplayedBattery();
            _hudForm.UpdateHud(
                ActiveProfileName,
                battery == null ? "--%" : $"{battery.Percent}%{(battery.Charging ? " ⚡" : "")}",
                device,
                Color.FromArgb(c.r, c.g, c.b),
                lowBatteryAlert: battery is { Level: > BatteryAlert.None }
            );
        }

        private void UpdateTrayTooltipText()
        {
            var (battery, device) = GetDisplayedBattery();
            string batSuffix = battery is { Level: > BatteryAlert.None } ? $" [🪫 {device} {battery.Percent}%]" : "";
            SetTrayText($"Gamepad Manager{batSuffix} - {ActiveProfileName}");
        }

        // NotifyIcon.Text est limité à 63 caractères par l'API WinForms (lève ArgumentOutOfRangeException au-delà)
        private void SetTrayText(string text)
        {
            if (_trayIcon == null) return;
            _trayIcon.Text = text.Length > 63 ? text.Substring(0, 62) + "…" : text;
        }

        private void UpdateMappingToggleMenu()
        {
            _mappingToggleMenuItem.Checked = _mappingEnabled;
            _mappingToggleMenuItem.Text = _mappingEnabled
                ? "⌨️ Raccourcis & Sticks : Actifs"
                : "🔇 Raccourcis & Sticks : En Pause (Mode Test)";
            if (!_mappingEnabled)
            {
                ReleaseAllControllerOutputs();
            }
            UpdateTrayIcon();
        }

        private void UpdateTrayIcon()
        {
            if (_trayIcon == null) return; // appelé pendant le constructeur, avant la création de l'icône
            var c = GetActiveProfile()?.color ?? new LightbarColor();
            Color iconColor = GetDisplayedBattery().Reading is { Level: > BatteryAlert.None }
                ? Color.FromArgb(239, 68, 68)
                : Color.FromArgb(c.r, c.g, c.b);
            var old = _trayIcon.Icon;
            _trayIcon.Icon = TrayIconRenderer.Create(iconColor, paused: !_mappingEnabled);
            old?.Dispose();
        }

        private void KillConflictingApps(bool killSteam)
        {
            int killed = 0;
            string[] targets = killSteam
                ? ConflictingProcessNames.Concat(new[] { "steam", "HidHideClient" }).ToArray()
                : ConflictingProcessNames;

            var running = targets.Where(n => Process.GetProcessesByName(n).Length > 0).ToList();
            if (running.Count > 0)
            {
                // Fermer un programme peut faire perdre des données (ex. jeu Steam en cours) : on demande d'abord
                var answer = MessageBox.Show(
                    $"Fermer ces programmes ?\n\n• {string.Join("\n• ", running)}",
                    "Purger les conflits de manette", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }

            foreach (var name in running)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        try { p.Kill(); killed++; Log.Info($"Processus fermé : {name}"); }
                        catch (Exception ex) { Log.Warn($"Impossible de fermer {name}", ex); }
                    }
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
            catch (Exception ex) { Log.Warn("Ouverture de l'outil Windows impossible", ex); }
        }

        private void RebuildTrayControllersMenu()
        {
            _controllersRootMenuItem.DropDownItems.Clear();

            var effectiveList = _connectedControllers.Count > 0
                ? _connectedControllers
                : new List<ControllerSlotInfo>
                {
                    new() { index = 99, type = "PS5", label = "Sony PS5 / PS4 (Pont HID C#)" },
                    new() { index = 0, type = "XBOX", label = "Manette Xbox Series / One (XInput C#)" }
                };

            foreach (var ctrl in effectiveList)
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
                    SetControllerSelection(targetIdx, targetType);
                    UpdateBatteryIndicators();
                    RebuildTrayControllersMenu();
                    RebuildTrayProfilesMenu();
                    RefreshHudOverlayContent();
                    UpdateTrayTooltipText();
                    SendToWebView(new { type = "selectControllerFromTray", index = targetIdx });
                    SyncStateToWebView();
                };
                _controllersRootMenuItem.DropDownItems.Add(item);
            }

            var current = effectiveList.FirstOrDefault(c => c.index == _selectedWebGpIndex) ?? effectiveList[0];
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
                string scInfo = "";
                if (!string.IsNullOrWhiteSpace(prof.shortcutKey))
                    scInfo += $" [⌨️ {prof.shortcutKey}]";
                if (!string.IsNullOrWhiteSpace(prof.shortcutButtons))
                    scInfo += $" [🎮 {FormatGamepadShortcutLabels(prof.shortcutButtons)}]";

                var activeIdForSelected = GetActiveProfile()?.id ?? _currentProfileId;
                var item = new ToolStripMenuItem($"{prof.name} ({prof.exe}){scInfo}")
                {
                    Checked = prof.id == activeIdForSelected
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
            UpdateTrayIcon(); // la couleur suit le profil actif
        }

        private static readonly string[] ShortButtonNames =
        {
            "A/Croix", "B/Rond", "X/Carré", "Y/Triangle",
            "LB/L1", "RB/R1", "LT/L2", "RT/R2",
            "View/Share", "Menu/Options", "L3", "R3",
            "D-Haut", "D-Bas", "D-Gauche", "D-Droit",
            "PS", "Pavé G", "Pavé D", "Mute"
        };

        private static string FormatGamepadShortcutLabels(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var names = new List<string>();
            foreach (var part in raw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out int idx) && idx >= 0 && idx < ShortButtonNames.Length)
                    names.Add(ShortButtonNames[idx]);
            }
            return string.Join(" + ", names);
        }

        private async void ShowDashboard()
        {
            if (_dashboardForm != null && !_dashboardForm.IsDisposed)
            {
                if (_dashboardForm.WindowState == FormWindowState.Minimized)
                    _dashboardForm.WindowState = FormWindowState.Normal;
                _dashboardForm.Activate();
                return;
            }
            if (_isOpeningDashboard) return;
            _isOpeningDashboard = true;

            try
            {
                _dashboardForm = new Form
                {
                    Text = "Gamepad OS — Console Diagnostic, Périphériques & Profils",
                    Size = new Size(1300, 900),
                    StartPosition = FormStartPosition.CenterScreen,
                    BackColor = Color.FromArgb(7, 7, 9),
                    Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) // icône de l'exe (Assets/app.ico)
                };

                var webView = new WebView2 { Dock = DockStyle.Fill };
                _webView = webView;
                _dashboardForm.Controls.Add(webView);

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

                // Désactive impérativement WindowsGamingInput côté Chromium pour éviter que WebView2 ne fasse tomber le Bluetooth LE Xbox
                var envOptions = new CoreWebView2EnvironmentOptions("--disable-features=HardwareMediaKeyHandling,GamepadButtonAxisEvents,WindowsGamingInput");
                Directory.CreateDirectory(WebViewDataDir);
                var env = await CoreWebView2Environment.CreateAsync(null, WebViewDataDir, envOptions);

                if (_dashboardForm == null || _dashboardForm.IsDisposed || webView.IsDisposed)
                    return;

                await webView.EnsureCoreWebView2Async(env);

                if (_dashboardForm == null || _dashboardForm.IsDisposed || webView.IsDisposed || webView.CoreWebView2 == null)
                    return;

                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                webView.CoreWebView2.NavigationCompleted += (_, _) =>
                {
                    SyncStateToWebView();
                    SendHardwareInventoryToWeb();
                };

                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UI", "index.html");
                if (File.Exists(htmlPath))
                {
                    webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                }
                else
                {
                    Log.Error($"Fichier d'interface introuvable : {htmlPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Ouverture du Dashboard WebView2 impossible", ex);
                MessageBox.Show(
                    "Impossible d'initialiser la fenêtre de configuration (Microsoft Edge WebView2).\n\n" +
                    "La manette, les profils et les raccourcis restent actifs dans la barre des tâches.\n" +
                    $"Détail : {ex.Message}",
                    "ControllerManager — WebView2",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                _isOpeningDashboard = false;
            }
        }

        private void BackgroundMonitoringTick(object? sender, EventArgs e)
        {
            CheckBatteryAndNotify();
            CheckForegroundGameAndSwitchProfile();
            CheckConflictingApps();
        }

        // Émulateurs qui capturent ou dupliquent les entrées de la manette
        private static readonly string[] ConflictingProcessNames = { "DS4Windows", "InputMapper", "DSX", "DualSenseX" };
        private readonly HashSet<string> _knownConflicts = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastConflictCheck = DateTime.MinValue;

        private void CheckConflictingApps()
        {
            if ((DateTime.Now - _lastConflictCheck).TotalSeconds < 10)
                return;
            _lastConflictCheck = DateTime.Now;

            foreach (var name in ConflictingProcessNames)
            {
                var procs = Process.GetProcessesByName(name);
                bool running = procs.Length > 0;
                foreach (var p in procs) p.Dispose();

                if (running && _knownConflicts.Add(name))
                {
                    Log.Warn($"Logiciel en conflit détecté : {name}");
                    _trayIcon.ShowBalloonTip(5000, "⚠️ Conflit de manette détecté",
                        $"{name} est lancé : il peut doubler ou bloquer les entrées. " +
                        "Menu Outils Pilotes → « Purger Conflits » pour le fermer.", ToolTipIcon.Warning);
                }
                else if (!running)
                {
                    _knownConflicts.Remove(name);
                }
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

                bool isSelf = pid == (uint)Environment.ProcessId;
                _isDashboardFocused = isSelf;
                if (isSelf)
                {
                    ReleaseAllControllerOutputs();
                    return;
                }

                if (!_autoSwitchEnabled)
                {
                    SendToWebView(new
                    {
                        type = "activeProcess",
                        exe = exeName,
                        profileId = GetActiveProfile()?.id ?? _currentProfileId,
                        profileName = ActiveProfileName,
                        autoSwitch = _autoSwitchEnabled,
                        mappingEnabled = _mappingEnabled
                    });
                    WarnIfForegroundElevated(pid, exeName);
                    return;
                }

                var matched = FindLaunchOverride(exeName) ?? LaunchProfileResolver.FindByExe(exeName, _profiles);

                if (matched != null && matched.id != GetActiveProfile()?.id)
                {
                    ApplyProfile(matched.id, notifyUser: true);
                }
                else
                {
                    SendToWebView(new
                    {
                        type = "activeProcess",
                        exe = exeName,
                        profileId = GetActiveProfile()?.id ?? _currentProfileId,
                        profileName = ActiveProfileName,
                        autoSwitch = _autoSwitchEnabled,
                        mappingEnabled = _mappingEnabled
                    });
                }

                // Après une éventuelle bascule : c'est le profil réellement appliqué au jeu qui compte
                WarnIfForegroundElevated(pid, exeName);
            }
            catch (Exception ex) { Log.Warn("Détection du jeu au premier plan impossible", ex); }
        }

        private readonly HashSet<string> _elevationWarnedExes = new(StringComparer.OrdinalIgnoreCase);

        // Un jeu lancé en administrateur ignore souris/clavier simulés : on prévient une fois par exe et par session
        private void WarnIfForegroundElevated(uint pid, string exeName)
        {
            if (ElevationHelper.IsCurrentProcessElevated || !_mappingEnabled || _elevationWarnedExes.Contains(exeName))
                return;

            var prof = GetActiveProfile();
            bool profileInjectsInput = prof != null &&
                ((prof.buttonMapping?.Count ?? 0) > 0 ||
                 prof.leftStickMode != "native" || prof.rightStickMode != "native" ||
                 prof.touchpadMouseEnabled);
            if (!profileInjectsInput) return;

            if (ElevationHelper.IsElevated(pid) != true) return;

            _elevationWarnedExes.Add(exeName);
            Log.Warn($"{exeName} tourne en administrateur : raccourcis et souris bloqués par Windows");
            _trayIcon.ShowBalloonTip(6000, "🛡️ Programme lancé en administrateur",
                $"{exeName} est lancé en administrateur : Windows bloque les raccourcis et la souris de la manette. " +
                "Menu → « Relancer en administrateur » pour les réactiver.", ToolTipIcon.Warning);
        }

        protected override void ExitThreadCore()
        {
            _isShuttingDown = true;
            _backgroundTimer.Stop();
            ReleaseAllControllerOutputs();
            if (_foregroundHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
            _muteLedActive = false;
            try { FlushSonyOutputImmediate(0, 0, 0, "off", 0, "off", 0, 0, 0); } catch { }
            lock (_sonyLock)
            {
                try { _activeSonyStream?.Dispose(); } catch { }
            }
            _batteryAlertTimer?.Dispose();
            _batteryAlertForm?.Dispose();
            _hudForm?.Close();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _webView?.Dispose();
            base.ExitThreadCore();
        }
    }
}
