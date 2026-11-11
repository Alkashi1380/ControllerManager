using System.Text.Json;
using HidSharp;
using Microsoft.Web.WebView2.Core;

namespace ControllerManager
{
    public partial class TrayApplicationContext
    {
        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string? json = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string action = OptString(root, "action") ?? "";

                if (action == "requestInitState")
                {
                    SyncStateToWebView();
                    SendHardwareInventoryToWeb();
                }
                else if (action == "setMappingEnabled")
                {
                    _mappingEnabled = OptBool(root, "enabled", _mappingEnabled);
                    UpdateMappingToggleMenu();
                    SyncStateToWebView();
                }
                else if (action == "setHudOverlay")
                {
                    bool en = OptBool(root, "enabled", _hudEnabled);
                    ToggleHudOverlay(en);
                }
                else if (action == "setWindowsStartup")
                {
                    bool en = OptBool(root, "enabled", IsStartupEnabled());
                    SetStartupEnabled(en);
                    _startupToggleMenuItem.Checked = IsStartupEnabled();
                    SyncStateToWebView();
                }
                else if (action == "setMultiController")
                {
                    bool en = OptBool(root, "enabled", _multiControllerEnabled);
                    SetMultiControllerEnabled(en);
                    if (_multiControllerMenuItem != null) _multiControllerMenuItem.Checked = _multiControllerEnabled;
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
                else if (action == "repairXboxDisconnect")
                {
                    RepairXboxWirelessDisconnect();
                }
                else if (action == "resetTriggersDefault")
                {
                    ResetTriggersToDefault();
                }
                else if (action == "killConflicts")
                {
                    bool includeSteam = OptBool(root, "includeSteam", false);
                    KillConflictingApps(includeSteam);
                }
                else if (action == "testMappingAction")
                {
                    // Bouton « Tester » de l'éditeur : délai pour laisser l'utilisateur cliquer dans l'appli cible
                    var testAction = MappingAction.Parse(OptString(root, "value"));
                    int delayMs = Math.Clamp(OptInt32(root, "delayMs", 3000), 0, 10_000);
                    if (testAction.Kind != MappingKind.None)
                    {
                        Log.Info($"Test d'action demandé : {testAction.Kind}");
                        Task.Run(async () =>
                        {
                            await Task.Delay(delayMs);
                            _actionRunner.Press("test", testAction);
                            // Maintien / turbo : on simule un appui d'une seconde
                            if (testAction.NeedsRelease) await Task.Delay(1000);
                            _actionRunner.Release("test");
                        });
                    }
                }
                else if (action == "browseExecutable")
                {
                    using var dialog = new OpenFileDialog
                    {
                        Title = "Choisir l'application à lancer",
                        Filter = "Applications et raccourcis (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Tous les fichiers (*.*)|*.*",
                        CheckFileExists = true
                    };
                    if (dialog.ShowDialog(_dashboardForm) == DialogResult.OK)
                        SendToWebView(new { type = "filePicked", requestId = OptString(root, "requestId") ?? "", path = dialog.FileName });
                }
                else if (action == "openWinTool")
                {
                    // Liste blanche : la page ne doit jamais pouvoir lancer un programme arbitraire
                    string tool = OptString(root, "tool") ?? "";
                    if (AllowedWindowsTools.Contains(tool))
                        OpenWindowsTool(tool);
                }
                else if (action == "selectController")
                {
                    int index = OptInt32(root, "index", 99);
                    SetControllerSelection(index, OptString(root, "controllerType") ?? (index == 99 ? "PS5" : "XBOX"));
                    UpdateBatteryIndicators();
                    RebuildTrayControllersMenu();
                    RebuildTrayProfilesMenu();
                    RefreshHudOverlayContent();
                    UpdateTrayTooltipText();
                    SyncStateToWebView();
                }
                else if (action == "updateControllerList")
                {
                    if (root.TryGetProperty("controllers", out var ctrlProp) && ctrlProp.ValueKind == JsonValueKind.Array)
                    {
                        string rawList = ctrlProp.GetRawText();
                        _connectedControllers = JsonSerializer.Deserialize<List<ControllerSlotInfo>>(rawList) ?? new();
                    }
                    if (root.TryGetProperty("activeIndex", out var actIdx) && actIdx.ValueKind == JsonValueKind.Number)
                    {
                        int index = actIdx.GetInt32();
                        var cur = _connectedControllers.FirstOrDefault(c => c.index == index);
                        SetControllerSelection(index, cur?.type ?? _selectedControllerType);
                    }
                    RebuildTrayControllersMenu();
                }
                else if (action == "selectProfile")
                {
                    var target = FindProfile(OptString(root, "profileId"), OptString(root, "profileName"));
                    if (root.TryGetProperty("autoSwitch", out var autoProp) && (autoProp.ValueKind == JsonValueKind.True || autoProp.ValueKind == JsonValueKind.False))
                    {
                        _autoSwitchEnabled = autoProp.GetBoolean();
                    }
                    if (target != null)
                        ApplyProfile(target.id, notifyUser: false);
                }
                else if (action == "setAutoSwitch")
                {
                    _autoSwitchEnabled = OptBool(root, "enabled", _autoSwitchEnabled);
                    RebuildTrayProfilesMenu();
                    SyncStateToWebView();
                }
                else if (action == "cycleProfile")
                {
                    int delta = OptInt32(root, "delta", 1);
                    CycleProfile(delta >= 0 ? 1 : -1);
                }
                else if (action == "syncProfiles")
                {
                    // Garde d'intégrité : on valide d'abord la liste en mémoire avant d'écrire sur le disque
                    if (!root.TryGetProperty("profiles", out var profProp) || profProp.ValueKind != JsonValueKind.Array)
                        return;

                    string rawProfiles = profProp.GetRawText();
                    var validated = JsonSerializer.Deserialize<List<GamepadProfile>>(rawProfiles);
                    if (validated == null || validated.Count == 0)
                        return;

                    foreach (var p in validated)
                        ProfileMigration.Migrate(p);
                    EnsureProfileIds(validated);

                    lock (_inputStateLock)
                    {
                        // Les raccourcis et sticks peuvent changer dans chacun des profils édités.
                        ReleaseAllControllerOutputs();
                        _profiles = validated;

                        var requested = FindProfile(OptString(root, "activeProfileId"), OptString(root, "activeProfileName"));
                        string resolvedId = requested?.id ?? GetActiveProfile()?.id ?? validated[0].id;
                        if (SelectedControllerSource == SourceXbox || !validated.Any(p => p.id == _currentXboxProfileId))
                            _currentXboxProfileId = resolvedId;
                        if (SelectedControllerSource == SourceSony || !validated.Any(p => p.id == _currentProfileId))
                            _currentProfileId = resolvedId;
                    }
                    SaveProfilesToDisk();

                    ApplyActiveProfileHardwareEffects();
                    RebuildTrayProfilesMenu();
                    RefreshHudOverlayContent();
                    UpdateTrayTooltipText();
                    SyncStateToWebView();
                }
                else if (action == "applyDualSenseTriggers")
                {
                    string l2M = OptString(root, "l2Mode") ?? "off";
                    int l2F = Math.Clamp(OptInt32(root, "l2Force", 180), 0, 255);
                    string r2M = OptString(root, "r2Mode") ?? "off";
                    int r2F = Math.Clamp(OptInt32(root, "r2Force", 180), 0, 255);
                    var prof = GetActiveProfile();
                    if (prof != null)
                    {
                        prof.l2TriggerMode = l2M;
                        prof.l2TriggerForce = l2F;
                        prof.r2TriggerMode = r2M;
                        prof.r2TriggerForce = r2F;
                        SaveProfilesToDisk();
                    }
                    var (r, g, b) = OptColor(root, prof?.color);
                    SendDualSenseOutputReport(r, g, b, l2M, l2F, r2M, r2F);
                }
                else if (action == "testRumble")
                {
                    double left = Math.Clamp(OptDouble(root, "left", 0), 0, 1);
                    double right = Math.Clamp(OptDouble(root, "right", 0), 0, 1);

                    XInput.SetVibration(XInput.FindSlot(_selectedWebGpIndex), left, right);

                    var prof = GetActiveProfile();
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
                    var prof = GetActiveProfile();
                    var (r, g, b) = OptColor(root, prof?.color);
                    SendDualSenseOutputReport(
                        r, g, b,
                        prof?.l2TriggerMode ?? "off", prof?.l2TriggerForce ?? 180,
                        prof?.r2TriggerMode ?? "off", prof?.r2TriggerForce ?? 180
                    );
                }
                else if (action == "setCapturingShortcut")
                {
                    _isCapturingShortcutInUi = OptBool(root, "capturing", false);
                }
                else if (action == "webGamepadInput")
                {
                    if (_lastXInputSlot < 0 && root.TryGetProperty("buttons", out var bProp) && bProp.ValueKind == JsonValueKind.Array)
                    {
                        var arr = new bool[20];
                        int idx = 0;
                        foreach (var el in bProp.EnumerateArray())
                        {
                            if (idx >= 20) break;
                            arr[idx++] = el.ValueKind == JsonValueKind.True;
                        }
                        float lx = (float)OptDouble(root, "lx", 0);
                        float ly = (float)OptDouble(root, "ly", 0);
                        float rx = (float)OptDouble(root, "rx", 0);
                        float ry = (float)OptDouble(root, "ry", 0);
                        PollWebGamepadState(arr, lx, ly, rx, ry);
                    }
                }
            }
            catch (Exception ex) { Log.Error("Message de l'interface non traité", ex); }
        }

