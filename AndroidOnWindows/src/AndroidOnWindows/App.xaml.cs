using System.Windows;
using AndroidOnWindows.Services;

namespace AndroidOnWindows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureCreated();

        // Headless diagnostics:  AndroidOnWindows.exe --list-images <outfile>
        if (e.Args.Length >= 1 && e.Args[0] == "--list-images")
        {
            var outFile = e.Args.Length >= 2 ? e.Args[1] : Path.Combine(AppPaths.Root, "images.txt");
            try
            {
                RunDiagnosticsSync(outFile);
            }
            catch (Exception ex)
            {
                File.WriteAllText(outFile, "ERROR: " + ex);
            }
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "AndroidOnWindows error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    private void RunDiagnosticsSync(string outFile)
    {
        var dl = new HttpDownloadService();
        var sdk = new SdkBootstrapper(dl, new AndroidRepository(dl));
        var images = sdk.GetSystemImagesAsync().GetAwaiter().GetResult();
        var lines = images.Select(i => $"{i.SortKey,6:0.0}  {i.PackagePath,-60} {i.Url}  {i.SizeBytes / 1024 / 1024} MB");
        File.WriteAllLines(outFile, lines);
    }
}
