namespace ControllerManager
{
    // Met à niveau les profils des anciennes versions sans perdre de réglage (même logique que migrateProfile() dans l'UI)
    public static class ProfileMigration
    {
        public const int CurrentVersion = 2;

        public static readonly string[] StickModes = { "native", "mouse", "scroll", "wasd", "arrows", "actions" };
        public static readonly string[] TriggerModes = { "off", "continuous", "section", "vibration" };
        public static readonly string[] Directions = { "up", "down", "left", "right" };

        public static void Migrate(GamepadProfile p)
        {
            p.exe ??= "";
            p.name ??= "Profil";
            p.buttonMapping ??= new Dictionary<string, string>();
            p.color ??= new LightbarColor();

            if (p.deadzone < 0 || p.deadzone > 0.5) p.deadzone = 0.08;
            if (p.mouseSensitivity <= 0) p.mouseSensitivity = 14.0;
            if (p.touchpadSensitivity <= 0) p.touchpadSensitivity = 1.2;
            if (!TriggerModes.Contains(p.l2TriggerMode)) p.l2TriggerMode = "off";
            if (!TriggerModes.Contains(p.r2TriggerMode)) p.r2TriggerMode = "off";
            if (p.l2TriggerForce <= 0) p.l2TriggerForce = 180;
            if (p.r2TriggerForce <= 0) p.r2TriggerForce = 180;
            if (string.IsNullOrEmpty(p.leftStickMode)) p.leftStickMode = "native";
            if (string.IsNullOrEmpty(p.rightStickMode)) p.rightStickMode = "native";

            // v1 → v2 : la vitesse et la zone morte communes deviennent les réglages de chaque stick
            p.leftStick ??= FromLegacy(p.leftStickMode, p);
            p.rightStick ??= FromLegacy(p.rightStickMode, p);
            Sanitize(p.leftStick, p);
            Sanitize(p.rightStick, p);

            // Les champs historiques reflètent le nouveau réglage (lecture par une ancienne version de l'appli)
            p.leftStickMode = p.leftStick.mode;
            p.rightStickMode = p.rightStick.mode;
            p.settingsVersion = CurrentVersion;
        }

        private static StickSettings FromLegacy(string mode, GamepadProfile p) => new()
        {
            mode = mode,
            sensitivity = p.mouseSensitivity,
            deadzone = p.deadzone,
            invertY = false
        };

        private static void Sanitize(StickSettings s, GamepadProfile p)
        {
            if (!StickModes.Contains(s.mode)) s.mode = "native";
            if (s.sensitivity <= 0 || s.sensitivity > 64) s.sensitivity = p.mouseSensitivity;
            if (s.deadzone < 0 || s.deadzone > 0.5) s.deadzone = p.deadzone;
            s.directions ??= new Dictionary<string, string>();
            foreach (var key in s.directions.Keys.Where(k => !Directions.Contains(k)).ToList())
                s.directions.Remove(key);
        }
    }
}
