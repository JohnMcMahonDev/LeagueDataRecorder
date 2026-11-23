using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace League_Data_Recorder;

#region Cleanup and vocabulary conditioning

public static partial class TranscribeAudio
{
    /// <summary>Cleans an existing transcript without running Whisper or NeMo again.</summary>
    public static async Task<TranscriptionResult> PostProcessTranscriptAsync(
        string transcriptPath,
        string? outputPath = null,
        string? rttmPath = null,
        TranscriptPostProcessingOptions? options = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        transcriptPath = Path.GetFullPath(transcriptPath);
        options ??= new TranscriptPostProcessingOptions();
        outputPath = outputPath is null
            ? Path.Combine(
                Path.GetDirectoryName(transcriptPath)!,
                Path.GetFileNameWithoutExtension(transcriptPath) + "_cleaned.json"
            )
            : Path.GetFullPath(outputPath);
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        rttmPath = rttmPath is null
            ? Path.ChangeExtension(transcriptPath, ".rttm")
            : Path.GetFullPath(rttmPath);
        if (comparer.Equals(outputPath, rttmPath))
            throw new ArgumentException("The transcript output and RTTM paths must be different.");

        var source =
            JsonSerializer.Deserialize<TranscriptionResult>(
                await File.ReadAllTextAsync(transcriptPath, cancellationToken).ConfigureAwait(false)
            ) ?? throw new JsonException("The transcript JSON is empty.");
        if (
            !string.IsNullOrWhiteSpace(source.AudioFile)
            && comparer.Equals(outputPath, Path.GetFullPath(source.AudioFile))
        )
            throw new ArgumentException(
                "The transcript output must be different from the audio recording."
            );
        var vocabulary = await TranscriptVocabulary
            .LoadAsync(source.AudioFile, options, log, cancellationToken)
            .ConfigureAwait(false);
        var turns = File.Exists(rttmPath)
            ? ReadRttm(rttmPath)
                .Select(t => new TranscriptSpeakerTurn(t.Start, t.End, t.Speaker))
                .ToArray()
            : Array.Empty<TranscriptSpeakerTurn>();
        var result = TranscriptPostProcessor.Process(
            source,
            options,
            turns,
            vocabulary,
            cancellationToken
        );
        result.JsonPath = outputPath;
        result.RttmPath = File.Exists(rttmPath) ? rttmPath : string.Empty;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        string temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer
                    .SerializeAsync(stream, result, TranscriptJsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (OutputLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, outputPath, overwrite: true);
            }
        }
        finally
        {
            TryDelete(temporary);
        }
        log?.Invoke(result.PostProcessing?.Summary ?? "Post-processing is disabled.");
        return result;
    }
}

public sealed record TranscriptPostProcessingOptions
{
    public bool Enabled { get; init; } = true;
    public bool CorrectLeagueTerms { get; init; } = true;

    // Normalize unique match aliases using league_aliases.json. RawWords always keeps the ASR output.
    public bool NormalizeLeagueAliases { get; init; } = true;
    public bool RemoveLoops { get; init; } = true;
    public bool RepairSpeakers { get; init; } = true;
    public bool RepairSentenceSpans { get; init; } = true;
    public bool RepairBoundaryFragments { get; init; } = true;

    // Sequence decoding resolves overlapping/boundary labels using neighbouring words.
    public bool RepairUtteranceContinuity { get; init; } = true;
    public double SpeakerSwitchPenalty { get; init; } = 1.4;
    // Timing estimates are explicitly marked; they never replace RawWords/AlignedWords.
    public bool RepairCollapsedTimings { get; init; } = true;
    public double MaxContinuityGapSeconds { get; init; } = 1.8;
    public bool MarkUnclearSpeech { get; init; } = true;

    // Extreme repetitions are retained in RawWords and marked for audio retry.
    public bool RemoveLongRepetitionLoops { get; init; } = true;
    public double SentenceSpeakerCoverage { get; init; } = 0.80;
    public double SentenceSpeakerMargin { get; init; } = 0.20;
    public double SentenceEdgeToleranceSeconds { get; init; } = 0.30;
    public bool SuggestSimilarTerms { get; init; } = true;

    // Null automatically uses transcript_corrections.json beside the application, if present.
    public string? CorrectionsPath { get; init; }

    // Game_<id> prefix, not the containing directory. Usually inferred from *_Audio.wav.
    public string? GamePath { get; init; }
    public string? GameContextPath { get; init; }
    public string? DataDragonDirectory { get; init; }

    // Null uses league_aliases.json beside the executable. Used by transcription and role scoring.
    public string? AliasesPath { get; init; }
    public double NearestTurnSeconds { get; init; } = 0.18;
    public double MaxUnknownRunSeconds { get; init; } = 1.6;
    public double MaxSpeakerIslandSeconds { get; init; } = 0.25;
    public int MaxSpeakerIslandWords { get; init; } = 1;
    public int MinSingleWordLoopRepeats { get; init; } = 8;
    public int MinPhraseLoopRepeats { get; init; } = 4;
    public int KeepLoopRepetitions { get; init; } = 2;
    public int MaxLoopPhraseWords { get; init; } = 64;
}

public readonly record struct TranscriptSpeakerTurn(double Start, double End, string Speaker);

public sealed class TranscriptPostProcessingReport
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 9;

    [JsonPropertyName("collapsed_timing_words_repaired")]
    public int CollapsedTimingWordsRepaired { get; set; }

    [JsonPropertyName("unclear_speech_spans")]
    public int UnclearSpeechSpans { get; set; }

    [JsonPropertyName("unknown_without_diarization_coverage")]
    public int UnknownWithoutDiarizationCoverage { get; set; }

    [JsonPropertyName("boundary_fragments_repaired")]
    public int BoundaryFragmentsRepaired { get; set; }

    [JsonPropertyName("sentence_spans_repaired")]
    public int SentenceSpansRepaired { get; set; }

    [JsonPropertyName("input_entries")]
    public int InputEntries { get; set; }

    [JsonPropertyName("output_entries")]
    public int OutputEntries { get; set; }

    [JsonPropertyName("original_unknown_entries")]
    public int OriginalUnknownEntries { get; set; }

    [JsonPropertyName("unknown_words_before_repair")]
    public int UnknownWordsBeforeRepair { get; set; }

    [JsonPropertyName("unknown_words_after_repair")]
    public int UnknownWordsAfterRepair { get; set; }

    [JsonPropertyName("speaker_words_inferred")]
    public int SpeakerWordsInferred { get; set; }

    [JsonPropertyName("speaker_words_relabelled")]
    public int SpeakerWordsRelabelled { get; set; }

    [JsonPropertyName("loop_entries_removed")]
    public int LoopEntriesRemoved { get; set; }

    [JsonPropertyName("league_term_corrections")]
    public int LeagueTermCorrections { get; set; }

    [JsonPropertyName("estimated_timing_words")]
    public int EstimatedTimingWords { get; set; }

    [JsonPropertyName("rttm_available")]
    public bool RttmAvailable { get; set; }

    [JsonPropertyName("notes")]
    public List<string> Notes { get; init; } = new();

    [JsonPropertyName("edits")]
    public List<TranscriptEdit> Edits { get; init; } = new();

    [JsonPropertyName("review")]
    public List<TranscriptReview> Review { get; init; } = new();

    [JsonIgnore]
    public string Summary =>
        $"Post-processing: {LeagueTermCorrections} League term correction(s), "
        + $"{LoopEntriesRemoved} loop entries removed, {SpeakerWordsInferred} speaker word(s) inferred, "
        + $"{SpeakerWordsRelabelled} speaker word(s) repaired; {UnknownWordsAfterRepair} remain unknown, "
        + $"{UnclearSpeechSpans} unclear speech span(s) need audio review.";
}

public sealed record TranscriptEdit(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("start")] double Start,
    [property: JsonPropertyName("end")] double End,
    [property: JsonPropertyName("before")] string? Before,
    [property: JsonPropertyName("after")] string? After,
    [property: JsonPropertyName("reason")] string Reason
);

public sealed record TranscriptReview(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("start")] double Start,
    [property: JsonPropertyName("end")] double End,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("reason")] string Reason
);

public static partial class TranscriptPostProcessor
{
    private static readonly Regex Pieces = new(@"\S+", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Interjections = new(StringComparer.OrdinalIgnoreCase)
    {
        "yeah",
        "yes",
        "no",
        "nah",
        "okay",
        "ok",
        "oh",
        "wait",
        "what",
        "huh",
        "nice",
        "true",
        "sure",
        "go",
        "stop",
        "back",
        "help",
        "chill",
        "thanks",
        "yep",
        "nope",
        "yo",
        "hey",
    };

    /// <summary>Deterministic, CPU-only cleanup. Reprocessing uses validated aligned_words, falling back to raw_words.</summary>
    public static TranscriptionResult Process(
        TranscriptionResult source,
        TranscriptPostProcessingOptions? options = null,
        IReadOnlyList<TranscriptSpeakerTurn>? turns = null,
        TranscriptVocabulary? vocabulary = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new TranscriptPostProcessingOptions();
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled)
            return source;
        Validate(options);
        var raw = (source.RawWords ?? source.Words).Select(CopyWord).ToList();
        if (
            raw.Any(w =>
                !double.IsFinite(w.Start)
                || !double.IsFinite(w.End)
                || w.Start < 0
                || w.End < w.Start
            )
        )
            throw new InvalidDataException(
                "Transcript word timestamps must be finite, nonnegative, and end after start."
            );
        var report = new TranscriptPostProcessingReport
        {
            InputEntries = raw.Count,
            OriginalUnknownEntries = raw.Count(w => Unknown(w.Speaker)),
            RttmAvailable = turns?.Count > 0,
        };
        var words = new List<WordResult>();
        var alignedInput = TranscriptAlignment.ValidAlignedWords(source);
        var input = alignedInput ?? raw;
        if (source.Alignment is not null)
            report.Notes.Add(
                alignedInput is not null ? source.Alignment.Message
                : source.AlignedWords is not null
                    ? "Stored alignment is stale/invalid; original ASR timings are being used."
                : source.Alignment.Message
            );
        var orderedRaw = input.OrderBy(w => w.Start).ToArray();
        for (int index = 0; index < orderedRaw.Length; index++)
        {
            var word = orderedRaw[index];
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(word.Word))
                continue;
            if (Pieces.Matches(word.Word).Count <= 1)
                words.Add(CopyWord(word));
            else
            {
                // A fallback sentence can overlap the following decoded entry. Do not let
                // invented word times insert that following word into the middle of the sentence.
                double estimateEnd =
                    index + 1 < orderedRaw.Length
                        ? Math.Min(word.End, orderedRaw[index + 1].Start)
                        : word.End;
                if (estimateEnd < word.End)
                    report.Review.Add(
                        new TranscriptReview(
                            "estimated_timing_conflict",
                            word.Start,
                            word.End,
                            word.Word.Trim(),
                            "Fallback timing overlaps the next entry. Estimated words are bounded by the next entry to preserve decoded text order; audio alignment is needed for accurate times."
                        )
                    );
                var split = SplitSegmentText(word.Word, word.Start, estimateEnd, word.Speaker);
                foreach (var part in split)
                {
                    part.OverlappingSpeech = word.OverlappingSpeech;
                    part.OverlappingSpeakers = word.OverlappingSpeakers?.ToArray();
                }
                words.AddRange(split);
            }
        }
        // Stable ordering preserves the text of zero-duration tokens sharing one timestamp.
        words = words.OrderBy(w => w.Start).ToList();
        foreach (var word in words)
            if (Unknown(word.Speaker))
                word.Speaker = null;
        report.EstimatedTimingWords = words.Count(w => w.TimingSource == "segment_estimate");
        if (report.EstimatedTimingWords > 0)
            report.Notes.Add(
                "Fallback sentence entries were split with estimated word times. These are not acoustic alignments."
            );
        if (!report.RttmAvailable)
            report.Notes.Add(
                "No RTTM supplied. Speaker inference uses only short gaps and brief isolated label changes."
            );

