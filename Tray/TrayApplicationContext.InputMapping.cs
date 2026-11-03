namespace ControllerManager
{
    public partial class TrayApplicationContext
    {
        private void InputPollingLoop()
        {
            byte[] sonyBuffer = new byte[128];
            byte[] drainBuffer = new byte[128];
            DateTime lastSonyStickStep = DateTime.Now;
            DateTime lastXboxStickStep = DateTime.Now;
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

                // Mode multi-manettes : Sony et Xbox pilotent en même temps ; sinon seule la manette choisie agit
                bool multi = _multiControllerEnabled;
                bool sonySelected = _selectedControllerType == "PS5" || _selectedControllerType == "PS4" || _selectedWebGpIndex == 99 || xSlot < 0;

                bool sonyPolled = (multi || sonySelected) &&
                                  PollSonyPersistentStream(sonyBuffer, drainBuffer, ref lastSonyStickStep);

                bool pollXbox = xSlot >= 0 && (multi || !sonyPolled);
                if (!pollXbox)
                {
                    System.Threading.Thread.Sleep(sonyPolled ? 1 : 8);
                    continue;
                }

                // La lecture Sony bloque déjà jusqu'à 4 ms : pas d'attente supplémentaire dans ce cas
                if (!sonyPolled)
                    System.Threading.Thread.Sleep(8);

                if (XInput.XInputGetState((uint)xSlot, out var state) != XInput.ERROR_SUCCESS)
                {
                    xSlot = -1;
                    continue;
                }

                PollXboxState(state.Gamepad, ref lastXboxStickStep);
            }
        }

        private void PollXboxState(XInput.XINPUT_GAMEPAD pad, ref DateTime lastStickStep)
        {
            ushort buttons = pad.wButtons;

            HandlePauseCombo((buttons & XInput.BACK) != 0 && (buttons & XInput.START) != 0, SourceXbox);

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
                    -Math.Max(-1f, pad.sThumbRY / 32767f),
                    SourceXbox
                );
            }

            _prevButtons = buttons;
        }

        // Combo manette (PS + Options / View + Menu) : bascule les raccourcis sans passer par le menu
        private readonly bool[] _prevPauseCombo = new bool[2];

        private void HandlePauseCombo(bool comboDown, int source)
        {
            bool pressed = comboDown && !_prevPauseCombo[source];
            _prevPauseCombo[source] = comboDown;
            if (!pressed) return;

            RunOnUiThread(() =>
            {
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
            var prof = GetActiveProfile();

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

            var prof = GetActiveProfile();
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
                    ReleaseMapping(StickButtonKey(source, stick, d));
        }

        private void ApplySingleStickAction(StickSettings s, float x, float y, int stick, int source)
        {
            string mode = s.mode;
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
                        PressMappingAction(key, s.directions.GetValueOrDefault(StickDirectionTracker.Names[direction]));
                    else
                        ReleaseMapping(key);
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
            if (!_mappingEnabled || _isDashboardFocused)
                return;

            // Sélecteur de profil ouvert (lancement d'appli) : la manette le pilote au lieu de déclencher ses actions
            if (TryRouteToProfileChooser(jsIndex)) return;

            var activeProfile = GetActiveProfile();
            if (activeProfile?.buttonMapping == null ||
                !activeProfile.buttonMapping.TryGetValue(jsIndex.ToString(), out string? raw))
                return;

            PressMappingAction(ButtonKey(jsIndex, source), raw);
        }

        // Commun aux boutons et aux directions de stick : même format d'action, même exécuteur
        private void PressMappingAction(string buttonKey, string? raw)
        {
            var action = MappingAction.Parse(raw);
            if (action.Kind == MappingKind.None) return;

            try { _actionRunner.Press(buttonKey, action); }
            catch (Exception ex) { Log.Warn($"Action {buttonKey} impossible", ex); }
        }

        // Toujours traité, même en pause : une touche « maintenue » ne doit jamais rester bloquée
        private void ReleaseMapping(int jsIndex, int source) => ReleaseMapping(ButtonKey(jsIndex, source));

        private void ReleaseMapping(string buttonKey)
        {
            try { _actionRunner.Release(buttonKey); }
            catch (Exception ex) { Log.Warn($"Relâchement {buttonKey} impossible", ex); }
        }
    }
}
