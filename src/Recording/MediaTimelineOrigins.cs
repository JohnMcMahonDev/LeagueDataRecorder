namespace League_Data_Recorder;

// File time zero is independent of League game time zero. In particular,
// loading-screen footage belongs to the MP4 timeline and must not be discarded
// from the voice offset merely because the WAV was trimmed by an older build.
internal static class MediaTimelineOrigins
{
    internal static double? Video(GameMediaTiming? timing) => Finite(timing?.VideoTrimmed == true
        ? timing.GameStartClockSeconds : timing?.VideoFileStartClockSeconds);

    internal static double? Audio(GameMediaTiming? timing, VideoReviewTiming? review = null)
    {
        if (review?.AudioAlignedToVideo == true) return Video(timing);
        if (timing is null) return null;
        if (timing.AlignedAudioFileStartClockSeconds is double aligned)
            return Finite(aligned);
        // Explicit video alignment was introduced with its own origin. Before
        // that, AudioTrimmed meant "trimmed to game start", even when video
        // trimming failed or was disabled. Never substitute the MP4 origin here.
        return Finite(timing.AudioTrimmed ? timing.GameStartClockSeconds
            : timing.AudioFileStartClockSeconds);
    }

    private static double? Finite(double? value) => value is double seconds && double.IsFinite(seconds)
        ? seconds : null;
}
