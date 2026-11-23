using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Wave;

namespace League_Data_Recorder;

#region Recording pipeline

// Kept in an existing source file so copying the transcription update also
// installs recovery. Older standalone SavedEnemyPicks.cs is no longer needed.
internal static class RecordedDraftRecovery
{
    private static readonly System.Threading.SemaphoreSlim gate = new(1, 1);

    internal static async Task<bool> TryRecoverAsync(string gamePath, Action<string>? log = null,
        CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            string draftPath = gamePath + "_DraftData.json";
            if (!File.Exists(draftPath))
            {
                log?.Invoke("Enemy recovery: no saved draft at " + draftPath);
                return false;
            }
            var draft = JsonNode.Parse(await File.ReadAllTextAsync(draftPath, ct).ConfigureAwait(false)) as JsonObject;
            if (draft is null) throw new JsonException("Draft data is not a JSON object.");
            var current = draft["enemyPicks"] as JsonArray;
            int[] known = current?.Select(Id).Where(id => id > 0).ToArray() ?? [];
            if (current is { Count: 5 } && current.All(node => node is JsonValue value &&
                value.TryGetValue<int>(out int id) && id > 0)) return false;

            // Parsed files are the normal recorder output. The raw optional
            // sidecar stores the API payload inside a postGameData string.
            foreach (string suffix in new[] { "_ParsedPostGameData.json", "_PostGameData.json" })
            {
                string postPath = gamePath + suffix;
                if (!File.Exists(postPath)) continue;
                JsonObject? post;
                try
                {
                    post = JsonNode.Parse(await File.ReadAllTextAsync(postPath, ct).ConfigureAwait(false)) as JsonObject;
                    if (post?["postGameData"] is JsonValue wrapper && wrapper.TryGetValue<string>(out string? payload))
                        post = JsonNode.Parse(payload!) as JsonObject;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    log?.Invoke($"Enemy recovery: could not read {Path.GetFileName(postPath)}: {ex.Message}");
                    continue;
                }
                JsonObject? enemy = EnemyTeam(post, draft["allyPicks"] as JsonArray, known);
                JsonObject[] players = Players(enemy).ToArray();
                int[] recovered = players.Select(p => Id(p["championId"])).ToArray();
                if (recovered.Length != 5 || recovered.Any(id => id <= 0))
                {
                    log?.Invoke($"Enemy recovery: {Path.GetFileName(postPath)} did not identify a complete enemy roster.");
                    continue;
                }

                // Preserve known picks in their current slots when they match
                // the authoritative roster. If incomplete draft data conflicts,
                // replace it with the identified post-game roster instead.
                var remaining = recovered.ToList();
                bool compatible = known.All(remaining.Remove);
                JsonArray next;
                if (compatible && (current?.Count ?? 0) <= 5)
                {
                    var ordered = current?.Select(Id).ToList() ?? new List<int>();
                    foreach (int id in remaining)
                    {
                        int empty = ordered.FindIndex(value => value <= 0);
                        if (empty >= 0) ordered[empty] = id;
                        else ordered.Add(id);
                    }
                    next = new JsonArray(ordered.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
                }
                else next = new JsonArray(recovered.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
                draft["enemyPicks"] = next;
                temporary = draftPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(temporary, draft.ToJsonString(
                    new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                string backup = draftPath + ".before-enemy-recovery.bak";
                if (!File.Exists(backup)) File.Copy(draftPath, backup);
                File.Move(temporary, draftPath, true);
                log?.Invoke($"Enemy recovery: replaced {Path.GetFileName(draftPath)} from {Path.GetFileName(postPath)} " +
                    $"({string.Join(", ", next.Select(Id))}). Original draft backed up.");
                return true;
            }
            log?.Invoke("Enemy recovery: no usable post-game enemy roster was found for " + Path.GetFileName(gamePath) + ".");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            log?.Invoke("Enemy recovery skipped: " + ex.Message);
            return false;
        }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { log?.Invoke("Could not remove temporary draft: " + ex.Message); }
            gate.Release();
        }
    }

    private static JsonObject? EnemyTeam(JsonObject? post, JsonArray? alliedPicks, int[] enemyPicks)
    {
        if (post?["teams"] is not JsonArray array) return null;
        JsonObject[] teams = array.OfType<JsonObject>().ToArray();
        if (teams.Length != 2) return null;
        JsonObject? ally = null;
        bool ambiguous = false;
        void IdentifyAlly(IEnumerable<JsonObject> matches)
        {
            JsonObject[] found = matches.ToArray();
            if (found.Length > 1) { ambiguous = true; return; }
            if (found.Length == 0) return;
            if (ally is not null && !ReferenceEquals(ally, found[0])) ambiguous = true;
            ally = found[0];
        }
        var local = post["localPlayer"] as JsonObject;
        int localTeam = Id(local?["teamId"]);
        if (localTeam > 0) IdentifyAlly(teams.Where(t => Id(t["teamId"]) == localTeam));
        IdentifyAlly(teams.Where(t => Players(t).Any(p => Flag(p["isLocalPlayer"]) == true || SamePlayer(p, local))));
        IdentifyAlly(teams.Where(t => Flag(t["isPlayerTeam"]) == true));
        if (ambiguous) return null;
        if (ally is not null) return teams.Single(t => !ReferenceEquals(t, ally));

        int[] allies = alliedPicks?.Select(Id).Where(id => id > 0).ToArray() ?? [];
        if (allies.Length > 0)
        {
            var matches = teams.Where(t => ContainsAll(Players(t).Select(p => Id(p["championId"])), allies)).ToArray();
            if (matches.Length == 1) return teams.Single(t => !ReferenceEquals(t, matches[0]));
        }
        // Some custom games mark BOTH teams false. Treat that as unknown;
        // local-player and roster evidence above can still identify our side.
        var markedEnemies = teams.Where(t => Flag(t["isPlayerTeam"]) == false).ToArray();
        if (markedEnemies.Length == 1) return markedEnemies[0];
        if (enemyPicks.Length > 0)
        {
            var matches = teams.Where(t => ContainsAll(Players(t).Select(p => Id(p["championId"])), enemyPicks)).ToArray();
            if (matches.Length == 1) return matches[0];
        }
        return null;
    }

    private static bool ContainsAll(IEnumerable<int> roster, IEnumerable<int> picks)
    {
        var remaining = roster.ToList();
        return picks.All(remaining.Remove);
    }

    private static bool SamePlayer(JsonObject player, JsonObject? local) => local is not null &&
        new[] { "puuid", "riotId", "summonerName" }.Any(key => Text(player[key]) is { } id &&
            string.Equals(id, Text(local[key]), StringComparison.OrdinalIgnoreCase));

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text)
            ? text : null;

    private static int Id(JsonNode? node) => node is JsonValue value &&
        (value.TryGetValue<int>(out int id) || int.TryParse(Text(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        && id > 0 ? id : 0;

    private static bool? Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out bool flag) ? flag : null;

    private static IEnumerable<JsonObject> Players(JsonObject? team) =>
        (team?["players"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();
}

public static partial class TranscribeAudio
{
    /// <summary>
    /// Generates an absent/unusable transcript, always reprocesses it in place, and
    /// creates missing/outdated generated roles. Pinned/custom role files are preserved.
    /// </summary>
    public static Task<RecordingTranscriptionResult> ProcessRecordingAsync(
        string gamePath,
        string transcriptOutputPath,
        TranscriptionOptions? options = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    ) =>
        ProcessRecordingCoreAsync(
            gamePath,
            transcriptOutputPath,
            options ?? new(),
            log,
            cancellationToken,
            TranscribeAsync
        );

    internal static async Task<RecordingTranscriptionResult> ProcessRecordingCoreAsync(
        string gamePath,
        string transcriptOutputPath,
        TranscriptionOptions options,
        Action<string>? log,
        CancellationToken cancellationToken,
        Func<
            string,
            TranscriptionOptions,
            Action<string>?,
            CancellationToken,
            Task<TranscriptionResult>
        > generateTranscript
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInferenceOptions(options);
        gamePath = Path.GetFullPath(gamePath);
        transcriptOutputPath = Path.GetFullPath(transcriptOutputPath);
        log?.Invoke("Checking for an existing transcript…");
        string? transcriptPath = await FindExistingTranscriptAsync(
                gamePath,
                transcriptOutputPath,
                log,
                cancellationToken
            )
            .ConfigureAwait(false);
        bool generated = transcriptPath is null;
        bool refresh = false;
        bool refreshDiarization = false;
        bool recoverSavedSpeech = false;
        TranscriptionResult? existing = transcriptPath is null
            ? null
            : JsonSerializer.Deserialize<TranscriptionResult>(
                await File.ReadAllTextAsync(transcriptPath, cancellationToken).ConfigureAwait(false)
            );
        if (existing is not null)
        {
            refreshDiarization = SavedSettingsSignature(existing, "diarization_settings_signature")
                != DiarizationSettingsSignature(options);
            recoverSavedSpeech = options.RetryRepetitiveChunks
                    && options.MaxRetryChunks > 0
                    && (existing.PostProcessing?.Version ?? 0) < 8
                    && TranscriptPostProcessor.SuspectRepetitionWords(existing.RawWords ?? existing.Words) >= 24;
            refresh = options.ForceRegenerateTranscript
                || refreshDiarization
                || SavedSettingsSignature(existing, "transcription_settings_signature") != TranscriptionSettingsSignature(options)
                || TranscriptAlignment.NeedsRefresh(existing, options) || recoverSavedSpeech;
        }
        string? generatedRttm = null;
        var postOptions = await PrepareGameContextForTranscriptionAsync(
                gamePath,
                transcriptPath ?? transcriptOutputPath,
                options.PostProcessing with
                {
                    GamePath = gamePath,
                },
                log,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (generated || refresh)
        {
            string? audioPath =
                File.Exists(gamePath + "_Audio.wav") ? gamePath + "_Audio.wav"
                : existing is not null && File.Exists(existing.AudioFile) ? existing.AudioFile
                : null;
            if (audioPath is null)
            {
                try
                {
                    audioPath = FindRecordingAudio(gamePath);
                }
                catch (FileNotFoundException)
                    when (!generated && !options.ForceRegenerateTranscript && !refreshDiarization)
                {
                    log?.Invoke(
                        "Audio decoding needs the original recording. Applying readability cleanup to the saved transcript instead."
                    );
                }
            }
            if (audioPath is not null)
            {
                string target = transcriptPath ?? transcriptOutputPath;
                string? reuseRttm = refreshDiarization
                    ? null
                    : options.ReuseExistingRttmPath
                    ?? (
                        generated
                            ? null
                            : FindRecordingRttm(
                                gamePath,
                                target,
                                transcriptOutputPath,
                                null,
                                options.RttmOutputPath
                            )
                    );
                if (refresh)
                {
                    log?.Invoke(
                        "Regenerating the saved transcript with the selected settings; Whisper will decode the audio again."
                    );
                    if (options.Alignment.KeepPreDtwBackup)
                    {
                        string backup = target + (recoverSavedSpeech ? ".pre-readability-v8.bak" : ".pre-dtw.bak");
                        if (!File.Exists(backup))
                            File.Copy(target, backup);
                    }
                }
                else
                    log?.Invoke("Generating the missing transcript…");
                var result = await generateTranscript(
                        audioPath,
                        options with
                        {
                            OutputJsonPath = target,
                            ReuseExistingRttmPath = reuseRttm,
                            ReusedRttmAudioTimelineShiftSeconds = options.ReuseExistingRttmPath is null
                                && reuseRttm is not null && existing is not null
                                ? existing.AudioTimelineShiftSeconds : null,
                            ReusedDiarizationSignature = options.ReuseExistingRttmPath is null
                                && existing is not null
                                ? SavedSettingsSignature(existing, "diarization_settings_signature") : null,
                            PostProcessing = postOptions,
                            DeferPostProcessing = true,
                        },
                        log,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                transcriptPath = target;
                generatedRttm = result.RttmPath;
                generated = true;
            }
        }
        else
            log?.Invoke("Reusing the existing transcript and saved word timings.");

        string rolesPath = GetRoleSidecarPath(transcriptPath!, "_speaker_roles.json");
        string? roleContext = postOptions.GameContextPath;
        string? rttmPath = FindRecordingRttm(
            gamePath,
            transcriptPath!,
            transcriptOutputPath,
            generatedRttm,
            options.RttmOutputPath
        );
        if (rttmPath is null && postOptions.Enabled)
            log?.Invoke("No RTTM found; using transcript-only speaker repair.");

        // Explicit outputPath avoids a _cleaned copy that the comms panel would not read.
        // PostProcessTranscriptAsync starts from aligned_words (or raw_words), so repeated clicks do not compound edits.
        log?.Invoke("Post-processing the transcript…");
        TranscriptionResult transcript = await PostProcessTranscriptAsync(
                transcriptPath!,
                transcriptPath,
                rttmPath,
                postOptions,
                log,
                cancellationToken
            )
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        EmotionAnalysisResult emotion = await EnsureEmotionAnalysisAsync(gamePath, transcript, options.Emotion,
            options.FfmpegExecutable, log, cancellationToken).ConfigureAwait(false);

        try
        {
            if (
                !await NeedsSpeakerRoleRefreshAsync(
                        transcript,
                        rolesPath,
                        roleContext,
                        cancellationToken,
                        postOptions.AliasesPath
                    )
                    .ConfigureAwait(false)
            )
            {
                log?.Invoke(
                    "Speaker roles are current or manually preserved; keeping the existing file."
                );
                return new RecordingTranscriptionResult(
                    transcript,
                    generated,
                    rolesPath,
                    false,
                    null,
                    emotion
                );
            }
            log?.Invoke(
                File.Exists(rolesPath)
                    ? "Refreshing outdated speaker roles…"
                    : "Generating missing speaker roles…"
            );
            // A missing context is optional. AssignSpeakerRolesAsync will use word weights.
            await AssignSpeakerRolesAsync(
                    transcriptPath!,
                    gameContextPath: roleContext,
                    cancellationToken: cancellationToken,
                    aliasesPath: postOptions.AliasesPath
                )
                .ConfigureAwait(false);
            log?.Invoke("Speaker roles saved.");
            var emotionReport = CommsEmotion.TryRead(CommsEmotion.ReportPath(transcript.JsonPath));
            if (CommsEmotion.Valid(emotionReport))
                await CommsOutlook.SaveForTranscriptAsync(gamePath, transcript, emotionReport!, cancellationToken).ConfigureAwait(false);
            return new RecordingTranscriptionResult(transcript, generated, rolesPath, true, null, emotion);
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or ArgumentException
                        or JsonException
            )
        {
            // Keep the completed transcript and let the next click retry just the missing role step.
            log?.Invoke(
                $"Transcript saved, but speaker roles could not be generated: {ex.Message}"
            );
            return new RecordingTranscriptionResult(
                transcript,
                generated,
                rolesPath,
                false,
                ex.Message,
                emotion
            );
        }
    }

    // Context is prepared before Whisper runs, including for direct TranscribeAsync callers.
    internal static async Task<TranscriptPostProcessingOptions> PrepareGameContextForTranscriptionAsync(
        string? gamePath,
        string transcriptPath,
        TranscriptPostProcessingOptions options,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        if (gamePath is null)
            return options;
        await RecordedDraftRecovery.TryRecoverAsync(gamePath, log, cancellationToken).ConfigureAwait(false);
        if (options.GameContextPath is not null)
            return options;
        string? contextPath = null;
        try
        {
            log?.Invoke("Preparing game context and aliases…");
            contextPath = await CreateGameContextAsync(
                    gamePath,
                    transcriptPath,
                    options.DataDragonDirectory,
                    cancellationToken,
                    options.AliasesPath
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or Newtonsoft.Json.JsonException
                        or HttpRequestException
            )
        {
            log?.Invoke(
                $"Could not build role context; using recorded match vocabulary and word-based roles: {ex.Message}"
            );
        }
        return options with { GamePath = gamePath, GameContextPath = contextPath };
    }

    /// <summary>Uses the same current/legacy transcript selection as the Transcribe button and comms panel.</summary>
    public static async Task<string?> FindExistingTranscriptAsync(
        string gamePath,
        string transcriptOutputPath,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        gamePath = Path.GetFullPath(gamePath);
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        string[] candidates =
        [
            Path.GetFullPath(transcriptOutputPath),
            gamePath + "_transcript.json",
            gamePath + "_Audio_transcript.json",
        ];
        foreach (string path in candidates.Distinct(comparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                );
                using JsonDocument json = await JsonDocument
                    .ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                JsonElement root = json.RootElement;
                bool hasWords =
                    root.ValueKind == JsonValueKind.Object
                    && (
                        (
                            root.TryGetProperty("words", out var words)
                            && words.ValueKind == JsonValueKind.Array
                        )
                        || (
                            root.TryGetProperty("raw_words", out var raw)
                            && raw.ValueKind == JsonValueKind.Array
                        )
                    );
                if (!hasWords)
                    throw new InvalidDataException("The transcript has no words array.");
                var transcript = root.Deserialize<TranscriptionResult>()!;
                var entries = transcript.RawWords ?? transcript.Words;
                if (
                    entries is null
                    || entries.Any(w =>
                        w is null
                        || w.Word is null
                        || !double.IsFinite(w.Start)
                        || !double.IsFinite(w.End)
                        || w.Start < 0
                        || w.End < w.Start
                    )
                )
                    throw new InvalidDataException("The transcript contains invalid word entries.");
                // An empty words array is a valid result for a silent recording.
                return path;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            { }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                log?.Invoke($"Transcript is unusable ({Path.GetFileName(path)}): {ex.Message}");
            }
        }
        return null;
    }

    private static string FindRecordingAudio(string gamePath)
    {
        string expected = gamePath + "_Audio.wav";
        if (File.Exists(expected))
            return expected;
        string folder = Path.GetDirectoryName(gamePath)!;
        string[] alternatives = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*_Audio.wav").ToArray()
            : [];
        if (alternatives.Length == 1)
            return alternatives[0];
        if (alternatives.Length > 1)
            throw new InvalidDataException(
                $"Multiple comms WAV files were found. Expected: {expected}"
            );
        throw new FileNotFoundException(
            "The transcript is missing or unusable, and its comms WAV could not be found.",
            expected
        );
    }

    private static string? FindRecordingRttm(
        string gamePath,
        string transcriptPath,
        string outputPath,
        string? generatedPath,
        string? configuredPath
    )
    {
        string?[] candidates =
        [
            generatedPath,
            configuredPath,
            Path.ChangeExtension(transcriptPath, ".rttm"),
            Path.ChangeExtension(outputPath, ".rttm"),
            gamePath + "_transcript.rttm",
            gamePath + "_Audio_transcript.rttm",
            gamePath + "_Audio.rttm",
        ];
        return candidates
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!))
            .FirstOrDefault(File.Exists);
    }
}

public sealed record RecordingTranscriptionResult(
    TranscriptionResult Transcript,
    bool TranscriptGenerated,
    string SpeakerRolesPath,
    bool SpeakerRolesGenerated,
    string? SpeakerRoleError,
    EmotionAnalysisResult? Emotion = null
);

#endregion

#region Audio decoding and result models

public static partial class TranscribeAudio
{
    private const int SampleRate = 16_000;
    private const double SecondsPerWhisperTick = 0.01;
    private const string LeaguePrompt = "League of Legends team voice comms.";

    private static readonly object OutputLock = new();
    private static readonly JsonSerializerOptions TranscriptJsonOptions = new()
    {
        WriteIndented = true,
    };

    public static async Task<TranscriptionResult> TranscribeAsync(
        string recordingPath,
        TranscriptionOptions? options = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingPath);
        options ??= new TranscriptionOptions();
        ValidateInferenceOptions(options);
        if (
            !Enum.IsDefined(options.DeviceMode)
            || options.GpuDevice < 0
            || !Enum.IsDefined(options.NemoDeviceMode)
            || options.NemoGpuDevice < 0
        )
            throw new ArgumentException("Invalid transcription device settings.");
        TranscriptionAcceleration.ConfigureRuntime(options.DeviceMode);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ChunkSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(options.OverlapSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            options.OverlapSeconds,
            options.ChunkSeconds
        );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BeamSize);
        ArgumentOutOfRangeException.ThrowIfNegative(options.CpuThreads);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.RetryChunkSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxRetryChunks);

        string audioPath = RequireFile(Path.GetFullPath(recordingPath), "Input recording");
        string ffmpegPath = RequireAsset(options.FfmpegExecutable, "FFmpeg executable");
        TranscriptAlignment.Validate(options.Alignment, options.OverlapSeconds);
        string diarizationModelPath = Path.GetFullPath(
            options.DiarizationModelPath,
            AppContext.BaseDirectory
        );
        string? existingRttm = string.IsNullOrWhiteSpace(options.ReuseExistingRttmPath)
            ? null
            : RequireFile(Path.GetFullPath(options.ReuseExistingRttmPath), "Existing RTTM");
        string whisperModelPath = RequireAsset(options.WhisperModelPath, "Whisper GGML model");
        string? vadModelPath = options.EnableVad
            ? RequireAsset(options.VadModelPath, "Silero VAD model")
            : null;
        int threads =
            options.CpuThreads > 0
                ? options.CpuThreads
                : Math.Max(1, Environment.ProcessorCount - 1);

        string jsonPath = string.IsNullOrWhiteSpace(options.OutputJsonPath)
            ? Path.ChangeExtension(audioPath, null) + "_transcript.json"
            : Path.GetFullPath(options.OutputJsonPath);
        string rttmPath = string.IsNullOrWhiteSpace(options.RttmOutputPath)
            ? Path.ChangeExtension(jsonPath, ".rttm")
            : Path.GetFullPath(options.RttmOutputPath);
        var paths = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
        )
        {
            audioPath,
            jsonPath,
            rttmPath,
        };
        if (paths.Count != 3)
            throw new ArgumentException(
                "The input recording, JSON, and RTTM paths must be different."
            );

        var postOptions = await PrepareGameContextForTranscriptionAsync(
                LeagueMatchVocabulary.InferGamePath(audioPath, options.PostProcessing.GamePath),
                jsonPath,
                options.PostProcessing,
                log,
                cancellationToken
            )
            .ConfigureAwait(false);
        options = options with { PostProcessing = postOptions };
        // Vocabulary conditioning is also used when transcript cleanup is disabled.
        TranscriptVocabulary vocabulary = await TranscriptVocabulary
            .LoadAsync(audioPath, postOptions, log, cancellationToken)
            .ConfigureAwait(false);
        string prompt = BuildPrompt(vocabulary, options);
        if (options.SaveTranscriptionContext)
            await WriteRoleJsonAsync(
                    GetRoleSidecarPath(jsonPath, "_transcription_context.json"),
                    new
                    {
                        Version = 1,
                        Prompt = prompt,
                        MaxPromptCharacters = options.MaxPromptCharacters,
                        Note = "Only the compact prompt is sent to Whisper. The remaining fields document all relevant candidates. The character cap is not an exact token budget.",
                        Vocabulary = vocabulary.Match,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        log?.Invoke($"Whisper vocabulary prompt prepared ({prompt.Length} characters).");

        string jsonFolder = Path.GetDirectoryName(jsonPath)!;
        string rttmFolder = Path.GetDirectoryName(rttmPath)!;
        Directory.CreateDirectory(jsonFolder);
        Directory.CreateDirectory(rttmFolder);
        string stageName = $".transcription-{Guid.NewGuid():N}";
        string stagedJson = Path.Combine(jsonFolder, stageName + ".json");
        string stagedRttm = Path.Combine(rttmFolder, stageName + ".rttm");
        DirectoryInfo workDirectory = Directory.CreateTempSubdirectory("league-transcription-");
        string normalizedWav = Path.Combine(workDirectory.FullName, "comms.wav");
        string workRttm = Path.Combine(workDirectory.FullName, "comms.rttm");
        TranscriptionExecutionInfo? diarizationExecution = null;

        try
        {
            log?.Invoke("Converting recording to mono 16 kHz PCM.");
            // These timestamps belong to the exact WAV snapshot normalized here,
            // even if a saved review correction changes the source during the
            // much longer diarization/Whisper pass below.
            var timelineGate = VideoReviewTiming.AudioTimelineGateForFile(audioPath);
            await timelineGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            double audioTimelineShiftSeconds;
            try
            {
                audioTimelineShiftSeconds = VideoReviewTiming.AudioShiftForFile(audioPath);
                await RunProcessAsync(
                        ffmpegPath,
                        new[]
                        {
                            "-hide_banner",
                            "-loglevel",
                            "error",
                            "-nostdin",
                            "-y",
                            "-i",
                            audioPath,
                            "-map",
                            "0:a:0",
                            "-vn",
                            "-ac",
                            "1",
                            "-ar",
                            "16000",
                            "-c:a",
                            "pcm_s16le",
                            normalizedWav,
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (VideoReviewTiming.AudioShiftForFile(audioPath) != audioTimelineShiftSeconds)
                    throw new IOException("The recording's audio timing changed while preparing transcription. Retry Transcribe after the timing correction finishes.");
            }
            finally { timelineGate.Release(); }

            float[] samples;
            await using (FileStream stream = File.OpenRead(normalizedWav))
            {
                var wave = new WaveParser(stream);
                samples = await wave.GetAvgSamplesAsync(cancellationToken).ConfigureAwait(false);
            }
            if (samples.Length == 0)
                throw new InvalidDataException("The recording contains no audio samples.");

            if (existingRttm is not null)
            {
                log?.Invoke("Reusing the existing RTTM; NeMo will not run again.");
                File.Copy(existingRttm, workRttm);
                // Automatic reuse compares the old RTTM's source timeline with
                // the WAV snapshot actually normalized for this run. A review
                // correction may have completed after the pipeline was started.
                double reusedRttmShiftSeconds = options.ReusedRttmAudioTimelineShiftSeconds is double sourceShift
                    ? audioTimelineShiftSeconds - sourceShift : options.ReusedRttmShiftSeconds;
                if (Math.Abs(reusedRttmShiftSeconds) > 0.000001)
                {
                    var shifted = ShiftSpeakerTimeline(ReadRttm(workRttm)
                        .Select(t => new TranscriptSpeakerTurn(t.Start, t.End, t.Speaker)),
                        reusedRttmShiftSeconds, samples.Length / (double)SampleRate);
                    await File.WriteAllLinesAsync(workRttm, shifted.Select(t =>
                        "SPEAKER recording 1 " + t.Start.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + " " +
                        (t.End - t.Start).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) +
                        " <NA> <NA> " + t.Speaker + " <NA> <NA>"), cancellationToken).ConfigureAwait(false);
                    log?.Invoke("Moved reused speaker timestamps onto the corrected WAV timeline; speaker IDs were preserved.");
                }
                diarizationExecution = new()
                {
                    Stage = "NeMo",
                    Device = "not_run",
                    Runtime = "Existing RTTM",
                    RequestedMode = options.NemoDeviceMode.ToString(),
                    Detail = "Existing RTTM reused; no NeMo inference ran for this job.",
                };
                options.ExecutionProgress?.Report(diarizationExecution);
            }
            else
            {
                log?.Invoke("Running NeMo speaker diarization.");
                diarizationExecution = await NemoAcceleration
                    .RunDiarizationAsync(
                        normalizedWav,
                        RequireFile(diarizationModelPath, "NeMo diarization model"),
                        workRttm,
                        options,
                        log,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            RequireFile(workRttm, "RTTM output from NeMo-Speech");
            List<DiarizationTurn> turns = ReadRttm(workRttm);
            var speakers = turns
                .Select(turn => turn.Speaker)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(speaker => speaker, StringComparer.Ordinal)
                .ToArray();

            List<AudioRange> speech = await FindSpeechAsync(
                    samples,
                    vadModelPath,
                    threads,
                    options.Vad,
                    cancellationToken
                )
                .ConfigureAwait(false);
            log?.Invoke(
                $"NeMo found {speakers.Length} speaker(s). Running Whisper on {speech.Count} speech region(s)."
            );
            NativeDecode decoded = await TranscribeChunksAsync(
                    samples,
                    speech,
                    whisperModelPath,
                    prompt,
                    threads,
                    options,
                    log,
                    cancellationToken
                )
                .ConfigureAwait(false);
            List<WordResult> words = decoded.Words;
            if (decoded.DtwActive)
                TranscriptAlignment.ConstrainChunkOrder(words);
            foreach (WordResult word in decoded.RawWords)
                word.Speaker = AssignSpeaker(word.Start, word.End, turns);
            BindDtwSpeakers(words, turns);
            var alignment = TranscriptAlignment.Report(
                options,
                decoded.RawWords,
                words,
                decoded.DtwActive,
                decoded.HeadsPreset,
                decoded.Warning
            );
            if (
                options.Alignment.Enabled
                && options.Alignment.RequireAlignment
                && words.Count > 0
                && alignment.AlignedWords == 0
            )
                throw new InvalidOperationException(
                    "Native DTW returned no usable word anchors; existing transcript outputs were preserved."
                );
            log?.Invoke(alignment.Message);

            var result = new TranscriptionResult
            {
                AudioFile = audioPath,
                AudioTimelineShiftSeconds = audioTimelineShiftSeconds,
                DurationSeconds = Math.Round((double)samples.Length / SampleRate, 3),
                Language = options.Language,
                AsrEngine = "Whisper.net / whisper.cpp",
                AsrModel = Path.GetFileName(whisperModelPath),
                DiarizationEngine = existingRttm is null ? "NeMo-Speech.cpp" : "Existing RTTM",
                DiarizationModel = diarizationModelPath,
                SampleRate = SampleRate,
                Speakers = speakers,
                WordCount = words.Count,
                Text = string.Join(" ", words.Select(word => word.Word.Trim())),
                Words = words,
                RawWords = decoded.RawWords,
                AlignedWords = words.Select(TranscriptPostProcessor.CopyWord).ToList(),
                Alignment = alignment,
                Execution = decoded.Execution,
                DiarizationExecution = diarizationExecution,
                AdditionalData = new()
                {
                    ["transcription_settings_signature"] = JsonSerializer.SerializeToElement(TranscriptionSettingsSignature(options)),
                    ["diarization_settings_signature"] = JsonSerializer.SerializeToElement(existingRttm is null
                        ? DiarizationSettingsSignature(options) : options.ReusedDiarizationSignature),
                },
                JsonPath = jsonPath,
                RttmPath = rttmPath,
            };

            if (options.PostProcessing.Enabled && !options.DeferPostProcessing)
            {
                log?.Invoke("Cleaning transcription loops, League terms, and speaker gaps.");
                result = TranscriptPostProcessor.Process(
                    result,
                    options.PostProcessing,
                    turns
                        .Select(turn => new TranscriptSpeakerTurn(
                            turn.Start,
                            turn.End,
                            turn.Speaker
                        ))
                        .ToArray(),
                    vocabulary,
                    cancellationToken
                );
                log?.Invoke(result.PostProcessing!.Summary);
            }

            File.Copy(workRttm, stagedRttm);
            await using (FileStream json = File.Create(stagedJson))
            {
                await JsonSerializer
                    .SerializeAsync(json, result, TranscriptJsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            lock (OutputLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SaveOutputs(stagedJson, jsonPath, stagedRttm, rttmPath);
            }
            if (!options.DeferPostProcessing)
                await EnsureEmotionAnalysisAsync(options.PostProcessing.GamePath ?? audioPath, result,
                    options.Emotion, options.FfmpegExecutable, log, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            TryDelete(stagedJson);
            TryDelete(stagedRttm);
            try
            {
                workDirectory.Delete(recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Task<List<AudioRange>> FindSpeechAsync(
        float[] samples,
        string? vadModelPath,
        int threads,
        SpeechDetectionOptions settings,
        CancellationToken cancellationToken
    ) =>
        // VAD uses whisper.cpp's process-wide logger too. Keep its CPU backend
        // messages outside another job's speech-recognition evidence scope.
        TranscriptionAcceleration.RunNativeExclusiveAsync(
            () => FindSpeechCoreAsync(samples, vadModelPath, threads, settings, cancellationToken),
            cancellationToken
        );

    private static async Task<List<AudioRange>> FindSpeechCoreAsync(
        float[] samples,
        string? vadModelPath,
        int threads,
        SpeechDetectionOptions settings,
        CancellationToken cancellationToken
    )
    {
        double duration = (double)samples.Length / SampleRate;
        if (vadModelPath is null)
            return new List<AudioRange> { new(0, duration) };

        using var factory = TranscriptionAcceleration.InitializeNative(() =>
            WhisperVadFactory.FromPath(vadModelPath, new WhisperFactoryOptions { UseGpu = false })
        );
        using var vad = TranscriptionAcceleration.InitializeNative(() =>
            factory
                .CreateBuilder()
                .WithUseGpu(false)
                .WithThreads(threads)
                .WithThreshold((float)settings.Threshold)
                .WithMinSpeechDuration(TimeSpan.FromMilliseconds(settings.MinSpeechDurationMs))
                .WithMinSilenceDuration(TimeSpan.FromMilliseconds(settings.MinSilenceDurationMs))
                .WithSpeechPadding(TimeSpan.FromMilliseconds(settings.SpeechPaddingMs))
                .Build()
        );
        var segments = await vad.DetectSpeechAsync(samples, cancellationToken)
            .ConfigureAwait(false);

        var speech = new List<AudioRange>();
        foreach (VadSegmentData segment in segments)
        {
            double start = Math.Clamp(segment.Start.TotalSeconds, 0, duration);
            double end = Math.Clamp(segment.End.TotalSeconds, start, duration);
            if (end > start)
                speech.Add(new AudioRange(start, end));
        }
        return speech;
    }

    private sealed record NativeDecode(
        List<WordResult> RawWords,
        List<WordResult> Words,
        bool DtwActive,
        string HeadsPreset,
        string? Warning,
        TranscriptionExecutionInfo? Execution
    );

    private static async Task<NativeDecode> TranscribeChunksAsync(
        float[] samples,
        List<AudioRange> speech,
        string modelPath,
        string prompt,
        int threads,
        TranscriptionOptions options,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rawWords = new List<WordResult>();
        var words = new List<WordResult>();
        if (speech.Count == 0)
            return new(rawWords, words, options.Alignment.Enabled, "", null, null);
        bool dtwActive = options.Alignment.Enabled;
        string preset = "";
        string? warning = null;
        var factoryOptions = new WhisperFactoryOptions { UseFlashAttention = false };
        if (dtwActive)
        {
            try
            {
                var heads = TranscriptAlignment.DetectPreset(
                    modelPath,
                    options.Alignment.HeadsPreset
                );
                preset = heads.ToString();
                factoryOptions.UseDtwTimeStamps = true;
                factoryOptions.HeadsPreset = heads;
                factoryOptions.DtwMemSize = checked(
                    (uint)options.Alignment.DtwMemoryMb * 1024U * 1024U
                );
            }
            catch (InvalidDataException ex) when (!options.Alignment.RequireAlignment)
            {
                dtwActive = false;
                warning = "DTW disabled for this decode: " + ex.Message;
                log?.Invoke(warning);
            }
        }
        WhisperProcessor CreateProcessor(WhisperFactory factory) =>
            factory
                .CreateBuilder()
                .WithLanguage(options.Language)
                .WithThreads(threads)
                .WithNoContext()
                .WithPrompt(prompt)
                .WithCarryInitialPrompt(true)
                .WithTemperature(0)
                .WithTemperatureInc(0)
                .WithTokenTimestamps()
                .WithProbabilities()
                .WithBeamSearchSamplingStrategy(beam => beam.WithBeamSize(options.BeamSize))
                .Build();
        TranscriptionAcceleration.WhisperModelSession session;
        try
        {
            session = TranscriptionAcceleration.OpenModel(
                modelPath,
                factoryOptions,
                options,
                CreateProcessor,
                log,
                cancellationToken
            );
        }
        catch (WhisperModelLoadException) when (dtwActive && !options.Alignment.RequireAlignment)
        {
            dtwActive = false;
            warning =
                "DTW context could not initialize; retrying with ordinary Whisper token timestamps.";
            log?.Invoke(warning);
            session = TranscriptionAcceleration.OpenModel(
                modelPath,
                new WhisperFactoryOptions { UseFlashAttention = false },
                options,
                CreateProcessor,
                log,
                cancellationToken
            );
        }
        TranscriptionExecutionInfo execution = session.Execution;
        await using (session)
        {
            var factory = session.Factory;
            var processor = session.Processor;
            double coreSeconds = TranscriptAlignment.CoreSeconds(options, dtwActive);
            log?.Invoke(
                dtwActive
                    ? $"Native Whisper DTW enabled ({preset}); {coreSeconds:F0}s core windows with {options.OverlapSeconds}s context."
                    : "Using ordinary Whisper token timestamps."
            );
            int retriedChunks = 0;
            bool reportedDevice = false;
            double duration = (double)samples.Length / SampleRate;
            for (double coreStart = 0; coreStart < duration; coreStart += coreSeconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double coreEnd = Math.Min(duration, coreStart + coreSeconds);
                if (!speech.Any(region => region.Start < coreEnd && region.End > coreStart))
                    continue;
                int first = Math.Max(
                    0,
                    (int)Math.Round((coreStart - options.OverlapSeconds) * SampleRate)
                );
                int last = (int)
                    Math.Min(
                        samples.Length,
                        Math.Round((coreEnd + options.OverlapSeconds) * SampleRate)
                    );
                double offset = (double)first / SampleRate,
                    readEnd = (double)last / SampleRate;
                log?.Invoke(
                    $"Whisper{(dtwActive ? " + DTW" : "")} chunk {coreStart:F1}-{coreEnd:F1}s."
                );
                var raw = new List<WordResult>();
                await foreach (
                    SegmentData segment in processor
                        .ProcessAsync(samples.AsMemory(first, last - first), cancellationToken)
                        .ConfigureAwait(false)
                )
                    AddSegmentWords(segment, offset, readEnd, dtwActive, raw);
                execution = session.Execution;
                if (!reportedDevice)
                {
                    options.ExecutionProgress?.Report(execution);
                    log?.Invoke(
                        $"Whisper device: {execution.Device.ToUpperInvariant()} · runtime {execution.Runtime} · {execution.Backend}. {execution.Detail}"
                    );
                    reportedDevice = true;
                }
                var aligned = TranscriptAlignment.Apply(
                    raw,
                    offset,
                    readEnd,
                    options.Alignment,
                    dtwActive
                );
                SelectCoreWords(raw, aligned, coreStart, coreEnd);
                int suspect = TranscriptPostProcessor.SuspectRepetitionWords(aligned);
                if (
                    options.RetryRepetitiveChunks
                    && (suspect >= 24 || HasCollapsedTimingProblem(aligned))
                    && retriedChunks < options.MaxRetryChunks
                )
                {
                    retriedChunks++;
                    log?.Invoke(
                        $"Retrying repetitive or poorly timed Whisper chunk {coreStart:F1}-{coreEnd:F1}s in smaller windows."
                    );
                    await using var retry = TranscriptionAcceleration.InitializeNative(() =>
                        factory
                            .CreateBuilder()
                            .WithLanguage(options.Language)
                            .WithThreads(threads)
                            .WithNoContext()
                            .WithPrompt(LeaguePrompt)
                            .WithCarryInitialPrompt(false)
                            .WithTemperature(0)
                            .WithTemperatureInc(0)
                            .WithTokenTimestamps()
                            .WithProbabilities()
                            .WithBeamSearchSamplingStrategy(beam =>
                                beam.WithBeamSize(options.BeamSize)
                            )
                            .Build()
                    );
                    var alternative = new List<WordResult>();
                    var alternativeRaw = new List<WordResult>();
                    double retrySeconds = Math.Min(
                        options.RetryChunkSeconds,
                        dtwActive ? 27 : options.RetryChunkSeconds
                    );
                    for (
                        double retryStart = coreStart;
                        retryStart < coreEnd;
                        retryStart += retrySeconds
                    )
                    {
                        double retryEnd = Math.Min(coreEnd, retryStart + retrySeconds);
                        if (!speech.Any(region => region.Start < retryEnd && region.End > retryStart))
                            continue;
                        int a = Math.Max(0, (int)Math.Round((retryStart - 0.5) * SampleRate));
                        int b = (int)
                            Math.Min(samples.Length, Math.Round((retryEnd + 0.5) * SampleRate));
                        var retryRaw = new List<WordResult>();
                        await foreach (
                            SegmentData segment in retry
                                .ProcessAsync(samples.AsMemory(a, b - a), cancellationToken)
                                .ConfigureAwait(false)
                        )
                            AddSegmentWords(
                                segment,
                                (double)a / SampleRate,
                                (double)b / SampleRate,
                                dtwActive,
                                retryRaw
                            );
                        var retryAligned = TranscriptAlignment.Apply(
                            retryRaw,
                            (double)a / SampleRate,
                            (double)b / SampleRate,
                            options.Alignment,
                            dtwActive
                        );
                        SelectCoreWords(retryRaw, retryAligned, retryStart, retryEnd);
                        alternativeRaw.AddRange(retryRaw);
                        alternative.AddRange(retryAligned);
                    }
                    if (ShouldUseRetry(aligned, alternative))
                    {
                        log?.Invoke(
                            $"Using retry: repetitive words {suspect} -> {TranscriptPostProcessor.SuspectRepetitionWords(alternative)}."
                        );
                        raw = alternativeRaw;
                        aligned = alternative;
                    }
                    else
                        log?.Invoke(
                            "Keeping initial decode; retry did not sufficiently improve the repeated hypothesis."
                        );
                }
                rawWords.AddRange(raw);
                words.AddRange(aligned);
            }
            // Retry states also initialize backends, so retain the final evidence.
            execution = session.Execution;
            options.ExecutionProgress?.Report(execution);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(rawWords, words, dtwActive, preset, warning, execution);
    }

    // Keep the same word indices in both streams, including at overlap-window boundaries.
    internal static void SelectCoreWords(
        List<WordResult> raw,
        List<WordResult> aligned,
        double start,
        double end
    )
    {
        for (int i = aligned.Count - 1; i >= 0; i--)
        {
            double midpoint = (aligned[i].Start + aligned[i].End) / 2;
            if (midpoint < start || midpoint >= end)
            {
                aligned.RemoveAt(i);
                raw.RemoveAt(i);
            }
        }
    }

    private static void BindDtwSpeakers(List<WordResult> words, List<DiarizationTurn> turns)
    {
        foreach (var word in words)
        {
            var candidates = turns.Where(t => t.Start < word.End && t.End > word.Start).ToList();
            word.Speaker = AssignSpeaker(
                word.Start,
                word.End,
                word.End > word.Start ? candidates : turns
            );
            word.SpeakerSource = word.TimingSource == "whisper_dtw" ? "rttm_dtw" : "rttm";
            word.OverlappingSpeech = candidates.Any(a =>
                candidates.Any(b =>
                    a.Speaker != b.Speaker
                    && Math.Min(word.End, Math.Min(a.End, b.End))
                        - Math.Max(word.Start, Math.Max(a.Start, b.Start))
                        > 0.03
                )
            );
        }
        TranscriptOverlap.Apply(words, turns.Select(t => new TranscriptSpeakerTurn(t.Start, t.End, t.Speaker)));
    }

    internal static bool ShouldUseRetry(
        IReadOnlyList<WordResult> original,
        IReadOnlyList<WordResult> candidate
    )
    {
        int before = TranscriptPostProcessor.SuspectRepetitionWords(original);
        int after = TranscriptPostProcessor.SuspectRepetitionWords(candidate);
        int count = candidate.Sum(w =>
            w.Word.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length
        );
        int unique = original
            .SelectMany(w => w.Word.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Select(TranscriptPostProcessor.Key)
            .Where(k => k.Length > 0)
            .Distinct()
            .Count();
        int originalCount = original.Sum(w =>
            w.Word.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length
        );
        // A fused token scores 24 to trigger retry, but only occupies one entry.
        // Do not let that synthetic score lower protection for the surrounding real speech.
        int nonRepetitiveCount = Math.Clamp(originalCount - before
            + TranscriptPostProcessor.FusedRepetitionEntries(original) * 23, 0, originalCount);
        int minimum = Math.Min(originalCount, Math.Max(
            Math.Max(4, Math.Min(12, unique / 2)),
            (int)Math.Ceiling(nonRepetitiveCount * 0.70)
        ));
        int collapsedBefore = original.Count(w => w.End - w.Start <= 0.005);
        int collapsedAfter = candidate.Count(w => w.End - w.Start <= 0.005);
        var remaining = original.SelectMany(w => w.Word.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Select(TranscriptPostProcessor.Key).Where(k => k.Length > 0)
            .GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        int shared = 0;
        foreach (string key in candidate.SelectMany(w => w.Word.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Select(TranscriptPostProcessor.Key).Where(k => k.Length > 0))
            if (remaining.TryGetValue(key, out int available) && available > 0)
            { shared++; remaining[key] = available - 1; }
        // A timing-only retry must preserve the recognized call. Better-looking
        // timestamps alone are not enough to replace it with unrelated words.
        bool agrees = shared >= Math.Ceiling(nonRepetitiveCount * .70);
        bool improved = before >= 24 && after <= before / 2
            || HasCollapsedTimingProblem(original) && agrees && after <= before
                && collapsedAfter <= collapsedBefore / 2;
        return improved
            && count >= minimum
            && candidate.All(w =>
                double.IsFinite(w.Start) && double.IsFinite(w.End) && w.Start >= 0 && w.End >= w.Start
            );
    }

    internal static bool HasCollapsedTimingProblem(IReadOnlyList<WordResult> words) =>
        // Short calls are common in comms. The old 20-word minimum silently
        // accepted entire short phrases collapsed onto a single timestamp.
        words.Count >= 2 && words.Count(w => w.End - w.Start <= 0.005) >= Math.Max(2, words.Count * 0.25);

    internal static void AddSegmentWords(
        SegmentData segment,
        double offset,
        double readEnd,
        bool dtwActive,
        List<WordResult> output
    )
    {
        double segmentStart = Math.Clamp(offset + segment.Start.TotalSeconds, offset, readEnd);
        double segmentEnd = Math.Clamp(offset + segment.End.TotalSeconds, segmentStart, readEnd);
        var segmentWords = new List<WordResult>();
        var text = new StringBuilder();
        double start = 0,
            end = 0,
            totalProbability = 0;
        int tokenCount = 0,
            probabilityCount = 0,
            spokenTokens = 0,
            anchoredTokens = 0;
        double? firstAnchor = null,
            lastAnchor = null;
        bool invalidAnchor = false,
            ordinaryEstimated = false;

        void FlushWord()
        {
            if (tokenCount == 0)
                return;
            segmentWords.Add(
                new WordResult
                {
                    Start = start,
                    End = end,
                    Word = text.ToString(),
                    Probability =
                        probabilityCount == 0 ? null : totalProbability / probabilityCount,
                    TimingSource = ordinaryEstimated ? "segment_estimate" : "whisper_token",
                    DtwFirstTokenTime =
                        dtwActive
                        && !invalidAnchor
                        && spokenTokens > 0
                        && spokenTokens == anchoredTokens
                            ? firstAnchor
                            : null,
                    DtwLastTokenTime =
                        dtwActive
                        && !invalidAnchor
                        && spokenTokens > 0
                        && spokenTokens == anchoredTokens
                            ? lastAnchor
                            : null,
                }
            );
            text.Clear();
            tokenCount = 0;
            totalProbability = 0;
            probabilityCount = 0;
            spokenTokens = 0;
            anchoredTokens = 0;
            firstAnchor = lastAnchor = null;
            invalidAnchor = false;
            ordinaryEstimated = false;
        }

        foreach (WhisperToken token in segment.Tokens)
        {
            string? part = token.Text;
            if (
                string.IsNullOrEmpty(part)
                || part.StartsWith("[_", StringComparison.Ordinal)
                || part.StartsWith("<|", StringComparison.Ordinal)
            )
                continue;

            if (char.IsWhiteSpace(part[0]))
                FlushWord();
            if (string.IsNullOrWhiteSpace(part))
            {
                text.Append(part);
                continue;
            }

            if (part.Contains('\uFFFD'))
            {
                segmentWords.Clear();
                tokenCount = 0;
                break;
            }

            // whisper.cpp token t0 and t1 are in 10 ms units relative to the chunk.
            bool ordinaryValid = token.Start >= 0 && token.End >= token.Start;
            if (!ordinaryValid)
                ordinaryEstimated = true;
            double tokenStart = ordinaryValid
                ? Math.Clamp(offset + token.Start * SecondsPerWhisperTick, segmentStart, segmentEnd)
                : segmentStart;
            double tokenEnd = ordinaryValid
                ? Math.Clamp(offset + token.End * SecondsPerWhisperTick, tokenStart, segmentEnd)
                : tokenStart;
            if (dtwActive && part.Any(char.IsLetterOrDigit))
            {
                spokenTokens++;
                double anchor = offset + token.DtwTimestamp * SecondsPerWhisperTick;
                if (
                    token.DtwTimestamp >= 0
                    && anchor >= offset
                    && anchor <= readEnd
                    && (lastAnchor is null || anchor >= lastAnchor)
                )
                {
                    firstAnchor ??= anchor;
                    lastAnchor = anchor;
                    anchoredTokens++;
                }
                else
                    invalidAnchor = true;
            }
            if (tokenCount == 0)
            {
                start = tokenStart;
                end = tokenEnd;
            }
            else
            {
                start = Math.Min(start, tokenStart);
                end = Math.Max(end, tokenEnd);
            }

            text.Append(part);
            tokenCount++;
            if (float.IsFinite(token.Probability))
            {
                totalProbability += token.Probability;
                probabilityCount++;
            }
        }
        FlushWord();

        if (segmentWords.Count == 0 && !string.IsNullOrWhiteSpace(segment.Text))
        {
            // Keep the indivisible decoded span until post-processing can see its
            // following entry. Estimating here can interleave an overlapping word
            // with the middle of this sentence when the output is sorted by time.
            segmentWords.Add(
                new WordResult
                {
                    Start = segmentStart,
                    End = segmentEnd,
                    Word = segment.Text,
                    Probability = float.IsFinite(segment.Probability) ? segment.Probability : null,
                    TimingSource = "segment",
                }
            );
        }

        output.AddRange(segmentWords);
    }

    private static string BuildPrompt(TranscriptVocabulary vocabulary, TranscriptionOptions options)
    {
        IEnumerable<string> extra = string.IsNullOrWhiteSpace(options.VocabularyPath)
            ? []
            : File.ReadLines(RequireAsset(options.VocabularyPath, "League vocabulary file"))
                .Select(line => line.Trim().Trim(',', ';', '\t', ' '))
                .Where(term => term.Length > 0 && !term.StartsWith('#'));
        return (vocabulary.Match ?? new LeagueMatchVocabulary()).BuildPrompt(
            LeaguePrompt,
            extra,
            options.MaxPromptCharacters
        );
    }

    private static List<DiarizationTurn> ReadRttm(string path)
    {
        var turns = new List<DiarizationTurn>();
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (
                fields.Length < 8
                || !fields[0].Equals("SPEAKER", StringComparison.OrdinalIgnoreCase)
                || !double.TryParse(
                    fields[3],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double start
                )
                || !double.TryParse(
                    fields[4],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double duration
                )
                || start < 0
                || duration <= 0
                || !double.IsFinite(start + duration)
            )
                continue;

            turns.Add(new DiarizationTurn(start, start + duration, fields[7]));
        }
        turns.Sort((first, second) => first.Start.CompareTo(second.Start));
        return turns;
    }

    private static string? AssignSpeaker(double start, double end, List<DiarizationTurn> turns)
    {
        if (end <= start)
        {
            start -= 0.025;
            end += 0.025;
        }
        // Aggregate all turns of each speaker; comparing individual fragments
        // made one split turn lose to a short competing label inside a word.
        var intervals = new Dictionary<string, List<(double Start, double End)>>(StringComparer.Ordinal);
        foreach (DiarizationTurn turn in turns)
        {
            if (turn.Start >= end) break;
            double a = Math.Max(start, turn.Start), b = Math.Min(end, turn.End);
            if (b <= a) continue;
            if (!intervals.TryGetValue(turn.Speaker, out var spans)) intervals[turn.Speaker] = spans = new();
            spans.Add((a, b));
        }
        double Covered(List<(double Start, double End)> spans)
        {
            double total = 0, a = -1, b = -1;
            foreach (var span in spans.OrderBy(x => x.Start))
            {
                if (a < 0) { a = span.Start; b = span.End; }
                else if (span.Start <= b) b = Math.Max(b, span.End);
                else { total += b - a; a = span.Start; b = span.End; }
            }
            return total + (a < 0 ? 0 : b - a);
        }
        var ranked = intervals.Select(p => (Speaker: p.Key, Duration: Covered(p.Value)))
            .OrderByDescending(x => x.Duration).ThenBy(x => x.Speaker, StringComparer.Ordinal).ToArray();
        return ranked.Length == 0 || ranked.Length > 1 && ranked[0].Duration - ranked[1].Duration < 0.005
            ? null : ranked[0].Speaker;
    }

    private static async Task RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        string tool = Path.GetFileName(executable);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Could not start {tool}.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Could not start {tool}: {ex.Message}", ex);
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited) { }
            catch (Win32Exception) when (process.HasExited) { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string details = (await stderr.ConfigureAwait(false)).Trim();
            if (details.Length == 0)
                details = (await stdout.ConfigureAwait(false)).Trim();
            const int maxErrorLength = 2500;
            if (details.Length > maxErrorLength)
                details = details[^maxErrorLength..];
            throw new InvalidOperationException(
                $"{tool} exited with code {process.ExitCode}.{Environment.NewLine}{details}"
            );
        }
    }

    private static void SaveOutputs(
        string stagedJson,
        string jsonPath,
        string stagedRttm,
        string rttmPath
    )
    {
        bool replacingRttm = File.Exists(rttmPath);
        string rttmBackup = stagedRttm + ".bak";
        if (replacingRttm)
            File.Replace(stagedRttm, rttmPath, rttmBackup);
        else
            File.Move(stagedRttm, rttmPath);

        try
        {
            File.Move(stagedJson, jsonPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (replacingRttm)
                File.Move(rttmBackup, rttmPath, overwrite: true);
            else
                TryDelete(rttmPath);
            throw;
        }
        TryDelete(rttmBackup);
    }

    private static string RequireAsset(string path, string description) =>
        RequireFile(Path.GetFullPath(path, AppContext.BaseDirectory), description);

    private static string RequireFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{description} was not found: {path}", path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private readonly record struct AudioRange(double Start, double End);

    private readonly record struct DiarizationTurn(double Start, double End, string Speaker);
}

public sealed record TranscriptionOptions
{
    public string FfmpegExecutable { get; init; } = "tools/ffmpeg.exe";
    public string NemoSpeechExecutable { get; init; } = "runtime/nemo/bin/nemo-speech.exe";
    public string DiarizationModelPath { get; init; } = "models/Nemotron-3-Diarization.q8_0.gguf";
    public string WhisperModelPath { get; init; } = "models/ggml-large-v3-q5_0.bin";
    public string VadModelPath { get; init; } = "models/ggml-silero-v6.2.0.bin";
    public string Language { get; init; } = "en";
    public int BeamSize { get; init; } = 5;
    public int ChunkSeconds { get; init; } = 30;
    public int OverlapSeconds { get; init; } = 3;
    public int CpuThreads { get; init; }
    public TranscriptionDeviceMode DeviceMode { get; init; } = TranscriptionDeviceMode.Auto;
    public int GpuDevice { get; init; }
    public TranscriptionDeviceMode NemoDeviceMode { get; init; } = TranscriptionDeviceMode.Auto;
    public int NemoGpuDevice { get; init; }
    public IProgress<TranscriptionExecutionInfo>? ExecutionProgress { get; init; }

    // Retry severe repetition or collapsed timing with the existing model.
    public bool RetryRepetitiveChunks { get; init; } = true;
    public int RetryChunkSeconds { get; init; } = 6;
    public int MaxRetryChunks { get; init; } = 32;
    public bool EnableVad { get; init; } = true;
    public SpeechDetectionOptions Vad { get; init; } = new();
    public DiarizationSegmentationOptions Diarization { get; init; } = new();

    // Optional additional text vocabulary (one term per line). Match aliases are selected automatically.
    public string? VocabularyPath { get; init; }

    // Character budget, not tokens. Keep small because Whisper has a short initial-prompt window.
    public int MaxPromptCharacters { get; init; } = 850;
    public bool SaveTranscriptionContext { get; init; } = true;
    public string? OutputJsonPath { get; init; }
    public string? RttmOutputPath { get; init; }

    // The normal pipeline reuses RTTM when upgrading an existing transcript to native DTW.
    public string? ReuseExistingRttmPath { get; init; }
    public bool ForceRegenerateTranscript { get; init; }
    internal string? ReusedDiarizationSignature { get; init; }
    internal double ReusedRttmShiftSeconds { get; init; }
    internal double? ReusedRttmAudioTimelineShiftSeconds { get; init; }
    public TranscriptAlignmentOptions Alignment { get; init; } = new();
    public TranscriptPostProcessingOptions PostProcessing { get; init; } = new();
    public EmotionInferenceOptions Emotion { get; init; } = new();

    // The recording pipeline cleans both new and reused transcripts in one shared step.
    internal bool DeferPostProcessing { get; init; }
}

public sealed record SpeechDetectionOptions
{
    public double Threshold { get; init; } = 0.50;
    public int MinSpeechDurationMs { get; init; } = 250;
    public int MinSilenceDurationMs { get; init; } = 500;
    public int SpeechPaddingMs { get; init; } = 30;
}

public sealed record DiarizationSegmentationOptions
{
    // Completed recordings benefit from the checkpoint's larger-context preset.
    // This is independent of custom onset/offset thresholds and is sent only
    // when the installed runtime advertises the model's recorded-audio preset.
    public bool UseRecordedAudioContext { get; init; } = true;
    // Off preserves the installed runtime's segmentation thresholds and supports older executables.
    public bool Enabled { get; init; }
    public double Onset { get; init; } = 0.50;
    public double Offset { get; init; } = 0.40;
    public double MinDurationOnSeconds { get; init; } = 0.10;
    public double MinDurationOffSeconds { get; init; } = 0.15;
    public double PadOnsetSeconds { get; init; } = 0.05;
    public double PadOffsetSeconds { get; init; } = 0.05;
}

/// <summary>Only user-editable inference preferences; no output paths or runtime callbacks.</summary>
public sealed record TranscriptionSettings
{
    public int BeamSize { get; init; } = 5;
    public int ChunkSeconds { get; init; } = 30;
    public int OverlapSeconds { get; init; } = 3;
    public bool EnableVad { get; init; } = true;
    public SpeechDetectionOptions Vad { get; init; } = new();
    public DiarizationSegmentationOptions Diarization { get; init; } = new();
    public EmotionInferenceOptions Emotion { get; init; } = new();
    public bool RetryRepetitiveChunks { get; init; } = true;

    public TranscriptionOptions ToOptions() => new()
    {
        BeamSize = BeamSize,
        ChunkSeconds = ChunkSeconds,
        OverlapSeconds = OverlapSeconds,
        EnableVad = EnableVad,
        Vad = Vad,
        Diarization = Diarization,
        Emotion = Emotion,
        RetryRepetitiveChunks = RetryRepetitiveChunks,
    };

}

public static partial class TranscribeAudio
{
    public static void ValidateInferenceOptions(TranscriptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BeamSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ChunkSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(options.OverlapSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(options.OverlapSeconds, options.ChunkSeconds);
        TranscriptAlignment.Validate(options.Alignment, options.OverlapSeconds);
        if (options.Emotion is null) throw new ArgumentException("Emotion analysis settings are required.");
        options.Emotion.Validate();
        if (options.Vad is null || options.Diarization is null)
            throw new ArgumentException("Speech detection and diarization settings are required.");
        static bool Probability(double value) => double.IsFinite(value) && value > 0 && value < 1;
        static bool Duration(double value) => double.IsFinite(value) && value >= 0 && value <= 60;
        var vad = options.Vad;
        if (!Probability(vad.Threshold) || vad.MinSpeechDurationMs < 0 || vad.MinSpeechDurationMs > 60_000
            || vad.MinSilenceDurationMs <= 0 || vad.MinSilenceDurationMs > 60_000
            || vad.SpeechPaddingMs < 0 || vad.SpeechPaddingMs > 60_000)
            throw new ArgumentException("VAD needs a threshold between 0 and 1, nonnegative speech/padding durations, and positive silence duration (at most 60000 ms).");
        var diar = options.Diarization;
        if (!Probability(diar.Onset) || !Probability(diar.Offset)
            || !Duration(diar.MinDurationOnSeconds) || !Duration(diar.MinDurationOffSeconds)
            || !Duration(diar.PadOnsetSeconds) || !Duration(diar.PadOffsetSeconds))
            throw new ArgumentException("Diarization needs thresholds between 0 and 1 and durations from 0 to 60 seconds.");
    }

    internal static string TranscriptionSettingsSignature(TranscriptionOptions options) => SettingsHash(new
    {
        Alignment = TranscriptAlignment.Signature(options),
        ModelStamp = TranscriptAlignment.ModelStamp(options),
        options.EnableVad, options.Vad, options.RetryRepetitiveChunks,
        options.RetryChunkSeconds, options.MaxRetryChunks, options.MaxPromptCharacters,
        options.VocabularyPath, options.PostProcessing.Enabled,
    });

    internal static string DiarizationSettingsSignature(TranscriptionOptions options)
    {
        var model = new FileInfo(Path.GetFullPath(options.DiarizationModelPath, AppContext.BaseDirectory));
        var runtime = new FileInfo(Path.GetFullPath(options.NemoSpeechExecutable, AppContext.BaseDirectory));
        return SettingsHash(new
        {
            RecordedAudioContextVersion = NemoAcceleration.RecordedAudioContextVersion,
            Model = model.FullName,
            ModelStamp = model.Exists ? model.Length + ":" + model.LastWriteTimeUtc.Ticks : "",
            Runtime = runtime.FullName,
            RuntimeStamp = runtime.Exists ? runtime.Length + ":" + runtime.LastWriteTimeUtc.Ticks : "",
            options.Diarization,
        });
    }

    // Cache markers use the existing extension-data field, which cleanup preserves.
    internal static string? SavedSettingsSignature(TranscriptionResult source, string key) =>
        source.AdditionalData is not null
        && source.AdditionalData.TryGetValue(key, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string SettingsHash(object settings) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings))));
}

public sealed class TranscriptionResult
{
    [JsonPropertyName("audio_timeline_shift_seconds")]
    public double AudioTimelineShiftSeconds { get; init; }

    [JsonPropertyName("audio_file")]
    public string AudioFile { get; init; } = string.Empty;

    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; init; }

    [JsonPropertyName("language")]
    public string Language { get; init; } = "en";

    [JsonPropertyName("asr_engine")]
    public string AsrEngine { get; init; } = string.Empty;

    [JsonPropertyName("asr_model")]
    public string AsrModel { get; init; } = string.Empty;

    [JsonPropertyName("diarization_engine")]
    public string DiarizationEngine { get; init; } = string.Empty;

    [JsonPropertyName("diarization_model")]
    public string DiarizationModel { get; init; } = string.Empty;

    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; init; }

    [JsonPropertyName("speakers")]
    public string[] Speakers { get; init; } = Array.Empty<string>();

    [JsonPropertyName("word_count")]
    public int WordCount { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("words")]
    public List<WordResult> Words { get; init; } = new();

    [JsonPropertyName("raw_words")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WordResult>? RawWords { get; init; }

    [JsonPropertyName("aligned_words")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WordResult>? AlignedWords { get; init; }

    [JsonPropertyName("alignment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TranscriptAlignmentReport? Alignment { get; init; }

    [JsonPropertyName("execution")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TranscriptionExecutionInfo? Execution { get; init; }

    [JsonPropertyName("diarization_execution")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TranscriptionExecutionInfo? DiarizationExecution { get; init; }


    [JsonPropertyName("post_processing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TranscriptPostProcessingReport? PostProcessing { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }

    [JsonIgnore]
    public string JsonPath { get; set; } = string.Empty;

    [JsonIgnore]
    public string RttmPath { get; set; } = string.Empty;
}

public sealed class WordResult
{
    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("end")]
    public double End { get; set; }

    [JsonPropertyName("word")]
    public string Word { get; set; } = string.Empty;

    [JsonPropertyName("probability")]
    public double? Probability { get; set; }

    [JsonPropertyName("speaker")]
    public string? Speaker { get; set; }

    [JsonPropertyName("speaker_source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpeakerSource { get; set; }

    [JsonPropertyName("timing_source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TimingSource { get; set; }

    [JsonPropertyName("alignment_confidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? AlignmentConfidence { get; set; }

    [JsonPropertyName("dtw_first_token_time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DtwFirstTokenTime { get; set; }

    [JsonPropertyName("dtw_last_token_time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DtwLastTokenTime { get; set; }

    [JsonPropertyName("dtw_fallback_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DtwFallbackReason { get; set; }

    [JsonPropertyName("overlapping_speech")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OverlappingSpeech { get; set; }

    // Ranked by simultaneous RTTM coverage, not a probability or a text attribution.
    [JsonPropertyName("overlapping_speakers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? OverlappingSpeakers { get; set; }
}

#endregion

#region Native word alignment

// Native Whisper alignment only. No Python, ONNX, or second acoustic model.
public sealed record TranscriptAlignmentOptions
{
    public bool Enabled { get; init; } = true;
    public bool RequireAlignment { get; init; }
    public bool RefreshExistingTranscripts { get; init; } = true;
    public bool KeepPreDtwBackup { get; init; } = true;

    // Null detects stock GGML architecture from its header, not its filename alone.
    public WhisperAlignmentHeadsPreset? HeadsPreset { get; init; }
    public int DtwMemoryMb { get; init; } = 128;

    // A large gap between token anchors is not evidence of continuous speech.
    public double MaxLeadingGapSeconds { get; init; } = 0.8;
    public double MaxLeadingPaddingSeconds { get; init; } = 0.35;
    public double MaxAnchorShiftSeconds { get; init; } = 1.5;
}

public sealed class TranscriptAlignmentReport
{
    public int Version { get; init; } = 2;
    public string Status { get; init; } = "unavailable";
    public string Engine { get; init; } = TranscriptAlignment.Engine;
    public string Model { get; init; } = "";
    public string HeadsPreset { get; init; } = "";
    public string ModelFileStamp { get; init; } = "";
    public string RawFingerprint { get; init; } = "";
    public string CacheKey { get; init; } = "";
    public int InputWords { get; init; }
    public int AlignedWords { get; init; }
    public int EstimatedWords { get; init; }
    public int RetainedWords { get; init; }
    public string Message { get; init; } = "";
    public DateTimeOffset CompletedUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<AlignmentReview> Review { get; init; } = new();
}

public sealed record AlignmentReview(int FirstWord, int LastWord, string Reason);

internal static class TranscriptAlignment
{
    internal const string Engine = "Whisper.net / whisper.cpp DTW";
    internal const double MaximumReadSeconds = 28;

    internal static string RawFingerprint(TranscriptionResult source) =>
        Fingerprint(source.RawWords ?? source.Words);

    internal static string Fingerprint(IEnumerable<WordResult> words) =>
        Hash(
            JsonSerializer.Serialize(
                words.Select(w => new
                {
                    w.Word,
                    w.Start,
                    w.End,
                    w.DtwFirstTokenTime,
                    w.DtwLastTokenTime,
                })
            )
        );

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string ModelStamp(TranscriptionOptions options)
    {
        var file = new FileInfo(
            Path.GetFullPath(options.WhisperModelPath, AppContext.BaseDirectory)
        );
        return file.Exists ? file.Length + ":" + file.LastWriteTimeUtc.Ticks : "";
    }

    internal static string Signature(TranscriptionOptions options) =>
        Hash(
            JsonSerializer.Serialize(
                new
                {
                    Version = 3,
                    Package = "Whisper.net 1.9.1",
                    Model = Path.GetFileName(options.WhisperModelPath),
                    options.Language,
                    options.BeamSize,
                    options.ChunkSeconds,
                    options.OverlapSeconds,
                    options.Alignment.Enabled,
                    options.Alignment.HeadsPreset,
                    options.Alignment.DtwMemoryMb,
                    options.Alignment.MaxLeadingGapSeconds,
                    options.Alignment.MaxLeadingPaddingSeconds,
                    options.Alignment.MaxAnchorShiftSeconds,
                }
            )
        );

    internal static void Validate(TranscriptAlignmentOptions options, int overlap)
    {
        if (
            options.DtwMemoryMb < 16
            || options.DtwMemoryMb > 4095
            || !double.IsFinite(options.MaxLeadingGapSeconds)
            || options.MaxLeadingGapSeconds <= 0
            || !double.IsFinite(options.MaxLeadingPaddingSeconds)
            || options.MaxLeadingPaddingSeconds <= 0
            || options.MaxLeadingPaddingSeconds > options.MaxLeadingGapSeconds
            || !double.IsFinite(options.MaxAnchorShiftSeconds)
            || options.MaxAnchorShiftSeconds <= 0
        )
            throw new ArgumentException("Invalid native DTW alignment configuration.");
        if (options.Enabled && 2 * (double)overlap >= MaximumReadSeconds)
            throw new ArgumentException(
                "DTW overlap must be less than 14 seconds to keep decoding windows below 30 seconds."
            );
        if (
            options.HeadsPreset is { } preset
            && (
                !Enum.IsDefined(preset)
                || preset
                    is WhisperAlignmentHeadsPreset.None
                        or WhisperAlignmentHeadsPreset.Custom
                        or WhisperAlignmentHeadsPreset.NTopMost
            )
        )
            throw new ArgumentException(
                "Choose a stock DTW alignment-head preset, or leave HeadsPreset null for automatic detection."
            );
    }

    internal static double CoreSeconds(TranscriptionOptions options, bool dtwActive) =>
        dtwActive
            ? Math.Min(options.ChunkSeconds, MaximumReadSeconds - 2 * options.OverlapSeconds)
            : options.ChunkSeconds;

    internal static WhisperAlignmentHeadsPreset DetectPreset(
        string modelPath,
        WhisperAlignmentHeadsPreset? requested
    )
    {
        using var reader = new BinaryReader(File.OpenRead(modelPath));
        if (reader.BaseStream.Length < 48 || reader.ReadUInt32() != 0x67676d6c)
            throw new InvalidDataException(
                "DTW auto-detection requires a Whisper GGML model header."
            );
        int vocab = reader.ReadInt32();
        reader.ReadInt32();
        int audioState = reader.ReadInt32();
        int audioHeads = reader.ReadInt32(),
            audioLayers = reader.ReadInt32();
        reader.ReadInt32();
        int textState = reader.ReadInt32(),
            textHeads = reader.ReadInt32(),
            textLayers = reader.ReadInt32(),
            mels = reader.ReadInt32();
        bool english = vocab == 51864;
        WhisperAlignmentHeadsPreset? detected = (
            audioLayers,
            textLayers,
            audioState,
            audioHeads,
            textState,
            textHeads,
            mels
        ) switch
        {
            (4, 4, 384, 6, 384, 6, 80) => english
                ? WhisperAlignmentHeadsPreset.TinyEn
                : WhisperAlignmentHeadsPreset.Tiny,
            (6, 6, 512, 8, 512, 8, 80) => english
                ? WhisperAlignmentHeadsPreset.BaseEn
                : WhisperAlignmentHeadsPreset.Base,
            (12, 12, 768, 12, 768, 12, 80) => english
                ? WhisperAlignmentHeadsPreset.SmallEn
                : WhisperAlignmentHeadsPreset.Small,
            (24, 24, 1024, 16, 1024, 16, 80) => english
                ? WhisperAlignmentHeadsPreset.MediumEn
                : WhisperAlignmentHeadsPreset.Medium,
            (32, 4, 1280, 20, 1280, 20, 128) when vocab == 51866 =>
                WhisperAlignmentHeadsPreset.LargeV3Turbo,
            (32, 32, 1280, 20, 1280, 20, 128) when vocab == 51866 =>
                WhisperAlignmentHeadsPreset.LargeV3,
            _ => null,
        };
        // Large v1/v2 have the same dimensions. Require an explicit choice or a clear filename.
        if (
            audioLayers == 32
            && textLayers == 32
            && audioState == 1280
            && audioHeads == 20
            && textState == 1280
            && textHeads == 20
            && mels == 80
            && vocab == 51865
        )
        {
            string name = Path.GetFileName(modelPath).ToLowerInvariant().Replace('_', '-');
            detected =
                requested
                    is WhisperAlignmentHeadsPreset.LargeV1
                        or WhisperAlignmentHeadsPreset.LargeV2
                    ? requested
                : name.Contains("large-v1", StringComparison.Ordinal)
                    ? WhisperAlignmentHeadsPreset.LargeV1
                : name.Contains("large-v2", StringComparison.Ordinal)
                    ? WhisperAlignmentHeadsPreset.LargeV2
                : null;
        }
        if (detected is null || requested is { } value && value != detected)
            throw new InvalidDataException(
                "The DTW preset cannot be matched safely to this model. Use a stock Whisper model and its matching HeadsPreset; distilled/custom architectures are not inferred."
            );
        return detected.Value;
    }

    internal static List<WordResult> Apply(
        IReadOnlyList<WordResult> raw,
        double windowStart,
        double windowEnd,
        TranscriptAlignmentOptions options,
        bool dtwActive
    )
    {
        var words = raw.Select(TranscriptPostProcessor.CopyWord).ToList();
        if (!dtwActive)
            return words;
        double? previousAnchor = null;
        var collapsed = new HashSet<int>();
        for (int a = 0; a < words.Count; )
        {
            int b = a + 1;
            if (words[a].DtwFirstTokenTime is { } t && words[a].DtwLastTokenTime == t)
                while (
                    b < words.Count
                    && words[b].DtwFirstTokenTime == t
                    && words[b].DtwLastTokenTime == t
                )
                    b++;
            if (b - a >= 3)
                for (int j = a; j < b; j++)
                    collapsed.Add(j);
            a = b;
        }
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            double? first = w.DtwFirstTokenTime,
                last = w.DtwLastTokenTime;
            string? reason = null;
            if (first is null || last is null)
                reason = "missing_dtw_anchor";
            else if (
                !double.IsFinite(first.Value)
                || !double.IsFinite(last.Value)
                || first < windowStart
                || last > windowEnd
                || last < first
                || previousAnchor is { } p && first < p - 0.001
            )
                reason = "invalid_or_nonmonotonic_dtw_anchor";
            else if (collapsed.Contains(i))
                reason = "collapsed_dtw_cluster";
            else if (
                last < w.Start - options.MaxAnchorShiftSeconds
                || first > w.End + options.MaxAnchorShiftSeconds
                || last > w.End + options.MaxAnchorShiftSeconds
            )
                reason = "dtw_shift_exceeds_guard";
            // t_dtw is the approximate token-output position, not a pair of word boundaries.
            // In the bundled native implementation it is recorded on a path transition.
            // Use the preceding token-output anchor as the word's left boundary, and this
            // word's last anchor as its right boundary. Guard long pauses and equal anchors.
            double end = last ?? w.End;
            double safeFirst =
                first is { } f && double.IsFinite(f)
                    ? Math.Clamp(f, windowStart, windowEnd)
                    : Math.Clamp(w.Start, windowStart, windowEnd);
            double start = Math.Clamp(
                w.Start,
                Math.Max(windowStart, safeFirst - options.MaxLeadingPaddingSeconds),
                safeFirst
            );
            if (previousAnchor is { } prev && safeFirst - prev <= options.MaxLeadingGapSeconds)
                start = prev;
            if (reason is null && end <= start + 0.001)
                reason = "collapsed_dtw_boundary";
            if (reason is null)
            {
                w.Start = Math.Clamp(start, windowStart, windowEnd);
                w.End = Math.Clamp(end, w.Start, windowEnd);
                w.TimingSource = "whisper_dtw";
            }
            else
                w.DtwFallbackReason = reason;
            if (reason is null)
                previousAnchor = last;
            else if (
                reason == "collapsed_dtw_boundary"
                && last is { } safe
                && double.IsFinite(safe)
                && safe >= windowStart
                && safe <= windowEnd
                && (previousAnchor is null || safe >= previousAnchor)
            )
                previousAnchor = safe;
        }
        // Keep fallback entries in decoded order without changing the raw stream.
        // Bound only conflicting coarse timings; never spread them across an entire gap.
        for (int a = 0; a < words.Count; )
        {
            if (words[a].TimingSource == "whisper_dtw")
            {
                a++;
                continue;
            }
            int b = a;
            while (b < words.Count && words[b].TimingSource != "whisper_dtw")
                b++;
            double left = a == 0 ? windowStart : words[a - 1].End;
            double right = b == words.Count ? windowEnd : words[b].Start;
            if (right < left)
                right = left;
            if (words.Any(w => w.TimingSource == "whisper_dtw"))
                for (int j = a; j < b; j++)
                {
                    double start = Math.Clamp(words[j].Start, left, right),
                        end = Math.Clamp(words[j].End, start, right);
                    if (start != words[j].Start || end != words[j].End)
                    {
                        words[j].Start = start;
                        words[j].End = end;
                        words[j].TimingSource = "dtw_fallback_estimate";
                    }
                    left = end;
                }
            a = b;
        }
        return words;
    }

    internal static void ConstrainChunkOrder(List<WordResult> words)
    {
        double previous = 0;
        foreach (var w in words)
        {
            if (w.Start < previous)
            {
                w.Start = previous;
                w.End = Math.Max(w.End, previous);
                w.TimingSource = "dtw_fallback_estimate";
                w.DtwFallbackReason = "chunk_boundary_order_conflict";
            }
            previous = w.Start;
        }
    }

    internal static TranscriptAlignmentReport Report(
        TranscriptionOptions options,
        IReadOnlyList<WordResult> raw,
        IReadOnlyList<WordResult> words,
        bool active,
        string preset,
        string? warning
    )
    {
        int count = words.Count(w => w.TimingSource == "whisper_dtw");
        int estimated = words.Count(w => w.TimingSource == "dtw_fallback_estimate");
        var reviews = new List<AlignmentReview>();
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].TimingSource == "whisper_dtw")
                continue;
            string reason =
                words[i].DtwFallbackReason ?? words[i].TimingSource ?? "ordinary_token_timing";
            if (reviews.Count > 0 && reviews[^1].LastWord == i - 1 && reviews[^1].Reason == reason)
                reviews[^1] = reviews[^1] with { LastWord = i };
            else
                reviews.Add(new(i, i, reason));
        }
        return new()
        {
            Status =
                !options.Alignment.Enabled ? "disabled"
                : !active ? "unavailable"
                : words.Count == 0 ? "completed"
                : count == words.Count ? "completed"
                : count > 0 ? "partial"
                : "no_matches",
            Model = Path.GetFileName(options.WhisperModelPath),
            HeadsPreset = preset,
            ModelFileStamp = ModelStamp(options),
            RawFingerprint = Fingerprint(raw),
            CacheKey = Signature(options),
            InputWords = words.Count,
            AlignedWords = count,
            EstimatedWords = estimated,
            RetainedWords = words.Count - count - estimated,
            Review = reviews,
            Message =
                $"Native DTW: {count}/{words.Count} words use DTW-derived boundaries; {estimated} constrained fallback times; {words.Count - count - estimated} ordinary timings. Word boundaries are estimates, not calibrated accuracy scores."
                + (warning is null ? "" : " " + warning),
        };
    }

    internal static List<WordResult>? ValidAlignedWords(TranscriptionResult source)
    {
        if (
            source.AlignedWords is not { } words
            || source.RawWords is not { } raw
            || source.Alignment is not { } report
            || report.Version != 2
            || report.Engine != Engine
            || report.RawFingerprint != RawFingerprint(source)
            || words.Count != raw.Count
        )
            return null;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (
                w.Word != raw[i].Word
                || !double.IsFinite(w.Start)
                || !double.IsFinite(w.End)
                || w.Start < 0
                || w.End < w.Start
            )
                return null;
        }
        return words;
    }

    internal static bool NeedsRefresh(TranscriptionResult source, TranscriptionOptions options) =>
        options.Alignment.Enabled
        && options.Alignment.RefreshExistingTranscripts
        && (
            ValidAlignedWords(source) is null
            || source.Alignment!.CacheKey != Signature(options)
            || ModelStamp(options) is { Length: > 0 } stamp
                && source.Alignment!.ModelFileStamp != stamp
            || options.Alignment.RequireAlignment
                && source.Words.Count > 0
                && source.Alignment!.AlignedWords == 0
        );
}

#endregion
