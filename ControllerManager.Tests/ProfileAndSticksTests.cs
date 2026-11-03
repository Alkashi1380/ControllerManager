using System.Text.Json;
using ControllerManager;
using Xunit;

namespace ControllerManager.Tests
{
    public class ProfileMigrationTests
    {
        // Profil tel qu'écrit par les versions précédentes (pas de leftStick / rightStick ni settingsVersion)
        private const string LegacyJson = """
            [{"id":"a1","name":"Bureau","exe":"calc.exe","deadzone":0.11,"leftStickMode":"mouse","rightStickMode":"scroll",
              "mouseSensitivity":19,"touchpadMouseEnabled":true,"touchpadSensitivity":1.4,
              "l2TriggerMode":"section","l2TriggerForce":150,"r2TriggerMode":"off","r2TriggerForce":0,
              "color":{"r":6,"g":182,"b":212},"buttonMapping":{"0":"Clic Gauche Souris"}}]
            """;

        [Fact]
        public void LegacyProfile_GetsPerStickSettingsFromSharedValues()
        {
            var p = JsonSerializer.Deserialize<List<GamepadProfile>>(LegacyJson)![0];
            Assert.Equal(0, p.settingsVersion);
            Assert.Null(p.leftStick);

            ProfileMigration.Migrate(p);

            Assert.Equal(ProfileMigration.CurrentVersion, p.settingsVersion);
            Assert.Equal("mouse", p.leftStick!.mode);
            Assert.Equal(19, p.leftStick.sensitivity);
            Assert.Equal(0.11, p.leftStick.deadzone);
            Assert.Equal("scroll", p.rightStick!.mode);
            Assert.Equal(19, p.rightStick.sensitivity);
        }

        [Fact]
        public void LegacyProfile_KeepsTriggersColorAndMappings()
        {
            var p = JsonSerializer.Deserialize<List<GamepadProfile>>(LegacyJson)![0];
            ProfileMigration.Migrate(p);

            Assert.Equal("section", p.l2TriggerMode);
            Assert.Equal(150, p.l2TriggerForce);
            Assert.Equal(180, p.r2TriggerForce); // force absente/nulle → valeur par défaut
            Assert.Equal(6, p.color.r);
            Assert.Equal(212, p.color.b);
            Assert.Equal("Clic Gauche Souris", p.buttonMapping["0"]);
        }

        [Fact]
        public void MigratedProfile_RoundTripsThroughJson()
        {
            var p = JsonSerializer.Deserialize<List<GamepadProfile>>(LegacyJson)![0];
            ProfileMigration.Migrate(p);
            p.rightStick!.mode = "actions";
            p.rightStick.invertY = true;
            p.rightStick.directions["up"] = "Touche:Ctrl+Z";

            var back = JsonSerializer.Deserialize<GamepadProfile>(JsonSerializer.Serialize(p))!;
            ProfileMigration.Migrate(back); // déjà en v2 : rien ne doit être écrasé

            Assert.Equal("actions", back.rightStick!.mode);
            Assert.True(back.rightStick.invertY);
            Assert.Equal("Touche:Ctrl+Z", back.rightStick.directions["up"]);
            Assert.Equal("actions", back.rightStickMode); // champ historique synchronisé
        }

        [Fact]
        public void InvalidValues_AreSanitized()
        {
            var p = new GamepadProfile
            {
                l2TriggerMode = "laser",
                color = null!,
                buttonMapping = null!,
                leftStick = new StickSettings { mode = "teleport", deadzone = 3, sensitivity = -1, directions = new() { ["up"] = "Volume +", ["nord"] = "x" } }
            };
            ProfileMigration.Migrate(p);

            Assert.Equal("off", p.l2TriggerMode);
            Assert.NotNull(p.color);
            Assert.NotNull(p.buttonMapping);
            Assert.Equal("native", p.leftStick!.mode);
            Assert.Equal(p.deadzone, p.leftStick.deadzone);
            Assert.Equal(p.mouseSensitivity, p.leftStick.sensitivity);
            Assert.Equal(new[] { "up" }, p.leftStick.directions.Keys);
        }
    }

    public class StickDirectionTrackerTests
    {
        [Fact]
        public void PushUp_PressesThenReleasesWithHysteresis()
        {
            var t = new StickDirectionTracker();
            Assert.Empty(t.Update(0, -0.45f, 0.08));                     // sous le seuil d'appui
            Assert.Equal(new[] { (StickDirectionTracker.Up, true) }, t.Update(0, -0.6f, 0.08));
            Assert.Empty(t.Update(0, -0.4f, 0.08));                      // entre 0.35 et 0.5 : reste enfoncé
            Assert.True(t.IsPressed(StickDirectionTracker.Up));
            Assert.Equal(new[] { (StickDirectionTracker.Up, false) }, t.Update(0, -0.3f, 0.08));
        }

