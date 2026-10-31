using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

// ==================== USER MODEL FOR KANBAN & FORM ====================
public class PosUserRecord
{
    public int? OdooId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = "••••••••";
    public string Pin { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed";
    public bool IsPurpleTheme { get; set; } = true;
    public BitmapImage? AvatarBitmap { get; set; }
    public string? AvatarBase64 { get; set; }
    public bool IsVisibleInPos { get; set; } = true;

    public Visibility PosBadgeVisibility => IsVisibleInPos ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InitialsVisibility => AvatarBitmap is null ? Visibility.Visible : Visibility.Collapsed;

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

    public Visibility HasCustomImageVisibility =>
        AvatarBitmap is not null ? Visibility.Visible : Visibility.Collapsed;

    public Brush AvatarSoftBrush => IsPurpleTheme
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3ECF1"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F4F5"));

    public Brush AvatarAccentBrush => IsPurpleTheme
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#714B67"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#017E84"));

    public Brush StatusDotBrush => Status switch
    {
        "Confirmed" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
        "Invited" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"))
    };
}
