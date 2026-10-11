using System.Text.Json;
using AndroidOnWindows.Models;

namespace AndroidOnWindows.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, Opts) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"settings load failed: {ex.Message}");
            Settings = new AppSettings();
        }
        if (string.IsNullOrWhiteSpace(Settings.SharedFolder))
            Settings.SharedFolder = AppPaths.SharedFolder;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Settings, Opts));
        }
        catch (Exception ex) { Log.Warn($"settings save failed: {ex.Message}"); }
    }
}

public static class VirtualizationCheck
{
    public sealed record Result(bool HypervisorPresent, bool VtFirmwareEnabled, bool Ok, string Summary);

    public static async Task<Result> CheckAsync()
    {
        try
        {
            var cmd =
                "$cs=Get-CimInstance Win32_ComputerSystem; " +
                "$pr=Get-CimInstance Win32_Processor | Select-Object -First 1; " +
                "[pscustomobject]@{HypervisorPresent=[bool]$cs.HypervisorPresent; VtFirmwareEnabled=[bool]$pr.VirtualizationFirmwareEnabled} | ConvertTo-Json -Compress";

            var (code, outp, _) = await Proc.RunAsync("powershell.exe",
                $"-NoProfile -NonInteractive -Command \"{cmd}\"");
            if (code != 0 || string.IsNullOrWhiteSpace(outp))
                return new Result(false, false, true, "Could not determine virtualization state; assuming usable.");

            using var doc = JsonDocument.Parse(outp.Trim());
            var hyper = doc.RootElement.GetProperty("HypervisorPresent").GetBoolean();
            var vt = doc.RootElement.GetProperty("VtFirmwareEnabled").GetBoolean();
            var ok = hyper || vt;

            var summary = ok
                ? (hyper ? "A hypervisor is active (Hyper-V / WHPX). Emulator acceleration should work."
                         : "CPU virtualization (VT-x) is enabled in firmware. Enable Windows Hypervisor Platform for best speed.")
                : "Virtualization appears OFF. Enable Intel VT-x in BIOS and turn on 'Windows Hypervisor Platform' " +
                  "in Windows Features, or the emulator will be unusably slow.";

            return new Result(hyper, vt, ok, summary);
        }
        catch (Exception ex)
        {
            Log.Warn($"virtualization check failed: {ex.Message}");
            return new Result(false, false, true, "Virtualization check failed; continuing.");
        }
    }
}
