using System.Xml.Linq;
using AndroidOnWindows.Models;

namespace AndroidOnWindows.Services;

/// <summary>
/// Reads Google's public SDK repository XML so we can download the exact zips we need
/// (platform-tools, emulator, system images) without Android Studio, sdkmanager or Java.
/// </summary>
public sealed class AndroidRepository
{
    public const string BaseUrl = "https://dl.google.com/android/repository/";

    private const string RepoIndexUrl = BaseUrl + "repository2-3.xml";
    private const string PlayImageIndexUrl = BaseUrl + "sys-img/google_apis_playstore/sys-img2-3.xml";
    private const string GoogleApisImageIndexUrl = BaseUrl + "sys-img/google_apis/sys-img2-3.xml";

    private readonly HttpDownloadService _dl;

    public AndroidRepository(HttpDownloadService dl) => _dl = dl;

    public sealed record PackageArchive(string Path, string Url, long Size, string? HostOs);

    private static async Task<XDocument> LoadXmlAsync(HttpDownloadService dl, string url, CancellationToken ct)
    {
        var cache = Path.Combine(AppPaths.CacheDir, "xml", Path.GetFileName(url));
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        // Refresh indices at most once a day.
        if (!File.Exists(cache) || DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) > TimeSpan.FromDays(1))
        {
            try { await dl.DownloadAsync(url, cache, null, ct); }
            catch (Exception ex)
            {
                Log.Warn($"could not refresh {url}: {ex.Message}");
                if (!File.Exists(cache)) throw;
            }
        }
        return XDocument.Load(cache);
    }

    private static IEnumerable<XElement> Packages(XDocument doc)
        => doc.Descendants().Where(e => e.Name.LocalName == "remotePackage");

    private static PackageArchive? ArchiveFor(XElement pkg, string? hostOs)
    {
        foreach (var arch in pkg.Descendants().Where(e => e.Name.LocalName == "archive"))
        {
            var host = arch.Descendants().FirstOrDefault(e => e.Name.LocalName == "host-os")?.Value;
            if (hostOs is not null && host is not null && !host.Equals(hostOs, StringComparison.OrdinalIgnoreCase))
                continue;
            var complete = arch.Descendants().FirstOrDefault(e => e.Name.LocalName == "complete");
            var url = complete?.Descendants().FirstOrDefault(e => e.Name.LocalName == "url")?.Value
                      ?? arch.Descendants().FirstOrDefault(e => e.Name.LocalName == "url")?.Value;
            if (string.IsNullOrWhiteSpace(url)) continue;
            var sizeStr = complete?.Descendants().FirstOrDefault(e => e.Name.LocalName == "size")?.Value
                          ?? arch.Descendants().FirstOrDefault(e => e.Name.LocalName == "size")?.Value;
            long.TryParse(sizeStr, out var size);
            return new PackageArchive(pkg.Attribute("path")?.Value ?? "", url!, size, host);
        }
        return null;
    }

    public async Task<PackageArchive> GetWindowsPackageAsync(string path, CancellationToken ct = default)
    {
        var doc = await LoadXmlAsync(_dl, RepoIndexUrl, ct);
        var pkg = Packages(doc).FirstOrDefault(p => p.Attribute("path")?.Value == path)
                  ?? throw new InvalidOperationException($"Package '{path}' not found in repository.");
        return ArchiveFor(pkg, "windows")
               ?? ArchiveFor(pkg, null)
               ?? throw new InvalidOperationException($"No Windows archive for '{path}'.");
    }

    public async Task<List<SystemImageOption>> GetPlaySystemImagesAsync(CancellationToken ct = default)
    {
        var results = new List<SystemImageOption>();
        await AddImagesAsync(PlayImageIndexUrl, results, ct);
        if (results.Count == 0)
            await AddImagesAsync(GoogleApisImageIndexUrl, results, ct);

        // Prefer x86_64 (fast on Intel with WHPX/Hyper-V); newest APIs first.
        return results
            .Where(r => r.Abi.Contains("x86_64", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.PackagePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(r => r.SortKey)
            .ToList();
    }

    private async Task AddImagesAsync(string indexUrl, List<SystemImageOption> results, CancellationToken ct)
    {
        try
        {
            var doc = await LoadXmlAsync(_dl, indexUrl, ct);
            foreach (var pkg in Packages(doc))
            {
                var path = pkg.Attribute("path")?.Value;
                if (string.IsNullOrWhiteSpace(path)) continue;
                // path = system-images;android-34;google_apis_playstore;x86_64
                var parts = path.Split(';');
                if (parts.Length != 4) continue;
                var abi = parts[3];
                if (!abi.Contains("x86_64", StringComparison.OrdinalIgnoreCase)) continue;
                var api = parts[1].Replace("android-", "");
                // Skip canary/preview channels and anything not a numeric API level.
                if (!double.TryParse(api, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _)) continue;

                // System-image archives can be per-host (some APIs ship -windows/-darwin/-linux
                // zips). Prefer the Windows archive; generic archives have no host-os and pass.
                var arch = ArchiveFor(pkg, "windows") ?? ArchiveFor(pkg, null);
                if (arch is null) continue;

                results.Add(new SystemImageOption
                {
                    ApiLevel = api,
                    Abi = abi,
                    Tag = parts[2],
                    PackagePath = path,
                    Url = arch.Url.StartsWith("http") ? arch.Url : BaseUrl + arch.Url,
                    SizeBytes = arch.Size
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"system image index {indexUrl} failed: {ex.Message}");
        }
    }
}
