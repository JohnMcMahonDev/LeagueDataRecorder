using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LeagueTelemetry;
using Newtonsoft.Json.Linq;

namespace League_Data_Recorder;

#region Role scoring and assignments

public static partial class TranscribeAudio
{
    private static readonly JsonSerializerOptions RoleJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>Scores an existing transcript and saves the role assignments beside it.</summary>
    public static async Task<string> AssignSpeakerRolesAsync(
        string transcriptPath,
        string roleWordWeightsPath = "role_word_weights.json",
        string? gameContextPath = null,
        CancellationToken cancellationToken = default,
        string? aliasesPath = null
    )
    {
        transcriptPath = Path.GetFullPath(transcriptPath);
        roleWordWeightsPath = Path.GetFullPath(roleWordWeightsPath, AppContext.BaseDirectory);
        string outputPath = GetRoleSidecarPath(transcriptPath, "_speaker_roles.json");
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        if (gameContextPath is null)
        {
            string sidecar = GetRoleSidecarPath(transcriptPath, "_game_context.json");
            if (File.Exists(sidecar))
                gameContextPath = sidecar;
        }
        else
        {
            gameContextPath = Path.GetFullPath(gameContextPath);
        }

        if (
            pathComparer.Equals(outputPath, roleWordWeightsPath)
            || pathComparer.Equals(outputPath, gameContextPath)
        )
            throw new ArgumentException(
                "The role output path must be different from the configuration paths."
            );

        var transcript = await ReadRoleJsonAsync<TranscriptionResult>(
                transcriptPath,
                cancellationToken
            )
            .ConfigureAwait(false);
        var weights = await ReadRoleJsonAsync<RoleWordWeights>(
                roleWordWeightsPath,
                cancellationToken
            )
            .ConfigureAwait(false);
        var context = gameContextPath is null
            ? null
            : await ReadRoleJsonAsync<RoleGameContext>(gameContextPath, cancellationToken)
                .ConfigureAwait(false);

        var aliases = await LeagueAliasCatalog
            .LoadAsync(aliasesPath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (context is not null)
            context = aliases.Enrich(context);

        SpeakerRoleResult result = SpeakerRoleScorer.Score(
            transcript,
            weights,
            context,
            cancellationToken
        );
        if (File.Exists(outputPath))
            File.Copy(outputPath, outputPath + ".previous", overwrite: true);
        await WriteRoleJsonAsync(
                outputPath,
                result with
                {
                    SourceFingerprint = RoleFingerprint(
                        transcript,
                        weights,
                        context,
                        aliases.Fingerprint
                    ),
                    TranscriptFilename = Path.GetFileName(transcriptPath),
                    WordWeightsFilename = Path.GetFileName(roleWordWeightsPath),
                    GameContextFilename = Path.GetFileName(gameContextPath),
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        return outputPath;
    }

    internal static async Task<bool> NeedsSpeakerRoleRefreshAsync(
        TranscriptionResult transcript,
        string rolesPath,
        string? contextPath,
        CancellationToken ct,
        string? aliasesPath = null
    )
    {
        if (!File.Exists(rolesPath))
            return true;
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(
                await File.ReadAllTextAsync(rolesPath, ct).ConfigureAwait(false)
            );
        }
        catch (JsonException)
        {
            return false;
        } // Preserve an unrecognized/custom file for the user.
        using var document = json;
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return false;
        if (
            (
                root.TryGetProperty("manual", out var manual)
                && manual.ValueKind == JsonValueKind.True
            )
            || (
                root.TryGetProperty("preserveAssignments", out var preserve)
                && preserve.ValueKind == JsonValueKind.True
            )
        )
            return false;
        bool generated =
            root.TryGetProperty("transcriptFilename", out _)
            && root.TryGetProperty("speakers", out _)
            && root.TryGetProperty("wordWeightsFilename", out _);
        if (!generated)
            return false; // Unrecognized/custom role files belong to the user.
        if (
            !root.TryGetProperty("version", out var version)
            || !version.TryGetInt32(out int v)
            || v != SpeakerRoleScorer.AlgorithmVersion
        )
            return true;
        string current = await RoleFingerprintAsync(
                transcript,
                Path.Combine(AppContext.BaseDirectory, "role_word_weights.json"),
                contextPath,
                ct,
                aliasesPath
            )
            .ConfigureAwait(false);
        return !root.TryGetProperty("sourceFingerprint", out var fingerprint)
            || fingerprint.ValueKind != JsonValueKind.String
            || fingerprint.GetString() != current;
    }

    private static async Task<string> RoleFingerprintAsync(
        TranscriptionResult transcript,
        string weightsPath,
        string? contextPath,
        CancellationToken ct,
        string? aliasesPath
    )
    {
        var weights = await ReadRoleJsonAsync<RoleWordWeights>(weightsPath, ct)
            .ConfigureAwait(false);
        var context = contextPath is null
            ? null
            : await ReadRoleJsonAsync<RoleGameContext>(contextPath, ct).ConfigureAwait(false);
        var aliases = await LeagueAliasCatalog
            .LoadAsync(aliasesPath, cancellationToken: ct)
            .ConfigureAwait(false);
        return RoleFingerprint(
            transcript,
            weights,
            context is null ? null : aliases.Enrich(context),
            aliases.Fingerprint
        );
    }

    private static string RoleFingerprint(
        TranscriptionResult transcript,
        RoleWordWeights weights,
        RoleGameContext? context,
        string aliasFingerprint
    ) =>
        Convert.ToHexString(
            SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        Algorithm = SpeakerRoleScorer.AlgorithmVersion,
                        transcript.DurationSeconds,
                        transcript.Words,
                        weights,
                        context,
                        aliasFingerprint,
                    },
                    RoleJsonOptions
                )
            )
        );

    private static string GetRoleSidecarPath(string transcriptPath, string suffix)
    {
        string stem = Path.GetFileNameWithoutExtension(transcriptPath);
        if (stem.EndsWith("_transcript", StringComparison.OrdinalIgnoreCase))
            stem = stem[..^"_transcript".Length];
        return Path.Combine(Path.GetDirectoryName(transcriptPath)!, stem + suffix);
    }

    private static async Task<T> ReadRoleJsonAsync<T>(
        string path,
        CancellationToken cancellationToken
    )
    {
        // StreamReader handles the UTF-8 BOM on older exported configuration files.
        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, RoleJsonOptions)
            ?? throw new JsonException($"Empty JSON in {Path.GetFileName(path)}.");
    }

    private static async Task WriteRoleJsonAsync<T>(
        string path,
        T data,
        CancellationToken cancellationToken,
        bool overwrite = true
    )
    {
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer
                    .SerializeAsync(stream, data, RoleJsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, path, overwrite);
        }
        catch (IOException) when (!overwrite && File.Exists(path))
        {
            // A second context build finished first.
        }
        finally
        {
            TryDelete(tempPath);
        }
    }
}

internal static class SpeakerRoleScorer
{
    internal const int AlgorithmVersion = 4;
    internal static readonly string[] Roles = ["top", "jungle", "mid", "adc", "support"];
    private static readonly Regex TokenPattern = new(
        @"[\p{L}\p{Nd}]+(?:'[\p{L}\p{Nd}]+)*",
        RegexOptions.CultureInvariant
    );

    internal static string[] Tokenize(string text) =>
        TokenPattern
            .Matches(text.Replace('’', '\'').ToLowerInvariant())
            .Select(match => match.Value)
            .ToArray();