        if (options.RemoveLoops)
        {
            CleanFusedRepetitions(words, options, report, cancellationToken);
            RemoveLoops(words, options, report, cancellationToken);
        }
        if (options.RepairCollapsedTimings)
            RepairCollapsedTimings(words, report, cancellationToken);
        TranscriptOverlap.Apply(words, turns ?? [], cancellationToken: cancellationToken);
        report.UnknownWordsBeforeRepair = words.Count(w => Unknown(w.Speaker) && !IsReadabilityMarker(w));
        if (options.RepairSpeakers)
            RepairSpeakers(
                words,
                new SpeakerTimeline(turns ?? Array.Empty<TranscriptSpeakerTurn>()),
                options,
                report,
                cancellationToken
            );
        // Speaker repairs may reconnect a loop whose labels were fragmented.
        if (options.RemoveLoops)
            RemoveLoops(words, options, report, cancellationToken);
        report.UnknownWordsAfterRepair = words.Count(w => Unknown(w.Speaker) && !IsReadabilityMarker(w));
        vocabulary ??= TranscriptVocabulary.Default();
        vocabulary = vocabulary.ForTranscript(words);
        if (options.CorrectLeagueTerms)
            CorrectTerms(words, vocabulary, options, report, cancellationToken);
        report.UnknownWordsAfterRepair = words.Count(w => Unknown(w.Speaker) && !IsReadabilityMarker(w));
        report.EstimatedTimingWords = words.Count(w =>
            w.TimingSource is "segment_estimate" or "term_estimate" or "dtw_fallback_estimate" or "continuity_estimate"
        );
        if (report.CollapsedTimingWordsRepaired > 0)
            report.Notes.Add("Collapsed word times were bounded by neighbouring words and marked continuity_estimate. These are readability estimates, not acoustic alignment.");
        if (words.Any(w => w.TimingSource == "term_estimate"))
            report.Notes.Add(
                "A name correction that changes the word count uses estimated times within the original word span."
            );
        TranscriptOverlap.Apply(words, turns ?? [], addMissingSpeech: true,
            duration: source.DurationSeconds, cancellationToken: cancellationToken);
        AddReviews(words, vocabulary, options, report, cancellationToken);
        CoalesceRepetitionReviews(report);
        var reviewTimeline = new SpeakerTimeline(turns ?? Array.Empty<TranscriptSpeakerTurn>());
        report.UnknownWithoutDiarizationCoverage = words.Count(w =>
            Unknown(w.Speaker) && !IsReadabilityMarker(w)
            && reviewTimeline.Coverage(w.Start, w.End).Count == 0);
        report.OutputEntries = words.Count;
        return new TranscriptionResult
        {
            AudioFile = source.AudioFile,
            AudioTimelineShiftSeconds = source.AudioTimelineShiftSeconds,
            DurationSeconds = source.DurationSeconds,
            Language = source.Language,
            AsrEngine = source.AsrEngine,
            AsrModel = source.AsrModel,
            DiarizationEngine = source.DiarizationEngine,
            DiarizationModel = source.DiarizationModel,
            SampleRate = source.SampleRate,
            Speakers = source
                .Speakers.Concat(words.Select(w => w.Speaker).OfType<string>())
                .Where(s => !Unknown(s))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray(),
            WordCount = words.Count,
            Text = Join(words),
            Words = words,
            RawWords = raw,
            AlignedWords = alignedInput,
            Alignment =
                source.AlignedWords is not null && alignedInput is null
                    ? new TranscriptAlignmentReport
                    {
                        Status = "invalidated",
                        Message =
                            "Stored alignment no longer matches raw ASR words; rerun the alignment stage.",
                        RawFingerprint = TranscriptAlignment.RawFingerprint(source),
                    }
                    : source.Alignment,
            PostProcessing = report,
            Execution = source.Execution,
            DiarizationExecution = source.DiarizationExecution,
            AdditionalData = source.AdditionalData,
            JsonPath = source.JsonPath,
            RttmPath = source.RttmPath,
        };
    }

    public static List<WordResult> SplitSegmentText(
        string text,
        double start,
        double end,
        string? speaker = null
    )
    {
        var matches = Pieces.Matches(text).Cast<Match>().ToArray();
        int total = matches.Sum(m => m.Length);
        int used = 0;
        var result = new List<WordResult>(matches.Length);
        foreach (var match in matches)
        {
            result.Add(
                new WordResult
                {
                    Start = start + (end - start) * used / total,
                    End = start + (end - start) * (used + match.Length) / total,
                    Word = " " + match.Value,
                    Speaker = speaker,
                    TimingSource = "segment_estimate",
                    // A segment-level ASR probability is not a word-level probability.
                }
            );
            used += match.Length;
        }
        return result;
    }

    internal static WordResult CopyWord(WordResult word) =>
        new()
        {
            Start = word.Start,
            End = word.End,
            Word = word.Word,
            Probability = word.Probability,
            Speaker = word.Speaker,
            TimingSource = word.TimingSource,
            SpeakerSource = word.SpeakerSource,
            OverlappingSpeech = word.OverlappingSpeech,
            OverlappingSpeakers = word.OverlappingSpeakers?.ToArray(),
            AlignmentConfidence = word.AlignmentConfidence,
            DtwFirstTokenTime = word.DtwFirstTokenTime,
            DtwLastTokenTime = word.DtwLastTokenTime,
            DtwFallbackReason = word.DtwFallbackReason,
        };

    internal static bool Unknown(string? speaker) =>
        string.IsNullOrWhiteSpace(speaker)
        || speaker.Equals("unknown", StringComparison.OrdinalIgnoreCase)
        || speaker.Equals("unassigned", StringComparison.OrdinalIgnoreCase);

    internal static string Key(string text) =>
        Regex
            .Replace(text.Replace('’', '\'').ToLowerInvariant(), @"[^\p{L}\p{Nd}']", "")
            .Trim('\'');

    internal static bool EndsSentence(WordResult word) =>
        Regex.IsMatch(word.Word.Trim(), "[.!?;][\\\"'’)]*$");

    private static string Join(IEnumerable<WordResult> words) =>
        string.Join(" ", words.Select(w => w.Word.Trim()));

    private static bool Close(WordResult a, WordResult b, double gap = 0.25) =>
        b.Start - a.End >= -0.03 && b.Start - a.End <= gap;

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

    private static bool Link(WordResult a, WordResult b) =>
        Close(a, b) && !EndsSentence(a) && !a.OverlappingSpeech && !b.OverlappingSpeech;

    // ASR word spans can overlap even when only one person is speaking.
    private static bool PhraseLink(WordResult a, WordResult b) =>
        b.Start >= a.Start && b.Start - a.End <= 0.45 && !EndsSentence(a);

    private static void Validate(TranscriptPostProcessingOptions options)
    {
        if (!double.IsFinite(options.MaxContinuityGapSeconds)
            || options.MaxContinuityGapSeconds < 0 || options.MaxContinuityGapSeconds > 2)
            throw new ArgumentOutOfRangeException(nameof(options), "Continuity gap must be between zero and two seconds.");
        if (
            !double.IsFinite(options.NearestTurnSeconds)
            || options.NearestTurnSeconds < 0
            || !double.IsFinite(options.MaxUnknownRunSeconds)
            || options.MaxUnknownRunSeconds < 0
            || !double.IsFinite(options.MaxSpeakerIslandSeconds)
            || options.MaxSpeakerIslandSeconds < 0
            || options.MaxSpeakerIslandWords < 0
            || options.MinSingleWordLoopRepeats < 3
            || options.MinPhraseLoopRepeats < 3
            || options.KeepLoopRepetitions < 1
            || options.MaxLoopPhraseWords < 1
            || options.MaxLoopPhraseWords > 128
            || options.KeepLoopRepetitions
                >= Math.Min(options.MinSingleWordLoopRepeats, options.MinPhraseLoopRepeats)
        )
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Post-processing thresholds are invalid."
            );
        if (
            !double.IsFinite(options.SpeakerSwitchPenalty)
            || options.SpeakerSwitchPenalty < 0
            || options.SpeakerSwitchPenalty > 5
        )
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Speaker switch penalty must be between zero and five."
            );
        if (
            !double.IsFinite(options.SentenceSpeakerCoverage)
            || options.SentenceSpeakerCoverage <= 0.5
            || options.SentenceSpeakerCoverage > 1
            || !double.IsFinite(options.SentenceSpeakerMargin)
            || options.SentenceSpeakerMargin <= 0
            || options.SentenceSpeakerMargin > 1
            || !double.IsFinite(options.SentenceEdgeToleranceSeconds)
            || options.SentenceEdgeToleranceSeconds < 0
            || options.SentenceEdgeToleranceSeconds > 0.75
        )
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Sentence repair thresholds are invalid."
            );
    }

    private static void RemoveLoops(
        List<WordResult> words,
        TranscriptPostProcessingOptions options,
        TranscriptPostProcessingReport report,
        CancellationToken cancellationToken
    )
    {
        var keys = words.Select(w => Key(w.Word)).ToList();
        for (int i = 0; i < words.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (
                int size = 1;
                size <= options.MaxLoopPhraseWords && i + size * 2 <= words.Count;
                size++
            )
            {
                if (keys[i].Length == 0 || keys[i] != keys[i + size])
                    continue;
                int end = i + size;
                while (end + size <= words.Count)
                {
                    bool equal = true;
                    for (int k = 0; k < size; k++)
                    {
                        var a = words[i + k];
                        var b = words[end + k];
                        if (keys[i + k] != keys[end + k] || !Close(words[end + k - 1], b, 3.0))
                        {
                            equal = false;
                            break;
                        }
                    }
                    if (!equal)
                        break;
                    end += size;
                }
                int repeats = (end - i) / size;
                int threshold =
                    size == 1 ? options.MinSingleWordLoopRepeats : options.MinPhraseLoopRepeats;
                bool duplicateWindow =
                    repeats == 2
                    && size >= 3
                    && Enumerable
                        .Range(0, size)
                        .All(k =>
                            Math.Abs(words[i + k].Start - words[i + size + k].Start) <= 0.04
                            && Math.Abs(words[i + k].End - words[i + size + k].End) <= 0.04
                            && words[i + k].TimingSource != "segment_estimate"
                            && words[i + size + k].TimingSource != "segment_estimate"
                        );
                if (repeats < threshold && !duplicateWindow)
                    continue;
                var run = words.GetRange(i, end - i);
                if (run.Any(IsReadabilityMarker))
                    continue;
                double span = run.Max(w => w.End) - run.Min(w => w.Start);
                double zeroFraction = (double)run.Count(w => w.End - w.Start <= 0.005) / run.Count;
                // A long repetition alone is not enough: real team-fight calls repeat frequently.
                // Estimated fallback times cannot prove an implausible speaking rate.
                // Artificial fallback times cannot prove that real spoken repetition is a loop.
                // Zero-length source spans stay invalid after fallback splitting. Estimated
                // positive durations prove nothing, but identical zero times are still evidence.
                bool collapsed = zeroFraction >= 0.6;
                bool impossibleRate =
                    span > 0
                    && run.Count / span > 18
                    && run.Count >= 8
                    && run.All(w => w.TimingSource != "segment_estimate");
                bool sameSpeaker = run.All(w => Same(w.Speaker, run[0].Speaker));
                bool extreme =
                    options.RemoveLongRepetitionLoops
                    && run.Count >= 24
                    && repeats >= (size == 1 ? 20 : 8)
                    && (span >= 5 || zeroFraction >= 0.6)
                    && (zeroFraction >= 0.6 || run.Count / Math.Max(1, span) > 1.0);
                bool timingProof = collapsed || impossibleRate || duplicateWindow || extreme;
                if (
                    !timingProof
                    || (
                        !extreme
                        && !sameSpeaker
                        && zeroFraction < 0.8
                        && !(impossibleRate && zeroFraction >= 0.3)
                    )
                )
                {
                    if (
                        run.Count >= 24
                        && !report.Review.Any(r =>
                            r.Type == "long_repetition" && Math.Abs(r.Start - run[0].Start) < 0.01
                        )
                    )
                        report.Review.Add(
                            new TranscriptReview(
                                "long_repetition",
                                run[0].Start,
                                run[^1].End,
                                Join(run.Take(32)),
                                "Long repeated text has plausible/uncertain timing or competing speaker labels; retained for audio review."
                            )
                        );
                    continue;
                }
                int keep = size * (duplicateWindow || size >= 8 ? 1 : options.KeepLoopRepetitions);
                report.Edits.Add(
                    new TranscriptEdit(
                        "repetition_loop",
                        run[0].Start,
                        run[^1].End,
                        Join(run),
                        Join(run.Take(keep)),
                        $"{repeats} repetitions with "
                            + (
                                duplicateWindow ? "duplicate phrase timestamps."
                                : collapsed ? "collapsed timestamps."
                                : extreme
                                    ? "an extreme repeated hypothesis; retry the original audio span."
                                : "an implausible timestamp rate."
                            )
                    )
                );
                report.Review.Add(
                    new TranscriptReview(
                        "asr_retry",
                        run.Min(w => w.Start),
                        run.Max(w => w.End),
                        Join(run.Take(16)),
                        "Repeated hypothesis shortened for readability. RawWords preserves it; re-decode this audio to recover missing speech."
                    )
                );
                foreach (var retained in run.Take(keep))
                    retained.SpeakerSource = "repetition_placeholder";
                words.RemoveRange(i + keep, end - i - keep);
                keys.RemoveRange(i + keep, end - i - keep);
                report.LoopEntriesRemoved += end - i - keep;
                double omittedStart = run.Take(keep).Max(w => w.End);
                double omittedEnd = run.Max(w => w.End);
                if (options.MarkUnclearSpeech && !duplicateWindow && omittedEnd - omittedStart >= 2)
                {
                    var marker = UnclearSpeech(omittedStart, omittedEnd);
                    words.Insert(i + keep, marker);
                    keys.Insert(i + keep, Key(marker.Word));
                    report.UnclearSpeechSpans++;
                }
                i += keep - 1;
                break;
            }
        }
    }

    private static void RepairSpeakers(
        List<WordResult> words,
        SpeakerTimeline timeline,
        TranscriptPostProcessingOptions options,
        TranscriptPostProcessingReport report,
        CancellationToken cancellationToken
    )
    {
        var originalLabels = words.Select(w => w.Speaker).ToArray();
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                word.SpeakerSource == "repetition_placeholder" || IsReadabilityMarker(word)
                || word.TimingSource == "segment_estimate" && word.End - word.Start <= 0.005
            )
                continue;
            var evidence = timeline.Evidence(word.Start, word.End);
            word.OverlappingSpeech |= evidence.Overlap;
            if (evidence.Overlap)
                continue;
            if (
                evidence.Best is not null
                && (Unknown(word.Speaker) || word.TimingSource == "segment_estimate")
            )
                SetSpeaker(word, evidence.Best, "rttm_overlap", report);
            else if (Unknown(word.Speaker) && word.End - word.Start <= 0.65)
            {
                string? near = timeline.Nearest(word.Start, word.End, options.NearestTurnSeconds);
                if (near is not null)
                    SetSpeaker(word, near, "nearby_rttm_turn", report);
            }
        }

        if (options.RepairSentenceSpans)
            RepairSentenceSpans(words, timeline, options, report, cancellationToken);
        if (options.RepairBoundaryFragments)
            RepairBoundaryFragments(words, timeline, options, report, cancellationToken);
        if (options.RepairUtteranceContinuity)
            RepairShortZeroTimingPrefixes(words, timeline, report);
        if (options.RepairUtteranceContinuity)
            RepairUtteranceContinuity(words, timeline, options, report, cancellationToken);
        if (options.RepairUtteranceContinuity)
            RepairStableSpeakerSpans(words, timeline, report, cancellationToken);

        // Infer from a frozen set of labels so a guess cannot become evidence for the next guess.
        var labels = words.Select(w => w.Speaker).ToArray();
        for (int i = 0; i < words.Count; i++)
        {
            if (!Unknown(labels[i]) || IsReadabilityMarker(words[i]))
                continue;
            int end = i + 1;
            while (end < words.Count && !IsReadabilityMarker(words[end])
                && Unknown(labels[end]) && Link(words[end - 1], words[end]))
                end++;
            if (
                end - i <= 6
                && words[end - 1].End - words[i].Start <= options.MaxUnknownRunSeconds
                && !words.GetRange(i, end - i).Any(w => w.OverlappingSpeech)
            )
            {
                bool left = i > 0 && !Unknown(labels[i - 1]) && Link(words[i - 1], words[i]);
                bool right =
                    end < words.Count && !Unknown(labels[end]) && Link(words[end - 1], words[end]);
                string? target =
                    left && right && Same(labels[i - 1], labels[end]) ? labels[i - 1] : null;
                // Only a very short prefix/suffix may inherit from one side; never choose between disagreeing sides.
                if (
                    target is null
                    && left != right
                    && end - i <= 2
                    && words[end - 1].End - words[i].Start <= 0.4
                )
                {
                    if (
                        left
                        && (
                            end == words.Count
                            || EndsSentence(words[end - 1])
                            || words[end].Start - words[end - 1].End > 0.65
                        )
                    )
                        target = labels[i - 1];
                    if (
                        right
                        && (
                            i == 0
                            || EndsSentence(words[i - 1])
                            || words[i].Start - words[i - 1].End > 0.65
                        )
                    )
                        target = labels[end];
                }
                if (target is not null)
                    for (int j = i; j < end; j++)
                        if (!IsReadabilityMarker(words[j]))
                            SetSpeaker(words[j], target, "short_speaker_gap", report);
            }
            i = end - 1;
        }

        labels = words.Select(w => w.Speaker).ToArray();
        // Repair A-B-A only. Long A-B changes can be a real interruption, even inside a sentence.
        for (int i = 1; i + 1 < words.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Unknown(labels[i]) || Same(labels[i - 1], labels[i]))
                continue;
            int end = i + 1;
            while (end < words.Count && Same(labels[end], labels[i]))
                end++;
            if (
                end == words.Count
                || Unknown(labels[i - 1])
                || !Same(labels[i - 1], labels[end])
                || end - i > options.MaxSpeakerIslandWords
                || words[end - 1].End - words[i].Start > options.MaxSpeakerIslandSeconds
            )
                continue;
            if (
                !Enumerable.Range(i, end - i + 1).All(j => Link(words[j - 1], words[j]))
                || words.GetRange(i, end - i).Any(w => Interjections.Contains(Key(w.Word)))
                || timeline.HasStableTurn(labels[i]!, words[i].Start, words[end - 1].End)
            )
                continue;
            bool stableLeft =
                i >= 2 && Same(labels[i - 2], labels[i - 1]) && Link(words[i - 2], words[i - 1]);
            bool stableRight =
                end + 1 < words.Count
                && Same(labels[end], labels[end + 1])
                && Link(words[end], words[end + 1]);
            if (!stableLeft || !stableRight)
                continue;
            for (int j = i; j < end; j++)
                SetSpeaker(words[j], labels[i - 1]!, "short_speaker_island", report);
            i = end - 1;
        }
        report.SpeakerWordsInferred = Enumerable
            .Range(0, words.Count)
            .Count(i => Unknown(originalLabels[i]) && !Unknown(words[i].Speaker));
        report.SpeakerWordsRelabelled = Enumerable
            .Range(0, words.Count)
            .Count(i => !Unknown(originalLabels[i]) && !Same(originalLabels[i], words[i].Speaker));
    }

    private static void RepairSentenceSpans(
        List<WordResult> words,
        SpeakerTimeline timeline,
        TranscriptPostProcessingOptions options,
        TranscriptPostProcessingReport report,
        CancellationToken ct
    )
    {
        for (int first = 0; first < words.Count; )
        {
            ct.ThrowIfCancellationRequested();
            int last = first + 1;
            while (
                last < words.Count
                && !EndsSentence(words[last - 1])
                && words[last].Start - words[last - 1].End <= 0.65
                && words[last].Start - words[first].Start <= 12
                && last - first < 60
            )
                last++;
            double start = words[first].Start,
                end = words.Skip(first).Take(last - first).Max(w => w.End);
            var ranked = timeline.Coverage(start, end).OrderByDescending(s => s.Value).ToArray();
            if (
                last - first >= 3
                && end - start >= 0.5
                && ranked.Length > 0
                && ranked[0].Value >= options.SentenceSpeakerCoverage
                && ranked[0].Value - (ranked.Length > 1 ? ranked[1].Value : 0)
                    >= options.SentenceSpeakerMargin
                && timeline.HasContinuousTurn(ranked[0].Key, start, end)
            )
            {
                string target = ranked[0].Key;
                bool supported = true;
                for (int i = first; i < last; i++)
                {
                    var word = words[i];
                    var local = timeline.Coverage(word.Start, word.End);
                    double candidate = local.GetValueOrDefault(target);
                    // Preserve a genuine, exclusively supported competing voice, even for one word.
                    if (
                        !Unknown(word.Speaker)
                        && word.Speaker != target
                        && local.GetValueOrDefault(word.Speaker!) >= 0.6
                        && candidate < 0.25
                    )
                    {
                        supported = false;
                        break;
                    }
                    if (
                        candidate < 0.45
                        && !timeline.CloseTo(
                            target,
                            word.Start,
                            word.End,
                            options.SentenceEdgeToleranceSeconds
                        )
                    )
                    {
                        supported = false;
                        break;
                    }
                }
                if (supported)
                {
                    bool changed = false;
                    for (int i = first; i < last; i++)
                    {
                        var word = words[i];
                        // Standalone responses and interjections retain their existing voice.
                        if (
                            i > first
                            && !Unknown(word.Speaker)
                            && word.Speaker != target
                            && Interjections.Contains(Key(word.Word))
                        )
                            continue;
                        if (!Same(word.Speaker, target))
                        {
                            SetSpeaker(word, target, "sentence_rttm_continuity", report);
                            changed = true;
                        }
                    }
                    if (changed)
                        report.SentenceSpansRepaired++;
                }
            }
            first = last;
        }
    }

    private static void RepairBoundaryFragments(
        List<WordResult> words,
        SpeakerTimeline timeline,
        TranscriptPostProcessingOptions options,
        TranscriptPostProcessingReport report,
        CancellationToken ct
    )
    {
        var functionWords = new HashSet<string>(
            "i i'm my we we're he he's she she's it it's the a an to and but so of that this then here there though now too do does did can could would should will have has".Split(
                ' '
            )
        );
        var labels = words.Select(w => w.Speaker).ToArray();
        for (int first = 0; first < words.Count; first++)
        {
            ct.ThrowIfCancellationRequested();
            int end = first + 1;
            while (
                end < words.Count
                && Same(labels[end], labels[first])
                && !EndsSentence(words[end - 1])
                && words[end].Start - words[end - 1].End <= 0.65
            )
                end++;
            if (
                end - first > 2
                || words[end - 1].End - words[first].Start > 0.65
                || words.Skip(first).Take(end - first).Any(w => IsReadabilityMarker(w)
                    || w.SpeakerSource == "repetition_placeholder" || Interjections.Contains(Key(w.Word)))
            )
            {
                first = end - 1;
                continue;
            }
            string? target = null;
            bool prefix =
                (
                    first == 0
                    || EndsSentence(words[first - 1])
                    || words[first].Start - words[first - 1].End > 0.65
                )
                && end + 2 < words.Count
                && !EndsSentence(words[end - 1])
                && Close(words[end - 1], words[end], 0.20)
                && !Unknown(labels[end])
                && Same(labels[end], labels[end + 1])
                && Same(labels[end], labels[end + 2]);
            prefix &= words.Skip(first).Take(end - first).All(w => functionWords.Contains(Key(w.Word)));
            bool suffix =
                first >= 3
                && !EndsSentence(words[first - 1])
                && EndsSentence(words[end - 1])
                && Close(words[first - 1], words[first], 0.20)
                && !Unknown(labels[first - 1])
                && Same(labels[first - 1], labels[first - 2])
                && Same(labels[first - 1], labels[first - 3]);
            // A tiny final content word ("mid?", "it.") is also a boundary fragment.
            suffix &= words.Skip(first).Take(end - first).All(w => functionWords.Contains(Key(w.Word)))
                || end - first == 1 && words[first].End - words[first].Start <= 0.30;
            if (prefix)
                target = labels[end];
            else if (suffix)
                target = labels[first - 1];
            if (
                target is not null
                && !Same(target, labels[first])
                && timeline.HasStableTurn(
                    target,
                    prefix ? words[end].Start : words[first - 1].Start,
                    prefix ? words[end + 2].End : words[first - 1].End
                )
            )
            {
                bool supported = true;
                for (int j = first; j < end; j++)
                {
                    var coverage = timeline.Coverage(words[j].Start, words[j].End);
                    if (
                        (!Unknown(labels[j]) && coverage.GetValueOrDefault(labels[j]!) >= 0.75
                            && coverage.GetValueOrDefault(target) < 0.1
                            && ReliableTiming(words[j]) && words[j].End - words[j].Start >= 0.24)
                        || (
                            coverage.GetValueOrDefault(target) < 0.60
                            && timeline.DistanceTo(target, words[j].Start, words[j].End)
                                > Math.Max(0.45, options.SentenceEdgeToleranceSeconds)
                        )
                    )
                        supported = false;
                }
                if (supported)
                {
                    for (int j = first; j < end; j++)
                        SetSpeaker(words[j], target, "rttm_boundary_fragment", report);
                    report.BoundaryFragmentsRepaired++;
                }
            }
            first = end - 1;
        }
    }

    private static void SetSpeaker(
        WordResult word,
        string speaker,
        string reason,
        TranscriptPostProcessingReport report
    )
    {
        if (IsReadabilityMarker(word) || Same(word.Speaker, speaker))
            return;
        if (Unknown(word.Speaker))
            report.SpeakerWordsInferred++;
        else
            report.SpeakerWordsRelabelled++;
        report.Edits.Add(
            new TranscriptEdit(
                "speaker",
                word.Start,
                word.End,
                word.Speaker,
                speaker,
                reason + ": " + word.Word.Trim()
            )
        );
        word.Speaker = speaker;
        word.SpeakerSource = reason;
    }

    private static void CorrectTerms(
        List<WordResult> words,
        TranscriptVocabulary vocabulary,
        TranscriptPostProcessingOptions options,
        TranscriptPostProcessingReport report,
        CancellationToken cancellationToken
    )
    {
        // Disabled normalization for one entity does not make a shared alias identify another entity.
        var aliasOwners = vocabulary.Match is null
            ? new Dictionary<string, string[]>(StringComparer.Ordinal)
            : LeagueAliasPostProcessing.AliasOwners(vocabulary.Match);
        var aliases = vocabulary
            .Rules.Where(r => r.Enabled && (!r.FromAliasCatalog || options.NormalizeLeagueAliases))
            .SelectMany(rule =>
                rule.Aliases.Select(alias => (Rule: rule, Alias: alias, Contextual: false))
                    .Concat(
                        rule.ContextualAliases.Select(alias =>
                            (Rule: rule, Alias: alias, Contextual: true)
                        )
                    )
            )
            .Select(a =>
                (
                    a.Rule,
                    a.Alias,
                    a.Contextual,
                    Keys: Regex.Split(a.Alias.Trim(), @"\s+").Select(Key).ToArray()
                )
            )
            .Where(a => a.Keys.Length > 0 && a.Keys.All(k => k.Length > 0))
            .GroupBy(a => string.Join(' ', a.Keys), StringComparer.Ordinal)
            .Select(g =>
                (
                    g.First().Rule,
                    g.First().Alias,
                    Contextual: g.All(a => a.Contextual),
                    g.First().Keys,
                    Targets: g.Select(a => a.Rule.Canonical)
                        .Concat(aliasOwners.GetValueOrDefault(g.Key, []))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                )
            )
            .OrderByDescending(a => a.Keys.Length)
            .ToArray();
        var canonical = vocabulary
            .Rules.Select(r => r.Canonical)
            .Concat(vocabulary.Match?.Champions.Select(e => e.Name) ?? [])
            .Concat(vocabulary.Match?.Abilities.Select(e => e.Name) ?? [])
            .Concat(vocabulary.Match?.Items.Select(e => e.Name) ?? [])
            .Concat(vocabulary.Match?.SummonerSpells.Select(e => e.Name) ?? [])
            .Select(t => Regex.Split(t.Trim(), @"\s+").Select(Key).ToArray())
            .Where(a => a.Length > 0)
            .OrderByDescending(a => a.Length)
            .ToArray();
        for (int i = 0; i < words.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int protectedLength = canonical
                .Where(c => CanonicalAt(words, i, c))
                .Select(c => c.Length)
                .FirstOrDefault();
            if (protectedLength > 0)
            {
                i += protectedLength - 1;
                continue;
            }
            foreach (var alias in aliases)
            {
                int n = alias.Keys.Length;
                if (i + n > words.Count)
                    continue;
                bool possessive = false,
                    match = true,
                    sameSpeaker = true;
                for (int j = 0; j < n; j++)
                {
                    string key = Key(words[i + j].Word);
                    if (
                        j == n - 1
                        && key.EndsWith("'s", StringComparison.Ordinal)
                        && !alias.Keys[j].EndsWith("'s", StringComparison.Ordinal)
                    )
                    {
                        key = key[..^2];
                        possessive = true;
                    }
                    if (
                        key != alias.Keys[j]
                        || (j > 0 && !PhraseLink(words[i + j - 1], words[i + j]))
                    )
                    {
                        match = false;
                        break;
                    }
                    sameSpeaker &= Same(words[i].Speaker, words[i + j].Speaker);
                }
                if (!match)
                    continue;
                if (!GuardContextualTypo(words, i, alias.Alias, alias.Rule.Canonical, vocabulary))
                    continue;
                if (alias.Targets.Length > 1)
                {
                    report.Review.Add(
                        new TranscriptReview(
                            "ambiguous_league_alias",
                            words[i].Start,
                            words[i + n - 1].End,
                            Join(words.GetRange(i, n)),
                            "Alias could mean "
                                + string.Join(" or ", alias.Targets)
                                + ". Retained for review."
                        )
                    );
                    break;
                }
                if (
                    alias.Rule.CatalogKind == "ability"
                    && !LeagueAliasPostProcessing.HasNounContext(words, i, n)
                )
                    break; // Preserve verbal uses such as "I can bubble" and "we should hook".
                if (!sameSpeaker)
                {
                    report.Review.Add(
                        new TranscriptReview(
                            "possible_league_term",
                            words[i].Start,
                            words[i + n - 1].End,
                            Join(words.GetRange(i, n)),
                            $"Possible {alias.Rule.Canonical}; phrase crosses speaker labels. Retained for review."
                        )
                    );
                    break;
                }
                bool known = vocabulary.ActiveTerms.Contains(
                    Key(alias.Rule.ContextTerm ?? alias.Rule.Canonical)
                );
                bool cue =
                    !alias.Contextual
                    || vocabulary.HasGameCue(words, i, n)
                    || Key(alias.Alias) is "grump" or "tier";
                if ((!known && alias.Rule.RequireEvidence) || !cue)
                {
                    report.Review.Add(
                        new TranscriptReview(
                            "possible_league_term",
                            words[i].Start,
                            words[i + n - 1].End,
                            Join(words.GetRange(i, n)),
                            $"Possible {alias.Rule.Canonical}; insufficient game context for an automatic replacement."
                        )
                    );
                    break;
                }
                var old = words.GetRange(i, n);
                string replacement = alias.Rule.Canonical + (possessive ? "'s" : string.Empty);
                string first = old[0].Word.Trim();
                string last = old[^1].Word.Trim();
                string leading = Regex.Match(first, @"^[^\p{L}\p{Nd}]*").Value;
                string trailing = Regex.Match(last, @"[^\p{L}\p{Nd}]*$").Value;
                string[] parts = replacement.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                List<WordResult> updated;
                if (parts.Length == n)
                {
                    // Keep each original interval when the phrase still has the same number of words.
                    updated = old.Select(CopyWord).ToList();
                    for (int j = 0; j < n; j++)
                    {
                        string token = old[j].Word.Trim();
                        updated[j].Word =
                            " "
                            + Regex.Match(token, @"^[^\p{L}\p{Nd}]*").Value
                            + parts[j]
                            + Regex.Match(token, @"[^\p{L}\p{Nd}]*$").Value;
                    }
                }
                else
                {
                    updated = SplitSegmentText(
                        leading + replacement + trailing,
                        old[0].Start,
                        old.Max(w => w.End),
                        old[0].Speaker
                    );
                    foreach (var word in updated)
                    {
                        word.TimingSource = "term_estimate";
                        word.SpeakerSource = old[0].SpeakerSource;
                        word.OverlappingSpeech = old.Any(w => w.OverlappingSpeech);
                        word.OverlappingSpeakers = old.SelectMany(w => w.OverlappingSpeakers ?? [])
                            .Distinct(StringComparer.Ordinal).ToArray();
                    }
                }
                report.Edits.Add(
                    new TranscriptEdit(
                        "league_term",
                        old[0].Start,
                        old.Max(w => w.End),
                        Join(old),
                        Join(updated),
                        alias.Rule.FromAliasCatalog
                                ? "Unique match alias from league_aliases.json ("
                                    + alias.Rule.CatalogKind
                                    + ")."
                            : alias.Contextual
                                ? "Explicit alias with a nearby game cue and known champion/item."
                            : "Explicit vocabulary alias."
                    )
                );
                words.RemoveRange(i, n);
                words.InsertRange(i, updated);
                report.LeagueTermCorrections++;
                i += updated.Count - 1;
                break;
            }
        }
    }

    private static void AddReviews(
        List<WordResult> words,
        TranscriptVocabulary vocabulary,
        TranscriptPostProcessingOptions options,
        TranscriptPostProcessingReport report,
        CancellationToken cancellationToken
    )
    {
        for (int i = 0; i < words.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReadabilityMarker(words[i]))
                continue;
            if (Unknown(words[i].Speaker))
            {
                int end = i + 1;
                while (
                    end < words.Count
                    && !IsReadabilityMarker(words[end])
                    && Unknown(words[end].Speaker)
                    && Close(words[end - 1], words[end], 0.65)
                )
                    end++;
                report.Review.Add(
                    new TranscriptReview(
                        "unknown_speaker",
                        words[i].Start,
                        words[end - 1].End,
                        Join(words.GetRange(i, end - i)),
                        "Insufficient timing evidence to assign a speaker."
                    )
                );
                i = end - 1;
                continue;
            }
            if (words[i].OverlappingSpeech && (i == 0 || !words[i - 1].OverlappingSpeech))
                report.Review.Add(
                    new TranscriptReview(
                        "overlapping_speech",
                        words[i].Start,
                        words[i].End,
                        words[i].Word.Trim(),
                        "RTTM contains simultaneous speakers. A mixed-audio word cannot be reliably attributed by text alone."
                    )
                );
            if (
                i > 0
                && !Unknown(words[i - 1].Speaker)
                && !Same(words[i - 1].Speaker, words[i].Speaker)
                && Close(words[i - 1], words[i])
                && !EndsSentence(words[i - 1])
                && !Interjections.Contains(Key(words[i].Word))
            )
                report.Review.Add(
                    new TranscriptReview(
                        "mid_sentence_speaker_change",
                        words[i - 1].Start,
                        words[i].End,
                        Join(words.Skip(Math.Max(0, i - 3)).Take(7)),
                        "Could be a diarization error, overlap, or a real interruption; retained for review."
                    )
                );
            if (!options.SuggestSimilarTerms || words[i].Probability is not double p || p >= 0.65)
                continue;
            string key = Key(words[i].Word);
            if (key.Length < 5 || vocabulary.ActiveTerms.Contains(key))
                continue;
            var candidates = vocabulary
                .SuggestionTerms.Where(t =>
                    Key(t).Length >= 5 && !t.Contains(' ') && OneEditApart(key, Key(t))
                )
                .Take(3)
                .ToArray();
            if (candidates.Length > 0)
                report.Review.Add(
                    new TranscriptReview(
                        "possible_league_term",
                        words[i].Start,
                        words[i].End,
                        words[i].Word.Trim(),
                        "Low ASR confidence; possible: "
                            + string.Join(", ", candidates)
                            + ". No fuzzy replacement was applied."
                    )
                );
        }
    }

    private static bool OneEditApart(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1 || a == b)
            return false;
        if (a.Length > b.Length)
            (a, b) = (b, a);
        int i = 0,
            j = 0,
            errors = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j])
            {
                i++;
                j++;
            }
            else
            {
                if (++errors > 1)
                    return false;
                if (a.Length == b.Length)
                    i++;
                j++;
            }
        }
        return errors + (j < b.Length ? 1 : 0) <= 1;
    }

    private sealed class SpeakerTimeline
    {
        internal bool HasTurns => turns.Length > 0;
        private readonly TranscriptSpeakerTurn[] turns;
        private readonly double[] maxEnd;

        internal SpeakerTimeline(IReadOnlyList<TranscriptSpeakerTurn> source)
        {
            turns = source
                .Where(t =>
                    !Unknown(t.Speaker)
                    && double.IsFinite(t.Start)
                    && double.IsFinite(t.End)
                    && t.Start >= 0
                    && t.End > t.Start
                )
                .OrderBy(t => t.Start)
                .ToArray();
            maxEnd = new double[turns.Length];
            for (int i = 0; i < turns.Length; i++)
                maxEnd[i] = Math.Max(i == 0 ? 0 : maxEnd[i - 1], turns[i].End);
        }

        private IEnumerable<TranscriptSpeakerTurn> Window(double start, double end)
        {
            int low = 0,
                high = turns.Length;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (maxEnd[mid] <= start)
                    low = mid + 1;
                else
                    high = mid;
            }
            for (int i = low; i < turns.Length && turns[i].Start < end; i++)
                if (turns[i].End > start)
                    yield return turns[i];
        }

        internal (string? Best, bool Overlap) Evidence(double start, double end)
        {
            if (end - start < 0.01)
            {
                start = Math.Max(0, start - 0.025);
                end += 0.025;
            }
            var clipped = Window(start, end)
                .Select(t =>
                    (Start: Math.Max(start, t.Start), End: Math.Min(end, t.End), t.Speaker)
                )
                .ToArray();
            var scores = clipped
                .GroupBy(t => t.Speaker)
                .Select(g => (Speaker: g.Key, Coverage: Union(g.Select(t => (t.Start, t.End)))))
                .OrderByDescending(s => s.Coverage)
                .ToArray();
            double union = Union(clipped.Select(t => (t.Start, t.End)));
            bool overlap = scores.Sum(s => s.Coverage) - union > 0.02;
            return (
                scores.Length == 0
                || (scores.Length > 1 && Math.Abs(scores[0].Coverage - scores[1].Coverage) < 0.01)
                    ? null
                    : scores[0].Speaker,
                overlap
            );
        }

        internal string? Nearest(double start, double end, double limit)
        {
            var near = Window(Math.Max(0, start - limit - 0.001), end + limit + 0.001)
                .Select(t => (t.Speaker, Gap: Math.Max(0, Math.Max(t.Start - end, start - t.End))))
                .GroupBy(t => t.Speaker)
                .Select(g => (Speaker: g.Key, Gap: g.Min(t => t.Gap)))
                .Where(t => t.Gap <= limit)
                .OrderBy(t => t.Gap)
                .ToArray();
            if (near.Length == 0 || (near.Length > 1 && near[1].Gap - near[0].Gap < 0.08))
                return null;
            return near[0].Speaker;
        }

        internal bool HasStableTurn(string speaker, double start, double end) =>
            Window(start, Math.Max(end, start + 0.01))
                .Any(t => t.Speaker == speaker && t.End - t.Start >= 0.5);

        internal Dictionary<string, double> Coverage(double start, double end)
        {
            if (end - start < 0.01)
            {
                start = Math.Max(0, start - 0.025);
                end += 0.025;
            }
            return Window(start, end)
                .GroupBy(t => t.Speaker)
                .ToDictionary(
                    g => g.Key,
                    g =>
                        Union(g.Select(t => (Math.Max(start, t.Start), Math.Min(end, t.End))))
                        / (end - start)
                );
        }

        internal bool HasContinuousTurn(string speaker, double start, double end) =>
            Window(start, end)
                .Any(t =>
                    t.Speaker == speaker
                    && Math.Min(end, t.End) - Math.Max(start, t.Start) >= (end - start) * 0.70
                );

        internal bool CloseTo(string speaker, double start, double end, double limit) =>
            end - start <= 0.8
            && Window(Math.Max(0, start - limit - 0.001), end + limit + 0.001)
                .Any(t =>
                    t.Speaker == speaker
                    && Math.Max(0, Math.Max(t.Start - end, start - t.End)) <= limit
                );

        internal double DistanceTo(string speaker, double start, double end) =>
            Window(Math.Max(0, start - 2.001), end + 2.001)
                .Where(t => t.Speaker == speaker)
                .Select(t => Math.Max(0, Math.Max(t.Start - end, start - t.End)))
                .DefaultIfEmpty(double.PositiveInfinity).Min();

        private static double Union(IEnumerable<(double Start, double End)> spans)
        {
            double sum = 0,
                start = -1,
                end = -1;
            foreach (var span in spans.OrderBy(s => s.Start))
            {
                if (start < 0)
                {
                    start = span.Start;
                    end = span.End;
                }
                else if (span.Start <= end)
                    end = Math.Max(end, span.End);
                else
                {
                    sum += end - start;
                    start = span.Start;
                    end = span.End;
                }
            }
            return sum + (start < 0 ? 0 : end - start);
        }
    }
}

