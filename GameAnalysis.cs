using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace League_Data_Recorder;

// This file has no WinForms or League-client dependency. Analysis only reads saved games.
internal sealed class AnalysisDataset
{
    internal List<AnalysisTeamGame> Games { get; } = new();
    internal List<string> Warnings { get; } = new();
    internal int DuplicateGames { get; set; }
}

internal sealed class AnalysisTeamGame
{
    internal string RecordingPath { get; init; } = "";
    internal bool? IsPlayerTeam { get; init; }
    internal double? Outlook { get; set; }
    internal string GameId { get; init; } = "";
    internal string TeamKey { get; init; } = "";
    internal string TeamName { get; init; } = "";
    internal string OpponentKey { get; set; } = "";
    internal string OpponentName { get; set; } = "";
    internal int TeamId { get; init; }
    internal string GameType { get; init; } = "";
    internal DateTime? PlayedAt { get; init; }
    internal double? Minutes { get; init; }
    internal bool? Won { get; init; }
    internal List<AnalysisPlayerGame> Players { get; } = new();
    internal Dictionary<string, bool?> Objectives { get; } = new(StringComparer.Ordinal);
}

internal sealed class AnalysisPlayerGame
{
    internal CommsAnalysisPlayer? Comms { get; set; }
    internal AnalysisTeamGame Game { get; init; } = null!;
    internal string Identity { get; set; } = "";
    internal string Account { get; init; } = "";
    internal string Name { get; init; } = "";
    internal string Role { get; init; } = "";
    internal string Champion { get; init; } = "";
    internal double? Kills { get; init; }
    internal double? Deaths { get; init; }
    internal double? Assists { get; init; }
    internal double? Damage { get; init; }
    internal double? DamageTaken { get; init; }
    internal double? Cs { get; init; }
    internal double? Gold { get; init; }
    internal double? Vision { get; init; }
}

internal sealed class AnalysisStatRow
{
    internal double? CommsShare { get; init; }
    internal double? FightCommsShare { get; init; }
    internal double? Callouts { get; init; }
    internal double? SummonerCallouts { get; init; }
    internal double? UltimateCallouts { get; init; }
    internal double? EnemyCallouts { get; init; }
    internal double? AllyCallouts { get; init; }
    internal double? EnemySummonerCallouts { get; init; }
    internal double? EnemyUltimateCallouts { get; init; }
    internal double? Tracking { get; init; }
    internal double? RoamCalls { get; init; }
    internal double? ObjectiveCalls { get; init; }
    internal int CommsGames { get; init; }
    internal int FightCommsGames { get; init; }
    internal int TrackingGames { get; init; }
    internal string Key { get; init; } = "";
    internal string Name { get; init; } = "";
    internal string Role { get; init; } = "";
    internal int Games { get; init; }
    internal int Wins { get; init; }
    internal int Results { get; init; }
    internal double? Kills { get; init; }
    internal double? Deaths { get; init; }
    internal double? Assists { get; init; }
    internal double? Kda { get; init; }
    internal double? DamagePerMinute { get; init; }
    internal double? TakenPerMinute { get; init; }
    internal double? CsPerMinute { get; init; }
    internal double? GoldPerMinute { get; init; }
    internal double? VisionPerMinute { get; init; }
    internal double? DamagePerMinutePerDeath { get; init; }
    internal double? WinRate => Results == 0 ? null : (double)Wins / Results;
}

internal static class GameAnalysis
{
    internal static readonly string[] ObjectiveNames = { "Soul", "Baron", "Herald", "Grubs", "Elder" };
    private const string PostSuffix = "_ParsedPostGameData.json";

    // Read the exact selected recording, independently of Analysis filters and
    // duplicate-game selection. Clipboard exports must match the clicked game.
    internal static AnalysisDataset LoadRecording(string gamePath, string root,
        CancellationToken cancellationToken = default)
    {
        var result = new AnalysisDataset();
        cancellationToken.ThrowIfCancellationRequested();
        using var post = ReadJson(gamePath + PostSuffix);
        string id = Text(Get(post.RootElement, "gameInfo"), "gameId");
        if (id.Length == 0) id = Path.GetFileName(gamePath);
        string sessionRoot = Path.Combine(root, "Sessions");
        if (!Directory.Exists(sessionRoot) && string.Equals(Path.GetFileName(root.TrimEnd(
                Path.DirectorySeparatorChar)), "Recordings", StringComparison.OrdinalIgnoreCase))
            sessionRoot = Path.Combine(Path.GetDirectoryName(root)!, "Sessions");
        var sessions = ReadSessions(sessionRoot, result, cancellationToken);
        string sessionId = GetSessionId(Path.GetDirectoryName(gamePath)!);
        sessions.TryGetValue(sessionId, out var names);
        var games = ReadTeams(post.RootElement, gamePath, id, sessionId, names, result);
        cancellationToken.ThrowIfCancellationRequested();
        ReadObjectives(gamePath, post.RootElement, games, result);
        result.Games.AddRange(games);
        return result;
    }

