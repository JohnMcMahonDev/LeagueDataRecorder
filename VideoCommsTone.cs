namespace League_Data_Recorder;

// The video and saved reports carry team outlook only.
internal sealed record VideoToneCue(double Start, double End, double Valence, double SpeechSeconds);
internal sealed record VideoCommsTone(IReadOnlyList<VideoToneCue> Segments, string Status, double AudioTimelineShiftSeconds = 0, int WindowSeconds = 20)
{
    internal static VideoCommsTone Read(string gamePath, TranscriptionResult? transcript, IReadOnlyDictionary<string, string>? roles = null)
    {
        if (transcript is null) return new([], "Select Transcribe to add team Outlook.");
        try
        {
            var report = CommsOutlook.Read(gamePath, transcript, roles);
            return new(report.Windows.Select(w => new VideoToneCue(w.Start, w.End, w.Vibe, w.SpeechSeconds)).ToArray(),
                report.Status, report.AudioTimelineShiftSeconds, report.WindowSeconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return new([], "Outlook could not be loaded. Select Transcribe to retry."); }
    }
}