public sealed class TranscriptCorrectionRule
{
    [JsonPropertyName("canonical")]
    public string Canonical { get; init; } = string.Empty;

    [JsonPropertyName("aliases")]
    public string[] Aliases { get; init; } = Array.Empty<string>();

    [JsonPropertyName("contextual_aliases")]
    public string[] ContextualAliases { get; init; } = Array.Empty<string>();

    [JsonPropertyName("spoken_forms")]
    public string[] SpokenForms { get; init; } = Array.Empty<string>();

    [JsonPropertyName("context_term")]
    public string? ContextTerm { get; init; }

    [JsonPropertyName("require_evidence")]
    public bool RequireEvidence { get; init; } = true;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
    internal bool FromAliasCatalog { get; init; }
    internal string? CatalogKind { get; init; }
}

public sealed class TranscriptVocabulary
{
    public IReadOnlyList<TranscriptCorrectionRule> Rules { get; }
    internal HashSet<string> ActiveTerms { get; }
    internal string[] SuggestionTerms { get; }
    private readonly string[] gameTerms;
    internal LeagueMatchVocabulary? Match { get; private init; }
    private static readonly HashSet<string> GameCues = new(StringComparer.Ordinal)
    {
        "missing",
        "picked",
        "pick",
        "playing",
        "champion",
        "champ",
        "lane",
        "laning",
        "topside",
        "botside",
        "bot",
        "mid",
        "jungle",
        "flash",
        "ignite",
        "ult",
        "ulting",
        "damage",
        "killed",
        "kill",
        "gank",
        "invade",
        "invading",
        "delaying",
        "wave",
        "raptors",
        "camps",
        "item",
        "items",
        "buy",
        "bought",
        "build",
        "building",
        "sums",
        "summs",
    };

