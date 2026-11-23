using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace League_Data_Recorder;

internal sealed class CommsAnalysisReport
{
    public const int CurrentVersion = 12;
    public CommsOutlookReport? Outlook { get; set; }
    public int Version { get; set; } = CurrentVersion;
    public bool HasTimedWords { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Game { get; set; } = "";
    public string Transcript { get; set; } = "";
    public double? AudioToGameSeconds { get; set; }
    public string ShareMethod { get; set; } = "Timed word intervals per role. Recovered overlap duration is split equally across its identified speakers; unresolved portions stay in Overlap. Duplicate intervals are unioned by maximum weight. Player shares exclude coach and unassigned speech. Overlap allocation is an estimate.";
    public string FightMethod { get; set; } = "Estimated from kill/assist clusters: gaps at most 12s, cluster span at most 45s, at least 3 identifiable participants per team. Windows include 8s before and 6s after; overlapping windows are merged. Kill-less fights and some one-sided fights are missed.";
    public string ObjectiveMethod { get; set; } = CommsAnalysis.ObjectiveCountingRule;
    public string RoamMethod { get; set; } = CommsAnalysis.RoamCountingRule;
    public string CalloutMethod { get; set; } = "Spell and tracking callouts: " + CommsAnalysis.SummonerCountingRule + " " + CommsAnalysis.UltimateCountingRule + " " + CommsAnalysis.JungleCountingRule +
        " The caller's own spell callouts are excluded. Text fragments are reconstructed independently of speech duration. Named target context expires 8s after the supporting statement. Ambiguous spell owners remain explicitly unresolved; questions, praise and past hypotheticals are excluded. Callouts are attributed once using consistent word speakers and non-overlap anchors; unresolved overlap remains separate. Counts describe what was said, not verified cooldowns or accuracy.";
    public List<string> Notes { get; set; } = new();
    public List<CommsAnalysisPlayer> Players { get; set; } = new();
    public List<CommsFightWindow> Fights { get; set; } = new();
    public List<CommsAnalysisFinding> Findings { get; set; } = new();
}

internal sealed class CommsAnalysisPlayer
{
    public string Role { get; set; } = "";
    public bool HasSpeakerAssignment { get; set; }
    public double Seconds { get; set; }
    public double? Share { get; set; }
    public double? FightSeconds { get; set; }
    public double? FightShare { get; set; }
    public int SummonerCallouts { get; set; }
    public int UltimateCallouts { get; set; }
    public int EnemySummonerCallouts { get; set; }
    public int EnemyUltimateCallouts { get; set; }
    public int EnemyCallouts => EnemySummonerCallouts + EnemyUltimateCallouts;
    public int AllyCallouts { get; set; }
    public int UnknownScopeCallouts { get; set; }
    public int? JungleMentions { get; set; }
    public int ObjectiveCalls { get; set; }
    public int RoamCalls { get; set; }
}

internal sealed class CommsFightWindow
{
    public double Start { get; set; }
    public double End { get; set; }
    public int Kills { get; set; }
    public string Participants { get; set; } = "";

