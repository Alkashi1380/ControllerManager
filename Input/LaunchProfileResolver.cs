namespace ControllerManager
{
    // Profil manette à appliquer quand un raccourci lance une application
    public static class LaunchProfileResolver
    {
        public const string Auto = "";          // profil associé à l'exe, sinon profil actuel
        public const string Keep = "__keep__";  // ne jamais changer de profil
        public const string Ask = "__ask__";    // fenêtre de choix à chaque lancement

        public enum Outcome { UseProfile, KeepCurrent, Ask }

        public sealed record Resolution(Outcome Outcome, string? ProfileId, string Reason);

        public static Resolution Resolve(string? choice, string? launchedExe, IReadOnlyList<GamepadProfile> profiles)
        {
            choice ??= Auto;
            if (choice == Ask) return new(Outcome.Ask, null, "choix demandé au lancement");
            if (choice == Keep) return new(Outcome.KeepCurrent, null, "profil actuel conservé (réglage du raccourci)");

            if (choice != Auto)
            {
                var chosen = profiles.FirstOrDefault(p => p.id == choice);
                if (chosen != null) return new(Outcome.UseProfile, chosen.id, $"profil du raccourci « {chosen.name} »");
                // Profil supprimé depuis : on retombe sur la règle automatique
            }

            var byExe = FindByExe(launchedExe, profiles);
            if (byExe != null) return new(Outcome.UseProfile, byExe.id, $"profil associé à {launchedExe} « {byExe.name} »");

            return new(Outcome.KeepCurrent, null, "aucun profil défini pour cette appli : profil actuel conservé");
        }

        public static GamepadProfile? FindByExe(string? exeName, IReadOnlyList<GamepadProfile> profiles)
        {
            if (string.IsNullOrWhiteSpace(exeName)) return null;
            return profiles.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.exe) &&
                p.exe.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Any(e => string.Equals(e, exeName, StringComparison.OrdinalIgnoreCase)));
        }

        // Nom de l'exe lancé quand on peut le déduire (pas pour un .lnk ou un lien steam://)
        public static string? ExeNameOf(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string clean = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (clean.Contains("://")) return null;
            string file = Path.GetFileName(clean);
            return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file : null;
        }
    }
}
