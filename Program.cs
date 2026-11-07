namespace ControllerManager
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\ControllerManager.SingleInstance";

        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, e) => Log.Error("Exception non gérée (thread UI)", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Exception non gérée", e.ExceptionObject as Exception);

            // Deux instances doubleraient chaque action (raccourcis, souris, rapports HID)
            using var mutex = new Mutex(true, SingleInstanceMutexName, out bool ownsMutex);
            if (!ownsMutex && args.Contains("--wait-instance"))
            {
                // Relance (ex. en administrateur) : on attend que l'instance précédente se ferme
                try { ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                catch (AbandonedMutexException) { ownsMutex = true; }
            }
            if (!ownsMutex)
            {
                Log.Info("Une instance est déjà lancée : arrêt");
                MessageBox.Show("ControllerManager est déjà lancé (icône dans la barre des tâches).",
                    "ControllerManager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool startMinimized = args.Contains("--tray");
            Log.Info($"Démarrage ControllerManager {Application.ProductVersion} (tray={startMinimized}, admin={ElevationHelper.IsCurrentProcessElevated})");
            try
            {
                Application.Run(new TrayApplicationContext(startMinimized));
            }
            finally
            {
                try { mutex.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
        }
    }
}
