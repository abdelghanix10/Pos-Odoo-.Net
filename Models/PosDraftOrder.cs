using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace OdooPosLogin;

public class PosDraftOrder : INotifyPropertyChanged
{
    private string _uuid = Guid.NewGuid().ToString();
    private long? _databaseOrderId;
    private int? _odooOrderId;
    private int _configOdooId = 1;
    private int _sequenceNumber = 1;
    private int _deviceDigit = 2;
    private string _orderNumber; // e.g. "2001", "2002"
    private string _orderRef;    // e.g. "262-1-000001"
    private DateTime _createdAt = DateTime.Now;
    private string _note = string.Empty;
    private int? _partnerId;
    private string _customerName = string.Empty;
    private bool _isToInvoice;
    private bool _isActive;

    public PosDraftOrder(string orderNumber, int configOdooId = 1, int sequenceNumber = 1, int deviceDigit = 2)
    {
        _configOdooId = Math.Max(1, configOdooId);
        _sequenceNumber = Math.Max(1, sequenceNumber);
        _deviceDigit = Math.Clamp(deviceDigit, 1, 9);

        int yearTwoDigits = DateTime.Now.Year % 100;
        _orderNumber = !string.IsNullOrWhiteSpace(orderNumber)
            ? orderNumber
            : $"{_deviceDigit}{_sequenceNumber % 1000:D3}";
        _orderRef = $"{yearTwoDigits:D2}{_deviceDigit}-{_configOdooId}-{_sequenceNumber:D6}";

        CartItems.CollectionChanged += (_, _) => NotifyTotalsChanged();
    }

    public static PosDraftOrder CreateForShop(int configOdooId, int sequenceNumber, int deviceDigit = 2)
    {
        int cleanCfg = Math.Max(1, configOdooId);
        int cleanSeq = Math.Max(1, sequenceNumber);
        int cleanDev = Math.Clamp(deviceDigit, 1, 9);
        string trackingNum = $"{cleanDev}{cleanSeq % 1000:D3}";
        return new PosDraftOrder(trackingNum, cleanCfg, cleanSeq, cleanDev);
    }

    public void ApplyOdooSequence(int configOdooId, int sequenceNumber, int deviceDigit = 2)
    {
        _configOdooId = Math.Max(1, configOdooId);
        _sequenceNumber = Math.Max(1, sequenceNumber);
        _deviceDigit = Math.Clamp(deviceDigit, 1, 9);

        int yearTwoDigits = DateTime.Now.Year % 100;
        OrderNumber = $"{_deviceDigit}{_sequenceNumber % 1000:D3}";
        OrderRef = $"{yearTwoDigits:D2}{_deviceDigit}-{_configOdooId}-{_sequenceNumber:D6}";
    }

    public string FormatOdooOrderName(string shopName) =>
        $"{(string.IsNullOrWhiteSpace(shopName) ? "Point of Sale" : shopName.Trim())} - {_sequenceNumber:D6}";

    public int SequenceNumber
    {
        get => _sequenceNumber;
        set { if (_sequenceNumber != value) { _sequenceNumber = value; OnPropertyChanged(); } }
    }

    public int DeviceDigit
    {
        get => _deviceDigit;
        set { if (_deviceDigit != value) { _deviceDigit = value; OnPropertyChanged(); } }
    }

    public int ConfigOdooId
    {
        get => _configOdooId;
        set { if (_configOdooId != value) { _configOdooId = value; OnPropertyChanged(); } }
    }

    public string Uuid
    {
        get => _uuid;
        set { if (_uuid != value) { _uuid = value; OnPropertyChanged(); } }
    }

    public long? DatabaseOrderId
    {
        get => _databaseOrderId;
        set { if (_databaseOrderId != value) { _databaseOrderId = value; OnPropertyChanged(); } }
    }

    public int? OdooOrderId
    {
        get => _odooOrderId;
        set { if (_odooOrderId != value) { _odooOrderId = value; OnPropertyChanged(); } }
    }

    public string OrderNumber
    {
        get => _orderNumber;
        set { if (_orderNumber != value) { _orderNumber = value; OnPropertyChanged(); } }
    }

    public string OrderRef
    {
        get => _orderRef;
        set { if (_orderRef != value) { _orderRef = value; OnPropertyChanged(); } }
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set { if (_createdAt != value) { _createdAt = value; OnPropertyChanged(); } }
    }

    public string Note
    {
        get => _note;
        set { if (_note != value) { _note = value; OnPropertyChanged(); } }
    }

    public int? PartnerId
    {
        get => _partnerId;
        set { if (_partnerId != value) { _partnerId = value; OnPropertyChanged(); } }
    }

    public string CustomerName
    {
        get => _customerName;
        set
        {
            if (_customerName != value)
            {
                _customerName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CustomerButtonDisplay));
                OnPropertyChanged(nameof(HasCustomer));
            }
        }
    }

    public bool HasCustomer => !string.IsNullOrWhiteSpace(_customerName);
    public string CustomerButtonDisplay => HasCustomer ? _customerName : "Customer";

    public bool IsToInvoice
    {
        get => _isToInvoice;
        set { if (_isToInvoice != value) { _isToInvoice = value; OnPropertyChanged(); } }
    }

    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<PosCartItem> CartItems { get; } = [];

    public bool HasItems => CartItems.Count > 0;

    public decimal SubTotal => CartItems.Sum(i => i.PriceSubtotal);
    public decimal Taxes => CartItems.Sum(i => i.LineTaxAmount);
    public decimal TotalAmount => CartItems.Sum(i => i.LineTotal);

    public string SubTotalDisplay => string.Format(CultureInfo.InvariantCulture, "$ {0:N2}", SubTotal);
    public string TaxesDisplay => string.Format(CultureInfo.InvariantCulture, "$ {0:N2}", Taxes);
    public string TotalDisplay => string.Format(CultureInfo.InvariantCulture, "$ {0:N2}", TotalAmount);

    // Split formatting for the giant "$ 18.62" display on the Payment & Confirmation screens
    public string TotalCurrencySymbol => "$";

    public string TotalIntegerPart
    {
        get
        {
            decimal abs = Math.Abs(TotalAmount);
            long intPart = (long)Math.Truncate(abs);
            string prefix = TotalAmount < 0 ? "-" : "";
            return $"{prefix}{intPart.ToString("N0", CultureInfo.InvariantCulture)}";
        }
    }

    public string TotalDecimalPart
    {
        get
        {
            decimal abs = Math.Abs(TotalAmount);
            int cents = (int)Math.Round((abs - Math.Truncate(abs)) * 100m);
            return $".{cents:D2}";
        }
    }

    public void NotifyTotalsChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(SubTotal));
        OnPropertyChanged(nameof(Taxes));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(SubTotalDisplay));
        OnPropertyChanged(nameof(TaxesDisplay));
        OnPropertyChanged(nameof(TotalDisplay));
        OnPropertyChanged(nameof(TotalIntegerPart));
        OnPropertyChanged(nameof(TotalDecimalPart));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}