    internal static SpeakerRoleResult Score(
        TranscriptionResult transcript,
        RoleWordWeights weights,
        RoleGameContext? context,
        CancellationToken cancellationToken
    )
    {
        string[] roles = weights.Roles;
        if (
            weights.Words is null
            || roles is null
            || roles.Length != Roles.Length
            || !roles.ToHashSet(StringComparer.Ordinal).SetEquals(Roles)
        )
            throw new InvalidDataException(
                "The word weights JSON must have a words object and list the five roles once each."
            );
        if (
            !double.IsFinite(weights.Settings.MinimumAssignmentMargin)
            || weights.Settings.MinimumAssignmentMargin < 0
        )
            throw new InvalidDataException(
                "MinimumAssignmentMargin must be finite and nonnegative."
            );

        double cutoff =
            transcript.DurationSeconds > 0 ? transcript.DurationSeconds : double.PositiveInfinity;
        var words = (transcript.Words ?? [])
            .Where(word =>
                word is not null
                && !string.IsNullOrWhiteSpace(word.Word)
                && double.IsFinite(word.Start)
                && double.IsFinite(word.End)
                && word.Start >= 0
                && word.Start < cutoff
                && word.SpeakerSource != "repetition_placeholder"
            )
            .OrderBy(word => word.Start)
            .ToArray();

        var counts = words
            .Where(word => !string.IsNullOrWhiteSpace(word.Speaker))
            .GroupBy(word => word.Speaker!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        string[] speakers = counts.Keys.Order(StringComparer.Ordinal).ToArray();
        var scores = speakers.ToDictionary(
            speaker => speaker,
            speaker => // One row per speaker in the score matrix.
                roles.ToDictionary(role => role, _ => new RoleScore(), StringComparer.Ordinal),
            StringComparer.Ordinal
        );

        bool flat = !string.Equals(
            context?.Settings.WordWeightsMode,
            "weighted",
            StringComparison.OrdinalIgnoreCase
        );
        ScoreWords(words, cutoff, weights, flat, scores, cancellationToken);
        if (context is not null)
            ScoreContext(words, context, scores, cancellationToken);

        var evidence = new List<RoleEvidence>();
        var coach = SpeakerRoleEvidence.Score(words, context, scores, evidence, cancellationToken);
        var best = FindBestAssignments(
            speakers,
            roles,
            scores,
            weights.Settings.MinimumAssignmentScore,
            cancellationToken
        );
        var assignments = new Dictionary<string, string>(StringComparer.Ordinal);
        var decisions = new Dictionary<string, RoleAssignmentDecision>(StringComparer.Ordinal);
        foreach (var (speaker, role) in best.Mapping)
        {
            var alternative = FindBestAssignments(
                speakers,
                roles,
                scores,
                weights.Settings.MinimumAssignmentScore,
                cancellationToken,
                (speaker, role)
            );
            double margin = best.Total - alternative.Total;
            // A one-to-one roster constraint can inflate the margin of a weak guess.
            // With game context, require positive first-person ownership evidence as well.
            bool accepted =
                margin >= weights.Settings.MinimumAssignmentMargin
                && (context is null || scores[speaker][role].OwnershipScore >= 4);
            decisions[speaker] = new RoleAssignmentDecision(
                role,
                scores[speaker][role].CombinedScore,
                margin,
                scores[speaker][role].OwnershipScore >= 5 ? "ownership_supported" : "weak",
                accepted
            );
            if (accepted)
                assignments[speaker] = role;
        }
        // Complete a roster only when four evidence-supported players and all
        // non-player voices are already accounted for. Never force five labels on six ambiguous voices.
        if (
            weights.Settings.AllowRosterElimination
            && context is not null
            && assignments.Count == 4
            && Roles.All(context.Roles.ContainsKey)
            && assignments.All(p => scores[p.Key][p.Value].OwnershipScore >= 5)
        )
        {
            string missing = roles.Except(assignments.Values, StringComparer.Ordinal).Single();
            var remaining = speakers.Where(s => !assignments.ContainsKey(s)).ToArray();
            var eligible = remaining
                .Where(s => !(coach[s] >= 2 && scores[s].Values.Max(v => v.OwnershipScore) < 4))
                .ToArray();
            if (
                eligible.Length == 1
                && counts[eligible[0]] >= Math.Max(25, words.Length * 0.025)
                && scores[eligible[0]][missing].CombinedScore >= 0.5
            )
            {
                string speaker = eligible[0];
                assignments[speaker] = missing;
                decisions[speaker] = new RoleAssignmentDecision(
                    missing,
                    scores[speaker][missing].CombinedScore,
                    0,
                    "roster_elimination",
                    true
                );
                evidence.Add(
                    new RoleEvidence(
                        speaker,
                        missing,
                        0,
                        "roster_elimination",
                        "remaining player role",
                        0,
                        "Four roles have ownership evidence; only this substantial non-coach speaker and this role remain. This is a deduction, not direct ownership evidence."
                    )
                );
            }
        }
        var speakerScores = speakers.ToDictionary(
            speaker => speaker,
            speaker => new SpeakerScores(counts[speaker], scores[speaker]),
            StringComparer.Ordinal
        );
        string[] excluded = (transcript.Speakers ?? [])
            .Concat(counts.Keys)
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker) && !scores.ContainsKey(speaker))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new SpeakerRoleResult
        {
            WordWeightsMode = flat ? "flat" : "weighted",
            Assignments = assignments,
            AssignmentDecisions = decisions,
            Evidence = evidence,
            SpeakerCategories = speakers.ToDictionary(
                s => s,
                s =>
                    assignments.ContainsKey(s) ? "player"
                    : coach[s] >= 2 && scores[s].Values.Max(v => v.OwnershipScore) < 5
                        ? "possible_coach"
                    : "unassigned"
            ),
            UnassignedRoles = roles.Except(assignments.Values, StringComparer.Ordinal).ToArray(),
            UnassignedSpeakers = speakers
                .Where(speaker => !assignments.ContainsKey(speaker))
                .Concat(excluded)
                .ToArray(),
            Speakers = speakerScores,
        };
    }

    private static void ScoreWords(
        WordResult[] words,
        double cutoff,
        RoleWordWeights weights,
        bool flat,
        Dictionary<string, Dictionary<string, RoleScore>> scores,
        CancellationToken cancellationToken
    )
    {
        RoleWordSettings settings = weights.Settings;
        var cues = new Dictionary<string, RoleWordCue>(
            weights.Words,
            StringComparer.OrdinalIgnoreCase
        );
        var durations = scores.Keys.ToDictionary(speaker => speaker, _ => (Seconds: 0d, End: 0d));
        if (!flat)
        {
            foreach (WordResult word in words)
            {
                if (word.Speaker is null || !durations.TryGetValue(word.Speaker, out var duration))
                    continue;
                double start = Math.Max(duration.End, word.Start);
                double end = Math.Min(cutoff, word.End);
                if (end > start)
                    durations[word.Speaker] = (duration.Seconds + end - start, end);
            }
        }
        double totalSpeech = durations.Values.Sum(duration => duration.Seconds);
        var lastHit = new Dictionary<(string Speaker, string Word), double>();

        foreach (WordResult word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (word.Speaker is null || !scores.TryGetValue(word.Speaker, out var speakerScores))
                continue;

            foreach (string token in Tokenize(word.Word))
            {
                var key = (word.Speaker, token);
                if (
                    !cues.TryGetValue(token, out RoleWordCue? cue)
                    || lastHit.TryGetValue(key, out double last)
                        && word.Start - last < settings.RepeatWindowSeconds
                )
                    continue;

                lastHit[key] = word.Start;
                double points = cue.Weight;
                if (!flat)
                {
                    double seconds = durations[word.Speaker].Seconds;
                    double ratio =
                        seconds > 0
                            ? Math.Clamp(
                                settings.ReferenceSpeechShare / (seconds / totalSpeech),
                                settings.MinimumSpeechRatio,
                                settings.MaximumSpeechRatio
                            )
                            : 1;
                    double lateStart = settings.LateWeightStartsAtSeconds;
                    double lateEnd = settings.LateWeightReachesFloorAtSeconds;
                    double progress =
                        lateEnd > lateStart
                            ? Math.Clamp((word.Start - lateStart) / (lateEnd - lateStart), 0, 1)
                            : (word.Start >= lateStart ? 1 : 0);
                    points *=
                        (1 + progress * (cue.LateWeightMultiplier - 1))
                        * (1 + cue.SpeechScalingAdjustment * (ratio - 1));
                }
                foreach (string role in cue.Roles.Distinct(StringComparer.Ordinal))
                    if (speakerScores.TryGetValue(role, out RoleScore? score))
                        score.WordScore += points;
            }
        }
    }

    private static void ScoreContext(
        WordResult[] words,
        RoleGameContext context,
        Dictionary<string, Dictionary<string, RoleScore>> scores,
        CancellationToken cancellationToken
    )
    {
        RoleContextSettings settings = context.Settings;
        var aliases = CompileContextCues(context);
        int[] cueLengths = aliases
            .Keys.Select(alias => alias.Count(character => character == ' ') + 1)
            .Distinct()
            .OrderDescending()
            .ToArray();
        var prefixes = settings
            .FirstPersonPrefixes.Select(prefix => string.Join(' ', Tokenize(prefix)))
            .Where(prefix => prefix.Length > 0)
            .SelectMany(prefix =>
                settings
                    .OptionalArticlesAfterFirstPersonPrefix.Select(article =>
                        prefix + " " + string.Join(' ', Tokenize(article))
                    )
                    .Prepend(prefix)
            )
            .ToHashSet(StringComparer.Ordinal);
        int[] prefixLengths = prefixes
            .Select(prefix => prefix.Count(character => character == ' ') + 1)
            .Distinct()
            .ToArray();

        char[] boundaries = settings.PhraseBoundaryPunctuation.ToCharArray();
        var lastHit = new Dictionary<(string Speaker, ContextCue Cue), double>();
        var tokens = new List<string>();
        var times = new List<double>();
        string? speaker = null;
        double previousEnd = 0;

        foreach (WordResult word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                speaker != word.Speaker
                || word.Start - previousEnd > settings.MaxGapWithinPhraseSeconds
            )
                ScorePhrase();
            speaker = word.Speaker;
            previousEnd = word.End;
            if (speaker is null || !scores.ContainsKey(speaker))
                continue;

            string[] fragments = boundaries.Length == 0 ? [word.Word] : word.Word.Split(boundaries);
            for (int fragment = 0; fragment < fragments.Length; fragment++)
            {
                foreach (string token in Tokenize(fragments[fragment]))
                {
                    tokens.Add(token);
                    times.Add(word.Start);
                }
                if (fragment < fragments.Length - 1)
                    ScorePhrase();
            }
        }
        ScorePhrase();

        void ScorePhrase()
        {
            if (speaker is null || tokens.Count == 0)
                return;

            string[] phrase = tokens.ToArray();
            int index = 0;
            while (index < phrase.Length)
            {
                int matchedLength = 0;
                foreach (int length in cueLengths)
                {
                    if (
                        index + length > phrase.Length
                        || !aliases.TryGetValue(
                            string.Join(" ", phrase, index, length),
                            out ContextCue? cue
                        )
                    )
                        continue;

                    matchedLength = length;
                    if (
                        cue.RequiresFirstPerson
                        && !prefixLengths.Any(size =>
                            size <= index
                            && prefixes.Contains(string.Join(" ", phrase, index - size, size))
                        )
                    )
                        break;

                    var key = (speaker, cue);
                    double time = times[index];
                    if (
                        !lastHit.TryGetValue(key, out double last)
                        || time - last >= settings.RepeatWindowSeconds
                    )
                    {
                        lastHit[key] = time;
                        foreach (string role in cue.Roles)
                            if (scores[speaker].TryGetValue(role, out RoleScore? score))
                                score.ContextScore += cue.Weight;
                    }
                    break;
                }
                index += Math.Max(matchedLength, 1);
            }
            tokens.Clear();
            times.Clear();
        }
    }

    private static Dictionary<string, ContextCue> CompileContextCues(RoleGameContext context)
    {
        RoleContextSettings settings = context.Settings;
        var aliases = new Dictionary<string, HashSet<ContextCue>>(StringComparer.Ordinal);
        var owners = new List<(string Role, string Kind, LoadoutCue Entry, double Weight)>();
        foreach (var (role, roster) in context.Roles)
        {
            if (!Roles.Contains(role))
                continue;

            string[] opponentRoles =
                settings.DraftEnemyNameRoleOverrides.GetValueOrDefault(role) ?? [role];
            if (!string.IsNullOrWhiteSpace(roster.OpposingChampion))
                Add(
                    new ContextCue(
                        settings.DraftEnemyNameWeight,
                        opponentRoles.Distinct().ToArray(),
                        false
                    ),
                    roster.OpposingChampionAliases,
                    allowPossessive: true
                );
            foreach (AbilityCue ability in roster.AlliedAbilityCues)
                Add(
                    new ContextCue(settings.DraftAlliedAbilityWeight, [role], true),
                    ability.Aliases
                );

            owners.AddRange(
                roster.SummonerSpells.Select(entry =>
                    (role, "spell", entry, settings.SummonerSpellWeight)
                )
            );
            owners.AddRange(
                roster.EndgameItems.Select(entry => (role, "item", entry, settings.ItemWeight))
            );
        }

        var alreadyScored = settings.AlreadyScoredByWordWeights.ToHashSet(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (
            var group in owners.GroupBy(owner =>
                (owner.Kind, Name: owner.Entry.Name.Trim().ToLowerInvariant())
            )
        )
        {
            if (
                alreadyScored.Contains(group.Key.Name)
                || group.Select(owner => owner.Role).Distinct().Count() != 1
            )
                continue;
            var verified = group
                .Where(owner =>
                    string.Equals(
                        owner.Entry.Status,
                        "verified",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToArray();
            if (verified.Length > 0)
                Add(
                    new ContextCue(verified[0].Weight, [verified[0].Role], true),
                    verified.SelectMany(owner => owner.Entry.Aliases)
                );
        }

        // Ambiguous aliases do not identify a particular champion, ability, or loadout.
        return aliases
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Single(), StringComparer.Ordinal);

        void Add(ContextCue cue, IEnumerable<string> names, bool allowPossessive = false)
        {
            foreach (string name in names)
            {
                string alias = string.Join(' ', Tokenize(name));
                if (alias.Length == 0)
                    continue;
                AddAlias(alias);
                if (allowPossessive)
                    AddAlias(alias + "'s");
            }

            void AddAlias(string alias)
            {
                if (!aliases.TryGetValue(alias, out var matches))
                    aliases[alias] = matches = new HashSet<ContextCue>();
                matches.Add(cue);
            }
        }
    }

    private static (Dictionary<string, string> Mapping, double Total) FindBestAssignments(
        string[] speakers,
        string[] roles,
        Dictionary<string, Dictionary<string, RoleScore>> scores,
        double minimumScore,
        CancellationToken cancellationToken,
        (string Speaker, string Role)? forbidden = null
    )
    {
        if (!double.IsFinite(minimumScore) || minimumScore < 0)
            throw new InvalidDataException(
                "MinimumAssignmentScore must be finite and nonnegative."
            );
        // Five role bits: O(speakerCount * 32 * 5). Extra speakers are skip candidates,
        // not discarded before their evidence is read and not a factorial permutation search.
        var states = new (double Total, Dictionary<string, string> Map)?[1 << roles.Length];
        states[0] = (0, new Dictionary<string, string>(StringComparer.Ordinal));
        foreach (string speaker in speakers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = states.ToArray(); // skip this speaker / leave them outside player roles
            for (int mask = 0; mask < states.Length; mask++)
            {
                if (states[mask] is not { } state)
                    continue;
                for (int r = 0; r < roles.Length; r++)
                {
                    if ((mask & (1 << r)) != 0 || forbidden == (speaker, roles[r]))
                        continue;
                    double score = scores[speaker][roles[r]].CombinedScore;
                    if (score < minimumScore || score <= 0)
                        continue;
                    int target = mask | (1 << r);
                    double total = state.Total + score;
                    if (next[target] is { } old && old.Total >= total - 1e-9)
                        continue;
                    var map = new Dictionary<string, string>(state.Map, StringComparer.Ordinal)
                    {
                        [speaker] = roles[r],
                    };
                    next[target] = (total, map);
                }
            }
            states = next;
        }
        var best = states
            .OfType<(double Total, Dictionary<string, string> Map)>()
            .OrderByDescending(s => s.Total)
            .First();
        return (best.Map, best.Total);
    }

    private sealed record ContextCue(double Weight, string[] Roles, bool RequiresFirstPerson);
}

internal sealed class RoleWordWeights
{
    public string[] Roles { get; init; } = SpeakerRoleScorer.Roles;
    public RoleWordSettings Settings { get; init; } = new();
    public required Dictionary<string, RoleWordCue> Words { get; init; }
}

internal sealed class RoleWordSettings
{
    public double MinimumAssignmentScore { get; init; } = 1;
    public bool AllowRosterElimination { get; init; } = true;
    public double MinimumAssignmentMargin { get; init; } = 0.75;
    public double RepeatWindowSeconds { get; init; } = 8;
    public double LateWeightStartsAtSeconds { get; init; } = 900;
    public double LateWeightReachesFloorAtSeconds { get; init; } = 1200;
    public double ReferenceSpeechShare { get; init; } = 0.2;
    public double MinimumSpeechRatio { get; init; } = 0.5;
    public double MaximumSpeechRatio { get; init; } = 2;
}

internal sealed class RoleWordCue
{
    public string[] Roles { get; init; } = [];
    public double Weight { get; init; } = 1;
    public double LateWeightMultiplier { get; init; } = 1;
    public double SpeechScalingAdjustment { get; init; }
}

internal sealed record SpeakerRoleResult
{
    public int Version => SpeakerRoleScorer.AlgorithmVersion;
    public string Generator => "League Data Recorder";
    public string? SourceFingerprint { get; init; }
    public bool PreserveAssignments { get; init; }
    public bool Manual { get; init; }
    public Dictionary<string, RoleAssignmentDecision> AssignmentDecisions { get; init; } = new();
    public Dictionary<string, string> SpeakerCategories { get; init; } = new();
    public List<RoleEvidence> Evidence { get; init; } = new();
    public string? TranscriptFilename { get; init; }
    public string? WordWeightsFilename { get; init; }
    public string? GameContextFilename { get; init; }
    public string WordWeightsMode { get; init; } = "flat";
    public Dictionary<string, string> Assignments { get; init; } = new();
    public string[] UnassignedRoles { get; init; } = [];
    public string[] UnassignedSpeakers { get; init; } = [];
    public Dictionary<string, SpeakerScores> Speakers { get; init; } = new();
}

internal sealed record SpeakerScores(
    int TranscriptWordEntries,
    Dictionary<string, RoleScore> Scores
);

internal sealed class RoleScore
{
    public double WordScore { get; set; }
    public double ContextScore { get; set; }
    public double OwnershipScore { get; set; }
    public double CoachPenalty { get; set; }
    public double CombinedScore =>
        Math.Max(
            0,
            Math.Min(3, WordScore * 0.2)
                + Math.Min(2, ContextScore * 0.25)
                + OwnershipScore
                - CoachPenalty
        );
}

internal sealed record RoleAssignmentDecision(
    string Role,
    double Score,
    double GlobalMargin,
    string Strength,
    bool Accepted
);

#endregion

#region Speaker evidence

internal static class SpeakerRoleEvidence
{
    private sealed record Cue(string Name, string Kind, string[] Roles, double Weight);

    private static readonly HashSet<string> Self = new(StringComparer.Ordinal)
    {
        "i",
        "i'm",
        "i've",
        "i'll",
        "i'd",
        "my",
        "me",
    };
    private static readonly HashSet<string> Other = new(StringComparer.Ordinal)
    {
        "you",
        "your",
        "he",
        "she",
        "his",
        "her",
        "they",
        "their",
        "our",
    };

    internal static Dictionary<string, double> Score(
        WordResult[] words,
        RoleGameContext? context,
        Dictionary<string, Dictionary<string, RoleScore>> scores,
        List<RoleEvidence> evidence,
        CancellationToken ct
    )
    {
        var coach = scores.Keys.ToDictionary(s => s, _ => 0d, StringComparer.Ordinal);
        var cues = new Dictionary<string, List<Cue>>(StringComparer.Ordinal);
        void Add(string alias, Cue cue)
        {
            string key = string.Join(' ', SpeakerRoleScorer.Tokenize(alias));
            if (key.Length == 0)
                return;
            if (!cues.TryGetValue(key, out var found))
                cues[key] = found = [];
            if (!found.Contains(cue))
                found.Add(cue);
        }
        if (context is not null)
        {
            foreach (
                var (role, roster) in context.Roles.Where(p =>
                    SpeakerRoleScorer.Roles.Contains(p.Key)
                )
            )
            {
                if (roster.AlliedChampion is { Length: > 0 } champion)
                    foreach (string alias in roster.AlliedChampionAliases)
                        Add(alias, new Cue(champion, "champion", [role], 7));
                foreach (var ability in roster.AlliedAbilityCues)
                foreach (string alias in ability.Aliases)
                    Add(
                        alias,
                        new Cue(
                            ability.Ability,
                            "ability",
                            [role],
                            6 * context.Settings.DraftAlliedAbilityWeight
                        )
                    );
                if (roster.OpposingChampion is { Length: > 0 } enemy)
                    foreach (string alias in roster.OpposingChampionAliases)
                        Add(
                            alias,
                            new Cue(
                                enemy,
                                "lane_opponent",
                                context.Settings.DraftEnemyNameRoleOverrides.GetValueOrDefault(role)
                                    ?? [role],
                                3 * context.Settings.DraftEnemyNameWeight
                            )
                        );
            }
            var loadout = context.Roles.SelectMany(p =>
                p.Value.SummonerSpells.Select(x => (Role: p.Key, Kind: "spell", Item: x))
                    .Concat(p.Value.EndgameItems.Select(x => (Role: p.Key, Kind: "item", Item: x)))
            );
            foreach (var group in loadout.GroupBy(x => (x.Kind, x.Item.Name)))
            {
                var verified = group
                    .Where(x =>
                        x.Item.Status.Equals("verified", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray();
                if (verified.Length == 0)
                    continue;
                string[] owners = group.Select(x => x.Role).Distinct().ToArray();
                // Shared Teleport is still useful as weak evidence for both solo lanes.
                if (owners.Length > 1 && group.Key.Kind != "spell")
                    continue;
                if (owners.Length == 5)
                    continue; // Everyone can Flash; it identifies no role.
                foreach (string alias in verified.SelectMany(x => x.Item.Aliases).Distinct())
                    Add(
                        alias,
                        new Cue(
                            group.Key.Name,
                            group.Key.Kind,
                            owners,
                            (owners.Length == 1 ? 5 : 2)
                                * (
                                    group.Key.Kind == "spell"
                                        ? context.Settings.SummonerSpellWeight
                                        : context.Settings.ItemWeight
                                )
                        )
                    );
            }
        }
        // Reject generic aliases shared by different abilities/owners, rather than making them strong evidence.
        var aliases = cues.Where(p =>
                p.Value.Select(x => (x.Name, x.Kind, Owners: string.Join(',', x.Roles.Order())))
                    .Distinct()
                    .Count() == 1
            )
            .ToDictionary(p => p.Key, p => p.Value[0]);
        int[] lengths = aliases
            .Keys.Select(x => x.Split(' ').Length)
            .Distinct()
            .OrderDescending()
            .ToArray();
        var last = new Dictionary<(string Speaker, string Kind, string Name), double>();
        var recentLaneIntent = new Dictionary<string, double>(StringComparer.Ordinal);
        var span = new List<WordResult>();
        void Flush()
        {
            if (span.Count == 0)
                return;
            string? speaker = span[0].Speaker;
            if (speaker is null || !scores.ContainsKey(speaker))
            {
                span.Clear();
                return;
            }
            string text = string.Join(' ', span.Select(w => w.Word.Trim()));
            string[] tokens = SpeakerRoleScorer.Tokenize(text);
            double time = span[0].Start;
            bool recentLane =
                recentLaneIntent.TryGetValue(speaker, out double laneTime) && time - laneTime <= 6;
            bool hasLaneWords = Regex.IsMatch(
                text,
                @"\b(?:wave|lane|laning|matchup|shove|cs)\b",
                RegexOptions.IgnoreCase
            );
            if (time < 900 && hasLaneWords && tokens.Any(Self.Contains))
                recentLaneIntent[speaker] = time;
            bool firstPerson = tokens.Any(Self.Contains);
            bool quotedOrHypothetical = Regex.IsMatch(
                text,
                @"\b(?:wish i|used to play|last game|he said|she said|he's like|she's like|my friend|my teammate)\b",
                RegexOptions.IgnoreCase
            );
            // Role-specific ability interactions are stronger than generic mentions of W or a champion.
            // Both signatures describe Leona's own damage reduction, not an opponent target call.
            if (
                context is not null
                && firstPerson
                && !quotedOrHypothetical
                && !Regex.IsMatch(text, @"^\s*if i\b", RegexOptions.IgnoreCase)
            )
            {
                var leona = context.Roles.FirstOrDefault(p =>
                    p.Value.AlliedChampion?.Equals("Leona", StringComparison.OrdinalIgnoreCase)
                    == true
                );
                if (
                    leona.Key is not null
                    && (
                        Regex.IsMatch(
                            text,
                            @"\b(?:used?|using|overlap|overlapping)\b.*\bw\b.*\baftershock\b|\bw\b.*\boverlap\b.*\baftershock\b",
                            RegexOptions.IgnoreCase
                        )
                        || Regex.IsMatch(
                            text,
                            @"\bi have (?:a )?button\b.*\balistar\b.*\b(?:ten|10) seconds\b",
                            RegexOptions.IgnoreCase
                        )
                    )
                )
                    Award(
                        speaker,
                        new Cue("Leona defensive W ownership", "ability_signature", [leona.Key], 8),
                        time,
                        text
                    );
            }
            bool coachPhrase = Regex.IsMatch(
                text,
                @"\byou guys (?:should|need|have to|got|won|win|lost|lose|are|did)|\bspec(?:tator)? (?:data|announcer)|\byour team\b",
                RegexOptions.IgnoreCase
            );
            if (
                coachPhrase
                && (
                    !last.TryGetValue((speaker, "coach", "commentary"), out double previous)
                    || time - previous >= 15
                )
            )
            {
                last[(speaker, "coach", "commentary")] = time;
                coach[speaker] += 1;
                evidence.Add(
                    new RoleEvidence(speaker, null, time, "possible_coach", "commentary", 1, text)
                );
            }
            for (int i = 0; i < tokens.Length; i++)
            {
                foreach (int length in lengths)
                {
                    if (
                        i + length > tokens.Length
                        || !aliases.TryGetValue(string.Join(' ', tokens, i, length), out var cue)
                    )
                        continue;
                    int start = Math.Max(0, i - 8);
                    int marker = -1;
                    for (int k = start; k < i; k++)
                        if (Self.Contains(tokens[k]))
                            marker = k;
                    bool own =
                        marker >= 0
                        && !tokens.Skip(marker + 1).Take(i - marker - 1).Any(Other.Contains);
                    if (cue.Kind == "lane_opponent" && recentLane)
                        own = true;
                    string before = string.Join(' ', tokens.Take(i));
                    bool hypothetical = Regex.IsMatch(
                        before,
                        @"(?:wish i|if i|i don't play|i do not play|used to play|last game|my (?:adc|jungler|support|mid laner|opponent|enemy|friend|coach|teammate)|he's like|he said|she said)"
                    );
                    if (!own || hypothetical)
                        break;
                    bool valid = cue.Kind switch
                    {
                        "champion" => Regex.IsMatch(
                            before,
                            @"(?:\bi(?: am|'m)(?: playing| on)?|\bplay|\bplaying|\bpick|\bpicked)\s*$"
                        ),
                        "lane_opponent" => time < 900 && (hasLaneWords || recentLane),
                        _ => true,
                    };
                    if (valid)
                        Award(speaker, cue, time, text);
                    break;
                }
            }
            // Distinctive early-game resource ownership, including clipped "gonna get six off red" fragments.
            if (
                time < 900
                && Regex.IsMatch(text, @"\b(?:i|i'm|my|gonna)\b", RegexOptions.IgnoreCase)
                && Regex.IsMatch(
                    text,
                    @"\b(?:my (?:topside|bot ?side|camps|raptors|gromp|krugs)|(?:get|hit) (?:six|6).*\b(?:red|blue)\b|i'm starting (?:at|as|on) (?:raptors|gromp|krugs|red|blue))",
                    RegexOptions.IgnoreCase
                )
            )
                Award(
                    speaker,
                    new Cue("early jungle resource ownership", "resource", ["jungle"], 6),
                    time,
                    text
                );
            span.Clear();
        }
        void Award(string speaker, Cue cue, double time, string text)
        {
            if (!double.IsFinite(cue.Weight) || cue.Weight <= 0)
                return;
            var key = (speaker, cue.Kind, cue.Name);
            if (last.TryGetValue(key, out double prior) && time - prior < 12)
                return;
            last[key] = time;
            foreach (string role in cue.Roles)
            {
                if (!scores[speaker].TryGetValue(role, out var score))
                    continue;
                score.OwnershipScore += cue.Weight;
                evidence.Add(
                    new RoleEvidence(speaker, role, time, cue.Kind, cue.Name, cue.Weight, text)
                );
            }
        }
        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();
            if (word.SpeakerSource == "repetition_placeholder")
            {
                Flush();
                continue;
            }
            if (
                span.Count > 0
                && (
                    span[0].Speaker != word.Speaker
                    || word.Start - span[^1].End > 1.2
                    || word.Start - span[0].Start > 12
                    || span.Count >= 60
                )
            )
                Flush();
            span.Add(word);
            if (Regex.IsMatch(word.Word.Trim(), "[.!?;][\\\"'’)]*$"))
                Flush();
        }
        Flush();
        foreach (var (speaker, row) in scores)
        {
            double maxOwned = row.Values.Max(s => s.OwnershipScore);
            foreach (var score in row.Values)
                score.CoachPenalty = maxOwned >= 5 ? 0 : Math.Min(3, coach[speaker] * 0.5);
        }
        return coach;
    }
}

internal sealed record RoleEvidence(
    string Speaker,
    string? Role,
    double Start,
    string Kind,
    string Cue,
    double Points,
    string Text
);

#endregion

#region Recorded match context

public static partial class TranscribeAudio
{
    /// <summary>
    /// Builds a context beside the transcript from the recording's saved game files.
    /// gamePath is the Game_&lt;id&gt; file prefix used by the recorder.
    /// </summary>
    public static async Task<string?> CreateGameContextAsync(
        string gamePath,
        string transcriptPath,
        string? dataDragonDirectory = null,
        CancellationToken cancellationToken = default,
        string? aliasesPath = null
    )
    {
        gamePath = Path.GetFullPath(gamePath);
        transcriptPath = Path.GetFullPath(transcriptPath);
        string contextPath = GetRoleSidecarPath(transcriptPath, "_game_context.json");
        if (File.Exists(contextPath))
            return contextPath; // Reuse hand-edited or previously generated context.

        RoleGameContext? context = await RecordedRoleContext
            .BuildAsync(
                gamePath,
                transcriptPath,
                dataDragonDirectory,
                cancellationToken,
                await LeagueAliasCatalog
                    .LoadAsync(aliasesPath, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        if (context is null)
            return null;

        await WriteRoleJsonAsync(contextPath, context, cancellationToken, overwrite: false)
            .ConfigureAwait(false);
        return contextPath;
    }
}

internal static class RecordedRoleContext
{
    private static readonly Dictionary<int, string> SpellNames = new()
    {
        [1] = "Cleanse",
        [3] = "Exhaust",
        [4] = "Flash",
        [6] = "Ghost",
        [7] = "Heal",
        [11] = "Smite",
        [12] = "Teleport",
        [13] = "Clarity",
        [14] = "Ignite",
        [21] = "Barrier",
    };

    internal static async Task<RoleGameContext?> BuildAsync(
        string gamePath,
        string transcriptPath,
        string? catalogDirectory,
        CancellationToken cancellationToken,
        LeagueAliasCatalog aliases
    )
    {
        JObject? post = await ReadGameJsonAsync(
                gamePath + "_ParsedPostGameData.json",
                cancellationToken
            )
            .ConfigureAwait(false);
        JObject? live = await ReadGameJsonAsync(
                gamePath + "_ParsedGameData.json",
                cancellationToken
            )
            .ConfigureAwait(false);
        if (post is null && live is null)
            return null;

        JObject? draft = await ReadGameJsonAsync(gamePath + "_DraftData.json", cancellationToken)
            .ConfigureAwait(false);
        bool useRemoteCatalog = catalogDirectory is null;
        catalogDirectory ??= FindCachedCatalog();
        if (catalogDirectory is null && useRemoteCatalog)
            catalogDirectory = await FetchCatalogDirectoryAsync(cancellationToken)
                .ConfigureAwait(false);
        var catalogs = await ReadCatalogAsync(
                catalogDirectory,
                "champion.json",
                useRemoteCatalog,
                cancellationToken
            )
            .ConfigureAwait(false);
        var champions = ObjectValues(catalogs?["data"])
            .Where(entry => Int(entry["key"]) > 0)
            .GroupBy(entry => Int(entry["key"]))
            .ToDictionary(group => group.Key, group => group.First());
        var championIds = champions
            .SelectMany(pair =>
                new[] { Str(pair.Value["name"]), Str(pair.Value["id"]) }.Select(name =>
                    (Name: NormalizeChampion(name), Id: pair.Key)
                )
            )
            .Where(entry => entry.Name.Length > 0)
            .ToLookup(entry => entry.Name, entry => entry.Id, StringComparer.Ordinal);

        JArray? postTeams = post?["teams"] as JArray;
        var players = ArrayObjects(postTeams)
            .SelectMany(team =>
                ArrayObjects(team["players"])
                    .Select(player => new MatchPlayer(
                        NormalizeTeam(player["teamId"]) ?? GetTeamId(team),
                        player,
                        null
                    ))
            )
            .ToList();

        foreach (JObject livePlayer in ArrayObjects(live?["players"]))
        {
            string? team = NormalizeTeam(livePlayer["team"]);
            string? id = PlayerId(livePlayer);
            string champion = NormalizeChampion(Str(livePlayer["championName"]));
            int index = players.FindIndex(player =>
                player.Post is { } saved
                && player.Live is null
                && (team is null || player.Team is null || team == player.Team)
                && (
                    (
                        id is not null
                        && string.Equals(id, PlayerId(saved), StringComparison.OrdinalIgnoreCase)
                    )
                    || (
                        champion.Length > 0
                        && (
                            champion == NormalizeChampion(Str(saved["championName"]))
                            || championIds[champion].Contains(Int(saved["championId"]))
                        )
                    )
                )
            );
            if (index >= 0)
                players[index] = players[index] with
                {
                    Team = players[index].Team ?? team,
                    Live = livePlayer,
                };
            else
                players.Add(new MatchPlayer(team, null, livePlayer));
        }

        string[] teamNames = players
            .Select(player => player.Team)
            .OfType<string>()
            .Distinct()
            .ToArray();
        if (teamNames.Length == 0)
            return null;

        var allyPickIds = ItemIds(draft?["allyPicks"]).ToHashSet();
        var enemyPickIds = ItemIds(draft?["enemyPicks"]).ToHashSet();
        string? allyTeam = FindAlliedTeam();
        if (allyTeam is null)
            return null;

        MatchPlayer[] allies = players.Where(player => player.Team == allyTeam).ToArray();
        var alliedRoles = PlayersByRole(allies);
        var enemyRoles = PlayersByRole(
            players.Where(player => player.Team != null && player.Team != allyTeam).ToArray()
        );
        if (alliedRoles.Count == 0 && enemyRoles.Count == 0)
            return null;

        var itemCatalogTask = ReadCatalogAsync(
            catalogDirectory,
            "item.json",
            useRemoteCatalog,
            cancellationToken
        );
        var abilityTasks = alliedRoles.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                string? championId = Str(
                    champions.GetValueOrDefault(ChampionId(pair.Value))?["id"]
                );
                return
                    string.IsNullOrEmpty(championId) || !championId.All(char.IsAsciiLetterOrDigit)
                    ? Task.FromResult<JObject?>(null)
                    : ReadCatalogAsync(
                        catalogDirectory,
                        $"champion/{championId}.json",
                        useRemoteCatalog,
                        cancellationToken
                    );
            }
        );
        await Task.WhenAll(abilityTasks.Values.Append(itemCatalogTask)).ConfigureAwait(false);
        JObject? items = (await itemCatalogTask.ConfigureAwait(false))?["data"] as JObject;
        bool allAlliesPlaced =
            allies.Length == SpeakerRoleScorer.Roles.Length && alliedRoles.Count == allies.Length;
        bool spellsVerified = allAlliesPlaced && allies.All(player => player.Spells.Length == 2);
        bool itemsVerified = allAlliesPlaced && allies.All(player => player.Inventory is not null);

        var context = new RoleGameContext
        {
            Game =
                Long((post?["gameInfo"] as JObject)?["gameId"])
                ?? (
                    long.TryParse(Path.GetFileName(gamePath).Split('_').Last(), out long matchId)
                        ? matchId
                        : null
                ),
            TranscriptFilename = Path.GetFileName(transcriptPath),
            ItemCoverage =
                "Post-game items when available, otherwise the last recorded inventory. "
                + "Loadout cues require a complete allied roster for ownership checks.",
        };
        foreach (string role in SpeakerRoleScorer.Roles)
        {
            alliedRoles.TryGetValue(role, out MatchPlayer? ally);
            enemyRoles.TryGetValue(role, out MatchPlayer? opponent);
            string? opponentName = ChampionName(opponent);
            JObject? details = abilityTasks.TryGetValue(role, out var task)
                ? await task.ConfigureAwait(false)
                : null;
            int? allyId = ally is null ? null : ChampionId(ally);
            string? allyName = ChampionName(ally);
            var abilities = ObjectValues(details?["data"])
                .SelectMany(champion => ArrayObjects(champion["spells"]))
                .Select(
                    (spell, index) =>
                        new AbilityCue(Str(spell["name"]) ?? "", [])
                        {
                            Id = Str(spell["id"]),
                            Slot = index < 4 ? "QWER"[index].ToString() : null,
                        }
                )
                .Where(cue => cue.Ability.Length > 0)
                .ToArray();
            context.Roles.Add(
                role,
                new RoleRoster
                {
                    AlliedChampionId = allyId,
                    OpposingChampionId = opponent is null ? null : ChampionId(opponent),
                    AlliedChampion = allyName,
                    OpposingChampion = opponentName,
                    AlliedAbilityCues = abilities,
                    SummonerSpells = (ally?.Spells ?? [])
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(name => new LoadoutCue(
                            name,
                            [],
                            spellsVerified ? "verified" : "unverified"
                        )
                        {
                            Id = aliases.Summoner(null, name)?.Id,
                        })
                        .ToArray(),
                    EndgameItems = (ally?.Inventory ?? [])
                        .Distinct()
                        .Select(id =>
                        {
                            string? name =
                                Str((items?[id.ToString()] as JObject)?["name"])
                                ?? aliases.Item(id)?.Name;
                            return name is null
                                ? null
                                : new LoadoutCue(
                                    name,
                                    [],
                                    itemsVerified ? "verified" : "unverified"
                                )
                                {
                                    Id = id,
                                };
                        })
                        .OfType<LoadoutCue>()
                        .ToArray(),
                }
            );
        }
        return aliases.Enrich(context);

        string? FindAlliedTeam()
        {
            var markedTeams = ArrayObjects(postTeams)
                .Where(team =>
                    team["isPlayerTeam"]?.Type == JTokenType.Boolean && (bool)team["isPlayerTeam"]!
                )
                .Select(GetTeamId)
                .OfType<string>()
                .Distinct()
                .ToArray();
            if (markedTeams.Length == 1 && teamNames.Contains(markedTeams[0]))
                return markedTeams[0];

            string? localId = PlayerId(post?["localPlayer"] as JObject);
            var localTeams = players
                .Where(player =>
                    player.Post?["isLocalPlayer"]?.Type == JTokenType.Boolean
                        && (bool)player.Post["isLocalPlayer"]!
                    || localId is not null
                        && string.Equals(
                            localId,
                            PlayerId(player.Post) ?? PlayerId(player.Live),
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .Select(player => player.Team)
                .OfType<string>()
                .Distinct()
                .ToArray();
            if (localTeams.Length == 1)
                return localTeams[0];

            var votes = teamNames
                .Select(team =>
                {
                    int score = 0;
                    foreach (
                        int champion in players
                            .Where(player => player.Team == team)
                            .Select(ChampionId)
                            .Distinct()
                    )
                        score +=
                            (allyPickIds.Contains(champion) ? 1 : 0)
                            - (enemyPickIds.Contains(champion) ? 1 : 0);
                    return (Team: team, Score: score);
                })
                .OrderByDescending(entry => entry.Score)
                .ToArray();
            return
                (votes[0].Score > 0 || votes.Length > 1 && votes[1].Score < 0)
                && (votes.Length == 1 || votes[0].Score > votes[1].Score)
                ? votes[0].Team
                : null;
        }

        int ChampionId(MatchPlayer player)
        {
            int id = Int(player.Post?["championId"]);
            if (id > 0)
                return id;
            int aliasId =
                aliases
                    .Champion(
                        null,
                        Str(player.Live?["championName"]) ?? Str(player.Post?["championName"])
                    )
                    ?.Id
                ?? 0;
            if (aliasId > 0)
                return aliasId;
            int[] matches = championIds[
                NormalizeChampion(
                    Str(player.Live?["championName"]) ?? Str(player.Post?["championName"])
                )
            ]
                .Distinct()
                .ToArray();
            return matches.Length == 1 ? matches[0] : 0;
        }

        string? ChampionName(MatchPlayer? player) =>
            player is null
                ? null
                : Str(champions.GetValueOrDefault(ChampionId(player))?["name"])
                    ?? Str(player.Live?["championName"])
                    ?? Str(player.Post?["championName"])
                    ?? aliases.Champion(ChampionId(player))?.Name;
    }

    private static Dictionary<string, MatchPlayer> PlayersByRole(MatchPlayer[] team)
    {
        var roles = new Dictionary<string, MatchPlayer>(StringComparer.Ordinal);
        MatchPlayer[] smiters = team.Where(player =>
                player.Spells.Contains("Smite", StringComparer.OrdinalIgnoreCase)
            )
            .ToArray();
        if (smiters.Length == 1)
            roles.Add("jungle", smiters[0]);

        foreach (var group in team.Except(roles.Values).GroupBy(player => player.Position))
            if (group.Key is not null && group.Count() == 1)
                roles.TryAdd(group.Key, group.Single());

        if (team.Length == 5 && roles.Count == 4)
            roles.Add(
                SpeakerRoleScorer.Roles.Except(roles.Keys).Single(),
                team.Except(roles.Values).Single()
            );
        return roles;
    }

    internal static string? FindCachedCatalog()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "League Data Recorder",
            "DataDragon"
        );
        if (!Directory.Exists(root))
            return null;

        return Directory
            .EnumerateDirectories(root)
            .Where(path =>
                Version.TryParse(Path.GetFileName(path), out _)
                && File.Exists(Path.Combine(path, "champion.json"))
            )
            .OrderByDescending(path => Version.Parse(Path.GetFileName(path)))
            .FirstOrDefault();
    }

    internal static async Task<string?> FetchCatalogDirectoryAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            string catalog = await LeagueClientApi
                .CacheLatestChampionDataAsync(cancellationToken)
                .ConfigureAwait(false);
            return Path.GetDirectoryName(catalog);
        }
        catch (Exception ex) when (IsCatalogUnavailable(ex, cancellationToken))
        {
            return null;
        }
    }

    internal static async Task<JObject?> ReadCatalogAsync(
        string? directory,
        string file,
        bool useRemote,
        CancellationToken cancellationToken
    )
    {
        if (directory is null)
            return null;

        try
        {
            string path = Path.Combine(directory, file);
            if (!File.Exists(path) && useRemote)
                path = await LeagueClientApi
                    .CacheRoleCatalogAsync(Path.GetFileName(directory), file, cancellationToken)
                    .ConfigureAwait(false);
            return await ReadGameJsonAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsCatalogUnavailable(ex, cancellationToken))
        {
            return null;
        }
    }

    private static bool IsCatalogUnavailable(
        Exception exception,
        CancellationToken cancellationToken
    ) =>
        exception
            is HttpRequestException
                or IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or Newtonsoft.Json.JsonException
                or System.Text.Json.JsonException
        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static async Task<JObject?> ReadGameJsonAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return JObject.Parse(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            );
        }
        catch (Exception ex)
            when (ex
                    is FileNotFoundException
                        or DirectoryNotFoundException
                        or Newtonsoft.Json.JsonException
            )
        {
            return null;
        }
    }

    private static IEnumerable<JObject> ArrayObjects(JToken? token) =>
        token is JArray array ? array.OfType<JObject>() : [];

    private static IEnumerable<JObject> ObjectValues(JToken? token) =>
        token is JObject obj
            ? obj.Properties().Select(property => property.Value).OfType<JObject>()
            : [];

    private static IEnumerable<int> ItemIds(JToken? token) =>
        token switch
        {
            JArray list => list.Select(entry =>
                    Int(entry is JObject obj ? obj["itemID"] ?? obj["itemId"] ?? obj["id"] : entry)
                )
                .Where(id => id > 0),
            _ => [],
        };

    private static long? Long(JToken? token) =>
        token is JValue value && long.TryParse(value.ToString(), out long result) ? result : null;

    private static int Int(JToken? token) =>
        token is JValue value && int.TryParse(value.ToString(), out int result) ? result : 0;

    private static string? Str(JToken? token) =>
        token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)token)
            ? ((string)token!).Trim()
            : null;

    private static string? PlayerId(JObject? player) =>
        Str(player?["riotId"]) ?? Str(player?["summonerName"]);

    private static string NormalizeChampion(string? champion) =>
        string.Concat((champion ?? "").Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string? GetTeamId(JObject team) =>
        NormalizeTeam(team["teamId"])
        ?? ArrayObjects(team["players"])
            .Select(player => NormalizeTeam(player["teamId"]))
            .OfType<string>()
            .FirstOrDefault();

    private static string? NormalizeTeam(JToken? team) =>
        (team as JValue)?.ToString().ToUpperInvariant() switch
        {
            "100" or "ORDER" or "BLUE" => "100",
            "200" or "CHAOS" or "RED" => "200",
            _ => null,
        };

    private static string? NormalizePosition(JToken? position) =>
        Str(position)?.ToUpperInvariant() switch
        {
            "TOP" => "top",
            "JUNGLE" => "jungle",
            "MID" or "MIDDLE" => "mid",
            "ADC" or "BOT" or "BOTTOM" => "adc",
            "SUPPORT" or "UTILITY" => "support",
            _ => null,
        };

    private sealed record MatchPlayer(string? Team, JObject? Post, JObject? Live)
    {
        public string? Position =>
            NormalizePosition(Post?["detectedTeamPosition"])
            ?? NormalizePosition(Live?["position"])
            ?? NormalizePosition(Post?["selectedPosition"]);

        public string[] Spells => GetSpells().ToArray();

        private IEnumerable<string> GetSpells()
        {
            JObject? liveSpells = Live?["summonerSpells"] as JObject;
            for (int slot = 1; slot <= 2; slot++)
            {
                string? spell =
                    SpellNames.GetValueOrDefault(Int(Post?[$"spell{slot}Id"]))
                    ?? Str((liveSpells?[$"summonerSpell{slot}"] as JObject)?["displayName"]);
                if (spell is not null)
                    yield return spell;
            }
        }

        public int[]? Inventory
        {
            get
            {
                if (Post?["items"] is JArray endgameItems && endgameItems.Count > 0)
                    return ItemIds(endgameItems).ToArray();

                if (Post?["stats"] is JObject stats)
                {
                    var slots = stats
                        .Properties()
                        .Where(property =>
                            property.Name.Length == 5
                            && property.Name.StartsWith("ITEM", StringComparison.OrdinalIgnoreCase)
                            && property.Name[4] is >= '0' and <= '6'
                        )
                        .ToArray();
                    if (slots.Length > 0)
                        return slots.Select(slot => Int(slot.Value)).Where(id => id > 0).ToArray();
                }

                JObject? lastFrame = ArrayObjects(Live?["timeline"]).LastOrDefault();
                return lastFrame?["items"] is JArray liveItems ? ItemIds(liveItems).ToArray()
                    : (Post?["items"] is JArray) ? Array.Empty<int>()
                    : null;
            }
        }
    }
}

