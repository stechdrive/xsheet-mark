# xsheet-mark

A transparent always-on-top drawing overlay for Windows. Write pen or mouse annotations on top of any running app; the overlay never interrupts the app underneath from receiving keyboard and pointer input.

*日本語版: [README.ja.md](README.ja.md)*

## What it's good for

- **Spotting in a video editor** — scrub playback in Premiere / DaVinci Resolve / VLC etc., draw timing marks on top, export per-image PSD
- **Annotating a reference image** — drop a still or sheet onto the canvas, mark it up, export
- **Animation timesheet work** (the original use case) — load PSD timesheets, write pen notes, export PSD with annotations as a new top layer while original layers are preserved
- **Markup on any other app** — slide decks, browser tabs, internal tools — the overlay stays above and passes keyboard through

## Features

- **Transparent always-on-top overlay** that doesn't steal keyboard or pointer input from the app below
- **Pen tablet support** — pressure-sensitive ink via Windows Ink (tested on HUION; Wacom-compatible)
- **Infinite canvas** — drop any number of images side-by-side, pan and zoom freely
- **5 ink colors, 3 widths, point-based eraser**; pen and eraser each remember their own width
- **Undo / Redo** for strokes, image drops, image moves, and canvas clears
- **Separate opacity sliders** for the whole window and for loaded images
- **Image formats** — PSD, PSB, JPG, PNG, BMP, GIF, TIFF, TGA
- **PSD export, two modes**:
  - **💾 per image** — each loaded image saves as its own PSD. PSD inputs preserve original layer structure and get an `xsheet-mark` ink layer added on top; other formats get a fresh PSD with image + ink
  - **📷 viewport capture** — whatever is currently visible saves as a single PSD with a transparent background, for compositing the notes back into other work
- **Move images, annotations follow** — strokes drawn on an image travel with it when you drag
- **Reset canvas** in one click; undoable
- **Remembers window size, position, and opacity** between sessions
- **Bilingual UI** — Japanese / English, auto-detected from Windows display language

## System requirements

- Windows 10 or 11 (64-bit)
- Pen tablet with **Windows Ink enabled** in the driver settings for pressure sensitivity (mouse works without it)

## Getting started

1. Download `xsheet-mark-v*.zip` from [Releases](https://github.com/stechdrive/xsheet-mark/releases)
2. Extract — you get an `xsheet-mark/` folder containing `XsheetMark.exe` and its WPF native DLLs
3. Run `XsheetMark.exe` from inside that folder (it needs its sibling DLLs)
4. Drop images onto the window (or start drawing on the empty canvas), annotate, export with 💾 or 📷

## Usage

### Window chrome

- **Top black bar** — drag to move the window
- **— (minimize)** — collapses to the taskbar; click the taskbar icon to restore
- **✕ (close)** — quit the app
- **Window edges** — grab to resize (cursor changes near each edge)
- **Opacity panel (top-right)** — two sliders:
  - *Window* (0.2 – 1.0): see through the whole overlay to the app underneath
  - *Image* (0.0 – 1.0): fade all loaded images uniformly (ink stays fully visible)

### Toolbar (left side)

| Icon | Action |
|---|---|
| ↶ | Undo the last stroke / erase / image drop / image move / reset |
| ↷ | Redo |
| ✎ | Pen |
| ⌫ | Eraser (removes partial strokes; size follows the line-width selector) |
| ✥ | Move — drag an image to reposition it; strokes on the image follow |
| ● | Ink color (black / red / blue / green / yellow) |
| ▬ | Line width — pen and eraser remember their own |
| ⛶ | Fit all images to the window |
| 💾 | Export each image as its own PSD |
| 📷 | Capture the current viewport as a single PSD (transparent background) |
| 🗑 | Clear the canvas (undoable) |

### Canvas controls

- **Wheel** — zoom in/out centered on the cursor
- **Middle-drag** or **right-drag** — pan the canvas
- **Drop image files** anywhere on the window to load them
- You can draw **without loading any image** — the whole canvas is ink-ready from the start

### Exporting

Two buttons, two intents:

- 💾 **Export** — one PSD per image on the canvas, named `<source-name>-marked.psd`. PSD inputs keep their original layer structure; the embedded thumbnail and composite preview ("Maximize Compatibility" payload) are regenerated so file-browser previews show the annotated version. When the canvas has no images, this button falls back to a single viewport-sized PSD.
- 📷 **Capture** — one viewport-sized PSD regardless of images, with a **transparent background**, for overlay-annotation workflows where the marks will be composited over other content.

Both write to a folder you pick, and file-name collisions get a numeric suffix (`-2`, `-3`, ...).

### Pen pressure

Enable "Windows Ink" in your tablet driver's settings. WPF reads pressure via the Windows Ink / Real-Time Stylus stack; WinTab-only configurations will not pass pressure data through.

## License

MIT — see [LICENSE](LICENSE). Third-party components and their licenses are listed in [NOTICE](NOTICE).
