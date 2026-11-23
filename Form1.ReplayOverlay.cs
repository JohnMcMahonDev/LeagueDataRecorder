using LeagueTelemetry;

namespace League_Data_Recorder;

public partial class Form1
{
    private ReplayOverlayWindow? replayOverlay;

    private void InitializeReplayOverlay()
    {
        FormClosed += (_, _) => { replayOverlay?.Dispose(); replayOverlay = null; };
    }

    private async Task<ReplayOverlayTimeline?> PrepareReplayOverlayAsync(string gamePath)
    {
        try
        {
            bool changed = await Task.Run(() => ReplayApiSettings.Enable(LeagueClientApi.GetInstallationDirectory()));
            if (IsDisposed || Disposing) return null;
            if (changed) AppendToLog("Enabled League's Replay API in game.cfg. The original config was backed up if it existed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or
            InvalidOperationException or System.Text.DecoderFallbackException)
        {
            if (!IsDisposed && !Disposing)
                AppendToLog("Could not automatically enable League's Replay API: " + ex.Message +
                    " The replay will open without comms bubbles. Check write access to League's Config/game.cfg.");
            return null;
        }

        // Respect the recorder's existing gate while loading a transcript and its
        // timing metadata; applying a WAV correction must not split this snapshot.
        var gate = VideoReviewTiming.AudioTimelineGateForFile(gamePath + "_Audio.wav");
        await gate.WaitAsync();
        try
        {
            var transcript = await ReadTranscriptAsync(gamePath);
            if (IsDisposed || Disposing) return null;
            if (transcript is null)
            {
                AppendToLog("No saved transcript for this recording. Replay comms audio is available without speech bubbles.");
                transcript = new TranscriptionResult { AudioFile = gamePath + "_Audio.wav",
                    AudioTimelineShiftSeconds = VideoReviewTiming.Read(gamePath).AudioTimelineShiftSeconds };
            }
            var roles = string.IsNullOrWhiteSpace(transcript.JsonPath) ? new Dictionary<string, string>() :
                await TranscribeAudio.ReadSpeakerRoleLabelsAsync(transcript.JsonPath) ?? new Dictionary<string, string>();
            var context = await Task.Run(() =>
            {
                // Share the same audio-to-game mapping as comms analysis, including
                // saved caption corrections and transcript/WAV timeline shifts.
                double? offset = CommsAnalysis.AudioToGameOffset(gamePath, transcript, new CommsAnalysisReport());
                var champions = VideoCommsTimeline.ReadChampions(gamePath, transcript.JsonPath);
                var calls = CommsAnalysis.DetectObjectiveCalls(transcript.Words ?? [], roles);
                return (offset, champions, calls);
            });
            if (IsDisposed || Disposing) return null;
            if (context.offset is not double offset)
            {
                AppendToLog("Replay comms bubbles need valid saved audio/game capture timing. This recording's timing is unavailable.");
                return null;
            }
            var champions = context.champions.ToDictionary(p => p.Key, p => AnalysisChampionDisplayName(p.Value),
                StringComparer.OrdinalIgnoreCase);
            var cues = VideoCommsTimeline.Build(transcript, roles, champions, VideoPortrait);
            var calls = context.calls.Select(f =>
            {
                string champion = f.Role is not ("unknown" or "overlap") && champions.TryGetValue(f.Role, out var c) ? c : "";
                return new VideoObjectiveCue(f.Id, f.AudioSeconds, f.AudioEndSeconds ?? f.AudioSeconds, f.Subject, f.Role,
                    f.Role is "unknown" or "overlap" ? CommsRoleName(f.Role) : TranscribeAudio.FormatSpeakerLabel(f.SpeakerId, roles),
                    champion, champion.Length > 0 ? VideoPortrait(champion) : null, f.SpeakerId);
            }).ToArray();
            var review = VideoReviewTiming.Read(gamePath);
            // Caption correction affects text only. Audio follows its WAV origin and
            // voice correction, accounting for any shift already written into the WAV.
            double audioOffset = offset + transcript.AudioTimelineShiftSeconds - review.AudioTimelineShiftSeconds
                - review.CaptionCorrectionSeconds;
            return new ReplayOverlayTimeline(cues, calls, offset, CommsEmotion.FindAudio(gamePath, transcript), audioOffset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or
            InvalidOperationException or ArgumentException)
        {
            if (!IsDisposed && !Disposing) AppendToLog("Could not load replay comms bubbles: " + ex.Message);
            return null;
        }
        finally { gate.Release(); }
    }

    private void StartReplayOverlay(string gamePath, ReplayOverlayTimeline timeline, int[] previousProcesses)
    {
        replayOverlay?.Dispose();
        var window = new ReplayOverlayWindow(gamePath, timeline, previousProcesses, AppendToLog);
        replayOverlay = window;
        window.Disposed += (_, _) => { if (ReferenceEquals(replayOverlay, window)) replayOverlay = null; };
    }
}
