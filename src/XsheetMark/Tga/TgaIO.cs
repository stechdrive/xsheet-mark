using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pfim;

namespace XsheetMark.Tga;

/// <summary>
/// Thin facade over Pfim for loading TGA files into a WPF BitmapSource.
/// Pfim's byte order (B,G,R / B,G,R,A) matches WPF's Bgr24 / Bgra32, so
/// the pixel data is handed off with no conversion.
/// </summary>
public static class TgaIO
{
    public static BitmapSource? TryLoad(string path)
    {
        try
        {
            using var image = Pfimage.FromFile(path);
            var fmt = image.Format switch
            {
                Pfim.ImageFormat.Rgb24 => PixelFormats.Bgr24,
                Pfim.ImageFormat.Rgba32 => PixelFormats.Bgra32,
                _ => (PixelFormat?)null,
            };
            if (fmt is null) return null;

            var bitmap = BitmapSource.Create(
                image.Width, image.Height,
                96, 96,
                fmt.Value,
                palette: null,
                image.Data,
                image.Stride);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
