using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OdooPosLogin;

public partial class MainShellView : UserControl
{
    private bool _isSidebarCollapsed = false;
    private readonly double _expandedSidebarWidth = 180.0;
    private readonly double _collapsedSidebarWidth = 56.0;

    private bool _isPasswordRevealed = false;
    private readonly DispatcherTimer _toastTimer;

    // Events to communicate with parent Window
    public event EventHandler? LogoutRequested;

    public string CurrentUser { get; private set; } = "Administrator";
    public UserManagementView UsersControl => UsersViewControl;

    public MainShellView()
    {
        InitializeComponent();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ShellToastBanner.Visibility = Visibility.Collapsed;
        };

        // Hook up UserManagementView "Back to POS" to return to Settings view
        UsersViewControl.RequestCloseToPos += (_, _) =>
        {
            SwitchToSettings();
        };

        // Listen for live online status updates
        OdooSyncService.Instance.OnlineStatusChanged += isOnline =>
        {
            Dispatcher.Invoke(() =>
            {
                if (isOnline)
                {
                    ConnectionStatusDot.Fill = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));
                    ConnectionStatusText.Text = "Odoo Online";
                }
                else
                {
                    ConnectionStatusDot.Fill = GetSafeBrush("MutedTextBrush", Color.FromRgb(0x9C, 0xA3, 0xAF));
                    ConnectionStatusText.Text = "Offline (Local Mode)";
                }
            });
        };

        // Load persisted Odoo configuration from SQLite
        _ = LoadSavedConfigAsync();
    }

    private async Task LoadSavedConfigAsync()
    {
        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            CfgServerUrlTextBox.Text = config.ServerUrl;
            CfgDatabaseTextBox.Text = config.DatabaseName;
            CfgUsernameTextBox.Text = config.Username;
            SetCurrentPassword(config.ApiKey ?? "");

            LastSyncedStatusText.Text = string.IsNullOrEmpty(config.LastSyncedAt)
                ? "Last Synced: Never"
                : $"Last Synced: {config.LastSyncedAt}";

            if (!string.IsNullOrEmpty(config.LastStatus))
            {
                ConnectionStatusText.Text = config.LastStatus;
                if (config.LastStatus.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) ||
                    config.LastStatus.Equals("Odoo Online", StringComparison.OrdinalIgnoreCase))
                {
                    ConnectionStatusDot.Fill = (Brush)FindResource("ActiveGreenBrush");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading Odoo config: {ex.Message}");
        }
    }

    public void SetCurrentUser(string userName)
    {
        CurrentUser = userName;
    }

    // =========================================================================
    // 1. COLLAPSIBLE LEFT SIDEBAR TOGGLE (|< / >|)
    // =========================================================================
    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;

        if (_isSidebarCollapsed)
        {
            // Collapse navigation panel into icon-only mode
            SidebarMenuColumn.Width = new GridLength(_collapsedSidebarWidth);
            UsersLabel.Visibility = Visibility.Collapsed;
            SettingsLabel.Visibility = Visibility.Collapsed;
            LogoutLabel.Visibility = Visibility.Collapsed;

            ToggleIconScale.ScaleX = -1; // Flip glyph horizontally to >|
            SidebarToggleButton.ToolTip = "Expand Navigation Sidebar (>|)";
        }
        else
        {
            // Expand navigation panel to show icons & labels
            SidebarMenuColumn.Width = new GridLength(_expandedSidebarWidth);
            UsersLabel.Visibility = Visibility.Visible;
            SettingsLabel.Visibility = Visibility.Visible;
            LogoutLabel.Visibility = Visibility.Visible;

            ToggleIconScale.ScaleX = 1; // Default glyph |<
            SidebarToggleButton.ToolTip = "Collapse Navigation Sidebar (|&lt;)";
        }
    }

    // =========================================================================
    // 2. PRIMARY NAVIGATION (USERS & SETTINGS)
    // =========================================================================
    private void NavUsers_Click(object sender, RoutedEventArgs e)
    {
        SwitchToUsers();
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        SwitchToSettings();
    }

    public void SwitchToUsers()
    {
        SettingsViewPanel.Visibility = Visibility.Collapsed;
        UsersViewControl.Visibility = Visibility.Visible;

        // Visual Active Highlights
        Style activeStyle = (Style)FindResource("ActiveSidebarItemButtonStyle");
        Style defaultStyle = (Style)FindResource("SidebarItemButtonStyle");
        Brush purpleBrush = (Brush)FindResource("OdooPurpleBrush");
        Brush primaryTextBrush = (Brush)FindResource("PrimaryTextBrush");

        NavUsersBtn.Style = activeStyle;
        NavSettingsBtn.Style = defaultStyle;

        UsersLabel.Foreground = purpleBrush;
        SettingsLabel.Foreground = primaryTextBrush;
    }

    public void SwitchToSettings()
    {
        UsersViewControl.Visibility = Visibility.Collapsed;
        SettingsViewPanel.Visibility = Visibility.Visible;

        // Visual Active Highlights
        Style activeStyle = (Style)FindResource("ActiveSidebarItemButtonStyle");
        Style defaultStyle = (Style)FindResource("SidebarItemButtonStyle");
        Brush purpleBrush = (Brush)FindResource("OdooPurpleBrush");
        Brush primaryTextBrush = (Brush)FindResource("PrimaryTextBrush");

        NavSettingsBtn.Style = activeStyle;
        NavUsersBtn.Style = defaultStyle;

        SettingsLabel.Foreground = purpleBrush;
        UsersLabel.Foreground = primaryTextBrush;
    }

    private void LogoutItem_Click(object sender, RoutedEventArgs e)
    {
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    // =========================================================================
    // 3. CONFIGURATION TAB STRIP SWITCHING
    // =========================================================================
    private void TabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedTab) return;

        Style activeStyle = (Style)FindResource("OdooActiveTabButtonStyle");
        Style defaultStyle = (Style)FindResource("OdooTabButtonStyle");

        // Reset all 5 tabs to default
        TabGeneral.Style = defaultStyle;
        TabLocale.Style = defaultStyle;
        TabPaymentMethod.Style = defaultStyle;
        TabPeripherals.Style = defaultStyle;
        TabSystemOptions.Style = defaultStyle;

        // Apply active accent style (bold bottom accent line) to clicked tab
        clickedTab.Style = activeStyle;

        string tabTag = clickedTab.Tag as string ?? "General";
        if (tabTag == "General")
        {
            GeneralSettingsScrollViewer.Visibility = Visibility.Visible;
            OtherTabsEmptyContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            GeneralSettingsScrollViewer.Visibility = Visibility.Collapsed;
            OtherTabsEmptyContainer.Visibility = Visibility.Visible;
        }
    }

    // =========================================================================
    // 4. PASSWORD MASK / REVEAL TOGGLE
    // =========================================================================
    private void TogglePasswordView_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;

        if (_isPasswordRevealed)
        {
            CfgPasswordRevealed.Text = CfgPasswordMasked.Password;
            CfgPasswordMasked.Visibility = Visibility.Collapsed;
            CfgPasswordRevealed.Visibility = Visibility.Visible;
            EyePupil.Fill = (Brush)FindResource("OdooPurpleBrush");
        }
        else
        {
            CfgPasswordMasked.Password = CfgPasswordRevealed.Text;
            CfgPasswordRevealed.Visibility = Visibility.Collapsed;
            CfgPasswordMasked.Visibility = Visibility.Visible;
            EyePupil.Fill = (Brush)FindResource("SecondaryTextBrush");
        }
    }

    private string GetCurrentPassword() =>
        _isPasswordRevealed ? CfgPasswordRevealed.Text : CfgPasswordMasked.Password;

    private void SetCurrentPassword(string password)
    {
        CfgPasswordMasked.Password = password;
        CfgPasswordRevealed.Text = password;
    }

    // =========================================================================
    // 5. ODOO CONNECTION ACTIONS (SAVE & TEST)
    // =========================================================================
    private async void SaveConnectionBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = new OdooConfigRecord(
                CfgServerUrlTextBox.Text.Trim(),
                CfgDatabaseTextBox.Text.Trim(),
                CfgUsernameTextBox.Text.Trim(),
                GetCurrentPassword(),
                null,
                ConnectionStatusText.Text
            );

            await SqliteDatabaseService.Instance.SaveOdooConfigAsync(config);
            ShowToast("Odoo connection credentials saved to local SQLite.");

            // Immediately check live connectivity with new settings
            _ = OdooSyncService.Instance.CheckConnectivityAsync();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to save: {ex.Message}", isError: true);
        }
    }

    private Brush GetSafeBrush(string resourceKey, Color fallbackColor)
    {
        if (TryFindResource(resourceKey) is Brush brush)
            return brush;
        return new SolidColorBrush(fallbackColor);
    }

    private void SetSettingsSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            SyncUsersNowBtn.IsEnabled = false;
            SettingsSyncButtonText.Text = "Syncing Users...";
            SyncProgressBar.Visibility = Visibility.Visible;
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            SettingsSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            SettingsSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            SettingsSyncIconRotation.Angle = 0;
            SettingsSyncButtonText.Text = "Sync Users Now";
            SyncProgressBar.Visibility = Visibility.Collapsed;
            SyncUsersNowBtn.IsEnabled = true;
        }
    }

    private async void TestConnectionBtn_Click(object sender, RoutedEventArgs e)
    {
        var config = new OdooConfigRecord(
            CfgServerUrlTextBox.Text.Trim(),
            CfgDatabaseTextBox.Text.Trim(),
            CfgUsernameTextBox.Text.Trim(),
            GetCurrentPassword(),
            null,
            null
        );

        TestConnectionProgressBar.Visibility = Visibility.Visible;
        TestConnectionBtn.IsEnabled = false;
        ConnectionStatusDot.Fill = GetSafeBrush("MutedTextBrush", Color.FromRgb(0x9C, 0xA3, 0xAF));
        ConnectionStatusText.Text = "Verifying connection...";

        try
        {
            var result = await OdooSyncService.Instance.TestConnectionAsync(config);

            if (result.Success)
            {
                ConnectionStatusDot.Fill = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));
                ConnectionStatusText.Text = result.Message;
                ShowToast("Odoo connection verified successfully!");
            }
            else
            {
                ConnectionStatusDot.Fill = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
                ConnectionStatusText.Text = result.Message;
                ShowToast(result.Message, isError: true);
            }

            // Persist status and re-verify connectivity
            await SqliteDatabaseService.Instance.SaveOdooConfigAsync(config with { LastStatus = ConnectionStatusText.Text });
            _ = OdooSyncService.Instance.CheckConnectivityAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatusDot.Fill = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ConnectionStatusText.Text = $"Error: {ex.Message}";
            ShowToast(ex.Message, isError: true);
        }
        finally
        {
            TestConnectionProgressBar.Visibility = Visibility.Collapsed;
            TestConnectionBtn.IsEnabled = true;
        }
    }

    // =========================================================================
    // 6. USER SYNCHRONIZATION FROM ODOO TO SQLITE
    // =========================================================================
    private async void SyncUsersNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetSettingsSyncLoadingState(true);
            SyncDetailText.Text = "Verifying connection & synchronizing res.users...";
            SyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            // 1. Verify live Odoo Online reachability before initiating sync
            bool isOnline = await OdooSyncService.Instance.CheckConnectivityAsync();
            if (!isOnline)
            {
                SyncDetailText.Text = "Cannot synchronize: Odoo Online is unreachable (Offline Mode).";
                SyncDetailText.Foreground = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
                ShowToast("Cannot synchronize: Odoo Online is unreachable. Operating in Offline (Local Mode).", isWarning: true);
                return;
            }

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();

            // Fetch users from Odoo
            var odooUsers = await OdooSyncService.Instance.FetchOdooUsersAsync(config, allowOfflineSimulation: true);

            // Upsert users into local SQLite database (preserving local PINs and passwords)
            var (imported, updated) = await SqliteDatabaseService.Instance.UpsertOdooUsersAsync(odooUsers);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            await SqliteDatabaseService.Instance.UpdateSyncStatusAsync(DateTime.Now, $"Success ({imported + updated} users)");

            LastSyncedStatusText.Text = $"Last Synced: {syncTime}";
            SyncDetailText.Text = $"Sync Complete: {imported} new user(s) imported, {updated} user(s) updated.";
            SyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            // Refresh the Users Kanban view and POS login cards
            await UsersViewControl.ReloadUsersFromDatabaseAsync();

            ShowToast($"Sync Complete: {imported} imported, {updated} updated from Odoo.");
        }
        catch (Exception ex)
        {
            SyncDetailText.Text = $"Sync Failed: {ex.Message}";
            SyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetSettingsSyncLoadingState(false);
        }
    }

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        ShellToastMessageText.Text = message;
        if (isWarning)
        {
            ShellToastDot.Fill = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else if (isError)
        {
            ShellToastDot.Fill = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
        }
        else
        {
            ShellToastDot.Fill = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));
        }
        ShellToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
