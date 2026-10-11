using System.Diagnostics;
using AndroidOnWindows.Models;

namespace AndroidOnWindows.Services;

public sealed class EmulatorService
{
    private Process? _emulator;
    public bool IsRunning => _emulator is { HasExited: false };

    public string Start(string avdName, AppSettings s, int port = 5554)
    {
        if (!File.Exists(AppPaths.Emulator))
            throw new FileNotFoundException("emulator.exe not found. Run Setup first.", AppPaths.Emulator);

        var args = string.Join(' ',
            $"-avd \"{avdName}\"",
            "-no-window",
            "-no-audio",
            "-no-boot-anim",
            "-no-snapshot",
            $"-gpu {s.GpuMode}",
            $"-camera-back {s.CameraBack}",
            $"-camera-front {s.CameraFront}",
            $"-port {port}",
            "-netdelay none -netspeed full");

        _emulator = Proc.StartDetached(AppPaths.Emulator, args, AppPaths.SdkRoot, redirect: true);
        _emulator.EnableRaisingEvents = true;
        Log.Info($"emulator started pid={_emulator.Id} port={port}");
        return $"emulator-{port}";
    }

    public void Stop()
    {
        try
        {
            if (_emulator is { HasExited: false })
            {
                _emulator.Kill(entireProcessTree: true);
                Log.Info("emulator stopped");
            }
        }
        catch (Exception ex) { Log.Warn($"stop emulator failed: {ex.Message}"); }
        _emulator = null;
    }

    public Process? Process => _emulator;
}
