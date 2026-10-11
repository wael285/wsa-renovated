using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using AndroidOnWindows.Core;
using AndroidOnWindows.Models;
using AndroidOnWindows.Services;
using Microsoft.Win32;

namespace AndroidOnWindows.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly HttpDownloadService _dl = new();
    private readonly AndroidRepository _repo;
    private readonly SdkBootstrapper _sdk;
    private readonly ScrcpyService _scrcpy;
    private readonly AdbService _adb = new();
    private readonly AvdService _avd = new();
    private readonly EmulatorService _emu = new();
    private readonly ScreenshotService _shots = new();
    private readonly SettingsService _settingsSvc = new();
    private readonly KeyMapService _keymap = new();

    private string _status = "Not checked yet.";
    private string _subStatus = "";
    private bool _isBusy;
    private double _progress;
    private bool _isIndeterminate;
    private bool _appsReady;
    private bool _deviceRunning;
    private bool _isMapping;
    private bool _isRecording;
    private AvdInfo? _selectedDevice;
    private SystemImageOption? _selectedImage;
    private string _newKey = "";
    private string _newKeyX = "";
    private string _newKeyY = "";
    private string _logText = "";

    public MainViewModel()
    {
        _repo = new AndroidRepository(_dl);
        _sdk = new SdkBootstrapper(_dl, _repo);
        _scrcpy = new ScrcpyService(_dl);
        _settingsSvc.Load();
        _keymap.Load();
        foreach (var m in _keymap.Mappings) Mappings.Add(m);

        CheckEnvironmentCommand = new AsyncRelayCommand(CheckEnvironmentAsync);
        SetupCommand = new AsyncRelayCommand(SetupAsync);
        CreateDeviceCommand = new AsyncRelayCommand(CreateDeviceAsync);
        StartDeviceCommand = new AsyncRelayCommand(StartDeviceAsync);
        StopDeviceCommand = new RelayCommand(StopDevice, () => _deviceRunning);
        RefreshAppsCommand = new AsyncRelayCommand(RefreshAppsAsync, () => _appsReady);
        OpenAppCommand = new RelayCommand(p => OpenApp(p as AndroidAppInfo));
        OpenPlayStoreCommand = new RelayCommand(OpenPlayStore, () => _appsReady);
        OpenDeviceWindowCommand = new RelayCommand(OpenDeviceWindow, () => _deviceRunning);
        InstallApkCommand = new AsyncRelayCommand(p => InstallApkAsync(p as string), p => _deviceRunning && p is string);
        ScreenshotCommand = new AsyncRelayCommand(ScreenshotAsync, () => _deviceRunning);
        ToggleRecordCommand = new AsyncRelayCommand(ToggleRecordAsync, () => _deviceRunning);
        FingerprintCommand = new AsyncRelayCommand(FingerprintAsync, () => _deviceRunning);
        CreateShortcutCommand = new RelayCommand(CreateShortcut);
        PinTaskbarCommand = new RelayCommand(PinTaskbar);
        OpenSharedFolderCommand = new RelayCommand(() => OpenFolder(Settings.SharedFolder));
        SendFileCommand = new AsyncRelayCommand(SendFileAsync, () => _deviceRunning);
        AddKeyMapCommand = new RelayCommand(AddKeyMap);
        RemoveKeyMapCommand = new RelayCommand(p => RemoveKeyMap(p as KeyMapping));
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        OpenLogsCommand = new RelayCommand(() => OpenFolder(AppPaths.LogsDir));
        ClearLogCommand = new RelayCommand(() => LogText = "");

        Log.LineWritten += line =>
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var all = LogText + line + Environment.NewLine;
                if (all.Length > 60000) all = all[^60000..];
                LogText = all;
            });
    }

    // ---- state exposed to the UI ----
    public AppSettings Settings => _settingsSvc.Settings;

    public string StatusText { get => _status; set => Set(ref _status, value); }
    public string SubStatus { get => _subStatus; set => Set(ref _subStatus, value); }
    public bool IsBusy
    {
        get => _isBusy;
        set { if (Set(ref _isBusy, value)) RaiseCommands(); }
    }
    public double ProgressPercent { get => _progress; set => Set(ref _progress, value); }
    public bool IsIndeterminate { get => _isIndeterminate; set => Set(ref _isIndeterminate, value); }
    public bool AppsReady { get => _appsReady; set { if (Set(ref _appsReady, value)) RaiseCommands(); } }
    public bool DeviceRunning { get => _deviceRunning; set { if (Set(ref _deviceRunning, value)) RaiseCommands(); } }
    public bool IsMapping { get => _isMapping; set { if (Set(ref _isMapping, value)) OnPropertyChanged(nameof(MappingModeText)); } }
    public string MappingModeText => IsMapping ? "Mapping ON — keys tap the device" : "Mapping OFF";
    public bool IsRecording { get => _isRecording; set { if (Set(ref _isRecording, value)) OnPropertyChanged(nameof(RecordButtonText)); } }
    public string RecordButtonText => IsRecording ? "Stop recording" : "Start recording";

    public string NewKey { get => _newKey; set => Set(ref _newKey, value); }
    public string NewKeyX { get => _newKeyX; set => Set(ref _newKeyX, value); }
    public string NewKeyY { get => _newKeyY; set => Set(ref _newKeyY, value); }
    public string LogText { get => _logText; set => Set(ref _logText, value); }

    public AvdInfo? SelectedDevice
    {
        get => _selectedDevice;
        set { if (Set(ref _selectedDevice, value)) _ = RefreshAppsAsync(); }
    }

    public SystemImageOption? SelectedImage { get => _selectedImage; set => Set(ref _selectedImage, value); }

    public ObservableCollection<AndroidAppInfo> Apps { get; } = new();
    public ObservableCollection<AvdInfo> Devices { get; } = new();
    public ObservableCollection<SystemImageOption> SystemImages { get; } = new();
    public ObservableCollection<KeyMapping> Mappings { get; } = new();

    public RelayCommand StopDeviceCommand { get; }
    public AsyncRelayCommand CheckEnvironmentCommand { get; }
    public AsyncRelayCommand SetupCommand { get; }
    public AsyncRelayCommand CreateDeviceCommand { get; }
    public AsyncRelayCommand StartDeviceCommand { get; }
    public AsyncRelayCommand RefreshAppsCommand { get; }
    public RelayCommand OpenAppCommand { get; }
    public RelayCommand OpenPlayStoreCommand { get; }
    public RelayCommand OpenDeviceWindowCommand { get; }
    public AsyncRelayCommand InstallApkCommand { get; }
    public AsyncRelayCommand ScreenshotCommand { get; }
    public AsyncRelayCommand ToggleRecordCommand { get; }
    public AsyncRelayCommand FingerprintCommand { get; }
    public RelayCommand CreateShortcutCommand { get; }
    public RelayCommand PinTaskbarCommand { get; }
    public RelayCommand OpenSharedFolderCommand { get; }
    public AsyncRelayCommand SendFileCommand { get; }
    public RelayCommand AddKeyMapCommand { get; }
    public RelayCommand RemoveKeyMapCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    private Process? _recordProc;

    private void RaiseCommands()
    {
        StopDeviceCommand.RaiseCanExecuteChanged();
        RefreshAppsCommand.RaiseCanExecuteChanged();
        OpenPlayStoreCommand.RaiseCanExecuteChanged();
        OpenDeviceWindowCommand.RaiseCanExecuteChanged();
        InstallApkCommand.RaiseCanExecuteChanged();
        ScreenshotCommand.RaiseCanExecuteChanged();
        ToggleRecordCommand.RaiseCanExecuteChanged();
        FingerprintCommand.RaiseCanExecuteChanged();
        SendFileCommand.RaiseCanExecuteChanged();
    }

    private IProgress<DownloadProgress> DlProgress => new Progress<DownloadProgress>(p =>
    {
        ProgressPercent = p.Percent;
        SubStatus = p.Message;
    });

    private IProgress<string> StatusProgress => new Progress<string>(m => SubStatus = m);

    // ---- environment / setup ----

    public async Task CheckEnvironmentAsync()
    {
        IsBusy = true;
        IsIndeterminate = true;
        try
        {
            AppPaths.EnsureCreated();
            ReloadDevices();

            var virt = await VirtualizationCheck.CheckAsync();
            var parts = new List<string>
            {
                File.Exists(AppPaths.Adb) ? "adb ✅" : "adb ❌",
                File.Exists(AppPaths.Emulator) ? "emulator ✅" : "emulator ❌",
                File.Exists(AppPaths.Scrcpy) ? "scrcpy ✅" : "scrcpy ❌",
                virt.Ok ? "virtualization ✅" : "virtualization ❌"
            };
            StatusText = "Environment: " + string.Join("   ", parts);
            SubStatus = virt.Summary;
            if (!virt.Ok)
                Log.Warn(virt.Summary);

            if (SystemImages.Count == 0)
                await LoadImagesAsync();
        }
        catch (Exception ex) { Fail(ex); }
        finally { IsBusy = false; IsIndeterminate = false; }
    }

    private async Task LoadImagesAsync()
    {
        try
        {
            SubStatus = "Fetching available Play system images...";
            var images = await _sdk.GetSystemImagesAsync();
            SystemImages.Clear();
            foreach (var i in images) SystemImages.Add(i);
            SelectedImage = PickDefaultImage(SystemImages);
        }
        catch (Exception ex) { Log.Warn($"image list failed: {ex.Message}"); }
    }

    private static SystemImageOption? PickDefaultImage(IEnumerable<SystemImageOption> images)
    {
        var list = images.ToList();
        var candidates = list.Where(i => !i.Tag.Contains("ps16k", StringComparison.OrdinalIgnoreCase)
                                         && i.SortKey >= 33 && i.SortKey <= 36.9).ToList();
        if (candidates.Count > 0)
            return candidates.OrderBy(i => i.SizeBytes).FirstOrDefault();
        candidates = list.Where(i => !i.Tag.Contains("ps16k", StringComparison.OrdinalIgnoreCase)
                                     && i.SortKey >= 33).ToList();
        if (candidates.Count > 0)
            return candidates.OrderBy(i => i.SizeBytes).FirstOrDefault();
        return list.FirstOrDefault();
    }

    private async Task SetupAsync()
    {
        IsBusy = true;
        try
        {
            AppPaths.EnsureCreated();
            await _sdk.InstallEmulatorAsync(StatusProgress, DlProgress, default);
            await _sdk.InstallPlatformToolsAsync(StatusProgress, DlProgress, default);
            await _scrcpy.EnsureInstalledAsync(StatusProgress, DlProgress, default);
            // Pick the smallest Play image if the user hasn't chosen one.
            if (SelectedImage is null)
            {
                if (SystemImages.Count == 0) await LoadImagesAsync();
                SelectedImage = PickDefaultImage(SystemImages) ?? SystemImages.OrderBy(i => i.SortKey >= 30 ? i.SizeBytes : long.MaxValue).FirstOrDefault();
            }
            StatusText = "Core tools installed ✅";
        }
        catch (Exception ex) { Fail(ex); }
        finally { IsBusy = false; IsIndeterminate = false; }
    }

    public async Task EnsureSystemImageAsync()
    {
        if (SelectedImage is null)
        {
            if (SystemImages.Count == 0) await LoadImagesAsync();
            SelectedImage ??= SystemImages.FirstOrDefault();
        }
        if (SelectedImage is null)
            throw new InvalidOperationException("No Play system image selected. Open the Setup tab and pick one.");

        IsBusy = true;
        try
        {
            await _sdk.InstallSystemImageAsync(SelectedImage, StatusProgress, DlProgress, default);
        }
        finally { IsBusy = false; }
    }

    private async Task CreateDeviceAsync()
    {
        IsBusy = true;
        try
        {
            if (SelectedImage is null)
            {
                if (SystemImages.Count == 0) await LoadImagesAsync();
                SelectedImage ??= SystemImages.FirstOrDefault();
            }
            if (SelectedImage is null) throw new InvalidOperationException("No system image available.");

            _adb.StartServerAsync().Wait();
            _avd.Create(Settings.AvdName, SelectedImage, Settings);
            Settings.ApiLevel = SelectedImage.ApiLevel;
            Settings.Abi = SelectedImage.Abi;
            _settingsSvc.Save();
            ReloadDevices();
            StatusText = $"Device '{Settings.AvdName}' created ✅";
        }
        catch (Exception ex) { Fail(ex); }
        finally { IsBusy = false; }
    }

    private async Task StartDeviceAsync()
    {
        IsBusy = true;
        IsIndeterminate = true;
        try
        {
            if (!File.Exists(AppPaths.Emulator))
                throw new InvalidOperationException("Emulator not installed. Click 'Setup' first.");
            if (!File.Exists(AppPaths.Scrcpy))
                await _scrcpy.EnsureInstalledAsync(StatusProgress, DlProgress, default);
            if (SelectedImage is not null && !_sdk.SystemImageReady(SelectedImage.PackagePath))
                await EnsureSystemImageAsync();
            if (SelectedDevice is null) ReloadDevices();
            var avd = SelectedDevice ?? Devices.FirstOrDefault();
            if (avd is null)
            {
                if (SelectedImage is null) throw new InvalidOperationException("No system image selected. Open Setup.");
                _avd.Create(Settings.AvdName, SelectedImage, Settings);
                ReloadDevices();
                avd = Devices.FirstOrDefault();
                if (avd is null) throw new InvalidOperationException("Could not create a device.");
            }

            StatusText = $"Booting '{avd.DisplayName}'...";
            _adb.Serial = _emu.Start(avd.Name, Settings);
            _adb.StartServerAsync().Wait();

            IsIndeterminate = false;
            var booted = await _adb.WaitForBootAsync(TimeSpan.FromMinutes(10), StatusProgress);
            if (!booted) throw new TimeoutException("Android did not finish booting in 10 minutes.");

            avd.IsRunning = true;
            DeviceRunning = true;
            StatusText = "Device running ✅ — launch Play Store or any app in its own window.";
            await RefreshAppsAsync();
        }
        catch (Exception ex) { Fail(ex); }
        finally { IsBusy = false; IsIndeterminate = false; }
    }

    private void StopDevice()
    {
        _emu.Stop();
        foreach (var d in Devices) d.IsRunning = false;
        DeviceRunning = false;
        AppsReady = false;
        Apps.Clear();
        StatusText = "Device stopped.";
    }

    // ---- apps ----

    private async Task RefreshAppsAsync()
    {
        if (!DeviceRunning) return;
        try
        {
            SubStatus = "Loading app list...";
            var list = await _adb.ListLaunchableAppsAsync(StatusProgress);
            Apps.Clear();
            foreach (var a in list) Apps.Add(a);
            AppsReady = Apps.Count > 0;
            SubStatus = $"{Apps.Count} launchable apps";
        }
        catch (Exception ex) { Log.Warn($"app list failed: {ex.Message}"); }
    }

    private void OpenApp(AndroidAppInfo? app)
    {
        if (app is null || !DeviceRunning) return;
        try
        {
            _scrcpy.OpenAppWindow(_adb.Serial, app.Package, app.Label, Settings);
            app.IsRunning = true;
            SubStatus = $"Opened {app.Label} in its own window";
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void OpenPlayStore() =>
        OpenApp(new AndroidAppInfo { Package = "com.android.vending", Label = "Google Play Store", IsSystem = true });

    private void OpenDeviceWindow()
    {
        try { _scrcpy.OpenDeviceWindow(_adb.Serial, Settings); }
        catch (Exception ex) { Fail(ex); }
    }

    private async Task InstallApkAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            IsBusy = true;
            await _adb.InstallApkAsync(path, StatusProgress);
            await RefreshAppsAsync();
            StatusText = $"Installed {Path.GetFileName(path)} ✅";
        }
        catch (Exception ex) { Fail(ex); }
        finally { IsBusy = false; }
    }

    public async Task InstallApksAsync(IEnumerable<string> paths)
    {
        foreach (var p in paths.Where(p => p.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)))
            await InstallApkAsync(p);
    }

    // ---- device extras ----

    private async Task ScreenshotAsync()
    {
        try
        {
            var file = await _shots.CaptureAsync(_adb);
            StatusText = $"Screenshot saved: {file}";
            OpenFolder(AppPaths.ScreenshotsDir);
        }
        catch (Exception ex) { Fail(ex); }
    }

    private Task ToggleRecordAsync()
    {
        try
        {
            if (IsRecording)
            {
                if (_recordProc is { HasExited: false }) _recordProc.Kill(true);
                _recordProc = null;
                IsRecording = false;
                StatusText = $"Recording saved to {AppPaths.RecordingsDir}";
                OpenFolder(AppPaths.RecordingsDir);
            }
            else
            {
                Directory.CreateDirectory(AppPaths.RecordingsDir);
                var file = Path.Combine(AppPaths.RecordingsDir, $"record-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
                _recordProc = _scrcpy.RecordWindow(_adb.Serial, Settings, file);
                IsRecording = true;
                StatusText = "Recording started.";
            }
        }
        catch (Exception ex) { Fail(ex); }
        return Task.CompletedTask;
    }

    private async Task FingerprintAsync()
    {
        try
        {
            await _adb.SendFingerprintAsync(1);
            StatusText = "Sent simulated fingerprint #1 (emulator has no real sensor).";
            SubStatus = "Enroll a fingerprint in Android Settings → Security first, then press this again.";
        }
        catch (Exception ex) { Fail(ex); }
    }

    public async Task TapAsync(int x, int y)
    {
        if (!DeviceRunning) return;
        try { await _adb.ShellAsync($"input tap {x} {y}"); }
        catch (Exception ex) { Log.Warn($"tap failed: {ex.Message}"); }
    }

    private async Task SendFileAsync()
    {
        var dlg = new OpenFileDialog { Title = "Send file to Android device" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            await _adb.PushAsync(dlg.FileName, "/sdcard/Download/" + Path.GetFileName(dlg.FileName));
            StatusText = $"Sent to device: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { Fail(ex); }
        finally { IsBusy = false; }
    }

    // ---- windows integration ----

    private void CreateShortcut()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName
                      ?? Path.Combine(AppContext.BaseDirectory, "AndroidOnWindows.exe");
            var lnk = ShortcutService.CreateShortcut(exe, "", "Android on Windows");
            Settings.CreateStartMenuShortcut = true;
            _settingsSvc.Save();
            StatusText = $"Start menu shortcut created: {lnk}";
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void PinTaskbar()
    {
        try
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var lnk = Path.Combine(programs, "Android on Windows.lnk");
            if (!File.Exists(lnk)) CreateShortcut();
            var ok = ShortcutService.TryPinToTaskbar(lnk);
            StatusText = ok ? "Pinned to taskbar ✅" : "Windows blocked automatic pinning — right-click the Start-menu shortcut → 'Pin to taskbar'.";
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void AddKeyMap()
    {
        if (string.IsNullOrWhiteSpace(NewKey)) { StatusText = "Enter a key first."; return; }
        if (!int.TryParse(NewKeyX, out var x) || !int.TryParse(NewKeyY, out var y))
        {
            StatusText = "X and Y must be numbers (device pixels).";
            return;
        }
        var m = new KeyMapping { Key = NewKey.Trim(), X = x, Y = y };
        _keymap.AddOrUpdate(m);
        Mappings.Clear();
        foreach (var k in _keymap.Mappings) Mappings.Add(k);
        StatusText = $"Mapped {m.Key} → ({x},{y})";
        NewKey = NewKeyX = NewKeyY = "";
    }

    private void RemoveKeyMap(KeyMapping? m)
    {
        if (m is null) return;
        _keymap.Remove(m.Key);
        Mappings.Remove(m);
    }

    private void SaveSettings()
    {
        _settingsSvc.Save();
        StatusText = "Settings saved.";
    }

    // ---- helpers ----

    public void ReloadDevices()
    {
        Devices.Clear();
        foreach (var d in _avd.List()) Devices.Add(d);
        SelectedDevice ??= Devices.FirstOrDefault();
    }

    public void HandleKey(string key)
    {
        if (!IsMapping || !DeviceRunning) return;
        if (_keymap.TryGet(key, out var m))
            _ = TapAsync(m.X, m.Y);
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn($"open folder failed: {ex.Message}"); }
    }

    private void Fail(Exception ex)
    {
        Log.Error(ex.ToString());
        StatusText = "Error: " + ex.Message;
        SubStatus = ex.Message;
    }
}
