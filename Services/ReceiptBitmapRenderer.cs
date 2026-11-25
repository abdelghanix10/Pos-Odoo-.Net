using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;

namespace OdooPosLogin;

public readonly record struct RenderedReceiptBitmap(byte[] BgraPixels, int Width, int Height);

public static partial class ReceiptBitmapRenderer
{
    private const double LogicalWidth = 300.0;
    private static FontFamily UiFont => PosFontUtils.ArabicFont;
    private static FontFamily ArabicFont => PosFontUtils.ArabicFont;
    private static readonly FontFamily MonoFont = new("Consolas, Courier New");

    [GeneratedRegex(@"^\s*<\?xml[^>]*\?>\s*", RegexOptions.IgnoreCase)]
    private static partial Regex XmlDeclarationRegex();

    [GeneratedRegex(@"<(br|hr|img)(\s[^>]*)?(?<!/)>", RegexOptions.IgnoreCase)]
    private static partial Regex VoidHtmlTagRegex();

    [GeneratedRegex(@"[\u0600-\u06FF]")]
    private static partial Regex ArabicCharsRegex();

    public static string ResolveCurrencyLabel(CompanyRecord? company)
    {
        if (!string.IsNullOrWhiteSpace(CurrencyFormatter.CurrentCurrency))
            return CurrencyFormatter.CurrentCurrency;
        string raw = company?.CurrencyName?.Trim() ?? "MAD";
        if (raw.Equals("MAD", StringComparison.OrdinalIgnoreCase) || raw.Equals("DH", StringComparison.OrdinalIgnoreCase)) return "DH";
        if (raw.Equals("EUR", StringComparison.OrdinalIgnoreCase)) return "€";
        if (raw.Equals("USD", StringComparison.OrdinalIgnoreCase)) return "$";
        return string.IsNullOrWhiteSpace(raw) ? "DH" : raw;
    }

    public static BitmapSource ToBitmapSource(RenderedReceiptBitmap rendered)
    {
        int stride = rendered.Width * 4;
        var bmp = BitmapSource.Create(
            rendered.Width,
            rendered.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            rendered.BgraPixels,
            stride);
        bmp.Freeze();
        return bmp;
    }

    private static string Money(decimal amount, string currency)
    {
        bool isSuffix = currency.Equals("MAD", StringComparison.OrdinalIgnoreCase) ||
                        currency.Equals("DH", StringComparison.OrdinalIgnoreCase);
        return isSuffix
            ? string.Format(CultureInfo.InvariantCulture, "{0:N2} {1}", amount, currency)
            : string.Format(CultureInfo.InvariantCulture, "{0} {1:N2}", currency, amount);
    }

    private static string Qty(decimal qty) =>
        qty == Math.Truncate(qty)
            ? ((long)qty).ToString(CultureInfo.InvariantCulture)
            : qty.ToString("0.##", CultureInfo.InvariantCulture);

