using AndroidOnWindows.Core;

namespace AndroidOnWindows.Models;

public sealed class AndroidAppInfo : ObservableObject
{
    private string _label = "";
    private bool _isRunning;

    public required string Package { get; init; }
    public required string Label { get => _label; set => Set(ref _label, value); }
    public bool IsSystem { get; init; }
    public bool IsPlayStore => Package == "com.android.vending";
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }
}

public sealed class AvdInfo : ObservableObject
{
    private bool _isRunning;
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Target { get; init; }
    public string Abi { get; init; } = "x86_64";
    public bool PlayStore { get; init; }
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }
}

public readonly record struct DownloadProgress(long BytesReceived, long TotalBytes, string Message)
{
    public double Percent => TotalBytes > 0 ? Math.Clamp(BytesReceived * 100.0 / TotalBytes, 0, 100) : 0;
    public string Eta(double bytesPerSecond)
    {
        if (TotalBytes <= 0 || bytesPerSecond <= 0) return "";
        var remain = TotalBytes - BytesReceived;
        var secs = remain / bytesPerSecond;
        return "ETA " + TimeSpan.FromSeconds(secs).ToString(@"hh\:mm\:ss");
    }
}

public sealed class SystemImageOption
{
    // API levels are no longer always integers in Google's repo (e.g. "36.1", "37.0").
    public required string ApiLevel { get; init; }
    public required string Abi { get; init; }
    public required string Tag { get; init; }          // google_apis_playstore | google_apis | default
    public required string PackagePath { get; init; }  // system-images;android-34;google_apis_playstore;x86_64
    public required string Url { get; init; }
    public long SizeBytes { get; init; }
    public bool PlayStore => Tag.Contains("playstore", StringComparison.OrdinalIgnoreCase);

    public double SortKey =>
        double.TryParse(ApiLevel, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

    public string Label => $"API {ApiLevel}  •  {Abi}  •  {(PlayStore ? "Google Play" : Tag)}  •  ~{SizeBytes / 1024.0 / 1024.0:0} MB";
}

public sealed class AppSettings
{
    // Device
    public string ApiLevel { get; set; } = "34";
    public string Abi { get; set; } = "x86_64";
    public int RamMb { get; set; } = 2048;
    public int Cores { get; set; } = 4;
    public string AvdName { get; set; } = "PlayPixel";
    public string Resolution { get; set; } = "1080x2340";
    public int Dpi { get; set; } = 440;

    // Camera (emulator passes the HOST webcam through)
    public string CameraBack { get; set; } = "webcam0";   // webcam0..n | emulated | none
    public string CameraFront { get; set; } = "emulated";

    // Rendering
    public string GpuMode { get; set; } = "auto";         // auto | host | swiftshader_indirect

    // scrcpy / per-app windows
    public bool ScrcpyAudio { get; set; } = true;
    public bool ScrcpyClipboardSync { get; set; } = true;
    public bool ScrcpyUhidKeyboard { get; set; } = true;
    public string VirtualDisplay { get; set; } = "1280x720";
    public int VirtualDisplayDpi { get; set; } = 240;

    // Features
    public bool AutoUpdateImages { get; set; } = false;
    public bool CreateStartMenuShortcut { get; set; } = true;
    public string SharedFolder { get; set; } = "";
}
