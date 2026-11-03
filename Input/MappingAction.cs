using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControllerManager
{
    public enum MappingKind
    {
        None,       // « Défaut (Native) » : le bouton garde son comportement normal
        Preset,     // action prédéfinie (« Volume + », « Clic Gauche Souris »…)
        KeyTap,     // Touche:Ctrl+S        → appui + relâchement
        KeyHold,    // Maintenir:Shift      → enfoncée tant que le bouton est tenu
        Turbo,      // Turbo:Space          → répétée tant que le bouton est tenu
        Text,       // Texte:Bonjour !      → texte tapé caractère par caractère
        Launch,     // Lancer:{"path":…,"args":…}
        Url,        // URL:https://…
        Macro       // Macro:[{"type":"key","value":"Ctrl+C"},{"type":"delay","ms":100},…]
    }

    public sealed class LaunchTarget
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("args")] public string Args { get; set; } = "";
    }

    public sealed class MacroStep
    {
        // key | text | delay | action | launch | url
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        [JsonPropertyName("ms")] public int Ms { get; set; }
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("args")] public string Args { get; set; } = "";
    }

    // Action associée à un bouton, décodée depuis la chaîne stockée dans buttonMapping (format partagé avec l'UI)
    public sealed class MappingAction
    {
        public const string NativeAction = "Défaut (Native)";
        public const string KeyPrefix = KeyCombo.ActionPrefix; // "Touche:"
        public const string HoldPrefix = "Maintenir:";
        public const string TurboPrefix = "Turbo:";
        public const string TextPrefix = "Texte:";
        public const string LaunchPrefix = "Lancer:";
        public const string UrlPrefix = "URL:";
        public const string MacroPrefix = "Macro:";

        public const int MaxMacroSteps = 64;
        public const int MaxDelayMs = 10_000;

        public MappingKind Kind { get; private init; }
        public string Payload { get; private init; } = "";
        public KeyCombo? Combo { get; private init; }
        public LaunchTarget? Launch { get; private init; }
        public IReadOnlyList<MacroStep> Steps { get; private init; } = Array.Empty<MacroStep>();

        // Une action « maintenue » a besoin de l'événement de relâchement du bouton
        public bool NeedsRelease => Kind is MappingKind.KeyHold or MappingKind.Turbo;

        public static readonly MappingAction None = new() { Kind = MappingKind.None };

        public static MappingAction Parse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw == NativeAction)
                return None;

            try
            {
                if (TryStrip(raw, KeyPrefix, out var keyText))
                    return KeyCombo.TryParse(keyText, out var c) ? new() { Kind = MappingKind.KeyTap, Payload = keyText, Combo = c } : None;

                if (TryStrip(raw, HoldPrefix, out var holdText))
                    return KeyCombo.TryParse(holdText, out var c) ? new() { Kind = MappingKind.KeyHold, Payload = holdText, Combo = c } : None;

                if (TryStrip(raw, TurboPrefix, out var turboText))
                {
                    // Turbo d'une touche (Turbo:Space) ou d'une action prédéfinie (Turbo:Clic Gauche Souris)
                    if (KeyCombo.TryParse(turboText, out var c))
                        return new() { Kind = MappingKind.Turbo, Payload = turboText, Combo = c };
                    return string.IsNullOrWhiteSpace(turboText) ? None : new() { Kind = MappingKind.Turbo, Payload = turboText };
                }

                if (TryStrip(raw, TextPrefix, out var text))
                    return text.Length == 0 ? None : new() { Kind = MappingKind.Text, Payload = text };

                if (TryStrip(raw, LaunchPrefix, out var launchJson))
                {
                    var target = JsonSerializer.Deserialize<LaunchTarget>(launchJson);
                    return target == null || string.IsNullOrWhiteSpace(target.Path)
                        ? None
                        : new() { Kind = MappingKind.Launch, Payload = launchJson, Launch = target };
                }

                if (TryStrip(raw, UrlPrefix, out var url))
                    return IsAllowedUrl(url) ? new() { Kind = MappingKind.Url, Payload = url } : None;

                if (TryStrip(raw, MacroPrefix, out var macroJson))
                {
                    var steps = JsonSerializer.Deserialize<List<MacroStep>>(macroJson);
                    if (steps == null || steps.Count == 0) return None;
                    var valid = steps.Where(IsValidStep).Take(MaxMacroSteps).ToList();
                    return valid.Count == 0 ? None : new() { Kind = MappingKind.Macro, Payload = macroJson, Steps = valid };
                }
            }
            catch (JsonException)
            {
                return None;
            }

            // Sans préfixe : nom d'une action prédéfinie
            return new() { Kind = MappingKind.Preset, Payload = raw };
        }

        // Seuls les liens web sont ouverts : pas de file:, javascript:, ms-settings: venant d'un profil importé
        public static bool IsAllowedUrl(string url) =>
            Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        public static bool IsValidStep(MacroStep s) => s.Type switch
        {
            "key" => KeyCombo.TryParse(s.Value, out _),
            "text" => !string.IsNullOrEmpty(s.Value),
            "delay" => s.Ms > 0 && s.Ms <= MaxDelayMs,
            "action" => !string.IsNullOrWhiteSpace(s.Value) && s.Value != NativeAction,
            "launch" => !string.IsNullOrWhiteSpace(s.Path),
            "url" => IsAllowedUrl(s.Value),
            _ => false
        };

        private static bool TryStrip(string raw, string prefix, out string rest)
        {
            if (raw.StartsWith(prefix, StringComparison.Ordinal))
            {
                rest = raw.Substring(prefix.Length);
                return true;
            }
            rest = "";
            return false;
        }
    }
}
