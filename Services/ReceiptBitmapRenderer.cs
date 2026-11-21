using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace OdooPosLogin;

public readonly record struct RenderedReceiptBitmap(byte[] BgraPixels, int Width, int Height);

public static class ReceiptBitmapRenderer
{
    private const double LogicalWidth = 300.0;
    private static readonly FontFamily UiFont = new("Segoe UI, Cairo, Arial");
    private static readonly FontFamily ArabicFont = new("Cairo, Segoe UI, Tahoma");
    private static readonly FontFamily MonoFont = new("Consolas, Courier New");

    public static string ResolveCurrencyLabel(CompanyRecord? company)
    {
        string raw = company?.CurrencyName?.Trim() ?? "MAD";
        if (raw.Equals("MAD", StringComparison.OrdinalIgnoreCase) || raw.Equals("DH", StringComparison.OrdinalIgnoreCase)) return "DH";
        if (raw.Equals("EUR", StringComparison.OrdinalIgnoreCase)) return "€";
        if (raw.Equals("USD", StringComparison.OrdinalIgnoreCase)) return "$";
        return string.IsNullOrWhiteSpace(raw) ? "DH" : raw;
    }

    private static string Money(decimal amount, string currency) =>
        string.Format(CultureInfo.InvariantCulture, "{0:N2} {1}", amount, currency);

    private static string Qty(decimal qty) =>
        qty == Math.Truncate(qty)
            ? ((long)qty).ToString(CultureInfo.InvariantCulture)
            : qty.ToString("0.##", CultureInfo.InvariantCulture);

