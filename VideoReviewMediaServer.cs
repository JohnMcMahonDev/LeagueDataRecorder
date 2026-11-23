using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace League_Data_Recorder;

// Stream on background I/O tasks, instead of delivering MP4/WAV bytes through
// WebResourceRequested and a managed COM stream on the WinForms UI thread.
// Bind only to loopback; an unguessable per-player URL exposes just the selected
// recording and the three embedded player assets, never a directory or input path.
internal sealed class VideoReviewMediaServer : IDisposable
{
    private sealed record Selection(string Prefix, string? Video, string? Audio);
    private readonly TcpListener listener;
    private readonly CancellationTokenSource cancellation = new();
    private readonly SemaphoreSlim connectionSlots = new(12, 12);
    private readonly ConcurrentDictionary<TcpClient, byte> clients = new();
    private readonly string root = "/" + Guid.NewGuid().ToString("N");
    private readonly Task acceptTask;
    private Selection? selection;
    private int disposed;

    internal string Origin { get; }
    internal string PageUri => Origin + root + "/player.html";
    internal string AssetPrefix => root + "/";

    internal VideoReviewMediaServer(int preferredPort = 0)
    {
        listener = new(IPAddress.Loopback, preferredPort is > 0 and <= 65535 ? preferredPort : 0);
        try { listener.Start(); }
        catch (SocketException) when (preferredPort != 0)
        {
            listener.Stop();
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
        }
        Origin = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
        acceptTask = AcceptAsync();
    }

    internal string SelectMedia(int revision, string? video, string? audio)
    {
        string prefix = root + "/media/" + revision.ToString(CultureInfo.InvariantCulture) + "/";
        Volatile.Write(ref selection, new(prefix, video, audio));
        return Origin + prefix;
    }

