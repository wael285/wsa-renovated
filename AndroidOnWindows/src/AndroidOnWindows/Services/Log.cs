using System.Diagnostics;
using System.Text;

namespace AndroidOnWindows.Services;

public static class Log
{
    private static readonly object Gate = new();
    private static string LogFilePath => Path.Combine(AppPaths.LogsDir, $"aow-{DateTime.Now:yyyyMMdd}.log");

    public static event Action<string>? LineWritten;

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERR ", msg);

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {msg}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch { /* logging must never throw */ }
        }
        LineWritten?.Invoke(line);
    }
}

public static class Proc
{
    public static async Task<(int Code, string StdOut, string StdErr)> RunAsync(
        string exe, string args, string? workDir = null,
        Action<string>? onStdOut = null, Action<string>? onStdErr = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workDir ?? AppPaths.Root
        };

        Log.Info($"exec: {exe} {args}");

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var so = new StringBuilder();
        var se = new StringBuilder();

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            so.AppendLine(e.Data);
            onStdOut?.Invoke(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            se.AppendLine(e.Data);
            onStdErr?.Invoke(e.Data);
        };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, so.ToString(), se.ToString());
    }

    /// <summary>Start a detached, long-lived process (emulator / scrcpy / an app window).</summary>
    public static Process StartDetached(string exe, string args, string? workDir = null,
        bool redirect = false, Action<string>? onStdErr = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = !redirect,
            WorkingDirectory = workDir ?? AppPaths.Root,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect
        };
        Log.Info($"spawn: {exe} {args}");
        var p = new Process { StartInfo = psi };
        if (redirect)
        {
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) { Log.Info($"[{Path.GetFileNameWithoutExtension(exe)}] {e.Data}"); onStdErr?.Invoke(e.Data); } };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) Log.Info($"[{Path.GetFileNameWithoutExtension(exe)}] {e.Data}"); };
        }
        p.Start();
        if (redirect) { p.BeginOutputReadLine(); p.BeginErrorReadLine(); }
        return p;
    }
}
