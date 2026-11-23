using System.Globalization;
using System.Text.Json;

namespace League_Data_Recorder;

internal static class MatchGoldHistory
{
    internal static JsonElement Get(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var value) ? value : default;
    internal static JsonElement Unwrap(JsonElement e) => Get(e, "info").ValueKind == JsonValueKind.Object ? Get(e, "info") : e;
    private static double? Number(JsonElement e, string key) => Get(e, key) is var v && double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : null;
    private static IEnumerable<JsonElement> Items(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : Enumerable.Empty<JsonElement>();

    internal static VideoGoldTimeline Build(JsonElement timeline, JsonElement details, JsonElement post, double shift, int? allyTeam)
    {
        if (!double.IsFinite(shift) || allyTeam is not (100 or 200)) return new([], "Match gold needs capture timing and a known allied side.");
        timeline = Unwrap(timeline); details = Unwrap(details);
        var owners = new Dictionary<int, int>(); bool contradiction = false;
        void Add(JsonElement p, int? side = null)
        {
            if (Number(p, "participantId") is not double id || id < 1 || id != Math.Truncate(id)) return;
            double? team = Number(p, "teamId") ?? side;
            if (team is not (100 or 200)) return;
            if (owners.TryGetValue((int)id, out int prior) && prior != (int)team) contradiction = true;
            else owners[(int)id] = (int)team;
        }
        foreach (var p in Items(Get(details, "participants"))) Add(p);
        foreach (var team in Items(Get(post, "teams")))
            foreach (var p in Items(Get(team, "players"))) Add(p, (int?)Number(team, "teamId"));
        var frames = Items(timeline.ValueKind == JsonValueKind.Array ? timeline : Get(timeline, "frames")).ToArray();
        IEnumerable<JsonElement> People(JsonElement frame) => Get(frame, "participantFrames") is var p && p.ValueKind == JsonValueKind.Object
            ? p.EnumerateObject().Select(v => v.Value) : Items(p);
        // A timeline may carry explicit team IDs even without a separate details response.
        if (owners.Count == 0) foreach (var frame in frames) foreach (var p in People(frame)) Add(p);
        int blue = owners.Values.Count(t => t == 100), red = owners.Values.Count(t => t == 200);
        if (contradiction || blue == 0 || blue != red) return new([], "Match timeline has no complete, unambiguous participant-to-team mapping.");
        var samples = new SortedDictionary<double, VideoGoldCue>();
        foreach (var frame in frames)
        {
            if (Number(frame, "timestamp") is not double ms || ms < 0 || ms > 7 * 24 * 3600 * 1000d) continue;
            var values = new Dictionary<int, double>(); bool valid = true;
            foreach (var p in People(frame))
            {
                if (Number(p, "participantId") is not double id || id < 1 || id != Math.Truncate(id) || !owners.ContainsKey((int)id)
                    || Number(p, "totalGold") is not double gold || gold < 0 || !values.TryAdd((int)id, gold)) { valid = false; break; }
                if (Number(p, "teamId") is double team && team != owners[(int)id]) { valid = false; break; }
            }
            if (!valid || values.Count != owners.Count) continue;
            double own = values.Where(p => owners[p.Key] == allyTeam).Sum(p => p.Value);
            double enemy = values.Where(p => owners[p.Key] != allyTeam).Sum(p => p.Value);
            double at = ms / 1000;
            if (double.IsFinite(own) && double.IsFinite(enemy) && double.IsFinite(at + shift))
                samples[at] = new(at + shift, at, own - enemy, own, enemy);
        }
        var ordered = samples.Values.ToArray();
        var intervals = ordered.Zip(ordered.Skip(1), (a, b) => b.GameTime - a.GameTime).Where(n => n > 0).Order().ToArray();
        double cadence = Number(timeline, "frameInterval") is >= 1000 and <= 180000
            ? Number(timeline, "frameInterval")!.Value / 1000
            : intervals.Length > 0 ? intervals[intervals.Length / 2] : 60;
        return new(ordered, ordered.Length > 0
            ? "Gold from saved match-history timeline frames. Green: allied lead; red: enemy lead. The preview interpolates between measured frames using their actual timestamps; missing frames remain gaps."
            : "The match timeline contains no complete cumulative-gold frames.", Math.Clamp(cadence * 1.75, 15, 180), true);
    }
}
