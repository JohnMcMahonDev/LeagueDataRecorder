using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace League_Data_Recorder;

internal sealed class VideoReviewTiming
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> audioTimelineGates =
        new(StringComparer.OrdinalIgnoreCase);
    // Normalizing a recording and committing a WAV timing correction must not
    // observe different versions of its samples and saved timeline metadata.
    internal static SemaphoreSlim AudioTimelineGateForFile(string audioPath) =>
        audioTimelineGates.GetOrAdd(Path.GetFullPath(audioPath), _ => new SemaphoreSlim(1, 1));

    public double AudioCorrectionSeconds { get; set; }
    public double CaptionCorrectionSeconds { get; set; }
    public double AudioTimelineShiftSeconds { get; set; }
    public bool AudioAlignedToVideo { get; set; }
    internal static string PathFor(string gamePath) => gamePath + "_ReviewTiming.json";

    internal static VideoReviewTiming Read(string gamePath)
    {
        string path = PathFor(gamePath);
        // New recordings use capture timing only; legacy global manual delays
        // must not silently affect recordings after the timing UI is removed.
        if (!File.Exists(path)) return new();
        var value = JsonSerializer.Deserialize<VideoReviewTiming>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Review timing is empty.");
        if (!ValidCorrection(value.AudioCorrectionSeconds) || !ValidCorrection(value.CaptionCorrectionSeconds) ||
            !double.IsFinite(value.AudioTimelineShiftSeconds))
            throw new InvalidDataException("Review timing contains an invalid offset.");
        return value;
    }

    internal static bool ValidCorrection(double value) => double.IsFinite(value) && Math.Abs(value) <= 30;

    internal async Task SaveAsync(string gamePath)
    {
        string path = PathFor(gamePath), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    // This is a correction to an already saved recording, independent of capture
    // timestamps. Rebuild from the review-original backup using the cumulative
    // shift, so repeated applies never compound rounding or discard more speech.
    internal async Task ApplyToAudioAsync(string gamePath, double captureOffset,
        CancellationToken token = default)
    {
        var gate = AudioTimelineGateForFile(gamePath + "_Audio.wav");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { await ApplyToAudioCoreAsync(gamePath, captureOffset, token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task ApplyToAudioCoreAsync(string gamePath, double captureOffset, CancellationToken token)
    {
        string audioPath = gamePath + "_Audio.wav";
        var video = await Task.Run(() => Mp4VideoTimeline.Read(gamePath + "_Video.mp4"), token).ConfigureAwait(false);
        double shift = AudioTimelineShiftSeconds + captureOffset + AudioCorrectionSeconds;
        string rollback = audioPath + "." + Guid.NewGuid().ToString("N") + ".rollback";
        double oldShift = AudioTimelineShiftSeconds, oldCorrection = AudioCorrectionSeconds;
        bool oldAligned = AudioAlignedToVideo;
        File.Copy(audioPath, rollback);
        try
        {
            await MediaTrimmer.AlignReviewAudioInPlaceAsync(audioPath, shift, video.DurationSeconds, token).ConfigureAwait(false);
            AudioTimelineShiftSeconds = shift;
            AudioCorrectionSeconds = 0;
            AudioAlignedToVideo = true;
            try { await SaveAsync(gamePath).ConfigureAwait(false); }
            catch
            {
                File.Move(rollback, audioPath, true);
                AudioTimelineShiftSeconds = oldShift; AudioCorrectionSeconds = oldCorrection; AudioAlignedToVideo = oldAligned;
                throw;
            }
        }
        finally { if (File.Exists(rollback)) File.Delete(rollback); }
    }

    internal static double AudioShiftForFile(string audioPath)
    {
        const string suffix = "_Audio.wav";
        if (!audioPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return 0;
        return Read(audioPath[..^suffix.Length]).AudioTimelineShiftSeconds;
    }
}