internal sealed record RoleGameContext
{
    public int Version { get; init; } = 2;
    public string? AliasCatalogFilename { get; init; }
    public string? AliasCatalogFingerprint { get; init; }

    // Keep true for hand-maintained alias overrides. Catalog additions still apply.
    public bool PreserveAliases { get; init; }
    public long? Game { get; init; }
    public string? TranscriptFilename { get; init; }
    public RoleContextSettings Settings { get; init; } = new();
    public string? ItemCoverage { get; init; }
    public Dictionary<string, RoleRoster> Roles { get; init; } = new();
}

internal sealed class RoleContextSettings
{
    public string Matching { get; init; } = "case_insensitive_whole_words_or_phrases";
    public double RepeatWindowSeconds { get; init; } = 8;
    public double MaxGapWithinPhraseSeconds { get; init; } = 1.2;
    public string WordWeightsFilename { get; init; } = "role_word_weights.json";
    public string WordWeightsMode { get; init; } = "flat";
    public double DraftEnemyNameWeight { get; init; } = 1;
    public Dictionary<string, string[]> DraftEnemyNameRoleOverrides { get; init; } =
        new() { ["adc"] = ["adc", "support"], ["support"] = ["adc", "support"] };
    public double DraftAlliedAbilityWeight { get; init; } = 1;
    public double SummonerSpellWeight { get; init; } = 1;
    public double ItemWeight { get; init; } = 1;
    public string AlliedAbilityMatch { get; init; } = "first_person";
    public string LoadoutMatch { get; init; } = "first_person_and_unique_verified_allied_owner";
    public string[] FirstPersonPrefixes { get; init; } =
    [
        "my",
        "i have",
        "i've got",
        "i got",
        "i almost have",
        "i already have",
        "i bought",
        "i built",
        "i'm buying",
        "i'm building",
        "i used",
        "i can use",
        "i can",
        "i'll",
        "i will",
        "i'm gonna",
        "i am going to",
    ];
    public string[] OptionalArticlesAfterFirstPersonPrefix { get; init; } = ["a", "an", "the"];
    public string PhraseBoundaryPunctuation { get; init; } = ".!?;:";
    public string[] AlreadyScoredByWordWeights { get; init; } = ["smite"];
    public string Assignment { get; init; } = "maximum_total_score_one_speaker_per_role";
}

