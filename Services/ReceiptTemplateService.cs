using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OdooPosLogin;

public static class ReceiptTemplateService
{
    public const string PastryReceiptName = "Pastry Receipt";
    public const string CafeReceiptName = "Cafe Receipt";
    public const string SaleDetailsReportName = "Sales Details Report";

    public static string GetPastryDefaultXml()
    {
        string diskPath = @"C:\Users\HP\Downloads\pastry.xml";
        if (File.Exists(diskPath))
        {
            try { return File.ReadAllText(diskPath); } catch { }
        }

        return """
        <div class="pos-receipt">
            <div style="text-align:center; border-bottom:2px solid #000; padding-bottom:5px;">
                <div style="font-size:16px; font-weight:bold; margin-bottom:5px;">
                    <t t-esc="company.name"/>
                </div>
                <div style="font-size:12px; line-height:1.5;">
                    <div><t t-esc="company.address"/></div>
                    <div><t t-esc="company.phone"/></div>
                </div>
            </div>
            
            <div style="display:flex; justify-content:space-between; margin:3px;">
                <span style="font-weight:bold;">Date:</span>
                <span><t t-esc="props.receipt.date"/></span>
            </div>
            
            <div>
                <table class='receipt-orderlines' style="font-size:13px; width:100%; border-collapse:collapse;">
                    <thead>
                        <tr style="border-top:2px solid #000; border-bottom:1px solid #000;">
                            <th style="text-align:left; padding:5px 0;">Product</th>
                            <th style="text-align:center;">Qty</th>
                            <th style="text-align:right;">Price</th>
                        </tr>
                    </thead>
                    <tbody>
                        <t t-foreach="props.orderlines" t-as="orderline">
                            <tr style="border-bottom:1px dashed #ccc;">
                                <td style="padding:8px 0; text-align:left;"><t t-esc="orderline.productName"/></td>
                                <td style="text-align:center;"><t t-esc="orderline.qty"/></td>
                                <td style="text-align:right; font-weight:500;"><t t-esc="orderline.price"/></td>
                            </tr>
                        </t>
                    </tbody>
                </table>
            </div>

            <div style="font-size:10px; border-top:2px solid #000; padding-top:10px;">
                <div style="display:flex; justify-content:space-between; font-size:16px; font-weight:bold;">
                    <span>TOTAL:</span>
                    <span t-esc='props.data.amount_total'/>
                </div>
            </div>
            
            <div style="margin-top:15px; font-size:13px;">
                <t t-foreach='props.paymentlines' t-as='line'>
                    <div style="display:flex; justify-content:space-between; margin-bottom:5px;">
                        <span><t t-esc='line.name' /></span>
                        <span t-esc='line.amount'/>
                    </div>
                </t>
            </div>

            <div dir="rtl" style="margin-top:8px; padding-top:10px; border-top:2px solid #000; text-align:center;">
                <div style="font-size:14px; font-weight:bold; margin-top:8px; margin-bottom:8px;">★ شكراً  لشرائك ★</div>
                <div style="font-size:11px; color:#666; margin-top:8px;">★ نحن نقدر تعاملكم معنا ونتطلع إلى خدمتكم مرة أخرى ★</div>
            </div>
        </div>
        """;
    }

