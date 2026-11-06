using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public class SparklineDataPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double CanvasLeft => X - 6.0;
    public double CanvasTop => Y - 6.0;
    public double TooltipLeft => Math.Clamp(X - 75.0, 6.0, 145.0);
    public double TooltipTop => Math.Max(2.0, Y - 48.0);
    public double Value { get; set; }
    public string DateLabel { get; set; } = string.Empty;
    public string ValueLabel => string.Format(CultureInfo.InvariantCulture, "Sales Last 7 Days: {0:0.##}", Value);
}

public class PosConfigRecord : INotifyPropertyChanged
{
    private static readonly Brush OpeningControlBadgeBrush = CreateFrozenBrush("#318896");
    private static readonly Brush ToCloseBadgeBrush = CreateFrozenBrush("#DC2626");
    private static readonly Brush ActiveSparklineBrush = CreateFrozenBrush("#6D4C64");
    private static readonly Brush InactiveSparklineBrush = CreateFrozenBrush("#E5E7EB");
    private static readonly Brush InactiveMarkerBrush = CreateFrozenBrush("#D1D5DB");

    private long _id;
    private int _odooId;
    private string _name = string.Empty;
    private int? _currentSessionId;
    private string? _currentSessionName;
    private string _currentSessionState = "closed"; // "closed", "opening_control", "opened", "to_close", "closing_control"
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

    public ObservableCollection<SparklineDataPoint> SparklinePoints { get; } = [];

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

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
                OnPropertyChanged(nameof(HasLastClosingHistory));
                OnPropertyChanged(nameof(LastClosingDateDisplay));
                OnPropertyChanged(nameof(LastClosingBalanceDisplay));
                OnPropertyChanged(nameof(ActiveSessionDateDisplay));
                GenerateSparklineGeometries();
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
                OnPropertyChanged(nameof(HasLastClosingHistory));
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
                OnPropertyChanged(nameof(HasActiveUser));
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
    // DYNAMIC STATES & PROPERTIES (Odoo 20 Design System - Screenshot 10)
    // =========================================================================
    public bool IsClosed =>
        string.IsNullOrWhiteSpace(CurrentSessionState) ||
        string.Equals(CurrentSessionState, "closed", StringComparison.OrdinalIgnoreCase);

    public bool IsOpeningControl =>
        string.Equals(CurrentSessionState, "opening_control", StringComparison.OrdinalIgnoreCase);

    public bool IsToClose =>
        string.Equals(CurrentSessionState, "opened", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentSessionState, "to_close", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentSessionState, "closing_control", StringComparison.OrdinalIgnoreCase);

    public Visibility StatusBadgeVisibility =>
        (IsOpeningControl || string.Equals(CurrentSessionState, "to_close", StringComparison.OrdinalIgnoreCase) || string.Equals(CurrentSessionState, "closing_control", StringComparison.OrdinalIgnoreCase))
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string StatusBadgeText =>
        IsOpeningControl ? "Opening Control" : (IsToClose ? "To Close" : "");

    public Brush StatusBadgeBackground =>
        IsOpeningControl ? OpeningControlBadgeBrush : (IsToClose ? ToCloseBadgeBrush : Brushes.Transparent);

    public Brush StatusBadgeForeground => Brushes.White;

    public string PrimaryActionButtonText =>
        (IsClosed || IsOpeningControl) ? "Open Register" : "Continue Selling";

    public bool HasActiveUser => !IsClosed && (!string.IsNullOrWhiteSpace(ActiveUserName) || HasActiveUserAvatar);
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

    public bool HasLastClosingHistory => LastClosingDate.HasValue || LastClosingBalance > 0m;

    // Matches Screenshot 10: "Nov 2" when closed history exists, empty when never opened/closed (like Furniture Shop)
    public string LastClosingDateDisplay =>
        LastClosingDate.HasValue
            ? LastClosingDate.Value.ToString("MMM d", CultureInfo.InvariantCulture)
            : string.Empty;