    public static RenderedReceiptBitmap RenderOrderReceipt(
        PosOrderRecord order, ReceiptDesignRecord? design, CompanyRecord? company,
        string shopName, bool printHeaderLogo, int targetDotWidth)
    {
        return RunOnUiThread(() =>
        {
            string currency = ResolveCurrencyLabel(company);
            var root = CreateRoot();
            var stack = new StackPanel();
            root.Child = stack;

            if (printHeaderLogo && company?.LogoBitmap != null)
            {
                var logo = new Image
                {
                    Source = company.LogoBitmap,
                    MaxWidth = 145,
                    MaxHeight = 115,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 8)
                };
                RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
                stack.Children.Add(logo);
            }

            string companyName = !string.IsNullOrWhiteSpace(company?.Name) ? company.Name : shopName;
            stack.Children.Add(CenteredText(companyName, 16.5, FontWeights.Bold, UiFont));
            string address = BuildAddress(company);
            if (!string.IsNullOrWhiteSpace(address))
                stack.Children.Add(CenteredText(address, 11.5, FontWeights.Normal, UiFont));
            if (!string.IsNullOrWhiteSpace(company?.Phone))
                stack.Children.Add(CenteredText(company.Phone.Trim(), 11.5, FontWeights.Normal, UiFont));

            stack.Children.Add(Rule(2, new Thickness(0, 4, 0, 6)));
            stack.Children.Add(LeftRight("Date:", (order.DateOrder ?? DateTime.Now).ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture), UiFont, 12.5));
            stack.Children.Add(Rule(2, new Thickness(0, 2, 0, 4)));
            stack.Children.Add(ThreeColumns("Product", "Qty", "Price", UiFont, 12.5, FontWeights.SemiBold));
            stack.Children.Add(Rule(1, new Thickness(0, 2, 0, 2)));

            foreach (var line in order.Lines)
            {
                stack.Children.Add(ThreeColumns(line.ProductName, Qty(line.Qty),
                    Money(line.PriceSubtotalIncl, currency), UiFont, 12.5,
                    FontWeights.Normal, col3Bold: true));
                stack.Children.Add(DashedRule());
            }

            stack.Children.Add(Rule(2, new Thickness(0, 4, 0, 6)));
            stack.Children.Add(LeftRight("TOTAL:", Money(order.AmountTotal, currency), UiFont, 16, FontWeights.Bold, FontWeights.Bold));
            stack.Children.Add(Rule(2, new Thickness(0, 0, 0, 8)));

            bool showPayments = design != null &&
                (design.Name.Contains("Pastry", StringComparison.OrdinalIgnoreCase) ||
                 design.ReceiptXml?.Contains("props.paymentlines", StringComparison.OrdinalIgnoreCase) == true);
            if (showPayments)
            {
                foreach (var payment in order.Payments)
                    stack.Children.Add(LeftRight(payment.PaymentMethodName, Money(payment.Amount, currency), UiFont, 12.5));
                if (order.ChangeAmount > 0)
                    stack.Children.Add(LeftRight("Change:", Money(order.ChangeAmount, currency), UiFont, 12.5, FontWeights.Bold, FontWeights.Bold));
            }

            stack.Children.Add(CenteredText("★ شكراً لشرائك ★", 14, FontWeights.Bold, ArabicFont,
                FlowDirection.RightToLeft));
            stack.Children.Add(CenteredText("★ نحن نقدر تعاملكم معنا ونتطلع إلى خدمتكم مرة أخرى ★",
                10.5, FontWeights.SemiBold, ArabicFont, FlowDirection.RightToLeft, true));
            return Rasterize(root, targetDotWidth);
        });
    }

    public static RenderedReceiptBitmap RenderSecondReceipt(
        PosOrderRecord order, CompanyRecord? company, int targetDotWidth)
    {
        return RunOnUiThread(() =>
        {
            string currency = ResolveCurrencyLabel(company);
            var root = CreateRoot();
            var stack = new StackPanel();
            root.Child = stack;
            stack.Children.Add(LeftRight("Date:", (order.DateOrder ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), MonoFont, 12.5));
            stack.Children.Add(LeftRight("Order No:", !string.IsNullOrWhiteSpace(order.PosReference) ? order.PosReference : order.Name, MonoFont, 12.5));
            stack.Children.Add(DashedRule());
            stack.Children.Add(ThreeColumns("Item", "Qty", "Price", MonoFont, 12.5, FontWeights.Bold));
            stack.Children.Add(Rule(1.5, new Thickness(0, 2, 0, 5)));
            foreach (var line in order.Lines)
            {
                stack.Children.Add(ThreeColumns(line.ProductName, Qty(line.Qty),
                    Money(line.PriceSubtotalIncl, currency), MonoFont, 12.5,
                    FontWeights.Bold, col2Normal: true, col3Normal: true));
                stack.Children.Add(DashedRule());
            }
            return Rasterize(root, targetDotWidth);
        });
    }

    public static RenderedReceiptBitmap RenderSaleDetailsReport(
        PosSessionRecord session, PosConfigRecord config, CompanyRecord? company,
        List<PosOrderRecord> sessionOrders,
        Dictionary<int, string> productCategoryByOdooId,
        Dictionary<string, string> productCategoryByName,
        int targetDotWidth)
    {
        return RunOnUiThread(() =>
        {
            string currency = ResolveCurrencyLabel(company);
            var root = CreateRoot();
            var stack = new StackPanel();
            root.Child = stack;

            string name = !string.IsNullOrWhiteSpace(company?.Name) ? company.Name : config.Name;
            stack.Children.Add(CenteredText(name, 14, FontWeights.Bold, MonoFont));
            string address = BuildAddress(company, true);
            if (!string.IsNullOrWhiteSpace(address))
                stack.Children.Add(CenteredText(address, 11.5, FontWeights.Bold, MonoFont));
            if (!string.IsNullOrWhiteSpace(company?.Phone))
                stack.Children.Add(CenteredText($"Phone: {company.Phone.Trim()}", 11.5, FontWeights.Bold, MonoFont));

            stack.Children.Add(CenteredText("SESSION REPORT", 13.5, FontWeights.Bold, MonoFont));
            stack.Children.Add(Rule(1.5, new Thickness(0, 2, 0, 6)));
            stack.Children.Add(MonoLine($"Session ID: {session.Name}"));
            stack.Children.Add(MonoLine($"Opened: {(session.StartAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "—")}"));
            stack.Children.Add(MonoLine($"Closed: {(session.StopAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "—")}"));
            stack.Children.Add(MonoLine($"Status: {session.State}"));

            decimal totalSales = sessionOrders.Sum(o => o.AmountTotal);
            stack.Children.Add(DashedRule(new Thickness(0, 6, 0, 6)));
            stack.Children.Add(CenteredText($"TOTAL SALES: {Money(totalSales, currency)}", 13.5, FontWeights.Bold, MonoFont));
            stack.Children.Add(DashedRule(new Thickness(0, 6, 0, 6)));

            stack.Children.Add(MonoLine("SALES BREAKDOWN"));
            stack.Children.Add(ThreeColumns("Item", "Qty", "Amount", MonoFont, 12.5, FontWeights.Bold, qtyAlignRight: true));
            stack.Children.Add(Rule(1.5, new Thickness(0, 0, 0, 5)));

            var categories = sessionOrders.SelectMany(o => o.Lines)
                .Select(line =>
                {
                    string category = "Other";
                    if (line.ProductId.HasValue && productCategoryByOdooId.TryGetValue(line.ProductId.Value, out var byId))
                        category = byId;
                    else if (productCategoryByName.TryGetValue(line.ProductName.Trim(), out var byName))
                        category = byName;
                    return (Category: category, Line: line);
                })
                .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var categoryGroup in categories)
            {
                decimal categoryQty = categoryGroup.Sum(x => x.Line.Qty);
                decimal categoryAmount = categoryGroup.Sum(x => x.Line.PriceSubtotalIncl);
                stack.Children.Add(Rule(1.5, new Thickness(0, 2, 0, 4)));
                stack.Children.Add(ThreeColumns(categoryGroup.Key, Qty(categoryQty),
                    Money(categoryAmount, currency), MonoFont, 13, FontWeights.Bold, qtyAlignRight: true));

                foreach (var productGroup in categoryGroup.GroupBy(x => x.Line.ProductName, StringComparer.OrdinalIgnoreCase)
                             .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    stack.Children.Add(DashedRule());
                    stack.Children.Add(ThreeColumns(productGroup.Key,
                        Qty(productGroup.Sum(x => x.Line.Qty)),
                        Money(productGroup.Sum(x => x.Line.PriceSubtotalIncl), currency),
                        MonoFont, 12.5, FontWeights.Bold, new Thickness(10, 1, 0, 4), true));
                }
            }

            stack.Children.Add(DashedRule(new Thickness(0, 6, 0, 6)));
            stack.Children.Add(MonoLine("PAYMENTS"));
            var payments = sessionOrders.SelectMany(o => o.Payments).ToList();
            if (payments.Count == 0)
                stack.Children.Add(MonoLine($"Cash: {Money(totalSales, currency)}"));
            else
            {
                foreach (var group in payments.GroupBy(p => p.PaymentMethodName).OrderBy(g => g.Key))
                    stack.Children.Add(MonoLine($"{group.Key}: {Money(group.Sum(p => p.Amount), currency)}"));
            }
            stack.Children.Add(DashedRule(new Thickness(0, 6, 0, 6)));
            stack.Children.Add(CenteredText($"TOTAL SALES: {Money(totalSales, currency)}", 13.5, FontWeights.Bold, MonoFont));
            stack.Children.Add(CenteredText("Thank you for using Odoo POS", 12, FontWeights.Bold, MonoFont));
            stack.Children.Add(CenteredText($"Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}", 10.5, FontWeights.Bold, MonoFont));
            return Rasterize(root, targetDotWidth);
        });
    }

    private static Border CreateRoot() => new()
    {
        Width = LogicalWidth,
        Background = Brushes.White,
        Padding = new Thickness(8, 6, 8, 8)
    };

    private static TextBlock CenteredText(string text, double size, FontWeight weight, FontFamily font,
        FlowDirection flow = FlowDirection.LeftToRight, bool wrap = false) => new()
    {
        Text = text,
        FontFamily = font,
        FontSize = size,
        FontWeight = weight,
        Foreground = Brushes.Black,
        TextAlignment = TextAlignment.Center,
        FlowDirection = flow,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static TextBlock MonoLine(string text) => new()
    {
        Text = text,
        FontFamily = MonoFont,
        FontSize = 12.5,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.Black,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 1, 0, 1)
    };

    private static Rectangle Rule(double height, Thickness margin) => new()
    {
        Height = height,
        Fill = Brushes.Black,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Margin = margin
    };

    private static Line DashedRule(Thickness? margin = null) => new()
    {
        X1 = 0,
        X2 = LogicalWidth - 16,
        Y1 = 0,
        Y2 = 0,
        Stroke = Brushes.Black,
        StrokeThickness = 1,
        StrokeDashArray = [4, 3],
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Margin = margin ?? new Thickness(0)
    };

    private static Grid LeftRight(string leftText, string rightText, FontFamily font, double size,
        FontWeight? leftWeight = null, FontWeight? rightWeight = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new TextBlock { Text = leftText, FontFamily = font, FontSize = size, FontWeight = leftWeight ?? FontWeights.Normal, Foreground = Brushes.Black };
        var right = new TextBlock { Text = rightText, FontFamily = font, FontSize = size, FontWeight = rightWeight ?? FontWeights.Normal, Foreground = Brushes.Black };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static Grid ThreeColumns(string c1, string c2, string c3, FontFamily font, double size,
        FontWeight weight, Thickness? margin = null, bool qtyAlignRight = false,
        bool col2Normal = false, bool col3Normal = false, bool col3Bold = false)
    {
        var grid = new Grid { Margin = margin ?? new Thickness(0, 2, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34, GridUnitType.Star) });
        var t1 = new TextBlock { Text = c1, FontFamily = font, FontSize = size, FontWeight = weight, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
        var t2 = new TextBlock { Text = c2, FontFamily = font, FontSize = size, FontWeight = col2Normal ? FontWeights.Normal : weight, Foreground = Brushes.Black, TextAlignment = qtyAlignRight ? TextAlignment.Right : TextAlignment.Center };
        var t3 = new TextBlock { Text = c3, FontFamily = font, FontSize = size, FontWeight = col3Bold ? FontWeights.SemiBold : (col3Normal ? FontWeights.Normal : weight), Foreground = Brushes.Black, TextAlignment = TextAlignment.Right };
        Grid.SetColumn(t1, 0); Grid.SetColumn(t2, 1); Grid.SetColumn(t3, 2);
        grid.Children.Add(t1); grid.Children.Add(t2); grid.Children.Add(t3);
        return grid;
    }

    private static string BuildAddress(CompanyRecord? company, bool includeCountry = false)
    {
        if (company == null) return string.Empty;
        var parts = includeCountry
            ? new[] { company.Street, company.City, company.CountryName }
            : new[] { company.Street, company.City };
        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
    }

    private static RenderedReceiptBitmap Rasterize(FrameworkElement element, int targetDotWidth)
    {
        TextOptions.SetTextFormattingMode(element, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(element, TextRenderingMode.Grayscale);
        element.Measure(new Size(LogicalWidth, double.PositiveInfinity));
        double height = Math.Max(32, Math.Ceiling(element.DesiredSize.Height));
        element.Arrange(new Rect(0, 0, LogicalWidth, height));
        element.UpdateLayout();
        double scale = targetDotWidth / LogicalWidth;
        int width = targetDotWidth;
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        double dpi = 96 * scale;
        var rtb = new RenderTargetBitmap(width, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(element);
        int stride = width * 4;
        var pixels = new byte[stride * pixelHeight];
        rtb.CopyPixels(pixels, stride, 0);
        return new RenderedReceiptBitmap(pixels, width, pixelHeight);
    }

    private static T RunOnUiThread<T>(Func<T> func)
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher == null || dispatcher.CheckAccess() ? func() : dispatcher.Invoke(func);
    }
}