    public TranscriptVocabulary(
        IEnumerable<TranscriptCorrectionRule> rules,
        IEnumerable<string>? gameTerms = null
    )
    {
        Rules = rules.ToArray();
        if (
            Rules.Any(r =>
                string.IsNullOrWhiteSpace(r.Canonical)
                || r.Aliases is null
                || r.ContextualAliases is null
                || r.SpokenForms is null
            )
        )
            throw new InvalidDataException(
                "Every correction rule needs a canonical name and non-null alias arrays."
            );
        this.gameTerms = (gameTerms ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ActiveTerms = this
            .gameTerms.Select(TranscriptPostProcessor.Key)
            .ToHashSet(StringComparer.Ordinal);
        SuggestionTerms = this.gameTerms;
    }

    internal TranscriptVocabulary ForTranscript(List<WordResult> words)
    {
        var result = new TranscriptVocabulary(Rules, gameTerms) { Match = Match };
        string text =
            " " + string.Join(" ", words.Select(w => w.Word.Trim())).Replace('’', '\'') + " ";
        foreach (var rule in Rules)
        {
            if (
                new[] { rule.Canonical, rule.ContextTerm }
                    .OfType<string>()
                    .Concat(rule.SpokenForms)
                    .Any(term =>
                        Regex.IsMatch(
                            text,
                            @"(?<![\p{L}\p{Nd}])" + Regex.Escape(term) + @"(?![\p{L}\p{Nd}])",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                        )
                    )
            )
                result.ActiveTerms.Add(
                    TranscriptPostProcessor.Key(rule.ContextTerm ?? rule.Canonical)
                );
        }
        return result;
    }

    internal bool HasGameCue(List<WordResult> words, int start, int count)
    {
        int first = start,
            last = start + count;
        while (
            first > Math.Max(0, start - 5)
            && !TranscriptPostProcessor.EndsSentence(words[first - 1])
        )
            first--;
        while (
            last < Math.Min(words.Count, start + count + 5)
            && !TranscriptPostProcessor.EndsSentence(words[last - 1])
        )
            last++;
        if (
            words
                .Skip(first)
                .Take(last - first)
                .Any(w => GameCues.Contains(TranscriptPostProcessor.Key(w.Word)))
        )
            return true;
        if (TranscriptPostProcessor.EndsSentence(words[start + count - 1]))
            return false;
        string previous =
            start > first ? TranscriptPostProcessor.Key(words[start - 1].Word) : string.Empty;
        string next =
            start + count < words.Count
                ? TranscriptPostProcessor.Key(words[start + count].Word)
                : string.Empty;
        // Entity location/construction patterns supplement vocabulary cues without replacing
        // ordinary uses such as "a cinch to win" or "we should sing together".
        bool location =
            words
                .Skip(Math.Max(first, start - 2))
                .Take(start - Math.Max(first, start - 2))
                .Any(w => TranscriptPostProcessor.Key(w.Word) == "where")
            && next == "is";
        bool noun =
            previous is "the" or "this" or "that"
            && next is "is" or "has" or "might" or "can" or "will";
        return location || (noun && words[start].Probability is double p && p < 0.65);
    }

    public static TranscriptVocabulary Default() =>
        new(
            new[]
            {
                Rule("Urgot", new[] { "urkat", "orgot", "ergot" }),
                Rule("Lee Sin", new[] { "leeson", "lysen" }, new[] { "leasing" }),
                Rule("Nautilus", new[] { "bautilus", "odylus" }),
                Rule("Singed", Array.Empty<string>(), new[] { "cinch", "singe", "sing", "cinder" }),
                Rule("Sivir", new[] { "syver", "siver", "syvir" }, new[] { "sylvia", "silver" }),
                new TranscriptCorrectionRule
                {
                    Canonical = "Sivir's",
                    ContextTerm = "Sivir",
                    Aliases = new[] { "sivers" },
                },
                new TranscriptCorrectionRule
                {
                    Canonical = "Cassiopeia",
                    Aliases = new[] { "casio" },
                    SpokenForms = new[] { "cassio", "cass" },
                },
                Rule("Milio", Array.Empty<string>(), new[] { "emilio", "melio", "millio" }),
                new TranscriptCorrectionRule
                {
                    Canonical = "Gromp",
                    ContextualAliases = ["grump"],
                    RequireEvidence = false,
                },
                new TranscriptCorrectionRule
                {
                    Canonical = "Tear of the Goddess",
                    ContextualAliases = ["tier"],
                    RequireEvidence = false,
                },
                new TranscriptCorrectionRule
                {
                    Canonical = "Rylai's Crystal Scepter",
                    ContextualAliases = ["rhylice"],
                    RequireEvidence = false,
                },
                new TranscriptCorrectionRule
                {
                    Canonical = "Death's Dance",
                    Aliases = new[] { "destin's dance", "deaths dance" },
                    RequireEvidence = false,
                },
                new TranscriptCorrectionRule
                {
                    Canonical = "Shadowflame",
                    Aliases = new[] { "shade of fame", "shadow flame", "shadow frame" },
                    RequireEvidence = false,
                },
            }.Concat(
                LeagueSpeechVocabulary
                    .Misspellings.Where(p => p.Key != "Milio")
                    .Select(p => new TranscriptCorrectionRule
                    {
                        Canonical = p.Key,
                        Aliases = p.Value,
                        SpokenForms = LeagueSpeechVocabulary.SpokenForms(p.Key).ToArray(),
                    })
            )
        );

    private static TranscriptCorrectionRule Rule(
        string canonical,
        string[] aliases,
        string[]? contextual = null
    ) =>
        new()
        {
            Canonical = canonical,
            Aliases = aliases,
            ContextualAliases = contextual ?? Array.Empty<string>(),
        };

    public static async Task<TranscriptVocabulary> LoadAsync(
        string audioPath,
        TranscriptPostProcessingOptions options,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        var match = await LeagueMatchVocabulary
            .LoadAsync(audioPath, options, log, cancellationToken)
            .ConfigureAwait(false);
        var rules = Default().Rules.Concat(LeagueAliasPostProcessing.BuildRules(match)).ToList();
        string config = options.CorrectionsPath is null
            ? Path.Combine(AppContext.BaseDirectory, "transcript_corrections.json")
            : Path.GetFullPath(options.CorrectionsPath, AppContext.BaseDirectory);
        if (options.Enabled && File.Exists(config))
        {
            using var json = JsonDocument.Parse(
                await File.ReadAllTextAsync(config, cancellationToken).ConfigureAwait(false)
            );
            if (
                !json.RootElement.TryGetProperty("terms", out var terms)
                || terms.ValueKind != JsonValueKind.Array
            )
                throw new InvalidDataException(
                    "The transcript corrections JSON must contain a terms array."
                );
            foreach (var term in terms.EnumerateArray())
            {
                var rule =
                    term.Deserialize<TranscriptCorrectionRule>()
                    ?? throw new JsonException("Empty correction rule.");
                // Explicit correction entries override both built-in and catalog-derived rules for this entity.
                rules.RemoveAll(existing =>
                    existing.Canonical.Equals(rule.Canonical, StringComparison.OrdinalIgnoreCase)
                );
                rules.Add(rule);
            }
        }
        else if (options.Enabled && options.CorrectionsPath is not null)
            throw new FileNotFoundException(
                "The configured transcript corrections file was not found.",
                config
            );

        return new TranscriptVocabulary(rules, match.Terms) { Match = match };
    }
}

#endregion

#region Speaker continuity and display grouping

public static partial class TranscriptPostProcessor
{
    public static IReadOnlyList<TranscriptDisplayUtterance> GroupForDisplay(
        IEnumerable<WordResult> source
    )
    {
        var result = new List<TranscriptDisplayUtterance>();
        var span = new List<WordResult>();
        void Flush()
        {
            if (span.Count == 0)
                return;
            bool overlapping = span[0].OverlappingSpeech;
            string[] speakers = (overlapping
                ? span.SelectMany(w => TranscriptOverlap.Speakers(w))
                : span.Select(w => w.Speaker))
                .Where(s => !Unknown(s))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            string? speaker =
                !overlapping && speakers.Length == 1 && span.All(w => !Unknown(w.Speaker)) ? speakers[0] : null;
            result.Add(
                new TranscriptDisplayUtterance(
                    span[0].Start,
                    span.Max(w => w.End),
                    speaker,
                    speakers,
                    string.Join(" ", span.Select(w => w.Word.Trim())),
                    overlapping
                )
            );
            span.Clear();
        }
        foreach (
            var word in source
                .Where(w =>
                    w is not null
                    && !string.IsNullOrWhiteSpace(w.Word)
                    && double.IsFinite(w.Start)
                    && double.IsFinite(w.End)
                    && w.Start >= 0
                    && w.End >= w.Start
                )
                .OrderBy(w => w.Start)
        )
        {
            if (IsReadabilityMarker(word))
            {
                Flush();
                span.Add(word);
                Flush();
                continue;
            }
            // Stable ordering preserves zero-duration words with identical start times.
            if (
                span.Count > 0
                && (
                    TranscriptOverlap.DisplayKey(span[0]) != TranscriptOverlap.DisplayKey(word)
                    || word.Start - span[^1].End > (SyntaxJoin(span[^1], word)
                        && (!ReliableTiming(span[^1]) || !ReliableTiming(word)) ? 1.8 : 1.25)
                    || word.Start - span[0].Start > 14
                    || span.Sum(w => w.Word.Length + 1) > 340
                )
            )
                Flush();
            span.Add(word);
            if (EndsSentence(word))
                Flush();
        }
        Flush();
        return result;
    }

