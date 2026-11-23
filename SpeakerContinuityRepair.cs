using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace League_Data_Recorder;

public static partial class TranscriptPostProcessor
{
    // Use frozen labels: an inferred word never becomes evidence for another.
    private static void RepairStableSpeakerSpans(List<WordResult> words, SpeakerTimeline timeline,
        TranscriptPostProcessingReport report, CancellationToken token)
    {
        string?[] labels = words.Select(w => w.Speaker).ToArray();
        bool Join(int a, int b) => a >= 0 && b < words.Count &&
            !IsReadabilityMarker(words[a]) && !IsReadabilityMarker(words[b]) &&
            !EndsSentence(words[a]) && words[b].Start - words[a].End <= 0.45 &&
            words[b].Start >= words[a].Start;
        bool Supported(string speaker, int index) => !timeline.HasTurns ||
            timeline.Coverage(words[index].Start, words[index].End).GetValueOrDefault(speaker) >= 0.25 ||
            timeline.CloseTo(speaker, words[index].Start, words[index].End, 0.35);
        bool Protected(int first, int end, string target)
        {
            for (int j = first; j < end; j++)
            {
                if (words[j].OverlappingSpeech || IsReadabilityMarker(words[j]) ||
                    words[j].SpeakerSource is "manual" or "manual_speaker" or "repetition_placeholder") return true;
                var coverage = timeline.Coverage(words[j].Start, words[j].End);
                if (coverage.Any(p => !Same(p.Key, target) && p.Value >= 0.55) &&
                    coverage.GetValueOrDefault(target) < 0.15) return true;
            }
            return false;
        }
        for (int i = 0; i < words.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!Unknown(labels[i]) || IsReadabilityMarker(words[i])) continue;
            int end = i + 1;
            while (end < words.Count && Unknown(labels[end]) && Join(end - 1, end)) end++;
            if (end - i > 10 || words[end - 1].End - words[i].Start > 2.5) { i = end - 1; continue; }
            bool left = i > 0 && !Unknown(labels[i - 1]) && Join(i - 1, i);
            bool right = end < words.Count && !Unknown(labels[end]) && Join(end - 1, end);
            string? target = left && right && Same(labels[i - 1], labels[end]) ? labels[i - 1] : null;
            if (target == null && left != right && end - i <= 4 && words[end - 1].End - words[i].Start <= 0.9)
            {
                if (right && end + 2 < words.Count && Same(labels[end], labels[end + 1]) &&
                    Same(labels[end], labels[end + 2]) && Join(end, end + 1) && Join(end + 1, end + 2)) target = labels[end];
                if (left && i >= 3 && Same(labels[i - 1], labels[i - 2]) && Same(labels[i - 1], labels[i - 3]) &&
                    Join(i - 3, i - 2) && Join(i - 2, i - 1)) target = labels[i - 1];
            }
            if (target != null && !Protected(i, end, target) &&
                Enumerable.Range(i, end - i).All(j => Supported(target, j)))
                for (int j = i; j < end; j++) SetSpeaker(words[j], target, "bounded_speaker_continuity", report);
            i = end - 1;
        }
        labels = words.Select(w => w.Speaker).ToArray();
        for (int i = 2; i + 2 < words.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (Unknown(labels[i]) || Unknown(labels[i - 1]) || Same(labels[i], labels[i - 1])) continue;
            int end = i + 1;
            while (end < words.Count && Same(labels[end], labels[i]) && Join(end - 1, end)) end++;
            if (end + 1 >= words.Count || end - i > 2 || words[end - 1].End - words[i].Start > 0.45 ||
                !Same(labels[i - 2], labels[i - 1]) || !Same(labels[i - 1], labels[end]) || !Same(labels[end], labels[end + 1]) ||
                !Enumerable.Range(i - 1, end - i + 3).All(j => Join(j - 1, j)) ||
                words.Skip(i).Take(end - i).Any(w => Interjections.Contains(Key(w.Word))) ||
                timeline.HasStableTurn(labels[i]!, words[i].Start, words[end - 1].End) ||
                Protected(i, end, labels[i - 1]!)) continue;
            for (int j = i; j < end; j++) SetSpeaker(words[j], labels[i - 1]!, "brief_speaker_island_continuity", report);
            i = end - 1;
        }
    }
}

public static partial class TranscribeAudio
{
    internal static IReadOnlyList<TranscriptSpeakerTurn> ShiftSpeakerTimeline(
        IEnumerable<TranscriptSpeakerTurn> turns, double shift, double duration) => turns
        .Select(t => new TranscriptSpeakerTurn(Math.Clamp(t.Start + shift, 0, duration),
            Math.Clamp(t.End + shift, 0, duration), t.Speaker))
        .Where(t => t.End > t.Start).ToArray();
}