    public static (BitmapSource? Image, string? ErrorMessage) RenderLivePreview(
        string? receiptXml,
        string? designName,
        bool openCashDrawerOnly,
        CompanyRecord? operatingCompany,
        bool printHeaderLogo,
        int targetDotWidth)
    {
        if (openCashDrawerOnly)
            return (null, null);

        return RunOnUiThread<(BitmapSource? Image, string? ErrorMessage)>(() =>
        {
            string currency = ResolveCurrencyLabel(operatingCompany);
            string shopName = !string.IsNullOrWhiteSpace(operatingCompany?.Name) ? operatingCompany.Name : "Point of Sale";

            bool isSaleDetails =
                (!string.IsNullOrWhiteSpace(designName) &&
                 (designName.Contains("Sales Details", StringComparison.OrdinalIgnoreCase) ||
                  designName.Contains("Sale Details", StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(receiptXml) &&
                 (receiptXml.Contains("report_saledetails", StringComparison.OrdinalIgnoreCase) ||
                  receiptXml.Contains("session.sales_breakdown_rows", StringComparison.OrdinalIgnoreCase)));

            bool isSecondReceipt = !isSaleDetails &&
                !string.IsNullOrWhiteSpace(designName) &&
                (designName.Contains("Second", StringComparison.OrdinalIgnoreCase) ||
                 designName.Contains("Kitchen", StringComparison.OrdinalIgnoreCase) ||
                 designName.Contains("Simplified", StringComparison.OrdinalIgnoreCase));

            var sampleOrder = CreateSampleOrder(currency);
            var sampleSession = CreateSampleSession(shopName);
            var sampleConfig = new PosConfigRecord { Name = shopName };
            var sampleOrders = new List<PosOrderRecord> { sampleOrder };
            var catById = new Dictionary<int, string>
            {
                [1] = "Pastry",
                [2] = "Beverages"
            };
            var catByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Croissant Beurre"] = "Pastry",
                ["Café Crème"] = "Beverages",
                ["Jus d'Orange"] = "Beverages"
            };

            if (!string.IsNullOrWhiteSpace(receiptXml))
            {
                var ctx = new XmlRenderContext(
                    operatingCompany,
                    shopName,
                    printHeaderLogo && !isSaleDetails,
                    currency,
                    sampleOrder,
                    sampleSession,
                    sampleConfig,
                    sampleOrders,
                    catById,
                    catByName,
                    isSaleDetails,
                    isSecondReceipt);

                var (rendered, error) = TryRenderFromXml(receiptXml, ctx, targetDotWidth);
                if (rendered.HasValue)
                {
                    return (ToBitmapSource(rendered.Value), null);
                }

                return (null, error ?? "Unable to render receipt XML.");
            }

            RenderedReceiptBitmap fallback = isSaleDetails
                ? RenderSaleDetailsReport(sampleSession, sampleConfig, operatingCompany, sampleOrders, catById, catByName, targetDotWidth)
                : isSecondReceipt
                    ? RenderSecondReceipt(sampleOrder, operatingCompany, targetDotWidth)
                    : RenderOrderReceipt(sampleOrder, null, operatingCompany, shopName, printHeaderLogo, targetDotWidth);

            return (ToBitmapSource(fallback), null);
        });
    }

    private static PosOrderRecord CreateSampleOrder(string currency)
    {
        var order = new PosOrderRecord
        {
            Name = "POS/00042",
            PosReference = "262-1-000042",
            TrackingNumber = "2042",
            DateOrder = DateTime.Now,
            CashierName = "Mitchell Admin",
            AmountTotal = 49.00m,
            AmountPaid = 50.00m,
            State = "paid"
        };
        order.Lines.Add(new PosOrderLineRecord { ProductId = 1, ProductName = "Croissant Beurre", Qty = 2m, PriceUnit = 8.00m, PriceSubtotal = 16.00m, PriceSubtotalIncl = 16.00m });
        order.Lines.Add(new PosOrderLineRecord { ProductId = 2, ProductName = "Café Crème", Qty = 1m, PriceUnit = 15.00m, PriceSubtotal = 15.00m, PriceSubtotalIncl = 15.00m });
        order.Lines.Add(new PosOrderLineRecord { ProductId = 3, ProductName = "Jus d'Orange", Qty = 1m, PriceUnit = 18.00m, PriceSubtotal = 18.00m, PriceSubtotalIncl = 18.00m });
        order.Payments.Add(new PosPaymentRecord { PaymentMethodName = "Cash", Amount = 50.00m, CurrencyName = currency, PaymentDate = DateTime.Now });
        return order;
    }

    private static PosSessionRecord CreateSampleSession(string shopName) => new()
    {
        Name = $"{shopName}/00018",
        PosConfigName = shopName,
        StartAt = DateTime.Today.AddHours(8),
        StopAt = DateTime.Now,
        State = "closed",
        OrdersCount = 1,
        TotalPaymentsAmount = 49.00m
    };

    public static RenderedReceiptBitmap RenderOrderReceipt(
        PosOrderRecord order, ReceiptDesignRecord? design, CompanyRecord? company,
        string shopName, bool printHeaderLogo, int targetDotWidth)
    {
        return RunOnUiThread(() =>
        {
            string currency = ResolveCurrencyLabel(company);
            if (design != null && !string.IsNullOrWhiteSpace(design.ReceiptXml))
            {
                var ctx = new XmlRenderContext(
                    company, shopName, printHeaderLogo, currency, order,
                    null, null, null, null, null, false, false);
                var (xmlBmp, _) = TryRenderFromXml(design.ReceiptXml, ctx, targetDotWidth);
                if (xmlBmp.HasValue)
                    return xmlBmp.Value;
            }

            var root = CreateRoot();
            var stack = new StackPanel();
            root.Child = stack;

            if (printHeaderLogo && company?.LogoBitmap != null)
            {
                var logo = new Image
                {
                    Source = TrimLogoVerticalWhitespace(company.LogoBitmap),
                    MaxWidth = 145,
                    MaxHeight = 115,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 6)
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
                10.5, FontWeights.SemiBold, ArabicFont, FlowDirection.RightToLeft, true, new Thickness(0)));
            return Rasterize(root, targetDotWidth);
        });
    }

