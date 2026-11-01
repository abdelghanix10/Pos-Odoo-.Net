using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public class PosSessionRecord : INotifyPropertyChanged
{
    private long _id;
    private int? _odooId;
    private string _name = string.Empty;
    private int? _posConfigId;
    private string _posConfigName = string.Empty;
    private int? _userId;
    private string _userName = string.Empty;
    private string? _userAvatarBase64;
    private BitmapImage? _userAvatarBitmap;
    private DateTime? _startAt;
    private DateTime? _stopAt;
    private string _state = "opened"; // opening_control, opened, closing_control, closed
    private decimal _openingBalance = 0.00m;
    private decimal _closingBalance = 0.00m;
    private int _ordersCount = 0;
    private decimal _totalPaymentsAmount = 0.00m;
    private string _employees = string.Empty;

    public long Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    public int? OdooId
    {
        get => _odooId;
        set { if (_odooId != value) { _odooId = value; OnPropertyChanged(); } }
    }

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public int? PosConfigId
    {
        get => _posConfigId;
        set { if (_posConfigId != value) { _posConfigId = value; OnPropertyChanged(); } }
    }

    public string PosConfigName
    {
        get => _posConfigName;
        set { if (_posConfigName != value) { _posConfigName = value; OnPropertyChanged(); } }
    }

    public int? UserId
    {
        get => _userId;
        set { if (_userId != value) { _userId = value; OnPropertyChanged(); } }
    }

    public string UserName
    {
        get => _userName;
        set
        {
            if (_userName != value)
            {
                _userName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UserInitials));
            }
        }
    }

    public string? UserAvatarBase64
    {
        get => _userAvatarBase64;
        set
        {
            if (_userAvatarBase64 != value)
            {
                _userAvatarBase64 = value;
                UserAvatarBitmap = PosImageUtils.Base64ToBitmapImage(value);
                OnPropertyChanged();
            }
        }
    }

    public BitmapImage? UserAvatarBitmap
    {
        get => _userAvatarBitmap;
        set
        {
            if (_userAvatarBitmap != value)
            {
                _userAvatarBitmap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUserAvatar));
                OnPropertyChanged(nameof(HasNoUserAvatar));
            }
        }
    }

    public DateTime? StartAt
    {
        get => _startAt;
        set
        {
            if (_startAt != value)
            {
                _startAt = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StartAtDisplay));
            }
        }
    }

    public DateTime? StopAt
    {
        get => _stopAt;
        set
        {
            if (_stopAt != value)
            {
                _stopAt = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StopAtDisplay));
            }
        }
    }

    public string State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateDisplay));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusSoftBrush));
                OnPropertyChanged(nameof(CanContinueSelling));
                OnPropertyChanged(nameof(IsStageOpeningControl));
                OnPropertyChanged(nameof(IsStageInProgress));
                OnPropertyChanged(nameof(IsStageClosingControl));
                OnPropertyChanged(nameof(IsStageClosed));
                OnPropertyChanged(nameof(IsClosed));
                OnPropertyChanged(nameof(ShowClosingDate));
            }
        }
    }

    public decimal OpeningBalance
    {
        get => _openingBalance;
        set
        {
            if (_openingBalance != value)
            {
                _openingBalance = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OpeningBalanceDisplay));
            }
        }
    }

    public decimal ClosingBalance
    {
        get => _closingBalance;
        set
        {
            if (_closingBalance != value)
            {
                _closingBalance = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ClosingBalanceDisplay));
            }
        }
    }

    public int OrdersCount
    {
        get => _ordersCount;
        set
        {
            if (_ordersCount != value)
            {
                _ordersCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OrdersCountDisplay));
            }
        }
    }

    public decimal TotalPaymentsAmount
    {
        get => _totalPaymentsAmount;
        set
        {
            if (_totalPaymentsAmount != value)
            {
                _totalPaymentsAmount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalPaymentsAmountDisplay));
            }
        }
    }

    public string Employees
    {
        get => _employees;
        set
        {
            if (_employees != value)
            {
                _employees = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EmployeesDisplay));
            }
        }
    }

    // ==================== DISPLAY & COMPUTED PROPERTIES ====================
    public string StartAtDisplay => StartAt.HasValue ? StartAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "—";
    public string StopAtDisplay => StopAt.HasValue ? StopAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "—";
    public bool ShowClosingDate => IsClosed && StopAt.HasValue;

    public string StateDisplay => State?.ToLowerInvariant() switch
    {
        "opening_control" => "Opening Control",
        "closing_control" => "Closing Control",
        "closed" => "Closed & Posted",
        _ => "In Progress"
    };

    public Brush StatusBrush => State?.ToLowerInvariant() switch
    {
        "opening_control" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#017E84")), // Teal
        "closed" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")), // Green
        "closing_control" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")), // Amber
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")) // Green for In Progress
    };

    public Brush StatusSoftBrush => State?.ToLowerInvariant() switch
    {
        "opening_control" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F4F5")),
        "closed" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5")),
        "closing_control" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"))
    };

    public bool CanContinueSelling => State == "opening_control" || State == "opened";
    public bool IsClosed => State == "closed";

    public bool IsStageOpeningControl => State == "opening_control";
    public bool IsStageInProgress => State == "opened";
    public bool IsStageClosingControl => State == "closing_control";
    public bool IsStageClosed => State == "closed";

    public string OpeningBalanceDisplay => $"$ {OpeningBalance:N2}";
    public string ClosingBalanceDisplay => $"$ {ClosingBalance:N2}";

    public string OrdersCountDisplay => $"{OrdersCount} {(OrdersCount == 1 ? "Order" : "Orders")}";
    public string TotalPaymentsAmountDisplay => $"$ {TotalPaymentsAmount:N2}";

    public string EmployeesDisplay => string.IsNullOrWhiteSpace(Employees) ? UserName : Employees;

    public bool HasUserAvatar => UserAvatarBitmap != null;
    public bool HasNoUserAvatar => UserAvatarBitmap == null;

    public string UserInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(UserName)) return "U";
            var parts = UserName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
            return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
