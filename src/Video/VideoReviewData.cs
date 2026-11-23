using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace League_Data_Recorder;

internal sealed record VideoReviewMedia(
    string GamePath, string? VideoPath, string? AudioPath,
    double AudioOffset, string TimingNote, string StorageKey, VideoReviewTiming ReviewTiming)
{
    internal static VideoReviewMedia Read(string gamePath)
    {
        string? video = File.Exists(gamePath + "_Video.mp4") ? gamePath + "_Video.mp4" : null;
        string? audio = File.Exists(gamePath + "_Audio.wav") ? gamePath + "_Audio.wav" : null;
        double offset = 0;
        string note = "";
        try
        {
            string path = gamePath + "_MediaTiming.json";
            if (File.Exists(path))
            {
                var timing = JsonSerializer.Deserialize<GameMediaTiming>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (timing is not null)
                {
                    double? videoOrigin = MediaTimelineOrigins.Video(timing);
                    double? audioOrigin = MediaTimelineOrigins.Audio(timing);
                    if (videoOrigin is double v && audioOrigin is double a &&
                        double.IsFinite(v) && double.IsFinite(a))
                        offset = a - v;
                    else
                        note = "Capture timing is unavailable; voice audio starts with the video.";
                    if (note.Length == 0 && !string.IsNullOrWhiteSpace(timing.AudioTrimError))
                        note = "Using capture timestamps because the saved audio could not be aligned.";
                    if (note.Length == 0 && !string.IsNullOrWhiteSpace(timing.VideoTimingError))
                        note = "Video capture timing could not be verified; check the recording log.";
                }
            }
            else if (audio is not null)
                note = "No capture timing saved; voice audio starts with the video.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { note = "Capture timing could not be read; voice audio starts with the video."; }
        var review = VideoReviewTiming.Read(gamePath);
        if (review.AudioAlignedToVideo) { offset = 0; note = "Voice timing saved to WAV."; }
        return new(gamePath, video, audio, offset, note,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(gamePath))))[..20], review);
    }
}

internal sealed record VideoFightCue(
    int Id, double Start, double End, double GameStart, double GameEnd, int Kills, string Participants,
    string Outcome, int? AllyKills, int? EnemyKills, string OutcomeNote, IReadOnlyList<VideoFightChampion> Champions);

internal sealed record VideoFightChampion(string Name, string Champion, int ChampionId, int Team, string Side,
    string? Icon = null, int Kills = 0, int Deaths = 0);