    public string ActiveSessionDateDisplay =>
        LastClosingDate.HasValue
            ? LastClosingDate.Value.ToString("MMM d", CultureInfo.InvariantCulture)
            : DateTime.Now.ToString("MMM d", CultureInfo.InvariantCulture);

    public string LastClosingBalanceDisplay =>
        HasLastClosingHistory
            ? string.Format(CultureInfo.InvariantCulture, "$ {0:N2}", LastClosingBalance)
            : string.Empty;

    public string OpeningBalanceDisplay =>
        string.Format(CultureInfo.InvariantCulture, "$ {0:N2}", OpeningBalance);

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
        IsZeroActivity ? InactiveSparklineBrush : ActiveSparklineBrush;

    public Brush SparklineMarkerBrush =>
        IsZeroActivity ? InactiveMarkerBrush : ActiveSparklineBrush;

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
                _recentSalesPoints = (list != null && list.Count > 0) ? list : [0, 0, 0, 0, 0];
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
        const double baselineY = 78.0;
        const double flatLineY = 66.0;

        OnPropertyChanged(nameof(IsZeroActivity));
        OnPropertyChanged(nameof(SparklineStrokeBrush));
        OnPropertyChanged(nameof(SparklineMarkerBrush));
        OnPropertyChanged(nameof(SparklineAreaVisibility));

        SparklinePoints.Clear();
        DateTime anchorDate = LastClosingDate ?? DateTime.Now;

        if (IsZeroActivity || _recentSalesPoints.Count < 2)
        {
            StrokeGeometryString = string.Format(CultureInfo.InvariantCulture, "M 0,{0:F1} L {1:F1},{0:F1}", flatLineY, width);
            AreaGeometryString = string.Empty;

            var markerSb = new StringBuilder();
            int count = 5;
            double leftMargin = 42.0;
            double rightMargin = 42.0;
            double step = (width - leftMargin - rightMargin) / (count - 1);

            for (int i = 0; i < count; i++)
            {
                double mx = leftMargin + (i * step);
                markerSb.Append(string.Format(CultureInfo.InvariantCulture,
                    "M {0:F1},{1:F1} m -2.2,0 a 2.2,2.2 0 1,0 4.4,0 a 2.2,2.2 0 1,0 -4.4,0 ", mx, flatLineY));
            }
            MarkerGeometryString = markerSb.ToString();
            return;
        }

        int n = _recentSalesPoints.Count;
        double maxVal = _recentSalesPoints.Max();
        if (maxVal <= 0.001) maxVal = 1.0;

        var points = new Point[n];
        double padX = 36.0;
        double usableWidth = width - (padX * 2.0);
        double xStep = usableWidth / (n - 1);

        for (int i = 0; i < n; i++)
        {
            double val = _recentSalesPoints[i];
            double normalized = Math.Clamp(val / maxVal, 0.0, 1.0);
            double py = baselineY - (normalized * 62.0);
            double px = padX + (i * xStep);
            points[i] = new Point(px, py);

            DateTime ptDate = anchorDate.AddDays(-(n - 1 - i));
            SparklinePoints.Add(new SparklineDataPoint
            {
                X = px,
                Y = py,
                Value = val,
                DateLabel = ptDate.ToString("d MMM", CultureInfo.InvariantCulture)
            });
        }

        var strokeSb = new StringBuilder();
        var areaSb = new StringBuilder();
        var markersSb = new StringBuilder();

        // Start from left edge along baseline up to first point
        strokeSb.Append(string.Format(CultureInfo.InvariantCulture, "M 0,{0:F1} L {1:F1},{2:F1}", baselineY, points[0].X, points[0].Y));
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

        strokeSb.Append(string.Format(CultureInfo.InvariantCulture, " L {0:F1},{1:F1}", width, baselineY));
        areaSb.Append(string.Format(CultureInfo.InvariantCulture, " L {0:F1},{1:F1} L 0,{1:F1} Z", width, height));

        for (int i = 0; i < n; i++)
        {
            markersSb.Append(string.Format(CultureInfo.InvariantCulture,
                "M {0:F1},{1:F1} m -3,0 a 3,3 0 1,0 6,0 a 3,3 0 1,0 -6,0 ",
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