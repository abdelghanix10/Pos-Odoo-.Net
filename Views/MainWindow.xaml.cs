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
    private string _currentPin = string.Empty;
    private const int RequiredPinLength = 4;

    public MainWindow()
    {
        InitializeComponent();

        _pinDots = [PinDot1, PinDot2, PinDot3, PinDot4];

        // Hook up MainShellModule's UsersControl with POS Login Screen
        MainShellModule.UsersControl.UsersUpdated += () => _ = RefreshLoginCardsAsync();
        _ = RefreshLoginCardsAsync();

        // Hook up MainShellModule Logout
        MainShellModule.LogoutRequested += (_, _) =>
        {
            MainShellModule.Visibility = Visibility.Collapsed;
            TopLoginHeaderBar.Visibility = Visibility.Visible;
            LoginWorkspaceGrid.Visibility = Visibility.Visible;
            HeaderQuickSetupBtn.Visibility = Visibility.Visible;
            _ = RefreshLoginCardsAsync();
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

        // Live Odoo Connectivity Monitor & Offline Queue Auto-Flush
        OdooSyncService.Instance.OnlineStatusChanged += isOnline =>
        {
            Dispatcher.Invoke(() => UpdateConnectionStatusVisual(isOnline));
        };

        OdooSyncService.Instance.PendingQueueFlushed += count =>
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await MainShellModule.DashboardControl.ReloadConfigsFromDatabaseAsync();
                await MainShellModule.SessionsControl.ReloadSessionsFromDatabaseAsync(resetDetailView: false);
                await MainShellModule.OrdersControl.ReloadOrdersFromDatabaseAsync(resetDetailView: false);
                await MainShellModule.PaymentsControl.ReloadPaymentsFromDatabaseAsync(resetDetailView: false);
                await MainShellModule.RefreshDatabaseCountsAsync();
                MainShellModule.ShowToast($"Synchronized {count} queued POS transaction(s) with Odoo.");
            });
        };

        _connectivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _connectivityTimer.Tick += async (_, _) => await OdooSyncService.Instance.CheckConnectivityAsync();
        _connectivityTimer.Start();
        _ = OdooSyncService.Instance.CheckConnectivityAsync();

        // Load General Application Settings (Screen mode, etc.)
        _ = InitializeGeneralSettingsAsync();
    }

    private async Task InitializeGeneralSettingsAsync()
    {
        try
        {
            var settings = await SqliteDatabaseService.Instance.GetGeneralSettingsAsync();
            ApplyScreenMode(settings.ScreenMode);
        }
        catch { }
    }

    public void ApplyScreenMode(string screenMode)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.Equals(screenMode, "Fullscreen", StringComparison.OrdinalIgnoreCase))
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                ResizeMode = ResizeMode.NoResize;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                ResizeMode = ResizeMode.CanResize;
            }
        });
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
            GlobalConnectionStatusBadge.ToolTip = "Operating in Offline (Local Mode) - Queued transactions will sync when online";
        }
    }

    public async Task RefreshLoginCardsAsync()
    {
        try
        {
            var allUsers = await SqliteDatabaseService.Instance.LoadAllUsersAsync();
            var visibleUsers = allUsers.Where(u => u.IsVisibleInPos).ToList();

            PosLoginUsersItemsControl.ItemsSource = visibleUsers;

            if (visibleUsers.Count > 0)
            {
                UsersScrollViewer.Visibility = Visibility.Visible;
                NoUsersEmptyStateCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                UsersScrollViewer.Visibility = Visibility.Collapsed;
                NoUsersEmptyStateCard.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading login cards: {ex.Message}");
        }
    }

    // ==================== INITIAL SETUP / QUICK SYNC FROM LOGIN ====================
    private async void QuickSyncFromLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            QuickSyncLoginBtn.IsEnabled = false;
            OpenSetupFromLoginBtn.IsEnabled = false;

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl) || string.IsNullOrWhiteSpace(config.DatabaseName) || string.IsNullOrWhiteSpace(config.Username))
            {
                OpenSetupSettings_Click(sender, e);
                MainShellModule.ShowToast("Please enter your Odoo Server URL, Database Name, and Username.", isWarning: true);
                return;
            }

            var progress = new Progress<SyncProgressReport>(report =>
            {
                QuickSyncLoginBtnText.Text = $"Syncing {report.CurrentModule} ({report.Percentage:0}%)...";
                NoUsersSubtitleText.Text = report.StatusMessage;
            });

            var summary = await OdooSyncService.Instance.SyncAllFromOdooAsync(config, progress);
            await MainShellModule.RefreshConfigAndCountsAsync();
            await MainShellModule.ReloadAllChildViewsAsync();
            await RefreshLoginCardsAsync();

            ShowLoginToast(summary.Message);
        }
        catch (Exception ex)
        {
            NoUsersSubtitleText.Text = $"Sync Failed: {ex.Message}";
            ShowLoginToast($"Sync Failed: {ex.Message}");
        }
        finally
        {
            QuickSyncLoginBtnText.Text = "Sync All Data from Odoo Now";
            QuickSyncLoginBtn.IsEnabled = true;
            OpenSetupFromLoginBtn.IsEnabled = true;
        }
    }

    private void OpenSetupSettings_Click(object sender, RoutedEventArgs e)
    {
        LoginWorkspaceGrid.Visibility = Visibility.Collapsed;
        TopLoginHeaderBar.Visibility = Visibility.Collapsed;
        HeaderQuickSetupBtn.Visibility = Visibility.Collapsed;
        MainShellModule.Visibility = Visibility.Visible;
        MainShellModule.SetCurrentUser("Administrator");
        MainShellModule.SwitchToSettings();
    }

    // ==================== POS USER CARD SELECTION ====================
    private void PosUserCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PosUserRecord user })
            return;

        _selectedUser = user;

        if (string.IsNullOrWhiteSpace(user.Pin))
        {
            StartPosSession(user.Name);
            return;
        }

        _currentPin = string.Empty;
        ModalRoleTitleText.Text = user.Name.Trim();
        ModalRoleBadge.Background = user.AvatarSoftBrush;

        PinPromptText.Text = $"Enter {user.Pin.Length}-digit security PIN";
        PinPromptText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        UpdatePinDotsVisual();

        PinPadModalOverlay.Visibility = Visibility.Visible;
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
        int targetLen = (_selectedUser?.Pin.Length > 0) ? _selectedUser.Pin.Length : RequiredPinLength;
        if (_currentPin.Length >= targetLen)
            return;

        _currentPin += digit;
        UpdatePinDotsVisual();

        if (_currentPin.Length == targetLen)
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
        bool isValid = _selectedUser is not null && _currentPin == _selectedUser.Pin;

        if (isValid)
        {
            PinPadModalOverlay.Visibility = Visibility.Collapsed;
            StartPosSession(_selectedUser?.Name ?? "User");
        }
        else
        {
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
    private void StartPosSession(string userName)
    {
        LoginWorkspaceGrid.Visibility = Visibility.Collapsed;
        TopLoginHeaderBar.Visibility = Visibility.Collapsed;
        HeaderQuickSetupBtn.Visibility = Visibility.Collapsed;
        MainShellModule.Visibility = Visibility.Visible;
        MainShellModule.SetCurrentUser(userName);
        MainShellModule.SwitchToDashboard();

        ShowLoginToast($"Authenticated as {userName} — Active POS Session");
    }

    private void ShowLoginToast(string message)
    {
        ToastMessageText.Text = message;
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