internal sealed record VideoFightTimeline(IReadOnlyList<VideoFightCue> Fights, string Status)
{
    internal static VideoFightTimeline Read(string gamePath)
    {
        try
        {
            string livePath = gamePath + "_ParsedGameData.json";
            string timingPath = gamePath + "_MediaTiming.json";
            if (!File.Exists(livePath))
                return new([], "Fight events need saved live game data.");
            if (!File.Exists(timingPath))
                return new([], "Fight times are unavailable without saved capture timing.");

            var timing = JsonSerializer.Deserialize<GameMediaTiming>(File.ReadAllText(timingPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            double? videoOrigin = MediaTimelineOrigins.Video(timing);
            if (timing?.GameStartClockSeconds is not double gameOrigin || videoOrigin is not double origin ||
                !double.IsFinite(gameOrigin) || !double.IsFinite(origin) || !double.IsFinite(gameOrigin - origin))
                return new([], "Fight times are unavailable: the video/game capture origins are incomplete.");

            using var live = JsonDocument.Parse(File.ReadAllText(livePath));
            if (!live.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array ||
                !live.RootElement.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
                return new([], "Fight events need readable live events and player identities.");

            // Game events use League's clock. They belong on the VIDEO clock,
            // independently of voice/caption offsets and later WAV alignment.
            double shift = gameOrigin - origin;
            using var post = ReadPostGame(gamePath);
            int? allyTeam = FindAllyTeam(post?.RootElement ?? default);
            var fights = CommsAnalysis.FindRecordedFights(live.RootElement, post?.RootElement ?? default)
                .Select((f, i) => BuildCue(f, i + 1, shift, allyTeam)).ToArray();
            return new(fights, fights.Length == 0 ? "No qualifying fights detected in the saved events."
                : "Estimated from kills and assists, with at least 3 participants per team. Includes 8s before and 6s after. Capture timing is approximate; replay pauses or fast-forward can affect alignment.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Unavailable event data must never prevent the recording from playing.
            return new([], "Fight events could not be loaded from the saved game data.");
        }
    }

    private static VideoFightCue BuildCue(CommsFightWindow fight, int id, double shift, int? allyTeam)
    {
        int? allyKills = null, enemyKills = null;
        string outcome = "unknown";
        string note = "The ally team could not be identified from the saved post-game data.";
        if (allyTeam is int own)
        {
            int other = own == 100 ? 200 : 100;
            if (fight.UnresolvedKills == 0 && fight.KillsByTeam.Values.Sum() == fight.Kills &&
                fight.KillsByTeam.Keys.All(t => t == own || t == other))
            {
                allyKills = fight.KillsByTeam.GetValueOrDefault(own);
                enemyKills = fight.KillsByTeam.GetValueOrDefault(other);
                outcome = allyKills > enemyKills ? "win" : allyKills < enemyKills ? "loss" : "even";
                note = "Estimated from kills in this fight. Objectives and later trades are not included.";
            }
            else note = "Some kills could not be attributed to a team, so the result is unknown.";
        }
        var champions = fight.Fighters.OrderBy(p => p.Team).ThenBy(p => p.Champion, StringComparer.Ordinal)
            .ThenBy(p => p.Name, StringComparer.Ordinal).Select(p => new VideoFightChampion(p.Name, p.Champion,
                p.ChampionId, p.Team, allyTeam.HasValue ? p.Team == allyTeam.Value ? "ally" : "enemy" :
                    p.Team == 100 ? "blue" : p.Team == 200 ? "red" : "unknown", Kills: p.Kills, Deaths: p.Deaths)).ToArray();
        return new(id, fight.Start + shift, fight.End + shift, fight.Start, fight.End, fight.Kills,
            fight.Participants, outcome, allyKills, enemyKills, note, champions);
    }

    private static JsonDocument? ReadPostGame(string gamePath)
    {
        try
        {
            string path = gamePath + "_ParsedPostGameData.json";
            return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    // A side/color is not an ally identity. Only use explicit saved ownership
    // evidence, and stay neutral when the flags/local player contradict each other.
    internal static int? FindAllyTeam(JsonElement post)
    {
        var teams = Items(Get(post, "teams")).ToArray();
        var marked = teams.Where(t => Get(t, "isPlayerTeam").ValueKind == JsonValueKind.True)
            .Select(t => TeamId(Get(t, "teamId"))).Where(t => t.HasValue).Select(t => t!.Value).Distinct().ToArray();
        var localPlayers = teams.Where(t => Items(Get(t, "players")).Any(p => Get(p, "isLocalPlayer").ValueKind == JsonValueKind.True))
            .Select(t => TeamId(Get(t, "teamId"))).Where(t => t.HasValue).Select(t => t!.Value);
        var evidence = marked.Concat(localPlayers).ToList();
        if (TeamId(Get(Get(post, "localPlayer"), "teamId")) is int local) evidence.Add(local);
        var candidates = evidence.Distinct().ToArray();
        if (candidates.Length != 1) return null;
        int own = candidates[0];
        if (teams.Any(t => TeamId(Get(t, "teamId")) == own && Get(t, "isPlayerTeam").ValueKind == JsonValueKind.False))
            return null;
        return own;
    }

    private static int? TeamId(JsonElement value) => value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int id) && id is 100 or 200 ? id : null;
    private static JsonElement Get(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var result) ? result : default;
    private static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray() : Enumerable.Empty<JsonElement>();
}

internal static class VideoCommsTimeline
{
    internal static List<VideoCommsCue> Build(TranscriptionResult? transcript,
        IReadOnlyDictionary<string, string>? roles, IReadOnlyDictionary<string, string> champions,
        Func<string, string?> iconForChampion)
    {
        var cues = new List<VideoCommsCue>();
        if (transcript is null) return cues;
        var words = new List<WordResult>();
        int characters = 0;
        string Key(WordResult word) => word.OverlappingSpeech ? TranscriptOverlap.DisplayKey(word)
            : "speaker:" + (word.Speaker?.Trim() ?? "").ToLowerInvariant();
        VideoCommsParticipant Participant(string id)
        {
            string assignedRole = roles is not null && roles.TryGetValue(id, out var value) ? value : "";
            string participantRole = NormalizeRole(assignedRole);
            string participantChampion = !assignedRole.EndsWith('?') && champions.TryGetValue(participantRole, out var valueChampion)
                ? valueChampion : "";
            return new(id, TranscribeAudio.FormatSpeakerLabel(id, roles), participantRole, participantChampion,
                participantChampion.Length > 0 ? iconForChampion(participantChampion) : null);
        }
        void Flush()
        {
            if (words.Count == 0) return;
            bool overlapping = words[0].OverlappingSpeech;
            string[] overlapSpeakers = words.SelectMany(w => TranscriptOverlap.Speakers(w))
                .Distinct(StringComparer.Ordinal).ToArray();
            string speaker = words[0].Speaker?.Trim() ?? "";
            string assigned = roles is not null && roles.TryGetValue(speaker, out var r) ? r : "";
            bool tentative = assigned.EndsWith('?');
            string role = overlapping ? "overlap" : NormalizeRole(assigned);
            // A proposed/weak role is not an identified champion.
            string champion = !tentative && champions.TryGetValue(role, out var c) ? c : "";
            double start = words[0].Start;
            double end = Math.Max(words.Max(w => w.End), start + 0.08);
            cues.Add(new(cues.Count, start, end, overlapping ? TranscriptOverlap.Label(overlapSpeakers, roles)
                    : TranscribeAudio.FormatSpeakerLabel(speaker, roles),
                overlapping ? Key(words[0]) : speaker, role, champion, champion.Length > 0 ? iconForChampion(champion) : null,
                string.Join(" ", words.Select(w => w.Word.Trim())),
                words.Select(w => new VideoCommsWord(w.Start, w.End, w.Word.Trim(), w.TimingSource)).ToArray(),
                overlapping, overlapping ? overlapSpeakers.Take(2).Select(Participant).ToArray() : null));
            words.Clear();
            characters = 0;
        }
        foreach (var word in (transcript.Words ?? []).Where(w => w is not null &&
            !string.IsNullOrWhiteSpace(w.Word) && double.IsFinite(w.Start) && double.IsFinite(w.End) &&
            w.Start >= 0 && w.End >= w.Start).OrderBy(w => w.Start))
        {
            if (word.SpeakerSource == "asr_gap")
            {
                Flush(); words.Add(word); Flush(); continue;
            }
            // The full transcript groups uncertain turns for readability. Video captions
            // need shorter, speaker-specific cards so a portrait never labels another turn.
            if (words.Count > 0 && (Key(words[0]) != Key(word) || word.Start - words[^1].End > 0.85 ||
                    word.End - words[0].Start > 4.5 || characters + word.Word.Length > 100))
                Flush();
            words.Add(word);
            characters += word.Word.Length + 1;
            if (word.Word.TrimEnd().EndsWith('.') || word.Word.TrimEnd().EndsWith('!') ||
                word.Word.TrimEnd().EndsWith('?')) Flush();
        }
        Flush();
        return cues;
    }

    internal static string NormalizeRole(string? role) => role?.Trim().TrimEnd('?').ToLowerInvariant() switch
    {
        "top" => "top", "jungle" or "jg" => "jungle", "mid" or "middle" => "mid",
        "adc" or "bot" or "bottom" => "adc", "support" or "sup" or "utility" => "support",
        "coach" or "possible_coach" => "coach", _ => "unknown",
    };

    internal static Dictionary<string, string> ReadChampions(string gamePath, string transcriptPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(transcriptPath))
        {
            string stem = Path.GetFileNameWithoutExtension(transcriptPath);
            if (stem.EndsWith("_transcript", StringComparison.OrdinalIgnoreCase)) stem = stem[..^11];
            string contextPath = Path.Combine(Path.GetDirectoryName(transcriptPath)!, stem + "_game_context.json");
            if (File.Exists(contextPath))
            {
                var context = JsonSerializer.Deserialize<RoleGameContext>(File.ReadAllText(contextPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                foreach (var pair in context?.Roles ?? [])
                {
                    string? name = pair.Value.AlliedChampion;
                    if (string.IsNullOrWhiteSpace(name) && pair.Value.AlliedChampionId is int id && id > 0)
                        name = "Champion " + id;
                    if (!string.IsNullOrWhiteSpace(name)) result[NormalizeRole(pair.Key)] = name;
                }
            }
        }
        // Older transcripts may predate game-context sidecars. Only use a team
        // explicitly identified as ours; draft pick order is not a role assignment.
        string postPath = gamePath + "_ParsedPostGameData.json";
        if (result.Count < 5 && File.Exists(postPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(postPath));
            var root = document.RootElement;
            int localTeam = root.TryGetProperty("localPlayer", out var local) &&
                local.TryGetProperty("teamId", out var localId) && localId.TryGetInt32(out int teamId) ? teamId : 0;
            if (root.TryGetProperty("teams", out var teams) && teams.ValueKind == JsonValueKind.Array)
                foreach (var team in teams.EnumerateArray())
                {
                    bool hasTeamFlag = team.TryGetProperty("isPlayerTeam", out var own) &&
                        own.ValueKind is JsonValueKind.True or JsonValueKind.False;
                    bool allied = hasTeamFlag && own.ValueKind == JsonValueKind.True;
                    if (!hasTeamFlag && localTeam > 0 && team.TryGetProperty("teamId", out var tid) &&
                        tid.TryGetInt32(out int value)) allied = value == localTeam;
                    if (!allied || !team.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
                        continue;
                    var roster = players.EnumerateArray().Select(p => (
                        Role: new[] { "detectedTeamPosition", "selectedPosition", "position", "teamPosition" }
                            .Select(key => NormalizeRole(Text(p, key))).FirstOrDefault(role => role != "unknown") ?? "unknown",
                        Champion: Text(p, "championName") is { Length: > 0 } name ? name :
                            p.TryGetProperty("championId", out var cid) && cid.TryGetInt32(out int id) && id > 0
                                ? "Champion " + id : "")).ToArray();
                    foreach (var group in roster.Where(p => p.Role != "unknown" && p.Champion.Length > 0).GroupBy(p => p.Role))
                        if (group.Count() == 1) result.TryAdd(group.Key, group.Single().Champion);
                }
        }
        return result;
    }

    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var text) &&
        text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : "";
}
