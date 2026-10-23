using System.IO;
#if !NO_WPF
using System.Windows.Media;
using System.Windows.Media.Imaging;
#endif

namespace PolyView;

/// <summary>把 PNG/JPEG 等图片解码成 BGRA32 像素，交给 D3D11 当纹理用。</summary>
public static class TextureDecoder
{
    public static MeshTexture? Decode(byte[] encoded)
    {
#if NO_WPF
        return null;
#else
        try
        {
            using var stream = new MemoryStream(encoded);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource source = frame;
            if (source.Format != PixelFormats.Bgra32)
            {
                source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            }

            int width = source.PixelWidth;
            int height = source.PixelHeight;
            var pixels = new byte[width * height * 4];
            source.CopyPixels(pixels, width * 4, 0);
            return new MeshTexture { Width = width, Height = height, Bgra = pixels };
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return null;
        }
#endif
    }
}
