
namespace League_Data_Recorder;

public static partial class TranscribeAudio
{
    // Old transcripts gain overlap labels immediately on opening. Their text,
    // word attribution and role assignments are left intact on disk.
    internal static void PrepareOverlapDisplay(TranscriptionResult transcript, string gamePath)
    {
        string? path = FindRecordingRttm(gamePath, transcript.JsonPath, transcript.JsonPath,
            transcript.RttmPath, null);
        if (path is null) return; // Stored flags still support the generic label.
        try
        {
            var turns = ReadRttm(path)
                .Select(t => new TranscriptSpeakerTurn(t.Start, t.End, t.Speaker)).ToArray();
            TranscriptOverlap.Apply(transcript.Words, turns, addMissingSpeech: true,
                duration: transcript.DurationSeconds);
            transcript.RttmPath = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { /* The transcript remains viewable if its optional RTTM is unavailable. */ }
    }
}

internal static class TranscriptOverlap
{
    private sealed record Span(double Start, double End, string[] Speakers);

    // Build disjoint spans of simultaneous activity. Duplicate turns belonging
    // to one speaker never count as a second voice; touching turns are sequential.
    private static List<Span> BuildSpans(IEnumerable<TranscriptSpeakerTurn> turns, double duration)
    {
        var events = new SortedDictionary<double, Dictionary<string, int>>();
        void Add(double time, string speaker, int delta)
        {
            if (!events.TryGetValue(time, out var changes)) events[time] = changes = new(StringComparer.Ordinal);
            changes[speaker] = changes.GetValueOrDefault(speaker) + delta;
        }
        foreach (var turn in turns)
        {
            if (TranscriptPostProcessor.Unknown(turn.Speaker) || !double.IsFinite(turn.Start) ||
                !double.IsFinite(turn.End) || turn.Start < 0 || turn.End <= turn.Start) continue;
            double end = duration > 0 && double.IsFinite(duration) ? Math.Min(duration, turn.End) : turn.End;
            if (end <= turn.Start) continue;
            Add(turn.Start, turn.Speaker, 1); Add(end, turn.Speaker, -1);
        }
        var spans = new List<Span>();
        var active = new Dictionary<string, int>(StringComparer.Ordinal);
        double previous = 0;
        foreach (var point in events)
        {
            if (active.Count > 1 && point.Key > previous)
            {
                string[] speakers = active.Keys.OrderBy(s => s, StringComparer.Ordinal).ToArray();
                if (spans.Count > 0 && spans[^1].End == previous && spans[^1].Speakers.SequenceEqual(speakers))
                    spans[^1] = spans[^1] with { End = point.Key };
                else spans.Add(new(previous, point.Key, speakers));
            }
            foreach (var change in point.Value)
            {
                int count = active.GetValueOrDefault(change.Key) + change.Value;
                if (count <= 0) active.Remove(change.Key); else active[change.Key] = count;
            }
            previous = point.Key;
        }
        return spans;
    }

    internal static void Apply(List<WordResult> words, IEnumerable<TranscriptSpeakerTurn> turns,
        bool addMissingSpeech = false, double duration = 0, CancellationToken cancellationToken = default)
    {
        var spans = BuildSpans(turns, duration);
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(word.Start) || !double.IsFinite(word.End) || word.End < word.Start) continue;
            double start = word.Start, end = word.End;
            if (end - start < .01) { start = Math.Max(0, start - .025); end += .025; }
            int low = 0, high = spans.Count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (spans[mid].End <= start) low = mid + 1; else high = mid;
            }
            var scores = new Dictionary<string, double>(StringComparer.Ordinal);
            double overlap = 0;
            for (int i = low; i < spans.Count && spans[i].Start < end; i++)
            {
                double seconds = Math.Min(end, spans[i].End) - Math.Max(start, spans[i].Start);
                if (seconds <= 0) continue;
                overlap += seconds;
                foreach (string speaker in spans[i].Speakers)
                    scores[speaker] = scores.GetValueOrDefault(speaker) + seconds;
            }
            if (overlap <= Math.Min(.02, (end - start) * .2)) continue;
            word.OverlappingSpeech = true;
            word.OverlappingSpeakers = scores.OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key).ToArray();
        }
        if (!addMissingSpeech || spans.Count == 0) return;

        // Cover only gaps with sustained simultaneous RTTM activity. A label is
        // evidence of speech, not a reconstruction of either speaker's words.
        var covered = new List<(double Start, double End)>();
        foreach (var word in words.Where(w => double.IsFinite(w.Start) && double.IsFinite(w.End) &&
            w.End > w.Start && !string.IsNullOrWhiteSpace(w.Word)).OrderBy(w => w.Start))
        {
            if (covered.Count > 0 && word.Start <= covered[^1].End)
                covered[^1] = (covered[^1].Start, Math.Max(covered[^1].End, word.End));
            else covered.Add((word.Start, word.End));
        }
        var missing = new List<WordResult>();
        void Gap(double start, double end, string[] speakers)
        {
            if (end - start < .65) return;
            missing.Add(new WordResult { Start = start, End = end,
                Word = " [overlapping speech; words not recovered]", SpeakerSource = "asr_gap",
                TimingSource = "review_span", OverlappingSpeech = true, OverlappingSpeakers = speakers.ToArray() });
        }
        int first = 0;
        foreach (var span in spans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double cursor = span.Start;
            while (first < covered.Count && covered[first].End <= cursor) first++;
            for (int i = first; i < covered.Count && covered[i].Start < span.End; i++)
            {
                Gap(cursor, Math.Min(covered[i].Start, span.End), span.Speakers);
                cursor = Math.Max(cursor, covered[i].End);
                if (cursor >= span.End) break;
            }
            Gap(cursor, span.End, span.Speakers);
        }
        if (missing.Count == 0) return;
        var ordered = words.Concat(missing).OrderBy(w => w.Start).ToArray();
        words.Clear(); words.AddRange(ordered);
    }

    internal static string[] Speakers(WordResult word) => (word.OverlappingSpeakers ?? [])
        .Where(s => !TranscriptPostProcessor.Unknown(s)).Distinct(StringComparer.Ordinal).ToArray();

    internal static string DisplayKey(WordResult word) => word.OverlappingSpeech
        ? "overlap:" + string.Join("|", Speakers(word).OrderBy(s => s, StringComparer.Ordinal)) : "single";

    internal static string Label(IEnumerable<string> speakers, IReadOnlyDictionary<string, string>? roles)
    {
        var ids = speakers.Where(s => !TranscriptPostProcessor.Unknown(s)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length < 2) return "Multiple speakers";
        string pair = string.Join(" + ", ids.Take(2).Select(s => TranscribeAudio.FormatSpeakerLabel(s, roles)));
        return ids.Length == 2 ? pair + " · overlap" : "Multiple speakers · likely " + pair;
    }
}
