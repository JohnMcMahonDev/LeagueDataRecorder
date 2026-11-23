using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace League_Data_Recorder;

internal static partial class CommsAnalysis
{
    // Time limits describe conversational continuity, not a desired count or game-specific calibration.
    private const double ContextSeconds = 8, RepeatSeconds = 4, FragmentGapSeconds = 1.6;
    private sealed record SpeechToken(WordResult Word, int Order)
    {
        internal string Text => Word.Word.Trim();
        internal string Speaker => Word.Speaker ?? "";
    }
    private sealed class Utterance
    {
        internal List<SpeechToken> Tokens = new();
        internal string Text => string.Join(" ", Tokens.Select(t => t.Text)).Replace('’', '\'');
        internal double Start => Tokens[0].Word.Start;
        internal double End => Tokens.Max(t => t.Word.End);
        internal string[] Speakers => Tokens.Select(t => t.Speaker).Where(s => !UnknownSpeaker(s)).Distinct(StringComparer.Ordinal).ToArray();
        internal string Speaker => Speakers.Length == 1 ? Speakers[0] : "";
        internal string Identity => string.Join("|", Speakers.OrderBy(s => s, StringComparer.Ordinal));
        internal SpeechToken At(int index)
        {
            int length = 0;
            foreach (var t in Tokens) { length += t.Text.Length + 1; if (length > index) return t; }
            return Tokens[^1];
        }
        internal bool Collapsed => Tokens.Any(t => t.Word.End <= t.Word.Start);
        internal bool Overlap => Tokens.Any(t => t.Word.OverlappingSpeech);
    }
    private sealed record Mention(Player Player, int Index, int Length, double Time, string Source);
    private sealed record Focus(Player Player, double Time, double End, string Text, string Speaker);
    private sealed record LaneFocus(string Role, double Time, string Text);
    private sealed record Resolution(Player Player, string Method, string Evidence, double AnchorTime, bool IsSelf = false);
    private sealed record CallState(string Kind, string Label, double? Seconds = null);
    private sealed record SpellMention(Match Match, string Category, string Name, bool NeedsContext, bool Bundle = false, Player? AbilityOwner = null);

    private static bool UnknownSpeaker(string? speaker) => string.IsNullOrWhiteSpace(speaker) ||
        speaker.Equals("unknown", StringComparison.OrdinalIgnoreCase) || speaker.Equals("unassigned", StringComparison.OrdinalIgnoreCase);
    private static bool DetectionWord(WordResult w) => w is not null && w.SpeakerSource != "asr_gap" &&
        !string.IsNullOrWhiteSpace(w.Word) && w.Word.Any(char.IsLetterOrDigit) &&
        double.IsFinite(w.Start) && double.IsFinite(w.End) && w.Start >= 0 && w.End >= w.Start;

    private static List<Utterance> DetectionUtterances(IEnumerable<WordResult> words)
    {
        var result = new List<Utterance>();
        // The retained word speaker is useful for sentence reconstruction even when the word overlaps.
        // It is not used to give the same callout to two candidate speakers.
        var tokens = words.Select((w, i) => new SpeechToken(w, i)).Where(t => DetectionWord(t.Word));
        foreach (var speaker in tokens.GroupBy(t => t.Speaker, StringComparer.Ordinal))
        {
            var span = new Utterance();
            foreach (var t in speaker.OrderBy(t => t.Word.Start).ThenBy(t => t.Order))
            {
                if (span.Tokens.Count > 0 && (t.Word.Start - span.End > 2 || t.Word.End - span.Start > 12))
                { result.Add(span); span = new(); }
                span.Tokens.Add(t);
                if (Regex.IsMatch(t.Text, @"[.!?]$")) { result.Add(span); span = new(); }
            }
            if (span.Tokens.Count > 0) result.Add(span);
        }
        return result.OrderBy(u => u.Start).ThenBy(u => u.Tokens[0].Order).ToList();
    }

    private static bool Question(string text) => text.Contains('?') || Regex.IsMatch(text,
        @"^\s*(?:(?:yeah|okay|ok|wait)[, ]+)*(?:does|do|did|can|could|should|would|where|when|who|is|are|was|were)\b", Rx);
    private static string Phrase(string phrase) => @"(?<![\p{L}\p{N}])" + Regex.Escape(phrase).Replace(@"\ ", @"\s+") + @"(?![\p{L}\p{N}])";
    private static string PlayerKey(Player p) => p.Team + "|" + p.ChampionId + "|" + p.Name + "|" + p.Champion;
    private static string DetectionNamePattern(string name)
    {
        if (name.Count(char.IsLetter) < 4) return Phrase(name);
        var result = new StringBuilder(@"(?<![\p{L}\p{N}])");
        for (int i = 0; i < name.Length; i++)
        {
            char ch = name[i];
            if (char.IsWhiteSpace(ch)) { result.Append(@"\s+"); continue; }
            int run = 1;
            while (i + 1 < name.Length && char.ToLowerInvariant(name[i + 1]) == char.ToLowerInvariant(ch)) { i++; run++; }
            result.Append(Regex.Escape(ch.ToString()));
            if (char.IsLetter(ch)) result.Append(run > 1 ? "{1,3}" : "{1,2}");
        }
        return result.Append(@"(?![\p{L}\p{N}])").ToString();
    }

    private static List<Mention> Mentions(Utterance u, List<Player> players)
    {
        var found = new List<Mention>();
        string text = u.Text;
        foreach (var p in players)
        {
            var names = p.Aliases.Concat(p.Name.Length > 0 ? new[] { p.Name.Split('#')[0] } : []).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
                foreach (Match m in Regex.Matches(text, p.Aliases.Contains(name, StringComparer.OrdinalIgnoreCase) ? DetectionNamePattern(name) : Phrase(name), Rx))
                {
                    // A mirror pick needs an explicit side qualifier; never guess which copy was meant.
                    bool ambiguous = players.Any(other => other != p && (other.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                        other.Name.Length > 0 && other.Name.Split('#')[0].Equals(name, StringComparison.OrdinalIgnoreCase)));
                    if (ambiguous)
                    {
                        string before = text[Math.Max(0, m.Index - 18)..m.Index];
                        bool? side = Regex.IsMatch(before, @"\b(?:enemy|their|opposing)\s*$", Rx) ? false :
                            Regex.IsMatch(before, @"\b(?:our|allied)\s*$", Rx) ? true : null;
                        if (side is null || p.Ally != side) continue;
                    }
                    found.Add(new(p, m.Index, m.Length, u.At(m.Index).Word.Start, m.Value));
                }
        }
        foreach (Match m in Regex.Matches(text, @"\b(?<side>enemy|their|opposing|our|allied)\s+(?<role>top|jungler|jungle|jg|mid|adc|ad carry|support|supp)\b", Rx))
        {
            bool ally = Regex.IsMatch(m.Groups["side"].Value, @"^(?:our|allied)$", Rx);
            string role = RoleReference(m.Groups["role"].Value);
            var candidates = players.Where(p => p.Ally == ally && p.Role == role).ToArray();
            if (candidates.Length == 1) found.Add(new(candidates[0], m.Index, m.Length, u.At(m.Index).Word.Start, m.Value));
            else if (candidates.Length == 0 && !ally && role == "jungle")
                found.Add(new(new Player { Champion = "Enemy jungler", Role = "jungle", Ally = false, Team = -1, Generic = true }, m.Index, m.Length, u.At(m.Index).Word.Start, m.Value));
        }
        return found.GroupBy(m => (PlayerKey(m.Player), m.Index)).Select(g => g.OrderByDescending(m => m.Length).First())
            .OrderBy(m => m.Index).ToList();
    }
    private static string RoleReference(string role) => role.ToLowerInvariant() switch
    { "jg" or "jungler" => "jungle", "ad carry" => "adc", "supp" => "support", var value => value };

