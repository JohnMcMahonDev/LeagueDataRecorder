using System.Drawing.Imaging;
using System.Text.Json;

namespace League_Data_Recorder;

public partial class Form1
{
    private VideoReviewControl? videoReview;
    private LoadingOverlay? commsLoading;
    private CancellationTokenSource? matchHistoryLoad;
    private Form? videoReviewWindow;
    private bool movingVideoWindow;
    private readonly Dictionary<string, string?> videoPortraits = new(StringComparer.OrdinalIgnoreCase);
    private List<double> commsRowStarts = new();

    private void InitializeVideoReview()
    {
        InitializeCommsShareDisplay();
        commsLoading = LoadingOverlay.Attach(commsPanel);
        // Create the browser only at runtime; Visual Studio's designer stays a
        // plain WinForms layout and does not start a browser or require media.
        thumbnailPictureBox.Visible = false;
        previewPlaceholder.Visible = false;
        videoReview = new VideoReviewControl
        {
            ChampionDisplayName = AnalysisChampionDisplayName,
            ChampionPortrait = VideoPortrait,
        };
        videoReview.Log += AppendToLog;
        videoReview.FullScreenRequested += async (_, _) => await ToggleVideoReviewWindowAsync();
        previewPanel.Controls.Add(videoReview);
        videoReview.BringToFront();
        historyToolTip.SetToolTip(commsGrid, "Click a timestamp to jump to that comm in the video. Click a speaker to edit roles.");
        commsGrid.CellDoubleClick += (_, e) =>
        {
            if (e.ColumnIndex == commsTextColumn.Index) SeekToCommsRow(e.RowIndex);
        };
        FormClosed += (_, _) => { matchHistoryLoad?.Cancel(); videoReviewWindow?.Close(); videoReview?.Dispose(); };
    }