    private async Task AcceptAsync()
    {
        CancellationToken token = cancellation.Token;
        try
        {
            while (true)
            {
                await connectionSlots.WaitAsync(token).ConfigureAwait(false);
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false); }
                catch { connectionSlots.Release(); throw; }
                clients.TryAdd(client, 0);
                _ = ServeAndCloseAsync(client, token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        { if (!token.IsCancellationRequested) Debug.WriteLine("Video review listener: " + ex.Message); }
    }

    private async Task ServeAndCloseAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            client.NoDelay = true;
            using NetworkStream stream = client.GetStream();
            string request;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                request = await ReadHeadersAsync(stream, timeout.Token).ConfigureAwait(false);
            }
            string[] lines = request.Split("\r\n", StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length != 3 || first[2] is not ("HTTP/1.1" or "HTTP/1.0"))
            { await HeadersAsync(stream, 400, "Bad Request", 0, null, null, token); return; }
            bool head = first[0] == "HEAD";
            if (!head && first[0] != "GET")
            { await HeadersAsync(stream, 405, "Method Not Allowed", 0, null, "Allow: GET, HEAD\r\n", token); return; }
            string? host = null, range = null;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string name = lines[i][..colon];
                string value = lines[i][(colon + 1)..].Trim();
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    if (host != null) throw new InvalidDataException("Duplicate Host header.");
                    host = value;
                }
                if (name.Equals("Range", StringComparison.OrdinalIgnoreCase))
                {
                    if (range != null) throw new InvalidDataException("Duplicate Range header.");
                    range = value;
                }
            }
            if (!string.Equals(host, new Uri(Origin).Authority, StringComparison.OrdinalIgnoreCase))
            { await HeadersAsync(stream, 403, "Forbidden", 0, null, null, token); return; }
            string path = first[1].Split('?', 2)[0];
            // Support both relative and root-relative links in existing player
            // assets. These three public static files contain no recording data.
            string? asset = path == root + "/player.html" || path == "/player.html" ? "player.html" :
                path == root + "/player.css" || path == "/player.css" ? "player.css" :
                path == root + "/player.js" || path == "/player.js" ? "player.js" :
                path == root + "/replay-audio.html" ? "replay-audio.html" :
                path == root + "/replay-audio.js" || path == "/replay-audio.js" ? "replay-audio.js" : null;
            if (asset != null)
            {
                using Stream? content = Assembly.GetExecutingAssembly().GetManifestResourceStream("VideoReview." + asset);
                if (content == null)
                { await HeadersAsync(stream, 404, "Not Found", 0, null, null, token); return; }
                string mime = asset.EndsWith(".html", StringComparison.Ordinal) ? "text/html; charset=utf-8" :
                    asset.EndsWith(".css", StringComparison.Ordinal) ? "text/css; charset=utf-8" : "text/javascript; charset=utf-8";
                await HeadersAsync(stream, 200, "OK", content.Length, mime, null, token);
                if (!head) await content.CopyToAsync(stream, token).ConfigureAwait(false);
                return;
            }

            Selection? current = Volatile.Read(ref selection);
            string? file = current == null ? null : path == current.Prefix + "video.mp4" ? current.Video :
                path == current.Prefix + "audio.wav" ? current.Audio : null;
            if (file == null || !File.Exists(file))
            { await HeadersAsync(stream, 404, "Not Found", 0, null, null, token); return; }
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long length = input.Length;
            if (!VideoReviewByteRange.TryParse(range ?? "", length, out long start, out long count))
            {
                await HeadersAsync(stream, 416, "Range Not Satisfiable", 0, null,
                    "Content-Range: bytes */" + length.ToString(CultureInfo.InvariantCulture) + "\r\n", token);
                return;
            }
            bool partial = range != null;
            string extra = "Accept-Ranges: bytes\r\n";
            if (partial) extra += "Content-Range: bytes " + start.ToString(CultureInfo.InvariantCulture) + "-" +
                (start + count - 1).ToString(CultureInfo.InvariantCulture) + "/" + length.ToString(CultureInfo.InvariantCulture) + "\r\n";
            string type = path.EndsWith(".mp4", StringComparison.Ordinal) ? "video/mp4" : "audio/wav";
            await HeadersAsync(stream, partial ? 206 : 200, partial ? "Partial Content" : "OK", count, type, extra, token);
            if (head) return;
            input.Position = start;
            byte[] buffer = new byte[131072];
            while (count > 0)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(count, buffer.Length)), token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("The selected media changed while streaming.");
                await stream.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                count -= read;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or
            ObjectDisposedException or UnauthorizedAccessException)
        { Debug.WriteLine("Video review request: " + ex.Message); }
        finally
        {
            clients.TryRemove(client, out _);
            client.Dispose();
            connectionSlots.Release();
        }
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken token)
    {
        byte[] buffer = new byte[16384];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(count, buffer.Length - count), token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            count += read;
            // Browsers send GET/HEAD without a body. Stop at the header terminator.
            string text = Encoding.ASCII.GetString(buffer, 0, count);
            int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0) return text[..end];
        }
        throw new InvalidDataException("Video review request headers are too large.");
    }

    private static Task HeadersAsync(NetworkStream stream, int status, string reason, long length,
        string? contentType, string? extra, CancellationToken token)
    {
        string headers = "HTTP/1.1 " + status.ToString(CultureInfo.InvariantCulture) + " " + reason + "\r\n" +
            "Content-Length: " + length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n" +
            "Cache-Control: private, no-store\r\nX-Content-Type-Options: nosniff\r\n" +
            (contentType == null ? "" : "Content-Type: " + contentType + "\r\n") + extra + "\r\n";
        return stream.WriteAsync(Encoding.ASCII.GetBytes(headers), token).AsTask();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancellation.Cancel();
        listener.Stop();
        foreach (TcpClient client in clients.Keys) client.Dispose();
        // Do not block the UI waiting for outstanding browser reads to cancel.
        _ = acceptTask;
    }
}

internal static class VideoReviewByteRange
{
    internal static bool TryParse(string header, long length, out long start, out long count)
    {
        start = 0; count = length;
        if (header.Length == 0) return length >= 0;
        if (length <= 0 || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || header.Contains(',')) return false;
        string[] parts = header[6..].Trim().Split('-');
        if (parts.Length != 2) return false;
        bool Parse(string value, out long n) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n);
        if (parts[0].Length == 0)
        {
            if (!Parse(parts[1], out long suffix) || suffix <= 0) return false;
            count = Math.Min(suffix, length); start = length - count; return true;
        }
        if (!Parse(parts[0], out start) || start >= length) return false;
        long end = length - 1;
        if (parts[1].Length > 0 && (!Parse(parts[1], out end) || end < start)) return false;
        end = Math.Min(end, length - 1);
        count = end - start + 1;
        return true;
    }
}