internal sealed record RoleRoster
{
    public int? AlliedChampionId { get; init; }
    public int? OpposingChampionId { get; init; }
    public string[] AlliedChampionAliases { get; init; } = [];
    public string? AlliedChampion { get; init; }
    public string? OpposingChampion { get; init; }
    public string[] OpposingChampionAliases { get; init; } = [];
    public AbilityCue[] AlliedAbilityCues { get; init; } = [];
    public LoadoutCue[] SummonerSpells { get; init; } = [];
    public LoadoutCue[] EndgameItems { get; init; } = [];
}

internal sealed record AbilityCue(string Ability, string[] Aliases)
{
    public string? Id { get; init; }
    public string? Slot { get; init; }
}

internal sealed record LoadoutCue(string Name, string[] Aliases, string Status)
{
    public int? Id { get; init; }
}

#endregion

#region Manual role overrides

internal sealed record SpeakerRoleEditEntry(
    string SpeakerId,
    string CurrentLabel,
    string Role,
    string Example
);

public static partial class TranscribeAudio
{
    internal static bool IsEditableSpeaker(string? speaker) =>
        !string.IsNullOrWhiteSpace(speaker)
        && !speaker.Trim().Equals("unknown", StringComparison.OrdinalIgnoreCase)
        && !speaker.Trim().Equals("unassigned", StringComparison.OrdinalIgnoreCase);

