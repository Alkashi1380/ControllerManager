using System.Text.Json;

namespace ControllerManager
{
    public partial class TrayApplicationContext
    {
        private void ApplyProfile(string profileId, bool notifyUser)
        {
            var matched = _profiles.FirstOrDefault(p => p.id == profileId);
            if (matched == null) return;

            bool changed = _currentProfileId != matched.id;
            if (changed) _actionRunner.ReleaseAll(); // les touches maintenues du profil précédent ne doivent pas survivre
            _currentProfileId = matched.id;

            ApplyActiveProfileHardwareEffects();

            RebuildTrayProfilesMenu();
            RefreshHudOverlayContent();
            _trayIcon.Text = $"Gamepad Manager - {ActiveProfileName}";

            if (notifyUser && changed)
            {
                _trayIcon.ShowBalloonTip(1500, "Profil Manette Activé",
                    $"Profil actif : {matched.name}", ToolTipIcon.Info);
            }

            SyncStateToWebView();
        }

        private void LoadProfilesFromDisk()
        {
            // Repli sur l'ancien emplacement (à côté de l'exe) tant qu'aucune sauvegarde n'existe dans %APPDATA%
            string sourcePath = File.Exists(ProfilesFilePath) ? ProfilesFilePath : LegacyProfilesFilePath;
            if (File.Exists(sourcePath))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<List<GamepadProfile>>(File.ReadAllText(sourcePath));
                    if (loaded != null && loaded.Count > 0)
                    {
                        int legacy = loaded.Count(p => p.settingsVersion < ProfileMigration.CurrentVersion);
                        foreach (var p in loaded)
                            ProfileMigration.Migrate(p);
                        if (legacy > 0)
                        {
                            BackupBeforeMigration(sourcePath);
                            Log.Info($"{legacy} profil(s) migré(s) vers le format v{ProfileMigration.CurrentVersion} (réglages par stick)");
                        }
                        // Liste complète (id compris) avant publication : le thread d'entrées ne voit jamais un état partiel
                        EnsureProfileIds(loaded);
                        _profiles = loaded;
                        return;
                    }
                }
                catch (Exception ex) { Log.Error("Lecture de profiles.json impossible, profils par défaut utilisés", ex); }
            }

            var defaults = new List<GamepadProfile>
            {
                new()
                {
                    name = "Jeux / Natif (Aucun raccourci)",
                    exe = "cs2.exe, cod.exe, apex.exe",
                    deadzone = 0.04,
                    leftStickMode = "native",
                    rightStickMode = "native",
                    mouseSensitivity = 14.0,
                    touchpadMouseEnabled = false,
                    touchpadSensitivity = 1.2,
                    l2TriggerMode = "off",
                    l2TriggerForce = 180,
                    r2TriggerMode = "off",
                    r2TriggerForce = 180,
                    color = new LightbarColor { r = 239, g = 68, b = 68 },
                    buttonMapping = new Dictionary<string, string>()
                },
                new()
                {
                    name = "Multimédia (VLC + Trackpad PS5)",
                    exe = "vlc.exe",
                    deadzone = 0.08,
                    leftStickMode = "mouse",
                    rightStickMode = "scroll",
                    mouseSensitivity = 12.0,
                    touchpadMouseEnabled = true,
                    touchpadSensitivity = 1.3,
                    l2TriggerMode = "off",
                    l2TriggerForce = 150,
                    r2TriggerMode = "off",
                    r2TriggerForce = 150,
                    color = new LightbarColor { r = 6, g = 182, b = 212 },
                    buttonMapping = new Dictionary<string, string>
                    {
                        { "0", "Play / Pause" },
                        { "1", "Plein Écran (F11)" },
                        { "2", "Mute" },
                        { "4", "Piste Précédente" },
                        { "5", "Piste Suivante" },
                        { "12", "Volume +" },
                        { "13", "Volume -" },
                        { "17", "Play / Pause" },
                        { "18", "Plein Écran (F11)" },
                        { "19", "Mute" }
                    }
                },
                new()
                {
                    name = "Bureau (Trackpad 2 Doigts & Souris)",
                    exe = "calc.exe",
                    deadzone = 0.09,
                    leftStickMode = "mouse",
                    rightStickMode = "scroll",
                    mouseSensitivity = 15.0,
                    touchpadMouseEnabled = true,
                    touchpadSensitivity = 1.4,
                    l2TriggerMode = "off",
                    l2TriggerForce = 150,
                    r2TriggerMode = "off",
                    r2TriggerForce = 150,
                    color = new LightbarColor { r = 0, g = 102, b = 255 },
                    buttonMapping = new Dictionary<string, string>
                    {
                        { "0", "Clic Gauche Souris" },
                        { "1", "Clic Droit Souris" },
                        { "2", "Touche Entrée" },
                        { "3", "Echap" },
                        { "4", "Alt + Tab" },
                        { "16", "Afficher le Bureau (Win + D)" },
                        { "17", "Clic Gauche Souris" },
                        { "18", "Clic Droit Souris" },
                        { "19", "Mute" }
                    }
                }
            };
            foreach (var p in defaults)
                ProfileMigration.Migrate(p); // crée leftStick / rightStick à partir des réglages ci-dessus
            EnsureProfileIds(defaults);
            _profiles = defaults;
        }

        // Copie de l'ancien fichier, une seule fois : permet de revenir en arrière si la migration pose problème
        private void BackupBeforeMigration(string sourcePath)
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                string backup = Path.Combine(AppDataDir, "profiles.v1-backup.json");
                if (!File.Exists(backup))
                {
                    File.Copy(sourcePath, backup);
                    Log.Info($"Sauvegarde des profils avant migration : {backup}");
                }
            }
            catch (Exception ex) { Log.Warn("Sauvegarde avant migration impossible", ex); }
        }

        private void SaveProfilesToDisk()
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                File.WriteAllText(ProfilesFilePath, JsonSerializer.Serialize(_profiles));
            }
            catch (Exception ex) { Log.Error("Sauvegarde de profiles.json impossible", ex); }
        }

        // Donne un id unique aux profils qui n'en ont pas (anciens fichiers) ou qui partagent le même
        private static void EnsureProfileIds(List<GamepadProfile> profiles)
        {
            var seen = new HashSet<string>();
            foreach (var p in profiles)
            {
                if (string.IsNullOrWhiteSpace(p.id) || !seen.Add(p.id))
                {
                    p.id = Guid.NewGuid().ToString("N");
                    seen.Add(p.id);
                }
            }
        }
    }
}
