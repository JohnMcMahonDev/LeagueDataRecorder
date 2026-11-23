using System.Collections.Concurrent;
using System.Text.Json;
using LeagueTelemetry;

namespace League_Data_Recorder;

internal static class MatchTimelineStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> Attempts = new(StringComparer.OrdinalIgnoreCase);
    internal static string TimelinePath(string gamePath) => gamePath + "_MatchTimeline.json";
    internal static string DetailsPath(string gamePath) => gamePath + "_MatchDetails.json";

    internal static async Task<bool> EnsureAsync(LeagueClientApi client, string gamePath, long gameId,
        Action<string>? log = null, CancellationToken token = default)
    {
        if (gameId <= 0) return false;
        var gate = Gates.GetOrAdd(Path.GetFullPath(gamePath), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            bool hasTimeline = ValidFile(TimelinePath(gamePath), gameId, timeline: true);
            bool hasDetails = ValidFile(DetailsPath(gamePath), gameId, timeline: false);
            if (hasTimeline && hasDetails) return true;
            if (Attempts.TryGetValue(gamePath, out var last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(30))
            { log?.Invoke("Gold history: retry cooldown; select this game again after 30 seconds."); return hasTimeline; }
            Attempts[gamePath] = DateTime.UtcNow;
            var timelineTask = hasTimeline ? Task.FromResult<string?>(null) : client.GetMatchTimelineAsync(gameId, token, log);
            var detailsTask = hasDetails ? Task.FromResult<string?>(null) : client.GetMatchDetailsAsync(gameId, token, log);
            await Task.WhenAll(timelineTask, detailsTask).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            string? timeline = await timelineTask, details = await detailsTask;
            if (details is not null)
            {
                string? error = ValidationError(details, gameId, false);
                if (error is null) await SaveAsync(DetailsPath(gamePath), details, token).ConfigureAwait(false);
                else log?.Invoke("Gold history: match details response rejected: " + error);
            }
            if (timeline is not null && ValidJson(timeline, gameId, true))
            {
                await SaveAsync(TimelinePath(gamePath), timeline, token).ConfigureAwait(false);
                log?.Invoke("Saved match-history timeline for game " + gameId + ".");
                return true;
            }
            if (timeline is not null) log?.Invoke("Gold history: timeline response rejected: " + ValidationError(timeline, gameId, true));
            if (!hasTimeline) log?.Invoke("Gold history could not be loaded for game " + gameId + ". See the request/response reason above; this does not confirm that the match has no history.");
            return hasTimeline;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Attempts.TryRemove(gamePath, out _); throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.Net.Http.HttpRequestException)
        { log?.Invoke("Could not cache match gold history: " + ex.Message); return false; }
        finally { gate.Release(); }
    }

    private static bool ValidFile(string path, long gameId, bool timeline)
    {
        try { return File.Exists(path) && ValidJson(File.ReadAllText(path), gameId, timeline); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    internal static bool ValidJson(string text, long gameId, bool timeline) => ValidationError(text, gameId, timeline) is null;

    internal static string? ValidationError(string text, long gameId, bool timeline)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = MatchGoldHistory.Unwrap(json.RootElement);
            if (timeline && root.ValueKind == JsonValueKind.Array)
                return root.GetArrayLength() > 1 ? null : $"Only {root.GetArrayLength()} timeline frame(s) were returned; at least two are needed.";
            if (root.ValueKind != JsonValueKind.Object) return "Expected a JSON object; received " + root.ValueKind + ".";
            var id = MatchGoldHistory.Get(root, "gameId");
            if (id.ValueKind != JsonValueKind.Undefined && (!long.TryParse(id.ToString(), out long returned) || returned != gameId))
                return "The returned gameId does not match the selected recording.";
            string field = timeline ? "frames" : "participants";
            var values = MatchGoldHistory.Get(root, field);
            if (values.ValueKind != JsonValueKind.Array)
                return $"Expected an array named '{field}'; received {values.ValueKind}.";
            int count = values.GetArrayLength();
            return count > (timeline ? 1 : 0) ? null : $"The '{field}' array has {count} entries.";
        }
        catch (JsonException) { return "The client returned invalid JSON."; }
    }
    private static async Task SaveAsync(string path, string data, CancellationToken token)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, data, token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
