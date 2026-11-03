namespace ControllerManager
{
    public partial class TrayApplicationContext
    {
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

        private void ProcessAnalogSticksMapping(float lx, float ly, float rx, float ry)
        {
            if (!_mappingEnabled || _isDashboardFocused)
                return;

            var prof = GetActiveProfile();
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

            var activeProfile = GetActiveProfile();

            if (activeProfile != null &&
                activeProfile.buttonMapping != null &&
                activeProfile.buttonMapping.TryGetValue(jsIndex.ToString(), out string? action) &&
                !string.IsNullOrEmpty(action) &&
                action != "Défaut (Native)")
            {
                VirtualInput.ExecuteAction(action);
            }
        }
    }
}
