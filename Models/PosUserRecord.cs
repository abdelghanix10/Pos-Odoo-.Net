using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public class PosUserRecord : INotifyPropertyChanged
{
    private int? _odooId;
    private string _name = string.Empty;
    private string _login = string.Empty;
    private string _email = string.Empty;
    private string _phone = string.Empty;
    private string _password = "••••••••";
    private string _pin = string.Empty;
    private string _role = "User";
    private bool _isPosOnly;
    private int? _assignedPosConfigId;
    private string _assignedPosConfigName = string.Empty;
    private string _odooServerUrl = string.Empty;
    private string _odooDatabase = string.Empty;
    private string _status = "Confirmed";
    private string _language = "English (US)";
    private bool _isPurpleTheme = true;
    private BitmapSource? _avatarBitmap;
    private string? _avatarBase64;
    private bool _isVisibleInPos = true;

    public int? OdooId
    {
        get => _odooId;
        set { if (_odooId != value) { _odooId = value; OnPropertyChanged(); } }
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
                OnPropertyChanged(nameof(Initials));
                OnPropertyChanged(nameof(AvatarSoftBrush));
                OnPropertyChanged(nameof(AvatarAccentBrush));
            }
        }
    }

    public string Login
    {
        get => _login;
        set { if (_login != value) { _login = value; OnPropertyChanged(); } }
    }

    public string Email
    {
        get => _email;
        set { if (_email != value) { _email = value; OnPropertyChanged(); } }
    }

    public string Phone
    {
        get => _phone;
        set { if (_phone != value) { _phone = value; OnPropertyChanged(); } }
    }

    public string Password
    {
        get => _password;
        set { if (_password != value) { _password = value; OnPropertyChanged(); } }
    }

    public string Pin
    {
        get => _pin;
        set { if (_pin != value) { _pin = value; OnPropertyChanged(); } }
    }

    public string Role
    {
        get => _role;
        set
        {
            if (_role != value)
            {
                _role = value;
                bool posOnlyRole = string.Equals(_role, "POS Only", StringComparison.OrdinalIgnoreCase);
                if (posOnlyRole && !_isPosOnly)
                {
                    _isPosOnly = true;
                    OnPropertyChanged(nameof(IsPosOnly));
                }
                else if (string.Equals(_role, "Administrator", StringComparison.OrdinalIgnoreCase) && _isPosOnly)
                {
                    _isPosOnly = false;
                    OnPropertyChanged(nameof(IsPosOnly));
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(RoleDisplay));
                OnPropertyChanged(nameof(RoleBadgeBackground));
                OnPropertyChanged(nameof(RoleBadgeForeground));
            }
        }
    }

    public bool IsPosOnly
    {
        get => _isPosOnly || string.Equals(_role, "POS Only", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (_isPosOnly != value)
            {
                _isPosOnly = value;
                if (value && !string.Equals(_role, "POS Only", StringComparison.OrdinalIgnoreCase))
                {
                    _role = "POS Only";
                    OnPropertyChanged(nameof(Role));
                }
                else if (!value && string.Equals(_role, "POS Only", StringComparison.OrdinalIgnoreCase))
                {
                    _role = "Administrator";
                    OnPropertyChanged(nameof(Role));
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(RoleDisplay));
                OnPropertyChanged(nameof(RoleBadgeBackground));
                OnPropertyChanged(nameof(RoleBadgeForeground));
            }
        }
    }

    public int? AssignedPosConfigId
    {
        get => _assignedPosConfigId;
        set { if (_assignedPosConfigId != value) { _assignedPosConfigId = value; OnPropertyChanged(); } }
    }

    public string AssignedPosConfigName
    {
        get => _assignedPosConfigName;
        set
        {
            if (_assignedPosConfigName != value)
            {
                _assignedPosConfigName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AssignedPosDisplay));
            }
        }
    }

    public string OdooServerUrl
    {
        get => _odooServerUrl;
        set { if (_odooServerUrl != value) { _odooServerUrl = value; OnPropertyChanged(); } }
    }

    public string OdooDatabase
    {
        get => _odooDatabase;
        set { if (_odooDatabase != value) { _odooDatabase = value; OnPropertyChanged(); } }
    }

    public bool HasConfiguredOdooCredentials =>
        !string.IsNullOrWhiteSpace(_login) &&
        !string.IsNullOrWhiteSpace(_password) &&
        _password != "••••••••";

    public string AssignedPosDisplay =>
        string.IsNullOrWhiteSpace(_assignedPosConfigName) ? "Default POS Register" : _assignedPosConfigName;

    public string RoleDisplay => IsPosOnly ? "POS Only" : Role;

    public Brush RoleBadgeBackground => IsPosOnly
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F4F5"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));

    public Brush RoleBadgeForeground => IsPosOnly
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#017E84"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDotBrush));
                OnPropertyChanged(nameof(ListStatusBackground));
                OnPropertyChanged(nameof(ListStatusForeground));
            }
        }
    }

    public string Language
    {
        get => _language;
        set { if (_language != value) { _language = value; OnPropertyChanged(); } }
    }

    public bool IsPurpleTheme
    {
        get => _isPurpleTheme;
        set
        {
            if (_isPurpleTheme != value)
            {
                _isPurpleTheme = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AvatarSoftBrush));
                OnPropertyChanged(nameof(AvatarAccentBrush));
            }
        }
    }

    public string? AvatarBase64
    {
        get => _avatarBase64;
        set
        {
            if (_avatarBase64 != value)
            {
                _avatarBase64 = value;
                AvatarBitmap = PosImageUtils.Base64ToBitmapSource(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAvatar));
                OnPropertyChanged(nameof(HasNoAvatar));
                OnPropertyChanged(nameof(InitialsVisibility));
                OnPropertyChanged(nameof(HasCustomImageVisibility));
            }
        }
    }

    public BitmapSource? AvatarBitmap
    {
        get => _avatarBitmap;
        set
        {
            if (_avatarBitmap != value)
            {
                _avatarBitmap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAvatar));
                OnPropertyChanged(nameof(HasNoAvatar));
                OnPropertyChanged(nameof(InitialsVisibility));
                OnPropertyChanged(nameof(HasCustomImageVisibility));
            }
        }
    }

    public bool IsVisibleInPos
    {
        get => _isVisibleInPos;
        set
        {
            if (_isVisibleInPos != value)
            {
                _isVisibleInPos = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PosBadgeVisibility));
            }
        }
    }

    public bool HasAvatar => AvatarBitmap is not null;
    public bool HasNoAvatar => AvatarBitmap is null;

    public Visibility PosBadgeVisibility => IsVisibleInPos ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InitialsVisibility => AvatarBitmap is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasCustomImageVisibility => AvatarBitmap is not null ? Visibility.Visible : Visibility.Collapsed;

    public string Initials
    {
        get
        {
            string[] parts = Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "U";
            if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
            return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
        }
    }

    public Brush AvatarSoftBrush => IsPurpleTheme
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4A87B"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#714B67"));

    public Brush AvatarAccentBrush => Brushes.White;

    public Brush StatusDotBrush => Status switch
    {
        "Confirmed" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754")),
        "Invited" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"))
    };

    public Brush ListStatusBackground => Status switch
    {
        "Confirmed" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28A745")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"))
    };

    public Brush ListStatusForeground => Status switch
    {
        "Confirmed" => Brushes.White,
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151"))
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}