using AndroidOnWindows.Models;

namespace AndroidOnWindows.Services;

/// <summary>
/// Downloads and installs the minimum Android SDK bits directly from Google's
/// archives: platform-tools (adb), the emulator, and one Play-enabled system image.
/// No Android Studio, no sdkmanager, no Java required.
/// </summary>
public sealed class SdkBootstrapper
{
    private readonly HttpDownloadService _dl;
    private readonly AndroidRepository _repo;

    public SdkBootstrapper(HttpDownloadService dl, AndroidRepository repo)
    {
        _dl = dl;
        _repo = repo;
    }

    public bool PlatformToolsReady => File.Exists(AppPaths.Adb);
    public bool EmulatorReady => File.Exists(AppPaths.Emulator);

    public bool SystemImageReady(string packagePath) =>
        File.Exists(Path.Combine(AppPaths.SystemImageDir(packagePath), "system.img")) ||
        File.Exists(Path.Combine(AppPaths.SystemImageDir(packagePath), "package.xml"));

    public async Task<List<SystemImageOption>> GetSystemImagesAsync(CancellationToken ct = default)
        => await _repo.GetPlaySystemImagesAsync(ct);

    public async Task InstallPlatformToolsAsync(
        IProgress<string>? status, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (PlatformToolsReady) { status?.Report("platform-tools already present"); return; }
        status?.Report("Resolving platform-tools package...");
        var pkg = await _repo.GetWindowsPackageAsync("platform-tools", ct);
        var url = pkg.Url.StartsWith("http") ? pkg.Url : AndroidRepository.BaseUrl + pkg.Url;
        var zip = Path.Combine(AppPaths.CacheDir, "platform-tools.zip");
        status?.Report("Downloading platform-tools (adb)...");
        await _dl.DownloadAsync(url, zip, progress, ct);
        status?.Report("Extracting platform-tools...");
        ZipExtractor.Extract(zip, AppPaths.SdkRoot);
        File.Delete(zip);
        Log.Info("platform-tools installed");
    }

    public async Task InstallEmulatorAsync(
        IProgress<string>? status, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (EmulatorReady) { status?.Report("emulator already present"); return; }
        status?.Report("Resolving emulator package...");
        var pkg = await _repo.GetWindowsPackageAsync("emulator", ct);
        var url = pkg.Url.StartsWith("http") ? pkg.Url : AndroidRepository.BaseUrl + pkg.Url;
        var zip = Path.Combine(AppPaths.CacheDir, "emulator.zip");
        status?.Report($"Downloading emulator (~{pkg.Size / 1024 / 1024} MB)...");
        await _dl.DownloadAsync(url, zip, progress, ct);
        status?.Report("Extracting emulator...");
        ZipExtractor.Extract(zip, AppPaths.SdkRoot);   // zip contains emulator/
        File.Delete(zip);
        Log.Info("emulator installed");
    }

    public async Task InstallSystemImageAsync(
        SystemImageOption image,
        IProgress<string>? status, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (SystemImageReady(image.PackagePath))
        {
            status?.Report("system image already present");
            return;
        }

        var urls = new[]
        {
            image.Url,
            AndroidRepository.BaseUrl + image.PackagePath.Replace(';', '/')
        };

        var zip = Path.Combine(AppPaths.CacheDir, $"{image.Tag}-{image.ApiLevel}-{image.Abi}.zip");
        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                status?.Report($"Downloading system image API {image.ApiLevel} (~{image.SizeBytes / 1024 / 1024} MB)...");
                await _dl.DownloadAsync(url, zip, progress, ct);
                last = null;
                break;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"image url failed ({url}): {ex.Message}");
            }
        }
        if (last is not null) throw last;

        status?.Report("Extracting system image...");
        var dir = AppPaths.SystemImageDir(image.PackagePath);
        Directory.CreateDirectory(dir);
        ZipExtractor.Extract(zip, dir);
        // Some archives nest a folder with the abi name; flatten if needed.
        FlattenIfNested(dir, image.Abi);
        File.Delete(zip);
        Log.Info($"system image installed: {image.PackagePath}");
    }

    private static void FlattenIfNested(string dir, string abi)
    {
        if (File.Exists(Path.Combine(dir, "system.img"))) return;
        var nested = Path.Combine(dir, abi);
        if (Directory.Exists(nested) && File.Exists(Path.Combine(nested, "system.img")))
        {
            foreach (var f in Directory.GetFiles(nested))
                File.Move(f, Path.Combine(dir, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(nested))
            {
                var target = Path.Combine(dir, Path.GetFileName(d));
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(d, target);
            }
        }
    }
}
