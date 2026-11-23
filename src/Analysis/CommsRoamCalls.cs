using System.Text.RegularExpressions;

namespace League_Data_Recorder;

internal static partial class CommsAnalysis
{
    internal const string RoamCountingRule = "Roam calls: stated lane rotations, ganks, lane swaps, teammate-join plans and enemy non-jungler movement warnings. Counts belong to the caller, not the player moving. A destination or an explicit roam/gank is required. Bare sightings, missing-only calls, waves moving, camp clears, recalls, fight footwork and retrospective/quoted plans are excluded. Same-caller repeats of the same actor/destination within 4s count once.";
    private sealed record RoamCandidate(int Index, string Label, string Kind, Resolution Resolution);
    private const string RoamVerb = @"(?:roam(?:ing)?|rotat(?:e|ing)|gank(?:ing)?|com(?:e|ing)|go(?:ing)?|walk(?:ing)?|run(?:ning)?|mov(?:e|ing)|head(?:ing)?|look(?:ing)?(?:\s+for\s+(?:an?\s+)?gank)?)";

    private static IEnumerable<RoamCandidate> RoamCandidates(Utterance u, List<Mention> mentions, List<Player> players,
        IReadOnlyDictionary<string, string> roles, List<Focus> history,
        Dictionary<string, (Resolution Resolution, double Last)> subjects, List<LaneFocus> lanes)
    {
        string text = u.Text;
        if (Regex.IsMatch(text, @"\b(?:said|told|used to|was like|skin|chroma|emote)\b|\b(?:should|could|would)(?:'ve|\s+have)\b", Rx)) yield break;
        var patterns = new[]
        {
            @"\b(?<verb>" + RoamVerb + @")\s+(?:(?:straight|back|up|down|over|right|just|towards?|to|at|into|the|lane|for|gank|all|you|guys)\s+){0,7}(?<lane>top|mid|middle|bot|bottom)\b",
            @"\b(?<verb>com(?:e|ing)|walk(?:ing)?|run(?:ning)?|mov(?:e|ing)|head(?:ing)?)\s+(?:(?:straight|back|up|down|over|right|just)\s+){0,5}to\s+(?<join>you(?:\s+guys)?|our\s+team)\b",
            @"\b(?<verb>roam(?:ing)?|rotat(?:e|ing))(?=\s*[.!?;]*\s*$)"
        };
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (string pattern in patterns)
            foreach (Match m in Regex.Matches(text, pattern, Rx))
            {
                var bounds = ClauseBounds(text, m);
                int originalLeft = bounds.Start;
                foreach (Match split in Regex.Matches(text[originalLeft..m.Index], @"\b(?:and|but|then)\s+(?:then\s+)?(?=(?:i|we|you|he|she|they)\b)", Rx))
                    bounds = (originalLeft + split.Index + split.Length, bounds.End);
                string clause = text[bounds.Start..bounds.End], before = text[bounds.Start..m.Index];
                if (Regex.IsMatch(clause, @"^\s*(?:(?:yeah|oh|okay)[,\s]+)*(?:where|why|when|what|who|is|are|was|were|did|does)\b", Rx) ||
                    Regex.IsMatch(before, @"\b(?:was|were|don't|do not|not|can't|cannot|won't)\b", Rx)) continue;
                string rest = text[(m.Index + m.Length)..bounds.End];
                if (m.Groups["lane"].Success && Regex.IsMatch(rest, @"^\s+(?:side|jungle|camps?|buff|wave|tower|turret|inhibitor)\b", Rx)) continue;
                bool own = SelfSpellOwner(before) || Regex.IsMatch(before, @"\bi(?:'d| would)\s+like\s+to\s*$", Rx);
                Resolution? actor;
                if (own)
                {
                    string role = CommsSpeechIntervals.AssignedRole(u.Speaker, roles);
                    var owners = players.Where(p => p.Ally == true && role != "unknown" && p.Role == role).ToArray();
                    actor = new(owners.Length == 1 ? owners[0] : new Player { Champion = "Unidentified caller", Role = "unknown", Ally = true, Generic = true },
                        owners.Length == 1 ? "self rotation" : "unresolved self", "First-person movement/gank plan with a stated destination.", u.At(m.Index).Word.Start, true);
                }
                else if (Regex.IsMatch(before, @"\b(?:we|you)(?:'re|'ll)?\s+(?:(?:guys|all|can|should|will|just|wanna|want to|gonna|going to|are)\s+)*$", Rx))
                    actor = new(TeamTarget(true, players), "team rotation request", "Explicit plural/team rotation; individual movers are not invented.", u.At(m.Index).Word.Start);
                else actor = Resolve(u, m, bounds, mentions, players, roles, history, subjects, lanes);
                if (actor is null && (Regex.IsMatch(before, @"\b(?:we|you)(?:\s+guys)?\b", Rx) ||
                    !Regex.IsMatch(before, @"\bi\b", Rx) && Regex.IsMatch(before, ObjectiveLeadPattern, Rx)))
                    actor = new(TeamTarget(true, players), "team rotation request", "Explicit team/teammate rotation instruction; individual movers are not invented.", u.At(m.Index).Word.Start);
                if (actor is null || actor.Player.Ally == false && actor.Player.Role == "jungle") continue;
                string verb = m.Groups["verb"].Value.ToLowerInvariant();
                if (verb.StartsWith("look", StringComparison.Ordinal) && actor.Player.Ally == true &&
                    actor.Player.Role != "jungle" && !verb.Contains("gank", StringComparison.Ordinal)) continue;
                string lane = m.Groups["lane"].Value.ToLowerInvariant() switch { "middle" => "mid", "bottom" => "bot", var v => v };
                string kind = verb.Contains("gank", StringComparison.Ordinal) || verb.StartsWith("look", StringComparison.Ordinal) ? "gank" : "roam";
                string label = m.Groups["join"].Success ? "Join teammates" : lane.Length > 0
                    ? (kind == "gank" ? "Gank " : "Rotate to ") + lane : "Roam; destination unstated";
                string key = PlayerKey(actor.Player) + "|" + label;
                if (emitted.Add(key)) yield return new(m.Index, label, kind, actor);
            }
        foreach (Match m in Regex.Matches(text, @"\b(?:send|put|bring)\s+(?<actor>[\p{L}'’ -]{1,28}?)\s+(?<lane>top|mid|middle|bot|bottom)\b", Rx))
        {
            if (Regex.IsMatch(text[..m.Index], @"\b(?:don't|do not|not|never|can't|cannot|won't)\b", Rx)) continue;
            var local = mentions.Where(x => x.Index >= m.Groups["actor"].Index && x.Index < m.Groups["actor"].Index + m.Groups["actor"].Length)
                .Select(x => x.Player).DistinctBy(PlayerKey).ToArray();
            Resolution? actor = local.Length == 1 ? new(local[0], "named rotation assignment", "Explicit player/champion rotation assignment.", u.At(m.Index).Word.Start) : null;
            if (actor is null || actor.Player.Ally == false && actor.Player.Role == "jungle") continue;
            string lane = m.Groups["lane"].Value.ToLowerInvariant() switch { "middle" => "mid", "bottom" => "bot", var v => v };
            string label = "Rotate to " + lane;
            if (emitted.Add(PlayerKey(actor.Player) + "|" + label)) yield return new(m.Index, label, "roam", actor);
        }
        foreach (Match m in Regex.Matches(text, @"\b(?:switch|swap)\s+(?:lanes?\s+)?(?:with|to)\s+(?<lane>top|mid|middle|bot|bottom)(?:\s+or\s+(?<other>top|mid|middle|bot|bottom))?\b", Rx))
        {
            if (!Regex.IsMatch(text[..m.Index], @"\b(?:we|you|i|let's|lets)\b", Rx)) continue;
            string destination = m.Groups["lane"].Value + (m.Groups["other"].Success ? " or " + m.Groups["other"].Value : "");
            yield return new(m.Index, "Swap lanes: " + destination, "swap", new(TeamTarget(true, players),
                "team lane-swap proposal", "Explicit lane-swap request; any alternative destinations remain as spoken.", u.At(m.Index).Word.Start));
        }
    }
}