    internal static List<SpeakerRoleEditEntry> BuildSpeakerRoleEditEntries(
        TranscriptionResult transcript,
        IReadOnlyDictionary<string, string> labels
    )
    {
        var words = (transcript.Words ?? [])
            .Where(w => w is not null && IsEditableSpeaker(w.Speaker))
            .ToArray();
        return (transcript.Speakers ?? [])
            .Concat(words.Select(w => w.Speaker!))
            .Where(IsEditableSpeaker)
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(s =>
            {
                labels.TryGetValue(s, out string? label);
                string role = label?.Trim().TrimEnd('?').ToLowerInvariant() switch
                {
                    "top" => "top",
                    "jungle" => "jungle",
                    "mid" => "mid",
                    "adc" or "bot" => "adc",
                    "support" => "support",
                    "coach" or "possible_coach" => "coach",
                    _ => "unassigned",
                };
                string example = string.Join(
                    ' ',
                    words
                        .Where(w => s.Equals(w.Speaker?.Trim(), StringComparison.OrdinalIgnoreCase))
                        .SelectMany(w =>
                            (w.Word ?? "").Split(
                                (char[]?)null,
                                StringSplitOptions.RemoveEmptyEntries
                            )
                        )
                        .Take(28)
                );
                if (example.Length > 200)
                    example = example[..197] + "…";
                return new SpeakerRoleEditEntry(s, FormatSpeakerLabel(s, labels), role, example);
            })
            .ToList();
    }

