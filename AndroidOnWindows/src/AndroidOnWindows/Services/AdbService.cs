using System.Text;
using AndroidOnWindows.Models;

namespace AndroidOnWindows.Services;

public sealed class AdbService
{
    public string Serial { get; set; } = "emulator-5554";

    private string Adb => AppPaths.Adb;

    public async Task<bool> StartServerAsync(CancellationToken ct = default)
    {
        var (_, outText, err) = await Proc.RunAsync(Adb, "start-server", ct: ct);
        return outText.Contains("daemon", StringComparison.OrdinalIgnoreCase) || true;
    }

    public async Task<bool> WaitForDeviceAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            var (code, _, _) = await Proc.RunAsync(Adb, $"-s {Serial} wait-for-device", ct: cts.Token);
            return code == 0;
        }
        catch (OperationCanceledException) { return false; }
    }

    public async Task<bool> WaitForBootAsync(TimeSpan timeout, IProgress<string>? status = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var boot = await ShellAsync("getprop sys.boot_completed");
            if (boot.Trim() == "1")
            {
                // give the Play services a little time to settle
                await Task.Delay(2000, ct);
                status?.Report("Device booted");
                return true;
            }
            var anim = await ShellAsync("getprop init.svc.bootanim");
            status?.Report($"Waiting for Android to boot... ({anim.Trim()})");
            await Task.Delay(2000, ct);
        }
        return false;
    }

    public async Task<string> ShellAsync(string args, CancellationToken ct = default)
    {
        var (_, outText, err) = await Proc.RunAsync(Adb, $"-s {Serial} shell {args}", ct: ct);
        return string.IsNullOrWhiteSpace(outText) ? err : outText;
    }

    public async Task<byte[]> ExecOutAsync(string args, CancellationToken ct = default)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(AppPaths.Adb, $"-s {Serial} exec-out {args}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        using var ms = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(copy, p.WaitForExitAsync(ct));
        await errTask;
        return ms.ToArray();
    }

    public async Task<List<AndroidAppInfo>> ListLaunchableAppsAsync(
        IProgress<string>? status = null, CancellationToken ct = default)
    {
        var result = new List<AndroidAppInfo>();
        var thirdParty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        status?.Report("Listing installed apps...");
        var pkgList = await ShellAsync("pm list packages -3", ct);
        foreach (var line in pkgList.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t.StartsWith("package:")) thirdParty.Add(t["package:".Length..].Trim());
        }

        // Launcher activities (package + component)
        var query = await ShellAsync(
            "cmd package query-activities -a android.intent.action.MAIN -c android.intent.category.LAUNCHER", ct);

        var launchable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in query.Split('\n'))
        {
            var t = line.Trim();
            var idx = t.IndexOf("packageName=", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var rest = t[(idx + "packageName=".Length)..].Trim();
                var comma = rest.IndexOfAny(new[] { ' ', ',', '\r' });
                if (comma > 0) rest = rest[..comma];
                if (rest.Length > 0) launchable.Add(rest);
            }
            else if (t.Contains('/') && !t.Contains(' '))
            {
                launchable.Add(t.Split('/')[0]);
            }
        }

        var candidates = new HashSet<string>(thirdParty, StringComparer.OrdinalIgnoreCase);
        foreach (var p in launchable) candidates.Add(p);

        foreach (var pkg in candidates)
        {
            if (pkg.StartsWith("com.android.", StringComparison.OrdinalIgnoreCase) &&
                pkg != "com.android.vending" &&
                !thirdParty.Contains(pkg))
                continue; // skip most system noise unless user-installed

            if (!launchable.Contains(pkg) && pkg != "com.android.vending") continue;

            result.Add(new AndroidAppInfo
            {
                Package = pkg,
                Label = FriendlyLabel(pkg),
                IsSystem = !thirdParty.Contains(pkg)
            });
        }

        if (!result.Any(a => a.IsPlayStore) && pkgList.Contains("com.android.vending"))
        {
            result.Insert(0, new AndroidAppInfo { Package = "com.android.vending", Label = "Google Play Store", IsSystem = true });
        }

        return result
            .OrderByDescending(a => a.IsPlayStore)
            .ThenBy(a => a.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FriendlyLabel(string pkg)
    {
        if (pkg == "com.android.vending") return "Google Play Store";
        var seg = pkg.Split('.').LastOrDefault(s => s.Length > 2) ?? pkg;
        var sb = new StringBuilder();
        foreach (var ch in seg)
        {
            if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' ');
            sb.Append(sb.Length == 0 ? char.ToUpper(ch) : ch);
        }
        var label = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(label) ? pkg : label;
    }

    public async Task InstallApkAsync(string apkPath, IProgress<string>? status = null, CancellationToken ct = default)
    {
        status?.Report($"Installing {Path.GetFileName(apkPath)}...");
        var (code, outText, err) = await Proc.RunAsync(AppPaths.Adb, $"-s {Serial} install -r -g \"{apkPath}\"", ct: ct);
        Log.Info($"install rc={code} {outText} {err}");
        if (code != 0 || (!outText.Contains("Success") && !outText.Contains("success")))
            throw new InvalidOperationException($"Install failed: {err}{outText}");
        status?.Report($"Installed {Path.GetFileName(apkPath)}");
    }

    public async Task PushAsync(string local, string remote, CancellationToken ct = default)
    {
        var (code, outText, err) = await Proc.RunAsync(AppPaths.Adb, $"-s {Serial} push \"{local}\" \"{remote}\"", ct: ct);
        if (code != 0) throw new InvalidOperationException($"push failed: {err}{outText}");
    }

    public async Task PullAsync(string remote, string local, CancellationToken ct = default)
    {
        var (code, outText, err) = await Proc.RunAsync(AppPaths.Adb, $"-s {Serial} pull \"{remote}\" \"{local}\"", ct: ct);
        if (code != 0) throw new InvalidOperationException($"pull failed: {err}{outText}");
    }

    public async Task StartActivityAsync(string package, CancellationToken ct = default)
    {
        // Prefer the monkey launcher (works without knowing the exact activity name).
        var (code, outText, err) = await Proc.RunAsync(AppPaths.Adb,
            $"-s {Serial} shell monkey -p {package} -c android.intent.category.LAUNCHER 1", ct: ct);
        Log.Info($"launch {package}: rc={code} {outText}");
    }

    public async Task ForceStopAsync(string package, CancellationToken ct = default)
        => await ShellAsync($"am force-stop {package}", ct);

    /// <summary>Simulated fingerprint (the emulator has no real sensor).</summary>
    public async Task SendFingerprintAsync(int id, CancellationToken ct = default)
        => await Proc.RunAsync(AppPaths.Adb, $"-s {Serial} emu finger touch {id}", ct: ct);

    /// <summary>Simulated NFC-like confirmation is not exposed; kept as a hook.</summary>
    public async Task<string> EmuAsync(string args, CancellationToken ct = default)
    {
        var (_, outText, err) = await Proc.RunAsync(AppPaths.Adb, $"-s {Serial} emu {args}", ct: ct);
        return string.IsNullOrWhiteSpace(outText) ? err : outText;
    }

    public async Task<string> GetPropAsync(string prop, CancellationToken ct = default)
    {
        var v = await ShellAsync($"getprop {prop}", ct);
        return v.Trim();
    }

    public async Task<bool> IsDeviceOnlineAsync(CancellationToken ct = default)
    {
        var (code, outText, _) = await Proc.RunAsync(Adb, "devices", ct: ct);
        if (code != 0) return false;
        return outText.Split('\n').Any(l => l.StartsWith(Serial) && l.Contains("device"));
    }
}
