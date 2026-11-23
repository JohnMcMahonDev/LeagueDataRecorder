using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace League_Data_Recorder;

internal sealed class VideoReviewControl : UserControl
{
    private VideoReviewMediaServer? mediaServer;
    private readonly LoadingOverlay loading;
    private string PlayerUri => mediaServer?.PageUri ?? "about:blank";
    private string? mediaPrefix;
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(15, 19, 26) };
    private readonly Label message = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = AppTheme.Muted, BackColor = Color.FromArgb(15, 19, 26), Padding = new Padding(24) };
    private readonly FlowLayoutPanel recovery = new() { Dock = DockStyle.Bottom, Height = 44,
        FlowDirection = FlowDirection.LeftToRight, BackColor = AppTheme.Surface, Padding = new Padding(8, 4, 8, 4), Visible = false };
    private Task? initialization;
    private VideoReviewMedia? media;
    private IReadOnlyList<VideoCommsCue> cues = [];
    private IReadOnlyList<VideoObjectiveCue> objectiveCalls = [];
    private VideoFightTimeline fightTimeline = new([], "");
    private VideoObjectiveEventsTimeline objectiveEvents = new([], "");
    private VideoGoldTimeline gold = new([], "");
    private VideoCommsTone tone = new([], "Select Transcribe to add Comms Tone.");
    private string? gamePath;
    private string captionStatus = "Select a recording to see its comms.";
    private string placeholder = "Select a recording to play its video.";
    private int revision;
    private bool pageReady;
    private bool fullScreen;
    private bool movingHost;
    private VideoPlayerPreferences playerPreferences = new();
    private double transcriptTimelineShift;
    private double? pendingSeek;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal event EventHandler? FullScreenRequested;
    internal event Action<string>? Log;
    internal bool HasVideo => media?.VideoPath is not null;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<string, string>? ChampionDisplayName { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<string, string?>? ChampionPortrait { get; set; }

    internal VideoReviewControl()
    {
        Dock = DockStyle.Fill;
        BackColor = message.BackColor;
        AccessibleName = "Recorded video with synchronized voice comms";
        Controls.Add(browser);
        Controls.Add(message);
        Controls.Add(recovery);
        message.Text = placeholder;
        loading = LoadingOverlay.Attach(this);
        var install = new LinkLabel { Text = "Install WebView2 Runtime", AutoSize = true, Margin = new Padding(4, 5, 14, 4),
            LinkColor = AppTheme.Accent };
        install.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(
            "https://developer.microsoft.com/microsoft-edge/webview2/#download-section") { UseShellExecute = true });
        var retry = new Button { Text = "Retry player", AutoSize = true };
        retry.Click += async (_, _) =>
        {
            initialization = null;
            await EnsurePlayerAsync();
            PublishMedia();
        };
        recovery.Controls.Add(install);
        recovery.Controls.Add(retry);
        try { playerPreferences = PreferenceFiles.Load<VideoPlayerPreferences>("player-settings.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Debug.WriteLine(ex.Message); }
        playerPreferences.Validate();
        try { PreferenceFiles.Save("player-settings.json", playerPreferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Debug.WriteLine("Player preferences could not be saved: " + ex.Message); }
        VisibleChanged += (_, _) => { if (!Visible && !movingHost) Pause(); };
    }

    internal async Task LoadRecordingAsync(string path)
    {
        Clear("Loading video…");
        loading.SetBusy(true, "Loading video preview…");
        gamePath = path;
        captionStatus = "Loading comms…";
        int request = revision;
        try
        {
            var read = Task.Run(() =>
            {
                var recording = VideoReviewMedia.Read(path);
                var fights = recording.VideoPath is null ? new VideoFightTimeline([], "") : VideoFightTimeline.Read(path);
                var objectives = recording.VideoPath is null ? new VideoObjectiveEventsTimeline([], "") : VideoObjectiveEventsTimeline.Read(path);
                return (Media: recording, Fights: fights, Objectives: objectives, Gold: VideoGoldTimeline.Read(path));
            });
            await EnsurePlayerAsync();
            var result = await read;
            if (IsDisposed || Disposing || request != revision) return;
            var loaded = result.Media;
            media = loaded;
            fightTimeline = result.Fights;
            objectiveEvents = result.Objectives;
            gold = result.Gold;
            Debug.WriteLine($"Video review timing: capture offset {loaded.AudioOffset:F3}s; " +
                $"voice correction {loaded.ReviewTiming.AudioCorrectionSeconds:F3}s; " +
                $"WAV shift {loaded.ReviewTiming.AudioTimelineShiftSeconds:F3}s. {loaded.TimingNote}");
            PublishMedia();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (request != revision || IsDisposed) return;
            message.Text = "Could not open this recording.\n" + ex.Message;
            message.Visible = true;
            message.BringToFront();
            Log?.Invoke("Video review: " + ex.Message);
        }
        finally
        {
            if (!IsDisposed && !Disposing && request == revision) loading.SetBusy(false);
        }
    }

    internal void Clear(string text)
    {
        loading.SetBusy(false);
        revision++;
        gamePath = null;
        media = null;
        cues = [];
        objectiveCalls = [];
        fightTimeline = new([], "");
        objectiveEvents = new([], "");
        gold = new([], "");
        tone = new([], "Select Transcribe to add Comms Tone.");
        transcriptTimelineShift = 0;
        pendingSeek = null;
        placeholder = text;
        captionStatus = "";
        Post(new { type = "clear", message = text });
        mediaPrefix = mediaServer?.SelectMedia(revision, null, null);
        if (!pageReady)
        {
            message.Text = text;
            message.Visible = true;
            message.BringToFront();
        }
    }

    internal void SetComms(string path, IReadOnlyList<VideoCommsCue> value, string status, double audioTimelineShift = 0,
        IReadOnlyList<VideoObjectiveCue>? calls = null, VideoCommsTone? commsTone = null)
    {
        if (!string.Equals(gamePath, path, StringComparison.OrdinalIgnoreCase) || IsDisposed) return;
        cues = value;
        objectiveCalls = calls ?? [];
        tone = commsTone ?? new([], status.Length > 0 ? status : "Select Transcribe to add Comms Tone.");
        transcriptTimelineShift = audioTimelineShift;
        captionStatus = status;
        if (media is not null) Post(new { type = "captions", cues, objectiveCalls, captionStatus, tone,
            toneAudioShift = media.ReviewTiming.AudioTimelineShiftSeconds - tone.AudioTimelineShiftSeconds,
            captionAudioShift = media.ReviewTiming.AudioTimelineShiftSeconds - transcriptTimelineShift });
    }

    internal void SeekToComms(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return;
        pendingSeek = seconds;
        if (pageReady && media is not null)
        {
            Post(new { type = "seek", seconds });
            pendingSeek = null;
        }
    }

    internal void Pause() => Post(new { type = "pause" });
    internal async Task RefreshGoldAsync(string path)
    {
        var value = await Task.Run(() => VideoGoldTimeline.Read(path));
        if (IsDisposed || Disposing || !string.Equals(gamePath, path, StringComparison.OrdinalIgnoreCase)) return;
        gold = value;
        if (media is not null) Post(new { type = "gold", gold });
    }
    internal void RefreshFightPortraits()
    {
        if (media is not null) Post(new { type = "fights", fights = FightsForPlayer(), fightStatus = fightTimeline.Status });
    }

    private IReadOnlyList<VideoFightCue> FightsForPlayer()
    {
        // Called on the UI thread; the form owns the offline icon dictionaries.
        // Refreshing icons only republishes event data, never reloads the media.
        VideoFightChampion Portrait(VideoFightChampion person)
        {
            string lookup = person.Champion.Length > 0 ? person.Champion : person.ChampionId > 0
                ? "Champion " + person.ChampionId : "Unknown champion";
            string display = ChampionDisplayName?.Invoke(lookup) ?? lookup;
            string? icon = ChampionPortrait?.Invoke(lookup);
            if (icon is null && person.ChampionId > 0)
            {
                string byId = "Champion " + person.ChampionId;
                icon = ChampionPortrait?.Invoke(byId);
                string name = ChampionDisplayName?.Invoke(byId) ?? byId;
                if (name != byId) display = name;
            }
            return person with { Champion = display, Icon = icon };
        }
        return fightTimeline.Fights.Select(f => f with { Champions = f.Champions.Select(Portrait).ToArray() }).ToArray();
    }

    internal async Task BeginHostMoveAsync()
    {
        movingHost = true;
        if (!pageReady || browser.CoreWebView2 is null) return;
        try { await browser.CoreWebView2.ExecuteScriptAsync("window.leagueReviewBeginMove?.()"); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { Debug.WriteLine(ex.Message); }
    }

    internal void EndHostMove()
    {
        movingHost = false;
        Post(new { type = "hostMoved" });
    }

    internal void SetFullScreen(bool value)
    {
        fullScreen = value;
        Post(new { type = "fullscreen", value });
    }

    private async Task EnsurePlayerAsync()
    {
        if (IsDisposed || Disposing) return;
        initialization ??= InitializePlayerAsync();
        await initialization;
    }

    private async Task InitializePlayerAsync()
    {
        try
        {
            recovery.Visible = false;
            string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League Data Recorder", "VideoReview", "WebView2");
            // Reuse the last loopback port so the player's localStorage preferences
            // (playback speed, caption settings, per-game offsets) survive restarts.
            string portPath = Path.Combine(Path.GetDirectoryName(userData)!, "player-port.txt");
            if (mediaServer is null)
            {
                int preferredPort = 0;
                try { int.TryParse(File.ReadAllText(portPath), out preferredPort); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Debug.WriteLine(ex.Message); }
                mediaServer = new VideoReviewMediaServer(preferredPort);
                Directory.CreateDirectory(Path.GetDirectoryName(portPath)!);
                File.WriteAllText(portPath, new Uri(mediaServer.Origin).Port.ToString(CultureInfo.InvariantCulture));
            }
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            if (IsDisposed || Disposing) return;
            await browser.EnsureCoreWebView2Async(environment);
            if (IsDisposed || Disposing) return;
            var core = browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) => e.Cancel = e.Uri != PlayerUri;
            // Assets and byte ranges are served on background I/O tasks. No large
            // media response crosses the WinForms WebResourceRequested handler.
            core.WebMessageReceived += MessageReceived;
            core.ProcessFailed += (_, e) =>
            {
                if (e.ProcessFailedKind != CoreWebView2ProcessFailedKind.BrowserProcessExited &&
                    e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessExited) return;
                pageReady = false;
                message.Text = "The video player stopped. Close and reopen the app to restart it.";
                message.Visible = true;
                message.BringToFront();
                mediaPrefix = mediaServer?.SelectMedia(revision, null, null);
            };
            core.Navigate(PlayerUri);
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing) return;
            pageReady = false;
            message.Text = "Could not start video review.\n"
                + "If WebView2 Runtime is missing, install it using the link below, then select Retry player.\n\n" + ex.Message;
            message.Visible = true;
            message.BringToFront();
            recovery.Visible = true;
            recovery.BringToFront();
            Log?.Invoke("Could not initialize video review: " + ex.Message);
        }
    }

    private void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.Source != PlayerUri || IsDisposed || Disposing) return;
        try
        {
            using var json = JsonDocument.Parse(e.WebMessageAsJson);
            string? type = json.RootElement.GetProperty("type").GetString();
            if (type == "ready")
            {
                pageReady = true;
                message.Visible = false;
                recovery.Visible = false;
                Post(new { type = "preferences", preferences = playerPreferences });
                PublishMedia();
            }
            else if (type == "playerPreferences")
            {
                playerPreferences = json.RootElement.GetProperty("preferences").Deserialize<VideoPlayerPreferences>(JsonOptions) ?? new();
                playerPreferences.Validate();
                try { PreferenceFiles.Save("player-settings.json", playerPreferences); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Log?.Invoke("Player preferences could not be saved: " + ex.Message); }
            }
            else if (type == "fullscreen") FullScreenRequested?.Invoke(this, EventArgs.Empty);
            else if (type == "error" && json.RootElement.TryGetProperty("message", out var detail))
                Log?.Invoke("Video review: " + detail.GetString());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { Debug.WriteLine(ex.Message); }
    }

    private void PublishMedia(double? videoSeek = null)
    {
        if (!pageReady) return;
        if (media is null) { Post(new { type = "clear", message = placeholder }); return; }
        mediaPrefix = mediaServer!.SelectMedia(revision, media.VideoPath, media.AudioPath);
        Post(new
        {
            type = "load", title = Path.GetFileName(media.GamePath).Replace('_', ' '),
            video = media.VideoPath is null ? null : mediaPrefix + "video.mp4",
            audio = media.AudioPath is null ? null : mediaPrefix + "audio.wav",
            media.AudioOffset, media.TimingNote, media.StorageKey, cues, objectiveCalls, captionStatus, fullScreen,
            fights = FightsForPlayer(), fightStatus = fightTimeline.Status,
            objectiveEvents = objectiveEvents.Events, objectiveEventStatus = objectiveEvents.Status,
            gold,
            tone, toneAudioShift = media.ReviewTiming.AudioTimelineShiftSeconds - tone.AudioTimelineShiftSeconds,
            seek = pendingSeek, videoSeek,
            audioCorrection = media.ReviewTiming.AudioCorrectionSeconds,
            captionCorrection = media.ReviewTiming.CaptionCorrectionSeconds,
            captionAudioShift = media.ReviewTiming.AudioTimelineShiftSeconds - transcriptTimelineShift,
        });
        pendingSeek = null;
    }

    private void Post(object value)
    {
        if (!pageReady || IsDisposed || Disposing || browser.CoreWebView2 is null) return;
        try { browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, JsonOptions)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { Debug.WriteLine("Video review message failed: " + ex.Message); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            revision++;
            pageReady = false;
            browser.Dispose();
            mediaServer?.Dispose();
            mediaServer = null;
        }
        base.Dispose(disposing);
    }
}
