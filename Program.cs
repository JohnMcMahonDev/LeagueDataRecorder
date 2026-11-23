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
            ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // Native dark mode is opt-in/experimental on .NET 9.
            Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
            using var activation = new ShareLinkActivation();
            if (!activation.IsPrimary)
            {
                try { activation.Forward(args); }
                catch (Exception ex) { MessageBox.Show("Could not open the running app: " + ex.Message); }
                return;
            }
            var form = new Form1();
            if (ShareLinkActivation.GetLink(args) is { } link) form.ReceiveShareLink(link);
            activation.Listen(form);
            Application.Run(form);
        }
    }
}
