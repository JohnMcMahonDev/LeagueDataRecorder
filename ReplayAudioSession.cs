using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace League_Data_Recorder;

internal sealed record ReplayAudioOptions(string AudioPath, double AudioGameOffset);

// Audio-only replay follower. There is no topmost window, drawing surface,
// speech timeline, portrait placement or mouse interception over League.
internal sealed class ReplayAudioSession : IDisposable
{
    private sealed class AudioHost : Form
    {
        internal AudioHost()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; Bounds = new(-32000, -32000, 2, 2); Opacity = 0;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; } }
    }
    private readonly AudioHost host = new();
    private readonly ReplayAudioPlayer audio;
    private readonly ReplayApiClient api = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer sync = new() { Interval = 50 };
    private readonly HashSet<int> previousProcesses;
    private readonly string gamePath;
    private readonly Action<string> log;
    private readonly double started = ReplayApiClient.Now;
    private Process? process;
    private ReplaySnapshot? snapshot;
    private bool polling, disposed;
    internal event EventHandler? Completed;

    internal ReplayAudioSession(string gamePath, ReplayAudioOptions options, IEnumerable<int> previousProcesses, Action<string> log)
    {
        this.gamePath = gamePath; this.log = log; this.previousProcesses = previousProcesses.ToHashSet();
        audio = new ReplayAudioPlayer(options.AudioPath, options.AudioGameOffset, log);
        host.Controls.Add(audio); host.Show(); _ = audio.InitializeAsync();
        poll.Tick += async (_, _) => await PollAsync();
        sync.Tick += (_, _) => Sync(); poll.Start(); sync.Start();
    }
    internal static int[] ExistingGameProcesses()
    {
        var ids = new List<int>();
        foreach (var p in Process.GetProcessesByName("League of Legends")) { using (p) ids.Add(p.Id); }
        return ids.ToArray();
    }
    internal bool TryFocus(string path)
    {
        if (disposed || !string.Equals(path, gamePath, StringComparison.OrdinalIgnoreCase) || process is null) return false;
        try
        {
            if (process.HasExited) return false;
            process.Refresh(); var window = process.MainWindowHandle;
            if (window == IntPtr.Zero) return false;
            if (IsIconic(window)) ShowWindow(window, 9);
            SetForegroundWindow(window);
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }
    private async Task PollAsync()
    {
        if (polling || disposed) return; polling = true;
        try
        {
            var value = await api.ReadAsync(lifetime.Token);
            if (disposed) return;
            if (process is null)
            {
                if (previousProcesses.Contains(value.ProcessId)) return;
                var candidate = Process.GetProcessById(value.ProcessId);
                if (!candidate.ProcessName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase)) { candidate.Dispose(); return; }
                process = candidate; log("Replay comms audio connected.");
            }
            if (process.Id != value.ProcessId || process.HasExited) { Dispose(); return; }
            snapshot = value;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { if (!disposed) { snapshot = null; audio.Sync(null, true, 1); } }
        finally { polling = false; }
    }
    private void Sync()
    {
        if (disposed) return;
        if (process is null)
        {
            if (ReplayApiClient.Now - started > 120) { log("Replay audio could not connect to the newly opened replay."); Dispose(); }
            return;
        }
        try
        {
            if (process.HasExited) { Dispose(); return; }
            var value = snapshot;
            double? time = value?.Playback.TimeAt(value.SampleClock, ReplayApiClient.Now);
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
            if (foreground != process.Id) time = null;
            audio.Sync(time, value?.Playback.Paused ?? true, value?.Playback.Speed ?? 1);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { audio.Sync(null, true, 1); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        audio.Sync(null, true, 1); lifetime.Cancel(); poll.Dispose(); sync.Dispose(); api.Dispose();
        host.Dispose(); process?.Dispose(); lifetime.Dispose(); Completed?.Invoke(this, EventArgs.Empty);
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
}
