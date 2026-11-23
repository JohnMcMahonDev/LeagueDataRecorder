namespace League_Data_Recorder
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (TranscriptionAcceleration.RunProbeWorker(args) is { } probeExit)
            {
                Environment.ExitCode = probeExit;
                return;
            }
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            // Enable native dark scrollbars and system control colors before
            // the first form is created. The existing target remains .NET 9.
#pragma warning disable WFO5001 // Native dark mode is opt-in/experimental on .NET 9.
            Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
            Application.Run(new Form1());
        }
    }
}
