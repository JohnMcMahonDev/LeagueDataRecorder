using System;
using System.IO;
using System.Text.Json;

namespace League_Data_Recorder;

// A per-recording override avoids changing every game in the original session.
internal sealed class RecordingTeamNames
{
    public string Ally { get; set; } = "";
    public string Enemy { get; set; } = "";
    public string GameType { get; set; } = "";
    internal static string PathFor(string gamePath) => gamePath + "_TeamNames.json";
    internal static RecordingTeamNames? ReadOverride(string gamePath)
    {
        string path = PathFor(gamePath);
        return File.Exists(path) ? JsonSerializer.Deserialize<RecordingTeamNames>(File.ReadAllText(path)) ??
            throw new InvalidDataException("Team name overrides are empty.") : null;
    }
    internal static RecordingTeamNames ReadDisplay(string gamePath, string rootFolder)
    {
        var saved = ReadOverride(gamePath);
        using var post = File.Exists(gamePath + "_ParsedPostGameData.json")
            ? ReadJson(gamePath + "_ParsedPostGameData.json") : JsonDocument.Parse("{}");
        var result = new RecordingTeamNames();
        var root = post.RootElement;
        int local = root.TryGetProperty("localPlayer", out var p) && p.TryGetProperty("teamId", out var t) && t.TryGetInt32(out int n) ? n : 0;
        if (root.TryGetProperty("teams", out var teams) && teams.ValueKind == JsonValueKind.Array)
            foreach (var team in teams.EnumerateArray())
            {
                if (!team.TryGetProperty("teamId", out var id) || !id.TryGetInt32(out int teamId)) continue;
                bool? ally = team.TryGetProperty("isPlayerTeam", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? a.GetBoolean() : local > 0 ? teamId == local : null;
                string name = team.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
                if (ally == true) result.Ally = name;
                else if (ally == false) result.Enemy = name;
            }
        var folder = new DirectoryInfo(Path.GetDirectoryName(gamePath)!);
        string[] parts = folder.Name.Split('_', 3);
        string sessionRoot = Path.Combine(rootFolder, "Sessions");
        if (!Directory.Exists(sessionRoot) && string.Equals(Path.GetFileName(rootFolder.TrimEnd(Path.DirectorySeparatorChar)), "Recordings", StringComparison.OrdinalIgnoreCase))
            sessionRoot = Path.Combine(Path.GetDirectoryName(rootFolder)!, "Sessions");
        string? sessionPath = parts.Length == 3 ? Path.Combine(sessionRoot, "Session_" + parts[2] + "_Data.json") : null;
        if (sessionPath is not null && File.Exists(sessionPath))
        {
            using var session = ReadJson(sessionPath);
            if (session.RootElement.TryGetProperty("gameType", out var type) && type.ValueKind == JsonValueKind.String)
                result.GameType = type.GetString() ?? "";
            if (session.RootElement.TryGetProperty("allyTeam", out var ally) && ally.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ally.GetString()))
                result.Ally = ally.GetString()!;
            if (session.RootElement.TryGetProperty("enemyTeam", out var enemy) && enemy.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(enemy.GetString()))
                result.Enemy = enemy.GetString()!;
        }
        if (saved is not null)
        {
            if (!string.IsNullOrWhiteSpace(saved.Ally)) result.Ally = saved.Ally;
            if (!string.IsNullOrWhiteSpace(saved.Enemy)) result.Enemy = saved.Enemy;
            if (!string.IsNullOrWhiteSpace(saved.GameType)) result.GameType = saved.GameType;
        }
        result.GameType = GameTypes.Normalize(result.GameType);
        return result;
    }
    private static JsonDocument ReadJson(string path) { using var reader = new StreamReader(path); return JsonDocument.Parse(reader.ReadToEnd()); }
    internal void Save(string gamePath, bool requireNames = true)
    {
        Ally = Clean(Ally); Enemy = Clean(Enemy); GameType = GameTypes.Normalize(GameType);
        if ((requireNames && (Ally.Length == 0 || Enemy.Length == 0)) || Ally.Length > 80 || Enemy.Length > 80)
            throw new ArgumentException("Enter both team names, up to 80 characters each.");
        if (GameType.Length > 80) throw new ArgumentException("Game type must be at most 80 characters.");
        CommsAnalysis.SaveJson(PathFor(gamePath), this);
    }
    internal static string Clean(string value) => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
