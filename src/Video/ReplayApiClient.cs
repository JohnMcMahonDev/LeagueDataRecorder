using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace League_Data_Recorder;

internal sealed record ReplayPlayback
{
    [JsonRequired] public double Time { get; init; }
    [JsonRequired] public double Speed { get; init; }
    [JsonRequired] public double Length { get; init; }
    [JsonRequired] public bool Paused { get; init; }
    [JsonRequired] public bool Seeking { get; init; }

    internal bool IsValid => double.IsFinite(Time) && Time >= 0 &&
        double.IsFinite(Speed) && Speed >= 0 && double.IsFinite(Length) && Length > 0 && Time <= Length + 1;

    // Bound extrapolation so an unresponsive API cannot leave stale comms on screen.
    internal double? TimeAt(double sampleClock, double now)
    {
        double elapsed = now - sampleClock;
        if (!IsValid || Seeking || !double.IsFinite(elapsed) || elapsed < 0 || elapsed > 0.75) return null;
        return Math.Min(Length, Time + (Paused ? 0 : elapsed * Speed));
    }
}

internal sealed record ReplaySnapshot(int ProcessId, ReplayPlayback Playback, double SampleClock);

internal sealed class ReplayApiClient : IDisposable
{
    private readonly HttpClient client;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record ReplayGame([property: JsonPropertyName("processID")] int ProcessId);
    internal static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    internal ReplayApiClient() : this(new HttpClientHandler
    {
        UseProxy = false, AllowAutoRedirect = false,
        // Only the fixed localhost URL below uses this handler. League's local
        // game API has a self-signed certificate; no remote traffic is permitted.
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    }) { }

    internal ReplayApiClient(HttpMessageHandler handler)
    {
        client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://127.0.0.1:2999"), Timeout = TimeSpan.FromMilliseconds(650),
        };
    }

    internal async Task<ReplaySnapshot> ReadAsync(CancellationToken token)
    {
        var game = await client.GetFromJsonAsync<ReplayGame>("/replay/game", JsonOptions, token).ConfigureAwait(false);
        double before = Now;
        var playback = await client.GetFromJsonAsync<ReplayPlayback>("/replay/playback", JsonOptions, token).ConfigureAwait(false);
        double after = Now;
        if (game is null || game.ProcessId <= 0 || playback is null || !playback.IsValid)
            throw new InvalidDataException("League returned an incomplete replay clock.");
        return new(game.ProcessId, playback, (before + after) / 2);
    }

    public void Dispose() => client.Dispose();
}
