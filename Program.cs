namespace ControllerManager
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            bool startMinimized = args.Contains("--tray");
            Application.Run(new TrayApplicationContext(startMinimized));
        }
    }
}
