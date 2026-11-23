using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace League_Data_Recorder;

public static partial class TranscribeAudio
{
    /// <summary>Runs independently of ASR, including when a saved transcript is reused.</summary>
    public static async Task<EmotionAnalysisResult> EnsureEmotionAnalysisAsync(
        string gamePath, TranscriptionResult transcript, EmotionInferenceOptions? options = null,
        string ffmpegExecutable = "tools/ffmpeg.exe", Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        string reportPath = CommsEmotion.ReportPath(transcript.JsonPath);
        if (!options.Enabled) return new(reportPath, false, null);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            options.Validate();
            string audioPath = CommsEmotion.FindAudio(gamePath, transcript)
                ?? throw new FileNotFoundException("The original comms audio is needed to generate Comms Tone.");
            string speechFingerprint = CommsEmotion.SpeechFingerprint(transcript);
            string modelPath = Path.GetFullPath(options.ModelPath, AppContext.BaseDirectory);
            var saved = CommsEmotion.TryRead(reportPath);
            // Previously completed results remain usable if the model is temporarily absent.
            string? modelHash = File.Exists(modelPath)
                ? await CommsEmotion.ModelHashAsync(modelPath, cancellationToken).ConfigureAwait(false) : null;
            if (!options.ForceRegenerate && CommsEmotion.IsCurrent(saved, speechFingerprint, audioPath, options.WindowSeconds)
                && (modelHash is null || saved!.ModelSha256 == modelHash))
            {
                await CommsOutlook.SaveForTranscriptAsync(gamePath, transcript, saved!, cancellationToken).ConfigureAwait(false);
                log?.Invoke($"Team Outlook is current ({saved!.WindowSeconds}s context); keeping the existing emotion analysis.");
                return new(reportPath, false, null);
            }
            if (modelHash is null)
                throw new FileNotFoundException("Comms Tone model is not installed. Place the official audEERING ONNX export at "
                    + modelPath + ". See EMOTION_MODEL_SETUP.md, then select Transcribe again.");
            string ffmpegPath = RequireAsset(ffmpegExecutable, "FFmpeg executable");
            DirectoryInfo work = Directory.CreateTempSubdirectory("league-emotion-");
            try
            {
                string wavPath = Path.Combine(work.FullName, "comms.wav");
                var timelineGate = VideoReviewTiming.AudioTimelineGateForFile(audioPath);
                string audioStamp;
                double shift;
                await timelineGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    audioStamp = CommsEmotion.AudioStamp(audioPath);
                    shift = VideoReviewTiming.AudioShiftForFile(audioPath);
                    log?.Invoke("Preparing comms audio for emotion analysis…");
                    await RunProcessAsync(ffmpegPath, new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                        "-i", audioPath, "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wavPath },
                        cancellationToken).ConfigureAwait(false);
                    if (audioStamp != CommsEmotion.AudioStamp(audioPath))
                        throw new IOException("Audio changed while preparing Comms Tone. Select Transcribe again.");
                }
                finally { timelineGate.Release(); }

                // Use Task.Run for direct callers too; ONNX inference must not occupy the UI thread.
                var report = await Task.Run(() => CommsEmotion.Analyze(wavPath, transcript, modelPath,
                    modelHash, audioStamp, speechFingerprint, shift, options, log, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                if (audioStamp != CommsEmotion.AudioStamp(audioPath))
                    throw new IOException("Audio changed during emotion analysis. Select Transcribe again.");
                await CommsEmotion.SaveAsync(reportPath, report, cancellationToken).ConfigureAwait(false);
                await CommsOutlook.SaveForTranscriptAsync(gamePath, transcript, report, cancellationToken).ConfigureAwait(false);
                log?.Invoke($"Team Outlook saved ({report.Windows.Count} team windows, {report.WindowSeconds}s context).");
                return new(reportPath, true, null);
            }
            finally
            {
                try { work.Delete(recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or InvalidOperationException or OnnxRuntimeException or DllNotFoundException
            or EntryPointNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            // Never discard a successful transcript or publish a partial emotion cache.
            string message = ex is TypeInitializationException ? ex.GetBaseException().Message : ex.Message;
            log?.Invoke("Transcript saved; Comms Tone is unavailable: " + message);
            return new(reportPath, false, message);
        }
    }
}

internal static partial class CommsEmotion
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static string ReportPath(string transcriptPath) => Path.Combine(Path.GetDirectoryName(transcriptPath)!,
        Path.GetFileNameWithoutExtension(transcriptPath) + "_emotion.json");

    internal static string? FindAudio(string gamePath, TranscriptionResult transcript) =>
        File.Exists(gamePath + "_Audio.wav") ? gamePath + "_Audio.wav" :
        File.Exists(transcript.AudioFile) ? transcript.AudioFile : null;

    internal static string AudioStamp(string path)
    {
        var file = new FileInfo(path);
        return file.Length.ToString(CultureInfo.InvariantCulture) + ":" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
    }

    internal static async Task<string> ModelHashAsync(string path, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
    }

    internal static CommsEmotionReport? TryRead(string path)
    {
        try { return JsonSerializer.Deserialize<CommsEmotionReport>(File.ReadAllText(path), JsonOptions); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }

    internal static bool Valid(CommsEmotionReport? report) => report is { Complete: true, Version: CommsEmotionReport.CurrentVersion }
        && report.WindowSeconds is >= 5 and <= 60
        && !string.IsNullOrWhiteSpace(report.ModelSha256) && double.IsFinite(report.AudioTimelineShiftSeconds)
        && double.IsFinite(report.DurationSeconds) && report.DurationSeconds >= 0 && report.Windows is not null
        && report.Windows.All(w => w is not null && double.IsFinite(w.Start) && double.IsFinite(w.End)
            && w.Start >= 0 && w.End > w.Start && w.End <= report.DurationSeconds + .001
            && double.IsFinite(w.SpeechSeconds) && w.SpeechSeconds > 0 && w.SpeechSeconds <= Math.Min(report.WindowSeconds, report.DurationSeconds) + .001
            && float.IsFinite(w.Valence));

    internal static bool IsCurrent(CommsEmotionReport? report, string fingerprint, string audioPath, int windowSeconds) =>
        Valid(report) && report!.WindowSeconds == windowSeconds && report!.SpeechFingerprint == fingerprint && report.AudioStamp == AudioStamp(audioPath)
        && Math.Abs(report.AudioTimelineShiftSeconds - VideoReviewTiming.AudioShiftForFile(audioPath)) < .00001;

    internal static async Task SaveAsync(string path, CommsEmotionReport report, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = File.Create(staged))
                await JsonSerializer.SerializeAsync(file, report, JsonOptions, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(staged, path, overwrite: true);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    internal static CommsEmotionReport Analyze(string wavPath, TranscriptionResult transcript, string modelPath,
        string modelHash, string audioStamp, string fingerprint, double audioShift, EmotionInferenceOptions options,
        Action<string>? log, CancellationToken token)
    {
        using var wave = new EmotionPcmReader(wavPath);
        options.Validate();
        var ranges = SpeechRanges(transcript, audioShift - transcript.AudioTimelineShiftSeconds, wave.Duration);
        var plan = TeamWindows(ranges, wave.Duration, options.WindowSeconds);
        var windows = new List<EmotionWindow>();
        // Silence-only/empty transcripts are a completed empty analysis, never a neutral mood.
        if (plan.Count > 0)
        {
            log?.Invoke($"Analyzing team Outlook with {options.WindowSeconds}s rolling audio context, updated every {CommsEmotionReport.StepSeconds}s ({plan.Count} windows)…");
            log?.Invoke("Loading audEERING emotion model (ONNX Runtime, CPU)…");
            using var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = options.CpuThreads > 0 ? options.CpuThreads : Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
                InterOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            };
            using var session = new InferenceSession(modelPath, sessionOptions);
            // Official Zenodo 1.1.0 export. Its graph ALREADY includes waveform normalization.
            // Do not normalize again or feed Hugging Face/PyTorch weights to this adapter.
            if (session.InputMetadata.Count != 1 || !session.InputMetadata.TryGetValue("signal", out var input)
                || input.ElementType != typeof(float) || input.Dimensions.Length != 2
                || !session.OutputMetadata.TryGetValue("logits", out var output) || output.ElementType != typeof(float))
                throw new InvalidDataException("Unexpected emotion model contract. Use the official Zenodo 1.1.0 model.onnx; see EMOTION_MODEL_SETUP.md.");
            using var runOptions = new RunOptions();
            using var cancel = token.Register(() => runOptions.Terminate = true);
            int analyzed = 0;
            foreach (var window in plan)
            {
                token.ThrowIfCancellationRequested();
                // Feed the entire rolling context, across turns, unknown voices and overlap.
                // The output span is the 5-second graph interval, not the context length.
                float[] signal = wave.Read(window.ContextStart, window.ContextEnd);
                if (signal.Length < 16000 || signal.Sum(s => (double)s * s) / signal.Length < .000001) continue;
                using var result = Run(session, signal, runOptions, token);
                float[] scores = result.Single(r => r.Name == "logits").AsTensor<float>().ToArray();
                if (scores.Length != 3 || scores.Any(s => !float.IsFinite(s)))
                    throw new InvalidDataException("Emotion model did not return three finite regression scores.");
                // The official graph emits A/D/V; persist only team valence.
                windows.Add(new(window.Start, window.End, window.SpeechSeconds, scores[2]));
                if (++analyzed % 20 == 0) log?.Invoke($"Analyzing team Outlook… {analyzed}/{plan.Count} windows ({options.WindowSeconds}s context)");
            }
        }
        token.ThrowIfCancellationRequested();
        return new() { Complete = true, ModelSha256 = modelHash, AudioStamp = audioStamp,
            SpeechFingerprint = fingerprint, AudioTimelineShiftSeconds = audioShift, DurationSeconds = wave.Duration,
            WindowSeconds = options.WindowSeconds, Windows = windows };
    }

    private static IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(InferenceSession session,
        float[] signal, RunOptions options, CancellationToken token)
    {
        try
        {
            var tensor = new DenseTensor<float>(signal, new[] { 1, signal.Length });
            return session.Run(new[] { NamedOnnxValue.CreateFromTensor("signal", tensor) }, new[] { "logits" }, options);
        }
        catch (OnnxRuntimeException) { token.ThrowIfCancellationRequested(); throw; }
    }
}

// FFmpeg emits plain little-endian PCM. Read only one configured context window at a time,
// rather than holding another full match waveform in memory alongside Whisper.
internal sealed class EmotionPcmReader : IDisposable
{
    private readonly FileStream stream;
    private readonly long dataStart;
    internal double Duration { get; }
    internal EmotionPcmReader(string path)
    {
        stream = File.OpenRead(path);
        try
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected a PCM WAV for emotion inference.");
            reader.ReadUInt32();
            if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Invalid emotion WAV header.");
            bool formatValid = false;
            while (stream.Position + 8 <= stream.Length)
            {
                string chunk = new(reader.ReadChars(4));
                uint size = reader.ReadUInt32();
                long next = stream.Position + size + (size & 1);
                if (next > stream.Length) throw new InvalidDataException("Truncated emotion WAV.");
                if (chunk == "fmt ")
                {
                    if (size < 16) throw new InvalidDataException("Invalid emotion WAV format.");
                    ushort format = reader.ReadUInt16(), channels = reader.ReadUInt16();
                    uint sampleRate = reader.ReadUInt32();
                    reader.ReadUInt32(); reader.ReadUInt16();
                    formatValid = format == 1 && channels == 1 && sampleRate == 16000 && reader.ReadUInt16() == 16;
                }
                else if (chunk == "data")
                {
                    if (!formatValid || size % 2 != 0) throw new InvalidDataException("Emotion inference requires mono 16 kHz, 16-bit PCM.");
                    dataStart = stream.Position;
                    Duration = size / 32000d;
                    return;
                }
                stream.Position = next;
            }
            throw new InvalidDataException("Emotion WAV has no audio data.");
        }
        catch { stream.Dispose(); throw; }
    }

    internal float[] Read(double start, double end)
    {
        long first = (long)Math.Round(start * 16000);
        int count = checked((int)Math.Round(end * 16000) - (int)first);
        byte[] pcm = new byte[count * 2];
        stream.Position = dataStart + first * 2;
        stream.ReadExactly(pcm);
        float[] samples = new float[count];
        for (int i = 0; i < count; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2, 2)) / 32768f;
        return samples;
    }
    public void Dispose() => stream.Dispose();
}
