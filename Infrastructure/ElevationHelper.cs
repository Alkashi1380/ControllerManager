using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ControllerManager
{
    // Windows (UIPI) bloque l'injection clavier/souris d'un processus normal vers un processus administrateur
    public static class ElevationHelper
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenElevation = 20;
        private const int ERROR_ACCESS_DENIED = 5;
        private const int ERROR_CANCELLED = 1223;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

        public static bool IsCurrentProcessElevated { get; } = IsElevated((uint)Environment.ProcessId) == true;

        // true / false, ou null si impossible à déterminer (processus protégé, déjà terminé…)
        public static bool? IsElevated(uint pid)
        {
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                if (!OpenProcessToken(process, TOKEN_QUERY, out IntPtr token))
                {
                    // Le jeton d'un processus administrateur est refusé à un processus normal
                    return Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED ? true : null;
                }
                try
                {
                    return GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _)
                        ? elevated != 0
                        : null;
                }
                finally { CloseHandle(token); }
            }
            finally { CloseHandle(process); }
        }

        // Relance l'appli avec l'invite UAC ; false si l'utilisateur refuse
        public static bool RestartElevated()
        {
            try
            {
                // --wait-instance : la nouvelle instance attend que celle-ci libère le verrou d'instance unique
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--tray --wait-instance")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
            {
                return false;
            }
        }
    }
}
