using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace League_Data_Recorder;

// An ordinary desktop window above the replay. It never injects into League or
// intercepts playback controls, and never needs the recorder to have focus.
internal sealed class ReplayOverlayWindow : Form
{
    private readonly ReplayOverlayTimeline timeline;
    private readonly Dictionary<string, int> speakerLanes;
    private readonly ReplayAudioPlayer? audio;
    private readonly Action<string> log;
    private readonly HashSet<int> previousProcesses;
    private readonly ReplayApiClient api = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer pollTimer = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer drawTimer = new() { Interval = 50 };
    private readonly double startedAt = ReplayApiClient.Now;
    private readonly Dictionary<string, Image?> portraits = new(StringComparer.Ordinal);
    private readonly List<(ReplayBubble Bubble, Rectangle Bounds)> cards = [];
    private Process? replayProcess;
    private ReplaySnapshot? snapshot;
    private IntPtr replayHandle;
    private bool polling, stopped, disconnected;
    private Font? labelFont, speechFont;
    private float drawScale;
    private string cardKey = "";
    private readonly string gamePath;
    private static readonly Color TransparentColor = Color.FromArgb(255, 0, 255);

    internal ReplayOverlayWindow(string gamePath, ReplayOverlayTimeline timeline, IEnumerable<int> previousProcesses, Action<string> log)
    {
        this.gamePath = gamePath;
        this.timeline = timeline;
        speakerLanes = timeline.People.Select((p, i) => (p.Speaker, i)).ToDictionary(p => p.Speaker, p => p.i, StringComparer.Ordinal);
        this.previousProcesses = previousProcesses.ToHashSet();
        this.log = log;
        Text = "League replay comms";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = TransparencyKey = TransparentColor;
        TopMost = true;
        DoubleBuffered = true;
        if (timeline.AudioPath is { } audioPath && File.Exists(audioPath))
        {
            audio = new ReplayAudioPlayer(audioPath, timeline.AudioGameOffset, log);
            Controls.Add(audio);
            Shown += async (_, _) => await audio.InitializeAsync();
        }
        pollTimer.Tick += async (_, _) => await PollAsync();
        drawTimer.Tick += (_, _) => RefreshBubbles();
        pollTimer.Start();
        drawTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var value = base.CreateParams;
            // Layered + transparent lets clicks pass through to League. Tool
            // window + no-activate keeps it out of Alt-Tab and preserves focus.
            value.ExStyle |= 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000;
            return value;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
        if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }

    internal bool TryFocus(string path)
    {
        if (stopped || !string.Equals(path, gamePath, StringComparison.OrdinalIgnoreCase) || replayProcess is null) return false;
        try
        {
            if (replayProcess.HasExited) return false;
            replayProcess.Refresh();
            IntPtr window = replayHandle != IntPtr.Zero ? replayHandle : replayProcess.MainWindowHandle;
            if (window == IntPtr.Zero) return false;
            if (IsIconic(window)) ShowWindow(window, 9); // restore only on an explicit Open Replay click
            SetForegroundWindow(window);
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    internal static int[] ExistingGameProcesses()
    {
        var ids = new List<int>();
        foreach (var process in Process.GetProcessesByName("League of Legends"))
        {
            using (process) ids.Add(process.Id);
        }
        return ids.ToArray();
    }

    private async Task PollAsync()
    {
        if (polling || stopped) return;
        polling = true;
        try
        {
            var sample = await api.ReadAsync(lifetime.Token);
            if (stopped) return;
            if (replayProcess is null)
            {
                // The replay was launched for this recording by Open Replay.
                // Never attach its transcript to a game that was already running.
                if (previousProcesses.Contains(sample.ProcessId)) return;
                var process = Process.GetProcessById(sample.ProcessId);
                if (!process.ProcessName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase))
                { process.Dispose(); return; }
                replayProcess = process;
                log("Replay comms overlay connected. Bubbles follow League's replay clock.");
            }
            if (replayProcess.Id != sample.ProcessId || replayProcess.HasExited)
            { Close(); return; }
            if (disconnected) log("Replay comms overlay reconnected.");
            disconnected = false;
            snapshot = sample;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or
            IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (stopped) return;
            snapshot = null;
            audio?.Sync(null, true, 1);
            Hide();
            if (replayProcess is not null && !disconnected)
            {
                disconnected = true;
                log("Replay clock temporarily unavailable; bubbles are hidden until it reconnects.");
            }
        }
        finally { polling = false; }
    }

    private void RefreshBubbles()
    {
        if (stopped) return;
        if (replayProcess is null)
        {
            if (ReplayApiClient.Now - startedAt > 120)
            {
                log("Replay overlay could not connect to a newly opened replay. Close the replay and use Open Replay again. " +
                    "The Replay API flag is enabled automatically before launch.");
                Close();
            }
            return;
        }
        try
        {
            if (replayProcess.HasExited) { Close(); return; }
            var sample = snapshot;
            double? time = sample?.Playback.TimeAt(sample.SampleClock, ReplayApiClient.Now);
            if (time is null || !TryGetReplayBounds(replayProcess.Id, out Rectangle viewport))
            { audio?.Sync(null, true, 1); Hide(); return; }
            audio?.Sync(time, sample!.Playback.Paused, sample.Playback.Speed);
            var bubbles = timeline.At(time.Value);
            float scale = Math.Clamp(viewport.Height / 1080f, .55f, 1.8f);
            string key = string.Join("\n", bubbles.Select(b => b.Key + ":" + b.Text));
            if (Math.Abs(scale - drawScale) > .001f || key != cardKey || Size != viewport.Size)
            {
                SetFonts(scale); cardKey = key; cards.Clear(); Size = viewport.Size;
                foreach (var bubble in bubbles)
                {
                    string person = bubble.Person.Length > 0 ? bubble.Person : bubble.Label;
                    if (!speakerLanes.ContainsKey(person)) speakerLanes[person] = speakerLanes.Count;
                }
                foreach (var bubble in bubbles)
                {
                    string person = bubble.Person.Length > 0 ? bubble.Person : bubble.Label;
                    cards.Add((bubble, ReplayCommsDock.Card(speakerLanes[person], speakerLanes.Count, viewport.Size)));
                }
                Invalidate();
            }
            Location = viewport.Location;
            if (!Visible) Show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { if (!stopped) { audio?.Sync(null, true, 1); Hide(); } }
    }

    private bool TryGetReplayBounds(int processId, out Rectangle result)
    {
        result = default;
        IntPtr foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint foregroundId);
        if (foregroundId != processId) return false;
        IntPtr window = GetAncestor(foreground, 2); // root window in this replay process
        if (window == IntPtr.Zero || IsIconic(window) || !GetClientRect(window, out NativeRect rect)) return false;
        var origin = new NativePoint();
        if (!ClientToScreen(window, ref origin) || rect.Right <= 0 || rect.Bottom <= 0) return false;
        replayHandle = window;
        result = new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom);
        return true;
    }

