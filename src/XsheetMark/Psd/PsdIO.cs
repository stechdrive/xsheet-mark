using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoshopFile;

namespace XsheetMark.Psd;

/// <summary>
/// Thin facade over the vendored PsdFile library. Callers use BitmapSource
/// as the common currency regardless of the underlying PSD implementation,
/// so swapping the library out later stays local to this file.
/// Currently supports reading the composite image from 8-bit RGB / RGBA
/// .psd and .psb files. Other color modes (Grayscale, CMYK, Indexed) and
/// higher bit depths return null until needed.
/// </summary>
public static class PsdIO
{
    public static BitmapSource? TryLoadComposite(string path)
    {
        try
        {
            var psd = new PsdFile(path, new LoadContext());

            if (psd.ColorMode != PsdColorMode.RGB) return null;
            if (psd.BitDepth != 8) return null;

            int width = psd.ColumnCount;
            int height = psd.RowCount;
            if (width <= 0 || height <= 0) return null;
            int pixels = width * height;

            var channels = psd.BaseLayer.Channels;
            byte[]? r = null, g = null, b = null, a = null;
            foreach (var ch in channels)
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
                width, height,
                96, 96,
                PixelFormats.Bgra32,
                palette: null,
                bgra,
                width * 4);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
