using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace League_Data_Recorder;
internal sealed record SharedFile(string Name, long Length, string Sha256, DateTime LastWriteUtc);
internal sealed record ShareManifest(int Version, string GameId, SharedFile[] Files);
internal sealed record GameShareLink(int Version, string DeviceId, string FolderId, string Title, string ManifestHash, long Bytes, DateTimeOffset Expires)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal string Encode() => "leaguerecorder://share/" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this, Json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static GameShareLink Parse(string value)
    {
        if (value.Length > 8192 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "leaguerecorder" || uri.Host != "share" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Paste a League Recorder game share link.");
        string payload = uri.AbsolutePath.TrimStart('/');
        if (!Regex.IsMatch(payload, "^[A-Za-z0-9_-]+$")) throw new InvalidDataException("Invalid share link encoding.");
        string encoded = payload.Replace('-', '+').Replace('_', '/');
        var link = JsonSerializer.Deserialize<GameShareLink>(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')), Json)
            ?? throw new InvalidDataException("Empty share link.");
        if (link.Version != 1 || !Regex.IsMatch(link.DeviceId ?? "", "^[A-Z2-7]{7}(-[A-Z2-7]{7}){7}$") ||
            !Regex.IsMatch(link.FolderId ?? "", "^ldr-[a-f0-9]{32}$") || !Regex.IsMatch(link.ManifestHash ?? "", "^[A-Fa-f0-9]{64}$") ||
            string.IsNullOrWhiteSpace(link.Title) || link.Title.Length > 120 || link.Title.Any(char.IsControl) || link.Bytes <= 0 || link.Bytes > 4_000_000_000_000L ||
            link.Expires <= DateTimeOffset.UtcNow || link.Expires > DateTimeOffset.UtcNow.AddDays(31))
            throw new InvalidDataException("This share link is invalid or expired. Ask for a new link.");
        return link;
    }
    internal string PairCode(string otherDevice)
    {
        string pair = string.Join("|", new[] { DeviceId, otherDevice }.Order(StringComparer.Ordinal)) + "|" + FolderId;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair)));
        return hash[..4] + " " + hash[4..8];
    }
}
internal static class SharePackage
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".json", ".mp4", ".wav", ".rttm", ".rofl", ".txt", ".png", ".jpg" };
    internal static bool SafeName(string name) => !string.IsNullOrEmpty(name) && name.Length <= 180 && name == Path.GetFileName(name) &&
        !name.Contains(':') && !name.Contains('\\') && !name.Contains('/') && name is not "." and not ".." &&
        !name.EndsWith(' ') && !name.EndsWith('.') && !name.Any(c => c < 32 || "<>\"|?*".Contains(c)) &&
        !Regex.IsMatch(name, "^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\\.|$)", RegexOptions.IgnoreCase);
    internal static async Task<(string Hash, long Bytes)> CreateAsync(string gameFolder, string destination, IProgress<string>? progress, CancellationToken ct)
    {
        var match = Regex.Match(Path.GetFileName(gameFolder), @"^Game_(\d+)_");
        if (!match.Success) throw new InvalidDataException("Select a saved game from history.");
        string id = match.Groups[1].Value;
        foreach (string required in new[] { "_ParsedGameData.json", "_ParsedPostGameData.json" })
            if (!File.Exists(Path.Combine(gameFolder, "Game_" + id + required))) throw new InvalidDataException("This game is not completely saved yet.");
        Directory.CreateDirectory(Path.Combine(destination, "game"));
        var files = new List<SharedFile>();
        var sources = Directory.EnumerateFiles(gameFolder).ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.OrdinalIgnoreCase);
        // Transcripts normally live beside Recordings, not inside the game.
        // Flatten this game's sidecars into the portable package; the existing
        // reader already supports game-local transcripts and their role/emotion files.
        var directory = new DirectoryInfo(gameFolder);
        if (directory.Parent?.Name.Equals("Recordings", StringComparison.OrdinalIgnoreCase) == true && directory.Parent.Parent is { } projectRoot)
        {
            foreach (string area in new[] { "Analysis", "Transcriptions" })
            {
                string sidecars = Path.Combine(projectRoot.FullName, area, directory.Name);
                if (!Directory.Exists(sidecars)) continue;
                if ((File.GetAttributes(sidecars) & FileAttributes.ReparsePoint) != 0 ||
                    (File.GetAttributes(Path.GetDirectoryName(sidecars)!) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked game sidecar folders cannot be shared.");
                foreach (string source in Directory.EnumerateFiles(sidecars)) sources[Path.GetFileName(source)] = source;
            }
        }
        foreach (string source in sources.Values.Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            string name = Path.GetFileName(source);
            if (!Extensions.Contains(Path.GetExtension(name)) || !SafeName(name)) continue;
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked files cannot be shared.");
            if (files.Count >= 10000) throw new InvalidDataException("Too many files in this game.");
            progress?.Report("Preparing " + name + "…");
            // Opening without write sharing refuses active recordings/writers.
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            await using var output = new FileStream(Path.Combine(destination, "game", name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128 * 1024]; int count; long length = 0;
            while ((count = await input.ReadAsync(buffer, ct)) > 0)
            { await output.WriteAsync(buffer.AsMemory(0, count), ct); hash.AppendData(buffer, 0, count); length += count; }
            await output.FlushAsync(ct); output.Close();
            DateTime lastWrite = File.GetLastWriteTimeUtc(source);
            File.SetLastWriteTimeUtc(Path.Combine(destination, "game", name), lastWrite);
            files.Add(new(name, length, Convert.ToHexString(hash.GetHashAndReset()), lastWrite));
        }
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new ShareManifest(1, id, files.ToArray()), GameShareLink.Json);
        await File.WriteAllBytesAsync(Path.Combine(destination, "manifest.json"), manifest, ct);
        return (Convert.ToHexString(SHA256.HashData(manifest)), files.Sum(f => f.Length));
    }
    internal static async Task<string> ImportAsync(GameShareLink link, string inbox, string recordings, CancellationToken ct)
    {
        string manifestPath = Path.Combine(inbox, "manifest.json");
        if ((File.GetAttributes(inbox) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked manifest or inbox is not allowed.");
        if (new FileInfo(manifestPath).Length > 8_000_000) throw new InvalidDataException("Oversized game manifest.");
        byte[] bytes = await File.ReadAllBytesAsync(manifestPath, ct);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(link.ManifestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The game manifest does not match the share link.");
        var manifest = JsonSerializer.Deserialize<ShareManifest>(bytes, GameShareLink.Json);
        if (manifest is null || manifest.Version != 1 || !Regex.IsMatch(manifest.GameId ?? "", "^[0-9]{1,19}$") ||
            !long.TryParse(manifest.GameId, out long gameId) || gameId <= 0 || manifest.Files is null || manifest.Files.Length is < 1 or > 10000)
            throw new InvalidDataException("Invalid game manifest.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null || !SafeName(file.Name) || !Extensions.Contains(Path.GetExtension(file.Name)) || !names.Add(file.Name) || file.Length < 0 ||
                file.Length > link.Bytes || file.LastWriteUtc.Year < 1601 || !Regex.IsMatch(file.Sha256 ?? "", "^[A-Fa-f0-9]{64}$")) throw new InvalidDataException("Invalid game file entry.");
            total = checked(total + file.Length);
        }
        if (total != link.Bytes || !names.Contains($"Game_{gameId}_ParsedGameData.json") || !names.Contains($"Game_{gameId}_ParsedPostGameData.json"))
            throw new InvalidDataException("Game data is incomplete.");
        // Deterministic destination makes retries idempotent. Publish a whole directory
        // atomically only after every file is copied and verified. Never sync this copy.
        Directory.CreateDirectory(recordings);
        string destination = Path.Combine(recordings, $"Game_{gameId}_shared-{link.FolderId[4..]}");
        if (Directory.Exists(destination)) return destination;
        string stage = Path.Combine(recordings, ".incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var file in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                string source = Path.Combine(inbox, "game", file.Name);
                if ((File.GetAttributes(Path.Combine(inbox, "game")) & FileAttributes.ReparsePoint) != 0 ||
                    (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 || new FileInfo(source).Length != file.Length)
                    throw new InvalidDataException("Incomplete or linked game file: " + file.Name);
                await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
                await using var output = new FileStream(Path.Combine(stage, file.Name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[128 * 1024]; int count; long length = 0;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                { await output.WriteAsync(buffer.AsMemory(0, count), ct); hash.AppendData(buffer, 0, count); length += count; }
                if (length != file.Length || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Game file checksum failed: " + file.Name);
                await output.FlushAsync(ct); output.Close();
                File.SetLastWriteTimeUtc(Path.Combine(stage, file.Name), file.LastWriteUtc);
            }
            ct.ThrowIfCancellationRequested(); Directory.Move(stage, destination); return destination;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
