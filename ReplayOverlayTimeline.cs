namespace League_Data_Recorder;

internal sealed record ReplayBubble(string Key, string Label, string Role, string Champion, string? Icon,
    string Text, bool IsCall, IReadOnlyList<VideoCommsParticipant>? Participants = null, string Person = "");

internal sealed class ReplayOverlayTimeline
{
    private readonly record struct Speech(VideoCommsCue Cue, double DisplayEnd);
    private readonly Speech[] speech;
    private readonly double[] prefixEnds;
    private readonly VideoObjectiveCue[] calls;
    private readonly double audioToGame;
    internal string? AudioPath { get; }
    internal double AudioGameOffset { get; }
    internal IReadOnlyList<VideoCommsParticipant> People { get; }

    internal ReplayOverlayTimeline(IReadOnlyList<VideoCommsCue> cues, IReadOnlyList<VideoObjectiveCue> calls, double audioToGame,
        string? audioPath = null, double? audioGameOffset = null)
    {
        if (!double.IsFinite(audioToGame)) throw new ArgumentOutOfRangeException(nameof(audioToGame));
        this.audioToGame = audioToGame;
        AudioPath = audioPath; AudioGameOffset = audioGameOffset ?? audioToGame;
        string[] roleOrder = ["top", "jungle", "mid", "adc", "support", "coach", "unknown"];
        People = cues.SelectMany(c => c.Participants is { Count: > 0 } p ? p :
                new[] { new VideoCommsParticipant(c.Speaker.Length > 0 ? c.Speaker : c.Label, c.Label, c.Role, c.Champion, c.Icon) })
            .Concat(calls.Where(c => c.SpeakerId.Length > 0).Select(c => new VideoCommsParticipant(c.SpeakerId, c.Speaker, c.Role, c.Champion, c.Icon)))
            .DistinctBy(p => p.Speaker).OrderBy(p => Array.IndexOf(roleOrder, p.Role) is int index && index >= 0 ? index : 7)
            .ThenBy(p => p.Speaker, StringComparer.Ordinal).ToArray();
        var ordered = cues.Where(c => double.IsFinite(c.Start) && double.IsFinite(c.End) && c.End >= c.Start)
            .OrderBy(c => c.Start).ToArray();
        speech = new Speech[ordered.Length];
        prefixEnds = new double[ordered.Length];
        var nextBySpeaker = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = ordered.Length - 1; i >= 0; i--)
        {
            var cue = ordered[i];
            double end = Math.Max(cue.End + Math.Clamp(cue.Text.Length / 28.0, .9, 3.5), cue.Start + 2.4);
            if (nextBySpeaker.TryGetValue(cue.Speaker, out double next)) end = Math.Min(end, next);
            if (i + 1 < ordered.Length && (cue.OverlappingSpeech || ordered[i + 1].OverlappingSpeech))
                end = Math.Min(end, Math.Max(cue.End, ordered[i + 1].Start));
            if (cue.Text.Contains("[overlapping speech; words not recovered]", StringComparison.Ordinal)) end = cue.End;
            speech[i] = new(cue, end);
            nextBySpeaker[cue.Speaker] = cue.Start;
        }
        for (int i = 0; i < speech.Length; i++)
            prefixEnds[i] = Math.Max(i == 0 ? double.NegativeInfinity : prefixEnds[i - 1], speech[i].DisplayEnd);
        this.calls = calls.Where(c => double.IsFinite(c.Start) && double.IsFinite(c.End) && c.End >= c.Start)
            .OrderBy(c => c.Start).ToArray();
    }

    internal IReadOnlyList<ReplayBubble> At(double gameTime)
    {
        if (!double.IsFinite(gameTime) || gameTime < 0) return [];
        double at = gameTime - audioToGame;
        var result = new List<ReplayBubble>();
        // Keep every active call, including simultaneous calls from different people.
        foreach (var call in calls.Where(c => at >= c.Start && at < Math.Max(c.Start + 5, c.End + 2)))
        {
            var people = speech.Where(s => s.Cue.Start <= call.End && s.Cue.End >= call.Start)
                .SelectMany(s => s.Cue.Participants ?? []).DistinctBy(p => p.Speaker).ToArray();
            result.Add(new("call:" + call.Id, call.Speaker, call.Role, call.Champion, call.Icon, call.Label, true,
                people.Length >= 2 && (call.Role == "overlap" || people.Any(p => p.Role == call.Role)) ? people : null,
                call.SpeakerId));
        }

        int low = 0, high = speech.Length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (speech[mid].Cue.Start <= at) low = mid + 1; else high = mid;
        }
        var active = new List<ReplayBubble>();
        var speakers = new HashSet<string>(StringComparer.Ordinal);
        for (int i = low - 1; i >= 0 && prefixEnds[i] > at && active.Count < 8; i--)
        {
            var item = speech[i];
            var cue = item.Cue;
            if (item.DisplayEnd <= at || !speakers.Add(cue.Speaker)) continue;
            string text = cue.Words.Count == 0 ? cue.Text :
                string.Join(" ", cue.Words.Where(w => w.Start <= at + .001).Select(w => w.Text));
            if (string.IsNullOrWhiteSpace(text)) continue;
            active.Add(new("speech:" + cue.Id, cue.Label, cue.Role, cue.Champion, cue.Icon, text, false, cue.Participants, cue.Speaker));
        }
        active.Reverse();
        result.AddRange(active);
        // An overlap represents shared recovered words, not a reliable separation.
        // Duplicate that same bubble beside each identified participant's portrait.
        return result.SelectMany(b => b.Participants is { Count: > 0 } people
            ? people.Select(p => b with { Key = b.Key + ":" + p.Speaker, Label = p.Label, Role = p.Role,
                Champion = p.Champion, Icon = p.Icon, Participants = null, Person = p.Speaker })
            : new[] { b }).GroupBy(b => b.Person.Length > 0 ? b.Person : b.Role + "|" + b.Label).Select(g =>
            {
                var call = g.LastOrDefault(b => b.IsCall);
                var spoken = g.LastOrDefault(b => !b.IsCall);
                if (call is null) return spoken!;
                return spoken is null ? call : call with { Key = call.Key + "|" + spoken.Key,
                    Text = call.Text + " · " + spoken.Text };
            }).ToArray();
    }
}
