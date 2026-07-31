# Snapshot

Snapshot is a lightweight, offline Windows screenshot utility that combines smart window selection with a second hotkey for saving directly to disk.

## Highlights

- Two configurable global hotkeys
- Smart top-level window detection
- Free-form region capture
- Move and resize a selected region before confirming
- Copy directly to the clipboard
- Save directly as JPG without a file dialog
- System tray settings
- No accounts, uploads, telemetry, or network dependency

## Download

Download the latest Windows x64 package from [GitHub Releases](https://github.com/wongzijie/Snapshot/releases/latest).

Extract the ZIP and run `Snapshot.exe`. The release is self-contained and does not require a separate .NET installation.

> Snapshot is not commercially code-signed yet. Windows may identify the publisher as unknown. Verify the SHA-256 shown on the Release page or build from source if you prefer.

## Default shortcuts

| Action | Shortcut | Result |
|---|---|---|
| Capture to clipboard | `Ctrl + Alt + A` | Paste immediately into another app |
| Capture to JPG | `Ctrl + Alt + S` | Save directly to the Windows Downloads folder |

After activation, click a highlighted window or drag a custom region. Press `Enter` or double-click to confirm; press `Esc` or right-click to cancel.

## Build from source

Requirements: Windows 10/11 x64 and the .NET 10 SDK.

```powershell
git clone https://github.com/wongzijie/Snapshot.git
cd Snapshot
dotnet build .\Snapshot.csproj -c Release
.\build-release.ps1
```

The self-contained executable is written to `dist\Snapshot.exe`.

For the complete documentation, see the [Chinese README](README.md) and [usage guide](docs/USAGE.zh-CN.md).
