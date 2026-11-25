using System.Runtime.CompilerServices;

namespace League_Data_Recorder
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                AppDiagnostics.Initialize();
                Run(args);
            }
            catch (Exception ex)
            {
                AppDiagnostics.ReportFatal("Startup or application message loop", ex);
                Environment.ExitCode = 1;
            }
        }

        // Keep application/dependency loading inside Main's exception boundary.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Run(string[] args)
        {
            if (TranscriptionAcceleration.RunProbeWorker(args) is { } probeExit)
            {
                AppDiagnostics.Record("Acceleration probe finished; exit code: " + probeExit);
                Environment.ExitCode = probeExit;
                return;
            }

            AppDiagnostics.Record("Initializing Windows Forms");
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
#pragma warning disable WFO5001 // Native dark mode is opt-in/experimental on .NET 9.
            Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001

            AppDiagnostics.Record("Checking for an existing application instance");
            using var activation = new ShareLinkActivation();
            if (!activation.IsPrimary)
            {
                try
                {
                    activation.Forward(args);
                    AppDiagnostics.Record("Activated existing instance; this instance is exiting");
                }
                catch (Exception ex)
                {
                    AppDiagnostics.ReportFatal("Activating the running application", ex);
                    Environment.ExitCode = 1;
                }
                return;
            }

            AppDiagnostics.Record("Constructing main window");
            using var form = new Form1();
            form.Load += (_, _) => AppDiagnostics.Record("Main window Load event reached");
            form.Shown += (_, _) => AppDiagnostics.Record("Main window Shown event reached");
            if (ShareLinkActivation.GetLink(args) is { } link) form.ReceiveShareLink(link);
            activation.Listen(form);
            AppDiagnostics.Record("Starting application message loop");
            Application.Run(form);
            AppDiagnostics.Record("Application message loop ended normally");
        }
    }
}