        private static string? OptString(JsonElement root, string property) =>
            root.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

        private static bool OptBool(JsonElement root, string property, bool fallback) =>
            root.TryGetProperty(property, out var prop) && (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
                ? prop.GetBoolean()
                : fallback;

        private static int OptInt32(JsonElement root, string property, int fallback) =>
            root.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int val)
                ? val
                : fallback;

        private static double OptDouble(JsonElement root, string property, double fallback) =>
            root.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out double val)
                ? val
                : fallback;

        private static (byte r, byte g, byte b) OptColor(JsonElement root, LightbarColor? fallback)
        {
            var fb = fallback ?? new LightbarColor { r = 0, g = 102, b = 255 };
            if (!root.TryGetProperty("color", out var c) || c.ValueKind != JsonValueKind.Object)
                return (fb.r, fb.g, fb.b);
            byte r = (byte)Math.Clamp(OptInt32(c, "r", fb.r), 0, 255);
            byte g = (byte)Math.Clamp(OptInt32(c, "g", fb.g), 0, 255);
            byte b = (byte)Math.Clamp(OptInt32(c, "b", fb.b), 0, 255);
            return (r, g, b);
        }

        private void SyncStateToWebView()
        {
            var activeProf = GetActiveProfile();
            SendToWebView(new
            {
                type = "initState",
                profiles = _profiles,
                activeProfileId = activeProf?.id ?? _currentProfileId,
                activeProfileName = ActiveProfileName,
                autoSwitch = _autoSwitchEnabled,
                mappingEnabled = _mappingEnabled,
                hudEnabled = _hudEnabled,
                startupEnabled = IsStartupEnabled(),
                multiController = _multiControllerEnabled,
                selectedControllerIndex = _selectedWebGpIndex,
                selectedControllerType = _selectedControllerType,
                exe = _lastDetectedExe
            });
            SendBatteryToWebView();
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

        private void SendHardwareInventoryToWeb()
        {
            try
            {
                double sinceHidEvent = (DateTime.Now - _lastHidChangeEvent).TotalMilliseconds;
                double sinceXboxConn = _xboxConnectedSince == DateTime.MinValue
                    ? 99999
                    : (DateTime.Now - _xboxConnectedSince).TotalMilliseconds;

                // Ne jamais scanner HidSharp pendant qu'une manette Xbox Bluetooth LE est en cours d'allumage/appairage
                bool skipHidScan = _activeSonyDevice == null && (sinceHidEvent < 4500 || sinceXboxConn < 6000);

                var sonyDevs = skipHidScan
                    ? new List<HidDevice>()
                    : DeviceList.Local.GetHidDevices(0x054C)
                        .Where(d => d.ProductID == 0x0CE6 || d.ProductID == 0x0DF2 || d.ProductID == 0x05C4 || d.ProductID == 0x09CC)
                        .ToList();

                var sonyHidList = sonyDevs
                    .Select(d => new
                    {
                        name = d.ProductID switch
                        {
                            0x0CE6 => "Sony DualSense (PS5)",
                            0x0DF2 => "Sony DualSense Edge (PS5)",
                            _ => "Sony DualShock 4 (PS4)"
                        },
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
            catch (Exception ex) { Log.Warn("Inventaire matériel impossible", ex); }
        }
    }
}
