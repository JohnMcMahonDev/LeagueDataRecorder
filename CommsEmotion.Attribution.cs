using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace League_Data_Recorder;

internal static partial class CommsEmotion
{
    // Team inference depends on speech timing, not speaker attribution or roles.
    internal static string SpeechFingerprint(TranscriptionResult transcript)
    {
        var text = new StringBuilder(transcript.AudioTimelineShiftSeconds.ToString("R", CultureInfo.InvariantCulture));
        foreach (var word in SpeechWords(transcript))
            text.Append('|').Append(word.Start.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                .Append(word.End.ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static IEnumerable<WordResult> SpeechWords(TranscriptionResult transcript) =>
        (transcript.Words ?? [])
        .Where(w => w is not null && double.IsFinite(w.Start) && double.IsFinite(w.End) && w.End > w.Start
            && !string.IsNullOrWhiteSpace(w.Word) && w.SpeakerSource != "asr_gap").OrderBy(w => w.Start).ThenBy(w => w.End);

    internal readonly record struct SpeechRange(double Start, double End);
    internal readonly record struct TeamWindow(double Start, double End, double ContextStart, double ContextEnd, double SpeechSeconds);

    // Union all voices so overlapping words never double-count speech coverage.
    internal static List<SpeechRange> SpeechRanges(TranscriptionResult transcript, double shift, double duration)
    {
        var ranges = new List<SpeechRange>();
        foreach (var word in SpeechWords(transcript))
        {
            double start = Math.Clamp(word.Start + shift, 0, duration), end = Math.Clamp(word.End + shift, 0, duration);
            if (end <= start) continue;
            if (ranges.Count > 0 && start <= ranges[^1].End)
                ranges[^1] = ranges[^1] with { End = Math.Max(ranges[^1].End, end) };
            else ranges.Add(new(start, end));
        }
        return ranges;
    }

    internal static List<TeamWindow> TeamWindows(IReadOnlyList<SpeechRange> ranges, double duration, int windowSeconds)
    {
        var windows = new List<TeamWindow>();
        if (duration < 1 || ranges.Count == 0) return windows;
        double contextLength = Math.Min(windowSeconds, duration);
        int firstRange = 0;
        for (double start = 0; start < duration; start += CommsEmotionReport.StepSeconds)
        {
            double end = Math.Min(duration, start + CommsEmotionReport.StepSeconds);
            double contextStart = Math.Clamp((start + end - contextLength) / 2, 0, duration - contextLength);
            double contextEnd = contextStart + contextLength;
            while (firstRange < ranges.Count && ranges[firstRange].End <= contextStart) firstRange++;
            double speech = 0;
            for (int j = firstRange; j < ranges.Count && ranges[j].Start < contextEnd; j++)
                speech += Math.Max(0, Math.Min(contextEnd, ranges[j].End) - Math.Max(contextStart, ranges[j].Start));
            if (speech < .25) continue;
            windows.Add(new(start, end, contextStart, contextEnd, speech));
        }
        return windows;
    }
}
