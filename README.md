# xsheet-mark

A Windows overlay drawing app for annotating animation timesheets while a video keeps playing underneath.

The app sits on top of your video player (Premiere, DaVinci Resolve, VLC, etc.) as an always-on-top, per-pixel-transparent window. Click, draw, and erase on the overlay without ever stealing focus from the video — the spacebar still play/pauses the clip while you mark timings.

*日本語版: [README.ja.md](README.ja.md)*

## Features

- **Focus-preserving overlay** — draw with pen or mouse without the video player losing keyboard control
- **Pen tablet support** — pressure-sensitive ink via Windows Ink (tested on HUION; Wacom-compatible)
- **Infinite canvas** — load any number of timesheets side-by-side, pan and zoom freely
- **Per-tool widths** — pen and eraser each remember their own line-width setting
- **5 ink colors, 3 widths, point-based eraser** in the toolbar
- **Image formats** — PSD, PSB, JPG, PNG, BMP, GIF, TIFF, TGA
- **PSD export** — each image on the canvas saves as its own PSD. PSD inputs preserve their original layer structure with annotations added on top; other formats produce a fresh PSD with the image and an `xsheet-mark` ink layer
- **Move images, annotations follow** — strokes drawn on an image travel with it when you drag the image
- **Bilingual UI** — Japanese / English, auto-detected from Windows display language

## System requirements

- Windows 10 or 11 (64-bit)
- Pen tablet with **Windows Ink enabled** in the driver settings for pressure sensitivity (mouse works without it)

## Getting started

1. Download `xsheet-mark.zip` from [Releases](https://github.com/stechdrive/xsheet-mark/releases)
2. Extract anywhere and run `XsheetMark.exe`
3. Start your video player and begin playback
4. Drag one or more timesheet images onto the xsheet-mark window
5. Annotate with the pen tool — the video keeps playing and responds to keyboard shortcuts

## Usage

### Toolbar (left side)

| Icon | Tool / Action |
|---|---|
| ✎ | Pen |
| ⌫ | Eraser (removes partial strokes, not whole strokes) |
| ✥ | Move — drag an image to reposition it; strokes on the image follow |
| ● | Ink color (black / red / blue / green / yellow) |
| ▬ | Line width — pen and eraser remember their own |
| ⛶ | Fit all images to the window |
| 💾 | Export each image as its own PSD |

### Canvas controls

- **Wheel** — zoom in/out centered on the cursor
- **Middle-drag** or **right-drag** — pan the canvas
- **Top black bar** — drag to move the window; close with the ✕ button
- **Window edges** — grab to resize (cursor changes near each edge)

### PSD export

Click the 💾 button and pick an output folder. One PSD per image on the canvas is written, named `<source-name>-marked.psd`. For PSD inputs the original layer structure is preserved and a new `xsheet-mark` layer is added on top; for other formats a fresh PSD is built with the image and the ink layer.

### Pen pressure

Enable "Windows Ink" in your tablet driver's settings. WPF reads pressure via the Windows Ink / Real-Time Stylus stack; WinTab-only configurations will not pass pressure data through.

## License

MIT — see [LICENSE](LICENSE). Third-party components and their licenses are listed in [NOTICE](NOTICE).
