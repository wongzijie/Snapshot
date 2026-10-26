# Snapshot

![Snapshot project overview](docs/assets/showcase/cover.png)

Snapshot is a lightweight Windows screenshot utility that combines smart window selection, direct-to-disk capture, annotation, local OCR, and optional text translation.

## Highlights

- Two configurable global hotkeys
- Smart top-level window detection
- Free-form region capture
- Move and resize a selected region before confirming
- Pen, arrow, mosaic, color, stroke width, undo, and redo tools
- Transparent, independently editable text boxes with drag repositioning and configurable font, style, size, and color
- Local OCR powered by installed Windows language packs
- Optional in-place image translation that covers detected source text and draws translated text back at the same coordinates
- Copy directly to the clipboard
- Save directly as JPG without a file dialog
- System tray settings
- No accounts or telemetry; screenshot pixels and OCR stay local

## Download

Download the latest Windows x64 package from [GitHub Releases](https://github.com/wongzijie/Snapshot/releases/latest).

Extract the ZIP and run `Snapshot.exe`. The release is self-contained and does not require a separate .NET installation.

> Snapshot is not commercially code-signed yet. Windows may identify the publisher as unknown. Verify the SHA-256 shown on the Release page or build from source if you prefer.

## Default shortcuts

| Action | Shortcut | Result |
|---|---|---|
| Capture to clipboard | `Ctrl + Alt + A` | Paste immediately into another app |
| Capture to JPG | `Ctrl + Alt + S` | Save directly to the Windows Downloads folder |

After activation, click a highlighted window or drag a custom region. Select the text tool and click anywhere in the capture to type directly; the editor has no background fill, `Ctrl+Enter` commits it, and the toolbar controls font, style, size, and color. Click completed text again to edit it, drag across the text body to select characters, or drag the green dashed border to reposition the entire text box. Use the other tools to annotate, mosaic, run OCR, or generate an in-place translated image. Press `Enter` or double-click to confirm; press `Esc` or right-click to cancel. `Ctrl+Z` and `Ctrl+Y` undo and redo edits.

OCR runs entirely on the device. Translation is opt-in and requires your own Microsoft Translator key; only recognized text is sent to the configured endpoint, never the screenshot image. The key is encrypted for the current Windows user with DPAPI.

## Build from source

Requirements: Windows 10 version 2004 or later / Windows 11 x64, and the .NET 10 SDK.

```powershell
git clone https://github.com/wongzijie/Snapshot.git
cd Snapshot
dotnet build .\Snapshot.csproj -c Release
.\build-release.ps1
```

The self-contained executable is written to `dist\Snapshot.exe`.

For the complete documentation, see the [Chinese README](README.md) and [usage guide](docs/USAGE.zh-CN.md).

## Visual guide

![Source-backed workflow illustration, not a runtime screenshot](docs/assets/showcase/workflow.png)

[Image gallery and evidence](docs/assets/showcase/README.md). This documentation update does not revalidate the application runtime.
