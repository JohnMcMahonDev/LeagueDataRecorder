using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace League_Data_Recorder;

internal static class AssetPaths
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "League Data Recorder", "Assets");
    internal static string Resolve(string relative)
    {
        if (Path.IsPathRooted(relative)) return relative;
        string bundled = Path.GetFullPath(relative, AppContext.BaseDirectory);
        return File.Exists(bundled) ? bundled : Path.GetFullPath(relative, Root);
    }
}
internal sealed record DownloadAsset(string Id, string Name, string Path, string Url, long Size, string Hash, string Algorithm, string Anchor);
internal sealed record DownloadProgress(string Message, long Completed = 0, long Total = 0);

internal static class AssetDownloads
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HttpClient Http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };
    internal static readonly DownloadAsset[] Catalog = Load();
    private static DownloadAsset[] Load()
    {
        using var stream = typeof(AssetDownloads).Assembly.GetManifestResourceStream("download-assets.json")!;
        return JsonSerializer.Deserialize<DownloadAsset[]>(stream) ?? throw new InvalidDataException("Missing asset catalog.");
    }
    internal static bool MissingStartupAssets => Catalog.Where(a => a.Id != "syncthing").Any(a => !File.Exists(AssetPaths.Resolve(a.Path)));
    internal static async Task EnsureAsync(IEnumerable<string> ids, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            foreach (var asset in Catalog.Where(a => ids.Contains(a.Id)))
            {
                if (File.Exists(AssetPaths.Resolve(asset.Path))) { progress?.Report(new(asset.Name + " is installed.")); continue; }
                Directory.CreateDirectory(AssetPaths.Root);
                string part = Path.Combine(AssetPaths.Root, asset.Id + ".download.part");
                await DownloadAsync(asset, part, progress, ct).ConfigureAwait(false);
                progress?.Report(new("Verifying " + asset.Name + "…"));
                await using (var file = File.OpenRead(part))
                {
                    using var hash = IncrementalHash.CreateHash(new HashAlgorithmName(asset.Algorithm));
                    byte[] buffer = new byte[128 * 1024]; int read;
                    while ((read = await file.ReadAsync(buffer, ct)) > 0) hash.AppendData(buffer, 0, read);
                    if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Hash, StringComparison.OrdinalIgnoreCase))
                    { file.Close(); File.Delete(part); throw new InvalidDataException(asset.Name + " checksum mismatch. Retry to download a fresh copy."); }
                }
                string target = Path.Combine(AssetPaths.Root, asset.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                ct.ThrowIfCancellationRequested();
                if (asset.Anchor.Length == 0) File.Move(part, target, true);
                else
                {
                    progress?.Report(new("Installing " + asset.Name + "…"));
                    InstallArchive(part, target, asset.Anchor, ct);
                    File.Delete(part);
                }
                progress?.Report(new(asset.Name + " installed.", asset.Size, asset.Size));
            }
        }
        finally { Gate.Release(); }
    }

    internal static void InstallArchive(string archive, string target, string anchorPath, CancellationToken ct)
    {
        if (!target.Replace('\\', '/').EndsWith("/" + anchorPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Asset destination does not match its archive layout.");
        string stage = Path.Combine(Path.GetDirectoryName(target)!, "extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            ZipFile.ExtractToDirectory(archive, stage);
            string anchor = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Single(f =>
                f.Replace('\\', '/').EndsWith("/" + anchorPath, StringComparison.OrdinalIgnoreCase));
            string sourceRoot = anchor[..^anchorPath.Length].TrimEnd(Path.DirectorySeparatorChar);
            string destinationRoot = target[..^anchorPath.Length].TrimEnd(Path.DirectorySeparatorChar);
            // Publish the executable last, after its libraries and license files.
            foreach (string source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).OrderBy(f => f == anchor ? 1 : 0))
            {
                ct.ThrowIfCancellationRequested();
                string destination = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination, true);
            }
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    private static async Task DownloadAsync(DownloadAsset asset, string part, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        long offset = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (offset == asset.Size) return;
        if (offset > asset.Size) { File.Delete(part); offset = 0; }
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        request.Headers.UserAgent.ParseAdd("LeagueDataRecorder/1.0");
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange?.From != offset) throw new InvalidDataException("Unexpected download range.");
        }
        else offset = 0;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(part, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 128 * 1024, true);
        byte[] buffer = new byte[128 * 1024]; int count; long completed = offset;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while ((count = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (completed + count > asset.Size) throw new InvalidDataException("Download exceeded its expected size.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false); completed += count;
            if (clock.ElapsedMilliseconds >= 250) { progress?.Report(new("Downloading " + asset.Name, completed, asset.Size)); clock.Restart(); }
        }
        if (completed != asset.Size) throw new IOException("Incomplete download; Retry will resume it.");
    }
}