    public static string GetCafeDefaultXml()
    {
        string diskPath = @"C:\Users\HP\Downloads\cafe.xml";
        if (File.Exists(diskPath))
        {
            try { return File.ReadAllText(diskPath); } catch { }
        }

        return """
        <div class="pos-receipt">
            <div style="text-align:center; border-bottom:2px solid #000; padding-bottom:5px;">
                <div style="font-size:16px; font-weight:bold; margin-bottom:5px;">
                    <t t-esc="company.name"/>
                </div>
                <div style="font-size:12px; line-height:1.5;">
                    <div><t t-esc="company.address"/></div>
                    <div><t t-esc="company.phone"/></div>
                </div>
            </div>
            
            <div style="display:flex; justify-content:space-between; margin:3px;">
                <span style="font-weight:bold;">Date:</span>
                <span><t t-esc="props.receipt.date"/></span>
            </div>

            <div>
                <table class='receipt-orderlines' style="font-size:13px; width:100%; border-collapse:collapse;">
                    <thead>
                        <tr style="border-top:2px solid #000; border-bottom:1px solid #000;">
                            <th style="text-align:left; padding:5px 0;">Product</th>
                            <th style="text-align:center;">Qty</th>
                            <th style="text-align:right;">Price</th>
                        </tr>
                    </thead>
                    <tbody>
                        <t t-foreach="props.orderlines" t-as="orderline">
                            <tr style="border-bottom:1px dashed #ccc;">
                                <td style="padding:8px 0; text-align:left;"><t t-esc="orderline.productName"/></td>
                                <td style="text-align:center;"><t t-esc="orderline.qty"/></td>
                                <td style="text-align:right; font-weight:500;"><t t-esc="orderline.price"/></td>
                            </tr>
                        </t>
                    </tbody>
                </table>
            </div>

            <div style="font-size:10px; border-top:2px solid #000; padding-top:5px;">
                <div style="display:flex; justify-content:space-between; font-size:16px; font-weight:bold;">
                    <span>TOTAL:</span>
                    <span t-esc='props.data.amount_total'/>
                </div>
            </div>

            <div dir="rtl" style="margin-top:5px; padding-top:5px; border-top:2px solid #000; text-align:center;">
                <div style="font-size:14px; font-weight:bold; margin-top:8px; margin-bottom:8px;">★ شكراً  لشرائك ★</div>
                <div style="font-size:11px; color:#666; margin-top:5px;">★ نحن نقدر تعاملكم معنا ونتطلع إلى خدمتكم مرة أخرى ★</div>
            </div>
        </div>
        """;
    }

    public static string GetSaleDetailsReportDefaultXml()
    {
        string diskPath = @"C:\Users\HP\Downloads\report_saledetails.xml";
        if (File.Exists(diskPath))
        {
            try { return File.ReadAllText(diskPath); } catch { }
        }

        return """
        <?xml version="1.0" encoding="utf-8"?>
        <odoo>
            <data>
                <template id="report_saledetails">
                    <main>
                        <div class="page" style="font-size: 14px; font-family: monospace; padding: 5px; font-weight: bold;">
                            <div style="text-align: center; margin-bottom: 5px;">
                                <strong><t t-esc="company.name"/></strong><br/>
                                <small><t t-esc="company.address"/></small><br/>
                                <small>Phone: <t t-esc="company.phone"/></small><br/>
                            </div>
                            <div style="text-align: center; margin-bottom: 5px; border-bottom: 1px solid #000;">
                                <strong>SESSION REPORT</strong>
                            </div>
                            <div style="margin-bottom: 5px;">
                                <strong>Session ID:</strong> <t t-esc="session.name"/><br/>
                                <strong>Opened:</strong> <t t-esc="session.start_at"/><br/>
                                <strong>Closed:</strong> <t t-esc="session.stop_at"/><br/>
                                <strong>Status:</strong> <t t-esc="session.state"/><br/>
                            </div>
                            <hr style="border: none; border-top: 1px dashed #000; margin: 5px 0;"/>
                            <div style="text-align: center; margin-bottom: 5px;">
                                <strong>TOTAL SALES: <t t-esc="session.total_sales"/> DH</strong>
                            </div>
                            <hr style="border: none; border-top: 1px dashed #000; margin: 5px 0;"/>
                            <div style="margin-bottom: 5px;">
                                <strong>SALES BREAKDOWN</strong><br/>
                                <table style="font-size: 14px; border-collapse: collapse; font-weight: bold; width: 100%;">
                                    <thead>
                                        <tr style="border-bottom: 1px solid #000;">
                                            <th style="text-align: left;">Item</th>
                                            <th style="text-align: right; padding-right: 10px;">Qty</th>
                                            <th style="text-align: center;">Amount</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        <t t-esc="session.sales_breakdown_rows"/>
                                    </tbody>
                                </table>
                            </div>
                            <hr style="border: none; border-top: 1px dashed #000; margin: 5px 0;"/>
                            <div style="margin-bottom: 5px;">
                                <strong>PAYMENTS</strong><br/>
                                <t t-esc="session.payments_breakdown"/>
                            </div>
                            <hr style="border: none; border-top: 1px dashed #000; margin: 5px 0;"/>
                            <div style="text-align: center; margin-bottom: 5px;">
                                <strong>TOTAL SALES: <t t-esc="session.total_sales"/> DH</strong>
                            </div>
                            <hr style="border: none; border-top: 1px dashed #000; margin: 5px 0;"/>
                            <div style="text-align: center; margin-top: 5px;">
                                <strong>Thank you for using Odoo POS</strong><br/>
                                <small>Generated on <t t-esc="session.report_date"/></small>
                            </div>
                        </div>
                    </main>
                </template>
            </data>
        </odoo>
        """;
    }

