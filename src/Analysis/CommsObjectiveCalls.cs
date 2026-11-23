using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace League_Data_Recorder;

internal static partial class CommsAnalysis
{
    internal const string ObjectiveCountingRule = "Objective calls: team instructions/proposals to contest, take, give, defend, push, prepare, deploy Herald or end, with an explicit objective or clear end-game route. Grub/Herald plans, dives and clear objective opportunities are included. Direct proposals such as 'go dragon?' are included; information questions are excluded. A concrete Smite-health instruction may use one unambiguous recently called epic objective. Negation is preserved. Same-caller repeats within 4s count once; changed instructions remain separate. Enemy reports, past counterfactuals, uncertain alternatives and bare objective/timer mentions are excluded. Counts describe calls, not their success.";

    // Keep the grammar explicit: a nearby objective noun alone is not a call.
    internal const string ObjectiveActionPattern = @"\b(?:(?:go\s+)?(?:tower\s+)?div(?:e|ing)|fight(?:ing(?:ing)?)?|contest(?:ing)?|tak(?:e|ing)|start(?:ing)?|do(?:ing)?|hit(?:ting)?|burn(?:ing)?|rush(?:ing)?|secur(?:e|ing)|finish(?:ing)?|steal(?:ing)?|get(?:ting)?|giv(?:e|ing)(?:\s+up)?|conced(?:e|ing)|leav(?:e|ing)|drop(?:ping)?|deploy(?:ing)?|defend(?:ing)?|hold(?:ing)?|sav(?:e|ing)|protect(?:ing)?|push(?:ing)?|sieg(?:e|ing)|set(?:ting)?\s+up|setup|prep(?:ping)?|prepar(?:e|ing)|group(?:ing)?|rotat(?:e|ing)|go(?:ing)?|com(?:e|ing)|walk(?:ing)?|run(?:ning)?|head(?:ing)?|look(?:ing)?(?:\s+at)?|solo(?:ing)?|reset(?:ting)?|end(?:ing)?)\b|\bcan(?=\s+(?:" + ObjectiveBuildingPattern + @"))";
    internal const string ObjectiveEpicPattern = @"\b(?:elder(?:\s+(?:dragon|drake))?|dragon|drake|baron(?:\s+nashor)?|nashor|nash|rift(?:\s+herald)?|herald|void\s*grubs|grubs|atakhan)\b";
    private const string ObjectiveLanePattern = @"(?:top|mid|middle|bot|bottom)";
    private const string ObjectiveTierPattern = @"(?:t\s*(?:[123]|one|two|three)|tier\s*(?:[123]|one|two|three)|outer|inner|first|second|third)";
    private const string ObjectiveStructurePattern = @"(?:inhib(?:itor)?\s+(?:tower|turret)s?|nexus\s+(?:tower|turret)s?|tower[s]?|turret[s]?|inhib(?:itor)?s?|nexus|base)";
    internal const string ObjectiveBuildingPattern = @"\b(?:" +
        @"(?<tier1>" + ObjectiveTierPattern + @")\s+(?<lane1>" + ObjectiveLanePattern + @")\s+(?<structure1>" + ObjectiveStructurePattern + @")|" +
        @"(?<lane2>" + ObjectiveLanePattern + @")\s+(?<tier2>" + ObjectiveTierPattern + @")(?:\s+(?<structure2>tower[s]?|turret[s]?))?|" +
        @"(?:(?<lane3>" + ObjectiveLanePattern + @")\s+)?(?:(?<tier3>" + ObjectiveTierPattern + @")\s+)?(?<structure3>" + ObjectiveStructurePattern + @")|" +
        @"(?<tier4>t\s*(?:[123]|one|two|three)|tier\s*(?:[123]|one|two|three))" +
        @")(?:\s+(?:(?:in|at|on)\s+)?(?<postLane>" + ObjectiveLanePattern + @"))?\b";
    internal const string ObjectiveLeadPattern = @"^\s*(?:(?:hey|yeah|yes|okay|ok|alright|wait|no|guys|team|please|just|so|then|now|actually|oh|uh|um|well|nice|100\s*%|100\s+percent)[,\s]+|(?:i\s+mean|(?:i\s+)?think|i\s+guess|let's|lets|let\s+us)[,\s]+)*" +
        @"(?:the\s+(?:right|best)\s+(?:play|move|call|place)\s+(?:is\s+)?(?:to\s+)?)?" +
        @"(?:(?:(?:we|you)(?:\s+guys)?(?:'re|\s+are)?|i(?:'ll|'m|\s+am)?)\s+)?" +
        @"(?:(?:can|should|must|will|are|be|need\s+to|have\s+to|gotta|want\s+to|wanna|going\s+to|gonna|all|just|still|really|definitely|100\s*%|100\s+percent|to)\s+)*" +
        @"(?:(?:don't|dont|do\s+not|not|never|can't|cannot|won't|shouldn't|aren't)\s+)?$";
    internal const string ObjectiveBridgePattern = @"^[,\s]*(?:(?:the|a|an|our|their|this|that|these|those|next|first|second|third|one|two|three|\d+|all|both|it|them|us|for|at|on|to|with|straight|right|uh|um|around|down|up|through|before|ahead\s+of|and|then|just|now)\b[\s,]*)*$";
    internal const string ObjectiveNonCallPattern = @"^\s*(?:(?:yeah|oh|okay|well)[,\s]+)*(?:can|could|should|would|why|how|what|when|where|who|is|are|was|were|if|unless|whether|maybe|perhaps)\b|^\s*(?:do|does|did)\s+(?:we|you|i|they|he|she|it)\b|\b(?:should|could|would|might)\s+have\b|\b(?:said|told)\b";
    private sealed record ObjectiveTarget(string Key, string Label, string Kind, int Index, int Length);
    private sealed record ObjectiveIntent(string Code, string Label);
    private sealed record ObjectiveRepeat(string Action, double LastSeen, CommsAnalysisFinding Finding);

