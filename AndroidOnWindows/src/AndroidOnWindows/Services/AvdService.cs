using System.Text;
using AndroidOnWindows.Models;

namespace AndroidOnWindows.Services;

/// <summary>
/// Creates and tracks AVDs by writing the .ini/config.ini files directly, so we never
/// need avdmanager or Java.
/// </summary>
public sealed class AvdService
{
    public string AvdHome => AppPaths.AvdHome;

    public List<AvdInfo> List()
    {
        var list = new List<AvdInfo>();
        if (!Directory.Exists(AvdHome)) return list;

        foreach (var ini in Directory.GetFiles(AvdHome, "*.ini"))
        {
            try
            {
                if (Path.GetFileNameWithoutExtension(ini).Contains('.')) continue; // skip <name>.avd.ini style
                var kv = ParseIni(ini);
                var target = kv.GetValueOrDefault("target", "");
                var name = Path.GetFileNameWithoutExtension(ini);
                var cfgPath = Path.Combine(AvdHome, name + ".avd", "config.ini");
                var cfg = File.Exists(cfgPath) ? ParseIni(cfgPath) : new Dictionary<string, string>();

                list.Add(new AvdInfo
                {
                    Name = name,
                    DisplayName = cfg.GetValueOrDefault("avd.ini.displayname", name),
                    Target = target,
                    Abi = cfg.GetValueOrDefault("hw.cpu.arch", "x86_64"),
                    PlayStore = cfg.GetValueOrDefault("PlayStore.enabled", "false")
                        .Equals("true", StringComparison.OrdinalIgnoreCase)
                });
            }
            catch (Exception ex) { Log.Warn($"failed to read AVD {ini}: {ex.Message}"); }
        }
        return list;
    }

    public string Create(string name, SystemImageOption image, AppSettings s)
    {
        Directory.CreateDirectory(AvdHome);
        var avdDir = Path.Combine(AvdHome, name + ".avd");
        Directory.CreateDirectory(avdDir);

        var (w, h) = ParseResolution(s.Resolution);

        var ini = new StringBuilder()
            .AppendLine("avd.ini.encoding=UTF-8")
            .AppendLine($"path={avdDir}")
            .AppendLine($"path.rel=avd\\{name}.avd")
            .AppendLine($"target=android-{image.ApiLevel}")
            .ToString();
        File.WriteAllText(Path.Combine(AvdHome, name + ".ini"), ini);

        var cfg = new StringBuilder()
            .AppendLine($"AvdId={name}")
            .AppendLine($"avd.ini.displayname={name}")
            .AppendLine("avd.ini.encoding=UTF-8")
            .AppendLine("disk.dataPartition.size=6442450944")
            .AppendLine("fastboot.forceColdBoot=no")
            .AppendLine("hw.accelerometer=yes")
            .AppendLine("hw.audioInput=yes")
            .AppendLine("hw.audioOutput=yes")
            .AppendLine("hw.battery=yes")
            .AppendLine($"hw.camera.back={s.CameraBack}")
            .AppendLine($"hw.camera.front={s.CameraFront}")
            .AppendLine($"hw.cpu.arch={image.Abi}")
            .AppendLine($"hw.cpu.ncore={s.Cores}")
            .AppendLine("hw.dPad=no")
            .AppendLine("hw.device.manufacturer=Google")
            .AppendLine("hw.device.name=pixel_5")
            .AppendLine("hw.gps=yes")
            .AppendLine("hw.gpu.enabled=yes")
            .AppendLine($"hw.gpu.mode={s.GpuMode}")
            .AppendLine("hw.keyboard=yes")
            .AppendLine($"hw.lcd.density={s.Dpi}")
            .AppendLine($"hw.lcd.height={h}")
            .AppendLine($"hw.lcd.width={w}")
            .AppendLine("hw.mainKeys=no")
            .AppendLine($"hw.ramSize={s.RamMb}")
            .AppendLine("hw.sdCard=no")
            .AppendLine("hw.sensors.orientation=yes")
            .AppendLine("hw.sensors.proximity=yes")
            .AppendLine("hw.trackBall=no")
            .AppendLine($"image.sysdir.1={AppPaths.SystemImageRel(image.PackagePath)}")
            .AppendLine($"PlayStore.enabled={(image.PlayStore ? "true" : "false")}")
            .AppendLine("runtime.network.latency=none")
            .AppendLine("runtime.network.speed=full")
            .AppendLine("skin.dynamic=yes")
            .AppendLine($"skin.name={w}x{h}")
            .AppendLine("skin.path=_no_skin")
            .AppendLine($"tag.display={(image.PlayStore ? "Google Play" : "Google APIs")}")
            .AppendLine($"tag.id={image.Tag}")
            .AppendLine("vm.heapSize=256")
            .ToString();
        File.WriteAllText(Path.Combine(avdDir, "config.ini"), cfg);

        Log.Info($"created AVD {name} from {image.PackagePath}");
        return avdDir;
    }

    public void Delete(string name)
    {
        var ini = Path.Combine(AvdHome, name + ".ini");
        var dir = Path.Combine(AvdHome, name + ".avd");
        if (File.Exists(ini)) File.Delete(ini);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static (int w, int h) ParseResolution(string res)
    {
        var parts = res.ToLowerInvariant().Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
            return (w, h);
        return (1080, 2340);
    }

    private static Dictionary<string, string> ParseIni(string path)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            var i = line.IndexOf('=');
            if (i <= 0) continue;
            d[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        return d;
    }
}
