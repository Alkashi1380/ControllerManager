using System.Collections.Concurrent;
using System.Text.Json;

namespace ControllerManager
{
    // Lancement d'une appli depuis un raccourci manette : choix du profil, mémorisation, repli
    public partial class TrayApplicationContext
    {
        // exe lancé → profil choisi : quand l'appli revient au premier plan, l'auto-switch réapplique ce profil
        private readonly ConcurrentDictionary<string, string> _launchProfileOverrides = new(StringComparer.OrdinalIgnoreCase);
        private volatile ProfileChooserForm? _profileChooser;

        // Appelé depuis le thread d'entrées ou une macro : tout ce qui touche aux profils et fenêtres passe par le thread UI
        private void HandleLaunchRequest(LaunchTarget target, string? rawAction) =>
            HandleLaunchRequest(target, rawAction, source: null);

        private void HandleLaunchRequest(LaunchTarget target, string? rawAction, int? source)
        {
            RunOnUiThread(() =>
            {
                if (source.HasValue && !CanUseController(source.Value)) return;
                int targetSource = source ?? SelectedControllerSource;
                string? exeName = LaunchProfileResolver.ExeNameOf(target.Path);
                string appName = exeName ?? Path.GetFileName(target.Path.Trim().Trim('"'));
                var resolution = LaunchProfileResolver.Resolve(target.ProfileId, exeName, _profiles);

                if (resolution.Outcome != LaunchProfileResolver.Outcome.Ask)
                {
                    Log.Info($"Lancement de {appName} : {resolution.Reason}");
                    LaunchWithProfile(target, exeName, resolution.ProfileId, targetSource);
                    return;
                }

                if (_profileChooser != null && !_profileChooser.IsDisposed)
                {
                    _profileChooser.Activate();
                    return;
                }

                // Présélection = ce que donnerait le mode automatique (profil associé à l'exe)
                string? preselected = LaunchProfileResolver.FindByExe(exeName, _profiles)?.id;
                string profileOwnerId = GetActiveProfileForSource(targetSource)?.id ?? "";

                _profileChooser = new ProfileChooserForm(appName, _profiles, preselected, choice =>
                {
                    _profileChooser = null;
                    if (choice.Cancelled)
                    {
                        Log.Info($"Lancement de {appName} annulé depuis le sélecteur de profil");
                        return;
                    }
                    if (choice.Remember && rawAction != null)
                        RememberLaunchProfile(profileOwnerId, rawAction, choice.ProfileId ?? LaunchProfileResolver.Keep);

                    Log.Info($"Lancement de {appName} : profil choisi {(choice.ProfileId == null ? "(inchangé)" : choice.ProfileId)}");
                    LaunchWithProfile(target, exeName, choice.ProfileId, targetSource);
                });
                _profileChooser.Show();
            });
        }

        // Le profil est appliqué avant le lancement : l'appli démarre directement avec les bons réglages
        private void LaunchWithProfile(LaunchTarget target, string? exeName, string? profileId, int source)
        {
            if (profileId != null && _profiles.Any(p => p.id == profileId))
            {
                ApplyProfile(profileId, notifyUser: true, targetSource: source);
                if (exeName != null) _launchProfileOverrides[exeName] = profileId;
            }
            Task.Run(() => ActionRunner.LaunchProgram(target.Path, target.Args));
        }

        // « Mémoriser ce choix » : le profil est écrit dans le raccourci lui-même (buttonMapping du profil propriétaire)
        private void RememberLaunchProfile(string ownerProfileId, string rawAction, string profileChoice)
        {
            var owner = _profiles.FirstOrDefault(p => p.id == ownerProfileId);
            if (owner?.buttonMapping == null) return;

            var parsed = MappingAction.Parse(rawAction);
            if (parsed.Kind != MappingKind.Launch || parsed.Launch == null) return;

            var updated = new LaunchTarget { Path = parsed.Launch.Path, Args = parsed.Launch.Args, ProfileId = profileChoice };
            string newRaw = MappingAction.LaunchPrefix + JsonSerializer.Serialize(updated);

            int changed = 0;
            foreach (var key in owner.buttonMapping.Keys.ToList())
            {
                if (owner.buttonMapping[key] != rawAction) continue;
                owner.buttonMapping[key] = newRaw;
                changed++;
            }
            if (changed == 0) return;

            SaveProfilesToDisk();
            SyncStateToWebView();
            Log.Info($"Profil de lancement mémorisé sur {changed} raccourci(s) du profil « {owner.name} »");
        }

        // Pendant que le sélecteur est ouvert, la manette le pilote au lieu de déclencher ses raccourcis
        private bool TryRouteToProfileChooser(int jsIndex)
        {
            var chooser = _profileChooser;
            if (chooser == null || chooser.IsDisposed) return false;

            Action? command = jsIndex switch
            {
                12 => () => chooser.MoveSelection(-1), // croix haut
                13 => () => chooser.MoveSelection(+1), // croix bas
                0 => chooser.Confirm,                  // A / Croix
                1 => chooser.CancelChoice,             // B / Rond
                2 => chooser.ToggleRemember,           // X / Carré
                _ => null
            };
            if (command != null) RunOnUiThread(() => { if (!chooser.IsDisposed) command(); });
            return true; // les autres boutons sont ignorés tant que la fenêtre est ouverte
        }

        // Auto-switch : un profil choisi au lancement prime sur l'association par exe
        private GamepadProfile? FindLaunchOverride(string exeName) =>
            _launchProfileOverrides.TryGetValue(exeName, out var id) ? _profiles.FirstOrDefault(p => p.id == id) : null;
    }
}
