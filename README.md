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
- **Undo / Redo** for strokes, image drops, image moves, and canvas clears
- **Separate opacity for window and images** — see through the overlay or fade images to compare marks
- **Image formats** — PSD, PSB, JPG, PNG, BMP, GIF, TIFF, TGA
- **PSD export (per image)** — each image on the canvas saves as its own PSD. PSD inputs preserve their original layer structure with annotations added on top; other formats produce a fresh PSD with the image and an `xsheet-mark` ink layer
- **PSD capture (viewport snapshot)** — a separate button captures whatever is currently visible in the viewport (images + ink at current zoom/pan) as a single PSD with a **transparent background**, for compositing the notes back over other content
- **Move images, annotations follow** — strokes drawn on an image travel with it when you drag the image
- **Reset canvas** — clear images and strokes in one click; undoable if you change your mind
- **Bilingual UI** — Japanese / English, auto-detected from Windows display language

## System requirements

- Windows 10 or 11 (64-bit)
- Pen tablet with **Windows Ink enabled** in the driver settings for pressure sensitivity (mouse works without it)

## Getting started

1. Download `xsheet-mark.zip` from [Releases](https://github.com/stechdrive/xsheet-mark/releases)
2. Extract anywhere and run `XsheetMark.exe`
3. Start your video player and begin playback
4. Drag one or more timesheet images onto the xsheet-mark window (or start drawing straight away on the empty canvas)
5. Annotate with the pen tool — the video keeps playing and responds to keyboard shortcuts

## Usage

### Window chrome

- **Top black bar** — drag to move the window
- **— (minimize)** — collapses the window to the taskbar; click the taskbar icon to restore
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
| ⌫ | Eraser (removes partial strokes, not whole strokes; size follows the line-width selector) |
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
- 📷 **Capture** — one viewport-sized PSD regardless of images, with a **transparent background**, for the "annotate on a transparent overlay and save" workflow.

Both write to a folder you pick, and file-name collisions get a numeric suffix (`-2`, `-3`, ...).

### Pen pressure

Enable "Windows Ink" in your tablet driver's settings. WPF reads pressure via the Windows Ink / Real-Time Stylus stack; WinTab-only configurations will not pass pressure data through.

## License

MIT — see [LICENSE](LICENSE). Third-party components and their licenses are listed in [NOTICE](NOTICE).
