using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace League_Data_Recorder;

internal static class AppDiagnostics
{
    private static readonly object FileGate = new();
    private static string? sessionPath;
    private static int initialized;
    private static int reportingFatal;

    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0) return;

        // Install handlers before constructing controls or loading application services.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportFatal("Unhandled background-thread exception", e.ExceptionObject as Exception
                ?? new InvalidOperationException("An unhandled non-Exception object was thrown."));
        Application.ThreadException += (_, e) =>
        {
            ReportFatal("Unhandled Windows Forms exception", e.Exception);
            // Continuing after an unhandled UI exception can corrupt application state.
            Environment.Exit(1);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
            RecordException("Unobserved task exception (application may continue)", e.Exception);

        sessionPath = CreateReport("startup", Header());
        Record("Diagnostic handlers installed");
    }

    internal static void Record(string message)
    {
        try
        {
            if (sessionPath is null) return;
            lock (FileGate)
                File.AppendAllText(sessionPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch (Exception) { /* Reporting must never prevent the application from running. */ }
    }

    private static void RecordException(string context, Exception exception)
    {
        try { Record(context + Environment.NewLine + exception); }
        catch (Exception) { }
    }

    internal static void ReportFatal(string context, Exception exception)
    {
        if (Interlocked.Exchange(ref reportingFatal, 1) != 0) return;
        try
        {
            // A separate file makes the exception easy to find among startup reports.
            string details = Header() + Environment.NewLine + "Failure: " + context
                + Environment.NewLine + "Startup log: " + (sessionPath ?? "Unavailable")
                + Environment.NewLine + Environment.NewLine + exception;
            string? crashPath = CreateReport("crash", details);
            RecordException(context, exception);
            string? reportPath = crashPath ?? sessionPath;
            string location = reportPath is null
                ? "The error report could not be saved. Check Windows Event Viewer → Windows Logs → Application."
                : "Please send this report to the app developer:" + Environment.NewLine + reportPath;
            string message = "League Data Recorder encountered an error and must close."
                + Environment.NewLine + Environment.NewLine + exception.GetType().Name + ": " + exception.Message
                + Environment.NewLine + Environment.NewLine + location;
            if (reportPath is not null)
                message += Environment.NewLine + Environment.NewLine + "Open the report folder?";

            // Use the native dialog even when WinForms initialization itself failed.
            int result = MessageBoxW(IntPtr.Zero, message, "League Data Recorder — Error report",
                0x00000010u | 0x00010000u | (reportPath is null ? 0u : 0x00000004u));
            if (result == 6 && reportPath is not null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + reportPath + "\"")
                    { UseShellExecute = true });
                }
                catch (Exception) { }
            }
        }
        catch (Exception)
        {
            // No recursive exception dialogs if the reporter or desktop is unavailable.
        }
    }

    private static string Header()
    {
        try
        {
            var assembly = typeof(AppDiagnostics).Assembly;
            return "League Data Recorder diagnostic report" + Environment.NewLine
                + "Time: " + DateTimeOffset.Now.ToString("O") + Environment.NewLine
                + "Version: " + (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? assembly.GetName().Version?.ToString() ?? "Unknown") + Environment.NewLine
                + "Runtime: " + RuntimeInformation.FrameworkDescription + Environment.NewLine
                + "OS: " + RuntimeInformation.OSDescription + Environment.NewLine
                + "OS architecture: " + RuntimeInformation.OSArchitecture + Environment.NewLine
                + "Process architecture: " + RuntimeInformation.ProcessArchitecture + Environment.NewLine
                + "Process ID: " + Environment.ProcessId + Environment.NewLine
                + "Application folder: " + AppContext.BaseDirectory + Environment.NewLine
                + "Interactive session: " + Environment.UserInteractive + Environment.NewLine;
        }
        catch (Exception) { return "League Data Recorder diagnostic report" + Environment.NewLine; }
    }

    private static string? CreateReport(string category, string contents)
    {
        // Do not rely on write permission to the application installation folder.
        foreach (bool useTemp in new[] { false, true })
        {
            try
            {
                string root = useTemp ? Path.GetTempPath()
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(root)) continue;
                string directory = Path.Combine(root, "League Data Recorder", "Logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory,
                    $"{category}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
                File.WriteAllText(path, contents, Encoding.UTF8);
                if (category == "startup") TrimStartupReports(directory);
                return path;
            }
            catch (Exception) { }
        }
        return null;
    }

    private static void TrimStartupReports(string directory)
    {
        try
        {
            // Only expire old startup traces; preserve crash and launcher reports.
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("startup-*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(30))
            {
                if (file.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-7)) continue;
                try { file.Delete(); } catch (Exception) { }
            }
        }
        catch (Exception) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