    private void LogMatchHistory(string message)
    {
        if (IsDisposed || Disposing) return;
        try
        {
            if (InvokeRequired) BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing) AppendToLog(message); }));
            else AppendToLog(message);
        }
        catch (InvalidOperationException) { }
    }

    private async Task RefreshMatchHistoryAsync(string? path, int request, Task mediaReady)
    {
        matchHistoryLoad?.Cancel();
        if (path is null) return;
        string name = Path.GetFileName(path);
        if (!name.StartsWith("Game_", StringComparison.OrdinalIgnoreCase) || !long.TryParse(name[5..], out long id)) return;
        using var cts = new CancellationTokenSource(); matchHistoryLoad = cts;
        try
        {
            bool available = await Task.Run(() => MatchTimelineStore.EnsureAsync(_leagueClientApi, path, id,
                LogMatchHistory, cts.Token), cts.Token);
            if (available) await mediaReady;
            if (available && request == previewRequestId && !IsDisposed && !Disposing && videoReview is not null)
                await videoReview.RefreshGoldAsync(path);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException)
        { if (!IsDisposed && !Disposing) AppendToLog("Match timeline: " + ex.Message); }
        finally { if (ReferenceEquals(matchHistoryLoad, cts)) matchHistoryLoad = null; }
    }

    private async Task UpdateVideoCommsAsync(string gamePath, TranscriptionResult? transcript,
        IReadOnlyDictionary<string, string>? roles, int request)
    {
        if (videoReview is null) return;
        var champions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (transcript is not null)
        {
            try
            {
                champions = await Task.Run(() => VideoCommsTimeline.ReadChampions(gamePath, transcript.JsonPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { AppendToLog("Could not read video comms champion context: " + ex.Message); }
        }
        if (request != commsRequestId || IsDisposed || Disposing) return;
        var objectiveCalls = transcript is null ? new List<CommsAnalysisFinding>() : await Task.Run(() =>
            CommsAnalysis.DetectObjectiveCalls(transcript.Words ?? [], roles ?? new Dictionary<string, string>()));
        if (request != commsRequestId || IsDisposed || Disposing) return;
        champions = champions.ToDictionary(p => p.Key, p => AnalysisChampionDisplayName(p.Value), StringComparer.OrdinalIgnoreCase);
        var captions = VideoCommsTimeline.Build(transcript, roles, champions, VideoPortrait);
        var tone = await Task.Run(() => VideoCommsTone.Read(gamePath, transcript, roles));
        if (request != commsRequestId || IsDisposed || Disposing) return;
        string status = transcript is null ? "No transcript yet. Select Transcribe to add comms." :
            captions.Count == 0 ? "This transcript has no timed speech to display." : "";
        var calls = objectiveCalls.Select(f =>
        {
            string champion = f.Role is not ("unknown" or "overlap") && champions.TryGetValue(f.Role, out var c) ? c : "";
            return new VideoObjectiveCue(f.Id, f.AudioSeconds, f.AudioEndSeconds ?? f.AudioSeconds, f.Subject, f.Role,
                f.Role is "unknown" or "overlap" ? CommsRoleName(f.Role) : TranscribeAudio.FormatSpeakerLabel(f.SpeakerId, roles),
                champion, champion.Length > 0 ? VideoPortrait(champion) : null, f.SpeakerId);
        }).ToArray();
        videoReview.SetComms(gamePath, captions, status, transcript?.AudioTimelineShiftSeconds ?? 0, calls, tone);
        UpdateCommsShares(transcript, roles, champions);
    }

    private string? VideoPortrait(string champion)
    {
        if (videoPortraits.TryGetValue(champion, out var existing)) return existing;
        if (!analysisChampionIconPaths.TryGetValue(ChampionIconKey(champion), out var path) || !File.Exists(path))
            return null;
        try
        {
            // Small data URLs reuse the existing offline icon cache, without
            // giving the player access to arbitrary files or Riot endpoints.
            using var original = Image.FromFile(path);
            using var bitmap = new Bitmap(original, new Size(64, 64));
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return videoPortraits[champion] = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or
            System.Runtime.InteropServices.ExternalException)
        { AppendToLog("Could not load video comms portrait: " + ex.Message); return null; }
    }

    private bool CanSeekToCommsRow(int row) => row >= 0 && row < commsRowStarts.Count &&
        videoReview?.HasVideo == true && double.IsFinite(commsRowStarts[row]);

    private void SeekToCommsRow(int row)
    {
        if (!CanSeekToCommsRow(row)) return;
        if (gameWorkspace is not null) gameWorkspace.SelectedIndex = 0;
        videoReview!.SeekToComms(commsRowStarts[row]);
    }

    private async Task ToggleVideoReviewWindowAsync()
    {
        if (videoReview is null || videoReview.IsDisposed || movingVideoWindow) return;
        if (videoReviewWindow is not null) { videoReviewWindow.Close(); return; }
        var window = new Form
        {
            Text = "Video review · Esc to return", FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual, Bounds = Screen.FromControl(this).Bounds,
            BackColor = Color.Black, KeyPreview = true,
        };
        videoReviewWindow = window;
        bool allowWindowClose = false;
        window.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; window.Close(); } };
        window.FormClosing += async (_, e) =>
        {
            if (allowWindowClose || IsDisposed || Disposing || videoReview.IsDisposed) return;
            e.Cancel = true;
            if (movingVideoWindow) return;
            movingVideoWindow = true;
            try
            {
                await videoReview.BeginHostMoveAsync();
                if (IsDisposed || Disposing || videoReview.IsDisposed) return;
                previewPanel.Controls.Add(videoReview);
                videoReview.Dock = DockStyle.Fill;
                videoReview.BringToFront();
                videoReview.SetFullScreen(false);
            }
            finally { videoReview.EndHostMove(); movingVideoWindow = false; }
            videoReviewWindow = null;
            allowWindowClose = true;
            window.Close();
        };
        movingVideoWindow = true;
        try
        {
            await videoReview.BeginHostMoveAsync();
            if (IsDisposed || Disposing || videoReview.IsDisposed) { window.Dispose(); return; }
            window.Controls.Add(videoReview);
            videoReview.SetFullScreen(true);
            window.Show(this);
            videoReview.Focus();
        }
        finally { videoReview.EndHostMove(); movingVideoWindow = false; }
    }
}
