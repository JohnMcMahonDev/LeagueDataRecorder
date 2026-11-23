using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LeagueTelemetry;

#region League client API

public class LeagueClientApi
{
    private const string DataDragonBaseUrl = "https://ddragon.leagueoflegends.com";
    private static readonly HttpClient DataDragonClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private readonly HttpClient _client;

    public LeagueClientApi()
    {
        var handler = new HttpClientHandler();

        // League uses a local self-signed HTTPS certificate.
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    private static string FindLockfilePath()
    {
        Process[] processes = Process
            .GetProcessesByName("LeagueClient")
            .Concat(Process.GetProcessesByName("LeagueClientUx"))
            .ToArray();

        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    string executablePath = GetExecutablePath(process.Id);

                    if (string.IsNullOrWhiteSpace(executablePath))
                        continue;

                    string? directory = Path.GetDirectoryName(executablePath);

                    if (string.IsNullOrWhiteSpace(directory))
                        continue;

                    string lockfilePath = Path.Combine(directory, "lockfile");

                    if (File.Exists(lockfilePath))
                        return lockfilePath;
                }
                catch (InvalidOperationException ex)
                {
                    Debug.WriteLine($"League process {process.Id} exited: {ex.Message}");
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    Debug.WriteLine(
                        $"Cannot inspect League process {process.Id}: "
                            + $"{ex.Message} (Windows error {ex.NativeErrorCode})"
                    );
                }
            }
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }

        throw new FileNotFoundException("Could not find the running League client's lockfile.");
    }

    // Resolve the active League installation instead of assuming C:\Riot Games.
    public static string GetInstallationDirectory() => Path.GetDirectoryName(FindLockfilePath())!;

    private static string ReadLockfile()
    {
        string lockfilePath = FindLockfilePath();

        using FileStream stream = new FileStream(
            lockfilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );

        using StreamReader reader = new StreamReader(stream);

        return reader.ReadToEnd().Trim();
    }

    private static (string port, string password) GetCredentials()
    {
        string lockfile = ReadLockfile();

        string[] parts = lockfile.Split(':');

        if (parts.Length != 5)
        {
            throw new InvalidDataException("League lockfile has an unexpected format.");
        }

        string port = parts[2];
        string password = parts[3];

        return (port, password);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var (port, password) = GetCredentials();

        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{password}"));

        var request = new HttpRequestMessage(method, $"https://127.0.0.1:{port}{url}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        return request;
    }

    public static async Task<string> CacheLatestChampionDataAsync(
        CancellationToken cancellationToken = default
    )
    {
        string[]? versions = await DataDragonClient.GetFromJsonAsync<string[]>(
            $"{DataDragonBaseUrl}/api/versions.json",
            cancellationToken
        );

        if (versions is not { Length: > 0 } || !Version.TryParse(versions[0], out _))
        {
            throw new InvalidDataException("Data Dragon did not return a valid version.");
        }

        string version = versions[0];
        string cacheDirectory = GetDataDragonCacheDirectory(version);

        string catalogPath = Path.Combine(cacheDirectory, "champion.json");
        if (File.Exists(catalogPath))
            return catalogPath;

        string json = await DataDragonClient.GetStringAsync(
            $"{DataDragonBaseUrl}/cdn/{version}/data/en_US/champion.json",
            cancellationToken
        );

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out JsonElement champions)
            || champions.ValueKind != JsonValueKind.Object
            || !champions.EnumerateObject().MoveNext()
        )
        {
            throw new InvalidDataException(
                "Data Dragon returned an empty or invalid champion catalog."
            );
        }

        Directory.CreateDirectory(cacheDirectory);
        await WriteCacheFileAsync(catalogPath, Encoding.UTF8.GetBytes(json), cancellationToken);
        return catalogPath;
    }

    internal static async Task<string> CacheRoleCatalogAsync(
        string version,
        string relativeFile,
        CancellationToken cancellationToken
    )
    {
        string path = Path.Combine(GetDataDragonCacheDirectory(version), relativeFile);
        if (File.Exists(path))
            return path;

        string urlPath = string.Join('/', relativeFile.Split('/').Select(Uri.EscapeDataString));
        string json = await DataDragonClient.GetStringAsync(
            $"{DataDragonBaseUrl}/cdn/{version}/data/en_US/{urlPath}",
            cancellationToken
        );
        using var document = JsonDocument.Parse(json);
        if (
            !document.RootElement.TryGetProperty("data", out JsonElement data)
            || data.ValueKind != JsonValueKind.Object
        )
            throw new InvalidDataException("Data Dragon returned an invalid catalog.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteCacheFileAsync(path, Encoding.UTF8.GetBytes(json), cancellationToken);
        return path;
    }

    public static async Task<string> CacheChampionIconsAsync(
        string catalogPath,
        CancellationToken cancellationToken = default
    )
    {
        string json = await File.ReadAllTextAsync(catalogPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string? version = root.GetProperty("version").GetString();
        if (version is null || !Version.TryParse(version, out _))
            throw new InvalidDataException(
                "The champion catalog does not specify a valid version."
            );

        var iconFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
        foreach (JsonProperty champion in root.GetProperty("data").EnumerateObject())
        {
            string? iconFileName = champion
                .Value.GetProperty("image")
                .GetProperty("full")
                .GetString();
            if (
                string.IsNullOrWhiteSpace(iconFileName)
                || !iconFileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || iconFileName.IndexOfAny(invalidFileNameChars) >= 0
            )
            {
                throw new InvalidDataException($"Invalid icon filename for {champion.Name}.");
            }

            iconFileNames.Add(iconFileName);
        }

        string iconsDirectory = Path.Combine(GetDataDragonCacheDirectory(version), "icons");
        Directory.CreateDirectory(iconsDirectory);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 6,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(
            iconFileNames,
            options,
            async (iconFileName, token) =>
            {
                string iconPath = Path.Combine(iconsDirectory, iconFileName);
                if (File.Exists(iconPath))
                    return;

                string iconUrl =
                    $"{DataDragonBaseUrl}/cdn/{version}/img/champion/{Uri.EscapeDataString(iconFileName)}";
                byte[] iconBytes = await DataDragonClient.GetByteArrayAsync(iconUrl, token);
                await WriteCacheFileAsync(iconPath, iconBytes, token);
            }
        );

        return iconsDirectory;
    }

    private static string GetDataDragonCacheDirectory(string version)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "League Data Recorder",
            "DataDragon",
            version
        );
    }

    private static async Task WriteCacheFileAsync(
        string destinationPath,
        byte[] contents,
        CancellationToken cancellationToken
    )
    {
        string tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, contents, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, destinationPath);
        }
        catch (IOException) when (File.Exists(destinationPath))
        {
            // Another call finished saving this file first.
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    public async Task<string?> GetChampionSelectSessionAsync()
    {
        try
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                "/lol-champ-select/v1/session"
            );

            using HttpResponseMessage response = await _client.SendAsync(request);

            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"League Client API returned "
                        + $"HTTP {(int)response.StatusCode} "
                        + $"{response.StatusCode}.\r\n\r\n"
                        + responseText
                );
            }

            return responseText;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            return null;
        }
    }

    public async Task<string?> GetGameflowPhaseAsync()
    {
        try
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                "/lol-gameflow/v1/gameflow-phase"
            );

            using HttpResponseMessage response = await _client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine(
                    $"Gameflow API returned "
                        + $"{(int)response.StatusCode} "
                        + $"{response.StatusCode}"
                );

                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            return null;
        }
    }

    public async Task<string?> GetEndOfGameStatsAsync()
    {
        try
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                "/lol-end-of-game/v1/eog-stats-block"
            );

            using HttpResponseMessage response = await _client.SendAsync(request);

            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine(
                    $"End-of-game API returned "
                        + $"{(int)response.StatusCode} "
                        + $"{response.StatusCode}"
                );

                Debug.WriteLine(responseText);

                return null;
            }

            Debug.WriteLine("End of Game Text: " + responseText);

            return responseText;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            return null;
        }
    }

    private async Task<string?> GetReplayPathAsync()
    {
        try
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                "/lol-replays/v1/rofls/path"
            );

            using HttpResponseMessage response = await _client.SendAsync(request);

            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine(
                    $"Replay path API returned "
                        + $"{(int)response.StatusCode} "
                        + $"{response.StatusCode}"
                );

                Debug.WriteLine(responseText);

                return null;
            }

            return JsonSerializer.Deserialize<string>(responseText);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            return null;
        }
    }

    public async Task<string?> DownloadReplayAsync(
        long gameId,
        string destinationDirectory,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            //Get configured replay directory
            string? replayDirectory = await GetReplayPathAsync();

            if (string.IsNullOrWhiteSpace(replayDirectory))
            {
                Debug.WriteLine("Could not determine League replay directory.");
                return null;
            }

            using (
                HttpRequestMessage request = CreateRequest(
                    HttpMethod.Post,
                    $"/lol-replays/v1/rofls/{gameId}/download"
                )
            )
            {
                request.Content = JsonContent.Create(new { componentType = "replay-downloader" });

                using HttpResponseMessage response = await _client.SendAsync(
                    request,
                    cancellationToken
                );

                string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine(
                        $"Replay download API returned "
                            + $"{(int)response.StatusCode} "
                            + $"{response.StatusCode}"
                    );

                    Debug.WriteLine(responseText);

                    return null;
                }
            }

            Debug.WriteLine($"Replay download started for game {gameId}.");

            //Wait for the ROFL file to appear and stop changing size
            const int pollIntervalMs = 1000;
            const int timeoutSeconds = 120;

            long previousSize = -1;
            int stableChecks = 0;

            DateTime timeout = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            string? sourcePath = null;

            while (DateTime.UtcNow < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                sourcePath = Directory
                    .EnumerateFiles(replayDirectory, $"*{gameId}.rofl")
                    .FirstOrDefault();

                if (sourcePath is not null)
                {
                    long currentSize = new FileInfo(sourcePath).Length;

                    if (currentSize > 0 && currentSize == previousSize)
                    {
                        stableChecks++;

                        if (stableChecks >= 3)
                            break;
                    }
                    else
                    {
                        stableChecks = 0;
                    }

                    previousSize = currentSize;
                }

                await Task.Delay(pollIntervalMs, cancellationToken);
            }

            if (sourcePath is null || !File.Exists(sourcePath))
            {
                Debug.WriteLine($"Replay file was not found after download for game {gameId}.");

                return null;
            }

            if (stableChecks < 3)
            {
                Debug.WriteLine(
                    $"Timed out waiting for replay {gameId} " + $"to finish downloading."
                );

                return null;
            }

            Directory.CreateDirectory(destinationDirectory);

            string destinationPath = Path.Combine(
                destinationDirectory,
                Path.GetFileName(sourcePath)
            );

            File.Copy(sourcePath, destinationPath, overwrite: true);

            Debug.WriteLine($"Replay copied to: {destinationPath}");

            return destinationPath;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"Replay download cancelled for game {gameId}.");

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            return null;
        }
    }

    public Task<string?> GetMatchTimelineAsync(long gameId, CancellationToken token = default, Action<string>? log = null) =>
        GetOptionalMatchJsonAsync($"/lol-match-history/v1/game-timelines/{gameId}", token, log);

    public Task<string?> GetMatchDetailsAsync(long gameId, CancellationToken token = default, Action<string>? log = null) =>
        GetOptionalMatchJsonAsync($"/lol-match-history/v1/games/{gameId}", token, log);

    private static string MatchErrorSummary(string body)
    {
        // Report only error fields, never request credentials, headers or the
        // complete response (which may contain player/account information).
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return "";
            var fields = new List<string>();
            foreach (string key in new[] { "errorCode", "message" })
                if (json.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    string text = new(value.GetString()!.Where(c => !char.IsControl(c)).Take(300).ToArray());
                    if (text.Length > 0) fields.Add(key + "=" + text);
                }
            return fields.Count > 0 ? " · " + string.Join(" · ", fields) : "";
        }
        catch (JsonException) { return ""; }
    }
    private async Task<string?> GetOptionalMatchJsonAsync(string route, CancellationToken token, Action<string>? log)
    {
        void Report(string text) { Debug.WriteLine(text); log?.Invoke("Gold history: " + text); }
        try
        {
            using var request = CreateRequest(HttpMethod.Get, route);
            using var response = await _client.SendAsync(request, token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Report($"GET {route} returned HTTP {(int)response.StatusCode} {response.StatusCode}" + MatchErrorSummary(body));
                return null;
            }
            Report($"GET {route} returned HTTP {(int)response.StatusCode}.");
            return body;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (FileNotFoundException) { Report($"GET {route}: running League client could not be found. Open League and sign in."); return null; }
        catch (OperationCanceledException) { Report($"GET {route}: request timed out."); return null; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Report($"GET {route}: {ex.GetType().Name}: {ex.Message}"); return null; }
    }

    public async Task<string?> GetPostGameStatsAsync()
    {
        try
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                "/lol-end-of-game/v1/eog-stats-block"
            );

            using HttpResponseMessage response = await _client.SendAsync(request);

            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine(
                    $"Post-game stats API returned "
                        + $"{(int)response.StatusCode} "
                        + $"{response.StatusCode}"
                );

                Debug.WriteLine(responseText);

                return null;
            }

            return responseText;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            return null;
        }
    }

    public async Task<long?> GetCurrentGameIdAsync()
    {
        try
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                "/lol-gameflow/v1/session"
            );

            using HttpResponseMessage response = await _client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument document = JsonDocument.Parse(json);

            JsonElement root = document.RootElement;

            if (
                root.TryGetProperty("gameData", out JsonElement gameData)
                && gameData.TryGetProperty("gameId", out JsonElement gameIdElement)
                && gameIdElement.TryGetInt64(out long gameId)
            )
            {
                return gameId;
            }

            Debug.WriteLine("gameId not found in gameflow session.");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId
    );

    [DllImport(
        "kernel32.dll",
        EntryPoint = "QueryFullProcessImageNameW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        uint flags,
        StringBuilder executablePath,
        ref uint size
    );

    private static string GetExecutablePath(int processId)
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        using SafeProcessHandle handle = OpenProcess(
            PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            processId
        );

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var buffer = new StringBuilder(32768);
        uint size = (uint)buffer.Capacity;

        if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return buffer.ToString();
    }

    public async Task WatchSavedReplayAsync(long gameId, string savedReplayPath)
    {
        using var pathRequest = CreateRequest(HttpMethod.Get, "/lol-replays/v1/rofls/path");

        using var pathResponse = await _client.SendAsync(pathRequest);
        pathResponse.EnsureSuccessStatusCode();

        string? replayFolder = await pathResponse.Content.ReadFromJsonAsync<string>();
        if (string.IsNullOrWhiteSpace(replayFolder))
        {
            throw new InvalidOperationException("League did not return a replay folder.");
        }

        Directory.CreateDirectory(replayFolder);
        string clientReplayPath = Path.Combine(replayFolder, Path.GetFileName(savedReplayPath));

        if (!File.Exists(clientReplayPath))
        {
            await Task.Run(() => File.Copy(savedReplayPath, clientReplayPath));
        }

        using var scanRequest = CreateRequest(HttpMethod.Post, "/lol-replays/v1/rofls/scan");

        using var scanResponse = await _client.SendAsync(scanRequest);
        scanResponse.EnsureSuccessStatusCode();

        using var watchRequest = CreateRequest(
            HttpMethod.Post,
            $"/lol-replays/v1/rofls/{gameId}/watch"
        );

        watchRequest.Content = JsonContent.Create(
            new { componentType = "replay-button_match-history" }
        );

        using var watchResponse = await _client.SendAsync(watchRequest);
        if (!watchResponse.IsSuccessStatusCode)
        {
            string error = await watchResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"League returned HTTP {(int)watchResponse.StatusCode} while opening the replay.\n{error}"
            );
        }
    }
}

#endregion

#region In-game API

public class LeagueApi
{
    private readonly HttpClient _client;

    public LeagueApi()
    {
        var handler = new HttpClientHandler();

        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://127.0.0.1:2999"),
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public async Task<string?> GetAllGameDataAsync()
    {
        try
        {
            return await _client.GetStringAsync("/liveclientdata/allgamedata");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            return null;
        }
    }
}

#endregion