    internal static Dictionary<string, string> ValidateSpeakerRoleEdits(
        IReadOnlyDictionary<string, string> assignments
    )
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (speaker, value) in assignments)
        {
            if (!IsEditableSpeaker(speaker))
                throw new ArgumentException("Unknown speech has no speaker identity to edit.");
            string role = value?.Trim().ToLowerInvariant() ?? "";
            if (!SpeakerRoleScorer.Roles.Contains(role) && role is not ("coach" or "unassigned"))
                throw new ArgumentException($"Choose a valid role for {speaker}.");
            if (!normalized.TryAdd(speaker.Trim(), role))
                throw new ArgumentException($"Speaker {speaker} appears more than once.");
        }
        var duplicate = normalized
            .Where(p => SpeakerRoleScorer.Roles.Contains(p.Value))
            .GroupBy(p => p.Value)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException(
                $"{FormatSpeakerLabel(duplicate.Key, new Dictionary<string, string> { [duplicate.Key] = duplicate.Key })} is assigned to more than one speaker. "
                    + "Choose a different role or Unassigned for the other speaker. You can swap both roles before saving."
            );
        return normalized;
    }

    /// <summary>Saves a game-wide manual role map without changing transcript words or speaker IDs.</summary>
    public static async Task<string> SaveSpeakerRolesAsync(
        string transcriptPath,
        IReadOnlyDictionary<string, string> assignments,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = ValidateSpeakerRoleEdits(assignments);
        transcriptPath = Path.GetFullPath(transcriptPath);
        var transcript = await ReadRoleJsonAsync<TranscriptionResult>(
                transcriptPath,
                cancellationToken
            )
            .ConfigureAwait(false);
        var expected = BuildSpeakerRoleEditEntries(transcript, new Dictionary<string, string>())
            .Select(e => e.SpeakerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expected.SetEquals(normalized.Keys))
            throw new ArgumentException(
                "The speaker list has changed. Reopen the role editor and try again."
            );

        string path = GetRoleSidecarPath(transcriptPath, "_speaker_roles.json");
        JsonObject data = new();
        if (File.Exists(path))
        {
            try
            {
                data =
                    JsonNode.Parse(
                        await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                    ) as JsonObject
                    ?? new();
            }
            catch (JsonException) { } // The original damaged file is still backed up below.
        }
        if (data["manual"]?.ToJsonString() != "true")
        {
            data["automaticAssignments"] = data["assignments"]?.DeepClone();
            data["automaticAssignmentDecisions"] = data["assignmentDecisions"]?.DeepClone();
        }
        data["manual"] = true;
        data["preserveAssignments"] = true;
        data["assignmentSource"] = "manual";
        data["manualEditedAtUtc"] = DateTimeOffset.UtcNow;
        data["transcriptFilename"] = Path.GetFileName(transcriptPath);
        data["assignments"] = JsonSerializer.SerializeToNode(normalized, RoleJsonOptions);
        data["assignmentDecisions"] = new JsonObject();
        data["speakerCategories"] = JsonSerializer.SerializeToNode(
            normalized.ToDictionary(
                p => p.Key,
                p => SpeakerRoleScorer.Roles.Contains(p.Value) ? "player" : p.Value
            ),
            RoleJsonOptions
        );
        data["unassignedRoles"] = JsonSerializer.SerializeToNode(
            SpeakerRoleScorer.Roles.Except(normalized.Values),
            RoleJsonOptions
        );
        data["unassignedSpeakers"] = JsonSerializer.SerializeToNode(
            normalized
                .Where(p => !SpeakerRoleScorer.Roles.Contains(p.Value))
                .Select(p => p.Key)
                .ToArray(),
            RoleJsonOptions
        );
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
            File.Copy(path, path + ".previous", overwrite: true);
        await WriteRoleJsonAsync(path, data, cancellationToken).ConfigureAwait(false);
        return path;
    }
}

