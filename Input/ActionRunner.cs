using System.Collections.Concurrent;
using System.Diagnostics;

namespace ControllerManager
{
    // Exécute les actions des boutons. Les actions longues (macro, turbo, lancement) tournent hors du thread
    // d'entrées pour ne jamais retarder la lecture de la manette.
    public sealed class ActionRunner
    {
        public const int TurboIntervalMs = 80; // ~12 répétitions par seconde

        // Tâche de fond d'un bouton : le turbo s'arrête au relâchement, une macro va jusqu'au bout
        private sealed record RunningAction(CancellationTokenSource Cts, bool StopOnRelease);

        private readonly ConcurrentDictionary<string, KeyCombo> _held = new();
        private readonly ConcurrentDictionary<string, RunningAction> _running = new();

        // buttonKey identifie le bouton physique (manette + index) pour associer appui et relâchement
        public void Press(string buttonKey, MappingAction action)
        {
            switch (action.Kind)
            {
                case MappingKind.None:
                    return;

                case MappingKind.Preset:
                    VirtualInput.ExecuteAction(action.Payload);
                    return;

                case MappingKind.KeyTap:
                    VirtualInput.SendKeyCombo(action.Combo!);
                    return;

                case MappingKind.KeyHold:
                    if (_held.TryAdd(buttonKey, action.Combo!))
                        VirtualInput.PressKeyCombo(action.Combo!);
                    return;

                case MappingKind.Turbo:
                    StartBackground(buttonKey, ct => TurboLoop(action, ct), stopOnRelease: true);
                    return;

                case MappingKind.Text:
                    VirtualInput.SendUnicodeText(action.Payload);
                    return;

                case MappingKind.Launch:
                    Task.Run(() => LaunchProgram(action.Launch!.Path, action.Launch.Args));
                    return;

                case MappingKind.Url:
                    Task.Run(() => OpenUrl(action.Payload));
                    return;

                case MappingKind.Macro:
                    // Une macro déjà en cours sur ce bouton n'est pas relancée (évite l'empilement en cas d'appuis répétés)
                    StartBackground(buttonKey, ct => RunMacro(action.Steps, ct), stopOnRelease: false);
                    return;
            }
        }

        public void Release(string buttonKey)
        {
            if (_held.TryRemove(buttonKey, out var combo))
                VirtualInput.ReleaseKeyCombo(combo);

            if (_running.TryGetValue(buttonKey, out var running) && running.StopOnRelease)
                SafeCancel(running.Cts);
        }

        // Pause, changement de profil, fermeture : aucune touche ne doit rester enfoncée
        public void ReleaseAll()
        {
            foreach (var key in _held.Keys.ToList())
                if (_held.TryRemove(key, out var combo))
                    VirtualInput.ReleaseKeyCombo(combo);

            foreach (var running in _running.Values.ToList())
                SafeCancel(running.Cts);
        }

        // La tâche a pu se terminer (et libérer son jeton) entre la lecture du dictionnaire et l'annulation
        private static void SafeCancel(CancellationTokenSource cts)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public bool IsRunning(string buttonKey) => _running.ContainsKey(buttonKey);

        public bool IsHeld(string buttonKey) => _held.ContainsKey(buttonKey);

        private void StartBackground(string buttonKey, Func<CancellationToken, Task> work, bool stopOnRelease)
        {
            // Turbo : un nouvel appui relance la répétition ; macro : un appui pendant l'exécution est ignoré
            if (_running.TryGetValue(buttonKey, out var existing))
            {
                if (!stopOnRelease) return;
                SafeCancel(existing.Cts);
            }

            var entry = new RunningAction(new CancellationTokenSource(), stopOnRelease);
            _running[buttonKey] = entry;

            Task.Run(async () =>
            {
                try { await work(entry.Cts.Token); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log.Warn($"Action du bouton {buttonKey} interrompue", ex); }
                finally
                {
                    // Ne retire que sa propre entrée : un nouvel appui a pu en créer une autre entre-temps
                    _running.TryRemove(new KeyValuePair<string, RunningAction>(buttonKey, entry));
                    entry.Cts.Dispose();
                }
            });
        }

        private static async Task TurboLoop(MappingAction action, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (action.Combo != null) VirtualInput.SendKeyCombo(action.Combo);
                else VirtualInput.ExecuteAction(action.Payload);
                await Task.Delay(TurboIntervalMs, ct);
            }
        }

        private static async Task RunMacro(IReadOnlyList<MacroStep> steps, CancellationToken ct)
        {
            foreach (var step in steps)
            {
                ct.ThrowIfCancellationRequested();
                switch (step.Type)
                {
                    case "key":
                        if (KeyCombo.TryParse(step.Value, out var combo)) VirtualInput.SendKeyCombo(combo!);
                        break;
                    case "text":
                        VirtualInput.SendUnicodeText(step.Value);
                        break;
                    case "delay":
                        await Task.Delay(Math.Clamp(step.Ms, 1, MappingAction.MaxDelayMs), ct);
                        continue; // pas de petite pause en plus après une pause explicite
                    case "action":
                        VirtualInput.ExecuteAction(step.Value);
                        break;
                    case "launch":
                        LaunchProgram(step.Path, step.Args);
                        break;
                    case "url":
                        OpenUrl(step.Value);
                        break;
                }
                // Petit délai entre deux étapes : certaines applis perdent des touches envoyées trop vite
                await Task.Delay(15, ct);
            }
        }

        public static void LaunchProgram(string path, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')), args ?? "")
                {
                    UseShellExecute = true
                };
                string? dir = Path.IsPathRooted(psi.FileName) ? Path.GetDirectoryName(psi.FileName) : null;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;

                Process.Start(psi)?.Dispose();
                Log.Info($"Application lancée par la manette : {psi.FileName} {args}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Lancement impossible : {path}", ex);
            }
        }

        public static void OpenUrl(string url)
        {
            if (!MappingAction.IsAllowedUrl(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url.Trim()) { UseShellExecute = true })?.Dispose();
                Log.Info($"Lien ouvert par la manette : {url}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Ouverture du lien impossible : {url}", ex);
            }
        }
    }
}
