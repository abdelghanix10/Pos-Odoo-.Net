using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OdooPosLogin;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _connectivityTimer;
    private readonly Ellipse[] _pinDots;

    private PosUserRecord? _selectedUser;
    private string _selectedRole = string.Empty;
    private string _currentPin = string.Empty;
    private const int RequiredPinLength = 4;

    // Demo role PINs (replace with your authentication service)
    private readonly Dictionary<string, string> _rolePins = new()
    {
        ["Administrator"] = "1234",
        ["Manager"] = "1234",
        ["Employee"] = "1234"
    };

    public MainWindow()
    {
        InitializeComponent();

        _pinDots = [PinDot1, PinDot2, PinDot3, PinDot4];

        // Hook up MainShellModule's UsersControl with POS Login Screen
        MainShellModule.UsersControl.UsersUpdated += RefreshLoginCards;
        RefreshLoginCards();

        // Hook up MainShellModule Logout
        MainShellModule.LogoutRequested += (_, _) =>
        {
            MainShellModule.Visibility = Visibility.Collapsed;
            RefreshLoginCards();
        };

        // Live Header Clock Timer
        UpdateDateTimeDisplay();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateDateTimeDisplay();
        _clockTimer.Start();

        // Toast Auto-Hide Timer
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            SessionToastBanner.Visibility = Visibility.Collapsed;
        };

        // Live Odoo Connectivity Monitor & Global Header Status Badge
        OdooSyncService.Instance.OnlineStatusChanged += isOnline =>
        {
            Dispatcher.Invoke(() => UpdateConnectionStatusVisual(isOnline));
        };
        _connectivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _connectivityTimer.Tick += async (_, _) => await OdooSyncService.Instance.CheckConnectivityAsync();
        _connectivityTimer.Start();
        _ = OdooSyncService.Instance.CheckConnectivityAsync();
    }

    private void UpdateDateTimeDisplay()
    {
        DateTime now = DateTime.Now;
        ClockTextBlock.Text = now.ToString("hh:mm:ss tt");
        DateTextBlock.Text = now.ToString("dddd, MMM dd, yyyy");
    }

    public void UpdateConnectionStatusVisual(bool isOnline)
    {
        if (isOnline)
        {
            GlobalStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            GlobalStatusText.Text = "Odoo Online";
            GlobalStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
            GlobalConnectionStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
            GlobalConnectionStatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
            GlobalConnectionStatusBadge.ToolTip = "Connected to Odoo Online";
        }
        else
        {
            GlobalStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            GlobalStatusText.Text = "Offline (Local Mode)";
            GlobalStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E"));
            GlobalConnectionStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
            GlobalConnectionStatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
            GlobalConnectionStatusBadge.ToolTip = "Operating in Offline (Local Mode) - Odoo Online is unreachable";
        }
    }

    private void RefreshLoginCards()
    {
        MainShellModule.UsersControl.RefreshPosLoginUsers();
        PosLoginUsersItemsControl.ItemsSource = null;
        PosLoginUsersItemsControl.ItemsSource = MainShellModule.UsersControl.PosLoginUsers;
    }

    // ==================== POS USER CARD SELECTION ====================
    private void PosUserCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PosUserRecord user })
            return;

        _selectedUser = user;
        _selectedRole = user.Name;

        // If no PIN configured, log in instantly
        if (string.IsNullOrWhiteSpace(user.Pin))
        {
            StartPosSession(user.Name);
            return;
        }

        // Open Touch PIN Pad Modal for protected user
        _currentPin = string.Empty;
        ModalRoleTitleText.Text = user.Name.Trim();
        ModalRoleBadge.Background = user.AvatarSoftBrush;

        PinPromptText.Text = "Enter 4-digit security PIN (Demo: 1234)";
        PinPromptText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        UpdatePinDotsVisual();

        PinPadModalOverlay.Visibility = Visibility.Visible;
    }

    // Legacy handler fallback
    private void UserRoleCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string role })
        {
            _selectedUser = null;
            _selectedRole = role;
            if (role == "Guest")
            {
                StartPosSession(role);
                return;
            }
            _currentPin = string.Empty;
            ModalRoleTitleText.Text = role;
            PinPromptText.Text = "Enter 4-digit security PIN (Demo: 1234)";
            PinPromptText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            UpdatePinDotsVisual();
            PinPadModalOverlay.Visibility = Visibility.Visible;
        }
    }

    // ==================== TOUCH PIN PAD LOGIC ====================
    private void PinDigit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string digit })
        {
            AppendPinDigit(digit);
        }
    }

    private void AppendPinDigit(string digit)
    {
        if (_currentPin.Length >= RequiredPinLength)
            return;

        _currentPin += digit;
        UpdatePinDotsVisual();

        if (_currentPin.Length == RequiredPinLength)
        {
            ValidateEnteredPin();
        }
    }

    private void PinBackspace_Click(object sender, RoutedEventArgs e) => RemoveLastPinDigit();

    private void RemoveLastPinDigit()
    {
        if (_currentPin.Length > 0)
        {
            _currentPin = _currentPin[..^1];
            UpdatePinDotsVisual();
        }
    }

    private void PinClear_Click(object sender, RoutedEventArgs e)
    {
        _currentPin = string.Empty;
        UpdatePinDotsVisual();
    }

    private void ValidateEnteredPin()
    {
        bool isValid = false;

        if (_selectedUser is not null)
        {
            isValid = _currentPin == _selectedUser.Pin;
        }
        else if (_rolePins.TryGetValue(_selectedRole, out string? expectedPin))
        {
            isValid = _currentPin == expectedPin;
        }

        if (isValid)
        {
            PinPadModalOverlay.Visibility = Visibility.Collapsed;
            string userName = _selectedUser?.Name ?? _selectedRole;
            StartPosSession(userName);
        }
        else
        {
            // Show error state in red and reset PIN
            PinPromptText.Text = "Invalid PIN code. Please try again.";
            PinPromptText.Foreground = (Brush)FindResource("DangerRedBrush");
            _currentPin = string.Empty;
            UpdatePinDotsVisual(isError: true);
        }
    }

    private void UpdatePinDotsVisual(bool isError = false)
    {
        Brush activeBrush = (Brush)FindResource("OdooPurpleBrush");
        Brush errorBrush = (Brush)FindResource("DangerRedBrush");
        Brush emptyStrokeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));

        for (int i = 0; i < _pinDots.Length; i++)
        {
            if (isError)
            {
                _pinDots[i].Fill = Brushes.Transparent;
                _pinDots[i].Stroke = errorBrush;
            }
            else if (i < _currentPin.Length)
            {
                _pinDots[i].Fill = activeBrush;
                _pinDots[i].Stroke = activeBrush;
            }
            else
            {
                _pinDots[i].Fill = Brushes.Transparent;
                _pinDots[i].Stroke = emptyStrokeBrush;
            }
        }
    }

    private void ClosePinModal_Click(object sender, RoutedEventArgs e)
    {
        _currentPin = string.Empty;
        PinPadModalOverlay.Visibility = Visibility.Collapsed;
    }

    // ==================== HARDWARE KEYBOARD NUMPAD SUPPORT ====================
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (PinPadModalOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key is >= Key.D0 and <= Key.D9)
            {
                AppendPinDigit((e.Key - Key.D0).ToString());
                e.Handled = true;
            }
            else if (e.Key is >= Key.NumPad0 and <= Key.NumPad9)
            {
                AppendPinDigit((e.Key - Key.NumPad0).ToString());
                e.Handled = true;
            }
            else if (e.Key == Key.Back)
            {
                RemoveLastPinDigit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ClosePinModal_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (ExitModalOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CancelExitModal_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    // ==================== SESSION START & EXIT LOGIC ====================
    private void StartPosSession(string role)
    {
        MainShellModule.Visibility = Visibility.Visible;
        MainShellModule.SetCurrentUser(role);
        MainShellModule.SwitchToDashboard();

        ToastMessageText.Text = $"Authenticated as {role} — Active POS Session";
        SessionToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        ExitModalOverlay.Visibility = Visibility.Visible;
    }

    private void CancelExitModal_Click(object sender, RoutedEventArgs e)
    {
        ExitModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void ConfirmShutdown_Click(object sender, RoutedEventArgs e)
    {
        _clockTimer.Stop();
        _toastTimer.Stop();
        _connectivityTimer.Stop();
        Application.Current.Shutdown();
    }
}
