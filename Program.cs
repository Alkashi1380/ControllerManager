namespace ControllerManager
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, e) => Log.Error("Exception non gérée (thread UI)", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Exception non gérée", e.ExceptionObject as Exception);

            bool startMinimized = args.Contains("--tray");
            Log.Info($"Démarrage ControllerManager {Application.ProductVersion} (tray={startMinimized})");
            Application.Run(new TrayApplicationContext(startMinimized));
        }
    }
}
