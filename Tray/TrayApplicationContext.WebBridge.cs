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
                    var target = FindProfile(OptString(root, "profileId"), OptString(root, "profileName"));
                    if (root.TryGetProperty("autoSwitch", out var autoProp))
                    {
                        _autoSwitchEnabled = autoProp.GetBoolean();
                    }
                    if (target != null)
                        ApplyProfile(target.id, notifyUser: false);
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
                    // Réécrit le fichier pour y figer les id générés pendant le chargement
                    SaveProfilesToDisk();

                    var requested = FindProfile(OptString(root, "activeProfileId"), OptString(root, "activeProfileName"));
                    _currentProfileId = requested?.id ?? GetActiveProfile()?.id ?? _profiles.FirstOrDefault()?.id ?? "";

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
                    var c = root.GetProperty("color");
                    var prof = GetActiveProfile();
                    SendDualSenseOutputReport(
                        (byte)c.GetProperty("r").GetInt32(),
                        (byte)c.GetProperty("g").GetInt32(),
                        (byte)c.GetProperty("b").GetInt32(),
                        prof?.l2TriggerMode ?? "off", prof?.l2TriggerForce ?? 180,
                        prof?.r2TriggerMode ?? "off", prof?.r2TriggerForce ?? 180
                    );
                }
            }
            catch (Exception ex) { Log.Error("Message de l'interface non traité", ex); }
        }

        private static string? OptString(JsonElement root, string property) =>
            root.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

        private void SyncStateToWebView()
        {
            SendToWebView(new
            {
                type = "initState",
                profiles = _profiles,
                activeProfileId = _currentProfileId,
                activeProfileName = ActiveProfileName,
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
            catch (Exception ex) { Log.Warn("Inventaire matériel impossible", ex); }
        }
    }
}
