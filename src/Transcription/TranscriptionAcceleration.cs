using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace League_Data_Recorder;

#region Device preferences and Whisper backend

public enum TranscriptionDeviceMode
{
    Auto,
    CpuOnly,
}

public sealed record TranscriptionDevicePreferences
{
    public TranscriptionDeviceMode Mode { get; init; } = TranscriptionDeviceMode.Auto;
    public TranscriptionDeviceMode NemoMode { get; init; } = TranscriptionDeviceMode.Auto;
    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "League Data Recorder",
            "transcription-device.json"
        );

    public static TranscriptionDevicePreferences Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<TranscriptionDevicePreferences>(
                File.ReadAllText(SettingsPath)
            );
            return value is not null && Enum.IsDefined(value.Mode) && Enum.IsDefined(value.NemoMode)
                ? value
                : new();
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, SettingsPath, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}

public static class TranscriptionSettingsPreferences
{
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "League Data Recorder", "transcription-settings.json");

    public static TranscriptionSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<TranscriptionSettings>(File.ReadAllText(SettingsPath));
            if (settings is null) return new();
            TranscribeAudio.ValidateInferenceOptions(settings.ToOptions());
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new();
        }
    }

    public static void Save(TranscriptionSettings settings)
    {
        TranscribeAudio.ValidateInferenceOptions(settings.ToOptions());
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, SettingsPath, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

public sealed record TranscriptionExecutionInfo
{
    public string Stage { get; init; } = "Whisper";
    public string Device { get; init; } = "unconfirmed";
    public string Backend { get; init; } = "";
    public string Runtime { get; init; } = "";
    public string RequestedMode { get; init; } = "Auto";
    public bool Confirmed { get; init; }

    // Separate setup tests from jobs: a capability scan is never execution evidence.
    public string Source { get; init; } = "transcription";
    public string Detail { get; init; } = "";
    public string[] Diagnostics { get; init; } = [];
    public DateTimeOffset ObservedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CudaRuntimeCheck(int Major, bool Packaged, bool Available, string Detail);

public sealed record TranscriptionCapabilities(
    bool WindowsX64,
    string[] Gpus,
    int DriverApiVersion,
    CudaRuntimeCheck[] CudaRuntimes,
    string DriverDetail
)
{
    public bool GpuExpected =>
        WindowsX64 && Gpus.Length > 0 && CudaRuntimes.Any(r => r.Packaged && r.Available);

    public string Describe() =>
        string.Join(
            Environment.NewLine,
            new[]
            {
                "Process: " + (WindowsX64 ? "Windows x64" : "This check supports Windows x64"),
                "NVIDIA GPU: " + (Gpus.Length == 0 ? "not detected" : string.Join(", ", Gpus)),
                "NVIDIA driver API: "
                    + (
                        DriverApiVersion == 0
                            ? "not available"
                            : $"CUDA {DriverApiVersion / 1000}.{DriverApiVersion % 1000 / 10}"
                    ),
                DriverDetail,
            }.Concat(
                CudaRuntimes.Select(r =>
                    $"CUDA {r.Major}: app runtime {(r.Packaged ? "present" : "missing")}; {r.Detail}"
                )
            )
        );
}

public static class TranscriptionAcceleration
{
    // Native logging and runtime selection are process-wide. Serialize all our factory/state
    // initializations so another model's log cannot be mistaken for this model's backend.
    private static readonly object NativeInitializationLock = new();

    // whisper.cpp logs are process-wide and state initialization happens on the
    // first ProcessAsync call. Keep one evidence scope alive through the decode.
    private static readonly SemaphoreSlim WhisperExecutionGate = new(1, 1);

    internal static async Task<T> RunNativeExclusiveAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken
    )
    {
        await WhisperExecutionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            WhisperExecutionGate.Release();
        }
    }

    public static T InitializeNative<T>(Func<T> initialize)
    {
        lock (NativeInitializationLock)
            return initialize();
    }

    public static RuntimeLibrary? LoadedRuntime => RuntimeOptions.LoadedLibrary;

    public static bool ConfigureRuntime(TranscriptionDeviceMode mode)
    {
        lock (NativeInitializationLock)
        {
            if (RuntimeOptions.LoadedLibrary is not null)
                return mode == TranscriptionDeviceMode.CpuOnly
                    || RuntimeOptions.LoadedLibrary is RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12;
            RuntimeOptions.RuntimeLibraryOrder =
                mode == TranscriptionDeviceMode.CpuOnly
                    ? [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx]
                    :
                    [
                        RuntimeLibrary.Cuda,
                        RuntimeLibrary.Cuda12,
                        RuntimeLibrary.Cpu,
                        RuntimeLibrary.CpuNoAvx,
                    ];
            return true;
        }
    }

    public static string Forecast(
        TranscriptionCapabilities capabilities,
        TranscriptionDeviceMode mode,
        RuntimeLibrary? loaded
    )
    {
        if (mode == TranscriptionDeviceMode.CpuOnly)
            return "Whisper: CPU selected";
        if (loaded is RuntimeLibrary.Cpu or RuntimeLibrary.CpuNoAvx)
            return capabilities.GpuExpected
                ? "Whisper: CPU loaded · restart for GPU"
                : "Whisper: CPU expected";
        return capabilities.GpuExpected ? "Whisper: GPU expected" : "Whisper: CPU expected";
    }

    internal sealed class BackendEvidence
    {
        private readonly object sync = new();
        private readonly Queue<string> lines = new();
        private string? cudaBackend;
        private bool failed,
            cpuFallback;

        internal void Observe(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            lock (sync)
            {
                foreach (string line in message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (lines.Count == 60)
                        lines.Dequeue();
                    lines.Enqueue(line.Trim());
                    // "CUDA build available" / "found GPU" is not execution evidence.
                    var match = Regex.Match(
                        line,
                        @"whisper_backend_init_gpu:\s*using\s+(CUDA(?:\d+|:\d+)?)\s+backend",
                        RegexOptions.IgnoreCase
                    );
                    if (match.Success)
                    {
                        cudaBackend = match.Groups[1].Value;
                        failed = false;
                        cpuFallback = false;
                    }
                    // Newer builds can report actual model/compute allocations instead.
                    match = Regex.Match(
                        line,
                        @"(?:whisper_model_load|whisper_init_state):\s*(CUDA(?:\d+|:\d+)?)\s+(?:total size|(?:compute|model|KV)\s+buffer(?:\s+size)?)\s*=\s*([0-9.]+)\s*(?:MiB|MB)",
                        RegexOptions.IgnoreCase
                    );
                    if (
                        match.Success
                        && double.TryParse(
                            match.Groups[2].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double size
                        )
                        && size > 0
                    )
                    {
                        cudaBackend = match.Groups[1].Value;
                        failed = false;
                        cpuFallback = false;
                    }
                    if (
                        Regex.IsMatch(
                            line,
                            @"failed to initialize\s+CUDA(?:\d+|:\d+)?\s+backend",
                            RegexOptions.IgnoreCase
                        )
                    )
                        failed = true;
                    if (
                        line.Contains(
                            "whisper_backend_init_gpu: no GPU found",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        cpuFallback = true;
                        cudaBackend = null;
                    }
                }
            }
        }

        internal TranscriptionExecutionInfo Finish(
            TranscriptionDeviceMode mode,
            bool requestedGpu,
            RuntimeLibrary? runtime,
            string? note
        )
        {
            lock (sync)
            {
                bool cuda =
                    requestedGpu
                    && runtime is RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12
                    && cudaBackend is not null
                    && !failed;
                bool cpu =
                    !cuda
                    && (
                        !requestedGpu
                        || runtime is RuntimeLibrary.Cpu or RuntimeLibrary.CpuNoAvx
                        || failed
                        || cpuFallback
                    );
                string detail =
                    cuda
                        ? "Whisper model initialized a CUDA backend. CPU work for audio, decoding, DTW and cleanup is still normal."
                    : cpu ? "Whisper model initialized on CPU."
                    : "A GPU was requested, but the native log did not confirm a CUDA backend. Open GPU setup to inspect the diagnostics.";
                if (!string.IsNullOrEmpty(note))
                    detail += " " + note;
                return new()
                {
                    Device =
                        cuda ? "cuda"
                        : cpu ? "cpu"
                        : "unconfirmed",
                    Backend =
                        cuda ? cudaBackend!
                        : cpu ? "CPU"
                        : "",
                    Runtime = runtime?.ToString() ?? "unknown",
                    RequestedMode = mode.ToString(),
                    Confirmed = cuda || cpu,
                    Detail = detail,
                    Diagnostics = lines.ToArray(),
                };
            }
        }
    }

    internal sealed class WhisperModelSession(
        WhisperFactory factory,
        WhisperProcessor processor,
        IDisposable logger,
        Func<TranscriptionExecutionInfo> getExecution
    ) : IAsyncDisposable
    {
        internal WhisperFactory Factory { get; } = factory;
        internal WhisperProcessor Processor { get; } = processor;
        private int disposed;
        internal TranscriptionExecutionInfo Execution => getExecution();

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            try
            {
                await Processor.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    Factory.Dispose();
                }
                finally
                {
                    logger.Dispose();
                    WhisperExecutionGate.Release();
                }
            }
        }
    }

    internal static WhisperModelSession OpenModel(
        string modelPath,
        WhisperFactoryOptions factoryOptions,
        TranscriptionOptions options,
        Func<WhisperFactory, WhisperProcessor> createProcessor,
        Action<string>? log,
        CancellationToken cancellationToken = default
    )
    {
        WhisperExecutionGate.Wait(cancellationToken);
        IDisposable? logger = null;
        try
        {
            lock (NativeInitializationLock)
            {
                bool canSelectGpu = ConfigureRuntime(options.DeviceMode);
                bool useGpu = options.DeviceMode == TranscriptionDeviceMode.Auto && canSelectGpu;
                factoryOptions.UseGpu = useGpu;
                factoryOptions.GpuDevice = options.GpuDevice;
                string? note = !canSelectGpu
                    ? "The CPU native runtime was loaded earlier. If CUDA has since been installed or enabled, restart with Auto selected to load it."
                    : null;
                var evidence = new BackendEvidence();
                // Native callbacks may arrive on worker threads. Initialization is already
                // serialized; filtering by the managed caller's thread discarded real evidence.
                logger = LogProvider.AddLogger((_, line) => evidence.Observe(line));
                WhisperFactory? factory = null;
                WhisperProcessor processor;
                try
                {
                    try
                    {
                        factory = WhisperFactory.FromPath(modelPath, factoryOptions);
                        // Backend initialization can be deferred further to ProcessAsync.
                        // The returned session owns the logger until decoding has finished.
                        processor = createProcessor(factory);
                    }
                    catch (WhisperModelLoadException) when (useGpu)
                    {
                        factory?.Dispose();
                        factory = null;
                        note =
                            "GPU model initialization failed; this job retried on CPU. Check free GPU memory and the CUDA dependencies in GPU setup.";
                        log?.Invoke(note);
                        factoryOptions.UseGpu = false;
                        useGpu = false;
                        evidence = new BackendEvidence();
                        factory = WhisperFactory.FromPath(modelPath, factoryOptions);
                        processor = createProcessor(factory);
                    }
                }
                catch
                {
                    factory?.Dispose();
                    throw;
                }
                return new WhisperModelSession(
                    factory,
                    processor,
                    logger,
                    () =>
                        evidence.Finish(
                            options.DeviceMode,
                            useGpu,
                            RuntimeOptions.LoadedLibrary,
                            note
                        )
                );
            }
        }
        catch
        {
            logger?.Dispose();
            WhisperExecutionGate.Release();
            throw;
        }
    }

    // Test in a disposable copy of this executable: native CUDA faults stay outside the UI.
    public static async Task<TranscriptionExecutionInfo> TestModelAsync(
        TranscriptionOptions options,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        string executable =
            Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Cannot find the application executable for the isolated model test."
            );
        if (
            Path.GetFileNameWithoutExtension(executable)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidOperationException(
                "Start the application's EXE to use the isolated model test."
            );
        var folder = Directory.CreateTempSubdirectory("league-device-probe-");
        try
        {
            string request = Path.Combine(folder.FullName, "request.json"),
                response = Path.Combine(folder.FullName, "response.json");
            await File.WriteAllTextAsync(
                    request,
                    JsonSerializer.Serialize(
                        new
                        {
                            WhisperModelPath = Path.GetFullPath(
                                options.WhisperModelPath,
                                AppContext.BaseDirectory
                            ),
                            options.DeviceMode,
                            options.GpuDevice,
                            options.Alignment,
                            options.OverlapSeconds,
                        }
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
            var start = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string arg in new[] { "--transcription-device-probe", request, response })
                start.ArgumentList.Add(arg);
            using var process = new Process { StartInfo = start };
            process.Start();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(),
                stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token
            );
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(true);
                }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(
                    "The isolated Whisper model test exceeded three minutes."
                );
            }
            await stdout.ConfigureAwait(false);
            string error = await stderr.ConfigureAwait(false);
            if (!File.Exists(response))
                throw new InvalidOperationException(
                    $"The isolated model test exited {process.ExitCode}. Check the driver/CUDA runtime and GPU memory, or use CPU only. "
                        + error[^Math.Min(error.Length, 1800)..]
                );
            using var json = JsonDocument.Parse(
                await File.ReadAllTextAsync(response, cancellationToken).ConfigureAwait(false)
            );
            if (json.RootElement.TryGetProperty("Error", out var failure))
                throw new InvalidOperationException(failure.GetString());
            var execution =
                json.RootElement.GetProperty("Execution").Deserialize<TranscriptionExecutionInfo>()
                ?? throw new InvalidDataException("No execution report returned by model test.");
            return execution with { Source = "model_test" };
        }
        finally
        {
            try
            {
                folder.Delete(true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Must run before Windows UI initialization. Returns null for ordinary launches.</summary>
    public static int? RunProbeWorker(string[] args)
    {
        if (args.Length == 0 || args[0] != "--transcription-device-probe")
            return null;
        if (args.Length != 3)
            return 2;
        try
        {
            var options =
                JsonSerializer.Deserialize<TranscriptionOptions>(File.ReadAllText(args[1]))
                ?? throw new InvalidDataException("Missing model-test options.");
            var result = TestModelInCurrentProcess(options).GetAwaiter().GetResult();
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { Execution = result }));
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(args[2], JsonSerializer.Serialize(new { Error = ex.Message }));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return 1;
        }
    }

    private static async Task<TranscriptionExecutionInfo> TestModelInCurrentProcess(
        TranscriptionOptions options
    )
    {
        string path = Path.GetFullPath(options.WhisperModelPath, AppContext.BaseDirectory);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "The Whisper model is missing. Copy your existing GGML model into the app's models folder before testing.",
                path
            );
        if (!Enum.IsDefined(options.DeviceMode) || options.GpuDevice < 0)
            throw new ArgumentException("Invalid device settings.");
        TranscriptAlignment.Validate(options.Alignment, options.OverlapSeconds);
        var settings = new WhisperFactoryOptions { UseFlashAttention = false };
        if (options.Alignment.Enabled)
        {
            settings.UseDtwTimeStamps = true;
            settings.HeadsPreset = TranscriptAlignment.DetectPreset(
                path,
                options.Alignment.HeadsPreset
            );
            settings.DtwMemSize = checked((uint)options.Alignment.DtwMemoryMb * 1024U * 1024U);
        }
        await using var session = OpenModel(
            path,
            settings,
            options,
            factory =>
                factory
                    .CreateBuilder()
                    .WithLanguage(options.Language)
                    .WithThreads(Math.Max(1, Environment.ProcessorCount / 2))
                    .Build(),
            null
        );
        // A factory alone does not initialize the inference backend. Run a tiny
        // silent sample so this isolated test observes the same path as a job.
        await foreach (var _ in session.Processor.ProcessAsync(new float[32000])) { }
        return session.Execution with
        {
            Source = "model_test",
            Detail = session.Execution.Detail + " Verified with a two-second silent test sample.",
        };
    }

    public static Task<TranscriptionCapabilities> CheckAsync(
        CancellationToken cancellationToken = default
    ) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = Check();
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            },
            cancellationToken
        );

    private static TranscriptionCapabilities Check()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            return new(
                false,
                [],
                0,
                [],
                "Automatic NVIDIA checks are available in the Windows x64 build. CPU remains available."
            );
        var gpus = new List<string>();
        int version = 0;
        string detail = "";
        if (NativeLibrary.TryLoad("nvcuda.dll", out var driver))
        {
            try
            {
                int init = Export<Init>(driver, "cuInit")(0);
                if (init != 0)
                    detail =
                        $"NVIDIA driver initialization failed (CUDA error {init}). Update the driver, then restart Windows if requested.";
                else
                {
                    Export<GetCount>(driver, "cuDriverGetVersion")(out version);
                    Export<GetCount>(driver, "cuDeviceGetCount")(out int count);
                    for (int i = 0; i < count; i++)
                    {
                        var name = new StringBuilder(256);
                        if (Export<GetName>(driver, "cuDeviceGetName")(name, name.Capacity, i) == 0)
                            gpus.Add(name.ToString());
                    }
                }
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or ArgumentException)
            {
                detail = "NVIDIA driver query failed: " + ex.Message;
            }
            finally
            {
                NativeLibrary.Free(driver);
            }
        }
        else
            detail =
                "NVIDIA's driver DLL was not found. CUDA Toolkit alone does not replace the NVIDIA display driver.";
        var checks = new List<CudaRuntimeCheck>();
        foreach (int major in new[] { 13, 12 })
        {
            string folder = Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                major == 13 ? "cuda" : "cuda12",
                "win-x64"
            );
            bool packaged = new[]
            {
                "whisper.dll",
                "ggml-cuda-whisper.dll",
                "ggml-whisper.dll",
                "ggml-base-whisper.dll",
                "ggml-cpu-whisper.dll",
            }.All(n => File.Exists(Path.Combine(folder, n)));
            bool available = false;
            string reason;
            if (NativeLibrary.TryLoad($"cudart64_{major}.dll", out var runtime))
            {
                try
                {
                    int status = Export<GetCount>(runtime, "cudaRuntimeGetVersion")(out int found);
                    int devices = Export<GetCount>(runtime, "cudaGetDeviceCount")(out int count);
                    available =
                        status == 0
                        && found / 1000 == major
                        && found >= (major == 13 ? 13000 : 12040)
                        && devices == 0
                        && count > 0;
                    reason = available
                        ? $"CUDA runtime available; {count} visible GPU(s)"
                        : $"CUDA check failed (version status {status}, runtime {found}, device status {devices}, devices {count})";
                }
                catch (Exception ex) when (ex is EntryPointNotFoundException or ArgumentException)
                {
                    reason = "CUDA runtime query failed: " + ex.Message;
                }
                finally
                {
                    NativeLibrary.Free(runtime);
                }
            }
            else
                reason = $"cudart64_{major}.dll was not found on the app/DLL search path";
            checks.Add(new(major, packaged, available, reason));
        }
        return new(true, gpus.ToArray(), version, checks.ToArray(), detail);
    }

    private static T Export<T>(IntPtr library, string name)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Init(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetCount(out int count);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    private delegate int GetName(StringBuilder name, int length, int device);
}

