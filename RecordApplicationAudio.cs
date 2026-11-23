using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// Replace the contents of your existing RecordApplicationAudio.cs with this file.
// NAudio 3.x: providers use Read(Span<T>), not the old array/offset/count API.
internal class RecordApplicationAudio
{
    private const int SampleRate = 16000;
    private const int BytesPerFrame = 2;
    private const int ChunkFrames = 320; // 20 ms of mono audio.
    private const double BufferingDelaySeconds = 0.25;

    private readonly SemaphoreSlim recordingLock = new(1, 1);
    private WasapiRecorder? discordRecorder;
    private WasapiRecorder? microphoneRecorder;
    private MMDeviceEnumerator? deviceEnumerator;
    private MMDevice? microphoneDevice;
    private WaveFileWriter? waveWriter;
    private CancellationTokenSource? pumpCancellation;
    private Task? pumpTask;

    private double fileStartClockSeconds = double.NaN;
    private double stopClockSeconds = double.NaN;
    private int captureStopped;
    private string? captureError;

    // Set before Start. Null uses the Windows default communications microphone.
    public string? MicrophoneDeviceId { get; set; }
    public float DiscordGain { get; set; } = 0.5f;
    public float MicrophoneGain { get; set; } = 0.5f;
    public bool isRecordingAudio { get; private set; }

    // These remain available after Stop for your existing media-timing code.
    public double? FileStartClockSeconds
    {
        get
        {
            double value = Volatile.Read(ref fileStartClockSeconds);
            return double.IsNaN(value) ? null : value;
        }
    }
    public string? CaptureError => Volatile.Read(ref captureError);

