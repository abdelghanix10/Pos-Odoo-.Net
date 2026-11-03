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
    private string _status = "Confirmed";
    private string _language = "English (US)";
    private bool _isPurpleTheme = true;
    private BitmapImage? _avatarBitmap;
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
        set { if (_role != value) { _role = value; OnPropertyChanged(); } }
    }

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
                AvatarBitmap = PosImageUtils.Base64ToBitmapImage(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAvatar));
                OnPropertyChanged(nameof(HasNoAvatar));
                OnPropertyChanged(nameof(InitialsVisibility));
                OnPropertyChanged(nameof(HasCustomImageVisibility));
            }
        }
    }

    public BitmapImage? AvatarBitmap
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