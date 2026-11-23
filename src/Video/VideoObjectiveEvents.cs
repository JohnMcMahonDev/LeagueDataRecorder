using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace League_Data_Recorder;

// Recorded captures use VIDEO coordinates. Voice/caption corrections never move them.
internal sealed record VideoObjectiveTakenCue(string Id, double Start, double GameTime,
    string Kind, string Label, string Side, int? TeamId, string Killer, string Target);

internal sealed record VideoObjectiveEventsTimeline(IReadOnlyList<VideoObjectiveTakenCue> Events, string Status)
{
    internal static VideoObjectiveEventsTimeline Read(string gamePath)
    {
        try
        {
            string livePath = gamePath + "_ParsedGameData.json", timingPath = gamePath + "_MediaTiming.json";
            if (!File.Exists(livePath)) return new([], "Objective captures need saved live game events.");
            if (!File.Exists(timingPath)) return new([], "Objective capture positions need saved capture timing.");
            using var live = JsonDocument.Parse(File.ReadAllText(livePath));
            using var clock = JsonDocument.Parse(File.ReadAllText(timingPath));
            double? gameOrigin = Number(clock.RootElement, "GameStartClockSeconds");
            double? videoOrigin = Get(clock.RootElement, "VideoTrimmed").ValueKind == JsonValueKind.True ? gameOrigin : Number(clock.RootElement, "VideoFileStartClockSeconds");
            if (gameOrigin is not double game || videoOrigin is not double video || !double.IsFinite(game - video))
                return new([], "Objective capture positions are unavailable: game/video origins are incomplete.");
            using var post = OptionalPost(gamePath);
            return Build(live.RootElement, post?.RootElement ?? default, game - video);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return new([], "Objective capture events could not be read; playback is still available."); }
    }

    internal static VideoObjectiveEventsTimeline Build(JsonElement live, JsonElement post, double videoShift)
    {
        if (!double.IsFinite(videoShift)) return new([], "Objective capture positions are unavailable.");
        JsonElement source = Get(live, "events");
        if (source.ValueKind == JsonValueKind.Object) source = Get(source, "Events");
        if (source.ValueKind != JsonValueKind.Array) return new([], "No readable saved objective event list.");
        var players = new List<(string Name, int Team)>();
        foreach (var p in Items(Get(live, "players")))
        {
            int? team = Team(Text(p, "team"));
            string name = Text(p, "riotId");
            if (name.Length == 0) name = Text(p, "summonerName");
            if (team.HasValue && name.Length > 0) players.Add((name, team.Value));
        }
        foreach (var t in Items(Get(post, "teams")))
            foreach (var p in Items(Get(t, "players")))
            {
                int? team = Team(Text(t, "teamId"));
                string name = Text(p, "riotId");
                if (name.Length == 0) name = Text(p, "riotIdGameName");
                if (name.Length == 0) name = Text(p, "summonerName");
                string tag = Text(p, "riotIdTagLine");
                if (tag.Length > 0 && !name.Contains('#')) name += "#" + tag;
                if (team.HasValue && name.Length > 0) players.Add((name, team.Value));
            }
        int? ally = VideoFightTimeline.FindAllyTeam(post);
        var cues = new List<VideoObjectiveTakenCue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in source.EnumerateArray())
        {
            string type = Text(e, "EventName");
            string kind = type switch
            {
                "DragonKill" => Text(e, "DragonType").Equals("Elder", StringComparison.OrdinalIgnoreCase) ? "elder" : "dragon",
                "BaronKill" => "baron", "HeraldKill" => "herald", "HordeKill" or "VoidGrubKill" or "VoidGrubsKill" => "grubs",
                "AtakhanKill" => "atakhan", "TurretKilled" or "TurretKill" => "tower", "InhibKilled" or "InhibitorKilled" => "inhibitor",
                "NexusKilled" or "NexusDestroyed" => "nexus", _ => ""
            };
            if (kind.Length == 0 || Number(e, "EventTime") is not double at || at < 0 || !double.IsFinite(at + videoShift)) continue;
            string killer = Text(e, "KillerName"), target = Text(e, "TurretKilled");
            if (target.Length == 0) target = Text(e, "InhibKilled");
            string id = Text(e, "EventID");
            if (id.Length == 0) id = at.ToString("R", CultureInfo.InvariantCulture) + "|" + killer + "|" + target;
            string key = type + "|" + id;
            if (!seen.Add(key)) continue;
            var teamEvidence = new List<int>();
            foreach (string field in new[] { "KillerTeamID", "KillerTeamId", "KillerTeam" })
                if (Team(Text(e, field)) is int explicitTeam) teamEvidence.Add(explicitTeam);
            var exact = players.Where(p => p.Name.Equals(killer, StringComparison.OrdinalIgnoreCase)).Select(p => p.Team).Distinct().ToArray();
            var matches = exact.Length > 0 ? exact : players.Where(p => killer.Length > 0 && p.Name.Split('#')[0].Equals(killer.Split('#')[0], StringComparison.OrdinalIgnoreCase)).Select(p => p.Team).Distinct().ToArray();
            if (matches.Length == 1) teamEvidence.Add(matches[0]);
            if (kind is "tower" or "inhibitor")
            {
                var structure = Regex.Match(target, @"^(?:Turret|Barracks)_T(?<team>[12])_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (structure.Success) teamEvidence.Add(structure.Groups["team"].Value == "1" ? 200 : 100);
            }
            var teams = teamEvidence.Distinct().ToArray();
            int? owner = teams.Length == 1 ? teams[0] : null;
            string side = owner.HasValue ? ally.HasValue ? owner.Value == ally.Value ? "ally" : "enemy" : owner == 100 ? "blue" : "red" : "unknown";
            string label = kind switch
            {
                "dragon" => Text(e, "DragonType") is { Length: > 0 } dragon ? dragon + " dragon" : "Dragon",
                "elder" => "Elder dragon", "baron" => "Baron", "herald" => "Rift Herald", "grubs" => "Void grub",
                "tower" => "Tower destroyed", "inhibitor" => "Inhibitor destroyed", "nexus" => "Nexus destroyed", _ => "Atakhan"
            };
            cues.Add(new("taken|" + key, at + videoShift, at, kind, label, side, owner, killer, target));
        }
        var ordered = cues.OrderBy(e => e.GameTime).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
        return new(ordered, ordered.Length == 0 ? "No objective captures were recorded in the saved events."
            : "Recorded objective captures and structure destructions. Times use the game/video capture clock; team is unknown when evidence is missing or contradictory. Replay pauses or speed changes can affect alignment.");
    }

    private static JsonDocument? OptionalPost(string path)
    {
        try { return File.Exists(path + "_ParsedPostGameData.json") ? JsonDocument.Parse(File.ReadAllText(path + "_ParsedPostGameData.json")) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    private static int? Team(string value) => value.ToUpperInvariant() switch { "100" or "ORDER" or "BLUE" => 100, "200" or "CHAOS" or "RED" => 200, _ => null };
    private static JsonElement Get(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object) return default;
        foreach (var p in value.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return default;
    }
    private static string Text(JsonElement value, string name) => Get(value, name) is var x && x.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? x.ToString() : "";
    private static double? Number(JsonElement value, string name) => double.TryParse(Text(value, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : null;
    private static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
