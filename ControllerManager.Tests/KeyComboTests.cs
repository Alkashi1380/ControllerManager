using ControllerManager;
using Xunit;

namespace ControllerManager.Tests
{
    public class KeyComboTests
    {
        [Theory]
        [InlineData("Ctrl+Shift+S", new byte[] { 0x11, 0x10 }, 0x53)]
        [InlineData("ctrl+shift+s", new byte[] { 0x11, 0x10 }, 0x53)]
        [InlineData("F5", new byte[0], 0x74)]
        [InlineData("F24", new byte[0], 0x87)]
        [InlineData("Alt + Tab", new byte[] { 0x12 }, 0x09)]
        [InlineData("Win+E", new byte[] { 0x5B }, 0x45)]
        [InlineData("Ctrl+Alt+Suppr", new byte[] { 0x11, 0x12 }, 0x2E)]
        [InlineData("Maj+Entrée", new byte[] { 0x10 }, 0x0D)]
        [InlineData("ctrl+ctrl+z", new byte[] { 0x11 }, 0x5A)] // modificateur dupliqué ignoré
        [InlineData("7", new byte[0], 0x37)]
        [InlineData("Shift", new byte[0], 0x10)] // modificateur seul = la touche elle-même (ex. maintenir Shift)
        [InlineData("Ctrl", new byte[0], 0x11)]
        public void Parses_ValidCombos(string text, byte[] modifiers, byte key)
        {
            Assert.True(KeyCombo.TryParse(text, out var combo));
            Assert.NotNull(combo);
            Assert.Equal(modifiers, combo!.Modifiers);
            Assert.Equal(key, combo.Key);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("Ctrl+Shift")] // plusieurs modificateurs sans touche
        [InlineData("Ctrl+")]     // morceau vide
        [InlineData("A+B")]       // deux touches
        [InlineData("F25")]
        [InlineData("F0")]
        [InlineData("toto")]
        [InlineData("Ctrl+é")]
        public void Rejects_InvalidCombos(string? text)
        {
            Assert.False(KeyCombo.TryParse(text, out var combo));
            Assert.Null(combo);
        }

        [Theory]
        [InlineData(0x25, true)]  // flèche gauche
        [InlineData(0x2E, true)]  // Suppr
        [InlineData(0x5B, true)]  // Win
        [InlineData(0x41, false)] // A
        [InlineData(0x74, false)] // F5
        public void ExtendedKeys_AreFlagged(byte vk, bool extended)
        {
            Assert.Equal(extended, KeyCombo.IsExtended(vk));
        }
    }
}