#endregion

#region NeMo backend

public sealed record NemoCapabilities
{
    public string Executable { get; init; } = "";
    public bool Exists { get; init; }
    public bool DoctorAvailable { get; init; }
    public string Version { get; init; } = "unknown";
    public bool? DiarizationCompiled { get; init; }
    public bool? CudaCompiled { get; init; }
    public bool? VulkanCompiled { get; init; }
    public bool? MetalCompiled { get; init; }
    public string[] GpuNames { get; init; } = [];
    public bool CudaDeviceAvailable { get; init; }
    public bool DeviceFlag { get; init; }
    public bool GpuFlag { get; init; }
    public bool VerboseFlag { get; init; }
    public string[] DiarizationPresets { get; init; } = [];
    public string[] SegmentationFlags { get; init; } = [];
    public bool CpuOnlyBuild =>
        DoctorAvailable
        && CudaCompiled == false
        && VulkanCompiled == false
        && MetalCompiled == false
        && GpuNames.Length == 0;
    public bool GpuExpected =>
        DoctorAvailable
        && GpuNames.Length > 0
        && (CudaCompiled == true || VulkanCompiled == true || MetalCompiled == true)
        && DiarizationCompiled != false;
    public bool CanForceCpu => DeviceFlag || GpuFlag || CpuOnlyBuild;
    public string[] Diagnostics { get; init; } = [];
    public DateTimeOffset CheckedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Forecast(TranscriptionDeviceMode mode)
    {
        if (!Exists)
            return "NeMo: runtime missing";
        if (DiarizationCompiled == false)
            return "NeMo: diarization not included";
        if (mode == TranscriptionDeviceMode.CpuOnly)
            return CanForceCpu ? "NeMo: CPU selected" : "NeMo: CPU control unsupported";
        if (GpuExpected)
            return "NeMo: GPU expected";
        if (CpuOnlyBuild)
            return "NeMo: CPU-only build";
        if (DoctorAvailable)
            return "NeMo: CPU expected · no GPU detected";
        return "NeMo: device not yet verified";
    }