    public static RenderedReceiptBitmap RenderSecondReceipt(
        PosOrderRecord order, CompanyRecord? company, int targetDotWidth, ReceiptDesignRecord? design = null,
        bool printHeaderLogo = false)
    {
        return RunOnUiThread(() =>
        {
            string currency = ResolveCurrencyLabel(company);
            if (design != null && !string.IsNullOrWhiteSpace(design.ReceiptXml))
            {
                var ctx = new XmlRenderContext(
                    company, company?.Name ?? "Point of Sale", printHeaderLogo, currency, order,
                    null, null, null, null, null, false, true);
                var (xmlBmp, _) = TryRenderFromXml(design.ReceiptXml, ctx, targetDotWidth);
                if (xmlBmp.HasValue)
                    return xmlBmp.Value;
            }

            var root = CreateRoot();
            var stack = new StackPanel();
            root.Child = stack;
            stack.Children.Add(LeftRight("Date:", (order.DateOrder ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), MonoFont, 12.5));
            stack.Children.Add(LeftRight("Order No:", !string.IsNullOrWhiteSpace(order.PosReference) ? order.PosReference : order.Name, MonoFont, 12.5, FontWeights.Normal, FontWeights.Bold));
            stack.Children.Add(DashedRule(new Thickness(0, 2, 0, 4)));
            stack.Children.Add(ThreeColumns("Item", "Qty", "Price", MonoFont, 12.5, FontWeights.Bold));
            stack.Children.Add(Rule(1.5, new Thickness(0, 2, 0, 4)));
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
        int targetDotWidth,
        ReceiptDesignRecord? design = null)
    {
        return RunOnUiThread(() =>
        {
            string currency = ResolveCurrencyLabel(company);
            if (design != null && !string.IsNullOrWhiteSpace(design.ReceiptXml))
            {
                var ctx = new XmlRenderContext(
                    company, config.Name, false, currency, null,
                    session, config, sessionOrders, productCategoryByOdooId, productCategoryByName, true, false);
                var (xmlBmp, _) = TryRenderFromXml(design.ReceiptXml, ctx, targetDotWidth);
                if (xmlBmp.HasValue)
                    return xmlBmp.Value;
            }

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

            AppendSalesBreakdownRows(stack, sessionOrders, productCategoryByOdooId, productCategoryByName, currency);

            stack.Children.Add(DashedRule(new Thickness(0, 6, 0, 6)));
            stack.Children.Add(MonoLine("PAYMENTS"));
            AppendPaymentBreakdownRows(stack, sessionOrders, totalSales, currency);

            stack.Children.Add(DashedRule(new Thickness(0, 6, 0, 6)));
            stack.Children.Add(CenteredText($"TOTAL SALES: {Money(totalSales, currency)}", 13.5, FontWeights.Bold, MonoFont));
            stack.Children.Add(CenteredText("Thank you for using Odoo POS", 12, FontWeights.Bold, MonoFont));
            stack.Children.Add(CenteredText($"Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}", 10.5, FontWeights.Bold, MonoFont, margin: new Thickness(0)));
            return Rasterize(root, targetDotWidth);
        });
    }

    // =========================================================================
    // XML RECEIPT TEMPLATE RENDERER (Shared by Live Preview & Printer Output)
    // =========================================================================
    private sealed record XmlRenderContext(
        CompanyRecord? Company,
        string ShopName,
        bool PrintHeaderLogo,
        string Currency,
        PosOrderRecord? Order,
        PosSessionRecord? Session,
        PosConfigRecord? Config,
        List<PosOrderRecord>? SessionOrders,
        Dictionary<int, string>? ProductCategoryByOdooId,
        Dictionary<string, string>? ProductCategoryByName,
        bool IsSaleDetailsReport,
        bool IsSecondReceipt);

    private sealed record XmlStyleState(
        double FontSize,
        FontWeight FontWeight,
        FontFamily FontFamily,
        TextAlignment TextAlignment,
        FlowDirection FlowDirection,
        Brush Foreground)
    {
        public static XmlStyleState Default(FontFamily font) =>
            new(12.5, FontWeights.Normal, font, TextAlignment.Left, FlowDirection.LeftToRight, Brushes.Black);
    }

    private static (RenderedReceiptBitmap? Bitmap, string? Error) TryRenderFromXml(
        string rawXml,
        XmlRenderContext ctx,
        int targetDotWidth)
    {
        try
        {
            string cleaned = XmlDeclarationRegex().Replace(rawXml.Trim(), string.Empty);
            cleaned = VoidHtmlTagRegex().Replace(cleaned, "<$1$2/>");
            var doc = XDocument.Parse($"<_root>{cleaned}</_root>", LoadOptions.PreserveWhitespace);
            if (doc.Root == null)
                return (null, "Empty XML template.");

            var root = CreateRoot();
            var stack = new StackPanel();
            root.Child = stack;

            // Logo rendering is driven solely by an explicit <img> element in the XML
            // (see the "img" case in RenderBlockElement). No implicit injection here:
            // a design without <img> must print without a logo.

            var baseStyle = XmlStyleState.Default(ctx.IsSaleDetailsReport ? MonoFont : UiFont);
            RenderChildrenIntoStack(doc.Root.Nodes(), stack, ctx, baseStyle, null, null);

            if (stack.Children.Count == 0)
            {
                stack.Children.Add(CenteredText("(Empty Receipt)", 12, FontWeights.Normal, UiFont));
            }

            return (Rasterize(root, targetDotWidth), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static Image BuildLogoImage(BitmapSource rawLogo)
    {
        var logo = new Image
        {
            Source = TrimLogoVerticalWhitespace(rawLogo),
            MaxWidth = 145,
            MaxHeight = 115,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        return logo;
    }

    private static void RenderChildrenIntoStack(
        IEnumerable<XNode> nodes,
        StackPanel targetStack,
        XmlRenderContext ctx,
        XmlStyleState style,
        string? loopVarName,
        object? loopItem)
    {
        var pendingInlines = new List<XNode>();

        void FlushInlines()
        {
            if (pendingInlines.Count == 0) return;
            var tb = BuildInlineTextBlock(pendingInlines, ctx, style, loopVarName, loopItem);
            pendingInlines.Clear();
            if (tb != null)
            {
                targetStack.Children.Add(tb);
            }
        }

        foreach (var node in nodes)
        {
            if (node is XText textNode)
            {
                if (!string.IsNullOrWhiteSpace(textNode.Value))
                    pendingInlines.Add(textNode);
                continue;
            }

            if (node is not XElement el)
                continue;

            string tag = el.Name.LocalName.ToLowerInvariant();

            if (tag == "t")
            {
                if (el.Attribute("t-if") is { } ifAttr && !EvaluateCondition(ifAttr.Value, ctx))
                    continue;

                if (el.Attribute("t-foreach") is { } foreachAttr)
                {
                    FlushInlines();
                    string varName = el.Attribute("t-as")?.Value?.Trim() ?? "line";
                    foreach (var item in EvaluateForeach(foreachAttr.Value, ctx))
                    {
                        RenderChildrenIntoStack(el.Nodes(), targetStack, ctx, style, varName, item);
                    }
                    continue;
                }

                if (el.Attribute("t-esc") is { } escAttr)
                {
                    string expr = escAttr.Value.Trim();
                    if (expr.Equals("session.sales_breakdown_rows", StringComparison.OrdinalIgnoreCase))
                    {
                        FlushInlines();
                        AppendSalesBreakdownRows(targetStack, ctx.SessionOrders ?? [],
                            ctx.ProductCategoryByOdooId ?? [], ctx.ProductCategoryByName ?? [], ctx.Currency);
                        continue;
                    }
                    if (expr.Equals("session.payments_breakdown", StringComparison.OrdinalIgnoreCase))
                    {
                        FlushInlines();
                        decimal totalSales = ctx.SessionOrders?.Sum(o => o.AmountTotal) ?? 0m;
                        AppendPaymentBreakdownRows(targetStack, ctx.SessionOrders ?? [], totalSales, ctx.Currency);
                        continue;
                    }

                    pendingInlines.Add(el);
                    continue;
                }

                FlushInlines();
                RenderChildrenIntoStack(el.Nodes(), targetStack, ctx, style, loopVarName, loopItem);
                continue;
            }

            if (IsInlineTag(tag) && !HasBlockDescendant(el))
            {
                pendingInlines.Add(el);
                continue;
            }

            FlushInlines();

            switch (tag)
            {
                case "odoo":
                case "data":
                case "template":
                case "main":
                    RenderChildrenIntoStack(el.Nodes(), targetStack, ctx, style, loopVarName, loopItem);
                    break;

                case "img":
                    if (ctx.PrintHeaderLogo && ctx.Company?.LogoBitmap != null)
                        targetStack.Children.Add(BuildLogoImage(ctx.Company.LogoBitmap));
                    break;

                case "hr":
                {
                    string css = el.Attribute("style")?.Value ?? "";
                    bool isDashed = css.Contains("dashed", StringComparison.OrdinalIgnoreCase);
                    targetStack.Children.Add(isDashed
                        ? DashedRule(new Thickness(0, 4, 0, 4))
                        : Rule(1.5, new Thickness(0, 4, 0, 4)));
                    break;
                }

                case "table":
                    RenderTableElement(el, targetStack, ctx, style, loopVarName, loopItem);
                    break;

                default:
                    RenderBlockElement(el, tag, targetStack, ctx, style, loopVarName, loopItem);
                    break;
            }
        }

        FlushInlines();
    }

    private static void RenderBlockElement(
        XElement el,
        string tag,
        StackPanel parentStack,
        XmlRenderContext ctx,
        XmlStyleState parentStyle,
        string? loopVarName,
        object? loopItem)
    {
        var (childStyle, box) = ParseElementStyle(el, tag, parentStyle);

        if (box.IsFlexBetween || HasClass(el, "orderline"))
        {
            var rowGrid = BuildFlexBetweenGrid(el, ctx, childStyle, loopVarName, loopItem);
            if (rowGrid != null)
            {
                WrapWithBox(rowGrid, box, parentStack);
            }
            return;
        }

        if (el.Attribute("t-esc") is { } directEsc)
        {
            string txt = EvaluateEscExpression(directEsc.Value, ctx, loopVarName, loopItem);
            if (!string.IsNullOrEmpty(txt))
            {
                var tb = CreateStyledTextBlock(txt, childStyle);
                WrapWithBox(tb, box, parentStack);
            }
            return;
        }

        var innerStack = new StackPanel();
        RenderChildrenIntoStack(el.Nodes(), innerStack, ctx, childStyle, loopVarName, loopItem);
        if (innerStack.Children.Count == 0 && !box.HasTopBorder && !box.HasBottomBorder)
            return;

        WrapWithBox(innerStack, box, parentStack);
    }

    private static Grid? BuildFlexBetweenGrid(
        XElement el,
        XmlRenderContext ctx,
        XmlStyleState style,
        string? loopVarName,
        object? loopItem)
    {
        var childElements = el.Elements().ToList();
        if (childElements.Count < 2)
        {
            var tb = BuildInlineTextBlock(el.Nodes(), ctx, style, loopVarName, loopItem);
            if (tb == null) return null;
            var singleGrid = new Grid();
            singleGrid.Children.Add(tb);
            return singleGrid;
        }

        var rightEl = childElements[^1];
        var leftNodes = el.Nodes().Where(n => !ReferenceEquals(n, rightEl)).ToList();

        var leftTb = BuildInlineTextBlock(leftNodes, ctx, style with { TextAlignment = TextAlignment.Left }, loopVarName, loopItem);
        var (rightStyle, _) = ParseElementStyle(rightEl, rightEl.Name.LocalName, style);
        var rightTb = BuildInlineTextBlock([rightEl], ctx, rightStyle with { TextAlignment = TextAlignment.Right }, loopVarName, loopItem);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (leftTb != null)
        {
            leftTb.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(leftTb, 0);
            grid.Children.Add(leftTb);
        }
        if (rightTb != null)
        {
            Grid.SetColumn(rightTb, 1);
            grid.Children.Add(rightTb);
        }

        return grid;
    }

    private static void RenderTableElement(
        XElement tableEl,
        StackPanel parentStack,
        XmlRenderContext ctx,
        XmlStyleState parentStyle,
        string? loopVarName,
        object? loopItem)
    {
        var (tableStyle, tableBox) = ParseElementStyle(tableEl, "table", parentStyle);
        var tableStack = new StackPanel();

        foreach (var child in tableEl.Elements())
        {
            string childTag = child.Name.LocalName.ToLowerInvariant();
            if (childTag is "thead" or "tbody" or "tfoot")
            {
                RenderTableSection(child.Nodes(), tableStack, ctx, tableStyle, loopVarName, loopItem);
            }
            else if (childTag == "tr")
            {
                RenderTableRow(child, tableStack, ctx, tableStyle, loopVarName, loopItem);
            }
        }

        WrapWithBox(tableStack, tableBox, parentStack);
    }

    private static void RenderTableSection(
        IEnumerable<XNode> nodes,
        StackPanel tableStack,
        XmlRenderContext ctx,
        XmlStyleState tableStyle,
        string? loopVarName,
        object? loopItem)
    {
        foreach (var node in nodes)
        {
            if (node is not XElement el) continue;
            string tag = el.Name.LocalName.ToLowerInvariant();

            if (tag == "t")
            {
                if (el.Attribute("t-if") is { } ifAttr && !EvaluateCondition(ifAttr.Value, ctx))
                    continue;

                if (el.Attribute("t-foreach") is { } foreachAttr)
                {
                    string varName = el.Attribute("t-as")?.Value?.Trim() ?? "line";
                    foreach (var item in EvaluateForeach(foreachAttr.Value, ctx))
                    {
                        RenderTableSection(el.Nodes(), tableStack, ctx, tableStyle, varName, item);
                    }
                    continue;
                }

                if (el.Attribute("t-esc") is { } escAttr &&
                    escAttr.Value.Trim().Equals("session.sales_breakdown_rows", StringComparison.OrdinalIgnoreCase))
                {
                    AppendSalesBreakdownRows(tableStack, ctx.SessionOrders ?? [],
                        ctx.ProductCategoryByOdooId ?? [], ctx.ProductCategoryByName ?? [], ctx.Currency);
                    continue;
                }

                RenderTableSection(el.Nodes(), tableStack, ctx, tableStyle, loopVarName, loopItem);
            }
            else if (tag == "tr")
            {
                RenderTableRow(el, tableStack, ctx, tableStyle, loopVarName, loopItem);
            }
        }
    }

    private static void RenderTableRow(
        XElement trEl,
        StackPanel tableStack,
        XmlRenderContext ctx,
        XmlStyleState tableStyle,
        string? loopVarName,
        object? loopItem)
    {
        var (rowStyle, rowBox) = ParseElementStyle(trEl, "tr", tableStyle);
        var cells = trEl.Elements().Where(e =>
        {
            string n = e.Name.LocalName.ToLowerInvariant();
            return n is "th" or "td";
        }).ToList();

        if (cells.Count == 0) return;

        var grid = new Grid();
        if (cells.Count == 3)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34, GridUnitType.Star) });
        }
        else
        {
            for (int i = 0; i < cells.Count; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            bool isHeader = cell.Name.LocalName.Equals("th", StringComparison.OrdinalIgnoreCase);
            var cellBase = isHeader
                ? rowStyle with { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center }
                : rowStyle;
            var (cellStyle, cellBox) = ParseElementStyle(cell, cell.Name.LocalName, cellBase);

            var tb = BuildInlineTextBlock(cell.Nodes(), ctx, cellStyle, loopVarName, loopItem);
            if (tb != null)
            {
                tb.Margin = cellBox.Padding;
                tb.VerticalAlignment = VerticalAlignment.Center;
                tb.HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetColumn(tb, i);
                grid.Children.Add(tb);
            }
        }

        WrapWithBox(grid, rowBox, tableStack);
    }

    private static void WrapWithBox(FrameworkElement content, CssBoxStyle box, StackPanel parentStack)
    {
        if (box.HasTopBorder)
        {
            parentStack.Children.Add(box.TopBorderDashed
                ? DashedRule(new Thickness(0, box.Margin.Top, 0, 2))
                : Rule(box.TopBorderThickness, new Thickness(0, box.Margin.Top, 0, 2)));
        }

        var topMargin = box.HasTopBorder ? 0 : box.Margin.Top;
        var bottomMargin = box.HasBottomBorder ? 0 : box.Margin.Bottom;
        if (topMargin > 0 || bottomMargin > 0 || box.Margin.Left > 0 || box.Margin.Right > 0 ||
            box.Padding.Top > 0 || box.Padding.Bottom > 0 || box.Padding.Left > 0 || box.Padding.Right > 0)
        {
            var wrapper = new Border
            {
                Margin = new Thickness(box.Margin.Left, topMargin, box.Margin.Right, bottomMargin),
                Padding = box.Padding,
                Child = content
            };
            parentStack.Children.Add(wrapper);
        }
        else
        {
            parentStack.Children.Add(content);
        }

        if (box.HasBottomBorder)
        {
            parentStack.Children.Add(box.BottomBorderDashed
                ? DashedRule(new Thickness(0, 2, 0, box.Margin.Bottom))
                : Rule(box.BottomBorderThickness, new Thickness(0, 2, 0, box.Margin.Bottom)));
        }
    }

    private static TextBlock? BuildInlineTextBlock(
        IEnumerable<XNode> nodes,
        XmlRenderContext ctx,
        XmlStyleState style,
        string? loopVarName,
        object? loopItem)
    {
        var tb = new TextBlock
        {
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            FontWeight = style.FontWeight,
            Foreground = style.Foreground,
            TextAlignment = style.TextAlignment,
            FlowDirection = style.FlowDirection,
            TextWrapping = TextWrapping.Wrap
        };

        AppendInlineNodes(nodes, tb.Inlines, ctx, style, loopVarName, loopItem);

        // Trim trailing line breaks
        while (tb.Inlines.LastInline is LineBreak)
            tb.Inlines.Remove(tb.Inlines.LastInline);

        bool hasText = tb.Inlines.Any(inl => inl is Run r && !string.IsNullOrWhiteSpace(r.Text));
        if (!hasText) return null;

        // Ensure Arabic text uses the embedded Arabic font
        string allText = string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text));
        if (ArabicCharsRegex().IsMatch(allText))
        {
            tb.FontFamily = ArabicFont;
        }

        return tb;
    }

    private static void AppendInlineNodes(
        IEnumerable<XNode> nodes,
        InlineCollection inlines,
        XmlRenderContext ctx,
        XmlStyleState style,
        string? loopVarName,
        object? loopItem)
    {
        foreach (var node in nodes)
        {
            if (node is XText textNode)
            {
                string collapsed = Regex.Replace(textNode.Value, @"\s+", " ");
                if (string.IsNullOrWhiteSpace(collapsed))
                {
                    if (inlines.Count > 0 && inlines.LastInline is Run prev && !prev.Text.EndsWith(' '))
                        inlines.Add(new Run(" ") { FontFamily = style.FontFamily, FontSize = style.FontSize, FontWeight = style.FontWeight });
                }
                else
                {
                    inlines.Add(new Run(collapsed) { FontFamily = style.FontFamily, FontSize = style.FontSize, FontWeight = style.FontWeight });
                }
                continue;
            }

            if (node is not XElement el) continue;
            string tag = el.Name.LocalName.ToLowerInvariant();

            if (tag == "br")
            {
                inlines.Add(new LineBreak());
                continue;
            }

            if (tag == "t")
            {
                if (el.Attribute("t-if") is { } ifAttr && !EvaluateCondition(ifAttr.Value, ctx))
                    continue;

                if (el.Attribute("t-esc") is { } escAttr)
                {
                    string val = EvaluateEscExpression(escAttr.Value, ctx, loopVarName, loopItem);
                    if (!string.IsNullOrEmpty(val))
                        inlines.Add(new Run(val) { FontFamily = style.FontFamily, FontSize = style.FontSize, FontWeight = style.FontWeight });
                    continue;
                }

                AppendInlineNodes(el.Nodes(), inlines, ctx, style, loopVarName, loopItem);
                continue;
            }

            var (childStyle, _) = ParseElementStyle(el, tag, style);
            if (el.Attribute("t-esc") is { } directEsc)
            {
                string val = EvaluateEscExpression(directEsc.Value, ctx, loopVarName, loopItem);
                if (!string.IsNullOrEmpty(val))
                    inlines.Add(new Run(val) { FontFamily = childStyle.FontFamily, FontSize = childStyle.FontSize, FontWeight = childStyle.FontWeight });
                continue;
            }

            AppendInlineNodes(el.Nodes(), inlines, ctx, childStyle, loopVarName, loopItem);
        }
    }

    private static TextBlock CreateStyledTextBlock(string text, XmlStyleState style)
    {
        bool hasArabic = ArabicCharsRegex().IsMatch(text);
        return new TextBlock
        {
            Text = text,
            FontFamily = hasArabic ? ArabicFont : style.FontFamily,
            FontSize = style.FontSize,
            FontWeight = style.FontWeight,
            Foreground = style.Foreground,
            TextAlignment = style.TextAlignment,
            FlowDirection = style.FlowDirection,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static bool IsInlineTag(string tag) =>
        tag is "span" or "strong" or "b" or "small" or "em" or "i" or "br";

    private static bool HasBlockDescendant(XElement el) =>
        el.Descendants().Any(d => d.Name.LocalName.ToLowerInvariant() is "div" or "table" or "hr" or "h1" or "h2" or "h3" or "img");

    private static bool HasClass(XElement el, string className)
    {
        string cls = el.Attribute("class")?.Value ?? "";
        return cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(c => c.Equals(className, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record CssBoxStyle(
        Thickness Margin,
        Thickness Padding,
        bool HasTopBorder,
        double TopBorderThickness,
        bool TopBorderDashed,
        bool HasBottomBorder,
        double BottomBorderThickness,
        bool BottomBorderDashed,
        bool IsFlexBetween);

    private static (XmlStyleState Style, CssBoxStyle Box) ParseElementStyle(XElement el, string tag, XmlStyleState parent)
    {
        double fontSize = parent.FontSize;
        FontWeight fontWeight = parent.FontWeight;
        FontFamily fontFamily = parent.FontFamily;
        TextAlignment align = parent.TextAlignment;
        FlowDirection flow = parent.FlowDirection;

        if (tag is "strong" or "b") fontWeight = FontWeights.Bold;
        else if (tag == "small") fontSize = Math.Max(9.5, parent.FontSize - 2.0);
        else if (tag == "h1") { fontSize = 18; fontWeight = FontWeights.Bold; }
        else if (tag == "h2") { fontSize = 16; fontWeight = FontWeights.Bold; }
        else if (tag == "h3") { fontSize = 14; fontWeight = FontWeights.Bold; }

        if (HasClass(el, "pos-receipt-contact") || HasClass(el, "pos-receipt-footer"))
            align = TextAlignment.Center;
        if (HasClass(el, "pos-receipt-amount"))
        {
            fontSize = 16;
            fontWeight = FontWeights.Bold;
        }
        if (HasClass(el, "price-display"))
            fontWeight = FontWeights.SemiBold;

        string? dir = el.Attribute("dir")?.Value;
        if (string.Equals(dir, "rtl", StringComparison.OrdinalIgnoreCase))
        {
            flow = FlowDirection.RightToLeft;
            fontFamily = ArabicFont;
        }

        double mLeft = 0, mTop = 0, mRight = 0, mBottom = 0;
        double pLeft = 0, pTop = 0, pRight = 0, pBottom = 0;
        bool hasTopBorder = false, topDashed = false, hasBottomBorder = false, bottomDashed = false;
        double topThick = 1, bottomThick = 1;
        bool isFlex = false, isSpaceBetween = false;

        string css = el.Attribute("style")?.Value ?? "";
        foreach (var decl in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = decl.IndexOf(':');
            if (colon <= 0) continue;
            string key = decl[..colon].Trim().ToLowerInvariant();
            string val = decl[(colon + 1)..].Trim().ToLowerInvariant();

            switch (key)
            {
                case "font-size":
                    if (TryParsePx(val, out double px) && px > 0) fontSize = Math.Clamp(px, 9.0, 26.0);
                    break;
                case "font-weight":
                    if (val is "bold" or "700" or "800" or "900") fontWeight = FontWeights.Bold;
                    else if (val is "500" or "600") fontWeight = FontWeights.SemiBold;
                    else if (val is "normal" or "400") fontWeight = FontWeights.Normal;
                    break;
                case "font-family":
                    if (val.Contains("mono")) fontFamily = MonoFont;
                    break;
                case "text-align":
                    align = val switch
                    {
                        "center" => TextAlignment.Center,
                        "right" => TextAlignment.Right,
                        _ => TextAlignment.Left
                    };
                    break;
                case "display":
                    if (val == "flex") isFlex = true;
                    break;
                case "justify-content":
                    if (val == "space-between") isSpaceBetween = true;
                    break;
                case "margin":
                    ParseFourSides(val, out mLeft, out mTop, out mRight, out mBottom);
                    break;
                case "margin-top":
                    if (TryParsePx(val, out double mt)) mTop = mt;
                    break;
                case "margin-bottom":
                    if (TryParsePx(val, out double mb)) mBottom = mb;
                    break;
                case "margin-left":
                    if (TryParsePx(val, out double ml)) mLeft = ml;
                    break;
                case "margin-right":
                    if (TryParsePx(val, out double mr)) mRight = mr;
                    break;
                case "padding":
                    ParseFourSides(val, out pLeft, out pTop, out pRight, out pBottom);
                    break;
                case "padding-top":
                    if (TryParsePx(val, out double pt)) pTop = pt;
                    break;
                case "padding-bottom":
                    if (TryParsePx(val, out double pb)) pBottom = pb;
                    break;
                case "padding-left":
                    if (TryParsePx(val, out double pl)) pLeft = pl;
                    break;
                case "padding-right":
                    if (TryParsePx(val, out double pr)) pRight = pr;
                    break;
                case "border-top":
                    if (val != "none" && !val.StartsWith("0 "))
                    {
                        hasTopBorder = true;
                        topDashed = val.Contains("dashed");
                        topThick = ParseBorderThickness(val);
                    }
                    break;
                case "border-bottom":
                    if (val != "none" && !val.StartsWith("0 "))
                    {
                        hasBottomBorder = true;
                        bottomDashed = val.Contains("dashed");
                        bottomThick = ParseBorderThickness(val);
                    }
                    break;
            }
        }

        return (
            new XmlStyleState(fontSize, fontWeight, fontFamily, align, flow, Brushes.Black),
            new CssBoxStyle(
                new Thickness(mLeft, mTop, mRight, mBottom),
                new Thickness(pLeft, pTop, pRight, pBottom),
                hasTopBorder, topThick, topDashed,
                hasBottomBorder, bottomThick, bottomDashed,
                isFlex && isSpaceBetween));
    }

    private static bool TryParsePx(string raw, out double px)
    {
        string clean = raw.Replace("px", "", StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out px);
    }

    private static void ParseFourSides(string raw, out double left, out double top, out double right, out double bottom)
    {
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => TryParsePx(p, out double v) ? v : 0)
            .ToArray();
        switch (parts.Length)
        {
            case 1:
                left = top = right = bottom = parts[0];
                break;
            case 2:
                top = bottom = parts[0];
                left = right = parts[1];
                break;
            case 4:
                top = parts[0]; right = parts[1]; bottom = parts[2]; left = parts[3];
                break;
            default:
                left = top = right = bottom = 0;
                break;
        }
    }

    private static double ParseBorderThickness(string val)
    {
        foreach (var tok in val.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (tok.EndsWith("px", StringComparison.OrdinalIgnoreCase) && TryParsePx(tok, out double px))
                return Math.Clamp(px, 1.0, 4.0);
        }
        return 1.0;
    }

    private static bool EvaluateCondition(string expr, XmlRenderContext ctx)
    {
        string clean = expr.Trim().ToLowerInvariant();
        if (clean is "false" or "0") return false;
        if (clean.Contains("logo")) return ctx.PrintHeaderLogo && ctx.Company?.LogoBitmap != null;
        if (clean.Contains("paymentlines")) return (ctx.Order?.Payments.Count ?? 0) > 0;
        return true;
    }

    private static IEnumerable<object> EvaluateForeach(string expr, XmlRenderContext ctx)
    {
        string clean = expr.Trim().ToLowerInvariant();
        if (clean.Contains("orderlines"))
            return ctx.Order?.Lines.Cast<object>() ?? [];
        if (clean.Contains("paymentlines"))
            return ctx.Order?.Payments.Cast<object>() ?? [];
        return [];
    }

    private static string EvaluateEscExpression(string expr, XmlRenderContext ctx, string? loopVarName, object? loopItem)
    {
        string key = expr.Trim();
        string lower = key.ToLowerInvariant();

        if (loopItem is PosOrderLineRecord line)
        {
            if (lower.EndsWith(".productname") || lower.EndsWith(".product_name")) return line.ProductName;
            if (lower.EndsWith(".qty")) return Qty(line.Qty);
            if (lower.EndsWith(".price") || lower.EndsWith(".price_display") || lower.EndsWith(".amount"))
                return Money(line.PriceSubtotalIncl, ctx.Currency);
            if (lower.EndsWith(".unit_price")) return Money(line.PriceUnit, ctx.Currency);
            if (lower.EndsWith(".discount")) return $"{line.Discount:0.#}%";
        }

        if (loopItem is PosPaymentRecord payment)
        {
            if (lower.EndsWith(".name")) return payment.PaymentMethodName;
            if (lower.EndsWith(".amount")) return Money(payment.Amount, ctx.Currency);
        }

        return lower switch
        {
            "company.name" or "receipt.company.name" => !string.IsNullOrWhiteSpace(ctx.Company?.Name) ? ctx.Company.Name : ctx.ShopName,
            "company.address" or "receipt.company.address" => BuildAddress(ctx.Company, ctx.IsSaleDetailsReport),
            "company.phone" or "receipt.company.phone" => ctx.Company?.Phone?.Trim() ?? "",
            "company.email" or "receipt.company.email" => ctx.Company?.Email?.Trim() ?? "",
            "company.vat" or "receipt.company.vat" => ctx.Company?.Vat?.Trim() ?? "",
            "receipt.name" or "order.name" or "order.pos_reference" =>
                !string.IsNullOrWhiteSpace(ctx.Order?.PosReference) ? ctx.Order.PosReference : (ctx.Order?.Name ?? ""),
            "receipt.cashier" or "order.cashier" => ctx.Order?.CashierName ?? "",
            "order.customer" or "receipt.customer" => ctx.Order?.CustomerName ?? "",
            "order.note" or "receipt.note" => ctx.Order?.Note ?? "",
            "order.datetime" =>
                (ctx.Order?.DateOrder ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "props.receipt.date" or "receipt.date" or "receipt.date.localestring" or "order.date" =>
                (ctx.Order?.DateOrder ?? DateTime.Now).ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture),
            "props.data.amount_total" or "receipt.total_with_tax" => Money(ctx.Order?.AmountTotal ?? 0m, ctx.Currency),
            "receipt.total_tax" => Money(ctx.Order?.AmountTax ?? 0m, ctx.Currency),
            "session.name" => ctx.Session?.Name ?? "",
            "session.start_at" => ctx.Session?.StartAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "—",
            "session.stop_at" => ctx.Session?.StopAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "—",
            "session.state" => ctx.Session?.State ?? "closed",
            "session.total_sales" => (ctx.SessionOrders?.Sum(o => o.AmountTotal) ?? 0m).ToString("N2", CultureInfo.InvariantCulture),
            "session.report_date" => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            _ => key
        };
    }

    private static void AppendSalesBreakdownRows(
        StackPanel stack,
        List<PosOrderRecord> sessionOrders,
        Dictionary<int, string> productCategoryByOdooId,
        Dictionary<string, string> productCategoryByName,
        string currency)
    {
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
    }

    private static void AppendPaymentBreakdownRows(StackPanel stack, List<PosOrderRecord> sessionOrders, decimal totalSales, string currency)
    {
        var payments = sessionOrders.SelectMany(o => o.Payments).ToList();
        if (payments.Count == 0)
        {
            stack.Children.Add(MonoLine($"Cash: {Money(totalSales, currency)}"));
        }
        else
        {
            foreach (var group in payments.GroupBy(p => p.PaymentMethodName).OrderBy(g => g.Key))
                stack.Children.Add(MonoLine($"{group.Key}: {Money(group.Sum(p => p.Amount), currency)}"));
        }
    }

    private static BitmapSource TrimLogoVerticalWhitespace(BitmapSource source)
    {
        if (_trimmedLogoCache.TryGetValue(source, out var cached))
            return cached;

        BitmapSource result = source;
        try
        {
            BitmapSource bgra = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int w = bgra.PixelWidth;
            int h = bgra.PixelHeight;
            if (w <= 0 || h <= 2)
            {
                _trimmedLogoCache.AddOrUpdate(source, result);
                return result;
            }

            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            bgra.CopyPixels(pixels, stride, 0);

            int top = -1;
            int bottom = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                bool hasInk = false;
                for (int x = 0; x < w; x++)
                {
                    int idx = row + (x * 4);
                    byte a = pixels[idx + 3];
                    if (a > 16 && (pixels[idx] < 225 || pixels[idx + 1] < 225 || pixels[idx + 2] < 225))
                    {
                        hasInk = true;
                        break;
                    }
                }
                if (hasInk)
                {
                    if (top < 0) top = y;
                    bottom = y;
                }
            }

            if (top >= 0 && bottom >= top && (top > 0 || bottom < h - 1))
            {
                var cropped = new CroppedBitmap(bgra, new Int32Rect(0, top, w, bottom - top + 1));
                cropped.Freeze();
                result = cropped;
            }
        }
        catch { }

        _trimmedLogoCache.AddOrUpdate(source, result);
        return result;
    }

    private static Border CreateRoot() => new()
    {
        Width = LogicalWidth,
        Background = Brushes.White,
        Padding = new Thickness(8, 0, 8, 0)
    };

    private static TextBlock CenteredText(string text, double size, FontWeight weight, FontFamily font,
        FlowDirection flow = FlowDirection.LeftToRight, bool wrap = false, Thickness? margin = null) => new()
    {
        Text = text,
        FontFamily = font,
        FontSize = size,
        FontWeight = weight,
        Foreground = Brushes.Black,
        TextAlignment = TextAlignment.Center,
        FlowDirection = flow,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Margin = margin ?? new Thickness(0, 0, 0, 4)
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

        int firstRow = -1;
        int lastRow = -1;
        for (int y = 0; y < pixelHeight; y++)
        {
            int rowStart = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = rowStart + (x * 4);
                if (pixels[i + 3] > 16 && (pixels[i] < 245 || pixels[i + 1] < 245 || pixels[i + 2] < 245))
                {
                    if (firstRow < 0) firstRow = y;
                    lastRow = y;
                    break;
                }
            }
        }

        if (firstRow >= 0 && lastRow >= firstRow && (firstRow > 0 || lastRow < pixelHeight - 1))
        {
            int trimmedHeight = lastRow - firstRow + 1;
            var trimmed = new byte[stride * trimmedHeight];
            Buffer.BlockCopy(pixels, firstRow * stride, trimmed, 0, trimmed.Length);
            return new RenderedReceiptBitmap(trimmed, width, trimmedHeight);
        }

        return new RenderedReceiptBitmap(pixels, width, pixelHeight);
    }

    private static T RunOnUiThread<T>(Func<T> func)
    {
        var uiDispatcher = Application.Current?.Dispatcher;
        if (uiDispatcher != null && uiDispatcher.CheckAccess())
            return func();

        // Receipt rendering is pure WPF layout + RenderTargetBitmap with no dependency on
        // live app resources, so it can run on a dedicated STA thread instead of blocking
        // the POS UI thread. Falls back to the UI dispatcher if no Application exists.
        if (uiDispatcher == null)
            return func();

        return EnsureRenderDispatcher().Invoke(func);
    }

    private static readonly object _renderStaLock = new();
    private static Dispatcher? _renderDispatcher;
    private static readonly ManualResetEventSlim _renderReady = new(false);
    private static readonly ConditionalWeakTable<BitmapSource, BitmapSource> _trimmedLogoCache = new();

    private static Dispatcher EnsureRenderDispatcher()
    {
        if (_renderDispatcher != null) return _renderDispatcher;
        lock (_renderStaLock)
        {
            if (_renderDispatcher != null) return _renderDispatcher;
            var thread = new Thread(() =>
            {
                _renderDispatcher = Dispatcher.CurrentDispatcher;
                _renderReady.Set();
                Dispatcher.Run();
            })
            {
                Name = "ReceiptRenderSTA",
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            _renderReady.Wait();
            return _renderDispatcher!;
        }
    }
}
