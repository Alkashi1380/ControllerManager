namespace ControllerManager
{
    public class GamepadProfile
    {
        public string id { get; set; } = "";
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
}