    public string Describe() =>
        string.Join(
            Environment.NewLine,
            new[]
            {
                "NeMo executable: " + Executable,
                "NeMo version: " + Version,
                "Doctor: "
                    + (
                        DoctorAvailable
                            ? "available"
                            : "unavailable / unsupported by this executable"
                    ),
                $"Compiled: diarization={DiarizationCompiled?.ToString() ?? "unknown"}, CUDA={CudaCompiled?.ToString() ?? "unknown"}, Vulkan={VulkanCompiled?.ToString() ?? "unknown"}, Metal={MetalCompiled?.ToString() ?? "unknown"}",
                "NeMo GPU devices: "
                    + (GpuNames.Length == 0 ? "none reported" : string.Join(", ", GpuNames)),
                $"CLI controls: --device={DeviceFlag}, --gpu={GpuFlag}, --verbose={VerboseFlag}",
                "Recorded-audio diarization presets: " + (DiarizationPresets.Length == 0
                    ? "not advertised by this build" : string.Join(", ", DiarizationPresets)),
                "Diarization segmentation controls: " + (SegmentationFlags.Length == 0
                    ? "not advertised by this build" : string.Join(", ", SegmentationFlags)),
            }.Concat(Diagnostics)
        );
}

public static class NemoAcceleration
{
    // Included in the saved RTTM signature: a prior low-latency result must not
    // be reused silently after changing the recorded-audio context policy.
    internal const int RecordedAudioContextVersion = 1;
    private static readonly ConcurrentDictionary<string, NemoCapabilities> Cache = new();

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);

    internal static NemoCapabilities ParseCapabilities(
        string executable,
        string doctor,
        string rootHelp,
        string diarHelp,
        string diagnostic = ""
    )
    {
        bool? diar = null,
            cuda = null,
            vulkan = null,
            metal = null;
        bool known = false,
            cudaDevice = false;
        string version = "unknown";
        var devices = new List<string>();
        try
        {
            string text = doctor.Trim().TrimStart('\uFEFF');
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (
                root.TryGetProperty("features", out var features)
                && features.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("devices", out var devs)
                && devs.ValueKind == JsonValueKind.Array
            )
            {
                known = true;
                bool? Flag(string name) =>
                    features.TryGetProperty(name, out var v)
                    && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? v.GetBoolean()
                        : null;
                diar = Flag("diarization");
                cuda = Flag("backend_cuda");
                vulkan = Flag("backend_vulkan");
                metal = Flag("backend_metal");
                if (
                    root.TryGetProperty("version", out var ver)
                    && ver.ValueKind == JsonValueKind.String
                )
                    version = ver.GetString() ?? version;
                foreach (var device in devs.EnumerateArray())
                {
                    if (
                        !device.TryGetProperty("type", out var type)
                        || type.ValueKind != JsonValueKind.String
                        || type.GetString() is not ("gpu" or "integrated-gpu")
                    )
                        continue;
                    string name =
                        device.TryGetProperty("name", out var n)
                        && n.ValueKind == JsonValueKind.String
                            ? n.GetString() ?? "GPU"
                            : "GPU";
                    string label =
                        device.TryGetProperty("description", out var d)
                        && d.ValueKind == JsonValueKind.String
                            ? d.GetString() ?? name
                            : name;
                    devices.Add(name + ": " + label);
                    if (name.StartsWith("CUDA", StringComparison.OrdinalIgnoreCase))
                        cudaDevice = true;
                }
            }
        }
        catch (JsonException) { }
        bool Help(string text, string flag) =>
            text.Contains("Usage:", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(text, @"(?<![\w-])" + Regex.Escape(flag) + @"\b")
            && !text.Contains("unknown option", StringComparison.OrdinalIgnoreCase);
        return new()
        {
            Executable = executable,
            Exists = true,
            DoctorAvailable = known,
            Version = version,
            DiarizationCompiled = diar,
            CudaCompiled = cuda,
            VulkanCompiled = vulkan,
            MetalCompiled = metal,
            GpuNames = devices.ToArray(),
            CudaDeviceAvailable = cudaDevice,
            DeviceFlag = Help(diarHelp, "--device"),
            GpuFlag = Help(diarHelp, "--gpu"),
            VerboseFlag = Help(rootHelp, "--verbose"),
            DiarizationPresets = Help(diarHelp, "--preset")
                ? new[] { "offline", "v3-offline" }.Where(preset =>
                    Regex.IsMatch(diarHelp, @"(?<![\w-])" + Regex.Escape(preset) + @"(?![\w-])"))
                    .ToArray()
                : [],
            SegmentationFlags = new[]
            {
                "--onset", "--offset", "--min-duration-on", "--min-duration-off",
                "--pad-onset", "--pad-offset",
            }.Where(flag => Help(diarHelp, flag)).ToArray(),
            Diagnostics = string.IsNullOrWhiteSpace(diagnostic) ? [] : [diagnostic],
        };
    }

    public static async Task<NemoCapabilities> CheckAsync(
        TranscriptionOptions? options = null,
        bool refresh = false,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new();
        cancellationToken.ThrowIfCancellationRequested();
        string exe = Path.GetFullPath(options.NemoSpeechExecutable, AppContext.BaseDirectory);
        if (!File.Exists(exe))
            return new()
            {
                Executable = exe,
                Diagnostics =
                [
                    "Copy the complete NeMo runtime bundle into runtime/nemo, including bin/nemo-speech.exe and its DLLs.",
                ],
            };
        var file = new FileInfo(exe);
        string key = exe + ":" + file.Length + ":" + file.LastWriteTimeUtc.Ticks;
        if (
            !refresh
            && Cache.TryGetValue(key, out var value)
            && DateTimeOffset.UtcNow - value.CheckedUtc < TimeSpan.FromSeconds(30)
        )
            return value;
        async Task<(string Text, string Error)> Probe(string[] args)
        {
            try
            {
                var r = await RunCliAsync(exe, args, 15, null, cancellationToken)
                    .ConfigureAwait(false);
                return (
                    r.ExitCode == 0 ? r.Stdout + r.Stderr : "",
                    r.ExitCode == 0
                        ? ""
                        : $"{string.Join(' ', args)} exited {r.ExitCode}: "
                            + Tail(r.Stderr + r.Stdout, 500)
                );
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or InvalidOperationException
                            or System.ComponentModel.Win32Exception
                            or TimeoutException
                )
            {
                return ("", ex.Message);
            }
        }
        var rootTask = Probe(["--help"]);
        var diarTask = Probe(["diarize", "--help"]);
        var doctorTask = Probe(["doctor", "--json"]);
        await Task.WhenAll(rootTask, diarTask, doctorTask).ConfigureAwait(false);
        var root = await rootTask;
        var diar = await diarTask;
        var doctor = await doctorTask;
        // doctor JSON is stdout-only; native device discovery may also write diagnostics to stderr.
        string doctorJson = doctor.Text;
        int brace = doctorJson.IndexOf('{'),
            end = doctorJson.LastIndexOf('}');
        if (brace >= 0 && end >= brace)
            doctorJson = doctorJson[brace..(end + 1)];
        var result = ParseCapabilities(
            exe,
            doctorJson,
            root.Text,
            diar.Text,
            string.Join(
                Environment.NewLine,
                new[] { root.Error, diar.Error, doctor.Error }.Where(x => x.Length > 0)
            )
        );
        Cache[key] = result;
        return result;
    }

    internal static List<string> BuildArguments(
        NemoCapabilities caps,
        string audio,
        string model,
        string output,
        TranscriptionOptions options,
        bool cpu
    )
    {
        if (cpu && !caps.CanForceCpu)
            throw new NotSupportedException(
                "This NeMo executable does not advertise a way to force CPU. Update the runtime or select Auto; CPU use will not be assumed."
            );
        var args = new List<string>();
        if (caps.VerboseFlag)
            args.Add("--verbose");
        args.AddRange(["diarize", audio, "--model", model, "--format", "rttm", "--output", output]);
        TranscribeAudio.ValidateInferenceOptions(options);
        if (RecordedAudioPreset(caps, model, options) is { } preset)
            args.AddRange(["--preset", preset]);
        if (options.Diarization.Enabled)
        {
            var settings = options.Diarization;
            var values = new (string Flag, double Value)[]
            {
                ("--onset", settings.Onset), ("--offset", settings.Offset),
                ("--min-duration-on", settings.MinDurationOnSeconds),
                ("--min-duration-off", settings.MinDurationOffSeconds),
                ("--pad-onset", settings.PadOnsetSeconds), ("--pad-offset", settings.PadOffsetSeconds),
            };
            string[] missing = values.Select(v => v.Flag).Except(caps.SegmentationFlags, StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                throw new NotSupportedException("This NeMo executable does not advertise "
                    + string.Join(", ", missing)
                    + ". Update the NeMo runtime or turn off custom diarization segmentation in Settings.");
            foreach (var (flag, value) in values)
                args.AddRange([flag, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)]);
        }
        if (caps.DeviceFlag)
        {
            string device =
                cpu ? "cpu"
                : caps.CudaCompiled == true && caps.CudaDeviceAvailable
                    ? $"cuda:{options.NemoGpuDevice}"
                : "auto";
            args.AddRange(["--device", device]);
        }
        else if (caps.GpuFlag)
            args.AddRange([
                "--gpu",
                cpu
                    ? "-1"
                    : options.NemoGpuDevice.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
            ]);
        return args;
    }

    internal static string? RecordedAudioPreset(
        NemoCapabilities caps, string model, TranscriptionOptions options)
    {
        if (!options.Diarization.UseRecordedAudioContext) return null;
        string name = Path.GetFileName(model);
        // Do not apply a V2 preset to the eight-speaker V3 checkpoint, or infer
        // the architecture of an arbitrary user-provided GGUF. The bundled
        // filename is stable across quantizations; unknown names keep defaults.
        string? preset = Regex.IsMatch(name, @"^Nemotron[-_]3[-_]Diarization(?:[._-]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? "v3-offline" : null;
        return preset is not null && caps.DiarizationPresets.Contains(preset, StringComparer.Ordinal)
            ? preset : null;
    }

    internal sealed class BackendEvidence
    {
        private readonly object sync = new();
        private readonly Queue<string> lines = new();
        private string? gpu;
        private bool cpu,
            failed,
            gpuHint;

        public void Observe(string line)
        {
            lock (sync)
            {
                if (lines.Count == 60)
                    lines.Dequeue();
                lines.Enqueue(line);
                var match = Regex.Match(
                    line,
                    @"\bUsing\s+(?:borrowed\s+)?GPU\s+backend\s*[:=]\s*(\S+)",
                    RegexOptions.IgnoreCase
                );
                if (!match.Success)
                    match = Regex.Match(
                        line,
                        @"\bUsing\s+((?:CUDA|Vulkan|Metal)(?:\d+|:\d+)?)\s+backend\b",
                        RegexOptions.IgnoreCase
                    );
                if (match.Success)
                {
                    gpu = match.Groups[1].Value;
                    failed = false;
                    gpuHint = true;
                }
                if (Regex.IsMatch(line, @"Using\s+CPU\s+backend\b", RegexOptions.IgnoreCase))
                    cpu = true;
                if (
                    Regex.IsMatch(
                        line,
                        @"(?:use_gpu=true|GPU backend|CUDA backend).*(?:failed|no matching)|(?:failed|error).*(?:CUDA|GPU backend)|\bfalling back to CPU\b",
                        RegexOptions.IgnoreCase
                    )
                )
                    failed = true;
                if (Regex.IsMatch(line, @"Using\s+(?:GPU|CUDA)", RegexOptions.IgnoreCase))
                    gpuHint = true;
            }
        }

        internal TranscriptionExecutionInfo Finish(
            NemoCapabilities caps,
            TranscriptionDeviceMode mode,
            int exitCode,
            string? note = null,
            bool cpuRequested = false
        )
        {
            lock (sync)
            {
                bool usedGpu = exitCode == 0 && !cpuRequested && gpu is not null && !failed;
                bool usedCpu =
                    exitCode == 0
                    && !usedGpu
                    && (cpuRequested || caps.CpuOnlyBuild || cpu && (!gpuHint || failed));
                return new()
                {
                    Stage = "NeMo",
                    Device =
                        usedGpu
                            ? (
                                gpu!.StartsWith("CUDA", StringComparison.OrdinalIgnoreCase)
                                    ? "cuda"
                                    : "gpu"
                            )
                        : usedCpu ? "cpu"
                        : "unconfirmed",
                    Backend =
                        usedGpu ? gpu!
                        : usedCpu ? "CPU"
                        : "",
                    Runtime = "NeMo-Speech.cpp " + caps.Version,
                    RequestedMode = mode.ToString(),
                    Confirmed = usedGpu || usedCpu,
                    Detail =
                        (
                            usedGpu
                                ? "NeMo diarization selected a GPU backend. CPU work can still occur."
                            : usedCpu ? "NeMo diarization used CPU."
                            : "NeMo did not provide enough backend evidence to confirm CPU/GPU use."
                        ) + (note is null ? "" : " " + note),
                    Diagnostics = lines.ToArray(),
                };
            }
        }
    }

    internal static async Task<TranscriptionExecutionInfo> RunDiarizationAsync(
        string audio,
        string model,
        string output,
        TranscriptionOptions options,
        Action<string>? log,
        CancellationToken cancellationToken,
        int timeoutSeconds = 0
    )
    {
        TranscribeAudio.ValidateInferenceOptions(options);
        if (!Enum.IsDefined(options.NemoDeviceMode) || options.NemoGpuDevice < 0)
            throw new ArgumentException("Invalid NeMo device settings.");
        var caps = await CheckAsync(options, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!caps.Exists)
            throw new FileNotFoundException(
                "NeMo runtime is missing. Open device setup and install/copy a complete NeMo bundle.",
                caps.Executable
            );
        if (caps.DiarizationCompiled == false)
            throw new InvalidOperationException(
                "This NeMo build does not include diarization. Install a build with NEMO_SPEECH_BUILD_DIAR enabled."
            );
        if (!File.Exists(model))
            throw new FileNotFoundException("NeMo diarization model is missing.", model);
        string? recordedPreset = RecordedAudioPreset(caps, model, options);
        string contextNote = recordedPreset is not null
            ? "Recorded-audio context: " + recordedPreset + "; larger chunks and look-ahead, with one continuous speaker cache."
            : options.Diarization.UseRecordedAudioContext
                ? "Recorded-audio context was unavailable for this runtime/model; keeping its default context."
                : "Using the runtime's default diarization context.";
        log?.Invoke(contextNote);
        bool cpu =
            options.NemoDeviceMode == TranscriptionDeviceMode.CpuOnly
            || caps.DoctorAvailable && !caps.GpuExpected && caps.CanForceCpu;
        var evidence = new BackendEvidence();
        log?.Invoke(
            cpu
                ? "Running NeMo with CPU requested."
                : "Running NeMo with automatic/GPU device selection."
        );
        var run = await RunCliAsync(
                caps.Executable,
                BuildArguments(caps, audio, model, output, options, cpu),
                timeoutSeconds,
                evidence.Observe,
                cancellationToken
            )
            .ConfigureAwait(false);
        string? note = contextNote;
        if (
            run.ExitCode != 0
            && !cpu
            && options.NemoDeviceMode == TranscriptionDeviceMode.Auto
            && caps.CanForceCpu
        )
        {
            log?.Invoke("NeMo Auto/GPU attempt failed; retrying once with CPU.");
            string failure = Tail(run.Stderr + run.Stdout, 1500);
            if (File.Exists(output))
                File.Delete(output);
            evidence = new BackendEvidence();
            run = await RunCliAsync(
                    caps.Executable,
                    BuildArguments(caps, audio, model, output, options, true),
                    timeoutSeconds,
                    evidence.Observe,
                    cancellationToken
                )
                .ConfigureAwait(false);
            cpu = true;
            note =
                "The first Auto/GPU attempt failed; CPU retry was used. Initial diagnostics: "
                + failure + " " + contextNote;
        }
        if (run.ExitCode != 0)
            throw new InvalidOperationException(
                $"NeMo diarization exited {run.ExitCode}: " + Tail(run.Stderr + run.Stdout, 2500)
            );
        if (!File.Exists(output))
            throw new InvalidDataException(
                "NeMo completed without writing its requested RTTM output."
            );
        var execution = evidence.Finish(
            caps,
            options.NemoDeviceMode,
            run.ExitCode,
            note,
            cpuRequested: cpu
        );
        log?.Invoke(
            $"NeMo device: {execution.Device.ToUpperInvariant()} · {execution.Backend}. {execution.Detail}"
        );
        options.ExecutionProgress?.Report(execution);
        return execution;
    }

    public static async Task<TranscriptionExecutionInfo> TestModelAsync(
        TranscriptionOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new();
        cancellationToken.ThrowIfCancellationRequested();
        var folder = Directory.CreateTempSubdirectory("league-nemo-test-");
        try
        {
            string audio = Path.Combine(folder.FullName, "test.wav"),
                output = Path.Combine(folder.FullName, "test.rttm");
            using (var writer = new BinaryWriter(File.Create(audio)))
            {
                writer.Write("RIFF"u8);
                writer.Write(36 + 64000);
                writer.Write("WAVEfmt "u8);
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(16000);
                writer.Write(32000);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write("data"u8);
                writer.Write(64000);
                writer.Write(new byte[64000]);
            }
            var execution = await RunDiarizationAsync(
                    audio,
                    Path.GetFullPath(options.DiarizationModelPath, AppContext.BaseDirectory),
                    output,
                    options with
                    {
                        ExecutionProgress = null,
                    },
                    null,
                    cancellationToken,
                    180
                )
                .ConfigureAwait(false);
            return execution with { Source = "model_test" };
        }
        finally
        {
            try
            {
                folder.Delete(true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string Tail(string value, int max) => value.Length > max ? value[^max..] : value;

    private static async Task<CliResult> RunCliAsync(
        string exe,
        IEnumerable<string> args,
        int timeoutSeconds,
        Action<string>? observe,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string arg in args)
            start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        process.Start();
        async Task<string> Drain(StreamReader reader)
        {
            var text = new StringBuilder();
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                observe?.Invoke(line);
                text.AppendLine(line);
                if (text.Length > 1_000_000)
                    text.Remove(0, text.Length - 1_000_000);
            }
            return text.ToString();
        }
        var stdout = Drain(process.StandardOutput);
        var stderr = Drain(process.StandardError);
        using var timeout = new CancellationTokenSource();
        if (timeoutSeconds > 0)
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("NeMo check/model test exceeded its time limit.");
        }
        return new(
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false)
        );
    }
}

#endregion
