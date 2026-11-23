using System.Text.Json;

namespace League_Data_Recorder;

internal sealed record VideoGoldCue(double Start, double GameTime, double Advantage, double AllyGold, double EnemyGold);
internal sealed record VideoGoldTimeline(IReadOnlyList<VideoGoldCue> Samples, string Status, double MaxSampleGapSeconds = 15, bool Interpolate = false)
{
    internal static VideoGoldTimeline Read(string gamePath)
    {
        try
        {
            string livePath = gamePath + "_ParsedGameData.json", timingPath = gamePath + "_MediaTiming.json";
            if (!File.Exists(timingPath)) return new([], "Gold history needs saved capture timing.");
            var timing = JsonSerializer.Deserialize<GameMediaTiming>(File.ReadAllText(timingPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            double? videoOrigin = MediaTimelineOrigins.Video(timing);
            if (timing?.GameStartClockSeconds is not double game || videoOrigin is not double video || !double.IsFinite(game - video))
                return new([], "Gold positions are unavailable because capture timing is incomplete.");
            using var post = Optional(gamePath + "_ParsedPostGameData.json");
            using var details = Optional(MatchTimelineStore.DetailsPath(gamePath));
            using var timeline = Optional(MatchTimelineStore.TimelinePath(gamePath));
            int? ally = VideoFightTimeline.FindAllyTeam(post?.RootElement ?? default);
            var history = MatchGoldHistory.Build(timeline?.RootElement ?? default, details?.RootElement ?? default, post?.RootElement ?? default, game - video, ally);
            if (history.Samples.Count > 0) return history;
            // Some saved/imported post-game responses already include a timeline.
            var embedded = MatchGoldHistory.Get(post?.RootElement ?? default, "timeline");
            history = MatchGoldHistory.Build(embedded, details?.RootElement ?? default, post?.RootElement ?? default, game - video, ally);
            if (history.Samples.Count > 0) return history;
            using var raw = Optional(gamePath + "_PostGameData.json");
            var source = raw?.RootElement ?? default;
            using var expanded = MatchGoldHistory.Get(source, "postGameData") is var payload && payload.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(payload.GetString()!) : null;
            source = expanded?.RootElement ?? source;
            embedded = MatchGoldHistory.Get(source, "timeline");
            if (embedded.ValueKind == JsonValueKind.Undefined) embedded = source;
            history = MatchGoldHistory.Build(embedded, details?.RootElement ?? default, source, game - video, ally);
            if (history.Samples.Count > 0) return history;
            using var live = Optional(livePath);
            return Build(live?.RootElement ?? default, game - video, ally);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return new([], "Team gold history could not be read from the saved game data."); }
    }

    private static JsonDocument? Optional(string path)
    {
        try { return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static VideoGoldTimeline Build(JsonElement live, double shift, int? allyTeam)
    {
        var samples = new SortedDictionary<double, VideoGoldCue>();
        if (allyTeam is not (100 or 200)) return new([], "The allied team is unknown; gold advantage cannot be attributed safely.");
        if (!double.IsFinite(shift)) return new([], "Gold positions need valid capture timing.");
        if (Get(live, "teamGold") is var frames && frames.ValueKind == JsonValueKind.Array)
            foreach (var frame in frames.EnumerateArray())
            {
                double? time = Number(frame, "time"), blue = Number(frame, "blueGold"), red = Number(frame, "redGold");
                if (time is not double at || at < 0 || blue is not double b || red is not double r || b < 0 || r < 0 || !double.IsFinite(at + shift)) continue;
                double own = allyTeam == 100 ? b : r, enemy = allyTeam == 100 ? r : b;
                samples[at] = new(at + shift, at, own - enemy, own, enemy);
            }
        // Some saved datasets contain cumulative per-player gold in snapshots.
        // Only exact, complete frames for both rosters are valid; no inventory,
        // current-wallet, CS, or final-game gold is used to invent a history.
        if (samples.Count == 0 && Get(live, "players") is var players && players.ValueKind == JsonValueKind.Array)
        {
            var roster = players.EnumerateArray().Where(p => Team(Get(p, "team")) is 100 or 200).ToArray();
            var blue = roster.Where(p => Team(Get(p, "team")) == 100).ToArray();
            var red = roster.Where(p => Team(Get(p, "team")) == 200).ToArray();
            if (blue.Length > 0 && blue.Length == red.Length)
            {
                var framesByPlayer = roster.Select(p => Items(Get(p, "timeline")).Where(t => Number(t, "time") is >= 0 && Number(t, "totalGold") is >= 0)
                    .GroupBy(t => Number(t, "time")!.Value).ToDictionary(g => g.Key, g => Number(g.Last(), "totalGold")!.Value)).ToArray();
                foreach (double at in framesByPlayer[0].Keys.Where(t => framesByPlayer.All(p => p.ContainsKey(t))).Order())
                {
                    double b = roster.Select((p, i) => Team(Get(p, "team")) == 100 ? framesByPlayer[i][at] : 0).Sum();
                    double r = roster.Select((p, i) => Team(Get(p, "team")) == 200 ? framesByPlayer[i][at] : 0).Sum();
                    double own = allyTeam == 100 ? b : r, enemy = allyTeam == 100 ? r : b;
                    if (double.IsFinite(at + shift) && double.IsFinite(own - enemy)) samples[at] = new(at + shift, at, own - enemy, own, enemy);
                }
            }
        }
        return new(samples.Values.ToArray(), samples.Count == 0
            ? "Team gold advantage unavailable: this recording has no complete cumulative gold totals for both teams. Local unspent gold cannot determine team advantage."
            : "Allied cumulative gold minus enemy cumulative gold. Green: allied lead; red: enemy lead. Missing team frames are not estimated.");
    }
    private static JsonElement Get(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) ? p : default;
    private static double? Number(JsonElement e, string name) => Get(e, name) is var v && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d) ? d : null;
    private static int? Team(JsonElement e) => e.ToString().ToUpperInvariant() switch { "ORDER" or "BLUE" or "100" => 100, "CHAOS" or "RED" or "200" => 200, _ => null };
    private static IEnumerable<JsonElement> Items(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
