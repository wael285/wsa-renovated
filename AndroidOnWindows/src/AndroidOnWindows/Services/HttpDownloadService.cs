using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using AndroidOnWindows.Models;

namespace AndroidOnWindows.Services;

/// <summary>
/// Resumable HTTP downloader. Critical on slow links: if the connection drops we
/// continue from the last byte using a Range request instead of restarting.
/// </summary>
public sealed class HttpDownloadService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };
        var c = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AndroidOnWindows/1.0 (Windows)");
        return c;
    }

    public async Task DownloadAsync(
        string url, string destPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default,
        int maxRetries = 8)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        var partPath = destPath + ".part";

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await DownloadOnceAsync(url, partPath, progress, ct);
                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(partPath, destPath);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < maxRetries)
            {
                Log.Warn($"download attempt {attempt}/{maxRetries} failed: {ex.Message}; resuming...");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 3)), ct);
            }
        }
    }

    private static async Task DownloadOnceAsync(
        string url, string partPath, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            req.Headers.Range = new RangeHeaderValue(existing, null);

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var resuming = resp.StatusCode == HttpStatusCode.PartialContent;
        if (existing > 0 && !resuming)
        {
            // Server ignored the range: start over.
            existing = 0;
        }

        long total = (resp.Content.Headers.ContentLength ?? 0) + existing;
        var mode = resuming ? FileMode.Append : FileMode.Create;

        await using var fs = new FileStream(partPath, mode, FileAccess.Write, FileShare.None, 1 << 20, true);
        await using var src = await resp.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[1 << 20];
        long done = existing;
        var name = Path.GetFileName(url);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = sw.ElapsedMilliseconds;
        long lastBytes = done;

        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;

            var now = sw.ElapsedMilliseconds;
            if (now - lastReport >= 250)
            {
                var bps = (done - lastBytes) / Math.Max(0.001, (now - lastReport) / 1000.0);
                lastReport = now;
                lastBytes = done;
                var p = new DownloadProgress(done, total, $"{name}  {Mb(done)} / {Mb(total)} MB  ({Bps(bps)})");
                progress?.Report(p);
            }
        }

        await fs.FlushAsync(ct);
        progress?.Report(new DownloadProgress(done, total, $"{name} complete"));
    }

    public static async Task<long> GetRemoteSizeAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return resp.Content.Headers.ContentLength ?? 0;
        }
        catch { return 0; }
    }

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0");
    private static string Bps(double bps) =>
        bps >= 1024 * 1024 ? $"{bps / 1024 / 1024:0.00} MB/s"
        : bps >= 1024 ? $"{bps / 1024:0.0} KB/s"
        : $"{bps:0} B/s";
}

/// <summary>Minimal zip extraction with a progress callback and zip-slip protection.</summary>
public static class ZipExtractor
{
    public static void Extract(string zipPath, string destDir, Action<int>? onPct = null)
    {
        Directory.CreateDirectory(destDir);
        using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var total = archive.Entries.Count;
        var i = 0;
        var destFull = Path.GetFullPath(destDir);

        foreach (var entry in archive.Entries)
        {
            i++;
            onPct?.Invoke(total == 0 ? 100 : (int)(i * 100.0 / total));

            if (string.IsNullOrEmpty(entry.Name))
            {
                // directory entry
                Directory.CreateDirectory(Path.Combine(destDir, entry.FullName));
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!target.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Blocked zip-slip entry: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