    // Runtime metadata for video review. Keep these internal so the saved
    // comms report format and existing speech/share calculations stay stable.
    internal List<CommsFightParticipant> Fighters { get; set; } = new();
    internal Dictionary<int, int> KillsByTeam { get; set; } = new();
    internal int UnresolvedKills { get; set; }
}

internal sealed record CommsFightParticipant(string Name, string Champion, int ChampionId, int Team, int Kills = 0, int Deaths = 0);

internal sealed class CommsAnalysisFinding
{
    public string Id { get; set; } = "";
    public double AudioSeconds { get; set; }
    public double? AudioEndSeconds { get; set; }
    public double? GameSeconds { get; set; }
    public string Role { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Text { get; set; } = "";
    public string Target { get; set; } = "";
    public string Call { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Spell { get; set; } = "";
    public string CallKind { get; set; } = "";
    public double? TimerSeconds { get; set; }
    public string TargetResolution { get; set; } = "";
    public string SpeakerId { get; set; } = "";
    public string SpeakerAttribution { get; set; } = "";
    public bool Uncertain { get; set; }
    public bool UsesCollapsedTiming { get; set; }
    public bool GenericSummonerBundle { get; set; }
    public string Scope { get; set; } = "Unknown";
    public bool TargetIsGroup { get; set; }
    public string TrackingKind { get; set; } = "";
}

// Local, deterministic analysis. No audio inference, web service or model is required.
internal static partial class CommsAnalysis
{
    internal static readonly string[] Roles = ["top", "jungle", "mid", "adc", "support", "coach", "unknown", "overlap"];
    internal const string SummonerCountingRule = "Summoner Spell Timer Callout: explicit spell availability, use, applied effect or a timer. The caller's own spells are excluded. Unresolved owners/sides remain Unknown; an affected target is not assumed to be the caster. Same-caller repeats within 4s count once; a collective/unspecified summoner bundle counts once.";
    internal const string UltimateCountingRule = "Ult Timer Callout: ultimate availability, reported use or a timer. The caller's own ultimate is excluded. Unresolved owners remain labeled. Same-caller repeats within 4s count once.";
    // The saved category key remains 'Jungle <15:00' for existing filters/exports.
    internal const string JungleCountingRule = "Tracking: enemy jungle routes, camps, objectives, rotations or inferred map positions throughout the game. Bare sightings in lane, here/on-me calls and wave help are excluded. Repeated facts within 4s count once.";
    internal const string EnemyCountingRule = "Enemy callouts report opponents' summoner or ultimate availability, use or timer. A team-wide call counts once; individual targets are not invented.";
    internal static string ScopeName(string scope) => scope switch { "Ally" => "Allied teammate", "Team" => "Team objective", "Unknown" => "Unknown side", _ => scope };
    internal static bool IsSpellCall(CommsAnalysisFinding finding) => finding.Category is "Summoner" or "Ultimate";
    internal static string TopicName(string category) => category switch
    {
        "Summoner" => "Summoner Spell Timer Callout",
        "Ultimate" => "Ult Timer Callout",
        "Jungle <15:00" => "Tracking",
        "Objective" => "Objective call",
        "Roam" => "Roam call",
        _ => category,
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly RegexOptions Rx = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private sealed record Spell(string Name, string Pattern, bool NeedsContext = false);
    private static readonly Spell[] Spells =
    [
        new("Flash", @"\bflash(?:ed|ing|es)?\b"),
        new("Teleport", @"\b(?:teleport(?:ed|ing)?|tp(?:'?ed|'?ing|'d|s)?)\b"),
        new("Ignite", @"\b(?:ignite[ds]?|igniting)\b"),
        new("Exhaust", @"\bexhaust(?:ed)?\b"),
        new("Cleanse", @"\bcleanse[ds]?\b", true),
        new("Ghost", @"\bghost(?:ed|ing)?\b", true),
        new("Barrier", @"\bbarrier\b", true),
        new("Heal", @"\bheal(?:ed)?\b", true),
        new("Smite", @"\bsmit(?:e[ds]?|ing)\b"),
        new("Clarity", @"\bclarity\b", true),
        new("Snowball", @"\bsnowball\b", true),
    ];
    private sealed class Player
    {
        internal string Name = "", Champion = "", Role = "";
        internal int ChampionId, Team;
        internal bool? Ally;
        internal HashSet<int> SpellIds = new();
        internal bool Generic;
        internal string[] Aliases = [];
    }
    private sealed record Kill(double Time, HashSet<Player> Participants, int? KillerTeam, Player? Killer, Player? Victim);
    internal static CommsAnalysisReport Analyze(string gamePath, TranscriptionResult transcript,
        IReadOnlyDictionary<string, string> roles, LeagueAliasCatalog catalog, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var report = new CommsAnalysisReport { Game = Path.GetFileName(gamePath), Transcript = transcript.JsonPath };
        using var post = ReadJson(gamePath + "_ParsedPostGameData.json");
        using var live = ReadOptional(gamePath + "_ParsedGameData.json", report);
        var players = ReadPlayers(post.RootElement, live?.RootElement ?? default, catalog);
        report.AudioToGameSeconds = AudioToGameOffset(gamePath, transcript, report);
        report.Notes.Add("All results are generated automatically. Enemy summoner/ultimate cooldowns and spell casts are not recorded, so callout accuracy and poisoned-comms scores are not evaluated. Availability details describe the speaker's words, not verified game state.");
        if (players.Count(p => p.Ally == false && p.Role == "jungle") != 1)
            report.Notes.Add("Enemy jungle role is unavailable or ambiguous. Only explicit phrases such as 'enemy jungler' can be counted; champion-name tracking is disabled.");
        if (!(transcript.Words ?? []).Any(ValidWord))
            report.Notes.Add("No usable timed words. Generate a timed transcript to analyze speech; transcript text alone is not assigned invented timestamps.");
        if (report.AudioToGameSeconds.HasValue && live is not null)
            report.Fights = FindFights(live.RootElement, players, token);
        else report.Notes.Add("Teamfight share is unavailable without readable live events and audio-to-game timing.");
        if (report.AudioToGameSeconds.HasValue && live is not null && report.Fights.Count == 0)
            report.Notes.Add("No qualifying fight windows were detected. Teamfight share is unavailable, rather than a measured zero.");

        var valid = (transcript.Words ?? []).Where(ValidWord).OrderBy(w => w.Start).ToList();
        report.HasTimedWords = valid.Count > 0;
        var spans = Roles.ToDictionary(r => r, _ => new List<(double Start, double End, double Weight)>());
        var fightSpans = Roles.ToDictionary(r => r, _ => new List<(double Start, double End, double Weight)>());
        double? offset = report.AudioToGameSeconds;
        double? duration = Number(Get(live?.RootElement ?? default, "gameInfo"), "duration")
            ?? Number(Get(post.RootElement, "gameInfo"), "gameLength");
        foreach (var word in valid)
        {
            token.ThrowIfCancellationRequested();
            double start = word.Start, end = word.End;
            if (offset is double shift)
            {
                start = Math.Max(start, -shift);
                if (duration is > 0) end = Math.Min(end, duration.Value - shift);
            }
            if (end <= start) continue;
            foreach (var part in CommsSpeechIntervals.Allocate(word, roles))
            {
                spans[part.Role].Add((start, end, part.Weight));
                if (offset is not double toGame) continue;
                foreach (var fight in report.Fights)
                {
                    double a = Math.Max(start + toGame, fight.Start), b = Math.Min(end + toGame, fight.End);
                    if (b > a) fightSpans[part.Role].Add((a, b, part.Weight));
                }
            }
        }
        bool fightAvailable = offset.HasValue && report.Fights.Count > 0;
        var seconds = spans.ToDictionary(p => p.Key, p => CommsSpeechIntervals.Seconds(p.Value));
        var fightSeconds = fightSpans.ToDictionary(p => p.Key, p => CommsSpeechIntervals.Seconds(p.Value));
        double playerSeconds = Roles.Take(5).Sum(r => seconds[r]);
        double playerFightSeconds = Roles.Take(5).Sum(r => fightSeconds[r]);
        var assignedRoles = roles.Keys.Select(s => CommsSpeechIntervals.AssignedRole(s, roles)).ToHashSet();
        report.Players = Roles.Select((r, i) => new CommsAnalysisPlayer
        {
            Role = r, Seconds = seconds[r], HasSpeakerAssignment = assignedRoles.Contains(r),
            Share = i < 5 && assignedRoles.Contains(r) && playerSeconds > 0 ? seconds[r] / playerSeconds * 100 : null,
            FightSeconds = fightAvailable ? fightSeconds[r] : null,
            FightShare = fightAvailable && i < 5 && assignedRoles.Contains(r) && playerFightSeconds > 0 ? fightSeconds[r] / playerFightSeconds * 100 : null,
            JungleMentions = offset.HasValue ? 0 : null,
        }).ToList();

        report.Findings = DetectCallouts(transcript.Words ?? [], roles, players, catalog, offset, duration, token);
        report.Findings.AddRange(DetectObjectiveCalls(transcript.Words ?? [], roles, offset, token));
        foreach (var row in report.Players)
        {
            row.SummonerCallouts = report.Findings.Count(f => f.Role == row.Role && f.Category == "Summoner");
            row.UltimateCallouts = report.Findings.Count(f => f.Role == row.Role && f.Category == "Ultimate");
            row.EnemySummonerCallouts = report.Findings.Count(f => f.Role == row.Role && f.Category == "Summoner" && f.Scope == "Enemy");
            row.EnemyUltimateCallouts = report.Findings.Count(f => f.Role == row.Role && f.Category == "Ultimate" && f.Scope == "Enemy");
            row.AllyCallouts = report.Findings.Count(f => f.Role == row.Role && IsSpellCall(f) && f.Scope == "Ally");
            row.UnknownScopeCallouts = report.Findings.Count(f => f.Role == row.Role && IsSpellCall(f) && f.Scope == "Unknown");
            if (row.JungleMentions.HasValue) row.JungleMentions = report.Findings.Count(f => f.Role == row.Role && f.Category == "Jungle <15:00");
            row.ObjectiveCalls = report.Findings.Count(f => f.Role == row.Role && f.Category == "Objective");
            row.RoamCalls = report.Findings.Count(f => f.Role == row.Role && f.Category == "Roam");
        }
        report.Findings = report.Findings.OrderBy(f => f.AudioSeconds).ThenBy(f => f.Category, StringComparer.Ordinal).ToList();
        report.Outlook = CommsOutlook.Read(gamePath, transcript, roles);
        return report;
    }

    private static bool InGame(CommsAnalysisFinding finding, double? duration) =>
        finding.GameSeconds is not double time || time >= 0 && (duration is not > 0 || time <= duration);
    private static bool ValidWord(WordResult w) => CommsSpeechIntervals.IsTimedWord(w);
    private static List<Player> ReadPlayers(JsonElement post, JsonElement live, LeagueAliasCatalog catalog)
    {
        var result = new List<Player>();
        int? local = (int?)Number(Get(post, "localPlayer"), "teamId");
        foreach (var team in Items(Get(post, "teams")))
        {
            int id = (int)(Number(team, "teamId") ?? 0);
            bool? ally = Boolean(team, "isPlayerTeam") ?? (local.HasValue ? id == local : null);
            foreach (var p in Items(Get(team, "players")))
            {
                string champion = Text(p, "championName");
                int champId = (int)(Number(p, "championId") ?? 0);
                var entry = catalog.Champion(champId, champion);
                champion = entry?.Name ?? champion;
                string name = Text(p, "riotId");
                if (name.Length == 0) name = Text(p, "riotIdGameName");
                if (name.Length == 0) name = Text(p, "summonerName");
                string tag = Text(p, "riotIdTagLine");
                if (tag.Length > 0 && !name.Contains('#')) name += "#" + tag;
                string position = Text(p, "detectedTeamPosition");
                if (position.Length == 0) position = Text(p, "selectedPosition");
                var player = new Player { Name = name, Champion = champion, ChampionId = entry?.Id ?? champId,
                    Team = id, Ally = ally, Role = VideoCommsTimeline.NormalizeRole(position),
                    Aliases = catalog.RoleNames(entry, champion).Where(a => a.Length >= 3 ||
                        LeagueAliasCatalog.Key(a) == LeagueAliasCatalog.Key(champion) || a.Equals("yi", StringComparison.OrdinalIgnoreCase)).ToArray() };
                foreach (string key in new[] { "spell1Id", "spell2Id" })
                    if (Number(p, key) is > 0 and var spell) player.SpellIds.Add((int)spell);
                result.Add(player);
            }
        }
        // Recover roles from saved live data when post-game fields are absent. Identity must be unique.
        foreach (var p in Items(Get(live, "players")))
        {
            string liveName = Text(p, "riotId"), champion = Text(p, "championName");
            var candidates = result.Where(x => SameName(x.Name, liveName) &&
                LeagueAliasCatalog.Key(x.Champion) == LeagueAliasCatalog.Key(catalog.Champion(null, champion)?.Name ?? champion)).ToArray();
            if (candidates.Length != 1) continue;
            var player = candidates[0];
            if (player.Role == "unknown") player.Role = VideoCommsTimeline.NormalizeRole(Text(p, "position"));
            if (player.SpellIds.Count < 2)
                foreach (string slot in new[] { "summonerSpellOne", "summonerSpellTwo" })
                    if (catalog.Summoner(null, Text(Get(Get(p, "summonerSpells"), slot), "displayName")) is { } entry)
                        player.SpellIds.Add(entry.Id);
        }
        return result;
    }
    private static bool SameName(string a, string b) => a.Length > 0 && b.Length > 0 &&
        (a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.Split('#')[0].Equals(b.Split('#')[0], StringComparison.OrdinalIgnoreCase));

    // Video review needs the same fight windows without a transcript, speaker
    // assignments, a cached analysis report, or the vocabulary/model pipeline.
    internal static List<CommsFightWindow> FindRecordedFights(JsonElement live, JsonElement post = default)
    {
        var players = new List<Player>();
        foreach (var p in Items(Get(live, "players")))
        {
            string name = Text(p, "riotId");
            if (name.Length == 0) name = Text(p, "summonerName");
            int team = Text(p, "team").ToUpperInvariant() switch
            {
                "ORDER" or "100" => 100,
                "CHAOS" or "200" => 200,
                _ => 0,
            };
            if (name.Length == 0 || team == 0) continue;
            players.Add(new Player { Name = name, Champion = Text(p, "championName"), Team = team });
        }
        if (players.Select(p => p.Team).Distinct().Count() < 2)
            throw new InvalidDataException("Both teams' player identities are needed to detect fights.");
        // The live roster supplies the identities used by kill events; post-game
        // champion IDs let the icon cache handle older/internal champion names.
        foreach (var team in Items(Get(post, "teams")))
            foreach (var p in Items(Get(team, "players")))
            {
                string name = Text(p, "riotId");
                if (name.Length == 0) name = Text(p, "riotIdGameName");
                if (name.Length == 0) name = Text(p, "summonerName");
                string tag = Text(p, "riotIdTagLine");
                if (tag.Length > 0 && !name.Contains('#')) name += "#" + tag;
                int id = (int)(Number(team, "teamId") ?? 0);
                var matches = players.Where(x => x.Team == id && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 0) matches = players.Where(x => x.Team == id && SameName(x.Name, name)).ToArray();
                if (matches.Length != 1) continue;
                var player = matches[0];
                player.ChampionId = (int)(Number(p, "championId") ?? 0);
                if (player.Champion.Length == 0) player.Champion = Text(p, "championName");
            }
        return FindFights(live, players, CancellationToken.None);
    }

    private static List<CommsFightWindow> FindFights(JsonElement live, List<Player> players, CancellationToken token)
    {
        Player? Resolve(string name)
        {
            var exact = players.Where(p => name.Length > 0 && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (exact.Length == 1) return exact[0];
            var candidates = players.Where(p => SameName(p.Name, name)).ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }
        var kills = new List<Kill>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ev in Items(Get(live, "events")))
        {
            token.ThrowIfCancellationRequested();
            if (Text(ev, "EventName") != "ChampionKill" || Number(ev, "EventTime") is not double time || time < 0) continue;
            string key = Text(ev, "EventID");
            if (!seen.Add(key.Length > 0 ? key : ev.GetRawText())) continue;
            var participants = new HashSet<Player>();
            foreach (string name in new[] { Text(ev, "KillerName"), Text(ev, "VictimName") }
                .Concat(Items(Get(ev, "Assisters")).Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "")))
                if (Resolve(name) is { } p) participants.Add(p);
            var killer = Resolve(Text(ev, "KillerName"));
            var victim = Resolve(Text(ev, "VictimName"));
            // Do not invent a team kill for a turret, minion, execution or an
            // ambiguous player name. Incomplete attribution has no win color.
            int? killerTeam = killer is { Team: > 0 } && (victim is null || victim.Team != killer.Team) ? killer.Team : null;
            kills.Add(new(time, participants, killerTeam, killer, victim));
        }
        var windows = new List<CommsFightWindow>();
        var cluster = new List<Kill>();
        void Flush()
        {
            if (cluster.Count == 0) return;
            var people = cluster.SelectMany(k => k.Participants).Distinct().ToArray();
            if (people.GroupBy(p => p.Team).Count(g => g.Key > 0 && g.Count() >= 3) >= 2)
                windows.Add(new() { Start = Math.Max(0, cluster[0].Time - 8), End = cluster[^1].Time + 6,
                    Kills = cluster.Count, Participants = string.Join(", ", people.Select(p => p.Champion).OrderBy(s => s, StringComparer.Ordinal)),
                    Fighters = people.Select(p => new CommsFightParticipant(p.Name, p.Champion, p.ChampionId, p.Team,
                        cluster.Count(k => k.KillerTeam.HasValue && k.Killer == p), cluster.Count(k => k.Victim == p))).ToList(),
                    KillsByTeam = cluster.Where(k => k.KillerTeam.HasValue).GroupBy(k => k.KillerTeam!.Value).ToDictionary(g => g.Key, g => g.Count()),
                    UnresolvedKills = cluster.Count(k => !k.KillerTeam.HasValue) });
            cluster.Clear();
        }
        foreach (var kill in kills.OrderBy(k => k.Time))
        {
            if (cluster.Count > 0 && (kill.Time - cluster[^1].Time > 12 || kill.Time - cluster[0].Time > 45)) Flush();
            cluster.Add(kill);
        }
        Flush();
        var merged = new List<CommsFightWindow>();
        foreach (var window in windows)
        {
            if (merged.Count == 0 || window.Start > merged[^1].End) { merged.Add(window); continue; }
            var last = merged[^1];
            last.End = Math.Max(last.End, window.End);
            last.Kills += window.Kills;
            last.Participants = string.Join(", ", (last.Participants + ", " + window.Participants).Split(", ").Distinct());
            // One portrait per player, including repeated kills/deaths across
            // merged windows. A victim is still counted for an execution when
            // the killer could not be identified as a champion.
            last.Fighters = last.Fighters.Concat(window.Fighters).GroupBy(p => (p.Team, p.Name))
                .Select(g => g.First() with { Kills = g.Sum(p => p.Kills), Deaths = g.Sum(p => p.Deaths) }).ToList();
            foreach (var pair in window.KillsByTeam)
                last.KillsByTeam[pair.Key] = last.KillsByTeam.GetValueOrDefault(pair.Key) + pair.Value;
            last.UnresolvedKills += window.UnresolvedKills;
        }
        return merged;
    }
    internal static double? AudioToGameOffset(string gamePath, TranscriptionResult transcript, CommsAnalysisReport report)
    {
        try
        {
            string path = gamePath + "_MediaTiming.json";
            if (!File.Exists(path)) { report.Notes.Add("No saved capture clock: game-clock tracking and teamfight timing are unavailable."); return null; }
            var timing = JsonSerializer.Deserialize<GameMediaTiming>(File.ReadAllText(path), JsonOptions);
            VideoReviewTiming review = VideoReviewTiming.Read(gamePath);
            double? audioOrigin = MediaTimelineOrigins.Audio(timing, review);
            if (timing?.GameStartClockSeconds is not double gameStart || audioOrigin is not double origin ||
                !double.IsFinite(gameStart) || !double.IsFinite(origin) || !double.IsFinite(transcript.AudioTimelineShiftSeconds) ||
                !double.IsFinite(review.AudioTimelineShiftSeconds) || !VideoReviewTiming.ValidCorrection(review.AudioCorrectionSeconds) ||
                !VideoReviewTiming.ValidCorrection(review.CaptionCorrectionSeconds))
            { report.Notes.Add("Saved audio/game origins are incomplete or invalid: game-clock metrics are unavailable."); return null; }
            report.Notes.Add("Game-clock alignment uses capture timestamps and saved review corrections. It is approximate; pauses or accelerated replay capture can change the relationship between audio and game time.");
            return origin - gameStart + review.AudioTimelineShiftSeconds - transcript.AudioTimelineShiftSeconds +
                review.AudioCorrectionSeconds + review.CaptionCorrectionSeconds;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { report.Notes.Add("Game-clock timing unavailable: " + ex.Message); return null; }
    }

    internal static void SaveReport(string gamePath, CommsAnalysisReport report)
    {
        SaveJson(gamePath + "_CommsAnalysis.json", report);
        if (report.Outlook is not null) CommsOutlook.Save(gamePath, report.Outlook);
    }
    internal static void SaveJson<T>(string path, T value)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static JsonDocument ReadJson(string path) { using var reader = new StreamReader(path); return JsonDocument.Parse(reader.ReadToEnd()); }
    private static JsonDocument? ReadOptional(string path, CommsAnalysisReport report)
    {
        try { return File.Exists(path) ? ReadJson(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { report.Notes.Add("Live game data unavailable: " + ex.Message); return null; }
    }
    private static JsonElement Get(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
    private static IEnumerable<JsonElement> Items(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : Enumerable.Empty<JsonElement>();
    private static string Text(JsonElement e, string name) => Get(e, name) is var v && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? v.ToString() : "";
    private static double? Number(JsonElement e, string name) => double.TryParse(Text(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : null;
    private static bool? Boolean(JsonElement e, string name) => bool.TryParse(Text(e, name), out bool b) ? b : null;
}