    private static IEnumerable<SpellMention> SpellMentions(string text, List<Player> players, LeagueAliasCatalog catalog)
    {
        foreach (var spell in Spells)
            foreach (Match m in Regex.Matches(text, spell.Pattern, Rx)) yield return new(m, "Summoner", spell.Name, spell.NeedsContext);
        foreach (Match m in Regex.Matches(text, @"\b(?:sums?|summs|summoners?(?:\s+spells?)?)\b", Rx))
            yield return new(m, "Summoner", "Summoner spells", false, true);
        foreach (Match m in Regex.Matches(text, @"\b(?:ult(?:imates?|i|ed|ing|s)?|ulti|r)\b", Rx))
            yield return new(m, "Ultimate", "Ultimate", false);
        var abilityNames = players.SelectMany(p =>
        {
            var ult = catalog.Abilities(p.ChampionId).FirstOrDefault(a => a.Slot == "R");
            return catalog.Names(ult, ult?.Name).Where(n => n.Length >= 4).Select(n => (Player: p, Name: n));
        }).GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in abilityNames)
            foreach (Match m in Regex.Matches(text, Phrase(name.Key), Rx))
                yield return new(m, "Ultimate", "Ultimate", false, false, name.Count() == 1 ? name.First().Player : null);
    }

    private static (int Start, int End) ClauseBounds(string text, Match m)
    {
        var punctuation = Regex.Matches(text, @"(?<!\d)\.|\.(?!\d)|[!?;]").Cast<Match>().ToArray();
        int left = punctuation.Where(p => p.Index < m.Index).Select(p => p.Index + p.Length).DefaultIfEmpty(0).Last();
        int end = punctuation.Where(p => p.Index >= m.Index + m.Length).Select(p => p.Index + p.Length).DefaultIfEmpty(text.Length).First();
        // A comma is a clause boundary only if the following words introduce a new subject/question.
        int originalLeft = left;
        foreach (Match split in Regex.Matches(text[originalLeft..end], @",\s*(?=(?:i|he|she|they|we|you|can|could|does|is|where)\b)", Rx))
        {
            int at = originalLeft + split.Index;
            if (at < m.Index) left = at + split.Length;
            else { end = at; break; }
        }
        return (left, end);
    }

    private static CallState? State(string text, SpellMention spell, string clause, string before, string after)
    {
        if (Regex.IsMatch(before, @"\b(?:should|could|would|might)(?:'ve|\s+have)\b|\b(?:if|unless)\s+(?:you|we|they|he|she|i)\s+(?:have|has)\b", Rx)) return null;
        if (Question(clause) || Regex.IsMatch(clause, @"^\s*(?:(?:yeah|well|okay|oh)[, ]+)*if\b", Rx)) return null;
        if (Regex.IsMatch(clause, @"\b(?:skin|chroma|emote|animation|particle|spelling|transcript|subtitle)\b", Rx)) return null;
        if (spell.Name == "Exhaust" && spell.Match.Value.Equals("exhausted", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(before, @"\b(?:i'm|i am|we're|we are|looks?|feels?|seems?|sounds?)\s*$", Rx)) return null;
        if (Regex.IsMatch(before, @"\b(?:nice|good|great|celebratory)\s*$", Rx)) return null;
        var delayed = Regex.Match(after, @"^\s+(?:in|on|for)\s+(?<n>one|two|three|four|five|six|seven|eight|nine|ten|fifteen|twenty|thirty|sixty)\s*(?<unit>seconds?|secs?|s|minutes?|mins?|m)?\b", Rx);
        if (delayed.Success && (delayed.Value.TrimStart().StartsWith("for", StringComparison.OrdinalIgnoreCase) ? Regex.IsMatch(before, @"\b(?:no|without)\s*$", Rx) : true))
        {
            string[] names = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "fifteen", "twenty", "thirty", "sixty"];
            int[] values = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 30, 60];
            int value = values[Array.IndexOf(names, delayed.Groups["n"].Value.ToLowerInvariant())];
            if (spell.Category == "Summoner" && value <= 5 && !delayed.Groups["unit"].Success)
                return new("unavailable", "Cooldown reported: " + delayed.Groups["n"].Value + " (unit unstated)");
            double seconds = value * (delayed.Groups["unit"].Value.StartsWith("m", StringComparison.OrdinalIgnoreCase) ? 60 : 1);
            return new("cooldown", "Back in " + seconds.ToString("0.##", CultureInfo.InvariantCulture) + "s", seconds);
        }
        var clock = Regex.Match(after, @"^\s+(?:(?:is|back|up|ready)\s+)*(?:at\s+)?(?<m>\d{1,2}):(?<s>[0-5]\d)\b", Rx);
        if (clock.Success) return new("clock", "Timer " + clock.Groups["m"].Value + ":" + clock.Groups["s"].Value,
            int.Parse(clock.Groups["m"].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(clock.Groups["s"].Value, CultureInfo.InvariantCulture));
        var remaining = Regex.Match(after, @"^\s+(?:(?:is|back|up|ready|comes?\s+back)\s+)*(?:in|on|for)\s+(?<n>\d+(?:\.\d+)?)\s*(?<unit>seconds?|secs?|s|minutes?|mins?|m)?\b", Rx);
        if (remaining.Success && double.TryParse(remaining.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
        {
            if (remaining.Value.TrimStart().StartsWith("for", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(before, @"\b(?:no|without)\s*$", Rx)) return null;
            if (spell.Category == "Summoner" && n <= 5 && !remaining.Groups["unit"].Success)
                return new("unavailable", "Cooldown reported: " + remaining.Groups["n"].Value + " (unit unstated)");
            bool minutes = remaining.Groups["unit"].Value.StartsWith("m", StringComparison.OrdinalIgnoreCase);
            double seconds = n * (minutes ? 60 : 1);
            if (seconds is >= 0 and <= 600) return new("cooldown", "Back in " + seconds.ToString("0.##", CultureInfo.InvariantCulture) + "s", seconds);
        }
        if (Regex.IsMatch(before, @"\b(?:no|without|(?:doesn't|does not|don't|do not|didn't|did not)\s+have)\s+(?:(?:his|her|their|my|any)\s+)?$", Rx) ||
            Regex.IsMatch(after, @"^\s*(?:'s\s+|(?:is|was)\s+)?(?:down|on cooldown|not (?:up|ready|available))\b", Rx)) return new("unavailable", "Unavailable");
        if (Regex.IsMatch(before, @"\b(?:haven't|have not|hasn't|has not|didn't|did not|don't|do not|never|not)\s+(?:(?:really|actually|ever|just|even|yet|used|cast|wasted|missed|my|his|her|their|the)\s+)*$", Rx)) return null;
        // A declared capability or plan is useful readiness information, not a cast.
        // Preserve the conditional wording and never turn wishes/past hypotheticals into use.
        if (Regex.IsMatch(before, @"\b(?:will|gonna|going to)\s+have\s+(?:(?:his|her|their|my|an?)\s+)?$", Rx) ||
            Regex.IsMatch(after, @"^\s*(?:'s\s+|is\s+)?(?:gonna|going to|will|should|might)\s+(?:be\s+)?(?:back\s+)?(?:up|ready|available)\b", Rx))
            return new("coming_up", "Expected back soon");
        bool own = SelfSpellOwner(before);
        bool counterfactual = Regex.IsMatch(clause, @"\b(?:wish|wished|would|should|could|might)\s+(?:(?:i|we)\s+)?(?:have|had)\b|\b(?:wish|wished)\b", Rx);
        bool holding = own && Regex.IsMatch(before, @"\b(?:don't|do not)\s+(?:want to|wanna)\s+(?:(?:use|cast)\s+)?$", Rx) &&
            Regex.IsMatch(text[(spell.Match.Index + spell.Match.Length)..], @"^[,\s]*(?:i\s+)?(?:want to|wanna|will|gonna|going to)\s+(?:save|hold)\s+(?:it|that|this)\b", Rx);
        if (!counterfactual && holding) return new("available", "Available; holding it");
        if (!counterfactual && own && !Regex.IsMatch(before, @"\b(?:not|never|don't|do not|can't|cannot|won't)\b", Rx) &&
            (Regex.IsMatch(before, @"\b(?:can|will|might|gonna|going to|want to|wanna)\b", Rx) || SelfRangePrefix(before)))
            return new("available", "Available for planned use");
        if (Regex.IsMatch(before, @"\b(?:will|would|should|could|can|gonna|going to|want to|wanna|don't|do not|hasn't|has not|haven't|have not|never)\s+(?:(?:use|cast)\s+)?$", Rx)) return null;
        // Quantified post-fight shorthand is one collective report, not N guessed owners.
        if (QuantifiedSpentSpells(clause, spell)) return new("used", "Used (collective report)");
        if (Regex.IsMatch(before, @"^\s*(?:(?:hey|yeah|oh|yep|okay)[,\s]+)*(?:that's|that is|that was)\s+(?:[\p{L}'-]+\s+){0,3}$", Rx) &&
            !Regex.IsMatch(after, @"^\s+(?:icon|animation|ability|name|button|cooldown|tooltip)\b", Rx))
            return new("used", "Used (event shorthand)");
        if (Regex.IsMatch(before, @"\b(?:not|never|didn't|did not|wasn't|was not|isn't|is not)\s*$", Rx) ||
            Regex.IsMatch(before, @"\b(?:should|could|would|might)\s+have\s*$", Rx)) return null;
        bool reportedUse = Regex.IsMatch(spell.Match.Value, @"(?:ed|ing)$", Rx) ||
            Regex.IsMatch(before, @"\b(?:used|missed|whiffed|wasted|burned|burnt|blew|popped|cast|had to)\s+(?:(?:his|her|their|my|an?|the)\s+)?$", Rx) ||
            Regex.IsMatch(after, @"^\s+(?:used|burned|burnt)\b", Rx);
        if (reportedUse && (Regex.IsMatch(before, @"\b(?:should|could|would|might)\s+have(?:\s+(?:just|already|used|cast|wasted|missed|my|his|her|their|an?|the))*\s*$", Rx) ||
            Regex.IsMatch(before, @"\b(?:will|gonna|going to|want to|wanna|need to|don't|do not)\s+(?:(?:use|cast|my|his|her|their|an?|the)\s+)*$", Rx) ||
            Regex.IsMatch(clause, @"^\s*(?:please\s+)?cast\b", Rx))) return null;
        if (reportedUse) return new("used", "Used");
        if (Regex.IsMatch(after, @"\b(?:back\s+)?up\s+yet\b", Rx)) return null;
        if (Regex.IsMatch(before, @"\b(?:has|have|got|i've got)\s+(?:(?:his|her|their|my|another|all|both|an?|the|still|already)\s+)*$", Rx) ||
            Regex.IsMatch(after, @"^\s*(?:'s\s+|(?:is|was)\s+)?(?:up|ready|back|available)\b", Rx)) return new("available", "Available");
        return null;
    }

    private static bool SelfRangePrefix(string before) => Regex.IsMatch(before,
        @"(?:^|\b(?:yeah|well|okay|oh)[,\s]+)\s*(?:(?:yeah|well|okay|oh)[,\s]+)*(?:(?:i'm|i am)\s+)?(?:almost\s+)?in\s+range\s+(?:for|to\s+use)\s+(?:my\s+)?$", Rx);

    private static bool SelfSpellOwner(string before)
    {
        before = Regex.Replace(before, @"\b(?:uh|um)\b[,\s]*", " ", Rx);
        before = before.Replace(',', ' ');
        return SelfRangePrefix(before) || Regex.IsMatch(before,
        @"\bmy\s*$|\bi(?:'ve|'m|'ll| am)?\s+(?:(?:just|already|still|another|had|to|have|got|use|used|missed|wasted|burned|burnt|cast|no|not|don't|do not|an?|the|can|will|might|gonna|going to|want to|wanna)\s+)*$|" +
        @"\bi(?:'m|\s+am)?\s+(?:(?:just|still|maybe|probably|also)\s+)*(?:(?:can|will|might|gonna|going to|want to|wanna)\s+)+" +
        @"(?:(?:come|join|help|run|sprint|walk|go|back|open|escape|engage|dive|leave|get|here|there|in|on|up|down|out|over|with|for|and|then|just|my|the|an?|to)\s+){1,12}$", Rx);
    }

    private static bool QuantifiedSpentSpells(string clause, SpellMention spell) =>
        Regex.IsMatch(spell.Match.Value, @"^(?:flashes|ults|ultimates)$", Rx) && Regex.IsMatch(clause,
            @"^\s*(?:(?:hey|yeah|okay|oh)[,\s]+)*(?:that's|that is|that was|those were)\s+(?:two|three|four|five|[2-5])\s+", Rx);

    private static Resolution UnresolvedSpell(Utterance u, SpellMention spell, string evidence) => new(
        new Player { Champion = "Unresolved target", Role = "unknown", Generic = true },
        "unresolved spell owner", evidence + " No champion or side was assigned.", u.At(spell.Match.Index).Word.Start);

    private static bool AppliedIgnite(SpellMention spell, string before) => spell.Name == "Ignite" &&
        spell.Match.Value.Equals("ignited", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(before,
            @"\b(?:(?:he|she|it)'s|(?:they|we)'re|is|are|was|were|being|been|got|gets?)\s*$", Rx);

    private static List<Utterance> JoinFragments(List<Utterance> input, List<Player> players)
    {
        var output = new List<Utterance>();
        for (int i = 0; i < input.Count; i++)
        {
            var a = input[i];
            while (i + 1 < input.Count)
            {
                var b = input[i + 1];
                string first = a.Text.TrimEnd('.', ',', ';');
                var names = Mentions(a, players);
                bool incomplete = Regex.IsMatch(first, @"(?:^|\s)(?:i|no|is|has|have|doesn't|does not|don't|do not)$", Rx) ||
                    names.Count > 0 && first.Length <= names.Max(n => n.Index + n.Length) + 1 ||
                    Regex.IsMatch(first, @"\b(?:top|bot|bottom)$", Rx) && Regex.IsMatch(b.Text, @"^side\b", Rx);
                bool continuation = Regex.IsMatch(b.Text, @"^\s*(?:have|has|no|not|r\b|ult\b|flash\b|(?:should|might|may|could|must|will)\s+(?:be|open|go|come|head|move|roam)|is\b|was\b|helping\b|top\b|bot\b|mid\b|side\b)", Rx);
                var nextNames = Mentions(b, players);
                bool namedNegative = Regex.IsMatch(first, @"^\s*(?:no|without)\s*$", Rx) &&
                    (a.Speaker.Length == 0 || b.Speaker.Length == 0) && nextNames.Count == 1 &&
                    nextNames[0].Index == 0 && Regex.IsMatch(b.Text[(nextNames[0].Index + nextNames[0].Length)..],
                        @"^\s*(?:'s\s+)?(?:ult(?:imate|i)?|r|flash|tp|teleport)(?:\s+(?:either|too|though))?[.!]?\s*$", Rx);
                if (incomplete && (continuation && nextNames.Count == 0 || namedNegative) && !Question(a.Text) &&
                    b.Start >= a.End - .05 && b.Start - a.End <= FragmentGapSeconds && b.End - a.Start <= 12)
                {
                    a = new Utterance { Tokens = a.Tokens.Concat(b.Tokens).OrderBy(t => t.Word.Start).ThenBy(t => t.Order).ToList() };
                    i++;
                }
                else break;
            }
            output.Add(a);
        }
        return output;
    }

    private static Resolution? Resolve(Utterance u, Match match, (int Start, int End) bounds, List<Mention> mentions,
        List<Player> players, IReadOnlyDictionary<string, string> roles, List<Focus> history,
        Dictionary<string, (Resolution Resolution, double Last)> subjects, List<LaneFocus> lanes, Player? abilityOwner = null)
    {
        string text = u.Text, clause = text[bounds.Start..bounds.End];
        string before = text[bounds.Start..match.Index];
        double at = u.At(match.Index).Word.Start;
        bool self = SelfSpellOwner(before);
        if (self)
        {
            string role = CommsSpeechIntervals.AssignedRole(u.Speaker, roles);
            var own = players.Where(p => p.Ally == true && p.Role == role && role != "unknown").ToArray();
            return own.Length == 1 ? new(own[0], "self", "First-person spell ownership/readiness and saved speaker role.", at, true) :
                new(new Player { Champion = "Unidentified caller", Role = "unknown", Ally = true, Generic = true },
                    "unresolved self", "First-person ownership is explicit, but speaker/champion identity is unresolved.", at, true);
        }
        // Status lists can contain names without their own spell predicate:
        // 'no Wukong, no Orianna ult'. Use the local explicit status owner only.
        var statusOwners = mentions.Where(m => m.Index >= bounds.Start && m.Index + m.Length <= match.Index &&
            Regex.IsMatch(text[(m.Index + m.Length)..match.Index], @"^\s*(?:'s\s*)?(?:(?:has|have|still|no|without|doesn't|does not|don't|do not)\s+)*$", Rx) &&
            (Regex.IsMatch(text[bounds.Start..m.Index], @"\b(?:no|without)\s*$", Rx) ||
             Regex.IsMatch(text[(m.Index + m.Length)..match.Index], @"\b(?:has|have|no|without)\b", Rx))).ToArray();
        if (statusOwners.Length == 1) return new(statusOwners[0].Player, "explicit status owner", "Named target immediately owns this spell status.", statusOwners[0].Time);
        if (abilityOwner is not null)
        {
            int localStart = Math.Max(bounds.Start, text.LastIndexOf(',', Math.Max(0, match.Index - 1)) + 1);
            if (!mentions.Any(m => m.Index >= localStart && m.Index < match.Index))
                return new(abilityOwner, "ability", "Named ultimate uniquely identifies its owner in this local status-list segment.", at);
        }
        int entityEnd = bounds.End;
        var followingClause = Regex.Match(text[(match.Index + match.Length)..bounds.End],
            @"\b(?:if|when|unless|because|but)\b|\band\s+(?=(?:we|they|i|he|she|you)\b)", Rx);
        if (followingClause.Success) entityEnd = match.Index + match.Length + followingClause.Index;
        var local = mentions.Where(m => m.Index >= bounds.Start && m.Index < entityEnd).ToArray();
        bool thirdPersonCast = Regex.IsMatch(before, @"\b(?:he|she|they)\b", Rx) &&
            (Regex.IsMatch(match.Value, @"(?:ed|ing)$", Rx) || Regex.IsMatch(before, @"\b(?:used|cast|missed|wasted)\s+(?:(?:his|her|their)\s+)?$", Rx));
        // In "he is ulting on Ahri", Ahri is the recipient, not the ultimate owner.
        if (thirdPersonCast) local = local.Where(m => m.Index < match.Index).ToArray();
        var named = local.Select(m => m.Player).DistinctBy(PlayerKey).ToArray();
        if (named.Length > 1)
        {
            // A conjunction separates targets only when each side has a spell predicate.
            int start = bounds.Start, end = bounds.End;
            string spellWords = @"\b(?:flash(?:ed)?|teleport|tp|ignite|exhaust|cleanse|ghost|barrier|heal|smite|ult(?:imate|i|ed|ing)?|r)\b";
            foreach (Match c in Regex.Matches(clause, @"[,;]|\b(?:and|but)\b", Rx))
            {
                int index = bounds.Start + c.Index;
                if (!Regex.IsMatch(text[bounds.Start..index], spellWords, Rx) ||
                    !Regex.IsMatch(text[(index + c.Length)..bounds.End], spellWords, Rx)) continue;
                if (index < match.Index) start = index + c.Length; else { end = index; break; }
            }
            local = local.Where(m => m.Index >= start && m.Index < end).ToArray();
            named = local.Select(m => m.Player).DistinctBy(PlayerKey).ToArray();
        }
        if (named.Length == 1)
        {
            var mention = local.First(m => PlayerKey(m.Player) == PlayerKey(named[0]));
            return new(named[0], "explicit", "Named champion/player or unambiguous side-qualified role: " + mention.Source + ".", mention.Time);
        }
        if (named.Length > 1 || local.Length > 0) return null;
        // A unique named ultimate can itself identify its owner; generic ult/R cannot.
        if (abilityOwner is not null) return new(abilityOwner, "ability", "Ultimate name uniquely identifies a champion in this game.", at);
        bool pronoun = Regex.IsMatch(clause, @"\b(?:he|she|his|her|him|they|their)\b", Rx);
        bool elliptical = Regex.IsMatch(clause, @"^\s*(?:(?:yeah|oh|well|okay)[, ]+)*(?:no|without)\b", Rx);
        if ((pronoun || elliptical) && u.Speaker.Length > 0 && subjects.TryGetValue(u.Speaker, out var subject) &&
            subject.Resolution.Player.Role != "team" &&
            !(pronoun && subject.Resolution.IsSelf) &&
            at >= subject.Last && at - subject.Last <= RepeatSeconds && at - subject.Resolution.AnchorTime <= ContextSeconds + RepeatSeconds)
        {
            var competing = history.Where(f => f.Time > subject.Last && f.Time <= at).Select(f => PlayerKey(f.Player)).Distinct().ToArray();
            if (competing.All(k => k == PlayerKey(subject.Resolution.Player)))
                return subject.Resolution with { Method = "same-speaker context", Evidence = "Same speaker continues the recently resolved target. " + subject.Resolution.Evidence };
        }
        if (!pronoun) return null;
        var recent = history.Where(f => f.Player.Role != "team" && f.Time <= at && at - f.End <= ContextSeconds).ToArray();
        var candidates = recent.Select(f => f.Player).DistinctBy(PlayerKey).ToArray();
        if (candidates.Length == 1)
        {
            var source = recent.OrderByDescending(f => f.Time).First();
            return new(candidates[0], "local context", $"Unambiguous topic within {ContextSeconds:0}s of the supporting statement: “{source.Text}” (audio {source.Time:F2}s).", Math.Min(at, source.End));
        }
        if (candidates.Length > 1) return null;
        // Lane context is weaker than a named target: require a recent gank/look proposal AND
        // a response from that lane's player. A bare geographical mid/top/bot word is insufficient.
        string ownRole = CommsSpeechIntervals.AssignedRole(u.Speaker, roles);
        var relevant = lanes.Where(l => l.Time <= at && at - l.Time <= ContextSeconds && l.Role == ownRole).ToArray();
        var opponents = players.Where(p => p.Ally == false && p.Role == ownRole).ToArray();
        if (relevant.Length > 0 && opponents.Length == 1)
        {
            var lane = relevant[^1];
            return new(opponents[0], "lane response", "Lane player's status reply to recent gank/look proposal: “" + lane.Text + "”.", lane.Time);
        }
        return null;
    }

    private static Player TeamTarget(bool ally, List<Player> players)
    {
        var teams = players.Where(p => p.Ally == ally).Select(p => p.Team).Distinct().ToArray();
        return new Player { Champion = ally ? "Allied team" : "Enemy team", Role = "team", Ally = ally,
            Team = teams.Length == 1 ? teams[0] : ally ? -2 : -1, Generic = true };
    }

    private static (bool Applies, Resolution? Resolution) ResolveGroup(Utterance u, SpellMention spell,
        (int Start, int End) bounds, List<Player> players, List<Focus> history)
    {
        string clause = u.Text[bounds.Start..bounds.End];
        string owner = u.Text[bounds.Start..spell.Match.Index];
        if (SelfSpellOwner(owner)) return (false, null);
        if (QuantifiedSpentSpells(clause, spell))
            return (true, new(new Player { Champion = "Players (side unresolved)", Role = "team", Generic = true },
                "unresolved collective report", "Quantified spell-use report counts once; neither individual owners nor team side are explicit.",
                u.At(spell.Match.Index).Word.Start));
        bool enemy = Regex.IsMatch(clause, @"\b(?:enemy|opposing|their)\s+(?:team|bot\s*lane|duo)\b", Rx);
        bool ally = Regex.IsMatch(clause, @"\b(?:our|allied|my)\s+(?:team(?:mates)?|bot\s*lane|duo)\b|^\s*(?:(?:yeah|okay|well)[, ]+)*we(?:'ve| have| got| don't| do not)\b", Rx);
        bool pluralThey = Regex.IsMatch(clause, @"^\s*(?:(?:yeah|okay|well|oh)[, ]+)*they(?:'ve| have| got| don't| do not| used| burned)\b", Rx);
        bool pluralSpell = spell.Bundle || Regex.IsMatch(spell.Match.Value, @"(?:flashes|ults|ultimates)$", Rx);
        if (!enemy && !ally && !(pluralThey && pluralSpell)) return (false, null);
        if (enemy && ally) return (true, null);
        double at = u.At(spell.Match.Index).Word.Start;
        if (!enemy && !ally)
        {
            var sides = history.Where(f => f.Time <= at && at - f.End <= ContextSeconds && f.Player.Ally.HasValue)
                .Select(f => f.Player.Ally!.Value).Distinct().ToArray();
            if (sides.Length > 1) return (true, null);
            ally = sides.Length == 1 && sides[0];
            enemy = !ally;
        }
        return (true, new Resolution(TeamTarget(ally, players), "team group",
            ally ? "Explicit allied plural/team reference; counted once without assigning individual spell owners."
                : "Opposing-team plural/team shorthand; counted once without inventing individual spell owners.", at));
    }

    private static string FindingScope(Utterance u, Resolution resolution, List<Player> players,
        IReadOnlyDictionary<string, string> roles)
    {
        if (resolution.IsSelf) return "Self";
        var target = resolution.Player;
        if (target.Ally == false) return "Enemy";
        if (target.Ally != true) return "Unknown";
        string role = CommsSpeechIntervals.AssignedRole(u.Speaker, roles);
        var own = players.Where(p => p.Ally == true && p.Role == role && role != "unknown").ToArray();
        return own.Length == 1 && PlayerKey(own[0]) == PlayerKey(target) ? "Self" : "Ally";
    }

    private static (string Role, string Method) CallSpeaker(Utterance u, IReadOnlyDictionary<string, string> roles)
    {
        string role = CommsSpeechIntervals.AssignedRole(u.Speaker, roles);
        if (u.Speaker.Length == 0 || u.Tokens.Any(t => UnknownSpeaker(t.Speaker)))
            return (u.Overlap ? "overlap" : "unknown", "unresolved fragment attribution");
        if (!u.Overlap) return (role, "consistent word speaker");
        int anchors = u.Tokens.Count(t => !t.Word.OverlappingSpeech && CommsSpeechIntervals.IsTimedWord(t.Word) &&
            !Regex.IsMatch(t.Text, @"^(?:yeah|yes|okay|ok|well|oh|ah|um|uh|hmm)[,.!?]*$", Rx));
        return anchors >= 2 ? (role, "consistent speaker with non-overlap word anchors") : ("overlap", "unresolved overlapping speaker");
    }

    private static string TrackingFacts(string text) => string.Join("|", Regex.Matches(text,
        @"\b(?:top(?:\s*side)?|mid(?:lane)?|bot(?:\s*side|tom)?|river|blue|red|wolves|raptors|krugs|gromp|scuttle|crab|dragon|drake|baron|rift|herald|grubs|here|there|missing|gank(?:ing|ed)?|path(?:ing)?|clear(?:ing)?|recall(?:ing|ed)?|freez(?:e|ing)|(?:three|3)[ -]?camp(?:ed|ing)?)\b", Rx)
        .Cast<Match>().Select(m => Regex.Replace(m.Value.ToLowerInvariant(), @"\s+", "")).Distinct().OrderBy(s => s, StringComparer.Ordinal));

    private static (string Text, string Kind)? TrackingPredicate(string clause)
    {
        if (Question(clause) || Regex.IsMatch(clause, @"^\s*(?:if|can|could|please|let's|lets)\b", Rx) ||
            Regex.IsMatch(clause, @"\b(?:want|need|would like)\s+(?:him|her|them|\w+(?:\s+\w+)?)\s+(?:here|there|to)\b", Rx) ||
            Regex.IsMatch(clause, @"\b(?:i|we)\s+(?:want|need|wanna|will|can|should|could)\b|\b(?:skin|chroma|emote)\b", Rx)) return null;
        string? kind = null;
        if (Regex.IsMatch(clause, @"\bi(?:'m| am)\s+(?:shooting|warding|checking|scouting|sweeping)\b", Rx) &&
            Regex.IsMatch(clause, @"\b(?:see|find|check)\s+where\b", Rx)) return null;
        bool camp = Regex.IsMatch(clause, @"\b(?:wolves|raptors|krugs|gromp|scuttle|crab|dragon|drake|baron|rift|herald|grubs)\b|\b(?:on|at|taking|clearing|stealing|invading)\s+(?:(?:his|her|their|our|the)\s+)?(?:blue|red)(?:\s+buff)?\b|\b(?:blue|red)\s+(?:buff|camp)\b", Rx);
        bool zone = Regex.IsMatch(clause, @"\b(?:top\s*side|bot\s*side|bottom\s*side|blue\s*side|red\s*side|river|tri[- ]?bush|jungle entrance)\b", Rx);
        bool movement = Regex.IsMatch(clause, @"\b(?:pathing|patting|rotating|heading|moving|walking|crossing)\b", Rx) &&
            (Regex.IsMatch(clause, @"\b(?:to|toward|towards|through|from|into|across)\b", Rx) ||
                Regex.IsMatch(clause, @"\b(?:pathing|patting|heading|rotating)\s+(?:top|mid|bot|bottom)\b", Rx)) &&
            Regex.IsMatch(clause, @"\b(?:top|mid|bot|bottom|river|jungle|blue|red|base|dragon|baron)\b", Rx);
        bool jungleActivity = Regex.IsMatch(clause, @"\b(?:(?:three|3)[ -]?camp(?:ed|ing)?|skipping\s+camps|counter[- ]?jungl(?:e|ing)|invad(?:e|ing))\b", Rx);
        bool inferred = Regex.IsMatch(clause, @"\b(?:should\s+be|must\s+be|probably|likely)\b", Rx) &&
            Regex.IsMatch(clause, @"\b(?:top|mid|bot|bottom|base|jungle)\b", Rx);
        if (camp) kind = "Camp or objective";
        else if (movement) kind = "Route or rotation";
        else if (jungleActivity) kind = "Jungle activity";
        else if (zone || inferred) kind = "Map position or inference";
        if (kind is null) return null;
        // A bare lane sighting, gank on the caller, wave help, here/there or missing call
        // does not establish route/camp/map tracking by itself.
        return (clause.Trim().TrimEnd('.', '!', '?'), kind);
    }

    private static List<CommsAnalysisFinding> DetectCallouts(IEnumerable<WordResult> words,
        IReadOnlyDictionary<string, string> roles, List<Player> players, LeagueAliasCatalog catalog,
        double? offset, double? duration, CancellationToken token)
    {
        var utterances = JoinFragments(DetectionUtterances(words), players);
        var history = new List<Focus>();
        var lanes = new List<LaneFocus>();
        var subjects = new Dictionary<string, (Resolution Resolution, double Last)>(StringComparer.Ordinal);
        var findings = new List<CommsAnalysisFinding>();
        var last = new Dictionary<string, CommsAnalysisFinding>(StringComparer.Ordinal);
        // Compound ability+Flash shorthand may ground a following pronoun, but is not
        // automatically a cast. Owners and aliases come from the current roster/catalog.
        var signatures = players.SelectMany(p => catalog.Abilities(p.ChampionId).Where(a => a.Slot == "R")
            .SelectMany(a => catalog.Names(a, a.Name)).Where(n => n.Length >= 4 && !n.Contains(' '))
            .Select(n => (Player: p, Name: n)))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(p => PlayerKey(p.Player)).Distinct().Count() == 1)
            .Select(g => g.First()).ToArray();
        foreach (var u in utterances)
        {
            token.ThrowIfCancellationRequested();
            history.RemoveAll(f => f.End < u.Start - ContextSeconds);
            lanes.RemoveAll(l => l.Time < u.Start - ContextSeconds);
            var mentions = Mentions(u, players);
            string text = u.Text;
            var spells = SpellMentions(text, players, catalog).OrderBy(s => s.Match.Index).ThenByDescending(s => s.Match.Length)
                .DistinctBy(s => (s.Match.Index, s.Category, s.Name)).ToArray();
            CallState? inherited = null;
            SpellMention? previous = null;
            Resolution? previousTarget = null;
            foreach (var spell in spells)
            {
                var bounds = ClauseBounds(text, spell.Match);
                string clause = text[bounds.Start..bounds.End];
                string before = text[bounds.Start..spell.Match.Index];
                string after = text[(spell.Match.Index + spell.Match.Length)..bounds.End];
                string stateBefore = before;
                // Champion-qualified spell names may sit between a use/status
                // verb and 'ult'; remove only known name spans for state parsing.
                foreach (var m in mentions.Where(m => m.Index >= bounds.Start && m.Index + m.Length <= spell.Match.Index).OrderByDescending(m => m.Index))
                    stateBefore = stateBefore.Remove(m.Index - bounds.Start, m.Length).Insert(m.Index - bounds.Start, new string(' ', m.Length));
                stateBefore = Regex.Replace(stateBefore, @"\b(?:uh|um)\b[,\s]*", " ", Rx);
                stateBefore = Regex.Replace(stateBefore, @"\s+", " ");
                var state = State(text, spell, clause, stateBefore, after);
                if (state is null && previous is not null && inherited is not null && previousTarget is not null &&
                    previous.Match.Index >= bounds.Start && previous.Match.Index + previous.Match.Length <= spell.Match.Index &&
                    Regex.IsMatch(text[(previous.Match.Index + previous.Match.Length)..spell.Match.Index], @"^(?:\s|,|and\b|or\b|no\b|my\b|his\b|her\b|their\b)+$", Rx)) state = inherited;
                var group = ResolveGroup(u, spell, bounds, players, history);
                var resolution = group.Applies ? group.Resolution : Resolve(u, spell.Match, bounds, mentions, players, roles, history, subjects, lanes, spell.AbilityOwner);
                bool namedSelfCast = state is not null && spell.Category == "Ultimate" && Regex.IsMatch(stateBefore,
                    @"\bi(?:'ve| have)?\s+(?:(?:just|also|already)\s+)*(?:used|missed|whiffed|wasted|cast)\s+(?:the\s+)?$", Rx);
                if (namedSelfCast)
                {
                    state = new("used", "Used");
                    string ownRole = CommsSpeechIntervals.AssignedRole(u.Speaker, roles);
                    var own = players.Where(p => p.Ally == true && p.Role == ownRole && ownRole != "unknown").ToArray();
                    resolution = new(own.Length == 1 ? own[0] : new Player { Champion = "Unidentified caller", Role = "unknown", Ally = true, Generic = true },
                        "self named-ability cast", "First-person use of an ultimate; a named ability source is not assumed to be the caster (for example a stolen ultimate).", u.At(spell.Match.Index).Word.Start, true);
                }
                if (state is not null && spell.Category == "Ultimate" && Regex.IsMatch(spell.Match.Value, @"ulted$", Rx) &&
                    Regex.IsMatch(before, @"\b(?:got|was|were|am|is|being|been)\s+(?:(?:solo|just)\s+)*$", Rx))
                {
                    var by = Regex.Match(after, @"^\s+by\s+", Rx);
                    var casters = by.Success ? mentions.Where(m => m.Index >= spell.Match.Index + spell.Match.Length + by.Length && m.Index < bounds.End).ToArray() : [];
                    resolution = casters.Select(m => m.Player).DistinctBy(PlayerKey).Count() == 1
                        ? new(casters[0].Player, "explicit caster", "The speaker was a recipient; the ultimate caster was named after 'by'.", casters[0].Time)
                        : UnresolvedSpell(u, spell, "An ultimate affected a player, but the caster is unresolved.");
                    state = new("used", "Used on a target");
                }
                // Passive effect reports identify the recipient, not who spent Ignite.
                // Keep the useful call without charging the affected enemy's summoner.
                if (state is not null && AppliedIgnite(spell, before))
                {
                    state = new("applied", "Ignite applied; caster unresolved");
                    resolution = UnresolvedSpell(u, spell, "An Ignite effect was reported; the affected player's identity does not establish the caster.");
                }
                if (!group.Applies && state is not null && resolution is null && previousTarget is not null && inherited == state &&
                    previous is not null && previous.Match.Index >= bounds.Start) resolution = previousTarget;
                if (state is not null && resolution is null && !spell.NeedsContext)
                    resolution = UnresolvedSpell(u, spell, "An explicit spell status/use/timer survived, but its owner could not be resolved from reliable local context.");
                if (state is not null && resolution is not null)
                {
                    bool ordinaryWord = spell.NeedsContext && state.Kind == "used" &&
                        !Regex.IsMatch(clause, @"\bsummoner(?:s| spell)?\b", Rx) &&
                        !Regex.IsMatch(before, @"\b(?:used|burned|burnt|my|his|her|their)\s*$", Rx);
                    int? spellId = spell.Category == "Summoner" && !spell.Bundle ? catalog.Summoner(null, spell.Name)?.Id : null;
                    bool impossibleSelfCast = resolution.IsSelf && state.Kind == "used" && spellId.HasValue &&
                        resolution.Player.SpellIds.Count == 2 && !resolution.Player.SpellIds.Contains(spellId.Value);
                    if (!ordinaryWord && !impossibleSelfCast)
                        AddFinding(u, spell.Match.Index, spell.Category, spell.Name, state, resolution, spell.Bundle);
                }
                previous = spell; inherited = state; previousTarget = resolution;
            }
            foreach (var roam in RoamCandidates(u, mentions, players, roles, history, subjects, lanes))
                AddFinding(u, roam.Index, "Roam", "Roam", new(roam.Kind, roam.Label), roam.Resolution, false);
            if (offset.HasValue)
            {
                // Tracking is evaluated per sentence, with local mention and time grounding.
                foreach (Match sentence in Regex.Matches(text, @"[^.!?;]+[.!?;]?", Rx))
                {
                    string clause = sentence.Value;
                    var predicate = TrackingPredicate(clause);
                    if (predicate is not { } tracking) continue;
                    var local = mentions.Where(m => m.Index >= sentence.Index && m.Index < sentence.Index + sentence.Length).ToArray();
                    var target = local.Select(m => m.Player).DistinctBy(PlayerKey).ToArray();
                    Resolution? resolution = null;
                    int index = sentence.Index;
                    if (target.Length == 1 && target[0].Ally == false && target[0].Role == "jungle")
                    {
                        var m = local[0]; index = m.Index;
                        resolution = new(target[0], "explicit", "Explicit enemy jungler plus location/activity.", m.Time);
                    }
                    else if (target.Length == 0)
                    {
                        var pronoun = Regex.Match(clause, @"\b(?:he|she|they|his|her|their)\b", Rx);
                        if (pronoun.Success)
                        {
                            index = sentence.Index + pronoun.Index;
                            // Resolve expects an index in the utterance, so find the same token at its absolute position.
                            var absolute = Regex.Matches(text, @"\b\w+\b").Cast<Match>().First(m => m.Index == index);
                            resolution = Resolve(u, absolute, (sentence.Index, sentence.Index + sentence.Length), mentions, players, roles, history, subjects, lanes);
                        }
                    }
                    if (resolution is not null && resolution.Player.Ally == false && resolution.Player.Role == "jungle")
                    {
                        double game = u.At(index).Word.Start + offset.Value;
                        if (game >= 0)
                            AddFinding(u, index, "Jungle <15:00", "Tracking", new("tracking", tracking.Text), resolution, false, tracking.Kind);
                    }
                }
            }
            // Publish identity evidence only; an inferred pronoun does not extend its own global expiry.
            foreach (var m in mentions) history.Add(new(m.Player, m.Time, u.End, text, u.Speaker));
            foreach (Match team in Regex.Matches(text, @"\b(?<side>our|allied|my|enemy|opposing|their)\s+(?:team(?:mates)?|bot\s*lane|duo)\b", Rx))
                history.Add(new(TeamTarget(Regex.IsMatch(team.Groups["side"].Value, @"^(?:our|allied|my)$", Rx), players), u.At(team.Index).Word.Start, u.End, text, u.Speaker));
            foreach (var signature in signatures)
                foreach (Match m in Regex.Matches(text, Phrase(signature.Name + "flash"), Rx))
                    history.Add(new(signature.Player, u.At(m.Index).Word.Start, u.End, text, u.Speaker));
            foreach (Match m in Regex.Matches(text, @"\b(?:look(?:ing)?(?:\s+at)?|gank(?:ing)?|dive)\s+(?<lane>top|mid|bot|bottom)\b", Rx))
                lanes.Add(new(m.Groups["lane"].Value is "bot" or "bottom" ? "adc" : m.Groups["lane"].Value.ToLowerInvariant(), u.At(m.Index).Word.Start, text));
        }
        // Keep self statements available while resolving nearby subjects, but
        // exclude the caller's own spells from reported callouts and counts.
        return findings.Where(f => !IsSpellCall(f) || f.Scope != "Self")
            .OrderBy(f => f.AudioSeconds).ThenBy(f => f.Category, StringComparer.Ordinal).ToList();

        void AddFinding(Utterance u, int index, string category, string spell, CallState state, Resolution resolution, bool bundle, string trackingKind = "")
        {
            double at = u.At(index).Word.Start;
            var p = resolution.Player;
            var attribution = CallSpeaker(u, roles);
            string role = attribution.Role;
            var finding = new CommsAnalysisFinding
            {
                AudioSeconds = at, GameSeconds = offset.HasValue ? at + offset.Value : null, Role = role, SpeakerId = u.Speaker, SpeakerAttribution = attribution.Method,
                Category = category, Spell = spell, Target = p.Champion, TargetResolution = resolution.Method,
                Scope = FindingScope(u, resolution, players, roles), TargetIsGroup = p.Role == "team", TrackingKind = trackingKind,
                Call = state.Label, CallKind = state.Kind, TimerSeconds = state.Seconds, GenericSummonerBundle = bundle,
                Text = u.Text, UsesCollapsedTiming = u.Collapsed,
                Uncertain = p.Generic || Regex.IsMatch(u.Text, @"\b(?:think|maybe|might|probably|perhaps|should|not sure)\b", Rx),
            };
            if (!InGame(finding, duration)) return;
            string identity = u.Identity.Length > 0 ? u.Identity : "unknown:" + u.Start.ToString("R", CultureInfo.InvariantCulture);
            string key = identity + "|" + PlayerKey(p) + "|" + category + "|" + spell;
            if (category == "Summoner")
            {
                string prefix = identity + "|" + PlayerKey(p) + "|Summoner|";
                var related = last.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal) &&
                    at >= pair.Value.AudioSeconds && at - pair.Value.AudioSeconds <= RepeatSeconds && pair.Value.CallKind == state.Kind).ToArray();
                if (bundle && related.Any(pair => !pair.Value.GenericSummonerBundle)) return;
                if (!bundle)
                    foreach (var pair in related.Where(pair => pair.Value.GenericSummonerBundle))
                    { findings.Remove(pair.Value); last.Remove(pair.Key); }
            }
            if (last.TryGetValue(key, out var prior) && at >= prior.AudioSeconds && at - prior.AudioSeconds <= RepeatSeconds &&
                (category == "Jungle <15:00" && TrackingFacts(prior.Call) == TrackingFacts(state.Label) ||
                    category == "Roam" && prior.CallKind == state.Kind && prior.Call == state.Label ||
                    category is not ("Jungle <15:00" or "Roam") && prior.CallKind == state.Kind &&
                    (state.Kind is not ("cooldown" or "clock") || prior.TimerSeconds == state.Seconds || state.Kind == "cooldown" &&
                        Math.Abs(prior.AudioSeconds + (prior.TimerSeconds ?? 0) - at - (state.Seconds ?? 0)) <= 2)))
            {
                if (!prior.Text.Contains(u.Text, StringComparison.Ordinal)) prior.Text += "\n" + u.Text;
                prior.UsesCollapsedTiming |= u.Collapsed;
                return;
            }
            finding.Subject = p.Champion + " · " + (category is "Jungle <15:00" or "Roam" ? state.Label : spell + " · called " + state.Label.ToLowerInvariant());
            finding.Evidence = "Callout scope: " + ScopeName(finding.Scope) + ". " + resolution.Evidence + " Availability/location describes what was said; saved data does not verify its correctness.";
            if (trackingKind.Length > 0) finding.Evidence += " Tracking basis: " + trackingKind + ".";
            if (u.Overlap) finding.Evidence += " Speaker attribution: " + attribution.Method + ". This call is counted once, never copied to all overlap candidates.";
            if (finding.Uncertain) finding.Evidence += " The speaker expressed uncertainty.";
            if (u.Collapsed) finding.Evidence += " Some supporting words have collapsed timing; no speech duration was added.";
            if (bundle) finding.Evidence += " Unspecified summoner bundle counts once; individual spells are not invented.";
            finding.Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(at.ToString("R", CultureInfo.InvariantCulture) + "|" + key + "|" + state.Kind)))[..24];
            findings.Add(finding); last[key] = finding;
            if (u.Speaker.Length > 0 && !p.Generic) subjects[u.Speaker] = (resolution, at);
        }
    }
}