    private static void RepairShortZeroTimingPrefixes(
        List<WordResult> words,
        SpeakerTimeline timeline,
        TranscriptPostProcessingReport report
    )
    {
        for (int i = 0; i < words.Count; i++)
        {
            if (
                words[i].End > words[i].Start
                || !Unknown(words[i].Speaker)
                || Key(words[i].Word) is not ("i" or "i'm" or "my" or "we")
            )
                continue;
            int end = i + 1;
            while (
                end < words.Count
                && words[end].Start == words[i].Start
                && words[end].End == words[i].Start
                && Unknown(words[end].Speaker)
            )
                end++;
            if (
                end - i > 4
                || end == words.Count
                || EndsSentence(words[end - 1])
                || Unknown(words[end].Speaker)
                || words[end].Start - words[i].End > 0.15
                || words[end].SpeakerSource == "repetition_placeholder"
            )
                continue;
            string target = words[end].Speaker!;
            if (
                !timeline.HasStableTurn(target, words[end].Start, words[end].End)
                || !timeline.CloseTo(target, words[i].Start, words[i].End, 0.55)
            )
                continue;
            // This is explicitly textual continuity, not an invented audio alignment.
            for (int j = i; j < end; j++)
                SetSpeaker(words[j], target, "zero_timing_fragment_continuity", report);
            i = end - 1;
        }
    }

