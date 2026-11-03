using ControllerManager;
using Xunit;

namespace ControllerManager.Tests
{
    public class MappingActionTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Défaut (Native)")]
        [InlineData("Touche:")]
        [InlineData("Touche:A+B")]
        [InlineData("Maintenir:Ctrl+Alt")]
        [InlineData("Texte:")]
        [InlineData("Lancer:{}")]
        [InlineData("Lancer:pas du json")]
        [InlineData("URL:javascript:alert(1)")]
        [InlineData("URL:file:///C:/Windows/System32/cmd.exe")]
        [InlineData("Macro:[]")]
        [InlineData("Macro:[{\"type\":\"delay\",\"ms\":999999}]")]
        public void InvalidOrEmpty_IsNone(string? raw)
        {
            Assert.Equal(MappingKind.None, MappingAction.Parse(raw).Kind);
        }

        [Fact]
        public void PresetName_IsPreset()
        {
            var a = MappingAction.Parse("Volume +");
            Assert.Equal(MappingKind.Preset, a.Kind);
            Assert.Equal("Volume +", a.Payload);
            Assert.False(a.NeedsRelease);
        }

        [Fact]
        public void KeyTap_ParsesCombo()
        {
            var a = MappingAction.Parse("Touche:Ctrl+Shift+S");
            Assert.Equal(MappingKind.KeyTap, a.Kind);
            Assert.Equal(new byte[] { 0x11, 0x10 }, a.Combo!.Modifiers);
            Assert.Equal(0x53, a.Combo.Key);
        }

        [Fact]
        public void Hold_NeedsRelease()
        {
            var a = MappingAction.Parse("Maintenir:Shift+W");
            Assert.Equal(MappingKind.KeyHold, a.Kind);
            Assert.True(a.NeedsRelease);
        }

        [Theory]
        [InlineData("Turbo:Space", true)]
        [InlineData("Turbo:Clic Gauche Souris", false)] // turbo d'une action prédéfinie
        public void Turbo_AcceptsComboOrPreset(string raw, bool hasCombo)
        {
            var a = MappingAction.Parse(raw);
            Assert.Equal(MappingKind.Turbo, a.Kind);
            Assert.True(a.NeedsRelease);
            Assert.Equal(hasCombo, a.Combo != null);
        }

        [Fact]
        public void Text_KeepsAccentsAndNewlines()
        {
            var a = MappingAction.Parse("Texte:Bien joué !\nGG");
            Assert.Equal(MappingKind.Text, a.Kind);
            Assert.Equal("Bien joué !\nGG", a.Payload);
        }

        [Fact]
        public void Launch_ParsesPathAndArgs()
        {
            var a = MappingAction.Parse("Lancer:{\"path\":\"C:\\\\Program Files\\\\OBS\\\\obs64.exe\",\"args\":\"--startreplaybuffer\"}");
            Assert.Equal(MappingKind.Launch, a.Kind);
            Assert.Equal(@"C:\Program Files\OBS\obs64.exe", a.Launch!.Path);
            Assert.Equal("--startreplaybuffer", a.Launch.Args);
        }

        [Theory]
        [InlineData("URL:https://www.twitch.tv", true)]
        [InlineData("URL:http://localhost:8080/x", true)]
        [InlineData("URL:ms-settings:bluetooth", false)]
        public void Url_OnlyAllowsWebLinks(string raw, bool allowed)
        {
            Assert.Equal(allowed ? MappingKind.Url : MappingKind.None, MappingAction.Parse(raw).Kind);
        }

        [Fact]
        public void Macro_KeepsValidStepsInOrder()
        {
            const string raw = "Macro:[" +
                "{\"type\":\"key\",\"value\":\"Ctrl+C\"}," +
                "{\"type\":\"delay\",\"ms\":150}," +
                "{\"type\":\"key\",\"value\":\"pas une touche\"}," +      // ignorée
                "{\"type\":\"text\",\"value\":\"Salut\"}," +
                "{\"type\":\"action\",\"value\":\"Volume +\"}," +
                "{\"type\":\"launch\",\"path\":\"notepad.exe\",\"args\":\"\"}," +
                "{\"type\":\"url\",\"value\":\"https://example.com\"}," +
                "{\"type\":\"inconnu\",\"value\":\"x\"}]";              // ignorée
            var a = MappingAction.Parse(raw);

            Assert.Equal(MappingKind.Macro, a.Kind);
            Assert.Equal(new[] { "key", "delay", "text", "action", "launch", "url" }, a.Steps.Select(s => s.Type));
            Assert.Equal(150, a.Steps[1].Ms);
        }

        [Fact]
        public void Macro_IsCappedToMaxSteps()
        {
            var steps = string.Join(",", Enumerable.Repeat("{\"type\":\"key\",\"value\":\"A\"}", 200));
            var a = MappingAction.Parse($"Macro:[{steps}]");
            Assert.Equal(MappingAction.MaxMacroSteps, a.Steps.Count);
        }

        // Chaînes produites telles quelles par l'éditeur de UI/index.html (serializeMappingAction)
        [Theory]
        [InlineData("Touche:Ctrl+Shift+S", MappingKind.KeyTap)]
        [InlineData("Maintenir:Shift", MappingKind.KeyHold)]
        [InlineData("Turbo:Clic Gauche Souris", MappingKind.Turbo)]
        [InlineData("URL:https://www.twitch.tv/directory", MappingKind.Url)]
        [InlineData("Lancer:{\"path\":\"notepad.exe\",\"args\":\"\"}", MappingKind.Launch)]
        public void StringsProducedByUi_AreUnderstood(string raw, MappingKind kind)
        {
            Assert.Equal(kind, MappingAction.Parse(raw).Kind);
        }

        [Fact]
        public void MacroProducedByUi_KeepsAllSteps()
        {
            const string raw = "Macro:[{\"type\":\"key\",\"value\":\"Ctrl+C\"},{\"type\":\"text\",\"value\":\"Salut à tous 👋\"},{\"type\":\"delay\",\"ms\":200},{\"type\":\"launch\",\"path\":\"notepad.exe\",\"args\":\"\"}]";
            var a = MappingAction.Parse(raw);
            Assert.Equal(MappingKind.Macro, a.Kind);
            Assert.Equal(4, a.Steps.Count);
            Assert.Equal("Salut à tous 👋", a.Steps[1].Value);
        }

        [Fact]
        public void SendInputStruct_HasNativeSize()
        {
            // Windows ignore SendInput si cbSize ne correspond pas à sizeof(INPUT)
            Assert.Equal(IntPtr.Size == 8 ? 40 : 28, VirtualInput.InputStructSize);
        }
    }
}
