namespace ControllerManager
{
    // Journal texte simple dans %LOCALAPPDATA%\ControllerManager\logs (rotation à 1 Mo, une seule archive .old)
    public static class Log
    {
        private const long MaxBytes = 1024 * 1024;
        private static readonly object Sync = new();

        public static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerManager", "logs");

        public static string LogFilePath { get; } = Path.Combine(LogDirectory, "controllermanager.log");

        public static void Info(string message) => Write("INFO ", message, null);

        public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);

        public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception? ex)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    var file = new FileInfo(LogFilePath);
                    if (file.Exists && file.Length > MaxBytes)
                    {
                        string archive = LogFilePath + ".old";
                        File.Delete(archive);
                        file.MoveTo(archive);
                    }

                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                    if (ex != null)
                        line += $" | {ex.GetType().Name}: {ex.Message}";
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Le journal ne doit jamais faire planter l'appli
            }
        }
    }
}