    internal static List<CommsAnalysisFinding> DetectObjectiveCalls(IEnumerable<WordResult> words,
        IReadOnlyDictionary<string, string> roles, double? audioToGame = null, CancellationToken token = default)
    {
        var found = new List<CommsAnalysisFinding>();
        var recent = new Dictionary<string, ObjectiveRepeat>(StringComparer.Ordinal);
        var activeEpics = new Dictionary<string, (ObjectiveTarget Target, double Time)>(StringComparer.Ordinal);
        foreach (var u in ObjectiveUtterances(words))
        {
            token.ThrowIfCancellationRequested();
            // Only unfinished fragments are joined below. Replacing their ASR
            // punctuation with spaces preserves character positions for u.At().
            string text = u.Text.Replace('.', ' ').Replace('!', ' ');
            AddSecureInstruction(u);
            var opportunities = ObjectiveTargets(text);
            foreach (var target in opportunities.Where(t => t.Kind is "tower" or "inhibitor"))
                if (Regex.IsMatch(text[..target.Index], @"^\s*(?:(?:hey|yeah|oh)[,\s]+)*$", Rx) &&
                    Regex.IsMatch(text[(target.Index + target.Length)..], @"^\s*(?:'s|is|are)\s+(?:free|open)\b", Rx))
                    AddStandalone(u, target.Index, target.Key, "Consider " + target.Label, "Consider taking", "propose", true);
            if (u.Text.Contains('?') && opportunities.Count >= 2 && opportunities.Select(t => t.Key).Distinct().Count() == 1)
            {
                string rest = text;
                foreach (var target in opportunities.OrderByDescending(t => t.Index)) rest = rest.Remove(target.Index, target.Length);
                if (Regex.IsMatch(rest, @"^[,\s?]*(?:(?:hey|yeah|guys|team|please|the|uh|um)[,\s?]*)*$", Rx))
                    AddStandalone(u, opportunities[0].Index, opportunities[0].Key, "Consider " + opportunities[0].Label, "Consider", "propose", true);
            }
            // A proposal asks teammates to act. An information question asks
            // whether an event happened/is happening; only the former is a call.
            var proposalPrefix = Regex.Match(text, @"\b(?<modal>can|could|should|would|do)\s+(?=we\b|you\b|i\b)", Rx);
            bool teamProposal = proposalPrefix.Success && !Regex.IsMatch(text[..proposalPrefix.Index], @"\b(?:if|unless|whether|asked|said|told)\b", Rx) &&
                Regex.IsMatch(text[(proposalPrefix.Index + proposalPrefix.Length)..],
                    @"^(?:we|you|i)(?:\s+guys)?\s+(?:(?:all|just|please|wanna|want to|need to|go and)\s+)*(?:" + ObjectiveActionPattern + @")", Rx);
            if (teamProposal) text = text.Remove(proposalPrefix.Index, proposalPrefix.Length).Insert(proposalPrefix.Index, new string(' ', proposalPrefix.Length));
            var condition = Regex.Match(text, @"\bif\b", Rx);
            var consequence = condition.Success ? Regex.Match(text[(condition.Index + condition.Length)..], @"\b(?:we\s+(?:can|should|will|just)|let's|lets)\b", Rx) : Match.Empty;
            bool conditionalPlan = consequence.Success && text.Substring(condition.Index + condition.Length, consequence.Index).Any(c => !char.IsWhiteSpace(c)) &&
                ObjectiveTargets(text[(condition.Index + condition.Length + consequence.Index)..]).Count > 0;
            if (conditionalPlan)
            {
                int start = condition.Index + condition.Length + consequence.Index;
                text = new string(' ', start) + text[start..];
            }
            bool directProposal = u.Text.Contains('?') && Regex.IsMatch(text,
                @"^\s*(?:(?:yeah|okay|oh|just|please)[,\s]+)*(?:go|come|look|take|get|do|fight(?:ing(?:ing)?)?|contest|give|start|solo|reset|dive)\b", Rx);
            directProposal |= teamProposal || conditionalPlan;
            directProposal |= u.Text.Contains('?') && Regex.IsMatch(text, @"\b(?:we|you)(?:\s+guys)?\s+(?:wanna|want to)\s+(?:" + ObjectiveActionPattern + @")", Rx);
            // A concrete diveability proposal is useful even when its target
            // is deictic. Preserve that uncertainty; it is never a tower capture.
            var diveable = Regex.Match(text, @"\b(?:should\s+be|(?:he|she|it)(?:'s|\s+is)|we\s+can)\s+(?:(?:easily|very|just)\s+)*(?:diveable|divable)\b", Rx);
            if (diveable.Success && !Regex.IsMatch(text, @"\b(?:we're|we are|was|were|not|never|can't|cannot|said|told)\b", Rx))
                AddStandalone(u, diveable.Index, "dive", "Dive target (proposed)", "Dive", "dive", true);
            if (u.Text.Contains('?') && !directProposal || Regex.IsMatch(text, ObjectiveNonCallPattern, Rx)) continue;
            var actions = Regex.Matches(text, ObjectiveActionPattern, Rx).Cast<Match>().ToArray();
            bool previousAccepted = false;
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                var intent = ObjectiveAction(action.Value);
                string before = text[..action.Index];
                var boundaries = Regex.Matches(before, @"[,;]|\b(?:and|but|then|while|instead)\b", Rx).Cast<Match>().ToArray();
                int boundary = boundaries.Length > 0 ? boundaries[^1].Index + boundaries[^1].Length : 0;
                string lead = text[boundary..action.Index];
                bool accepted = Regex.IsMatch(lead, ObjectiveLeadPattern, Rx);
                if (!accepted && i > 0 && previousAccepted && boundary <= actions[i - 1].Index)
                {
                    // ASR may omit the comma in 'give dragon, take mid tower'.
                    // Inherit only after an explicit objective, never arbitrary speech.
                    string remainder = text[(actions[i - 1].Index + actions[i - 1].Length)..action.Index];
                    var previousTargets = ObjectiveTargets(remainder);
                    foreach (var t in previousTargets.OrderByDescending(t => t.Index))
                        remainder = remainder.Remove(t.Index, t.Length).Insert(t.Index, new string(' ', t.Length));
                    if (previousTargets.Count > 0 && Regex.IsMatch(remainder, ObjectiveBridgePattern, Rx))
                    { lead = ""; accepted = true; }
                }
                // A coordinated enemy report is still an enemy report: 'they
                // take dragon and push mid tower' must not become our instruction.
                if (i > 0 && !previousAccepted && !Regex.IsMatch(lead, @"\b(?:we|you|i|let's|lets|let\s+us)\b", Rx)) accepted = false;
                int end = i + 1 < actions.Length ? actions[i + 1].Index : text.Length;
                var targets = ObjectiveTargets(text[(action.Index + action.Length)..end])
                    .Select(t => t with { Index = t.Index + action.Index + action.Length }).ToList();
                // Explicit topic-first phrasing, such as 'dragon, we give'. Do
                // not borrow an objective from a preceding, complete instruction.
                if (i == 0 && targets.Count == 0)
                {
                    var topic = ObjectiveTargets(before).LastOrDefault();
                    if (topic is not null && (before[(topic.Index + topic.Length)..].Contains(',') ||
                        Regex.IsMatch(before[(topic.Index + topic.Length)..], @"\b(?:we|you|i|let's|lets)\b", Rx)) &&
                        Regex.IsMatch(text[(action.Index + action.Length)..end], ObjectiveBridgePattern, Rx) &&
                        Regex.IsMatch(before[(topic.Index + topic.Length)..], @"^[,\s]*(?:" + ObjectiveLeadPattern[1..^1] + @")$", Rx))
                    {
                        targets.Add(topic);
                        lead = before[(topic.Index + topic.Length)..].TrimStart(',', ' ');
                        accepted = Regex.IsMatch(lead, ObjectiveLeadPattern, Rx);
                    }
                }
                previousAccepted = accepted;
                if (!accepted || Regex.IsMatch(text[(action.Index + action.Length)..end], @"\bor\b", Rx)) continue;
                bool negative = Regex.IsMatch(lead, @"\b(?:don't|dont|do\s+not|not|never|can't|cannot|won't|shouldn't|aren't)\b", Rx);
                string code = (negative ? "avoid_" : "") + intent.Code;
                if (intent.Code == "dive")
                {
                    var lane = Regex.Match(text[action.Index..end], @"\b(?:top|mid|middle|bot|bottom)\b", Rx);
                    string target = lane.Success ? "Dive " + ObjectiveLane(lane.Value) : "Dive target";
                    AddStandalone(u, action.Index, "dive:" + (lane.Success ? ObjectiveLane(lane.Value) : "unknown"), target,
                        negative ? "Don't dive" : "Dive", code, directProposal || text.Contains("if", StringComparison.OrdinalIgnoreCase));
                    continue;
                }
                int previousTargetEnd = action.Index + action.Length;
                foreach (var target in targets.OrderBy(t => t.Index))
                {
                    string bridge = target.Index >= previousTargetEnd ? text[previousTargetEnd..target.Index] : "";
                    if (!Regex.IsMatch(bridge, ObjectiveBridgePattern, Rx) || Regex.Matches(bridge, @"\b\w+\b").Count > 8) continue;
                    if (Regex.IsMatch(text[(target.Index + target.Length)..], @"^\s+(?:dive|diving|aggro|damage|range|shots?)\b", Rx)) continue;
                    if (intent.Code == "push" && target.Kind == "epic" || intent.Code == "end" && target.Kind is not ("base" or "nexus")) continue;
                    bool deploy = Regex.IsMatch(action.Value, @"^(?:drop|deploy)", Rx) && target.Key.StartsWith("epic:herald", StringComparison.Ordinal);
                    var targetIntent = deploy ? new ObjectiveIntent("deploy", "Deploy") : intent;
                    Add(target, (negative ? "avoid_" : "") + targetIntent.Code,
                        negative ? "Don't " + targetIntent.Label.ToLowerInvariant() : targetIntent.Label);
                    previousTargetEnd = Math.Max(previousTargetEnd, target.Index + target.Length);
                }
                if (intent.Code == "end" && targets.Count == 0)
                {
                    string rest = text[(action.Index + action.Length)..end];
                    rest = Regex.Replace(rest, @"(?:,\s*|\b(?:and|but|then|while)\s*)+$", "", Rx);
                    var route = Regex.Match(rest, @"^\s*(?:(?:the|this)\s+game|it)?\s*(?:(?:through|via|down)\s+(?<lane>top|mid|middle|bot|bottom)|(?<directLane>top|mid|middle|bot|bottom))?\s*(?:now|here|just)?\s*$", Rx);
                    if (route.Success)
                    {
                        string lane = ObjectiveLane(route.Groups["lane"].Value.Length > 0 ? route.Groups["lane"].Value : route.Groups["directLane"].Value);
                        string label = lane.Length > 0 ? "through " + lane : "the game";
                        Add(new("nexus:" + lane + "::", label, "nexus", action.Index + action.Length, route.Length), code,
                            negative ? "Don't end" : "End");
                    }
                }

                void Add(ObjectiveTarget target, string actionCode, string actionLabel)
                {
                    var attribution = CallSpeaker(u, roles);
                    // In 'take dragon and baron', the second explicit target is
                    // called when its name is spoken, not at the earlier verb.
                    int anchor = previousTargetEnd > action.Index + action.Length && target.Index >= previousTargetEnd
                        ? target.Index : action.Index;
                    double at = u.At(anchor).Word.Start;
                    double lastWord = u.At(Math.Max(action.Index, Math.Min(text.Length - 1, target.Index + target.Length - 1))).Word.End;
                    string identity = u.Identity.Length > 0 ? u.Identity : "unknown";
                    string key = identity + "|" + target.Key;
                    if (recent.TryGetValue(key, out var prior) && prior.Action == actionCode && at >= prior.LastSeen && at - prior.LastSeen <= RepeatSeconds)
                    {
                        prior.Finding.AudioEndSeconds = Math.Max(prior.Finding.AudioEndSeconds ?? prior.Finding.AudioSeconds, lastWord);
                        if (!prior.Finding.Text.Contains(u.Text, StringComparison.Ordinal)) prior.Finding.Text += "\n" + u.Text;
                        recent[key] = prior with { LastSeen = at };
                        RememberEpic(target, actionCode, at);
                        return;
                    }
                    var finding = new CommsAnalysisFinding
                    {
                        Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("objective|" + key + "|" + actionCode + "|" + at.ToString("R", CultureInfo.InvariantCulture))))[..24],
                        AudioSeconds = at, AudioEndSeconds = Math.Max(at, lastWord),
                        GameSeconds = audioToGame.HasValue ? at + audioToGame.Value : null,
                        Category = "Objective", Scope = "Team", Role = attribution.Role, SpeakerId = u.Speaker,
                        SpeakerAttribution = attribution.Method, Subject = actionLabel + " " + target.Label, Target = target.Label,
                        Call = actionLabel, CallKind = actionCode, Text = u.Text, UsesCollapsedTiming = u.Collapsed,
                        TargetResolution = "Explicit objective instruction", Uncertain = directProposal || Regex.IsMatch(u.Text, @"\b(?:think|should|guess|probably|maybe)\b", Rx),
                        Evidence = "An explicit team instruction and objective were stated. Repeats from the same caller within 4s count once; changed instructions remain separate. Speaker attribution: " + attribution.Method + ". This does not verify whether the call was followed or succeeded.",
                    };
                    found.Add(finding);
                    recent[key] = new(actionCode, at, finding);
                    RememberEpic(target, actionCode, at);
                }
            }
        }
        return found.OrderBy(f => f.AudioSeconds).ThenBy(f => f.Id, StringComparer.Ordinal).ToList();

        void AddStandalone(Utterance u, int index, string targetKey, string target, string label, string kind, bool uncertain)
        {
            double at = u.At(index).Word.Start;
            var attribution = CallSpeaker(u, roles);
            string identity = u.Identity.Length > 0 ? u.Identity : "unknown";
            string key = identity + "|" + targetKey;
            if (recent.TryGetValue(key, out var prior) && prior.Action == kind && at >= prior.LastSeen && at - prior.LastSeen <= RepeatSeconds) return;
            var finding = new CommsAnalysisFinding
            {
                Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("objective|" + key + "|" + kind + "|" + at.ToString("R", CultureInfo.InvariantCulture))))[..24],
                AudioSeconds = at, AudioEndSeconds = u.End, GameSeconds = audioToGame.HasValue ? at + audioToGame.Value : null,
                Category = "Objective", Scope = "Team", Role = attribution.Role, SpeakerId = u.Speaker, SpeakerAttribution = attribution.Method,
                Subject = target, Target = target, Call = label, CallKind = kind, Text = u.Text, UsesCollapsedTiming = u.Collapsed,
                Uncertain = uncertain, TargetResolution = kind is "dive" or "avoid_dive" ? "Explicit dive plan" : "Explicit objective opportunity/proposal",
                Evidence = kind is "dive" or "avoid_dive" ? "A team dive instruction/proposal was stated. The target/lane remains unspecified unless spoken. A dive is not a reported structure capture."
                    : "An objective opportunity or direct team prompt was stated. It is a proposal, not a verified capture or committed action.",
            };
            found.Add(finding); recent[key] = new(kind, at, finding);
        }

        void RememberEpic(ObjectiveTarget target, string action, double at)
        {
            if (target.Kind != "epic") return;
            if (action is "take" or "contest" or "secure") activeEpics[target.Key] = (target, at);
            else if (action is "give" or "avoid_take" or "avoid_contest") activeEpics.Remove(target.Key);
        }

        void AddSecureInstruction(Utterance u)
        {
            if (u.Text.Contains('?') || Regex.IsMatch(u.Text, @"\b(?:should|could|would|might)\s+have\b|\b(?:said|told)\b|^\s*(?:(?:yeah|oh|okay|well)[,\s]+)*(?:if|unless|whether|maybe|perhaps)\b", Rx)) return;
            foreach (Match instruction in Regex.Matches(u.Text, @"\bsmite\s+(?:(?:it|this|that)\s+)?(?:at|on)\s+(?<health>\d{3,5})\b", Rx))
            {
                string before = u.Text[..instruction.Index];
                bool direct = Regex.IsMatch(before, ObjectiveLeadPattern, Rx);
                bool tell = Regex.IsMatch(before, @"\b(?:tell|remind|ask)(?:\s+[\p{L}']+){1,5}\s+to\s*$", Rx);
                if (!direct && !tell) continue;
                double at = u.At(instruction.Index).Word.Start;
                var candidates = activeEpics.Values.Where(v => at >= v.Time && at - v.Time <= 45).ToArray();
                if (candidates.Length != 1) continue;
                var target = candidates[0].Target;
                var attribution = CallSpeaker(u, roles);
                string identity = u.Identity.Length > 0 ? u.Identity : "unknown";
                string key = identity + "|" + target.Key;
                if (recent.TryGetValue(key, out var prior) && prior.Action == "secure" && at >= prior.LastSeen && at - prior.LastSeen <= RepeatSeconds) continue;
                string label = "Secure at " + instruction.Groups["health"].Value + " HP";
                var finding = new CommsAnalysisFinding
                {
                    Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("objective|" + key + "|secure|" + at.ToString("R", CultureInfo.InvariantCulture))))[..24],
                    AudioSeconds = at, AudioEndSeconds = u.At(instruction.Index + instruction.Length - 1).Word.End,
                    GameSeconds = audioToGame.HasValue ? at + audioToGame.Value : null,
                    Category = "Objective", Scope = "Team", Role = attribution.Role, SpeakerId = u.Speaker,
                    SpeakerAttribution = attribution.Method, Subject = label + " · " + target.Label, Target = target.Label,
                    Call = label, CallKind = "secure", Text = u.Text, UsesCollapsedTiming = u.Collapsed, Uncertain = true,
                    TargetResolution = "Recent explicit objective decision",
                    Evidence = "Concrete Smite-health instruction for the only recently called epic objective (within 45s). The health is spoken advice, not verified damage or Smite availability. Speaker attribution: " + attribution.Method + ".",
                };
                found.Add(finding); recent[key] = new("secure", at, finding);
            }
        }
    }

    private static List<Utterance> ObjectiveUtterances(IEnumerable<WordResult> words)
    {
        var result = new List<Utterance>();
        foreach (var speaker in DetectionUtterances(words).GroupBy(u => u.Speaker, StringComparer.Ordinal))
        {
            Utterance? current = null;
            foreach (var next in speaker.OrderBy(u => u.Start))
            {
                bool incomplete = current is not null && !current.Text.Contains('?') && Regex.IsMatch(current.Text,
                    @"\b(?:fight|contest|give|take|start|do|hit|burn|rush|secure|finish|steal|get|defend|hold|save|protect|siege|push|end|set\s+up|group|rotate|go|come|look|solo|reset|drop|deploy|top|mid|bot|bottom|don't|dont|do\s+not|not)(?:\s+(?:the|a|our|their|this|that|for|at|to|through|up))*[.!]?\s*$", Rx);
                if (incomplete && current is not null && next.Start >= current.End - .05 && next.Start - current.End <= FragmentGapSeconds && next.End - current.Start <= 12)
                    current = new Utterance { Tokens = current.Tokens.Concat(next.Tokens).OrderBy(t => t.Word.Start).ThenBy(t => t.Order).ToList() };
                else { if (current is not null) result.Add(current); current = next; }
            }
            if (current is not null) result.Add(current);
        }
        var ordered = result.OrderBy(u => u.Start).ToList();
        var joined = new List<Utterance>();
        for (int i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            if (i + 1 < ordered.Count)
            {
                var next = ordered[i + 1];
                bool prefix = Regex.IsMatch(current.Text, @"^\s*(?:i|we|you)(?:\s+(?:can|should|will|just|go|take|hit|look|at|the)){0,5}[.!]?\s*$", Rx);
                if (prefix && (current.Speaker.Length == 0 || next.Speaker.Length == 0) &&
                    next.Start >= current.End - .05 && next.Start - current.End <= FragmentGapSeconds && next.End - current.Start <= 12 &&
                    ObjectiveTargets(next.Text).Count > 0)
                {
                    current = new Utterance { Tokens = current.Tokens.Concat(next.Tokens).OrderBy(t => t.Word.Start).ThenBy(t => t.Order).ToList() };
                    i++;
                }
            }
            joined.Add(current);
        }
        return joined;
    }

    private static ObjectiveIntent ObjectiveAction(string verb)
    {
        string value = verb.ToLowerInvariant();
        if (Regex.IsMatch(value, @"\bdiv(?:e|ing)\b")) return new("dive", "Dive");
        if (Regex.IsMatch(value, @"^(?:go|com|walk|run|head|look|reset)")) return new("setup", value.StartsWith("look", StringComparison.Ordinal) ? "Look at" : value.StartsWith("reset", StringComparison.Ordinal) ? "Reset for" : "Go to");
        if (Regex.IsMatch(value, @"^(?:fight|contest)")) return new("contest", "Contest");
        if (Regex.IsMatch(value, @"^(?:giv|conced|leav|drop)")) return new("give", "Give");
        if (Regex.IsMatch(value, @"^(?:defend|hold|sav|protect)")) return new("defend", "Defend");
        if (Regex.IsMatch(value, @"^(?:push|sieg)")) return new("push", "Push");
        if (Regex.IsMatch(value, @"^(?:set|prep|group|rotat)")) return new("setup", "Set up");
        return value.StartsWith("end", StringComparison.Ordinal) ? new("end", "End") : new("take", "Take");
    }

    private static List<ObjectiveTarget> ObjectiveTargets(string text)
    {
        var result = new List<ObjectiveTarget>();
        foreach (Match m in Regex.Matches(text, ObjectiveEpicPattern, Rx))
        {
            string value = m.Value.ToLowerInvariant();
            string label = value.StartsWith("elder", StringComparison.Ordinal) ? "elder" :
                Regex.IsMatch(value, @"^(?:dragon|drake)$") ? "dragon" : Regex.IsMatch(value, @"^(?:baron|nash)") ? "baron" :
                value.Contains("herald", StringComparison.Ordinal) || value == "rift" ? "herald" : value.Contains("grub", StringComparison.Ordinal) ? "grubs" : "atakhan";
            var lane = label == "herald" ? Regex.Match(text[(m.Index + m.Length)..], @"^\s+(?:(?:in|at|to)\s+)?(?<lane>top|mid|middle|bot|bottom)\b", Rx) : Match.Empty;
            string route = lane.Success ? ObjectiveLane(lane.Groups["lane"].Value) : "";
            result.Add(new("epic:" + label + (route.Length > 0 ? ":" + route : ""), label + (route.Length > 0 ? " " + route : ""), "epic", m.Index, m.Length + lane.Length));
        }
        foreach (Match m in Regex.Matches(text, ObjectiveBuildingPattern, Rx))
        {
            string Group(params string[] names) => names.Select(n => m.Groups[n].Value).FirstOrDefault(v => v.Length > 0) ?? "";
            string lane = ObjectiveLane(Group("lane1", "lane2", "lane3", "postLane"));
            string postLane = ObjectiveLane(m.Groups["postLane"].Value);
            if (lane.Length > 0 && postLane.Length > 0 && lane != postLane) continue;
            string structure = Group("structure1", "structure2", "structure3").ToLowerInvariant();
            string tier = ObjectiveTier(Group("tier1", "tier2", "tier3", "tier4"));
            bool tower = structure.Contains("tower", StringComparison.Ordinal) || structure.Contains("turret", StringComparison.Ordinal) || tier.Length > 0;
            string kind = structure.StartsWith("nexus", StringComparison.Ordinal) ? "nexus" : structure == "base" ? "base" : tower ? "tower" : "inhibitor";
            if (tower && structure.StartsWith("inhib", StringComparison.Ordinal) && tier.Length == 0) tier = "T3";
            string label = kind == "nexus" ? structure.Contains(' ') ? "nexus towers" : "nexus" : kind == "base" ? "base" :
                (lane.Length > 0 ? lane + " " : "") + (kind == "inhibitor" ? "inhibitor" : tier.Length > 0 ? tier + (lane.Length == 0 ? " tower" : "") : "tower");
            result.Add(new(kind + ":" + lane + ":" + tier + ":" + (structure.StartsWith("nexus ", StringComparison.Ordinal) ? "towers" : ""), label, kind, m.Index, m.Length));
        }
        return result.OrderBy(t => t.Index).ToList();
    }
    private static string ObjectiveLane(string value) => value.ToLowerInvariant() switch { "middle" => "mid", "bottom" => "bot", var v => v };
    private static string ObjectiveTier(string value)
    {
        if (value.Length == 0) return "";
        if (Regex.IsMatch(value, @"(?:1|one|first|outer)\b", Rx)) return "T1";
        return Regex.IsMatch(value, @"(?:2|two|second|inner)\b", Rx) ? "T2" : "T3";
    }
}
