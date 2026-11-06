# Mobi-Top

Use your Android phone as a desktop computer, on any Windows laptop or PC.
Plug the phone in by USB and Mobi-Top shows a full desktop (taskbar, start menu, widgets,
notifications, music controls) in a window on the laptop, driven by the laptop's mouse and keyboard.
Each app you open gets its own window inside the desktop. There's also a plain screen mirror.

## Parts

| Folder | What it is |
|---|---|
| `app/` | The Android app (Java, no libraries): the desktop, start menu, widgets, notification panel |
| `tools/mobitop-laptop.ps1` | Runs a Mobi-Top session on the laptop: creates the phone screens with scrcpy, opens app windows, sizes them |
| `desktop/` | The Windows launcher (`Launcher.cs`) and installer (`Setup.cs`), plus `build.ps1` |

## Build

1. Android app: `gradlew assembleDebug` (needs the Android SDK; set `sdk.dir` in `local.properties`).
   Copy `app/build/outputs/apk/debug/app-debug.apk` to `Mobi-Top.apk` in this folder.
2. Download [scrcpy v5.0.1 for Windows](https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1)
   and unzip it to `tools/scrcpy-win64-v5.0.1/`.
3. Windows installer: `powershell -ExecutionPolicy Bypass -File desktop/build.ps1`
   (needs Visual Studio Build Tools' C# compiler; the result runs on any Windows 10/11 with nothing else installed).
   Output: `dist/MobiTop-Setup.exe`.

## Phone setup

- Developer options: USB debugging on (on Xiaomi also "Install via USB" and "USB debugging (Security settings)").
- Leave "Force desktop mode" **off**: every app gets its own screen, so it isn't needed.
- Grant Mobi-Top notification access and file access from the desktop when asked.

## Credits

Screen and input forwarding by [scrcpy](https://github.com/Genymobile/scrcpy) (Apache License 2.0), bundled unmodified in the installer.