    private void SetFonts(float scale)
    {
        if (labelFont is not null && Math.Abs(scale - drawScale) < .001f) return;
        labelFont?.Dispose(); speechFont?.Dispose();
        drawScale = scale;
        labelFont = new Font("Segoe UI", 13.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        speechFont = new Font("Segoe UI", 17 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (labelFont is null || speechFont is null) return;
        int padding = (int)(12 * drawScale), iconSize = (int)(40 * drawScale);
        foreach (var (bubble, rect) in cards)
        {
            using var background = new SolidBrush(bubble.IsCall ? Color.FromArgb(19, 43, 39) : Color.FromArgb(18, 25, 35));
            using var accent = new SolidBrush(bubble.IsCall ? Color.FromArgb(69, 213, 171) : RoleColor(bubble.Role));
            using var border = new Pen(Color.FromArgb(61, 75, 92));
            e.Graphics.FillRectangle(background, rect);
            e.Graphics.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            e.Graphics.FillRectangle(accent, rect.X, rect.Y, Math.Max(3, (int)(3 * drawScale)), rect.Height);
            var portrait = new Rectangle(rect.Left + padding, rect.Top + padding, iconSize, iconSize);
            if (bubble.Participants is { Count: >= 2 } people)
            {
                int size = (int)(iconSize * .7);
                DrawPortrait(e.Graphics, people[0].Icon, people[0].Role, new Rectangle(portrait.X, portrait.Y, size, size));
                DrawPortrait(e.Graphics, people[1].Icon, people[1].Role,
                    new Rectangle(portrait.Right - size, portrait.Bottom - size, size, size));
            }
            else DrawPortrait(e.Graphics, bubble.Icon, bubble.Role, portrait);
            int left = portrait.Right + padding;
            string label = (bubble.IsCall ? "CALL · " : "") + bubble.Label +
                (bubble.Champion.Length > 0 ? " · " + bubble.Champion : "");
            TextRenderer.DrawText(e.Graphics, label, labelFont, new Rectangle(left, rect.Top + padding,
                rect.Right - left - padding, (int)(21 * drawScale)), ((SolidBrush)accent).Color,
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, bubble.Text, speechFont, new Rectangle(left, rect.Top + (int)(28 * drawScale),
                rect.Right - left - padding, rect.Height - (int)(31 * drawScale)), Color.FromArgb(238, 243, 249),
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    private void DrawPortrait(Graphics graphics, string? data, string role, Rectangle rect)
    {
        Image? image = null;
        if (data is not null && !portraits.TryGetValue(data, out image))
        {
            try
            {
                if (data.StartsWith("data:image/png;base64,", StringComparison.Ordinal) && data.Length < 100_000)
                {
                    using var stream = new MemoryStream(Convert.FromBase64String(data[22..]));
                    using var source = Image.FromStream(stream);
                    image = new Bitmap(source);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or ExternalException) { }
            portraits[data] = image;
        }
        if (image is not null) graphics.DrawImage(image, rect);
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(38, 49, 65));
            graphics.FillRectangle(fill, rect);
            string initials = role switch { "jungle" => "JG", "support" => "SUP", "adc" => "AD",
                "top" => "TOP", "mid" => "MID", "coach" => "C", "overlap" => "2+", _ => "?" };
            TextRenderer.DrawText(graphics, initials, labelFont, rect, RoleColor(role),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private static Color RoleColor(string role) => role switch
    {
        "top" => Color.FromArgb(246, 173, 85), "jungle" => Color.FromArgb(104, 211, 145),
        "mid" => Color.FromArgb(183, 148, 244), "adc" => Color.FromArgb(99, 179, 237),
        "support" => Color.FromArgb(246, 135, 179), _ => Color.FromArgb(181, 196, 214),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing && !stopped)
        {
            stopped = true;
            lifetime.Cancel();
            audio?.Dispose();
            pollTimer.Dispose(); drawTimer.Dispose(); api.Dispose(); replayProcess?.Dispose();
            labelFont?.Dispose(); speechFont?.Dispose();
            foreach (var image in portraits.Values) image?.Dispose();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
}
