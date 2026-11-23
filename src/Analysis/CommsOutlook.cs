namespace League_Data_Recorder;

internal sealed record CommsOutlookWindow(double Start, double End, double Vibe, double SpeechSeconds);
internal sealed record CommsOutlookReport(IReadOnlyList<CommsOutlookWindow> Windows, double? TeamVibe, string Status,
    double AudioTimelineShiftSeconds = 0, string SpeechFingerprint = "", int WindowSeconds = 20)
{
    public int Version { get; init; } = 3;
}

internal static class CommsOutlook
{
    internal static CommsOutlookReport Read(string gamePath, TranscriptionResult transcript, IReadOnlyDictionary<string, string>? roles)
    {
        if (string.IsNullOrWhiteSpace(transcript.JsonPath)) return Empty("No saved emotion report is linked to this transcript.");
        var report = CommsEmotion.TryRead(CommsEmotion.ReportPath(transcript.JsonPath));
        if (!CommsEmotion.Valid(report) || report!.SpeechFingerprint != CommsEmotion.SpeechFingerprint(transcript))
            return Empty("Select Transcribe to generate team Outlook with rolling audio windows.");
        string? audio = CommsEmotion.FindAudio(gamePath, transcript);
        if (audio is not null && report.AudioStamp != CommsEmotion.AudioStamp(audio)
            && Math.Abs(report.AudioTimelineShiftSeconds - VideoReviewTiming.AudioShiftForFile(audio)) < .00001)
            return Empty("The audio changed. Select Transcribe to refresh team Outlook.");
        return Build(report, transcript);
    }

    internal static CommsOutlookReport Build(CommsEmotionReport report, TranscriptionResult? transcript = null)
    {
        // Apply to existing caches too: context may contain speech while the
        // displayed five-second window itself is silent. No re-inference needed.
        var speech = transcript is null ? null : CommsEmotion.SpeechRanges(transcript,
            report.AudioTimelineShiftSeconds - transcript.AudioTimelineShiftSeconds, report.DurationSeconds);
        var windows = report.Windows.Where(w => speech is null || speech
            .Where(r => r.Start < w.End && r.End > w.Start)
            .Sum(r => Math.Max(0, Math.Min(w.End, r.End) - Math.Max(w.Start, r.Start))) >= .25)
            .OrderBy(w => w.Start)
            .Select(w => new CommsOutlookWindow(w.Start, w.End, w.Valence, w.SpeechSeconds)).ToArray();
        double weight = windows.Sum(w => w.End - w.Start);
        return new(windows, weight > 0 ? windows.Sum(w => w.Vibe * (w.End - w.Start)) / weight : null,
            windows.Length > 0 ? $"Estimated team vocal outlook, 0–100. Uses {report.WindowSeconds}-second rolling audio context, updated every {CommsEmotionReport.StepSeconds} seconds. All recorded comms voices contribute, including coach, unknown and overlapping speech. Silent five-second windows are blank; smoothing only connects adjacent measured windows. No per-speaker scores are stored."
                : "No speech with sufficient audio was available for team Outlook. Select Transcribe to retry.",
            report.AudioTimelineShiftSeconds, report.SpeechFingerprint, report.WindowSeconds);
    }

    internal static CommsOutlookReport Empty(string status) => new([], null, status);
    internal static void Save(string gamePath, CommsOutlookReport report) => CommsAnalysis.SaveJson(gamePath + "_CommsOutlook.json", report);
    internal static Task SaveForTranscriptAsync(string gamePath, TranscriptionResult transcript,
        CommsEmotionReport report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Save(gamePath, Build(report, transcript));
        return Task.CompletedTask;
    }
}
