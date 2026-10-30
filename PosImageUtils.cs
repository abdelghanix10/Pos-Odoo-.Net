using System.IO;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public static class PosImageUtils
{
    public static BitmapImage? Base64ToBitmapImage(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64) || base64.Equals("false", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            byte[] bytes = Convert.FromBase64String(base64.Trim());
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze(); // Freeze for safe cross-thread usage in WPF
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static string? BitmapImageToBase64(BitmapImage? bitmap)
    {
        if (bitmap == null) return null;

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch
        {
            return null;
        }
    }
}

