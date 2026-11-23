using ScreenRecorderLib;
using System.Diagnostics;

namespace League_Data_Recorder
{
    internal class RecordApplicationVideo
    {
        private Recorder? recorder;

        private TaskCompletionSource<
            (string? FilePath, string? Error)>? completion;

        private readonly SemaphoreSlim stopLock = new(1, 1);
        private readonly object stopTaskLock = new();
        private string? capturePath;

        public Task<(string? FilePath, string? Error)>? Completion
            => completion?.Task;

        private Task<(string? FilePath, string? Error)>? pendingStop;

        private bool stopTimedOut;

        private readonly object videoTimingLock = new();
        private const int CaptureFps = 30;
        private double? firstFrameReadyClockSeconds;
        private double? fileStartClockSeconds;
        private double? previousFrameClockSeconds;

        // Software H.264 avoids MF_E_NOTACCEPTING on unsupported hardware encoders.
        public bool UseHardwareEncoding { get; set; } = false;
        public double? FirstFrameReadyClockSeconds { get; private set; }
        public double? FirstFrameEndSeconds { get; private set; }
        public double? DurationSeconds { get; private set; }
        public int FrameCount { get; private set; }
        public double MaximumFrameCallbackGapSeconds { get; private set; }
        public string? TimingError { get; private set; }
        public string TimingSource => "First Recording status (before encoding), minus first MP4 presentation end";

        public double? FileStartClockSeconds
        {
            get
            {
                lock (videoTimingLock)
                {
                    return fileStartClockSeconds;
                }
            }
        }

        public void StartVideoRecording(
            IntPtr gameWindow, string outputPath)
        {
            if (gameWindow == IntPtr.Zero)
                throw new ArgumentException(
                    "A valid game window is required.",
                    nameof(gameWindow));

            ReleaseCompletedStopBeforeStart();

            if (recorder != null)
                throw new InvalidOperationException(
                    "Video recording is already active or stopping.");

            var options = new RecorderOptions
            {
                SourceOptions = new SourceOptions
                {
                    RecordingSources =
                    {
                        new WindowRecordingSource(gameWindow)
                        {
                            IsBorderRequired = false
                        }
                    }
                },

                VideoEncoderOptions = new VideoEncoderOptions
                {
                    Framerate = CaptureFps,
                    IsFixedFramerate = true,
                    Bitrate = 6_000_000,
                    Encoder = new H264VideoEncoder
                    {
                        EncoderProfile = H264Profile.Main,
                        BitrateMode = H264BitrateControlMode.CBR
                    },
                    IsHardwareEncodingEnabled = UseHardwareEncoding,
                    IsLowLatencyEnabled = false,
                    IsThrottlingDisabled = false,
                    IsMp4FastStartEnabled = true,
                    IsFragmentedMp4Enabled = false
                },

                OutputOptions = new OutputOptions
                {
                    RecorderMode = RecorderMode.Video,
                    OutputFrameSize = new ScreenSize(1280, 720),
                    Stretch = StretchMode.Uniform,
                    IsVideoFramePreviewEnabled = false
                },

                LogOptions = new LogOptions
                {
                    IsLogEnabled = false
                },

                AudioOptions = new AudioOptions
                {
                    IsAudioEnabled = false
                }
            };

            var finished = new TaskCompletionSource<
                (string? FilePath, string? Error)>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            lock (videoTimingLock)
            {
                firstFrameReadyClockSeconds = null;
                fileStartClockSeconds = null;
                previousFrameClockSeconds = null;
                FirstFrameReadyClockSeconds = null;
                FirstFrameEndSeconds = null;
                DurationSeconds = null;
                FrameCount = 0;
                MaximumFrameCallbackGapSeconds = 0;
                TimingError = null;
            }
            Recorder newRecorder = Recorder.CreateRecorder(options);

            newRecorder.OnStatusChanged += (_, e) =>
            {
                // In ScreenRecorderLib 7.0.1 this runs immediately after OnVideoFrame,
                // BEFORE ProcessTexture/RenderFrame for frame 1. That clock instant
                // corresponds to the END of the first MP4 sample, not its start.
                // Later Recording notifications (e.g. resume) must not move this anchor.
                if (e.Status != RecorderStatus.Recording) return;
                double now = MediaClock.NowSeconds;
                lock (videoTimingLock) firstFrameReadyClockSeconds ??= now;
            };

            newRecorder.OnFrameRecorded += (_, e) =>
            {
                // Diagnostics only. The native library uses elapsed presentation-clock
                // durations even with IsFixedFramerate=true. Neither frameNumber / FPS
                // nor e.Timestamp (Unix wall-clock milliseconds) is a media timestamp.
                double now = MediaClock.NowSeconds;
                lock (videoTimingLock)
                {
                    FrameCount = Math.Max(FrameCount, e.FrameNumber);
                    if (previousFrameClockSeconds is double previous)
                        MaximumFrameCallbackGapSeconds = Math.Max(MaximumFrameCallbackGapSeconds, now - previous);
                    previousFrameClockSeconds = now;
                }
            };

            newRecorder.OnRecordingComplete += (_, e) =>
                finished.TrySetResult((e.FilePath, null));

            newRecorder.OnRecordingFailed += (_, e) =>
            {
                lock (stopTaskLock)
                {
                    if (!ReferenceEquals(recorder, newRecorder) ||
                        !finished.TrySetResult((null, e.Error))) return;
                    lock (videoTimingLock) TimingError = e.Error;
                    // Native teardown may wait for this callback to return.
                    // Atomically publish failure and queue disposal off-thread;
                    // Stop must share this worker even if it finishes immediately.
                    BeginStop(newRecorder, finished);
                }
            };

            recorder = newRecorder;
            completion = finished;
            capturePath = outputPath;

            try
            {
                newRecorder.Record(outputPath);
            }
            catch (Exception ex)
            {
                finished.TrySetResult((null, ex.Message));
                lock (videoTimingLock) TimingError = ex.Message;
                BeginStop(newRecorder, finished);
                throw;
            }
        }

