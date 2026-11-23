using LeagueTelemetry;
using System.Text.Json;

namespace League_Data_Recorder;

public partial class Form1
{
    private ReplayAudioSession? replayAudio;
    private void InitializeReplayAudio() => FormClosed += (_, _) => { replayAudio?.Dispose(); replayAudio = null; };

    private async Task<ReplayAudioOptions?> PrepareReplayAudioAsync(string gamePath)
    {
        if (!File.Exists(gamePath + "_Audio.wav")) { AppendToLog("No saved comms audio for this replay."); return null; }
        try
        {
            bool changed = await Task.Run(() => ReplayApiSettings.Enable(LeagueClientApi.GetInstallationDirectory()));
            if (IsDisposed || Disposing) return null;
            if (changed) AppendToLog("Enabled League's Replay API for synchronized comms audio.");
            var gate = VideoReviewTiming.AudioTimelineGateForFile(gamePath + "_Audio.wav");
            await gate.WaitAsync();
            try
            {
                // This clock refers to the current WAV; no transcript, caption
                // correction, speaker assignment or overlay is needed.
                var review = VideoReviewTiming.Read(gamePath);
                var clock = new TranscriptionResult { AudioTimelineShiftSeconds = review.AudioTimelineShiftSeconds };
                double? offset = CommsAnalysis.AudioToGameOffset(gamePath, clock, new CommsAnalysisReport());
                if (offset is not double value) { AppendToLog("Replay audio needs valid saved audio/game capture timing."); return null; }
                return new(gamePath + "_Audio.wav", value - review.CaptionCorrectionSeconds);
            }
            finally { gate.Release(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or System.ComponentModel.Win32Exception or System.Text.DecoderFallbackException)
        { if (!IsDisposed && !Disposing) AppendToLog("Replay comms audio is unavailable: " + ex.Message); return null; }
    }
    private void StartReplayAudio(string gamePath, ReplayAudioOptions options, int[] previousProcesses)
    {
        replayAudio?.Dispose();
        var session = new ReplayAudioSession(gamePath, options, previousProcesses, AppendToLog);
        replayAudio = session;
        session.Completed += (_, _) => { if (ReferenceEquals(replayAudio, session)) replayAudio = null; };
    }
}
