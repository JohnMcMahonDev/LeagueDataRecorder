using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace League_Data_Recorder;

// A tiny, noninteractive WebView uses the existing range-streaming server. The
// browser supplies pitch-preserving rate changes and accurate WAV seeks.
internal sealed class ReplayAudioPlayer : WebView2
{
    private readonly VideoReviewMediaServer server = new();
    private readonly string audioPath;
    private readonly double audioToGame;
    private readonly Action<string> log;
    private bool ready;
    private string PageUri => server.Origin + server.AssetPrefix + "replay-audio.html";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal ReplayAudioPlayer(string audioPath, double audioToGame, Action<string> log)
    {
        this.audioPath = audioPath; this.audioToGame = audioToGame; this.log = log;
        Size = new Size(1, 1); TabStop = false; DefaultBackgroundColor = Color.Transparent;
    }

    internal async Task InitializeAsync()
    {
        try
        {
            string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League Data Recorder", "ReplayAudio", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData,
                options: new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            if (IsDisposed) return;
            await EnsureCoreWebView2Async(environment);
            if (IsDisposed) return;
            CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            CoreWebView2.Settings.AreDevToolsEnabled = false;
            CoreWebView2.Settings.AreHostObjectsAllowed = false;
            CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            CoreWebView2.NavigationStarting += (_, e) => e.Cancel = e.Uri != PageUri;
            CoreWebView2.WebMessageReceived += (_, e) =>
            {
                if (e.Source != PageUri) return;
                using var data = JsonDocument.Parse(e.WebMessageAsJson);
                if (data.RootElement.GetProperty("type").GetString() == "ready")
                {
                    ready = true;
                    var preferences = new VideoPlayerPreferences();
                    try { preferences = PreferenceFiles.Load<VideoPlayerPreferences>("player-settings.json"); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
                    preferences.Validate();
                    Send(new { type = "load", audio = server.SelectMedia(1, null, audioPath) + "audio.wav",
                        volume = preferences.Volume, muted = preferences.Muted });
                }
                else if (data.RootElement.TryGetProperty("message", out var message))
                    log("Replay comms audio: " + message.GetString());
            };
            CoreWebView2.Navigate(PageUri);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException
            or UnauthorizedAccessException or ArgumentException or WebView2RuntimeNotFoundException)
        { if (!IsDisposed) log("Replay comms audio could not start: " + ex.Message); }
    }

    internal void Sync(double? gameTime, bool paused, double speed) => Send(new
    {
        type = "clock", time = gameTime.HasValue ? gameTime - audioToGame : null,
        paused, speed,
    });

    private void Send(object data)
    {
        if (!ready || IsDisposed || CoreWebView2 is null) return;
        try { CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(data, JsonOptions)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { System.Diagnostics.Debug.WriteLine(ex.Message); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { ready = false; server.Dispose(); }
        base.Dispose(disposing);
    }
}