        private Task<(string? FilePath, string? Error)> BeginStop(Recorder activeRecorder,
            TaskCompletionSource<(string? FilePath, string? Error)> finished)
        {
            lock (stopTaskLock)
            {
                // A late failure callback must not dispose a retired recorder
                // again or attach its cleanup task to the next capture.
                if (!ReferenceEquals(recorder, activeRecorder)) return finished.Task;
                return pendingStop ??= Task.Run(async () =>
                {
                    (string? FilePath, string? Error) result;
                    try
                    {
                        if (!finished.Task.IsCompleted) activeRecorder.Stop();
                        result = await finished.Task.ConfigureAwait(false);
                    }
                    catch (Exception ex) { result = (null, ex.Message); }
                    try { activeRecorder.Dispose(); }
                    catch (Exception ex)
                    {
                        result = (null, result.Error is null ? ex.Message
                            : result.Error + " Recorder cleanup: " + ex.Message);
                    }
                    return result;
                });
            }
        }

        public async Task<string?> StopVideoRecordingAsync()
        {
            await stopLock.WaitAsync();

            try
            {
                Recorder? activeRecorder = recorder;
                var finished = completion;

                if (activeRecorder == null || finished == null)
                    return null;

                var stopTask = BeginStop(activeRecorder, finished);

                (string? FilePath, string? Error) result;

                try
                {
                    result = await stopTask.WaitAsync(
                        TimeSpan.FromSeconds(20));
                }
                catch (TimeoutException ex)
                {
                    stopTimedOut = true;
                    TimingError = "Video finalization timed out; the recorder is still stopping.";

                    throw new TimeoutException(
                        "Video finalization timed out. " +
                        "The recorder is still stopping.", ex);
                }
                catch
                {
                    // A faulted worker has finished. Don't keep stale references.
                    ClearRecorderState();
                    throw;
                }

                // The worker finished, including disposal.
                bool discardResult = stopTimedOut;

                ClearRecorderState();

                if (result.Error != null)
                {
                    TimingError = result.Error;
                    throw new InvalidOperationException(result.Error);
                }

                if (discardResult)
                {
                    // A late result must not be assigned to a later game.
                    RecordingFileCleanup.TryDelete(result.FilePath, message => Debug.WriteLine(message));

                    return null;
                }

                // Successful encoding and timing inspection are separate steps.
                // Return the completed file so the caller can save it BEFORE
                // reading optional timing metadata.
                return result.FilePath;
            }
            finally
            {
                stopLock.Release();
            }
        }

        internal async Task ReadSavedVideoTimingAsync(string videoPath)
        {
            try
            {
                Mp4VideoTimeline timeline = await Task.Run(() => Mp4VideoTimeline.Read(videoPath));
                lock (videoTimingLock)
                {
                    DurationSeconds = timeline.DurationSeconds;
                    if (firstFrameReadyClockSeconds is not double first)
                        throw new InvalidDataException("The recorder did not supply its first-frame status event.");
                    if (timeline.SampleCount != FrameCount)
                        throw new InvalidDataException($"Video timing cannot be verified: {FrameCount} captured frames, {timeline.SampleCount} MP4 samples.");
                    FirstFrameEndSeconds = timeline.FirstFrameEndSeconds;
                    fileStartClockSeconds = first - timeline.FirstFrameEndSeconds;
                    TimingError = null;
                }
            }
            catch (Exception ex)
            {
                // InvalidDataException is not an IOException. Treat all inspection
                // failures as unavailable timing, never as a failed video save.
                // The MP4 has already been saved; leave the original WAV unchanged.
                lock (videoTimingLock)
                {
                    fileStartClockSeconds = null;
                    FirstFrameEndSeconds = null;
                    TimingError = ex.Message;
                }
            }
            finally
            {
                lock (videoTimingLock) FirstFrameReadyClockSeconds = firstFrameReadyClockSeconds;
            }
        }

        private void ClearRecorderState()
        {
            lock (stopTaskLock)
            {
                recorder = null;
                completion = null;
                pendingStop = null;
                capturePath = null;
                stopTimedOut = false;
            }
        }

        private void ReleaseCompletedStopBeforeStart()
        {
            var finishedStop = pendingStop;

            if (finishedStop == null || !finishedStop.IsCompleted)
                return;

            try
            {
                var result = finishedStop.GetAwaiter().GetResult();
                // A previous failed/timed-out capture must not poison the next
                // game. Its unique temporary file is never reused.
                RecordingFileCleanup.TryDelete(result.FilePath ?? capturePath,
                    message => Debug.WriteLine(message));
            }
            finally
            {
                // The worker has finished, including its disposal attempt.
                ClearRecorderState();
            }
        }
    }
}
