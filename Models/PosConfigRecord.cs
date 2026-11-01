using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public class PosConfigRecord : INotifyPropertyChanged
{
    private long _id;
    private int _odooId;
    private string _name = string.Empty;
    private int? _currentSessionId;
    private string? _currentSessionName;
    private string _currentSessionState = "closed"; // "closed", "opening_control", "opened", "to_close"
    private DateTime? _lastClosingDate;
    private decimal _lastClosingBalance = 0.00m;
    private decimal _openingBalance = 0.00m;
    private int? _activeUserId;
    private string? _activeUserName;
    private string? _activeUserAvatarBase64;
    private BitmapImage? _activeUserAvatarBitmap;
    private string _recentSalesPointsJson = "[]";
    private List<double> _recentSalesPoints = [];

    // Sparkline visual cache
    private string _strokeGeometryString = string.Empty;
    private string _areaGeometryString = string.Empty;
    private string _markerGeometryString = string.Empty;

    public long Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    public int OdooId
    {
        get => _odooId;
        set { if (_odooId != value) { _odooId = value; OnPropertyChanged(); } }
    }

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public int? CurrentSessionId
    {
        get => _currentSessionId;
        set { if (_currentSessionId != value) { _currentSessionId = value; OnPropertyChanged(); } }
    }

    public string? CurrentSessionName
    {
        get => _currentSessionName;
        set { if (_currentSessionName != value) { _currentSessionName = value; OnPropertyChanged(); } }
    }

    public string CurrentSessionState
    {
        get => _currentSessionState;
        set
        {
            if (_currentSessionState != value)
            {
                _currentSessionState = value;
                OnPropertyChanged();
                NotifyStatePropertiesChanged();
            }
        }
    }

    public DateTime? LastClosingDate
    {
        get => _lastClosingDate;
        set
        {
            if (_lastClosingDate != value)
            {
                _lastClosingDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastClosingDateDisplay));
                OnPropertyChanged(nameof(ActiveSessionDateDisplay));
            }
        }
    }

    public decimal LastClosingBalance
    {
        get => _lastClosingBalance;
        set
        {
            if (_lastClosingBalance != value)
            {
                _lastClosingBalance = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastClosingBalanceDisplay));
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

    public int? ActiveUserId
    {
        get => _activeUserId;
        set { if (_activeUserId != value) { _activeUserId = value; OnPropertyChanged(); } }
    }

    public string? ActiveUserName
    {
        get => _activeUserName;
        set
        {
            if (_activeUserName != value)
            {
                _activeUserName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActiveUser));
                OnPropertyChanged(nameof(ActiveUserInitials));
            }
        }
    }

    public string? ActiveUserAvatarBase64
    {
        get => _activeUserAvatarBase64;
        set
        {
            if (_activeUserAvatarBase64 != value)
            {
                _activeUserAvatarBase64 = value;
                ActiveUserAvatarBitmap = PosImageUtils.Base64ToBitmapImage(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActiveUserAvatar));
            }
        }
    }

    public BitmapImage? ActiveUserAvatarBitmap
    {
        get => _activeUserAvatarBitmap;
        set
        {
            if (_activeUserAvatarBitmap != value)
            {
                _activeUserAvatarBitmap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActiveUserAvatar));
            }
        }
    }

    public string RecentSalesPointsJson
    {
        get => _recentSalesPointsJson;
        set
        {
            if (_recentSalesPointsJson != value)
            {
                _recentSalesPointsJson = value;
                OnPropertyChanged();
                ParseAndGenerateSparkline();
            }
        }
    }

    public List<double> RecentSalesPoints
    {
        get => _recentSalesPoints;
        set
        {
            _recentSalesPoints = value ?? [];
            OnPropertyChanged();
            GenerateSparklineGeometries();
        }
    }

    // =========================================================================
    // DYNAMIC STATES & PROPERTIES (Odoo 20 Design System)
    // =========================================================================
    public bool IsClosed =>
        string.Equals(CurrentSessionState, "closed", StringComparison.OrdinalIgnoreCase);

    public bool IsOpeningControl =>
        string.Equals(CurrentSessionState, "opening_control", StringComparison.OrdinalIgnoreCase);

    public bool IsToClose =>
        string.Equals(CurrentSessionState, "opened", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentSessionState, "to_close", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentSessionState, "closing_control", StringComparison.OrdinalIgnoreCase);

    // Conditional Status Badge:
    // Opening Control -> Teal pill (#017E84)
    // To Close -> Red pill (#DC2626)
    // Closed -> Collapsed
    public Visibility StatusBadgeVisibility =>
        IsClosed ? Visibility.Collapsed : Visibility.Visible;

    public string StatusBadgeText =>
        IsOpeningControl ? "Opening Control" : (IsToClose ? "To Close" : "");

    public Brush StatusBadgeBackground =>
        IsOpeningControl
            ? new SolidColorBrush(Color.FromRgb(0x01, 0x7E, 0x84))
            : (IsToClose
                ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
                : Brushes.Transparent);

    public Brush StatusBadgeForeground => Brushes.White;

    // Primary Action Button:
    // "Open Register" when closed or in Opening Control
    // "Continue Selling" when active / To Close
    public string PrimaryActionButtonText =>
        (IsClosed || IsOpeningControl) ? "Open Register" : "Continue Selling";

    // Active User Avatar (shown directly below button, hidden when register is completely closed)
    public bool HasActiveUser => !IsClosed && !string.IsNullOrWhiteSpace(ActiveUserName);
    public bool HasActiveUserAvatar => ActiveUserAvatarBitmap != null;

    public string ActiveUserInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ActiveUserName))
                return "U";
            var parts = ActiveUserName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
                return parts[0][0].ToString().ToUpperInvariant();
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
        }
    }

    // Metrics Displays
    public string LastClosingDateDisplay =>
        LastClosingDate.HasValue ? LastClosingDate.Value.ToString("MMM dd", CultureInfo.InvariantCulture) : "Oct 20";

    public string ActiveSessionDateDisplay =>
        LastClosingDate.HasValue ? LastClosingDate.Value.ToString("MMM dd", CultureInfo.InvariantCulture) : "Oct 27";

    public string LastClosingBalanceDisplay =>
        string.Format(CultureInfo.InvariantCulture, "$ {0:N2}", LastClosingBalance);

    public string OpeningBalanceDisplay =>
        string.Format(CultureInfo.InvariantCulture, "$ {0:N2}", OpeningBalance);

    // Dynamic Metrics Row Visibility:
    // Closed State: Row 1 (Closing Oct 20), Row 2 (Balance $ 51.49)
    // Opening Control: Single Row (Opening $ 0.00)
    // Active / To Close: Row 1 (Date Oct 27), Row 2 (Opening $ 25.59)
    public Visibility ClosedMetricsVisibility => IsClosed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OpeningControlMetricsVisibility => IsOpeningControl ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ToCloseMetricsVisibility => IsToClose ? Visibility.Visible : Visibility.Collapsed;

    // =========================================================================
    // SALES SPARKLINE AREA CHART PROPERTIES
    // =========================================================================
    public bool IsZeroActivity =>
        RecentSalesPoints.Count == 0 || RecentSalesPoints.All(p => p <= 0.0001);

    public string StrokeGeometryString
    {
        get => _strokeGeometryString;
        private set { _strokeGeometryString = value; OnPropertyChanged(); }
    }

    public string AreaGeometryString
    {
        get => _areaGeometryString;
        private set { _areaGeometryString = value; OnPropertyChanged(); }
    }

    public string MarkerGeometryString
    {
        get => _markerGeometryString;
        private set { _markerGeometryString = value; OnPropertyChanged(); }
    }

    public Brush SparklineStrokeBrush =>
        IsZeroActivity
            ? new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB))
            : new SolidColorBrush(Color.FromRgb(0x71, 0x4B, 0x67));

    public Brush SparklineMarkerBrush =>
        IsZeroActivity
            ? new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB))
            : new SolidColorBrush(Color.FromRgb(0x71, 0x4B, 0x67));

    public Visibility SparklineAreaVisibility =>
        IsZeroActivity ? Visibility.Collapsed : Visibility.Visible;

    private void NotifyStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(IsOpeningControl));
        OnPropertyChanged(nameof(IsToClose));
        OnPropertyChanged(nameof(StatusBadgeVisibility));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(StatusBadgeBackground));
        OnPropertyChanged(nameof(PrimaryActionButtonText));
        OnPropertyChanged(nameof(HasActiveUser));
        OnPropertyChanged(nameof(ClosedMetricsVisibility));
        OnPropertyChanged(nameof(OpeningControlMetricsVisibility));
        OnPropertyChanged(nameof(ToCloseMetricsVisibility));
    }

    private void ParseAndGenerateSparkline()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_recentSalesPointsJson))
            {
                var list = JsonSerializer.Deserialize<List<double>>(_recentSalesPointsJson);
                if (list != null && list.Count > 0)
                {
                    _recentSalesPoints = list;
                }
                else
                {
                    _recentSalesPoints = [0, 0, 0, 0, 0];
                }
            }
            else
            {
                _recentSalesPoints = [0, 0, 0, 0, 0];
            }
        }
        catch
        {
            _recentSalesPoints = [0, 0, 0, 0, 0];
        }

        GenerateSparklineGeometries();
    }

    public void GenerateSparklineGeometries()
    {
        const double width = 300.0;
        const double height = 80.0;
        const double baselineY = 65.0;

        OnPropertyChanged(nameof(IsZeroActivity));
        OnPropertyChanged(nameof(SparklineStrokeBrush));
        OnPropertyChanged(nameof(SparklineMarkerBrush));
        OnPropertyChanged(nameof(SparklineAreaVisibility));

        if (IsZeroActivity || _recentSalesPoints.Count < 2)
        {
            // Flat light-gray baseline (#D1D5DB) with gray dot markers
            StrokeGeometryString = string.Format(CultureInfo.InvariantCulture, "M 0,{0:F1} L {1:F1},{0:F1}", baselineY, width);
            AreaGeometryString = string.Empty;

            // Generate 5 gray circular markers along the baseline
            var markerSb = new System.Text.StringBuilder();
            int count = Math.Max(5, _recentSalesPoints.Count);
            double step = width / (count - 1);
            for (int i = 0; i < count; i++)
            {
                double mx = i * step;
                markerSb.Append(string.Format(CultureInfo.InvariantCulture,
                    "M {0:F1},{1:F1} m -3,0 a 3,3 0 1,0 6,0 a 3,3 0 1,0 -6,0 ", mx, baselineY));
            }
            MarkerGeometryString = markerSb.ToString();
            return;
        }

        // Active sales: compute smooth cubic Bezier spline curve
        int n = _recentSalesPoints.Count;
        double maxVal = _recentSalesPoints.Max();
        if (maxVal <= 0.001) maxVal = 1.0;

        var points = new Point[n];
        double xStep = width / (n - 1);
        for (int i = 0; i < n; i++)
        {
            double val = _recentSalesPoints[i];
            double normalized = Math.Clamp(val / maxVal, 0.0, 1.0);
            double py = baselineY - (normalized * 50.0); // leave 15px margin at top
            points[i] = new Point(i * xStep, py);
        }

        // Build smooth curve path
        var strokeSb = new System.Text.StringBuilder();
        var areaSb = new System.Text.StringBuilder();
        var markersSb = new System.Text.StringBuilder();

        strokeSb.Append(string.Format(CultureInfo.InvariantCulture, "M {0:F1},{1:F1}", points[0].X, points[0].Y));
        areaSb.Append(string.Format(CultureInfo.InvariantCulture, "M 0,{0:F1} L {1:F1},{2:F1}", height, points[0].X, points[0].Y));

        for (int i = 1; i < n; i++)
        {
            var p0 = points[i - 1];
            var p1 = points[i];
            double midX = (p0.X + p1.X) / 2.0;

            strokeSb.Append(string.Format(CultureInfo.InvariantCulture,
                " C {0:F1},{1:F1} {0:F1},{2:F1} {3:F1},{2:F1}",
                midX, p0.Y, p1.Y, p1.X));

            areaSb.Append(string.Format(CultureInfo.InvariantCulture,
                " C {0:F1},{1:F1} {0:F1},{2:F1} {3:F1},{2:F1}",
                midX, p0.Y, p1.Y, p1.X));
        }

        areaSb.Append(string.Format(CultureInfo.InvariantCulture, " L {0:F1},{1:F1} L 0,{1:F1} Z", width, height));

        // Generate circular dot markers
        for (int i = 0; i < n; i++)
        {
            markersSb.Append(string.Format(CultureInfo.InvariantCulture,
                "M {0:F1},{1:F1} m -3.5,0 a 3.5,3.5 0 1,0 7,0 a 3.5,3.5 0 1,0 -7,0 ",
                points[i].X, points[i].Y));
        }

        StrokeGeometryString = strokeSb.ToString();
        AreaGeometryString = areaSb.ToString();
        MarkerGeometryString = markersSb.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
