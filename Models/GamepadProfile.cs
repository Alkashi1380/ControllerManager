namespace ControllerManager
{
    public class GamepadProfile
    {
        // Version du format : absent/0 = ancien fichier, 2 = réglages par stick. Fixée par ProfileMigration.Migrate.
        public int settingsVersion { get; set; }

        public string id { get; set; } = "";
        public string name { get; set; } = "Jeux / Natif (Aucun raccourci)";
        public string exe { get; set; } = "default";

        // Zone morte utilisée par l'onglet Diagnostic (et source de migration des sticks)
        public double deadzone { get; set; } = 0.08;

        // Anciens réglages communs aux deux sticks : conservés pour compatibilité, remplacés par leftStick / rightStick
        public string leftStickMode { get; set; } = "native";
        public string rightStickMode { get; set; } = "native";
        public double mouseSensitivity { get; set; } = 14.0;

        public StickSettings? leftStick { get; set; }
        public StickSettings? rightStick { get; set; }

        public bool touchpadMouseEnabled { get; set; } = true;
        public double touchpadSensitivity { get; set; } = 1.2;
        public string l2TriggerMode { get; set; } = "off";
        public int l2TriggerForce { get; set; } = 180;
        public string r2TriggerMode { get; set; } = "off";
        public int r2TriggerForce { get; set; } = 180;
        public LightbarColor color { get; set; } = new();
        public Dictionary<string, string> buttonMapping { get; set; } = new();
    }

    // Réglage complet d'un stick analogique, propre à chaque profil
    public class StickSettings
    {
        // native | mouse | scroll | wasd | arrows | actions
        public string mode { get; set; } = "native";
        public double sensitivity { get; set; } = 14.0;
        public double deadzone { get; set; } = 0.08;
        public bool invertY { get; set; } = false;

        // Mode « actions » : une action par direction (up / down / left / right), même format que buttonMapping
        public Dictionary<string, string> directions { get; set; } = new();
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
}