    internal static AnalysisDataset Load(string root, CancellationToken cancellationToken = default)
    {
        var result = new AnalysisDataset();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            result.Warnings.Add("Choose your saved recordings folder on the Recording tab.");
            return result;
        }

        string recordings = Directory.Exists(Path.Combine(root, "Recordings"))
            ? Path.Combine(root, "Recordings") : root;
        string sessionRoot = Path.Combine(root, "Sessions");
        if (!Directory.Exists(sessionRoot) && string.Equals(Path.GetFileName(recordings.TrimEnd(
                Path.DirectorySeparatorChar)), "Recordings", StringComparison.OrdinalIgnoreCase))
            sessionRoot = Path.Combine(Path.GetDirectoryName(recordings)!, "Sessions");
        var sessions = ReadSessions(sessionRoot, result, cancellationToken);
        var files = new List<string>(Directory.EnumerateFiles(recordings, "*" + PostSuffix));
        foreach (string folder in Directory.EnumerateDirectories(recordings, "Game_*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { files.AddRange(Directory.EnumerateFiles(folder, "*" + PostSuffix)); }
            catch (Exception ex) when (IsDataError(ex))
            { result.Warnings.Add($"{Path.GetFileName(folder)}: {ex.Message}"); }
        }

        // Prefer the latest copy of a game, and count it once even if it was recorded twice.
        var seenGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in files.OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(x => x, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var post = ReadJson(path);
                string gamePath = path[..^PostSuffix.Length];
                string id = Text(Get(post.RootElement, "gameInfo"), "gameId");
                if (id.Length == 0) id = Path.GetFileName(gamePath);
                if (seenGames.Contains(id)) { result.DuplicateGames++; continue; }
                string sessionId = GetSessionId(Path.GetDirectoryName(path)!);
                sessions.TryGetValue(sessionId, out var names);
                var games = ReadTeams(post.RootElement, gamePath, id, sessionId, names, result);
                if (games.Count == 0)
                {
                    result.Warnings.Add($"{Path.GetFileName(path)}: no usable team/player statistics.");
                    continue;
                }
                ReadObjectives(gamePath, post.RootElement, games, result);
                result.Games.AddRange(games);
                seenGames.Add(id);
            }
            catch (Exception ex) when (IsDataError(ex))
            { result.Warnings.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }

        // Join older name-only records to a PUUID only when the full Riot ID maps unambiguously.
        var accounts = result.Games.SelectMany(g => g.Players).Where(p => p.Account.Contains('#'))
            .GroupBy(p => p.Account, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Identity)
                .Where(x => x.StartsWith("puuid:", StringComparison.Ordinal)).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var p in result.Games.SelectMany(g => g.Players))
            if (!p.Identity.StartsWith("puuid:", StringComparison.Ordinal) &&
                accounts.TryGetValue(p.Account, out var ids) && ids.Length == 1)
                p.Identity = ids[0];
        return result;
    }

    internal static List<AnalysisTeamGame> Filter(AnalysisDataset dataset, string teamKey,
        string gameType, string opponentKey, DateTime? from, DateTime? to) => dataset.Games
        .Where(g => g.TeamKey == teamKey && (gameType.Length == 0 || g.GameType.Equals(gameType, StringComparison.OrdinalIgnoreCase)) &&
            (opponentKey.Length == 0 || g.OpponentKey == opponentKey) &&
            (!from.HasValue || g.PlayedAt?.Date >= from.Value.Date) &&
            (!to.HasValue || g.PlayedAt?.Date <= to.Value.Date)).ToList();

    internal static List<AnalysisStatRow> PlayerRows(IEnumerable<AnalysisTeamGame> games) => games
        .SelectMany(g => g.Players).GroupBy(p => p.Identity, StringComparer.OrdinalIgnoreCase)
        .Select(g => Aggregate(g.Key, g.OrderBy(p => p.Game.PlayedAt).Last().Name, g))
        .OrderBy(r => RoleOrder(r.Role)).ThenByDescending(r => r.Games).ThenBy(r => r.Name).ToList();

    internal static List<AnalysisStatRow> ChampionRows(IEnumerable<AnalysisTeamGame> games, string identity) => games
        .SelectMany(g => g.Players).Where(p => p.Identity == identity)
        .GroupBy(p => p.Champion, StringComparer.OrdinalIgnoreCase)
        .Select(g => Aggregate(g.Key, g.Key, g))
        .OrderByDescending(r => r.Games).ThenByDescending(r => r.WinRate).ThenBy(r => r.Name).ToList();

    internal static void AttachComms(AnalysisDataset data, string path, CommsAnalysisReport report)
    {
        if (report.Version != CommsAnalysisReport.CurrentVersion || !report.HasTimedWords) return;
        // Voice recordings belong to the recording team. Never copy role metrics to opponents.
        var teams = data.Games.Where(g => g.RecordingPath == path && g.IsPlayerTeam == true).ToArray();
        if (teams.Length != 1) return;
        teams[0].Outlook = report.Outlook?.TeamVibe;
        foreach (var role in teams[0].Players.GroupBy(p => p.Role))
        {
            if (role.Count() != 1) continue;
            string key = VideoCommsTimeline.NormalizeRole(role.Key);
            if (!CommsAnalysis.Roles.Take(5).Contains(key)) continue;
            var metrics = report.Players.Where(p => p.Role == key && p.HasSpeakerAssignment).ToArray();
            if (metrics.Length == 1) role.Single().Comms = metrics[0];
        }
    }

    private static AnalysisStatRow Aggregate(string key, string name, IEnumerable<AnalysisPlayerGame> source)
    {
        var players = source.ToList();
        double? deaths = Average(players.Select(p => p.Deaths));
        double? damagePerMinute = Average(players.Select(p => Divide(p.Damage, p.Game.Minutes)));
        var kdaGames = players.Where(p => p.Kills.HasValue && p.Deaths.HasValue && p.Assists.HasValue).ToList();
        return new AnalysisStatRow
        {
            Key = key, Name = name,
            Role = players.GroupBy(p => p.Role).OrderByDescending(g => g.Count())
                .ThenBy(g => RoleOrder(g.Key)).First().Key,
            Games = players.Count, Wins = players.Count(p => p.Game.Won == true),
            Results = players.Count(p => p.Game.Won.HasValue),
            Kills = Average(players.Select(p => p.Kills)), Deaths = deaths,
            Assists = Average(players.Select(p => p.Assists)),
            Kda = kdaGames.Count == 0 ? null : Divide(kdaGames.Sum(p => p.Kills + p.Assists), kdaGames.Sum(p => p.Deaths)),
            DamagePerMinute = damagePerMinute,
            TakenPerMinute = Average(players.Select(p => Divide(p.DamageTaken, p.Game.Minutes))),
            CsPerMinute = Average(players.Select(p => Divide(p.Cs, p.Game.Minutes))),
            GoldPerMinute = Average(players.Select(p => Divide(p.Gold, p.Game.Minutes))),
            VisionPerMinute = Average(players.Select(p => Divide(p.Vision, p.Game.Minutes))),
            DamagePerMinutePerDeath = Divide(damagePerMinute, deaths),
            CommsShare = Average(players.Select(p => p.Comms?.Share / 100)),
            FightCommsShare = Average(players.Select(p => p.Comms?.FightShare / 100)),
            Callouts = Average(players.Select(p => p.Comms is { } c ? (double?)(c.SummonerCallouts + c.UltimateCallouts) : null)),
            SummonerCallouts = Average(players.Select(p => (double?)p.Comms?.SummonerCallouts)),
            UltimateCallouts = Average(players.Select(p => (double?)p.Comms?.UltimateCallouts)),
            EnemyCallouts = Average(players.Select(p => (double?)p.Comms?.EnemyCallouts)),
            AllyCallouts = Average(players.Select(p => (double?)p.Comms?.AllyCallouts)),
            EnemySummonerCallouts = Average(players.Select(p => (double?)p.Comms?.EnemySummonerCallouts)),
            EnemyUltimateCallouts = Average(players.Select(p => (double?)p.Comms?.EnemyUltimateCallouts)),
            Tracking = Average(players.Select(p => (double?)p.Comms?.JungleMentions)),
            RoamCalls = Average(players.Select(p => (double?)p.Comms?.RoamCalls)),
            ObjectiveCalls = Average(players.Select(p => (double?)p.Comms?.ObjectiveCalls)),
            CommsGames = players.Count(p => p.Comms is not null),
            FightCommsGames = players.Count(p => p.Comms?.FightShare is not null),
            TrackingGames = players.Count(p => p.Comms?.JungleMentions is not null),
        };
    }

    internal static double? Average(IEnumerable<double?> values)
    {
        var present = values.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return present.Length == 0 ? null : present.Average();
    }

    private static double? Divide(double? a, double? b) => a.HasValue && b > 0 ? a / b : null;
    private static int RoleOrder(string role) => role switch { "Top" => 0, "Jungle" => 1, "Mid" => 2, "ADC" => 3, "Support" => 4, _ => 5 };

    private static Dictionary<string, (string Ally, string Enemy, string GameType)> ReadSessions(string path,
        AnalysisDataset data, CancellationToken cancellationToken)
    {
        var sessions = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(path)) return sessions;
        foreach (string file in Directory.EnumerateFiles(path, "Session_*_Data.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var json = ReadJson(file);
                string id = Text(json.RootElement, "sessionId");
                if (id.Length > 0) sessions[id] = (Text(json.RootElement, "allyTeam"), Text(json.RootElement, "enemyTeam"), Text(json.RootElement, "gameType"));
            }
            catch (Exception ex) when (IsDataError(ex))
            { data.Warnings.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
        return sessions;
    }

    private static List<AnalysisTeamGame> ReadTeams(JsonElement root, string path, string id,
        string sessionId, (string Ally, string Enemy, string GameType) names, AnalysisDataset data)
    {
        var games = new List<AnalysisTeamGame>();
        var info = Get(root, "gameInfo");
        if (Boolean(info, "invalid") == true) return games;
        double? seconds = Number(info, "gameLength");
        DateTime? date = ReadDate(Number(info, "endOfGameTimestamp"), seconds);
        if (!date.HasValue)
        {
            try
            {
                using var draft = ReadJson(path + "_DraftData.json");
                if (DateTimeOffset.TryParse(Text(draft.RootElement, "endTime"), CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var time) && time.Year is > 2000 and < 9999)
                    date = time.LocalDateTime;
            }
            catch (Exception ex) when (IsDataError(ex)) { /* Timestamp fallback below. */ }
        }
        date ??= File.GetLastWriteTime(path + PostSuffix);
        double? minutes = seconds > 0 ? seconds / 60 : null;
        int localTeam = (int)(Number(Get(root, "localPlayer"), "teamId") ?? 0);
        RecordingTeamNames? nameOverrides = null;
        try { nameOverrides = RecordingTeamNames.ReadOverride(path); }
        catch (Exception ex) when (IsDataError(ex))
        { data.Warnings.Add($"{Path.GetFileName(path)} team names: {ex.Message}"); }
        foreach (var team in Array(Get(root, "teams")))
        {
            int teamId = (int)(Number(team, "teamId") ?? 0);
            if (teamId <= 0 || games.Any(g => g.TeamId == teamId))
            {
                data.Warnings.Add($"{Path.GetFileName(path)}: skipped a missing or duplicate team ID.");
                continue;
            }
            bool? isAlly = Boolean(team, "isPlayerTeam") ?? (localTeam > 0 ? teamId == localTeam : null);
            string name = Clean(Text(team, "name"));
            string sessionName = Clean((isAlly == true ? names.Ally : isAlly == false ? names.Enemy : "") ?? "");
            if (sessionName.Length > 0) name = sessionName;
            string perGameName = isAlly == true ? nameOverrides?.Ally ?? "" : isAlly == false ? nameOverrides?.Enemy ?? "" : "";
            if (!string.IsNullOrWhiteSpace(perGameName)) name = Clean(perGameName);
            string side = isAlly == true ? "allies" : isAlly == false ? "enemies" : "team " + teamId;
            string scope = sessionId.Length > 0 ? sessionId : "game " + id;
            string key = name.Length > 0 ? "named:" + name.ToUpperInvariant() : "unnamed:" + scope + ":" + side;
            if (name.Length == 0) name = $"Unnamed {side} ({(sessionId.Length > 0 ? "session " + sessionId[..Math.Min(8, sessionId.Length)] : scope)})";
            var game = new AnalysisTeamGame
            {
                RecordingPath = path, IsPlayerTeam = isAlly,
                GameId = id, TeamId = teamId, TeamKey = key, TeamName = name,
                GameType = GameTypes.Normalize(string.IsNullOrWhiteSpace(nameOverrides?.GameType) ? names.GameType : nameOverrides.GameType), PlayedAt = date, Minutes = minutes,
                Won = Boolean(team, "isWinningTeam") ?? WinFromPlayers(team),
            };
            foreach (var p in Array(Get(team, "players")))
            {
                string puuid = Text(p, "puuid");
                string account = Text(p, "riotId");
                string playerName = Text(p, "riotIdGameName");
                if (playerName.Length == 0) playerName = Text(p, "summonerName");
                string tag = Text(p, "riotIdTagLine");
                if (account.Length == 0) account = playerName + (tag.Length > 0 && !playerName.Contains('#') ? "#" + tag : "");
                if (puuid.Length == 0 && account.Length == 0) continue;
                if (account.Length == 0) account = "Player " + puuid[..Math.Min(8, puuid.Length)];
                var stats = Get(p, "stats");
                double? laneCs = Stat(stats, "totalMinionsKilled", "MINIONS_KILLED");
                double? jungleCs = Stat(stats, "neutralMinionsKilled", "NEUTRAL_MINIONS_KILLED");
                string role = Text(p, "detectedTeamPosition");
                if (role.Length == 0) role = Text(p, "selectedPosition");
                game.Players.Add(new AnalysisPlayerGame
                {
                    Game = game, Identity = puuid.Length > 0 ? "puuid:" + puuid.ToLowerInvariant() : "riot:" + account.ToLowerInvariant(),
                    Account = account, Name = account, Role = FormatRole(role),
                    Champion = Text(p, "championName") is { Length: > 0 } champ ? champ : "Champion " + Text(p, "championId"),
                    Kills = Stat(stats, "kills", "CHAMPIONS_KILLED"),
                    Deaths = Stat(stats, "deaths", "NUM_DEATHS"), Assists = Stat(stats, "assists", "ASSISTS"),
                    Damage = Stat(stats, "totalDamageDealtToChampions", "TOTAL_DAMAGE_DEALT_TO_CHAMPIONS"),
                    DamageTaken = Stat(stats, "totalDamageTaken", "TOTAL_DAMAGE_TAKEN"),
                    Cs = laneCs.HasValue && jungleCs.HasValue ? laneCs + jungleCs : null,
                    Gold = Stat(stats, "goldEarned", "GOLD_EARNED"), Vision = Stat(stats, "visionScore", "VISION_SCORE"),
                });
            }
            if (game.Players.Count > 0) games.Add(game);
        }
        foreach (var game in games)
        {
            var opponent = games.FirstOrDefault(g => g != game);
            game.OpponentKey = opponent?.TeamKey ?? "unknown";
            game.OpponentName = opponent?.TeamName ?? "Unknown opponent";
        }
        return games;
    }

    private static void ReadObjectives(string path, JsonElement post, List<AnalysisTeamGame> games, AnalysisDataset data)
    {
        JsonDocument? live = null;
        try
        {
            if (File.Exists(path + "_ParsedGameData.json")) live = ReadJson(path + "_ParsedGameData.json");
        }
        catch (Exception ex) when (IsDataError(ex))
        { data.Warnings.Add($"{Path.GetFileName(path)} objectives: {ex.Message}"); }
        using (live)
        {
            var eventsToken = live is null ? default : Get(live.RootElement, "events");
            var events = Array(eventsToken).ToList();
            bool complete = events.Any(e => Text(e, "EventName") == "GameStart") &&
                events.Any(e => Text(e, "EventName") == "GameEnd");
            var counts = games.ToDictionary(g => g.TeamId, _ => ObjectiveNames.ToDictionary(n => n, _ => 0));
            var uncertain = new HashSet<string>(StringComparer.Ordinal);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in events)
            {
                string type = Text(e, "EventName");
                string? objective = type switch
                {
                    "BaronKill" => "Baron", "HeraldKill" => "Herald", "HordeKill" or "VoidGrubKill" => "Grubs",
                    "DragonKill" => Text(e, "DragonType").Equals("Elder", StringComparison.OrdinalIgnoreCase) ? "Elder" : "Soul",
                    _ => null,
                };
                if (objective is null) continue;
                if (type == "DragonKill" && Text(e, "DragonType").Length == 0)
                {
                    uncertain.Add("Soul");
                    uncertain.Add("Elder");
                    continue;
                }
                string eventKey = Text(e, "EventID");
                if (eventKey.Length == 0) eventKey = e.GetRawText();
                if (!unique.Add(eventKey)) continue;
                string killer = Text(e, "KillerName");
                var exact = games.Where(g => g.Players.Any(p => p.Account.Equals(killer, StringComparison.OrdinalIgnoreCase))).ToArray();
                var candidates = exact.Length > 0 ? exact : games.Where(g => g.Players.Any(p =>
                    p.Account.Split('#')[0].Equals(killer, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (candidates.Length == 1) counts[candidates[0].TeamId][objective]++;
                else uncertain.Add(objective);
            }
            foreach (var game in games)
            {
                foreach (string objective in ObjectiveNames)
                {
                    bool obtained = counts[game.TeamId][objective] >= (objective == "Soul" ? 4 : 1);
                    game.Objectives[objective] = obtained ? true : complete && !uncertain.Contains(objective) ? false : null;
                }
                // The end-game total is authoritative, even if live capture started late.
                var postTeam = Array(Get(post, "teams")).FirstOrDefault(t => Number(t, "teamId") == game.TeamId);
                double? barons = Stat(Get(postTeam, "stats"), "baronKills", "BARON_KILLS");
                if (barons.HasValue) game.Objectives["Baron"] = barons > 0;
            }
        }
    }

    private static bool? WinFromPlayers(JsonElement team)
    {
        var wins = Array(Get(team, "players")).Select(p => Stat(Get(p, "stats"), "win", "WIN"))
            .Where(x => x.HasValue).Select(x => x > 0).Distinct().ToArray();
        return wins.Length == 1 ? wins[0] : null;
    }


    private static DateTime? ReadDate(double? timestamp, double? seconds)
    {
        if (!timestamp.HasValue) return null;
        try
        {
            double ms = timestamp.Value < 100_000_000_000 ? timestamp.Value * 1000 : timestamp.Value;
            return DateTimeOffset.FromUnixTimeMilliseconds(checked((long)ms))
                .AddSeconds(-(seconds ?? 0)).LocalDateTime;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException) { return null; }
    }

    private static string FormatRole(string role) => role.ToUpperInvariant() switch
    { "TOP" => "Top", "JUNGLE" => "Jungle", "MIDDLE" or "MID" => "Mid", "BOTTOM" or "ADC" => "ADC", "UTILITY" or "SUPPORT" => "Support", _ => "Unknown" };
    private static string GetSessionId(string folder)
    {
        var parts = Path.GetFileName(folder).Split('_', 3);
        return parts.Length == 3 && parts[0] == "Game" ? parts[2] : "";
    }
    private static string Clean(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static bool IsDataError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or OverflowException;
    private static JsonDocument ReadJson(string path)
    {
        using var reader = new StreamReader(path); // Also handles older UTF-8 files with a BOM.
        return JsonDocument.Parse(reader.ReadToEnd());
    }
    private static JsonElement Get(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child) ? child : default;
    private static IEnumerable<JsonElement> Array(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : Enumerable.Empty<JsonElement>();
    private static string Text(JsonElement value, string name)
    {
        var child = Get(value, name);
        return child.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "" : child.ToString().Trim();
    }
    private static double? Number(JsonElement value, string name)
    {
        string text = Text(value, name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n >= 0 ? n : null;
    }
    private static double? Stat(JsonElement value, params string[] names)
    {
        foreach (string name in names)
        {
            double? n = Number(value, name);
            if (n.HasValue) return n;
            bool? b = Boolean(value, name);
            if (b.HasValue) return b.Value ? 1 : 0;
        }
        return null;
    }
    private static bool? Boolean(JsonElement value, string name) => Text(value, name).ToLowerInvariant() switch
    { "true" or "win" or "1" => true, "false" or "fail" or "loss" or "0" => false, _ => null };
}
