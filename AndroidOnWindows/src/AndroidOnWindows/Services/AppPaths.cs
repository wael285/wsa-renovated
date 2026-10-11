namespace AndroidOnWindows.Services;

/// <summary>
/// Central layout for everything we download and generate. Nothing here requires
/// Android Studio or Java: the SDK is assembled straight from Google's zip archives.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidOnWindows");

    public static string SdkRoot { get; } = Path.Combine(Root, "sdk");
    public static string ToolsRoot { get; } = Path.Combine(Root, "tools");
    public static string ScrcpyDir { get; } = Path.Combine(ToolsRoot, "scrcpy");
    public static string CacheDir { get; } = Path.Combine(Root, "cache");
    public static string LogsDir { get; } = Path.Combine(Root, "logs");
    public static string SharedFolder { get; } = Path.Combine(Root, "Shared");
    public static string ScreenshotsDir { get; } = Path.Combine(Root, "Screenshots");
    public static string RecordingsDir { get; } = Path.Combine(Root, "Recordings");
    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");

    public static string UserProfile { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string AvdHome { get; } = Path.Combine(UserProfile, ".android", "avd");
    public static string AndroidHome { get; } = Path.Combine(UserProfile, ".android");

    public static string Adb => Path.Combine(SdkRoot, "platform-tools", "adb.exe");
    public static string Emulator => Path.Combine(SdkRoot, "emulator", "emulator.exe");
    public static string Scrcpy => Path.Combine(ScrcpyDir, "scrcpy.exe");
    public static string SystemImages => Path.Combine(SdkRoot, "system-images");

    public static bool HasPlatformTools => File.Exists(Adb);
    public static bool HasEmulator => File.Exists(Emulator);
    public static bool HasScrcpy => File.Exists(Scrcpy);

    public static void EnsureCreated()
    {
        foreach (var d in new[]
                 {
                     Root, SdkRoot, ToolsRoot, CacheDir, LogsDir, SharedFolder,
                     ScreenshotsDir, RecordingsDir, AvdHome, AndroidHome,
                     Path.Combine(SdkRoot, "platform-tools"),
                     Path.Combine(SdkRoot, "emulator")
                 })
        {
            Directory.CreateDirectory(d);
        }
    }

    /// <summary>Path (relative to SDK root, forward slashes) for a system image, e.g.
    /// system-images/android-34/google_apis_playstore/x86_64/</summary>
    public static string SystemImageDir(string packagePath)
    {
        // packagePath = system-images;android-34;google_apis_playstore;x86_64
        var parts = packagePath.Split(';');
        return parts.Length == 4
            ? Path.Combine(SdkRoot, "system-images", parts[1], parts[2], parts[3])
            : Path.Combine(SdkRoot, "system-images");
    }

    public static string SystemImageRel(string packagePath)
    {
        var parts = packagePath.Split(';');
        return parts.Length == 4
            ? $"system-images/{parts[1]}/{parts[2]}/{parts[3]}/"
            : "system-images/";
    }
}