#endregion

#region Speaker display labels

public static partial class TranscribeAudio
{
    public static async Task<IReadOnlyDictionary<string, string>> ReadSpeakerRoleLabelsAsync(
        string transcriptPath,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        string path = GetRoleSidecarPath(Path.GetFullPath(transcriptPath), "_speaker_roles.json");
        try
        {
            var roles = await ReadRoleJsonAsync<SpeakerRoleResult>(path, cancellationToken)
                .ConfigureAwait(false);
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var decisions = roles.AssignmentDecisions ?? [];
            foreach (var pair in roles.Assignments ?? [])
                if (!string.IsNullOrWhiteSpace(pair.Key))
                    labels[pair.Key.Trim()] =
                        pair.Value
                        + (
                            !roles.Manual
                            && decisions.TryGetValue(pair.Key, out var decision)
                            && decision.Strength == "weak"
                                ? "?"
                                : ""
                        );
            if (roles.Manual)
                return labels; // Automatic proposals must not undo an explicit manual choice.
            foreach (var pair in decisions.Where(p => !p.Value.Accepted))
                if (!labels.ContainsKey(pair.Key))
                    labels[pair.Key] = pair.Value.Role + "?";
            foreach (var pair in roles.SpeakerCategories ?? [])
                if (!labels.ContainsKey(pair.Key))
                    labels[pair.Key] = pair.Value;
            return labels;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log?.Invoke($"Could not read speaker roles: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string FormatSpeakerLabel(
        string? speaker,
        IReadOnlyDictionary<string, string>? roles = null
    )
    {
        if (string.IsNullOrWhiteSpace(speaker))
            return "Unknown";
        speaker = speaker.Trim();
        if (
            speaker.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || speaker.Equals("unassigned", StringComparison.OrdinalIgnoreCase)
        )
            return "Unknown";
        if (roles is not null && roles.TryGetValue(speaker, out string? role))
        {
            bool tentative = role?.EndsWith('?') == true;
            string? label = role?.Trim().TrimEnd('?').ToLowerInvariant() switch
            {
                "top" => "Top",
                "jungle" => "Jungle",
                "mid" => "Mid",
                "adc" or "bot" => "ADC",
                "support" => "Support",
                "coach" => "Coach",
                "possible_coach" => "Coach?",
                "unassigned" => "Unassigned",
                _ => null,
            };
            if (label is not null)
                return label + (tentative ? "?" : "");
        }
        return
            speaker.StartsWith("speaker_", StringComparison.OrdinalIgnoreCase) && speaker.Length > 8
            ? "Speaker " + speaker[8..]
            : speaker;
    }
}

#endregion
