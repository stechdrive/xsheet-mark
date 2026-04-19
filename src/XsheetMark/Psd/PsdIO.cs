using System;
using System.Drawing;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoshopFile;

namespace XsheetMark.Psd;

/// <summary>
/// Thin facade over the vendored PsdFile library. Callers use BitmapSource
/// as the common currency regardless of the underlying PSD implementation,
/// so swapping the library out later stays local to this file.
///
/// - TryLoadComposite reads the composite from 8-bit RGB / RGBA PSD.
/// - TryExportWithInkLayer re-reads a source PSD and appends an ink layer
///   on top (preserves original layer structure).
/// - TryExportNewPsd writes a new PSD from scratch with an image layer,
///   an ink layer on top, and a flat composite so Photoshop-agnostic
///   preview tools render the intended look.
///
/// All byte buffers passed in for export must be premultiplied BGRA
/// (matches WPF RenderTargetBitmap with PixelFormats.Pbgra32).
/// </summary>
public static class PsdIO
{
    public static BitmapSource? TryLoadComposite(string path)
    {
        try
        {
            var psd = new PsdFile(path, new LoadContext());
            return BuildCompositeBitmap(psd);
        }
        catch
        {
            return null;
        }
    }

    public static bool TryExportWithInkLayer(
        string sourcePsdPath,
        byte[] inkBgraPremultiplied,
        int width,
        int height,
        string layerName,
        string outputPath)
    {
        try
        {
            var psd = new PsdFile(sourcePsdPath, new LoadContext());
            AppendBgraLayer(psd, inkBgraPremultiplied, width, height, layerName);
            psd.Save(outputPath, Encoding.Default);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryExportNewPsd(
        byte[] imageBgraPremultiplied,
        byte[] compositeBgraPremultiplied,
        byte[] inkBgraPremultiplied,
        int width,
        int height,
        string imageLayerName,
        string inkLayerName,
        string outputPath)
    {
        try
        {
            var psd = new PsdFile(PsdFileVersion.Psd)
            {
                ColorMode = PsdColorMode.RGB,
                ChannelCount = 4,
                BitDepth = 8,
                ColumnCount = width,
                RowCount = height,
                // PsdFile.SaveImage writes this top-level compression mode
                // ONCE for the composite. Without this, it would default to
                // Raw while the channels below are RLE-compressed, which
                // produces a corrupt file ("unexpected end of file" in
                // Photoshop).
                ImageCompression = ImageCompression.Rle,
            };
            SetBaseLayerFromBgra(psd, compositeBgraPremultiplied, width, height);
            AppendBgraLayer(psd, imageBgraPremultiplied, width, height, imageLayerName);
            AppendBgraLayer(psd, inkBgraPremultiplied, width, height, inkLayerName);
            psd.Save(outputPath, Encoding.Default);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static BitmapSource? BuildCompositeBitmap(PsdFile psd)
    {
        if (psd.ColorMode != PsdColorMode.RGB) return null;
        if (psd.BitDepth != 8) return null;

        int width = psd.ColumnCount;
        int height = psd.RowCount;
        if (width <= 0 || height <= 0) return null;
        int pixels = width * height;

        byte[]? r = null, g = null, b = null, a = null;
        foreach (var ch in psd.BaseLayer.Channels)
        {
            ch.DecodeImageData();
            switch (ch.ID)
            {
                case 0: r = ch.ImageData; break;
                case 1: g = ch.ImageData; break;
                case 2: b = ch.ImageData; break;
                case -1: a = ch.ImageData; break;
            }
        }

        if (r is null || g is null || b is null) return null;
        if (r.Length < pixels || g.Length < pixels || b.Length < pixels) return null;

        byte[] bgra = new byte[pixels * 4];
        for (int i = 0; i < pixels; i++)
        {
            bgra[i * 4 + 0] = b[i];
            bgra[i * 4 + 1] = g[i];
            bgra[i * 4 + 2] = r[i];
            bgra[i * 4 + 3] = a is not null && i < a.Length ? a[i] : (byte)255;
        }

        var bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32,
            palette: null, bgra, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// BaseLayer stores the flat composite preview. Photoshop uses this as
    /// the quick-open thumbnail and non-Photoshop viewers often show only
    /// this bitmap. Channel IDs for BaseLayer are 0 (R), 1 (G), 2 (B), 3 (A).
    /// </summary>
    private static void SetBaseLayerFromBgra(PsdFile psd, byte[] bgraPremult, int width, int height)
    {
        psd.BaseLayer.Rect = new Rectangle(0, 0, width, height);

        int pixels = width * height;
        byte[] r = new byte[pixels];
        byte[] g = new byte[pixels];
        byte[] b = new byte[pixels];
        byte[] a = new byte[pixels];
        UnpremultiplyBgra(bgraPremult, pixels, r, g, b, a);

        AddChannel(psd.BaseLayer, 0, r);
        AddChannel(psd.BaseLayer, 1, g);
        AddChannel(psd.BaseLayer, 2, b);
        AddChannel(psd.BaseLayer, 3, a);
    }

    /// <summary>
    /// Adds an editable layer. Regular layers use channel IDs -1 (A), 0 (R),
    /// 1 (G), 2 (B) — note the alpha-at-minus-one convention, which differs
    /// from BaseLayer.
    /// </summary>
    private static void AppendBgraLayer(
        PsdFile psd, byte[] bgraPremult, int width, int height, string layerName)
    {
        var layer = new Layer(psd)
        {
            Name = layerName,
            Rect = new Rectangle(0, 0, width, height),
            BlendModeKey = PsdBlendMode.Normal,
            Opacity = 255,
            Visible = true,
            Masks = new MaskInfo(),
        };
        layer.BlendingRangesData = new BlendingRanges(layer);

        int pixels = width * height;
        byte[] r = new byte[pixels];
        byte[] g = new byte[pixels];
        byte[] b = new byte[pixels];
        byte[] a = new byte[pixels];
        UnpremultiplyBgra(bgraPremult, pixels, r, g, b, a);

        AddChannel(layer, -1, a);
        AddChannel(layer, 0, r);
        AddChannel(layer, 1, g);
        AddChannel(layer, 2, b);

        psd.Layers.Add(layer);
    }

    private static void AddChannel(Layer layer, short id, byte[] data)
    {
        var ch = new Channel(id, layer)
        {
            ImageData = data,
            ImageCompression = ImageCompression.Rle,
        };
        layer.Channels.Add(ch);
    }

    private static void UnpremultiplyBgra(
        byte[] bgraPremult, int pixels, byte[] r, byte[] g, byte[] b, byte[] a)
    {
        for (int i = 0; i < pixels; i++)
        {
            byte pb = bgraPremult[i * 4 + 0];
            byte pg = bgraPremult[i * 4 + 1];
            byte pr = bgraPremult[i * 4 + 2];
            byte pa = bgraPremult[i * 4 + 3];

            a[i] = pa;
            if (pa == 0)
            {
                r[i] = 0; g[i] = 0; b[i] = 0;
            }
            else if (pa == 255)
            {
                r[i] = pr; g[i] = pg; b[i] = pb;
            }
            else
            {
                r[i] = (byte)Math.Min(255, (pr * 255 + pa / 2) / pa);
                g[i] = (byte)Math.Min(255, (pg * 255 + pa / 2) / pa);
                b[i] = (byte)Math.Min(255, (pb * 255 + pa / 2) / pa);
            }
        }
    }
}