    public static string FormatOrderReceiptText(
        PosOrderRecord order,
        ReceiptDesignRecord? design,
        CompanyRecord? company,
        string shopName,
        bool isSimplified)
    {
        if (design != null && design.OpenCashDrawerOnly)
        {
            return $"[PULSE SIGNAL] Cash Drawer Opened via \"{design.Name}\".\n(Open Cash Drawer Only = Yes)";
        }

        string compName = !string.IsNullOrWhiteSpace(company?.Name) ? company.Name : (!string.IsNullOrWhiteSpace(design?.CompanyName) ? design.CompanyName : shopName);
        string compStreet = !string.IsNullOrWhiteSpace(company?.Street) ? company.Street : (company?.City ?? "");
        string compPhone = !string.IsNullOrWhiteSpace(company?.Phone) ? company.Phone : "";

        // Check if template is pastry, cafe, or custom
        string xml = design?.ReceiptXml ?? string.Empty;
        bool isPastry = design != null && (design.Name.Contains("Pastry", StringComparison.OrdinalIgnoreCase) || xml.Contains("★ شكراً  لشرائك ★") || xml.Contains("receipt-orderlines"));
        bool isCafe = design != null && design.Name.Contains("Cafe", StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine(CenterText(compName.ToUpperInvariant(), 40));
        if (!string.IsNullOrWhiteSpace(compStreet))
            sb.AppendLine(CenterText(compStreet, 40));
        if (!string.IsNullOrWhiteSpace(compPhone))
            sb.AppendLine(CenterText(compPhone, 40));
        sb.AppendLine("========================================");

        string orderRef = !string.IsNullOrWhiteSpace(order.PosReference) ? order.PosReference : order.Name;
        sb.AppendLine($"Receipt #: {orderRef}");
        sb.AppendLine($"Date:      {(order.DateOrder ?? DateTime.Now):yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Cashier:   {order.CashierName}");
        if (!string.IsNullOrWhiteSpace(order.CustomerName))
            sb.AppendLine($"Customer:  {order.CustomerName}");
        sb.AppendLine("----------------------------------------");

        if (isPastry || isCafe)
        {
            sb.AppendLine(string.Format("{0,-20} {1,6} {2,11}", "Product", "Qty", "Price"));
            sb.AppendLine("----------------------------------------");
            foreach (var line in order.Lines)
            {
                string prodName = line.ProductName.Length > 20 ? line.ProductName[..17] + "..." : line.ProductName;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-20} {1,6:0.##} {2,11:N2}", prodName, line.Qty, line.PriceSubtotalIncl));
                if (line.Discount > 0)
                {
                    sb.AppendLine($"   Discount: {line.Discount:0.#}%");
                }
            }
        }
        else
        {
            foreach (var line in order.Lines)
            {
                if (isSimplified)
                {
                    sb.AppendLine($"{line.Qty:0.##} x  {line.ProductName}");
                }
                else
                {
                    sb.AppendLine($"{line.Qty:0.##} x {line.ProductName,-22} $ {line.PriceSubtotalIncl,7:N2}");
                    if (line.Discount > 0)
                        sb.AppendLine($"   Discount: {line.Discount:0.#}%");
                }
            }
        }

        sb.AppendLine("----------------------------------------");
        if (!isSimplified)
        {
            if (order.AmountTax > 0)
            {
                sb.AppendLine($"Taxes:                       $ {order.AmountTax,8:N2}");
            }
            sb.AppendLine($"TOTAL:                       $ {order.AmountTotal,8:N2}");

            if (order.Payments.Count > 0)
            {
                foreach (var p in order.Payments)
                {
                    sb.AppendLine($"{p.PaymentMethodName,-28} $ {p.Amount,8:N2}");
                }
            }
        }

        if (isPastry || isCafe)
        {
            sb.AppendLine("========================================");
            sb.AppendLine(CenterText("★ شكراً لشرائك ★", 40));
            sb.AppendLine(CenterText("★ نحن نقدر تعاملكم معنا ونتطلع إلى خدمتكم مرة أخرى ★", 40));
            sb.AppendLine("========================================");
        }
        else
        {
            sb.AppendLine("----------------------------------------");
            sb.AppendLine(CenterText("Thank you for shopping with us!", 40));
        }

        if (design != null && !string.IsNullOrWhiteSpace(design.Name))
        {
            sb.AppendLine($"[Design: {design.Name}]");
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatSaleDetailsReportText(
        PosSessionRecord session,
        PosConfigRecord config,
        ReceiptDesignRecord? design,
        CompanyRecord? company,
        List<PosOrderRecord> sessionOrders)
    {
        string compName = !string.IsNullOrWhiteSpace(company?.Name) ? company.Name : (!string.IsNullOrWhiteSpace(design?.CompanyName) ? design.CompanyName : config.Name);
        string compStreet = !string.IsNullOrWhiteSpace(company?.Street) ? company.Street : (company?.City ?? "");
        string compPhone = !string.IsNullOrWhiteSpace(company?.Phone) ? company.Phone : "";

        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine(CenterText(compName.ToUpperInvariant(), 40));
        if (!string.IsNullOrWhiteSpace(compStreet))
            sb.AppendLine(CenterText(compStreet, 40));
        if (!string.IsNullOrWhiteSpace(compPhone))
            sb.AppendLine(CenterText($"Phone: {compPhone}", 40));
        sb.AppendLine("========================================");
        sb.AppendLine(CenterText("SESSION REPORT", 40));
        sb.AppendLine("========================================");
        sb.AppendLine($"Session ID: {session.Name}");
        sb.AppendLine($"Opened:     {(session.StartAt.HasValue ? session.StartAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "—")}");
        sb.AppendLine($"Closed:     {(session.StopAt.HasValue ? session.StopAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}");
        sb.AppendLine($"Status:     {(string.IsNullOrWhiteSpace(session.State) ? "closed" : session.State)}");
        sb.AppendLine("----------------------------------------");

        decimal totalSales = sessionOrders.Sum(o => o.AmountTotal);
        sb.AppendLine(CenterText(string.Format(CultureInfo.InvariantCulture, "TOTAL SALES: {0:N2} DH", totalSales), 40));
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("SALES BREAKDOWN");
        sb.AppendLine(string.Format("{0,-20} {1,6} {2,11}", "Item", "Qty", "Amount"));
        sb.AppendLine("----------------------------------------");

        // Group lines by product name
        var allLines = sessionOrders.SelectMany(o => o.Lines).ToList();
        var prodGroups = allLines
            .GroupBy(l => l.ProductName)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var grp in prodGroups)
        {
            decimal qty = grp.Sum(l => l.Qty);
            decimal amt = grp.Sum(l => l.PriceSubtotalIncl);
            string name = grp.Key.Length > 20 ? grp.Key[..17] + "..." : grp.Key;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-20} {1,6:0.##} {2,8:N2} DH", name, qty, amt));
        }

        sb.AppendLine("----------------------------------------");
        sb.AppendLine("PAYMENTS");

        var allPayments = sessionOrders.SelectMany(o => o.Payments).ToList();
        var payGroups = allPayments
            .GroupBy(p => p.PaymentMethodName)
            .OrderBy(g => g.Key)
            .ToList();

        if (payGroups.Count > 0)
        {
            foreach (var pGrp in payGroups)
            {
                decimal pTotal = pGrp.Sum(p => p.Amount);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-26} {1,8:N2} DH", pGrp.Key + ":", pTotal));
            }
        }
        else
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-26} {1,8:N2} DH", "Cash:", totalSales));
        }

        sb.AppendLine("----------------------------------------");
        sb.AppendLine(CenterText(string.Format(CultureInfo.InvariantCulture, "TOTAL SALES: {0:N2} DH", totalSales), 40));
        sb.AppendLine("----------------------------------------");
        sb.AppendLine(CenterText("Thank you for using Odoo POS", 40));
        sb.AppendLine(CenterText($"Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}", 40));
        sb.AppendLine("========================================");

        return sb.ToString().TrimEnd();
    }

    private static string CenterText(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length >= width) return text;
        int leftPad = (width - text.Length) / 2;
        return text.PadLeft(text.Length + leftPad);
    }
}
