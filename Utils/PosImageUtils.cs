using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace OdooPosLogin;

public static class PosImageUtils
{
    public static BitmapImage? Base64ToBitmapImage(string? base64)
    {
        string? cleaned = CleanBase64(base64);
        if (string.IsNullOrEmpty(cleaned))
            return null;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(cleaned);
        }
        catch
        {
            return null;
        }

        // Reject tiny payloads (e.g. accidental bin_size strings like "14.2 Kb")
        if (bytes.Length < 24)
            return null;

        // 1. Check if the payload is an SVG XML image (Odoo user avatars, letter avatars, etc.)
        if (IsSvgPayload(bytes))
        {
            var svgBitmap = RenderSvgToBitmapImage(bytes);
            if (svgBitmap != null)
                return svgBitmap;
        }

        // 2. Standard raster image (PNG, JPEG, BMP, GIF, WebP/WIC)
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
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

    private static string? CleanBase64(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string s = raw.Trim();
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Reject Odoo bin_size strings such as "18.45 Kb" or "120 bytes"
        if (s.Length < 40 && (s.EndsWith("Kb", StringComparison.OrdinalIgnoreCase) ||
                              s.EndsWith("Mb", StringComparison.OrdinalIgnoreCase) ||
                              s.EndsWith("bytes", StringComparison.OrdinalIgnoreCase) ||
                              s.EndsWith("B", StringComparison.OrdinalIgnoreCase) && s.Contains(' ')))
        {
            return null;
        }

        // Strip data URI prefix if present (e.g. "data:image/png;base64,...")
        int commaIdx = s.IndexOf(',');
        if (commaIdx >= 0 && s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            s = s[(commaIdx + 1)..].Trim();
        }

        // Remove whitespace/newlines
        if (s.Contains('\n') || s.Contains('\r') || s.Contains(' '))
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (!char.IsWhiteSpace(c))
                    sb.Append(c);
            }
            s = sb.ToString();
        }

        // Normalize URL-safe base64 if needed
        s = s.Replace('-', '+').Replace('_', '/');
        int mod4 = s.Length % 4;
        if (mod4 > 0)
        {
            s += new string('=', 4 - mod4);
        }

        return s;
    }

    private static bool IsSvgPayload(byte[] bytes)
    {
        try
        {
            int inspectLen = Math.Min(bytes.Length, 512);
            string header = Encoding.UTF8.GetString(bytes, 0, inspectLen).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            return header.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
                   header.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
                   header.Contains("<svg", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Renders an Odoo SVG (such as res.users illustrated avatars or letter avatars) into a frozen WPF BitmapImage.
    /// </summary>
    private static BitmapImage? RenderSvgToBitmapImage(byte[] svgBytes)
    {
        try
        {
            string xmlText = Encoding.UTF8.GetString(svgBytes).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            var doc = XDocument.Parse(xmlText);
            var svgRoot = doc.Root;
            if (svgRoot == null) return null;

            double viewWidth = ParseDoubleAttr(svgRoot, "width", 180);
            double viewHeight = ParseDoubleAttr(svgRoot, "height", 180);

            string? viewBox = svgRoot.Attribute("viewBox")?.Value;
            double vbMinX = 0, vbMinY = 0;
            if (!string.IsNullOrWhiteSpace(viewBox))
            {
                var parts = viewBox.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 &&
                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &&
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double y) &&
                    double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double w) &&
                    double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double h) &&
                    w > 0 && h > 0)
                {
                    vbMinX = x;
                    vbMinY = y;
                    viewWidth = w;
                    viewHeight = h;
                }
            }

            if (viewWidth <= 0) viewWidth = 180;
            if (viewHeight <= 0) viewHeight = 180;

            const int targetPx = 160;
            double scaleX = targetPx / viewWidth;
            double scaleY = targetPx / viewHeight;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scaleX, scaleY));
                if (Math.Abs(vbMinX) > 0.001 || Math.Abs(vbMinY) > 0.001)
                {
                    dc.PushTransform(new TranslateTransform(-vbMinX, -vbMinY));
                }

                RenderSvgElementRecursive(dc, svgRoot, Brushes.Transparent, viewWidth, viewHeight);

                if (Math.Abs(vbMinX) > 0.001 || Math.Abs(vbMinY) > 0.001)
                {
                    dc.Pop();
                }
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(targetPx, targetPx, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var outStream = new MemoryStream();
            encoder.Save(outStream);
            outStream.Position = 0;

            var result = new BitmapImage();
            result.BeginInit();
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.StreamSource = outStream;
            result.EndInit();
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static void RenderSvgElementRecursive(DrawingContext dc, XElement element, Brush inheritedFill, double viewWidth, double viewHeight)
    {
        string localName = element.Name.LocalName.ToLowerInvariant();
        if (localName is "defs" or "clippath" or "mask" or "style" or "title" or "desc")
            return;

        bool pushedTransform = false;
        var transform = ParseSvgTransform(element.Attribute("transform")?.Value);
        if (transform != null)
        {
            dc.PushTransform(transform);
            pushedTransform = true;
        }

        bool pushedOpacity = false;
        double opacity = ParseSvgOpacity(element);
        if (opacity < 0.999)
        {
            dc.PushOpacity(opacity);
            pushedOpacity = true;
        }

        Brush currentFill = ParseSvgFillBrush(element, inheritedFill);
        Pen? currentPen = ParseSvgStrokePen(element);

        try
        {
            switch (localName)
            {
                case "svg":
                case "g":
                    foreach (var child in element.Elements())
                    {
                        RenderSvgElementRecursive(dc, child, currentFill, viewWidth, viewHeight);
                    }
                    break;

                case "rect":
                {
                    double x = ParseDoubleAttr(element, "x", 0);
                    double y = ParseDoubleAttr(element, "y", 0);
                    double w = ParseDoubleAttr(element, "width", viewWidth);
                    double h = ParseDoubleAttr(element, "height", viewHeight);
                    double rx = ParseDoubleAttr(element, "rx", 0);
                    double ry = ParseDoubleAttr(element, "ry", rx);
                    if (w > 0 && h > 0)
                    {
                        dc.DrawRoundedRectangle(currentFill, currentPen, new Rect(x, y, w, h), rx, ry);
                    }
                    break;
                }

                case "circle":
                {
                    double cx = ParseDoubleAttr(element, "cx", viewWidth / 2.0);
                    double cy = ParseDoubleAttr(element, "cy", viewHeight / 2.0);
                    double r = ParseDoubleAttr(element, "r", 0);
                    if (r > 0)
                    {
                        dc.DrawEllipse(currentFill, currentPen, new Point(cx, cy), r, r);
                    }
                    break;
                }

                case "ellipse":
                {
                    double cx = ParseDoubleAttr(element, "cx", viewWidth / 2.0);
                    double cy = ParseDoubleAttr(element, "cy", viewHeight / 2.0);
                    double rx = ParseDoubleAttr(element, "rx", 0);
                    double ry = ParseDoubleAttr(element, "ry", 0);
                    if (rx > 0 && ry > 0)
                    {
                        dc.DrawEllipse(currentFill, currentPen, new Point(cx, cy), rx, ry);
                    }
                    break;
                }

                case "path":
                {
                    string? d = element.Attribute("d")?.Value;
                    if (!string.IsNullOrWhiteSpace(d))
                    {
                        var geom = Geometry.Parse(d);
                        dc.DrawGeometry(currentFill, currentPen, geom);
                    }
                    break;
                }

                case "polygon":
                case "polyline":
                {
                    string? pts = element.Attribute("points")?.Value;
                    if (!string.IsNullOrWhiteSpace(pts))
                    {
                        string prefix = localName == "polygon" ? "M " : "M ";
                        string suffix = localName == "polygon" ? " Z" : "";
                        var geom = Geometry.Parse(prefix + pts + suffix);
                        dc.DrawGeometry(localName == "polygon" ? currentFill : null, currentPen, geom);
                    }
                    break;
                }

                case "text":
                {
                    string content = element.Value?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(content))
                    {
                        double fontSize = ParseDoubleAttr(element, "font-size", viewHeight * 0.48);
                        if (fontSize <= 0) fontSize = viewHeight * 0.48;

                        Brush textBrush = currentFill == Brushes.Transparent ? Brushes.White : currentFill;
                        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                        var formatted = new FormattedText(
                            content,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            fontSize,
                            textBrush,
                            1.0);

                        double tx = (viewWidth - formatted.Width) / 2.0;
                        double ty = (viewHeight - formatted.Height) / 2.0;
                        dc.DrawText(formatted, new Point(tx, ty));
                    }
                    break;
                }
            }
        }
        catch
        {
            // Ignore individual unsupported SVG shape syntax and continue rendering
        }
        finally
        {
            if (pushedOpacity) dc.Pop();
            if (pushedTransform) dc.Pop();
        }
    }

    private static double ParseDoubleAttr(XElement el, string attrName, double defaultVal)
    {
        string? raw = el.Attribute(attrName)?.Value ?? GetStyleProperty(el, attrName);
        if (string.IsNullOrWhiteSpace(raw)) return defaultVal;

        raw = raw.Trim();
        if (raw.EndsWith('%'))
        {
            if (double.TryParse(raw.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out double pct))
                return defaultVal * (pct / 100.0);
        }

        raw = raw.Replace("px", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("pt", "", StringComparison.OrdinalIgnoreCase)
                 .Trim();

        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : defaultVal;
    }

    private static double ParseSvgOpacity(XElement el)
    {
        double op = ParseDoubleAttr(el, "opacity", 1.0);
        double fillOp = ParseDoubleAttr(el, "fill-opacity", 1.0);
        return Math.Clamp(op * fillOp, 0.0, 1.0);
    }

    private static Brush ParseSvgFillBrush(XElement el, Brush fallback)
    {
        string? fillStr = el.Attribute("fill")?.Value ?? GetStyleProperty(el, "fill");
        if (string.IsNullOrWhiteSpace(fillStr))
        {
            // In SVG, default fill on shapes is black if not specified anywhere
            return fallback == Brushes.Transparent && el.Name.LocalName.ToLowerInvariant() is "path" or "rect" or "circle" or "ellipse" or "polygon"
                ? Brushes.Black
                : fallback;
        }

        fillStr = fillStr.Trim();
        if (fillStr.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            fillStr.Equals("transparent", StringComparison.OrdinalIgnoreCase) ||
            fillStr.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.Transparent;
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(fillStr);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return fallback;
        }
    }

    private static Pen? ParseSvgStrokePen(XElement el)
    {
        string? strokeStr = el.Attribute("stroke")?.Value ?? GetStyleProperty(el, "stroke");
        if (string.IsNullOrWhiteSpace(strokeStr) || strokeStr.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(strokeStr.Trim());
            double width = ParseDoubleAttr(el, "stroke-width", 1.0);
            var pen = new Pen(new SolidColorBrush(color), Math.Max(0.5, width));
            pen.Freeze();
            return pen;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStyleProperty(XElement el, string propName)
    {
        string? style = el.Attribute("style")?.Value;
        if (string.IsNullOrWhiteSpace(style)) return null;

        var declarations = style.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (string decl in declarations)
        {
            int colon = decl.IndexOf(':');
            if (colon > 0)
            {
                string key = decl[..colon].Trim();
                if (key.Equals(propName, StringComparison.OrdinalIgnoreCase))
                {
                    return decl[(colon + 1)..].Trim();
                }
            }
        }
        return null;
    }

    private static Transform? ParseSvgTransform(string? transformStr)
    {
        if (string.IsNullOrWhiteSpace(transformStr)) return null;
        transformStr = transformStr.Trim();

        try
        {
            int openParen = transformStr.IndexOf('(');
            int closeParen = transformStr.IndexOf(')');
            if (openParen <= 0 || closeParen <= openParen) return null;

            string fn = transformStr[..openParen].Trim().ToLowerInvariant();
            string argsRaw = transformStr[(openParen + 1)..closeParen];
            var nums = argsRaw.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                              .Select(s => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ? d : 0.0)
                              .ToArray();

            return fn switch
            {
                "translate" when nums.Length >= 1 => new TranslateTransform(nums[0], nums.Length > 1 ? nums[1] : 0),
                "scale" when nums.Length >= 1 => new ScaleTransform(nums[0], nums.Length > 1 ? nums[1] : nums[0]),
                "matrix" when nums.Length == 6 => new MatrixTransform(nums[0], nums[1], nums[2], nums[3], nums[4], nums[5]),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}