    private static double NowSeconds =>
        Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public async Task StartAsyncAudioRecording(
        IntPtr gameWindow, int processId, string outputPath)
    {
        await recordingLock.WaitAsync();
        try
        {
            if (isRecordingAudio) return;
            if (processId <= 0)
                throw new ArgumentOutOfRangeException(nameof(processId));
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
                throw new PlatformNotSupportedException(
                    "Process loopback requires Windows build 20348 or newer.");
            if (!float.IsFinite(DiscordGain) || !float.IsFinite(MicrophoneGain) ||
                DiscordGain < 0 || MicrophoneGain < 0)
                throw new ArgumentException("Audio gains must be finite and nonnegative.");

            captureError = null;
            stopClockSeconds = double.NaN;
            captureStopped = 0;
            fileStartClockSeconds = double.NaN;
            var pcmFormat = new WaveFormat(SampleRate, 16, 1);

            discordRecorder = await new WasapiRecorderBuilder()
                .WithProcessLoopback((uint)processId,
                    ProcessLoopbackMode.IncludeTargetProcessTree)
                .WithBufferLength(50)
                .WithFormat(pcmFormat)
                .BuildAsync();

            deviceEnumerator = new MMDeviceEnumerator();
            microphoneDevice = string.IsNullOrWhiteSpace(MicrophoneDeviceId)
                ? deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                : deviceEnumerator.GetDevice(MicrophoneDeviceId);

            microphoneRecorder = new WasapiRecorderBuilder()
                .WithDevice(microphoneDevice)
                .WithBufferLength(50)
                .WithFormat(pcmFormat)
                .Build();

            ValidateFormat(discordRecorder.WaveFormat);
            ValidateFormat(microphoneRecorder.WaveFormat);

            var discordInput = new TimestampedPcmInput("Discord", pcmFormat);
            var microphoneInput = new TimestampedPcmInput("Microphone", pcmFormat);

            // The two callbacks ONLY copy captured packets into their own queues.
            // They never touch the WAV writer.
            discordRecorder.DataAvailable += (data, flags, device, qpc) =>
                QueuePacket(discordInput, data, flags, qpc);
            microphoneRecorder.DataAvailable += (data, flags, device, qpc) =>
                QueuePacket(microphoneInput, data, flags, qpc);

            discordRecorder.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null) SaveError("Discord capture", e.Exception);
            };
            microphoneRecorder.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null) SaveError("Microphone capture", e.Exception);
            };

            // MixingSampleProvider requires a common floating-point sample format.
            var mixer = new MixingSampleProvider(
                WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1))
            {
                ReadFully = true
            };
            mixer.AddMixerInput(new VolumeSampleProvider(
                new Pcm16BitToSampleProvider(discordInput))
            { Volume = DiscordGain });
            mixer.AddMixerInput(new VolumeSampleProvider(
                new Pcm16BitToSampleProvider(microphoneInput))
            { Volume = MicrophoneGain });

            // Convert the mixed float samples back to 16-bit PCM for storage.
            var mixedPcm = new SampleToWaveProvider16(mixer);
            waveWriter = new WaveFileWriter(outputPath, mixedPcm.WaveFormat);

            Volatile.Write(ref fileStartClockSeconds, NowSeconds);
            discordInput.OriginSeconds = fileStartClockSeconds;
            microphoneInput.OriginSeconds = fileStartClockSeconds;

            microphoneRecorder.StartRecording();
            discordRecorder.StartRecording();

            pumpCancellation = new CancellationTokenSource();
            pumpTask = Task.Run(() => PumpAsync(mixedPcm, pumpCancellation.Token));
            isRecordingAudio = true;
        }
        catch
        {
            await StopCoreAsync();
            fileStartClockSeconds = double.NaN;
            throw;
        }
        finally { recordingLock.Release(); }
    }

    private void QueuePacket(TimestampedPcmInput input, ReadOnlySpan<byte> data,
        AudioClientBufferFlags flags, long qpcPosition)
    {
        try { input.AddPacket(data, flags, qpcPosition); }
        catch (Exception ex)
        {
            SaveError($"{input.Name} buffering", ex);
            throw; // NAudio stops this capture source and raises RecordingStopped.
        }
    }

    private async Task PumpAsync(IWaveProvider mixedPcm, CancellationToken token)
    {
        byte[] output = new byte[ChunkFrames * BytesPerFrame];
        long framesWritten = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                double requestedStop = Volatile.Read(ref stopClockSeconds);
                bool stopped = Volatile.Read(ref captureStopped) != 0;

                // Normally stay 250 ms behind capture so both packet queues can fill.
                double readyClock = NowSeconds - BufferingDelaySeconds;
                if (!double.IsNaN(requestedStop))
                    readyClock = stopped ? requestedStop : Math.Min(readyClock, requestedStop);

                long targetFrames = Math.Max(0, (long)Math.Floor(
                    (readyClock - fileStartClockSeconds) * SampleRate));

                while (framesWritten < targetFrames)
                {
                    token.ThrowIfCancellationRequested();
                    int frames = (int)Math.Min(ChunkFrames, targetFrames - framesWritten);
                    int bytes = frames * BytesPerFrame;
                    int read = mixedPcm.Read(output.AsSpan(0, bytes));
                    if (read != bytes)
                        throw new InvalidDataException("The mixer returned a short audio block.");

                    // This is the ONLY thread that writes to the WAV.
                    waveWriter!.Write(output.AsSpan(0, read));
                    framesWritten += read / BytesPerFrame;
                }

                if (stopped) break;
                await Task.Delay(10, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            SaveError("Audio mix writer", ex);
            throw;
        }
    }

    public async Task StopAsyncAudioRecording()
    {
        await recordingLock.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally { recordingLock.Release(); }
    }

    private async Task StopCoreAsync()
    {
        // Ceiling the writer at the stop request avoids encoding/disposal delays
        // extending the audio file. It drains to this time after capture stops.
        Volatile.Write(ref stopClockSeconds, NowSeconds);
        try
        {
            await Task.WhenAll(
                DisposeCaptureAsync(discordRecorder, "Discord stop"),
                DisposeCaptureAsync(microphoneRecorder, "Microphone stop"));
        }
        finally
        {
            Volatile.Write(ref captureStopped, 1);
            try
            {
                if (pumpTask != null)
                {
                    try { await pumpTask; }
                    catch (Exception ex) { SaveError("Audio mix completion", ex); }
                }
            }
            finally
            {
                pumpTask = null;
                discordRecorder = null;
                microphoneRecorder = null;
                pumpCancellation?.Dispose();
                pumpCancellation = null;
                try { waveWriter?.Dispose(); }
                finally
                {
                    waveWriter = null;
                    microphoneDevice?.Dispose();
                    microphoneDevice = null;
                    deviceEnumerator?.Dispose();
                    deviceEnumerator = null;
                    isRecordingAudio = false;
                }
            }
        }
    }

    private async Task DisposeCaptureAsync(WasapiRecorder? recorder, string operation)
    {
        if (recorder == null) return;
        try { await recorder.DisposeAsync(); }
        catch (Exception ex) { SaveError(operation, ex); }
    }

    private void SaveError(string operation, Exception ex)
    {
        string message = $"{operation}: {ex.Message}";
        Interlocked.CompareExchange(ref captureError, message, null);
        Debug.WriteLine(message);
    }

    private static void ValidateFormat(WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Pcm || format.SampleRate != SampleRate ||
            format.BitsPerSample != 16 || format.Channels != 1)
            throw new NotSupportedException(
                "Both capture devices must return 16 kHz mono 16-bit PCM.");
    }

    // A bounded queue of copied packets positioned on the common file timeline.
    // Missing intervals read as silence; packets from different capture threads
    // are never concatenated into the same writer.
    private sealed class TimestampedPcmInput : IWaveProvider
    {
        private readonly object sync = new();
        private readonly Queue<Packet> packets = new();
        private long readFrame;
        private long queuedFrames;
        private bool warnedAboutTiming;
        private bool warnedAboutLatePackets;
        private double observedDeliveryDelay;
        public string Name { get; }
        public WaveFormat WaveFormat { get; }
        public double OriginSeconds { get; set; }

        public TimestampedPcmInput(string name, WaveFormat format)
        {
            Name = name;
            WaveFormat = format;
        }

        public void AddPacket(ReadOnlySpan<byte> data,
            AudioClientBufferFlags flags, long qpcPosition)
        {
            if (data.Length == 0) return;
            if (data.Length % BytesPerFrame != 0)
                throw new InvalidDataException("Capture packet is not sample-aligned.");

            long frames = data.Length / BytesPerFrame;
            double arrival = NowSeconds;
            double packetSeconds = qpcPosition / 10_000_000.0;
            bool timestampValid = qpcPosition > 0 && ((int)flags & 0x4) == 0 &&
                packetSeconds <= arrival + 0.1 && packetSeconds >= arrival - 5;

            if (timestampValid)
            {
                observedDeliveryDelay = Math.Clamp(
                    arrival - packetSeconds - frames / (double)SampleRate, 0, 1);
            }
            else
            {
                // Some drivers report zero/unusable QPC positions. Keep recording,
                // but use an explicitly approximate arrival-time estimate instead.
                packetSeconds = arrival - frames / (double)SampleRate - observedDeliveryDelay;
                if (!warnedAboutTiming)
                {
                    Debug.WriteLine($"{Name}: capture timestamps unavailable; using approximate arrival timing.");
                    warnedAboutTiming = true;
                }
            }

            long startFrame = (long)Math.Round((packetSeconds - OriginSeconds) * SampleRate);
            // The span belongs to WASAPI and expires when this callback returns.
            byte[] copy = (flags & AudioClientBufferFlags.Silent) != 0
                ? new byte[data.Length] : data.ToArray();

            lock (sync)
            {
                if (startFrame + frames <= readFrame)
                {
                    if (!warnedAboutLatePackets)
                    {
                        Debug.WriteLine($"{Name}: a packet arrived after its interval was written; consider a larger buffering delay.");
                        warnedAboutLatePackets = true;
                    }
                    return;
                }
                if (queuedFrames + frames > SampleRate * 5L)
                    throw new IOException("Audio mix is more than five seconds behind capture.");
                packets.Enqueue(new Packet(startFrame, copy));
                queuedFrames += frames;
            }
        }

        public int Read(Span<byte> destination)
        {
            if (destination.Length % BytesPerFrame != 0)
                throw new ArgumentException("Read size must contain whole samples.");
            destination.Clear();
            long count = destination.Length / BytesPerFrame;
            lock (sync)
            {
                long endFrame = readFrame + count;
                while (packets.Count > 0)
                {
                    Packet packet = packets.Peek();
                    long packetFrames = packet.Data.Length / BytesPerFrame;
                    long packetEnd = packet.StartFrame + packetFrames;
                    if (packetEnd <= readFrame)
                    {
                        packets.Dequeue();
                        queuedFrames -= packetFrames;
                        continue;
                    }
                    if (packet.StartFrame >= endFrame) break;

                    long first = Math.Max(readFrame, packet.StartFrame);
                    long last = Math.Min(endFrame, packetEnd);
                    int sourceOffset = checked((int)(first - packet.StartFrame) * BytesPerFrame);
                    int outputOffset = checked((int)(first - readFrame) * BytesPerFrame);
                    int bytes = checked((int)(last - first) * BytesPerFrame);
                    packet.Data.AsSpan(sourceOffset, bytes)
                        .CopyTo(destination.Slice(outputOffset, bytes));

                    if (packetEnd > endFrame) break;
                    packets.Dequeue();
                    queuedFrames -= packetFrames;
                }
                readFrame = endFrame;
            }
            return destination.Length;
        }

        private sealed record Packet(long StartFrame, byte[] Data);
    }
}
