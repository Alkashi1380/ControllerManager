namespace ControllerManager
{
    public partial class TrayApplicationContext
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private DateTime _lastKeyboardShortcutCheck = DateTime.MinValue;
        private DateTime _lastXboxWebUiPush = DateTime.MinValue;
        private DateTime _lastWebGamepadStickStep = DateTime.Now;
        private volatile int _lastXInputSlot = -1;
        private volatile bool _isCapturingShortcutInUi = false;
        private readonly HashSet<string> _activeProfileKeyShortcuts = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string>[] _activeProfilePadShortcuts =
        {
            new(StringComparer.OrdinalIgnoreCase),
            new(StringComparer.OrdinalIgnoreCase)
        };
        private readonly object _inputStateLock = new();
        private readonly ActionRunner[] _controllerActions = { new(), new() };
        private readonly bool[] _xboxCurButtons = new bool[20];
        private readonly bool[] _prevXboxButtons = new bool[20];
        private readonly DateTime[,] _pendingSinglePressTime = new DateTime[2, 20];

        private void InputPollingLoop()
        {
            byte[] sonyBuffer = new byte[128];
            byte[] drainBuffer = new byte[128];
            DateTime lastSonyStickStep = DateTime.Now;
            DateTime lastXboxStickStep = DateTime.Now;
            DateTime lastXboxPoll = DateTime.MinValue;
            int xSlot = -1;
            DateTime lastXInputScan = DateTime.MinValue;

            while (!_isShuttingDown)
            {
                // Surveillance globale des raccourcis clavier par profil (toutes les 25 ms)
                if ((DateTime.Now - _lastKeyboardShortcutCheck).TotalMilliseconds >= 25)
                {
                    _lastKeyboardShortcutCheck = DateTime.Now;
                    CheckGlobalProfileKeyboardShortcuts();
                }

                // Interroger un slot XInput déconnecté est coûteux : on ne re-scanne qu'une fois toutes les 2 secondes
                int preferredSlot = _selectedWebGpIndex;
                bool checkPreferredSlot = preferredSlot is >= 0 and < 4 && preferredSlot != xSlot;
                if ((xSlot < 0 || checkPreferredSlot) && (DateTime.Now - lastXInputScan).TotalMilliseconds > 2000)
                {
                    lastXInputScan = DateTime.Now;
                    int newSlot = xSlot < 0 ? XInput.FindSlot(preferredSlot)
                        : XInput.IsConnected(preferredSlot) ? preferredSlot : xSlot;
                    bool slotChanged = newSlot != xSlot;
                    if (slotChanged)
                    {
                        lock (_inputStateLock)
                        {
                            ResetControllerInput(SourceXbox);
                            xSlot = newSlot;
                            _xboxConnectedSince = DateTime.Now;
                            _lastXInputSlot = newSlot;
                        }
                    }

                    if (slotChanged)
                    {
                        Log.Info($"Manette Xbox (XInput slot #{xSlot}) détectée — stabilisation Bluetooth LE (2,5 s)");

                        if (_activeSonyStream == null && (_selectedWebGpIndex == 99 || _selectedControllerType != "XBOX"))
                        {
                            SetControllerSelection(newSlot, "XBOX");
                            RunOnUiThread(() =>
                            {
                                RebuildTrayControllersMenu();
                                RebuildTrayProfilesMenu();
                                RefreshHudOverlayContent();
                                SendToWebView(new { type = "selectControllerFromTray", index = newSlot });
                                SyncStateToWebView();
                            });
                        }
                    }
                }

                bool sonyPolled = PollSonyPersistentStream(sonyBuffer, drainBuffer, ref lastSonyStickStep);

                if (xSlot < 0)
                {
                    System.Threading.Thread.Sleep(sonyPolled ? 2 : 10);
                    continue;
                }

                // Garde anti-extinction Xbox Bluetooth LE :
                // 1. Pendant les 2,5 premières secondes suivant l'allumage de la manette Xbox, le pilote Bluetooth LE / GIP
                //    négocie ses paramètres GATT. L'interroger à haute fréquence pendant cette fenêtre coupe la liaison.
                // 2. Même après stabilisation, on cadence XInputGetState à 60 Hz maximum (toutes les 16 ms) pour ne jamais
                //    inonder le pilote Xbox quand la PS5 tourne à 250 Hz dans la même boucle.
                double xboxConnMs = (DateTime.Now - _xboxConnectedSince).TotalMilliseconds;
                double minXboxIntervalMs = xboxConnMs < 2500 ? 250.0 : 16.0;
                if ((DateTime.Now - lastXboxPoll).TotalMilliseconds < minXboxIntervalMs)
                {
                    if (!sonyPolled)
                        System.Threading.Thread.Sleep(8);
                    continue;
                }
                lastXboxPoll = DateTime.Now;

                if (XInput.GetState((uint)xSlot, out var state) != XInput.ERROR_SUCCESS)
                {
                    lock (_inputStateLock)
                    {
                        ResetControllerInput(SourceXbox);
                        xSlot = -1;
                        _lastXInputSlot = -1;
                    }
                    _xboxConnectedSince = DateTime.MinValue;
                    lastXInputScan = DateTime.Now;
                    continue;
                }

                // On n'envoie rien à WebView2 pendant les 2,5 premières secondes de stabilisation Xbox
                if (xboxConnMs >= 2500)
                {
                    PollXboxState(xSlot, state.Gamepad, ref lastXboxStickStep);
                }
            }
        }

        private void PollXboxState(int xSlot, XInput.XINPUT_GAMEPAD pad, ref DateTime lastStickStep)
        {
            ushort buttons = pad.wButtons;

            _xboxCurButtons[0] = (buttons & XInput.A) != 0;
            _xboxCurButtons[1] = (buttons & XInput.B) != 0;
            _xboxCurButtons[2] = (buttons & XInput.X) != 0;
            _xboxCurButtons[3] = (buttons & XInput.Y) != 0;
            _xboxCurButtons[4] = (buttons & XInput.LEFT_SHOULDER) != 0;
            _xboxCurButtons[5] = (buttons & XInput.RIGHT_SHOULDER) != 0;
            _xboxCurButtons[6] = pad.bLeftTrigger > 64;
            _xboxCurButtons[7] = pad.bRightTrigger > 64;
            _xboxCurButtons[8] = (buttons & XInput.BACK) != 0;
            _xboxCurButtons[9] = (buttons & XInput.START) != 0;
            _xboxCurButtons[10] = (buttons & XInput.LEFT_THUMB) != 0;
            _xboxCurButtons[11] = (buttons & XInput.RIGHT_THUMB) != 0;
            _xboxCurButtons[12] = (buttons & XInput.DPAD_UP) != 0;
            _xboxCurButtons[13] = (buttons & XInput.DPAD_DOWN) != 0;
            _xboxCurButtons[14] = (buttons & XInput.DPAD_LEFT) != 0;
            _xboxCurButtons[15] = (buttons & XInput.DPAD_RIGHT) != 0;
            _xboxCurButtons[16] = (buttons & XInput.GUIDE) != 0;
            _xboxCurButtons[17] = false;
            _xboxCurButtons[18] = false;
            _xboxCurButtons[19] = false;

            float lx = Math.Clamp(pad.sThumbLX / 32767f, -1f, 1f);
            float ly = Math.Clamp(-pad.sThumbLY / 32767f, -1f, 1f);
            float rx = Math.Clamp(pad.sThumbRX / 32767f, -1f, 1f);
            float ry = Math.Clamp(-pad.sThumbRY / 32767f, -1f, 1f);
            float l2Val = pad.bLeftTrigger / 255f;
            float r2Val = pad.bRightTrigger / 255f;

            bool buttonChanged;
            lock (_inputStateLock)
            {
                bool enabled = CanUseController(SourceXbox);
                buttonChanged = ProcessControllerButtons(_xboxCurButtons, SourceXbox, enabled);
                if (enabled && (DateTime.Now - lastStickStep).TotalMilliseconds >= 8)
                {
                    lastStickStep = DateTime.Now;
                    ProcessAnalogSticksMapping(lx, ly, rx, ry, SourceXbox);
                }
            }

            if (_webView != null && (buttonChanged || (DateTime.Now - _lastXboxWebUiPush).TotalMilliseconds >= 16))
            {
                _lastXboxWebUiPush = DateTime.Now;
                SendToWebView(new
                {
                    type = "xboxHidState",
                    slot = xSlot,
                    lx,
                    ly,
                    rx,
                    ry,
                    l2 = l2Val,
                    r2 = r2Val,
                    buttons = _xboxCurButtons
                });
            }

            _prevButtons = buttons;
        }

        // Repli WebGamepad : si une manette Xbox Bluetooth LE / générique est lue par WebView2 alors que XInput ne la voit pas
        private void PollWebGamepadState(bool[] curButtons, float lx, float ly, float rx, float ry)
        {
            lock (_inputStateLock)
            {
                if (_lastXInputSlot >= 0) return;

                bool enabled = CanUseController(SourceXbox);
                ProcessControllerButtons(curButtons, SourceXbox, enabled);
                if (enabled && (DateTime.Now - _lastWebGamepadStickStep).TotalMilliseconds >= 8)
                {
                    _lastWebGamepadStickStep = DateTime.Now;
                    ProcessAnalogSticksMapping(lx, ly, rx, ry, SourceXbox);
                }
            }
        }

        private int SelectedControllerSource => _selectedControllerType == "XBOX" ? SourceXbox : SourceSony;

        private bool CanUseController(int source) =>
            !_isShuttingDown && (_multiControllerEnabled || source == SelectedControllerSource);

        private void SetControllerSelection(int index, string type)
        {
            lock (_inputStateLock)
            {
                int previousSource = SelectedControllerSource;
                _selectedWebGpIndex = index;
                _selectedControllerType = type;
                if (!_multiControllerEnabled && previousSource != SelectedControllerSource)
                    ReleaseControllerOutputs(previousSource);
            }
        }

        private void ReleaseControllerOutputs(int source)
        {
            _controllerActions[source].ReleaseAll();
            ReleaseStickOutputs(source);
            ClearPendingComboCandidatePresses(source);
            if (source == SourceSony)
                ResetSonyTouchpadInput();
        }

        private void ReleaseAllControllerOutputs()
        {
            lock (_inputStateLock)
            {
                ReleaseControllerOutputs(SourceSony);
                ReleaseControllerOutputs(SourceXbox);
                _actionRunner.ReleaseAll(); // Action lancée par le bouton « Tester ».
            }
        }

        private void ResetControllerInput(int source)
        {
            ReleaseControllerOutputs(source);
            Array.Clear(source == SourceSony ? _prevSonyButtons : _prevXboxButtons);
            _activeProfilePadShortcuts[source].Clear();
            _prevPauseCombo[source] = false;
            _prevProfilePrevCombo[source] = false;
            _prevProfileNextCombo[source] = false;
            if (source == SourceSony)
            {
                _prevPhysTouchClick = false;
                _touchClickLatchedRight = false;
            }
        }

        private void ResetSonyTouchpadInput()
        {
            _prevTouch1 = _prevTouch2 = false;
            _touchStartTime = DateTime.MinValue;
            _tpScrollCarry = 0;
        }

        private bool ProcessControllerButtons(bool[] cur, int source, bool enabled)
        {
            bool isSony = source == SourceSony;
            bool[] previous = isSony ? _prevSonyButtons : _prevXboxButtons;
            bool modifier = cur[16] || (!isSony && cur[8]);
            bool pause = modifier && cur[9];
            bool previousProfile = modifier && cur[4];
            bool nextProfile = modifier && cur[5];

            if (!enabled)
            {
                // Continue à lire l'état physique pour le dashboard et évite de rejouer un appui déjà tenu.
                bool changed = !cur.AsSpan().SequenceEqual(previous);
                cur.CopyTo(previous, 0);
                _prevPauseCombo[source] = pause;
                _prevProfilePrevCombo[source] = previousProfile;
                _prevProfileNextCombo[source] = nextProfile;
                CheckCustomProfileGamepadShortcuts(cur, source, trigger: false);
                return changed;
            }

            HandlePauseCombo(pause, source);
            HandleProfileSwitchCombo(previousProfile, nextProfile, source);
            bool custom = CheckCustomProfileGamepadShortcuts(cur, source);
            ProcessGamepadButtonsWithComboGuard(cur, previous, pause || previousProfile || nextProfile || custom,
                source, isSony, out bool buttonChanged);
            return buttonChanged;
        }

        private bool IsButtonInAnyCombo(int btnIdx, int source)
        {
            // Les deux côtés des combos système doivent attendre : PS/Guide/View + L1/LB, R1/RB ou Options/Menu.
            if (btnIdx is 4 or 5 or 9 or 16 || (source == SourceXbox && btnIdx == 8))
                return true;

            var snapshot = _profiles;
            if (snapshot == null) return false;

            foreach (var prof in snapshot)
            {
                if (string.IsNullOrWhiteSpace(prof.shortcutButtons)) continue;
                var parts = prof.shortcutButtons.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 2) continue;
                foreach (var p in parts)
                {
                    if (int.TryParse(p, out int idx))
                    {
                        if (idx == btnIdx) return true;
                        if (source == SourceXbox && idx == 16 && btnIdx == 8) return true;
                    }
                }
            }
            return false;
        }

        private void ClearPendingComboCandidatePresses(int source)
        {
            for (int i = 0; i < 20; i++)
                _pendingSinglePressTime[source, i] = DateTime.MinValue;
        }

        // Gère les 20 boutons avec une fenêtre anti-collision de 85 ms sur les boutons participant à une combinaison
        // (évite que PS+R1 ou LB+RB ne déclenche Win+D ou Alt+Tab sur le premier bouton pressé)
        private void ProcessGamepadButtonsWithComboGuard(
            bool[] cur, bool[] prev, bool anyComboActive, int source, bool isSony, out bool buttonChanged)
        {
            buttonChanged = false;
            var now = DateTime.Now;

            if (anyComboActive)
            {
                ClearPendingComboCandidatePresses(source);
            }

            for (int i = 0; i < 20; i++)
            {
                if (cur[i] != prev[i])
                    buttonChanged = true;

                if (cur[i] && !prev[i])
                {
                    if (isSony && i == 19 && !anyComboActive)
                    {
                        _muteLedActive = !_muteLedActive;
                        ApplyActiveProfileHardwareEffects();
                    }

                    if (!anyComboActive)
                    {
                        if (IsButtonInAnyCombo(i, source))
                        {
                            _pendingSinglePressTime[source, i] = now;
                        }
                        else
                        {
                            ExecuteMapping(i, source);
                        }
                    }
                }
                else if (cur[i] && prev[i])
                {
                    DateTime pendingAt = _pendingSinglePressTime[source, i];
                    if (pendingAt != DateTime.MinValue)
                    {
                        if (anyComboActive)
                        {
                            _pendingSinglePressTime[source, i] = DateTime.MinValue;
                        }
                        else if ((now - pendingAt).TotalMilliseconds >= 85)
                        {
                            _pendingSinglePressTime[source, i] = DateTime.MinValue;
                            ExecuteMapping(i, source);
                        }
                    }
                }
                else if (!cur[i] && prev[i])
                {
                    DateTime pendingAt = _pendingSinglePressTime[source, i];
                    if (pendingAt != DateTime.MinValue)
                    {
                        _pendingSinglePressTime[source, i] = DateTime.MinValue;
                        if (!anyComboActive)
                        {
                            ExecuteMapping(i, source);
                        }
                    }
                    ReleaseMapping(i, source);
                }

                prev[i] = cur[i];
            }
        }

        private static bool IsVkDown(byte vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        private static bool HasModifier(IReadOnlyList<byte> mods, byte target)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                if (mods[i] == target) return true;
            }
            return false;
        }

        // Raccourci 1 par profil : Touche / Combinaison clavier globale (ex. Ctrl+F1, Alt+1, F9)
        private void CheckGlobalProfileKeyboardShortcuts()
        {
            if (_isCapturingShortcutInUi)
            {
                _activeProfileKeyShortcuts.Clear();
                return;
            }

            var snapshot = _profiles;
            if (snapshot == null || snapshot.Count == 0) return;

            bool ctrlDown = IsVkDown(0x11);
            bool shiftDown = IsVkDown(0x10);
            bool altDown = IsVkDown(0x12);
            bool winDown = IsVkDown(0x5B) || IsVkDown(0x5C);

            int startIdx = snapshot.FindIndex(p => p.id == _currentProfileId);
            startIdx = startIdx >= 0 ? (startIdx + 1) % snapshot.Count : 0;

            var currentlyDownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? triggeredProfileId = null;

            for (int offset = 0; offset < snapshot.Count; offset++)
            {
                var prof = snapshot[(startIdx + offset) % snapshot.Count];
                if (string.IsNullOrWhiteSpace(prof.shortcutKey)) continue;
                string keySig = prof.shortcutKey.Trim();
                if (!KeyCombo.TryParse(keySig, out var combo) || combo == null) continue;

                bool reqCtrl = HasModifier(combo.Modifiers, 0x11);
                bool reqShift = HasModifier(combo.Modifiers, 0x10);
                bool reqAlt = HasModifier(combo.Modifiers, 0x12);
                bool reqWin = HasModifier(combo.Modifiers, 0x5B);

                bool modifiersMatch = (ctrlDown == reqCtrl) &&
                                      (shiftDown == reqShift) &&
                                      (altDown == reqAlt) &&
                                      (!reqWin || winDown);

                if (modifiersMatch && IsVkDown(combo.Key))
                {
                    currentlyDownKeys.Add(keySig);
                    if (triggeredProfileId == null && !_activeProfileKeyShortcuts.Contains(keySig))
                    {
                        triggeredProfileId = prof.id;
                    }
                }
            }

            _activeProfileKeyShortcuts.RemoveWhere(k => !currentlyDownKeys.Contains(k));
            foreach (var k in currentlyDownKeys)
                _activeProfileKeyShortcuts.Add(k);

            if (triggeredProfileId != null)
            {
                var now = DateTime.Now;
                if ((now - _lastProfileSwitch).TotalMilliseconds >= ProfileSwitchCooldownMs)
                {
                    _lastProfileSwitch = now;
                    string targetId = triggeredProfileId;
                    RunOnUiThread(() =>
                    {
                        _autoSwitchEnabled = false;
                        ApplyProfile(targetId, notifyUser: true);
                    });
                }
            }
        }

        // Raccourci 2 par profil : Combinaison de boutons manette (ex. "4+0" = LB/L1 + A/Croix)
        private bool CheckCustomProfileGamepadShortcuts(IReadOnlyList<bool> curButtons, int source, bool trigger = true)
        {
            if (_isCapturingShortcutInUi)
            {
                _activeProfilePadShortcuts[source].Clear();
                return false;
            }

            var snapshot = _profiles;
            if (snapshot == null || snapshot.Count == 0) return false;

            string activeId = source == SourceXbox ? _currentXboxProfileId : _currentProfileId;
            int startIdx = snapshot.FindIndex(p => p.id == activeId);
            startIdx = startIdx >= 0 ? (startIdx + 1) % snapshot.Count : 0;

            var currentlyDownCombos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? triggeredProfileId = null;

            for (int offset = 0; offset < snapshot.Count; offset++)
            {
                var prof = snapshot[(startIdx + offset) % snapshot.Count];
                if (string.IsNullOrWhiteSpace(prof.shortcutButtons)) continue;

                string comboSig = prof.shortcutButtons.Trim();
                var parts = comboSig.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0) continue;

                bool allPressed = true;
                int validCount = 0;
                foreach (var part in parts)
                {
                    if (int.TryParse(part, out int btnIdx) && btnIdx >= 0 && btnIdx < curButtons.Count)
                    {
                        validCount++;
                        bool isBtnDown = curButtons[btnIdx];
                        // Sur manette Xbox, un raccourci configuré avec B16 (PS/Guide) accepte aussi B8 (View/Back)
                        if (!isBtnDown && source == SourceXbox && btnIdx == 16 && curButtons.Count > 8)
                            isBtnDown = curButtons[8];

                        if (!isBtnDown)
                        {
                            allPressed = false;
                            break;
                        }
                    }
                    else
                    {
                        allPressed = false;
                        break;
                    }
                }

                if (validCount > 0 && allPressed)
                {
                    currentlyDownCombos.Add(comboSig);
                    if (trigger && triggeredProfileId == null && !_activeProfilePadShortcuts[source].Contains(comboSig))
                    {
                        triggeredProfileId = prof.id;
                    }
                }
            }

            _activeProfilePadShortcuts[source].RemoveWhere(c => !currentlyDownCombos.Contains(c));
            foreach (var c in currentlyDownCombos)
                _activeProfilePadShortcuts[source].Add(c);

            if (triggeredProfileId != null)
            {
                var now = DateTime.Now;
                if ((now - _lastProfileSwitch).TotalMilliseconds >= ProfileSwitchCooldownMs)
                {
                    _lastProfileSwitch = now;
                    string targetId = triggeredProfileId;
                    int targetSrc = source;
                    RunOnUiThread(() =>
                    {
                        if (!CanUseController(targetSrc)) return;
                        _autoSwitchEnabled = false;
                        ApplyProfile(targetId, notifyUser: true, targetSource: targetSrc);
                    });
                }
            }

            return currentlyDownCombos.Count > 0;
        }

        // Combo manette (PS + Options / View + Menu) : bascule les raccourcis sans passer par le menu
        private readonly bool[] _prevPauseCombo = new bool[2];
        private const int PauseToggleCooldownMs = 1000;
        private DateTime _lastPauseToggle = DateTime.MinValue;

        // Combo changement de profil à la volée (PS + L1 / R1 sur Sony, View + LB / RB sur Xbox)
        private readonly bool[] _prevProfilePrevCombo = new bool[2];
        private readonly bool[] _prevProfileNextCombo = new bool[2];
        private const int ProfileSwitchCooldownMs = 150;
        private DateTime _lastProfileSwitch = DateTime.MinValue;

        private void HandleProfileSwitchCombo(bool prevDown, bool nextDown, int source)
        {
            bool prevPressed = prevDown && !_prevProfilePrevCombo[source];
            bool nextPressed = nextDown && !_prevProfileNextCombo[source];
            _prevProfilePrevCombo[source] = prevDown;
            _prevProfileNextCombo[source] = nextDown;

            if (!prevPressed && !nextPressed) return;

            var now = DateTime.Now;
            if ((now - _lastProfileSwitch).TotalMilliseconds < ProfileSwitchCooldownMs) return;
            _lastProfileSwitch = now;

            int delta = nextPressed ? 1 : -1;
            int targetSrc = source;
            RunOnUiThread(() => { if (CanUseController(targetSrc)) CycleProfile(delta, targetSrc); });
        }

        private void HandlePauseCombo(bool comboDown, int source)
        {
            bool pressed = comboDown && !_prevPauseCombo[source];
            _prevPauseCombo[source] = comboDown;
            if (!pressed) return;

            // Anti-rebond : vu en test réel, des appuis rapprochés faisaient basculer 6 fois en 3 s
            // et laissaient l'appli en pause sans que l'utilisateur s'en rende compte
            var now = DateTime.Now;
            if ((now - _lastPauseToggle).TotalMilliseconds < PauseToggleCooldownMs) return;
            _lastPauseToggle = now;

            RunOnUiThread(() =>
            {
                if (!CanUseController(source)) return;
                _mappingEnabled = !_mappingEnabled;
                Log.Info($"Raccourcis {(_mappingEnabled ? "réactivés" : "mis en pause")} par combo manette");
                UpdateMappingToggleMenu();
                SyncStateToWebView();
                _trayIcon.ShowBalloonTip(1500,
                    _mappingEnabled ? "⌨️ Raccourcis actifs" : "🔇 Raccourcis en pause",
                    _mappingEnabled ? "La manette pilote à nouveau souris et raccourcis." : "Les boutons de la manette ne déclenchent plus d'action.",
                    ToolTipIcon.Info);
            });
        }

        // --- GESTION MULTIPOINT DU PAVÉ TACTILE (1 DOIGT = SOURIS/CLIC GAUCHE, 2 DOIGTS = SCROLL/CLIC DROIT) ---
        private void ProcessDualSenseMultiTouchpad(bool t1Active, int t1X, int t1Y, bool t2Active, int t2X, int t2Y)
        {
            var prof = GetActiveProfileForSource(SourceSony);

            if (prof == null || !prof.touchpadMouseEnabled || !_mappingEnabled || _isDashboardFocused)
            {
                ResetSonyTouchpadInput();
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

        private const int StickLeft = 0, StickRight = 1;

        // [manette][stick] : directions du mode « actions »
        private readonly StickDirectionTracker[][] _stickDirections =
        {
            new[] { new StickDirectionTracker(), new StickDirectionTracker() },
            new[] { new StickDirectionTracker(), new StickDirectionTracker() }
        };

        private void ProcessAnalogSticksMapping(float lx, float ly, float rx, float ry, int source)
        {
            if (!_mappingEnabled || _isDashboardFocused)
            {
                ReleaseStickOutputs(source); // aucune touche de stick ne doit rester enfoncée pendant la pause
                return;
            }

            var prof = GetActiveProfileForSource(source);
            if (prof == null) return;

            ApplySingleStickAction(prof.leftStick ?? LegacyStick(prof, prof.leftStickMode), lx, ly, StickLeft, source);
            ApplySingleStickAction(prof.rightStick ?? LegacyStick(prof, prof.rightStickMode), rx, ry, StickRight, source);
        }

        private static StickSettings LegacyStick(GamepadProfile p, string mode) =>
            new() { mode = mode, sensitivity = p.mouseSensitivity, deadzone = p.deadzone };

        private static string StickButtonKey(int source, int stick, int direction) =>
            $"{source}:{(stick == StickLeft ? "LS" : "RS")}:{StickDirectionTracker.Names[direction]}";

        // Relâche touches ZQSD/flèches et actions directionnelles d'une manette (pause, dashboard, changement de mode)
        private void ReleaseStickOutputs(int source)
        {
            var states = _stickKeyStates[source];
            for (int i = 0; i < states.Length; i++)
            {
                if (!states[i]) continue;
                int k = i % 8;
                if (k < 4) VirtualInput.SetScanKeyState(new[] { VirtualInput.SCAN_W, VirtualInput.SCAN_S, VirtualInput.SCAN_A, VirtualInput.SCAN_D }[k], false, ref states[i]);
                else VirtualInput.SetKeyState(new[] { VirtualInput.VK_UP, VirtualInput.VK_DOWN, VirtualInput.VK_LEFT, VirtualInput.VK_RIGHT }[k - 4], false, ref states[i]);
            }
            for (int stick = 0; stick < 2; stick++)
                foreach (int d in _stickDirections[source][stick].ReleaseAll())
                    ReleaseMapping(source, StickButtonKey(source, stick, d));
        }

        private void ReleaseSingleStickKeys(int source, int stick, string activeMode)
        {
            int keyBase = stick * 8;
            var states = _stickKeyStates[source];
            if (activeMode != "wasd")
            {
                byte[] scans = { VirtualInput.SCAN_W, VirtualInput.SCAN_S, VirtualInput.SCAN_A, VirtualInput.SCAN_D };
                for (int k = 0; k < 4; k++)
                    if (states[keyBase + k])
                        VirtualInput.SetScanKeyState(scans[k], false, ref states[keyBase + k]);
            }
            if (activeMode != "arrows")
            {
                byte[] vks = { VirtualInput.VK_UP, VirtualInput.VK_DOWN, VirtualInput.VK_LEFT, VirtualInput.VK_RIGHT };
                for (int k = 0; k < 4; k++)
                    if (states[keyBase + 4 + k])
                        VirtualInput.SetKeyState(vks[k], false, ref states[keyBase + 4 + k]);
            }
            if (activeMode != "actions")
            {
                foreach (int d in _stickDirections[source][stick].ReleaseAll())
                    ReleaseMapping(source, StickButtonKey(source, stick, d));
            }
        }

        private void ApplySingleStickAction(StickSettings s, float x, float y, int stick, int source)
        {
            string mode = s.mode ?? "native";
            ReleaseSingleStickKeys(source, stick, mode);
            if (string.IsNullOrEmpty(mode) || mode == "native") return;

            double dz = s.deadzone >= 0.01 && s.deadzone <= 0.5 ? s.deadzone : 0.08;
            double sens = s.sensitivity > 1 ? s.sensitivity : 14.0;
            if (s.invertY) y = -y;
            int keyBase = stick * 8; // chaque stick a ses propres touches ZQSD / flèches

            if (mode == "actions")
            {
                var tracker = _stickDirections[source][stick];
                foreach (var (direction, pressed) in tracker.Update(x, y, dz))
                {
                    string key = StickButtonKey(source, stick, direction);
                    if (pressed)
                        PressMappingAction(source, key, s.directions.GetValueOrDefault(StickDirectionTracker.Names[direction]));
                    else
                        ReleaseMapping(source, key);
                }
                return;
            }

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
                int cd = source * 2 + stick; // délai propre à chaque stick de chaque manette
                if (_scrollCooldown[cd] > 0) _scrollCooldown[cd]--;
                if (Math.Abs(fy) > 0.25f && _scrollCooldown[cd] <= 0)
                {
                    // Vitesse 14 = comportement historique (90 unités de molette par cran)
                    int wheel = (int)(-fy * 90 * (sens / 14.0));
                    VirtualInput.ScrollMouseWheel(wheel);
                    _scrollCooldown[cd] = 3;
                }
            }
            else if (mode == "wasd")
            {
                VirtualInput.SetScanKeyState(VirtualInput.SCAN_W, fy < -0.4f, ref _stickKeyStates[source][keyBase + 0]);
                VirtualInput.SetScanKeyState(VirtualInput.SCAN_S, fy > 0.4f, ref _stickKeyStates[source][keyBase + 1]);
                VirtualInput.SetScanKeyState(VirtualInput.SCAN_A, fx < -0.4f, ref _stickKeyStates[source][keyBase + 2]);
                VirtualInput.SetScanKeyState(VirtualInput.SCAN_D, fx > 0.4f, ref _stickKeyStates[source][keyBase + 3]);
            }
            else if (mode == "arrows")
            {
                VirtualInput.SetKeyState(VirtualInput.VK_UP, fy < -0.4f, ref _stickKeyStates[source][keyBase + 4]);
                VirtualInput.SetKeyState(VirtualInput.VK_DOWN, fy > 0.4f, ref _stickKeyStates[source][keyBase + 5]);
                VirtualInput.SetKeyState(VirtualInput.VK_LEFT, fx < -0.4f, ref _stickKeyStates[source][keyBase + 6]);
                VirtualInput.SetKeyState(VirtualInput.VK_RIGHT, fx > 0.4f, ref _stickKeyStates[source][keyBase + 7]);
            }
        }

        // Le chemin d'une manette Bluetooth contient le GUID du service HID Bluetooth, pas forcément "bth"
        private const string BluetoothHidServiceGuid = "{00001124-0000-1000-8000-00805f9b34fb}";

        private void CheckButton(ushort current, ushort flag, int jsIndex)
        {
            bool isPressed = (current & flag) != 0;
            bool wasPressed = (_prevButtons & flag) != 0;
            if (isPressed && !wasPressed)
                ExecuteMapping(jsIndex, SourceXbox);
            else if (!isPressed && wasPressed)
                ReleaseMapping(jsIndex, SourceXbox);
        }

        private void CheckTrigger(double currentValue, ref double prevValue, int jsIndex)
        {
            bool isPressed = currentValue > 0.5;
            bool wasPressed = prevValue > 0.5;
            if (isPressed && !wasPressed)
                ExecuteMapping(jsIndex, SourceXbox);
            else if (!isPressed && wasPressed)
                ReleaseMapping(jsIndex, SourceXbox);
            prevValue = currentValue;
        }

        private readonly ActionRunner _actionRunner = new();

        private static string ButtonKey(int jsIndex, int source) => $"{source}:{jsIndex}";

        private void ExecuteMapping(int jsIndex, int source)
        {
            // Sélecteur de profil ouvert (lancement d'appli) : la manette le pilote au lieu de déclencher ses actions.
            // Testé AVANT le garde « dashboard au premier plan » : le sélecteur est lui-même une fenêtre de l'appli.
            if (TryRouteToProfileChooser(jsIndex)) return;

            var activeProfile = GetActiveProfileForSource(source);
            if (activeProfile?.buttonMapping == null ||
                !activeProfile.buttonMapping.TryGetValue(jsIndex.ToString(), out string? raw))
                return;

            // Autoriser le changement de profil ("Profil Suivant" / "Profil Précédent") même quand le Dashboard est au premier plan
            if (_isDashboardFocused)
            {
                int targetSrc = source;
                if (string.Equals(raw, "Profil Suivant", StringComparison.OrdinalIgnoreCase))
                    RunOnUiThread(() => CycleProfile(1, targetSrc));
                else if (string.Equals(raw, "Profil Précédent", StringComparison.OrdinalIgnoreCase))
                    RunOnUiThread(() => CycleProfile(-1, targetSrc));
                return;
            }

            if (!_mappingEnabled)
                return;

            PressMappingAction(source, ButtonKey(jsIndex, source), raw);
        }

        // Commun aux boutons et aux directions de stick : même format d'action, même exécuteur
        private void PressMappingAction(int source, string buttonKey, string? raw)
        {
            var action = MappingAction.Parse(raw);
            if (action.Kind == MappingKind.None) return;

            try { _controllerActions[source].Press(buttonKey, action); }
            catch (Exception ex) { Log.Warn($"Action {buttonKey} impossible", ex); }
        }

        // Toujours traité, même en pause : une touche « maintenue » ne doit jamais rester bloquée
        private void ReleaseMapping(int jsIndex, int source) => ReleaseMapping(source, ButtonKey(jsIndex, source));

        private void ReleaseMapping(int source, string buttonKey)
        {
            try { _controllerActions[source].Release(buttonKey); }
            catch (Exception ex) { Log.Warn($"Relâchement {buttonKey} impossible", ex); }
        }
    }
}