        [Fact]
        public void Diagonal_PressesTwoDirections()
        {
            var t = new StickDirectionTracker();
            var changes = t.Update(0.7f, 0.7f, 0.08); // bas-droite
            Assert.Contains((StickDirectionTracker.Down, true), changes);
            Assert.Contains((StickDirectionTracker.Right, true), changes);
            Assert.Equal(2, changes.Count);
        }

        [Fact]
        public void InsideDeadzone_ReleasesEverything()
        {
            var t = new StickDirectionTracker();
            t.Update(-0.9f, 0, 0.08);
            var changes = t.Update(-0.6f, 0, 0.7); // zone morte énorme : le stick est considéré au repos
            Assert.Equal(new[] { (StickDirectionTracker.Left, false) }, changes);
        }

        [Fact]
        public void ReleaseAll_ReturnsHeldDirections()
        {
            var t = new StickDirectionTracker();
            t.Update(0.8f, -0.8f, 0.08);
            Assert.Equal(new[] { StickDirectionTracker.Up, StickDirectionTracker.Right }, t.ReleaseAll());
            Assert.Empty(t.ReleaseAll());
        }
    }

    public class LaunchProfileResolverTests
    {
        private static readonly List<GamepadProfile> Profiles = new()
        {
            new() { id = "jeu", name = "Jeu", exe = "ConsoleHub.exe, game.exe" },
            new() { id = "bureau", name = "Bureau", exe = "explorer.exe" }
        };

        [Fact]
        public void ExplicitProfile_WinsOverExeAssociation()
        {
            var r = LaunchProfileResolver.Resolve("bureau", "ConsoleHub.exe", Profiles);
            Assert.Equal(LaunchProfileResolver.Outcome.UseProfile, r.Outcome);
            Assert.Equal("bureau", r.ProfileId);
        }

        [Theory]
        [InlineData("")]            // automatique
        [InlineData(null)]
        [InlineData("supprime")]   // profil mémorisé puis supprimé → repli automatique
        public void Auto_UsesProfileAssociatedWithExe(string? choice)
        {
            var r = LaunchProfileResolver.Resolve(choice, "consolehub.EXE", Profiles);
            Assert.Equal(LaunchProfileResolver.Outcome.UseProfile, r.Outcome);
            Assert.Equal("jeu", r.ProfileId);
        }

        [Fact]
        public void NoProfileDefined_FallsBackToCurrentProfile()
        {
            var r = LaunchProfileResolver.Resolve("", "inconnu.exe", Profiles);
            Assert.Equal(LaunchProfileResolver.Outcome.KeepCurrent, r.Outcome);
            Assert.Null(r.ProfileId);
        }

        [Fact]
        public void KeepAndAsk_AreHonored()
        {
            Assert.Equal(LaunchProfileResolver.Outcome.KeepCurrent, LaunchProfileResolver.Resolve(LaunchProfileResolver.Keep, "game.exe", Profiles).Outcome);
            Assert.Equal(LaunchProfileResolver.Outcome.Ask, LaunchProfileResolver.Resolve(LaunchProfileResolver.Ask, "game.exe", Profiles).Outcome);
        }

        [Theory]
        [InlineData(@"C:\Jeux\ConsoleHub\ConsoleHub.exe", "ConsoleHub.exe")]
        [InlineData("\"C:\\Program Files\\OBS\\obs64.exe\"", "obs64.exe")]
        [InlineData("notepad.exe", "notepad.exe")]
        [InlineData(@"C:\Users\x\Desktop\Jeu.lnk", null)]
        [InlineData("steam://rungameid/730", null)]
        public void ExeName_IsDeducedWhenPossible(string path, string? expected)
        {
            Assert.Equal(expected, LaunchProfileResolver.ExeNameOf(path));
        }

        [Fact]
        public void LaunchAction_KeepsMemorizedProfile()
        {
            var a = MappingAction.Parse("Lancer:{\"path\":\"C:\\\\Jeux\\\\ConsoleHub.exe\",\"args\":\"\",\"profileId\":\"jeu\"}");
            Assert.Equal("jeu", a.Launch!.ProfileId);
            var legacy = MappingAction.Parse("Lancer:{\"path\":\"notepad.exe\",\"args\":\"\"}");
            Assert.Equal(LaunchProfileResolver.Auto, legacy.Launch!.ProfileId); // anciens raccourcis = automatique
        }
    }
}
