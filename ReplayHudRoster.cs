using System.Text.Json;
using System.Text.RegularExpressions;

namespace League_Data_Recorder;

internal sealed record ReplayPortraitAnchor(string Side, int Slot);
internal static class ReplayHudRoster
{
    internal static IReadOnlyDictionary<string, ReplayPortraitAnchor> Read(string gamePath, IReadOnlyDictionary<string, string> champions)
    {
        var result = new Dictionary<string, ReplayPortraitAnchor>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var post = File.Exists(gamePath + "_ParsedPostGameData.json")
                ? JsonDocument.Parse(File.ReadAllText(gamePath + "_ParsedPostGameData.json")) : null;
            int? ally = VideoFightTimeline.FindAllyTeam(post?.RootElement ?? default);
            if (!ally.HasValue || !File.Exists(gamePath + "_ParsedGameData.json")) return result;
            using var live = JsonDocument.Parse(File.ReadAllText(gamePath + "_ParsedGameData.json"));
            if (!live.RootElement.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array) return result;
            string side = ally == 100 ? "blue" : "red";
            int slot = 0;
            foreach (var player in players.EnumerateArray())
            {
                string team = Text(player, "team").ToUpperInvariant();
                if (!(ally == 100 ? team is "ORDER" or "BLUE" or "100" : team is "CHAOS" or "RED" or "200")) continue;
                string champion = Key(Text(player, "championName"));
                // Use recorded roster order, rather than assuming roles were sorted.
                var matching = champions.Where(p => Key(p.Value) == champion && champion.Length > 0).ToArray();
                string role = matching.Length == 1 ? matching[0].Key : Text(player, "position").ToLowerInvariant() switch
                {
                    "top" => "top", "jungle" => "jungle", "middle" or "mid" => "mid",
                    "bottom" or "bot" or "adc" => "adc", "utility" or "support" => "support", _ => ""
                };
                if (role.Length > 0 && slot < 5 && !result.ContainsKey(role)) result[role] = new(side, slot);
                slot++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
        return result;
    }
    private static string Key(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");
    private static string Text(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p) ? p.ToString() : "";
}
