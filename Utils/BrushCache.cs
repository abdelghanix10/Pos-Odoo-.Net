using System.Collections.Concurrent;
using System.Windows.Media;

namespace OdooPosLogin;

public static class BrushCache
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush Get(string? hex, string fallback = "#9CA3AF")
    {
        string key = Normalize(hex, fallback);
        return Cache.GetOrAdd(key, CreateBrush);
    }

    public static SolidColorBrush GetAlpha(string? hex, byte alpha, string fallback = "#9CA3AF")
    {
        string baseKey = Normalize(hex, fallback);
        string key = $"A{alpha}:{baseKey}";
        return Cache.GetOrAdd(key, _ =>
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(baseKey);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                brush.Freeze();
                return brush;
            }
            catch
            {
                var fallbackColor = (Color)ColorConverter.ConvertFromString(Normalize(null, fallback));
                var brush = new SolidColorBrush(Color.FromArgb(alpha, fallbackColor.R, fallbackColor.G, fallbackColor.B));
                brush.Freeze();
                return brush;
            }
        });
    }

    private static string Normalize(string? hex, string fallback)
    {
        string key = string.IsNullOrWhiteSpace(hex) ? fallback : hex.Trim();
        return key.StartsWith('#') ? key : "#" + key;
    }

    private static SolidColorBrush CreateBrush(string key)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(key));
            brush.Freeze();
            return brush;
        }
        catch
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
            brush.Freeze();
            return brush;
        }
    }
}
