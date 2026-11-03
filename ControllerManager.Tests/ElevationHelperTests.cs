using System.Diagnostics;
using System.Security.Principal;
using ControllerManager;
using Xunit;

namespace ControllerManager.Tests
{
    public class ElevationHelperTests
    {
        [Fact]
        public void CurrentProcess_MatchesWindowsAdministratorRole()
        {
            // Avec l'UAC, le rôle Administrateur n'est présent que dans un jeton élevé
            bool expected = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            Assert.Equal(expected, ElevationHelper.IsCurrentProcessElevated);
            Assert.Equal(expected, ElevationHelper.IsElevated((uint)Environment.ProcessId));
        }

        [Fact]
        public void ChildProcessOfSameUser_HasSameElevation()
        {
            // ping plutôt que timeout : timeout échoue sans console interactive (agents CI)
            using var child = Process.Start(new ProcessStartInfo("ping.exe", "-n 6 127.0.0.1")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            })!;
            try
            {
                Assert.Equal(ElevationHelper.IsCurrentProcessElevated, ElevationHelper.IsElevated((uint)child.Id));
            }
            finally
            {
                child.Kill(entireProcessTree: true);
            }
        }

        [Fact]
        public void UnknownProcess_ReturnsNull()
        {
            Assert.Null(ElevationHelper.IsElevated(0xFFFFFFF0));
        }
    }
}
