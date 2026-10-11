# Android on Windows

Runs the **Google Play Store** and Android apps on Windows 11, where **each app opens in
its own native Windows window**. There is no emulator window: the Android device runs
headless and [scrcpy](https://github.com/Genymobile/scrcpy) renders each app on its own
Android *virtual display* as a separate Windows window.

```
Android app  ──(virtual display)──►  scrcpy  ──►  native Windows window
     ▲
     └── headless Android emulator (Google Play system image)  ◄── adb ── AndroidOnWindows (WPF)
```

## What it is / isn't

| | |
|---|---|
| ✅ Real, legal **Play Store** (Google-certified Play system image, your Google account) | ❌ It is **not** real phone hardware. |
| ✅ adb, emulator and image downloaded **directly from Google** — no Android Studio, no Java | ❌ **Fingerprint** is *simulated* (`adb emu finger touch`); no real sensor. |
| ✅ **Resumable** downloads (safe on slow links) | ❌ **Bluetooth / Wi-Fi / NFC** are *emulated*, not your real radios. |
| ✅ Each app in its **own window**; clipboard sync; drag-drop APK install | ❌ **Face unlock** uses no real biometrics (Google doesn't expose it to the emulator). |
| ✅ Screenshots, recording, shared folder, Start-menu shortcut | ⚠️ **Camera** works by passing your **host webcam** through to the guest. |

## Requirements

- Windows 11 (x64).
- **Intel VT-x / AMD-V enabled in BIOS** and **Windows Hypervisor Platform** turned on
  (`OptionalFeatures.exe` → *Windows Hypervisor Platform*). Without this the emulator is unusable.
- ~4 GB free disk and a reasonably fast connection. The trimmed first-run download is
  **≈2.5–3 GB** (platform-tools + emulator + one Play image + scrcpy). Everything is resumable.

## Build & run

```powershell
cd AndroidOnWindows
.\build.ps1 -Run
```

`build.ps1` installs a user-local .NET 10 SDK if needed (no admin), then builds and launches.

## First run

1. **Check environment** — verifies adb / emulator / scrcpy / virtualization.
2. **Setup** — downloads the core tools (resumable).
3. Pick a **Play system image** (API 34 recommended) and **Create + Start**.
4. First boot takes a few minutes; sign into your Google account in the Play Store window.
5. In **Apps**, press **Google Play Store** or any app → it opens in its own Windows window.
6. Drag any `.apk` onto the window to install it.

## Layout

```
src/AndroidOnWindows/
  App.xaml(.cs)            application + theme
  MainWindow.xaml(.cs)     UI, drag-drop APK, keyboard-mapping hook
  Core/Mvvm.cs             tiny MVVM helpers (no NuGet dependencies)
  Models/Models.cs         app/AVD/image/settings models
  Services/
    AppPaths.cs            %LOCALAPPDATA%\AndroidOnWindows layout
    HttpDownloadService.cs resumable downloader + zip extractor
    AndroidRepository.cs   parses Google's repository XML (no sdkmanager/Java)
    SdkBootstrapper.cs     installs platform-tools + emulator + system image
    AvdService.cs          creates AVDs by writing config files directly
    EmulatorService.cs     starts the emulator headless (-no-window)
    ScrcpyService.cs       one window per app via virtual displays
    AdbService.cs          install apps, list apps, push/pull, taps, fingerprint
    ShortcutService.cs     Start-menu shortcut + taskbar pin
    KeyMapService.cs       key -> device tap mapping
    SettingsService.cs     settings.json + virtualization probe
```

Everything is stored under `%LOCALAPPDATA%\AndroidOnWindows` (SDK, tools, screenshots,
recordings, shared folder, logs). AVDs live in `%USERPROFILE%\.android\avd`.

## Notes & limits

- **Per-app windows** rely on scrcpy virtual displays, which need a recent image
  (**API 33+**; API 34 is the default). If an image doesn't support it, use
  *Whole device (1 window)* instead.
- **App labels** are derived from package names (adb can't read labels without root);
  you can tell apps apart by package, and the Play Store is always labelled.
- Automatically **pinning to the taskbar** is often blocked by Windows policy; if so,
  right-click the created Start-menu shortcut → *Pin to taskbar*.
- This project is a launcher/orchestrator. It does not bundle Android; Google's terms
  apply to the SDK and Play services you download and sign into.
