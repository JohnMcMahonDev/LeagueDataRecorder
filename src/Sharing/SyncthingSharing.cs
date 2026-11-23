using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace League_Data_Recorder;
internal sealed class GameShareEntry
{
    public GameShareLink Link { get; set; } = null!;
    public bool Outbound { get; set; }
    public string? ImportedTo { get; set; }
    public bool Stopped { get; set; }
    public string Status { get; set; } = "Waiting for connection";
    public List<string> Recipients { get; set; } = [];
    public override string ToString() => $"{(Outbound ? "Send" : "Receive")} · {Link.Title} · {Status}";
}
internal sealed record PendingShareDevice(string DeviceId, string Name)
{
    public override string ToString() => $"{Name} · {DeviceId[..7]}…";
}
internal sealed class SyncthingSharing : IAsyncDisposable
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "League Data Recorder", "Sharing");
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? process;
    private HttpClient? api;
    internal string DeviceId { get; private set; } = "";
    internal List<GameShareEntry> Entries { get; } = PreferenceFiles.Load<List<GameShareEntry>>("game-shares.json");
    internal bool Running => process is { HasExited: false } && api is not null;
    private void Save() => PreferenceFiles.Save("game-shares.json", Entries);
    internal string Folder(GameShareEntry e) => Path.Combine(Root, e.Outbound ? "Outbox" : "Inbox", e.Link.FolderId);

    internal async Task StartAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (Running) return;
            string exe = AssetPaths.Resolve("runtime/syncthing/syncthing.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Install the Syncthing sharing engine from Downloads / notices.");
            string home = Path.Combine(Root, "Engine"); Directory.CreateDirectory(home);
            string configPath = Path.Combine(home, "config.xml");
            if (!File.Exists(configPath))
            {
                using var generation = NewProcess(exe, "generate", "--home=" + home);
                generation.Start();
                var stdout = generation.StandardOutput.ReadToEndAsync(ct); var stderr = generation.StandardError.ReadToEndAsync(ct);
                await generation.WaitForExitAsync(ct); await Task.WhenAll(stdout, stderr);
                if (generation.ExitCode != 0) throw new IOException("Syncthing could not initialize. See sharing-engine.log.");
            }
            var xml = XDocument.Load(configPath); var config = xml.Root!;
            var gui = config.Element("gui")!; gui.SetAttributeValue("enabled", "true"); gui.SetAttributeValue("tls", "false");
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            gui.SetElementValue("address", "127.0.0.1:" + port); gui.SetElementValue("apikey", key);
            var options = config.Element("options")!;
            options.SetElementValue("startBrowser", "false"); options.SetElementValue("urAccepted", "-1");
            options.SetElementValue("autoUpgradeIntervalH", "0"); options.SetElementValue("crashReportingEnabled", "false");
            options.Elements("listenAddress").Remove();
            options.Add(new XElement("listenAddress", "tcp://0.0.0.0:0"), new XElement("listenAddress", "quic://0.0.0.0:0"),
                new XElement("listenAddress", "dynamic+https://relays.syncthing.net/endpoint"));
            // A dedicated profile never shares Syncthing's generated default folder.
            var activeFolders = Entries.Where(e => !e.Stopped && e.Link.Expires > DateTimeOffset.UtcNow &&
                (e.Outbound || e.ImportedTo is null)).Select(e => e.Link.FolderId).ToHashSet(StringComparer.Ordinal);
            config.Elements("folder").Where(f => !activeFolders.Contains((string?)f.Attribute("id") ?? "")).Remove();
            string temp = configPath + ".tmp"; xml.Save(temp); File.Move(temp, configPath, true);
            process?.Dispose(); api?.Dispose();
            process = NewProcess(exe, "serve", "--home=" + home, "--no-browser", "--no-restart", "--no-upgrade", "--log-file=" + Path.Combine(Root, "sharing-engine.log"));
            process.StartInfo.RedirectStandardOutput = process.StartInfo.RedirectStandardError = false;
            process.Start();
            api = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(15) };
            api.DefaultRequestHeaders.Add("X-API-Key", key);
            for (int i = 0; i < 80; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException("Sharing engine exited. See " + Path.Combine(Root, "sharing-engine.log"));
                try
                {
                    var status = await GetAsync("/rest/system/status", ct);
                    DeviceId = status["myID"]!.GetValue<string>();
                    foreach (var e in Entries.Where(e => !e.Stopped))
                    {
                        if (e.Link.Expires <= DateTimeOffset.UtcNow) { await StopAsync(e, ct); e.Status = "Expired"; }
                        else if (!e.Outbound && e.ImportedTo is not null) await DeleteFolderAsync(e, ct);
                        else if (e.Outbound) { if (Directory.Exists(Folder(e))) await ConfigureFolderAsync(e, e.Recipients, ct); }
                        else if (e.ImportedTo is null) { await AddDeviceAsync(e.Link.DeviceId, ct); await ConfigureFolderAsync(e, [e.Link.DeviceId], ct); }
                    }
                    Save(); return;
                }
                catch (HttpRequestException) { await Task.Delay(250, ct); }
            }
            throw new IOException("Sharing engine did not become ready. Retry or check the engine log.");
        }
        catch
        {
            if (process is { HasExited: false }) process.Kill(true);
            api?.Dispose(); api = null; throw;
        }
        finally { gate.Release(); }
    }
    private static Process NewProcess(string exe, params string[] args)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return new Process { StartInfo = start };
    }
    private async Task<JsonNode> GetAsync(string path, CancellationToken ct) =>
        await api!.GetFromJsonAsync<JsonNode>(path, ct) ?? throw new InvalidDataException("Empty Syncthing response.");
    private async Task PostAsync(string path, JsonNode value, CancellationToken ct)
    {
        using var response = await api!.PostAsJsonAsync(path, value, ct); response.EnsureSuccessStatusCode();
    }
    private async Task AddDeviceAsync(string id, CancellationToken ct)
    {
        // Never overwrite an established device's settings or auto-accept other folders.
        var devices = (await GetAsync("/rest/config/devices", ct)).AsArray();
        if (devices.Any(d => d?["deviceID"]?.GetValue<string>() == id)) return;
        var device = await GetAsync("/rest/config/defaults/device", ct);
        device["deviceID"] = id; device["name"] = "League Recorder " + id[..7];
        device["autoAcceptFolders"] = false; device["introducer"] = false;
        await PostAsync("/rest/config/devices", device, ct);
    }
    private async Task ConfigureFolderAsync(GameShareEntry entry, IEnumerable<string> devices, CancellationToken ct)
    {
        string path = Folder(entry); Directory.CreateDirectory(path); Directory.CreateDirectory(Path.Combine(path, ".stfolder"));
        var folder = await GetAsync("/rest/config/defaults/folder", ct);
        folder["id"] = entry.Link.FolderId; folder["label"] = entry.Link.Title; folder["path"] = path;
        folder["type"] = entry.Outbound ? "sendonly" : "receiveonly";
        folder["devices"] = new JsonArray(devices.Append(DeviceId).Distinct().Select(id => (JsonNode)new JsonObject { ["deviceID"] = id }).ToArray());
        folder["ignorePerms"] = true; folder["fsWatcherEnabled"] = true;
        await PostAsync("/rest/config/folders", folder, ct);
    }
    internal async Task<GameShareEntry> CreateAsync(string source, IProgress<string>? progress, CancellationToken ct)
    {
        await StartAsync(ct);
        string id = "ldr-" + Guid.NewGuid().ToString("N"); string path = Path.Combine(Root, "Outbox", id);
        try
        {
            var package = await SharePackage.CreateAsync(source, path, progress, ct);
            var entry = new GameShareEntry { Outbound = true, Link = new(1, DeviceId, id,
                "Game " + Path.GetFileName(source).Split('_')[1], package.Hash, package.Bytes, DateTimeOffset.UtcNow.AddDays(7)), Status = "Awaiting recipient approval" };
            await ConfigureFolderAsync(entry, [], ct); Entries.Add(entry); Save(); return entry;
        }
        catch { if (Directory.Exists(path)) Directory.Delete(path, true); throw; }
    }
    internal async Task<GameShareEntry> ReceiveAsync(GameShareLink link, CancellationToken ct)
    {
        await StartAsync(ct);
        if (link.DeviceId == DeviceId) throw new InvalidDataException("This link belongs to this computer. Open it on the other computer.");
        var existing = Entries.FirstOrDefault(e => e.Link.FolderId == link.FolderId);
        if (existing is not null)
        {
            if (existing.Link != link) throw new InvalidDataException("Conflicting share invitation.");
            if (existing.Stopped) throw new InvalidOperationException("This download was stopped. Ask for a new share link.");
            return existing;
        }
        var entry = new GameShareEntry { Link = link, Status = "Waiting for sender approval · code " + link.PairCode(DeviceId) };
        await AddDeviceAsync(link.DeviceId, ct); await ConfigureFolderAsync(entry, [link.DeviceId], ct);
        Entries.Add(entry); Save(); return entry;
    }
    internal async Task<PendingShareDevice[]> PendingAsync(CancellationToken ct)
    {
        var pending = await GetAsync("/rest/cluster/pending/devices", ct);
        return pending.AsObject().Select(p => new PendingShareDevice(p.Key,
            p.Value?["name"]?.GetValue<string>() ?? "Unrecognized device")).ToArray();
    }
    internal async Task ApproveAsync(GameShareEntry entry, string device, CancellationToken ct)
    {
        if (!entry.Outbound || entry.Stopped || entry.Link.Expires <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Select an active outgoing share.");
        await AddDeviceAsync(device, ct);
        if (!entry.Recipients.Contains(device)) entry.Recipients.Add(device);
        await ConfigureFolderAsync(entry, entry.Recipients, ct); entry.Status = "Shared with " + entry.Recipients.Count + " approved device(s)"; Save();
    }
    private async Task DeleteFolderAsync(GameShareEntry entry, CancellationToken ct)
    {
        using var response = await api!.DeleteAsync("/rest/config/folders/" + entry.Link.FolderId, ct);
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }
    internal async Task StopAsync(GameShareEntry entry, CancellationToken ct)
    {
        await DeleteFolderAsync(entry, ct); entry.Stopped = true; entry.Status = entry.Outbound ? "Sharing revoked" : "Download stopped"; Save();
        // Remove only our snapshot/inbox, never the source or imported game copy.
        if (Directory.Exists(Folder(entry))) Directory.Delete(Folder(entry), true);
    }
    internal async Task<int> PollAsync(string recordings, CancellationToken ct)
    {
        int imported = 0;
        foreach (var entry in Entries.Where(e => !e.Stopped).ToArray())
        {
            if (entry.Link.Expires <= DateTimeOffset.UtcNow) { await StopAsync(entry, ct); entry.Status = "Expired"; continue; }
            if (entry.Outbound || entry.ImportedTo is not null) continue;
            var status = await GetAsync("/rest/db/status?folder=" + entry.Link.FolderId, ct);
            long total = status["globalBytes"]?.GetValue<long>() ?? 0, remaining = status["needBytes"]?.GetValue<long>() ?? 0;
            long errors = status["pullErrors"]?.GetValue<long>() ?? 0;
            if (total > entry.Link.Bytes + 8_000_000)
            {
                await StopAsync(entry, ct); entry.Status = "Stopped: offered data exceeds the invitation size"; Save(); continue;
            }
            entry.Status = total > 0 ? $"{Math.Clamp(100d * (total - remaining) / total, 0, 100):0}% downloaded" : "Waiting for sender approval · code " + entry.Link.PairCode(DeviceId);
            if (errors > 0) entry.Status += " · transfer errors; will retry";
            if (string.IsNullOrWhiteSpace(recordings)) { entry.Status += " · choose a recordings folder to import"; continue; }
            if (total == 0 || errors != 0 || (status["needTotalItems"]?.GetValue<long>() ?? 1) != 0 || status["state"]?.GetValue<string>() != "idle" || !File.Exists(Path.Combine(Folder(entry), "manifest.json"))) continue;
            entry.Status = "Verifying downloaded game…";
            try
            {
                entry.ImportedTo = await SharePackage.ImportAsync(entry.Link, Folder(entry), recordings, ct);
                entry.Status = "Imported into game history"; Save(); imported++;
                // Stop syncing after a verified import; the independent recording remains.
                await DeleteFolderAsync(entry, ct);
            }
            catch (IOException ex) { entry.Status = "Import needs attention: " + ex.Message; }
        }
        Save(); return imported;
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Running)
            {
                using var response = await api!.PostAsync("/rest/system/shutdown", null);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await process!.WaitForExitAsync(timeout.Token);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException) { }
        finally { if (process is { HasExited: false }) process.Kill(true); process?.Dispose(); api?.Dispose(); api = null; }
    }
}