    // Deterministic sequence decoding: a switch must earn acoustic support rather
    // than winning a separate overlap contest on each word. No identity/role is invented.
    private static void RepairUtteranceContinuity(
        List<WordResult> words,
        SpeakerTimeline timeline,
        TranscriptPostProcessingOptions options,
        TranscriptPostProcessingReport report,
        CancellationToken ct
    )
    {
        if (!timeline.HasTurns)
            return;
        var frozen = words.Select(w => w.Speaker).ToArray();
        for (int first = 0; first < words.Count; )
        {
            ct.ThrowIfCancellationRequested();
            int last = first + 1;
            while (
                last < words.Count
                && !EndsSentence(words[last - 1])
                && ContinuityLink(words[last - 1], words[last], options)
                && words[last].Start - words[first].Start <= 12
                && last - first < 64
            )
                last++;
            int count = last - first;
            if (
                count < 3
                || words
                    .Skip(first)
                    .Take(count)
                    .Any(w => w.SpeakerSource == "repetition_placeholder" || IsReadabilityMarker(w))
            )
            {
                first = last;
                continue;
            }
            double start = words[first].Start,
                end = words.Skip(first).Take(count).Max(w => w.End);
            var overall = timeline.Coverage(start, end);
            string[] states = frozen
                .Skip(first)
                .Take(count)
                .Where(s => !Unknown(s))
                .OfType<string>()
                .Concat(overall.Keys)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (states.Length == 0)
            {
                first = last;
                continue;
            }
            int unknown = states.Length,
                width = unknown + 1;
            var costs = new double[count, width];
            var previous = new int[count, width];
            var coverage = new Dictionary<string, double>[count];
            for (int i = 0; i < count; i++)
                coverage[i] = timeline.Coverage(words[first + i].Start, words[first + i].End);
            var anchorCounts = states.Select(target => Enumerable.Range(0, count).Count(j =>
                Same(frozen[first + j], target) && coverage[j].GetValueOrDefault(target) >= 0.35
                && words[first + j].End - words[first + j].Start > 0.005)).ToArray();
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var w = words[first + i];
                for (int s = 0; s < width; s++)
                {
                    double emission;
                    double reliability = ReliableTiming(w) ? 1.0 : 0.45;
                    if (w.End - w.Start <= 0.005)
                        reliability = 0.10;
                    if (s == unknown)
                        emission = Unknown(frozen[first + i])
                            ? (coverage[i].Count == 0 ? 0.15 : -0.5)
                            : -2;
                    else
                    {
                        double acoustic = coverage[i].GetValueOrDefault(states[s]);
                        double prior = Same(frozen[first + i], states[s]) ? 0.20 * reliability : 0;
                        emission =
                            2.4 * acoustic * reliability + prior + 0.20 * overall.GetValueOrDefault(states[s]);
                        if (acoustic == 0 && !timeline.CloseTo(states[s], w.Start, w.End, 0.35))
                            emission -= 0.35;
                        // An isolated label can retain its own word, but cannot attract
                        // neighbouring words and then fail the application-time anchor guard.
                        if (anchorCounts[s] < 2 && !Same(frozen[first + i], states[s]))
                            emission = double.NegativeInfinity;
                    }
                    if (i == 0)
                    {
                        costs[i, s] = emission;
                        previous[i, s] = -1;
                        continue;
                    }
                    double best = double.NegativeInfinity;
                    int chosen = 0;
                    for (int p = 0; p < width; p++)
                    {
                        double gap = w.Start - words[first + i - 1].End;
                        double penalty =
                            p == s ? 0 : options.SpeakerSwitchPenalty * (gap > 0.35 ? 0.3 : 1);
                        if (p != s && SyntaxJoin(words[first + i - 1], w))
                            penalty = options.SpeakerSwitchPenalty * 1.8;
                        double candidate = costs[i - 1, p] - penalty;
                        if (candidate > best)
                        {
                            best = candidate;
                            chosen = p;
                        }
                    }
                    costs[i, s] = best + emission;
                    previous[i, s] = chosen;
                }
            }
            int state = Enumerable.Range(0, width).MaxBy(s => costs[count - 1, s]);
            var path = new int[count];
            for (int i = count - 1; i >= 0; i--)
            {
                path[i] = state;
                state = previous[i, state];
            }
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                if (path[i] == unknown)
                    continue;
                var word = words[first + i];
                string target = states[path[i]];
                if (Same(word.Speaker, target) || word.SpeakerSource == "repetition_placeholder")
                    continue;
                int anchors = anchorCounts[path[i]];
                if (anchors < 2 || overall.GetValueOrDefault(target) < 0.20)
                    continue;
                double local = coverage[i].GetValueOrDefault(target);
                if (local < 0.1 && timeline.DistanceTo(target, word.Start, word.End) > options.MaxContinuityGapSeconds)
                    continue;
                if (!Unknown(frozen[first + i]) && Interjections.Contains(Key(word.Word))
                    && ReliableTiming(word) && coverage[i].GetValueOrDefault(frozen[first + i]!) >= 0.6
                    && local < 0.2)
                    continue;
                if (
                    !Unknown(frozen[first + i])
                    && ReliableTiming(word)
                    && coverage[i].GetValueOrDefault(frozen[first + i]!) >= 0.75
                    && local < 0.20
                )
                {
                    // Preserve sustained, exclusively supported interruptions. A tiny
                    // end word can move; a real multiword second voice cannot be erased.
                    int a = i,
                        b = i + 1;
                    while (a > 0 && Same(frozen[first + a - 1], frozen[first + i]))
                        a--;
                    while (b < count && Same(frozen[first + b], frozen[first + i]))
                        b++;
                    if (b - a >= 3 && words[first + b - 1].End - words[first + a].Start > 0.5
                        && Enumerable.Range(a, b - a).Count(j => ReliableTiming(words[first + j])) >= 2)
                        continue;
                }
                SetSpeaker(word, target, "utterance_sequence_continuity", report);
                changed = true;
            }
            if (changed)
                report.SentenceSpansRepaired++;
            first = last;
        }
    }

    private static bool IsReadabilityMarker(WordResult word) => word.SpeakerSource == "asr_gap";

    private static WordResult UnclearSpeech(double start, double end) => new()
    {
        Start = start, End = end, Word = " [unclear speech]",
        SpeakerSource = "asr_gap", TimingSource = "review_span",
    };

    // Some hallucinations are one enormous token ("ch-ch-ch...", "hhhh...").
    // Counting repeated words misses these entirely, including during audio retries.
    private static bool HasFusedRepetition(string text)
    {
        if (text.Length < 48 || Pieces.Matches(text).Count != 1)
            return false;
        string key = Key(text);
        if (key.Length < 48)
            return false;
        for (int size = 1; size <= 8; size++)
            for (int first = 0; first < Math.Min(12, key.Length - size); first++)
            {
                int end = first + size;
                while (end < key.Length && key[end] == key[first + (end - first) % size])
                    end++;
                if (end - first >= Math.Max(48, size * 10) && end - first >= key.Length * 0.80)
                    return true;
            }
        return false;
    }

    private static void CleanFusedRepetitions(List<WordResult> words,
        TranscriptPostProcessingOptions options, TranscriptPostProcessingReport report, CancellationToken ct)
    {
        for (int i = 0; i < words.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var w = words[i];
            if (!HasFusedRepetition(w.Word))
                continue;
            report.Review.Add(new("asr_retry", w.Start, w.End, w.Word.Trim()[..Math.Min(80, w.Word.Trim().Length)],
                "Repetition inside one token. Original text is preserved in RawWords; retry this audio span."));
            if (!options.MarkUnclearSpeech)
                continue;
            report.Edits.Add(new("fused_repetition", w.Start, w.End, w.Word, "[unclear speech]",
                "A repeated-character hypothesis cannot recover the underlying speech."));
            words[i] = UnclearSpeech(w.Start, w.End);
            report.UnclearSpeechSpans++;
        }
    }

    private static void RepairCollapsedTimings(List<WordResult> words,
        TranscriptPostProcessingReport report, CancellationToken ct)
    {
        for (int first = 0; first < words.Count; first++)
        {
            ct.ThrowIfCancellationRequested();
            if (IsReadabilityMarker(words[first]) || words[first].End - words[first].Start > 0.005)
                continue;
            int end = first + 1;
            double start = words[first].Start;
            while (end < words.Count && !IsReadabilityMarker(words[end])
                && Math.Abs(words[end].Start - start) < 0.015
                && words[end].End - words[end].Start <= 0.005)
                end++;
            double right = end < words.Count ? words[end].Start : start;
            double available = right - start;
            // Never stretch words across a long silence or move a reliable neighbour.
            if (end - first <= 12 && available >= 0.04 * (end - first)
                && available <= 1.4 && (first == 0 || words[first - 1].End <= start + 0.01))
            {
                double duration = Math.Min(available, 0.18 * (end - first));
                int total = words.Skip(first).Take(end - first).Sum(w => Math.Max(1, Key(w.Word).Length));
                double used = 0;
                for (int j = first; j < end; j++)
                {
                    double next = used + Math.Max(1, Key(words[j].Word).Length);
                    words[j].Start = start + duration * used / total;
                    words[j].End = start + duration * next / total;
                    words[j].TimingSource = "continuity_estimate";
                    used = next;
                    report.CollapsedTimingWordsRepaired++;
                }
            }
            first = end - 1;
        }
    }

    private static bool ReliableTiming(WordResult word) =>
        word.End - word.Start >= 0.04 && word.End - word.Start <= 0.8
        && (word.TimingSource is null or "whisper_dtw");

    private static bool ContinuityLink(WordResult left, WordResult right,
        TranscriptPostProcessingOptions options) =>
        !IsReadabilityMarker(left) && !IsReadabilityMarker(right)
        && left.SpeakerSource != "repetition_placeholder" && right.SpeakerSource != "repetition_placeholder"
        && right.Start >= left.Start
        && right.Start - left.End <= (ReliableTiming(left) && ReliableTiming(right)
            ? 0.8 : options.MaxContinuityGapSeconds);

    private static bool SyntaxJoin(WordResult left, WordResult right)
    {
        string a = Key(left.Word), b = Key(right.Word);
        if (EndsSentence(left) || Interjections.Contains(b))
            return false;
        if (!left.Word.TrimEnd().EndsWith(',')
            && b is "my" or "your" or "our" or "their" or "his" or "her" or "the" or "a" or "an" or "out")
            return true;
        return a is "i" or "i'm" or "we" or "we're" or "you" or "he" or "she" or "it" or "they"
            or "the" or "a" or "an" or "my" or "your" or "our" or "their" or "his" or "her"
            or "to" or "of" or "for" or "with" or "on" or "at" or "from" or "into"
            or "can" or "can't" or "could" or "should" or "would" or "will" or "don't"
            or "doesn't" or "didn't" or "have" or "has" or "had" or "is" or "are" or "was" or "were" or "out";
    }

    private static bool CanonicalAt(List<WordResult> words, int start, string[] canonical)
    {
        if (start + canonical.Length > words.Count)
            return false;
        for (int j = 0; j < canonical.Length; j++)
        {
            string key = Key(words[start + j].Word);
            if (j == canonical.Length - 1 && key.EndsWith("'s", StringComparison.Ordinal))
                key = key[..^2];
            string expected = canonical[j];
            if (j == canonical.Length - 1 && expected.EndsWith("'s", StringComparison.Ordinal))
                expected = expected[..^2];
            if (
                key != expected
                || j > 0
                    && (
                        words[start + j].Start < words[start + j - 1].Start
                        || words[start + j].Start - words[start + j - 1].End > 0.65
                    )
            )
                return false;
        }
        return true;
    }

    private static bool GuardContextualTypo(
        List<WordResult> words,
        int start,
        string alias,
        string canonical,
        TranscriptVocabulary vocabulary
    )
    {
        string key = Key(alias),
            previous = start > 0 ? Key(words[start - 1].Word) : "";
        string next = start + 1 < words.Count ? Key(words[start + 1].Word) : "";
        if (key == "cinder" && canonical == "Singed" && vocabulary.ActiveTerms.Contains("syndra"))
            return false;
        if (key == "silver")
            return previous is "picked" or "pick" or "on" or "kill" or "hit" or "flash"
                || next is "q" or "w" or "e" or "r";
        if (key == "grump")
            return previous is "the" or "my" or "your" or "our" or "his";
        if (key == "tier")
            return words
                .Skip(Math.Max(0, start - 4))
                .Take(Math.Min(4, start))
                .Any(w => Key(w.Word) is "building" or "build" or "buy" or "buying" or "bought");
        return true;
    }

    private static void CoalesceRepetitionReviews(TranscriptPostProcessingReport report)
    {
        var repetitive = report
            .Review.Where(r => r.Type is "long_repetition" or "asr_retry")
            .OrderBy(r => r.Type, StringComparer.Ordinal)
            .ThenBy(r => r.Start)
            .ToArray();
        report.Review.RemoveAll(r => r.Type is "long_repetition" or "asr_retry");
        TranscriptReview? pending = null;
        foreach (var r in repetitive)
        {
            if (pending is not null && pending.Type == r.Type && r.Start <= pending.End + 0.05)
                pending = pending with { End = Math.Max(pending.End, r.End) };
            else
            {
                if (pending is not null)
                    report.Review.Add(pending);
                pending = r;
            }
        }
        if (pending is not null)
            report.Review.Add(pending);
        report.Review.Sort((a, b) => a.Start.CompareTo(b.Start));
    }

    internal static int FusedRepetitionEntries(IReadOnlyList<WordResult> words) =>
        words.Count(w => HasFusedRepetition(w.Word));

    /// <summary>Scores severe loops; a fused repeated token contributes a retry-triggering score of 24.</summary>
    internal static int SuspectRepetitionWords(IReadOnlyList<WordResult> words)
    {
        string[] keys = words
            .SelectMany(w => Pieces.Matches(w.Word).Cast<Match>().Select(m => Key(m.Value)))
            .Where(k => k.Length > 0)
            .ToArray();
        int suspect = FusedRepetitionEntries(words) * 24;
        for (int i = 0; i < keys.Length; i++)
        {
            int longest = 0;
            for (int size = 1; size <= 8 && i + size * 4 <= keys.Length; size++)
            {
                int end = i + size;
                while (
                    end + size <= keys.Length
                    && Enumerable.Range(0, size).All(k => keys[i + k] == keys[end + k])
                )
                    end += size;
                int repeats = (end - i) / size;
                if (repeats >= (size == 1 ? 12 : 6) && end - i >= 24)
                    longest = Math.Max(longest, end - i);
            }
            if (longest > 0)
            {
                suspect += longest;
                i += longest - 1;
            }
        }
        return suspect;
    }
}

public sealed record TranscriptDisplayUtterance(
    double Start,
    double End,
    string? Speaker,
    string[] Speakers,
    string Text,
    bool OverlappingSpeech = false
);

#endregion
