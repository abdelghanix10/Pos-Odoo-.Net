using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OdooPosLogin;

public class ReceiptDesignRecord : INotifyPropertyChanged
{
    private long _id;
    private string _name = string.Empty;
    private bool _openCashDrawerOnly;
    private string _receiptXml = string.Empty;
    private long? _companyId;
    private string? _companyName;
    private string _updatedAt = string.Empty;
    private bool _isSelected;

    public long Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public bool OpenCashDrawerOnly
    {
        get => _openCashDrawerOnly;
        set
        {
            if (_openCashDrawerOnly != value)
            {
                _openCashDrawerOnly = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OpenCashDrawerDisplay));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string ReceiptXml
    {
        get => _receiptXml;
        set { if (_receiptXml != value) { _receiptXml = value; OnPropertyChanged(); } }
    }

    public long? CompanyId
    {
        get => _companyId;
        set { if (_companyId != value) { _companyId = value; OnPropertyChanged(); } }
    }

    public string? CompanyName
    {
        get => _companyName;
        set { if (_companyName != value) { _companyName = value; OnPropertyChanged(); } }
    }

    public string UpdatedAt
    {
        get => _updatedAt;
        set { if (_updatedAt != value) { _updatedAt = value; OnPropertyChanged(); } }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public string OpenCashDrawerDisplay => OpenCashDrawerOnly ? "Yes (Cash Drawer Only)" : "No";

    public string DisplayName => OpenCashDrawerOnly ? $"{Name} (Drawer Only)" : Name;

    public static string GetDefaultXmlTemplate() =>
        """
        <t t-name="point_of_sale.OrderReceipt">
            <div class="pos-receipt">
                <t t-if="receipt.company.logo">
                    <img class="pos-receipt-logo" t-att-src="receipt.company.logo" alt="Logo"/>
                </t>
                <div class="pos-receipt-contact">
                    <div t-esc="receipt.company.name"/>
                    <div>Cashier: <t t-esc="receipt.cashier"/></div>
                </div>
                <br/>
                <div class="orderlines">
                    <t t-foreach="receipt.orderlines" t-as="line" t-key="line.id">
                        <div class="orderline">
                            <span t-esc="line.product_name"/> x <span t-esc="line.qty"/>
                            <span class="price-display" t-esc="line.price_display"/>
                        </div>
                    </t>
                </div>
                <hr/>
                <div class="pos-receipt-amount">
                    TOTAL: <span t-esc="receipt.total_with_tax"/>
                </div>
                <div class="pos-receipt-footer">
                    Thank you for shopping with us!
                </div>
            </div>
        </t>
        """;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
