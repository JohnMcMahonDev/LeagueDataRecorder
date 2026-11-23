using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace League_Data_Recorder;

internal sealed record AudioAlignmentResult(
    double OffsetSeconds, long LeadingFrames, long SkippedFrames,
    long CopiedFrames, long TargetFrames, string OriginalAudioPath);

internal static class MediaTrimmer
{
    private static readonly SemaphoreSlim alignmentLock = new(1, 1);

    // The MP4 is the master. This method never opens it for writing and never
    // resamples or stretches speech. A positive offset adds leading silence;
    // a negative offset removes audio captured before the video timeline.
    public static async Task<AudioAlignmentResult> AlignAudioToVideoInPlaceAsync(
        string audioPath, double audioOffsetSeconds, double videoDurationSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(audioOffsetSeconds) || !double.IsFinite(videoDurationSeconds) ||
            videoDurationSeconds <= 0)
            throw new ArgumentException("Finite capture offset and positive video duration are required.");
        await alignmentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Align(audioPath, audioOffsetSeconds,
                videoDurationSeconds, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally { alignmentLock.Release(); }
    }

    private static AudioAlignmentResult Align(string audioPath, double offset,
        double duration, CancellationToken cancellationToken, string backupSuffix = ".original.wav")
    {
        string originalPath = audioPath + backupSuffix;
        string temporaryPath = audioPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // Retry from the same raw samples if final metadata saving failed after
        // a successful replacement; never apply the offset to an aligned WAV twice.
        bool hasOriginal = File.Exists(originalPath);
        string sourcePath = hasOriginal ? originalPath : audioPath;
        try
        {
            long targetFrames, leadingFrames, skippedFrames, copiedFrames;
            WaveFormat format;
            using (var input = new WaveFileReader(sourcePath))
            {
                format = input.WaveFormat;
                if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 ||
                    format.BlockAlign <= 0 || input.Length % format.BlockAlign != 0)
                    throw new InvalidDataException("Expected a sample-aligned 16-bit PCM WAV.");
                int bytesPerFrame = format.BlockAlign;
                long sourceFrames = input.Length / bytesPerFrame;
                targetFrames = checked((long)Math.Round(duration * format.SampleRate));
                if (targetFrames <= 0 || targetFrames > (uint.MaxValue - 128L) / bytesPerFrame)
                    throw new InvalidDataException("Aligned audio does not fit a standard WAV file.");
                double offsetFrames = Math.Round(offset * format.SampleRate);
                leadingFrames = (long)Math.Clamp(offsetFrames, 0, targetFrames);
                skippedFrames = (long)Math.Clamp(-offsetFrames, 0, sourceFrames);
                copiedFrames = Math.Min(sourceFrames - skippedFrames, targetFrames - leadingFrames);
                if (sourceFrames > 0 && copiedFrames <= 0)
                    throw new InvalidDataException("The audio and video capture intervals do not overlap.");

                input.Position = checked(skippedFrames * bytesPerFrame);
                using var output = new WaveFileWriter(temporaryPath, format);
                byte[] buffer = new byte[8192 * bytesPerFrame];
                WriteSilence(leadingFrames);
                long remaining = copiedFrames;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int frames = (int)Math.Min(remaining, 8192);
                    int bytes = frames * bytesPerFrame;
                    input.ReadExactly(buffer, 0, bytes);
                    output.Write(buffer, 0, bytes);
                    remaining -= frames;
                }
                WriteSilence(targetFrames - leadingFrames - copiedFrames);

                void WriteSilence(long frames)
                {
                    Array.Clear(buffer);
                    while (frames > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int blockFrames = (int)Math.Min(frames, 8192);
                        output.Write(buffer, 0, blockFrames * bytesPerFrame);
                        frames -= blockFrames;
                    }
                }
            }

            using (var check = new WaveFileReader(temporaryPath))
                if (check.WaveFormat.SampleRate != format.SampleRate ||
                    check.WaveFormat.Channels != format.Channels ||
                    check.WaveFormat.BitsPerSample != format.BitsPerSample ||
                    check.Length != checked(targetFrames * format.BlockAlign))
                    throw new InvalidDataException("Aligned WAV failed format/length validation.");

            cancellationToken.ThrowIfCancellationRequested();
            // Atomic commit, with the unmodified audio retained on the first pass.
            File.Replace(temporaryPath, audioPath, hasOriginal ? null : originalPath);
            return new(offset, leadingFrames, skippedFrames, copiedFrames, targetFrames, originalPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    internal static async Task<AudioAlignmentResult> AlignReviewAudioInPlaceAsync(
        string audioPath, double offset, double duration, CancellationToken token = default)
    {
        if (!double.IsFinite(offset) || !double.IsFinite(duration) || duration <= 0)
            throw new ArgumentException("A finite offset and positive duration are required.");
        await alignmentLock.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => Align(audioPath, offset, duration, token, ".review-original.wav"), token).ConfigureAwait(false); }
        finally { alignmentLock.Release(); }
    }
}
