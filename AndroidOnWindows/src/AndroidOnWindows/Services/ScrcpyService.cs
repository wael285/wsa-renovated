using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using AndroidOnWindows.Models;

namespace AndroidOnWindows.Services;

/// <summary>
/// scrcpy is what turns a headless Android device into real Windows windows.
/// Each app gets its OWN window backed by an Android "virtual display" running just
/// that app, which is how Play Store / any app behaves like a native Windows program.
/// </summary>
public sealed class ScrcpyService
{
    private readonly HttpDownloadService _dl;

    public ScrcpyService(HttpDownloadService dl) => _dl = dl;

    public bool IsInstalled => File.Exists(AppPaths.Scrcpy);

    public async Task EnsureInstalledAsync(
        IProgress<string>? status, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (IsInstalled) { status?.Report("scrcpy already present"); return; }

        status?.Report("Finding latest scrcpy...");
        var assetUrl = await FindLatestWin64AssetAsync(ct);
        var zip = Path.Combine(AppPaths.CacheDir, "scrcpy.zip");
        status?.Report("Downloading scrcpy (~50 MB)...");
        await _dl.DownloadAsync(assetUrl, zip, progress, ct);

        status?.Report("Extracting scrcpy...");
        var tmp = Path.Combine(AppPaths.CacheDir, "scrcpy-extract");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        ZipExtractor.Extract(zip, tmp);

        // Zip contains a single top folder (scrcpy-win64-vX.Y); flatten it.
        var top = Directory.GetDirectories(tmp).FirstOrDefault() ?? tmp;
        Directory.CreateDirectory(AppPaths.ScrcpyDir);
        foreach (var f in Directory.GetFiles(top))
            File.Move(f, Path.Combine(AppPaths.ScrcpyDir, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(top))
        {
            var target = Path.Combine(AppPaths.ScrcpyDir, Path.GetFileName(d));
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(d, target);
        }

        Directory.Delete(tmp, true);
        File.Delete(zip);
        Log.Info("scrcpy installed");
    }

    private async Task<string> FindLatestWin64AssetAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AndroidOnWindows/1.0");
            var json = await http.GetStringAsync("https://api.github.com/repos/Genymobile/scrcpy/releases/latest", ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (Regex.IsMatch(name, @"scrcpy-win64-.*\.zip$", RegexOptions.IgnoreCase))
                    return asset.GetProperty("browser_download_url").GetString()!;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"scrcpy release lookup failed: {ex.Message}; using pinned URL.");
        }
        // Pinned fallback.
        return "https://github.com/Genymobile/scrcpy/releases/download/v3.1/scrcpy-win64-v3.1.zip";
    }

    private static string BaseArgs(string serial, AppSettings s)
    {
        var parts = new List<string> { $"--serial {serial}" };
        if (!s.ScrcpyAudio) parts.Add("--no-audio");
        if (!s.ScrcpyClipboardSync) parts.Add("--no-clipboard-autosync");
        parts.Add(s.ScrcpyUhidKeyboard ? "--keyboard=uhid" : "--keyboard=sdk");
        return string.Join(' ', parts);
    }

    /// <summary>Open one Android app in its own dedicated Windows window.</summary>
    public Process OpenAppWindow(string serial, string package, string label, AppSettings s)
    {
        if (!IsInstalled) throw new FileNotFoundException("scrcpy.exe not found. Run Setup first.", AppPaths.Scrcpy);

        var args = string.Join(' ',
            BaseArgs(serial, s),
            $"--new-display={s.VirtualDisplay}/{s.VirtualDisplayDpi}",
            $"--start-app={package}",
            "--stay-awake",
            $"--window-title=\"{Escape(label)}\"");

        return Proc.StartDetached(AppPaths.Scrcpy, args, AppPaths.ScrcpyDir, redirect: true);
    }

    /// <summary>Open the whole device in a single window (fallback / classic mirror).</summary>
    public Process OpenDeviceWindow(string serial, AppSettings s)
    {
        if (!IsInstalled) throw new FileNotFoundException("scrcpy.exe not found. Run Setup first.", AppPaths.Scrcpy);
        var args = $"{BaseArgs(serial, s)} --window-title=\"Android device\" --stay-awake";
        return Proc.StartDetached(AppPaths.Scrcpy, args, AppPaths.ScrcpyDir, redirect: true);
    }

    /// <summary>Fire an Android app intent forced onto a fresh virtual display via adb
    /// (used when scrcpy virtual displays are unsupported by the image).</summary>
    public Process RecordWindow(string serial, AppSettings s, string outputFile)
    {
        var args = $"{BaseArgs(serial, s)} --record=\"{outputFile}\" --no-playback --stay-awake";
        return Proc.StartDetached(AppPaths.Scrcpy, args, AppPaths.ScrcpyDir, redirect: true);
    }

    private static string Escape(string v) => v.Replace("\"", "'");
}

/// <summary>Screenshots via adb exec-out screencap (no scrcpy window needed).</summary>
public sealed class ScreenshotService
{
    public async Task<string> CaptureAsync(AdbService adb, CancellationToken ct = default)
    {
        Directory.CreateDirectory(AppPaths.ScreenshotsDir);
        var file = Path.Combine(AppPaths.ScreenshotsDir, $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        var bytes = await adb.ExecOutAsync("screencap -p", ct);
        await File.WriteAllBytesAsync(file, bytes, ct);
        Log.Info($"screenshot saved: {file}");
        return file;
    }
}
