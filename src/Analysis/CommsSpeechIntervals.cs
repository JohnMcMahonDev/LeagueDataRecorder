
namespace League_Data_Recorder;

// Overlap speaker lists identify simultaneous activity, not which speaker said a word.
// Split recovered word time equally; preserve unresolved shares in the overlap bucket.
internal static class CommsSpeechIntervals
{
    internal static bool IsTimedWord(WordResult w) => w is not null && !string.IsNullOrWhiteSpace(w.Word) &&
        w.SpeakerSource != "asr_gap" && double.IsFinite(w.Start) && double.IsFinite(w.End) && w.Start >= 0 && w.End > w.Start;

    internal static string AssignedRole(string? speaker, IReadOnlyDictionary<string, string>? roles)
    {
        string assigned = speaker is not null && roles is not null && roles.TryGetValue(speaker, out var role) ? role : "";
        return assigned.EndsWith('?') ? "unknown" : VideoCommsTimeline.NormalizeRole(assigned);
    }

    internal static IEnumerable<(string Role, double Weight)> Allocate(WordResult word, IReadOnlyDictionary<string, string>? roles)
    {
        if (!word.OverlappingSpeech) { yield return (AssignedRole(word.Speaker, roles), 1); yield break; }
        var speakers = (word.OverlappingSpeakers ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray();
        if (speakers.Length == 0)
        {
            string role = AssignedRole(word.Speaker, roles);
            yield return (role == "unknown" ? "overlap" : role, 1);
            yield break;
        }
        foreach (var group in speakers.GroupBy(s => AssignedRole(s, roles)))
            yield return (group.Key == "unknown" ? "overlap" : group.Key, (double)group.Count() / speakers.Length);
    }

    // Maximum active weight unions duplicate/overlapping words without counting them twice.
    internal static double Seconds(IEnumerable<(double Start, double End, double Weight)> spans)
    {
        var events = spans.Where(s => s.End > s.Start && s.Weight > 0)
            .SelectMany(s => new[] { (Time: s.Start, s.Weight, Delta: 1), (Time: s.End, s.Weight, Delta: -1) })
            .OrderBy(e => e.Time).ToArray();
        var active = new SortedDictionary<double, int>();
        double total = 0, previous = 0;
        foreach (var e in events)
        {
            if (active.Count > 0) total += (e.Time - previous) * Math.Min(1, active.Last().Key);
            int count = active.GetValueOrDefault(e.Weight) + e.Delta;
            if (count == 0) active.Remove(e.Weight); else active[e.Weight] = count;
            previous = e.Time;
        }
        return total;
    }
}
