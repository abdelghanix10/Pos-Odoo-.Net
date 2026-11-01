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
    public PosDashboardView DashboardControl => DashboardViewControl;
    public UserManagementView UsersControl => UsersViewControl;
    public PosCategoryManagementView CategoriesControl => CategoriesViewControl;
    public ProductManagementView ProductsControl => ProductsViewControl;
    public ComboChoiceManagementView CombosControl => ComboChoicesViewControl;
    public SessionManagementView SessionsControl => SessionsViewControl;
    public OrderManagementView OrdersControl => OrdersViewControl;
    public PaymentManagementView PaymentsControl => PaymentsViewControl;

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

        // Wire Dashboard navigation
        DashboardViewControl.VM.OpenRegisterRequested += config =>
        {
            SwitchToSessions();
        };
        DashboardViewControl.VM.ViewOrdersRequested += shopName =>
        {
            SwitchToOrders(shopName);
        };
        DashboardViewControl.VM.ViewSessionsRequested += shopName =>
        {
            SwitchToSessions();
        };
        DashboardViewControl.VM.ViewPaymentsRequested += shopName =>
        {
            SwitchToPayments(shopName);
        };

        // Wire cross-navigation: Sessions smart buttons to Orders and Payments
        SessionsViewControl.VM.NavigateToOrdersRequested += sessionName =>
        {
            SwitchToOrders(sessionName);
        };
        SessionsViewControl.VM.NavigateToPaymentsRequested += sessionName =>
        {
            SwitchToPayments(sessionName);
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

            var lastCatSync = await SqliteDatabaseService.Instance.GetLastCategorySyncStatusAsync();
            if (!string.IsNullOrEmpty(lastCatSync))
            {
                LastCategoriesSyncedStatusText.Text = $"Last Synced: {lastCatSync}";
            }

            var lastAttrSync = await SqliteDatabaseService.Instance.GetLastAttributeSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastAttrSync))
            {
                LastAttributesSyncedStatusText.Text = $"Last Synced: {lastAttrSync}";
            }

            var lastTagSync = await SqliteDatabaseService.Instance.GetLastTagSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastTagSync))
            {
                LastTagsSyncedStatusText.Text = $"Last Synced: {lastTagSync}";
            }

            var lastProdSync = await SqliteDatabaseService.Instance.GetLastProductSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastProdSync))
            {
                LastProductsSyncedStatusText.Text = $"Last Synced: {lastProdSync}";
            }

            var lastComboSync = await SqliteDatabaseService.Instance.GetLastComboSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastComboSync))
            {
                LastCombosSyncedStatusText.Text = $"Last Synced: {lastComboSync}";
            }

            var lastSessionSync = await SqliteDatabaseService.Instance.GetLastSessionSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastSessionSync))
            {
                LastSessionsSyncedStatusText.Text = $"Last Synced: {lastSessionSync}";
            }

            var lastOrderSync = await SqliteDatabaseService.Instance.GetLastOrderSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastOrderSync))
            {
                LastOrdersSyncedStatusText.Text = $"Last Synced: {lastOrderSync}";
            }

            var lastPaymentSync = await SqliteDatabaseService.Instance.GetLastPaymentSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastPaymentSync))
            {
                LastPaymentsSyncedStatusText.Text = $"Last Synced: {lastPaymentSync}";
            }

            var lastConfigSync = await SqliteDatabaseService.Instance.GetLastPosConfigSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastConfigSync))
            {
                LastPosConfigsSyncedStatusText.Text = $"Last Synced: {lastConfigSync}";
            }

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
            SessionsLabel.Visibility = Visibility.Collapsed;
            OrdersLabel.Visibility = Visibility.Collapsed;
            PaymentsLabel.Visibility = Visibility.Collapsed;
            UsersLabel.Visibility = Visibility.Collapsed;
            ProductsLabel.Visibility = Visibility.Collapsed;
            CombosLabel.Visibility = Visibility.Collapsed;
            CategoriesLabel.Visibility = Visibility.Collapsed;
            AttributesLabel.Visibility = Visibility.Collapsed;
            ProductTagsLabel.Visibility = Visibility.Collapsed;
            SettingsLabel.Visibility = Visibility.Collapsed;
            LogoutLabel.Visibility = Visibility.Collapsed;

            ToggleIconScale.ScaleX = -1; // Flip glyph horizontally to >|
            SidebarToggleButton.ToolTip = "Expand Navigation Sidebar (>|)";
        }
        else
        {
            // Expand navigation panel to show icons & labels
            SidebarMenuColumn.Width = new GridLength(_expandedSidebarWidth);
            SessionsLabel.Visibility = Visibility.Visible;
            OrdersLabel.Visibility = Visibility.Visible;
            PaymentsLabel.Visibility = Visibility.Visible;
            UsersLabel.Visibility = Visibility.Visible;
            ProductsLabel.Visibility = Visibility.Visible;
            CombosLabel.Visibility = Visibility.Visible;
            CategoriesLabel.Visibility = Visibility.Visible;
            AttributesLabel.Visibility = Visibility.Visible;
            ProductTagsLabel.Visibility = Visibility.Visible;
            SettingsLabel.Visibility = Visibility.Visible;
            LogoutLabel.Visibility = Visibility.Visible;

            ToggleIconScale.ScaleX = 1; // Default glyph |<
            SidebarToggleButton.ToolTip = "Collapse Navigation Sidebar (|&lt;)";
        }
    }

    // =========================================================================
    // 2. PRIMARY NAVIGATION (DASHBOARD, SESSIONS, ORDERS, PAYMENTS, USERS, PRODUCTS, COMBOS, CATEGORIES, ATTRIBUTES, TAGS, SETTINGS)
    // =========================================================================
    private void NavDashboard_Click(object sender, RoutedEventArgs e) => SwitchToDashboard();
    private void NavSessions_Click(object sender, RoutedEventArgs e) => SwitchToSessions();
    private void NavOrders_Click(object sender, RoutedEventArgs e) => SwitchToOrders();
    private void NavPayments_Click(object sender, RoutedEventArgs e) => SwitchToPayments();
    private void NavUsers_Click(object sender, RoutedEventArgs e) => SwitchToUsers();
    private void NavProducts_Click(object sender, RoutedEventArgs e) => SwitchToProducts();
    private void NavCombos_Click(object sender, RoutedEventArgs e) => SwitchToCombos();
    private void NavCategories_Click(object sender, RoutedEventArgs e) => SwitchToCategories();
    private void NavAttributes_Click(object sender, RoutedEventArgs e) => SwitchToAttributes();
    private void NavProductTags_Click(object sender, RoutedEventArgs e) => SwitchToTags();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => SwitchToSettings();

    private void HideAllMainViews()
    {
        DashboardViewControl.Visibility = Visibility.Collapsed;
        SettingsViewPanel.Visibility = Visibility.Collapsed;
        SessionsViewControl.Visibility = Visibility.Collapsed;
        OrdersViewControl.Visibility = Visibility.Collapsed;
        PaymentsViewControl.Visibility = Visibility.Collapsed;
        UsersViewControl.Visibility = Visibility.Collapsed;
        ProductsViewControl.Visibility = Visibility.Collapsed;
        ComboChoicesViewControl.Visibility = Visibility.Collapsed;
        CategoriesViewControl.Visibility = Visibility.Collapsed;
        AttributesViewControl.Visibility = Visibility.Collapsed;
        TagsViewControl.Visibility = Visibility.Collapsed;
    }

    private void SetActiveSidebarButton(Button activeButton, TextBlock activeLabel)
    {
        Style activeStyle = (Style)FindResource("ActiveSidebarItemButtonStyle");
        Style defaultStyle = (Style)FindResource("SidebarItemButtonStyle");
        Brush purpleBrush = (Brush)FindResource("OdooPurpleBrush");
        Brush primaryTextBrush = (Brush)FindResource("PrimaryTextBrush");

        NavDashboardBtn.Style = NavDashboardBtn == activeButton ? activeStyle : defaultStyle;
        NavSessionsBtn.Style = NavSessionsBtn == activeButton ? activeStyle : defaultStyle;
        NavOrdersBtn.Style = NavOrdersBtn == activeButton ? activeStyle : defaultStyle;
        NavPaymentsBtn.Style = NavPaymentsBtn == activeButton ? activeStyle : defaultStyle;
        NavUsersBtn.Style = NavUsersBtn == activeButton ? activeStyle : defaultStyle;
        NavProductsBtn.Style = NavProductsBtn == activeButton ? activeStyle : defaultStyle;
        NavCombosBtn.Style = NavCombosBtn == activeButton ? activeStyle : defaultStyle;
        NavCategoriesBtn.Style = NavCategoriesBtn == activeButton ? activeStyle : defaultStyle;
        NavAttributesBtn.Style = NavAttributesBtn == activeButton ? activeStyle : defaultStyle;
        NavProductTagsBtn.Style = NavProductTagsBtn == activeButton ? activeStyle : defaultStyle;
        NavSettingsBtn.Style = NavSettingsBtn == activeButton ? activeStyle : defaultStyle;

        DashboardLabel.Foreground = DashboardLabel == activeLabel ? purpleBrush : primaryTextBrush;
        SessionsLabel.Foreground = SessionsLabel == activeLabel ? purpleBrush : primaryTextBrush;
        OrdersLabel.Foreground = OrdersLabel == activeLabel ? purpleBrush : primaryTextBrush;
        PaymentsLabel.Foreground = PaymentsLabel == activeLabel ? purpleBrush : primaryTextBrush;
        UsersLabel.Foreground = UsersLabel == activeLabel ? purpleBrush : primaryTextBrush;
        ProductsLabel.Foreground = ProductsLabel == activeLabel ? purpleBrush : primaryTextBrush;
        CombosLabel.Foreground = CombosLabel == activeLabel ? purpleBrush : primaryTextBrush;
        CategoriesLabel.Foreground = CategoriesLabel == activeLabel ? purpleBrush : primaryTextBrush;
        AttributesLabel.Foreground = AttributesLabel == activeLabel ? purpleBrush : primaryTextBrush;
        ProductTagsLabel.Foreground = ProductTagsLabel == activeLabel ? purpleBrush : primaryTextBrush;
        SettingsLabel.Foreground = SettingsLabel == activeLabel ? purpleBrush : primaryTextBrush;
    }

    public void SwitchToDashboard()
    {
        HideAllMainViews();
        DashboardViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavDashboardBtn, DashboardLabel);
        _ = DashboardViewControl.ReloadConfigsFromDatabaseAsync();
    }

    public void SwitchToSessions()
    {
        HideAllMainViews();
        SessionsViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavSessionsBtn, SessionsLabel);
        _ = SessionsViewControl.ReloadSessionsFromDatabaseAsync();
    }

    public void SwitchToOrders(string? sessionFilter = null)
    {
        HideAllMainViews();
        OrdersViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavOrdersBtn, OrdersLabel);
        _ = OrdersViewControl.ReloadOrdersFromDatabaseAsync(sessionFilter);
    }

    public void SwitchToPayments(string? sessionFilter = null)
    {
        HideAllMainViews();
        PaymentsViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavPaymentsBtn, PaymentsLabel);
        _ = PaymentsViewControl.ReloadPaymentsFromDatabaseAsync(sessionFilter);
    }

    public void SwitchToUsers()
    {
        HideAllMainViews();
        UsersViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavUsersBtn, UsersLabel);
    }

    public void SwitchToProducts()
    {
        HideAllMainViews();
        ProductsViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavProductsBtn, ProductsLabel);
        _ = ProductsViewControl.ReloadProductsFromDatabaseAsync();
    }

    public void SwitchToCombos()
    {
        HideAllMainViews();
        ComboChoicesViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavCombosBtn, CombosLabel);
        _ = ComboChoicesViewControl.ReloadCombosFromDatabaseAsync();
    }

    public void SwitchToCategories()
    {
        HideAllMainViews();
        CategoriesViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavCategoriesBtn, CategoriesLabel);
        _ = CategoriesViewControl.ReloadCategoriesFromDatabaseAsync();
    }

    public void SwitchToAttributes()
    {
        HideAllMainViews();
        AttributesViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavAttributesBtn, AttributesLabel);
        _ = AttributesViewControl.ReloadAttributesFromDatabaseAsync();
    }

    public void SwitchToTags()
    {
        HideAllMainViews();
        TagsViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavProductTagsBtn, ProductTagsLabel);
        _ = TagsViewControl.ReloadTagsFromDatabaseAsync();
    }

    public void SwitchToSettings()
    {
        HideAllMainViews();
        SettingsViewPanel.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavSettingsBtn, SettingsLabel);
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
            SyncDetailText.Text = "Connecting to Odoo & synchronizing res.users...";
            SyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            string url = CfgServerUrlTextBox.Text.Trim();
            string db = CfgDatabaseTextBox.Text.Trim();
            string user = CfgUsernameTextBox.Text.Trim();
            string pwd = GetCurrentPassword();

            OdooConfigRecord config;
            if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(db) && !string.IsNullOrWhiteSpace(user))
            {
                config = new OdooConfigRecord(url, db, user, pwd, null, "Connected");
                await SqliteDatabaseService.Instance.SaveOdooConfigAsync(config);
            }
            else
            {
                config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            }

            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                SyncDetailText.Text = "Cannot synchronize: Odoo Server URL is not configured.";
                SyncDetailText.Foreground = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
                ShowToast("Please enter and save Odoo connection settings first.", isWarning: true);
                return;
            }

            // Fetch live users directly from Odoo (no simulation; connects with real credentials)
            var odooUsers = await OdooSyncService.Instance.FetchOdooUsersAsync(config, allowOfflineSimulation: false);

            // Reconcile users in local SQLite: upsert users present in Odoo, DELETE users NOT in Odoo
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooUsersAsync(odooUsers);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated.";

            await SqliteDatabaseService.Instance.UpdateSyncStatusAsync(DateTime.Now, statusMsg);

            LastSyncedStatusText.Text = $"Last Synced: {syncTime}";
            SyncDetailText.Text = statusMsg;
            SyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            // Refresh the Users Kanban view and POS login cards
            await UsersViewControl.ReloadUsersFromDatabaseAsync();
            UsersViewControl.NotifyUsersUpdated();

            ShowToast(statusMsg);
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

    private async void SyncCategoriesNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetCategoriesSyncLoadingState(true);
            CategoriesSyncDetailText.Text = "Connecting to Odoo & synchronizing pos.category...";
            CategoriesSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            string url = CfgServerUrlTextBox.Text.Trim();
            string db = CfgDatabaseTextBox.Text.Trim();
            string user = CfgUsernameTextBox.Text.Trim();
            string pwd = GetCurrentPassword();

            OdooConfigRecord config;
            if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(db) && !string.IsNullOrWhiteSpace(user))
            {
                config = new OdooConfigRecord(url, db, user, pwd, null, "Connected");
                await SqliteDatabaseService.Instance.SaveOdooConfigAsync(config);
            }
            else
            {
                config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            }

            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                CategoriesSyncDetailText.Text = "Cannot synchronize: Odoo Server URL is not configured.";
                CategoriesSyncDetailText.Foreground = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
                ShowToast("Please enter and save Odoo connection settings first.", isWarning: true);
                return;
            }

            var odooCategories = await OdooSyncService.Instance.FetchOdooCategoriesAsync(config, allowOfflineSimulation: false);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooCategoriesAsync(odooCategories);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated.";

            await SqliteDatabaseService.Instance.UpdateCategorySyncStatusAsync(DateTime.Now, statusMsg);

            LastCategoriesSyncedStatusText.Text = $"Last Synced: {syncTime}";
            CategoriesSyncDetailText.Text = statusMsg;
            CategoriesSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await CategoriesViewControl.ReloadCategoriesFromDatabaseAsync();

            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            CategoriesSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            CategoriesSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetCategoriesSyncLoadingState(false);
        }
    }

    private void SetCategoriesSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            SyncCategoriesNowBtn.IsEnabled = false;
            SettingsCatSyncButtonText.Text = "Syncing Categories...";
            CategoriesSyncProgressBar.Visibility = Visibility.Visible;
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            SettingsCatSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            SettingsCatSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            SettingsCatSyncIconRotation.Angle = 0;
            SettingsCatSyncButtonText.Text = "Sync Categories Now";
            CategoriesSyncProgressBar.Visibility = Visibility.Collapsed;
            SyncCategoriesNowBtn.IsEnabled = true;
        }
    }

    // =========================================================================
    // 6. ATTRIBUTES & PRODUCT TAGS SYNCHRONIZATION FROM SETTINGS
    // =========================================================================
    private async void SyncAttributesNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetAttributesSyncLoadingState(true);

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Cannot synchronize: Odoo Server URL is not configured.", isWarning: true);
                return;
            }

            var (odooAttrs, odooVals) = await OdooSyncService.Instance.FetchOdooAttributesAsync(config, allowOfflineSimulation: false);
            var (aImp, aUpd, aDel, vImp, vUpd, vDel) = await SqliteDatabaseService.Instance.UpsertOdooAttributesAsync(odooAttrs, odooVals);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            string statusMsg = $"Sync Complete: {aImp + aUpd} attributes, {vImp + vUpd} values synced.";
            if (aDel > 0 || vDel > 0)
            {
                statusMsg += $" ({aDel} attributes, {vDel} values removed)";
            }

            await SqliteDatabaseService.Instance.UpdateAttributeSyncStatusAsync(DateTime.Now, statusMsg);

            LastAttributesSyncedStatusText.Text = $"Last Synced: {syncTime}";
            AttributesSyncDetailText.Text = statusMsg;
            AttributesSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await AttributesViewControl.ReloadAttributesFromDatabaseAsync();
            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            AttributesSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            AttributesSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetAttributesSyncLoadingState(false);
        }
    }

    private void SetAttributesSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            SyncAttributesNowBtn.IsEnabled = false;
            SettingsAttrSyncButtonText.Text = "Syncing Attributes...";
            AttributesSyncProgressBar.Visibility = Visibility.Visible;
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            SettingsAttrSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            SettingsAttrSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            SettingsAttrSyncIconRotation.Angle = 0;
            SettingsAttrSyncButtonText.Text = "Sync Attributes Now";
            AttributesSyncProgressBar.Visibility = Visibility.Collapsed;
            SyncAttributesNowBtn.IsEnabled = true;
        }
    }

    private async void SyncTagsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetTagsSyncLoadingState(true);

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Cannot synchronize: Odoo Server URL is not configured.", isWarning: true);
                return;
            }

            var odooTags = await OdooSyncService.Instance.FetchOdooTagsAsync(config, allowOfflineSimulation: false);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooTagsAsync(odooTags);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated ({odooTags.Count} tags).";

            await SqliteDatabaseService.Instance.UpdateTagSyncStatusAsync(DateTime.Now, statusMsg);

            LastTagsSyncedStatusText.Text = $"Last Synced: {syncTime}";
            TagsSyncDetailText.Text = statusMsg;
            TagsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await TagsViewControl.ReloadTagsFromDatabaseAsync();
            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            TagsSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            TagsSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetTagsSyncLoadingState(false);
        }
    }

    private void SetTagsSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            SyncTagsNowBtn.IsEnabled = false;
            SettingsTagSyncButtonText.Text = "Syncing Tags...";
            TagsSyncProgressBar.Visibility = Visibility.Visible;
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            SettingsTagSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            SettingsTagSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            SettingsTagSyncIconRotation.Angle = 0;
            SettingsTagSyncButtonText.Text = "Sync Tags Now";
            TagsSyncProgressBar.Visibility = Visibility.Collapsed;
            SyncTagsNowBtn.IsEnabled = true;
        }
    }

    // =========================================================================
    // 7. PRODUCTS & COMBO CHOICES SYNCHRONIZATION FROM SETTINGS
    // =========================================================================
    private async void SyncProductsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetProductsSyncLoadingState(true);

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Cannot synchronize: Odoo Server URL is not configured.", isWarning: true);
                return;
            }

            var odooProducts = await OdooSyncService.Instance.FetchOdooProductsAsync(config, allowOfflineSimulation: false);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooProductsAsync(odooProducts);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated ({odooProducts.Count} products).";

            await SqliteDatabaseService.Instance.UpdateProductSyncStatusAsync(DateTime.Now, statusMsg);

            LastProductsSyncedStatusText.Text = $"Last Synced: {syncTime}";
            ProductsSyncDetailText.Text = statusMsg;
            ProductsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await ProductsViewControl.ReloadProductsFromDatabaseAsync();
            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            ProductsSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            ProductsSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetProductsSyncLoadingState(false);
        }
    }

    private void SetProductsSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            SyncProductsNowBtn.IsEnabled = false;
            SettingsProdSyncButtonText.Text = "Syncing Products...";
            ProductsSyncProgressBar.Visibility = Visibility.Visible;
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            SettingsProdSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            SettingsProdSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            SettingsProdSyncIconRotation.Angle = 0;
            SettingsProdSyncButtonText.Text = "Sync Products Now";
            ProductsSyncProgressBar.Visibility = Visibility.Collapsed;
            SyncProductsNowBtn.IsEnabled = true;
        }
    }

    private async void SyncCombosNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetCombosSyncLoadingState(true);

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Cannot synchronize: Odoo Server URL is not configured.", isWarning: true);
                return;
            }

            var (combos, lines) = await OdooSyncService.Instance.FetchOdooComboChoicesAsync(config, allowOfflineSimulation: false);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooCombosAsync(combos, lines);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated ({combos.Count} combos, {lines.Count} lines).";

            await SqliteDatabaseService.Instance.UpdateComboSyncStatusAsync(DateTime.Now, statusMsg);

            LastCombosSyncedStatusText.Text = $"Last Synced: {syncTime}";
            CombosSyncDetailText.Text = statusMsg;
            CombosSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await ComboChoicesViewControl.ReloadCombosFromDatabaseAsync();
            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            CombosSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            CombosSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetCombosSyncLoadingState(false);
        }
    }

    private void SetCombosSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            SyncCombosNowBtn.IsEnabled = false;
            SettingsComboSyncButtonText.Text = "Syncing Combos...";
            CombosSyncProgressBar.Visibility = Visibility.Visible;
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            SettingsComboSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            SettingsComboSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            SettingsComboSyncIconRotation.Angle = 0;
            SettingsComboSyncButtonText.Text = "Sync Combos Now";
            CombosSyncProgressBar.Visibility = Visibility.Collapsed;
            SyncCombosNowBtn.IsEnabled = true;
        }
    }

    private async void SyncSessionsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetSessionsSyncLoadingState(true);

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Cannot synchronize: Odoo Server URL is not configured.", isWarning: true);
                return;
            }

            var sessionsDto = await OdooSyncService.Instance.FetchOdooSessionsAsync(allowOfflineSimulation: true);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooSessionsAsync(sessionsDto);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated ({sessionsDto.Count} sessions).";

            await SqliteDatabaseService.Instance.UpdateSessionSyncStatusAsync(DateTime.Now, statusMsg);

            LastSessionsSyncedStatusText.Text = $"Last Synced: {syncTime}";
            SessionsSyncDetailText.Text = statusMsg;
            SessionsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await SessionsViewControl.ReloadSessionsFromDatabaseAsync();
            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            SessionsSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            SessionsSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetSessionsSyncLoadingState(false);
        }
    }

    private void SetSessionsSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            SyncSessionsNowBtn.IsEnabled = false;
            SettingsSessionSyncButtonText.Text = "Syncing Sessions...";
            SessionsSyncProgressBar.Visibility = Visibility.Visible;
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            SettingsSessionSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            SettingsSessionSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            SettingsSessionSyncIconRotation.Angle = 0;
            SettingsSessionSyncButtonText.Text = "Sync Sessions Now";
            SessionsSyncProgressBar.Visibility = Visibility.Collapsed;
            SyncSessionsNowBtn.IsEnabled = true;
        }
    }

    private async void SyncOrdersNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetOrdersSyncLoadingState(true);

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Cannot synchronize: Odoo Server URL is not configured.", isWarning: true);
                return;
            }

            var (orders, lines, payments) = await OdooSyncService.Instance.FetchOdooOrdersAsync(allowOfflineSimulation: true);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooOrdersAsync(orders, lines, payments);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated ({orders.Count} orders, {lines.Count} lines).";

            await SqliteDatabaseService.Instance.UpdateOrderSyncStatusAsync(DateTime.Now, statusMsg);

            LastOrdersSyncedStatusText.Text = $"Last Synced: {syncTime}";
            OrdersSyncDetailText.Text = statusMsg;
            OrdersSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await OrdersViewControl.ReloadOrdersFromDatabaseAsync();
            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            OrdersSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            OrdersSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetOrdersSyncLoadingState(false);
        }
    }

    private void SetOrdersSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            SyncOrdersNowBtn.IsEnabled = false;
            SettingsOrderSyncButtonText.Text = "Syncing Orders...";
            OrdersSyncProgressBar.Visibility = Visibility.Visible;
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            SettingsOrderSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            SettingsOrderSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            SettingsOrderSyncIconRotation.Angle = 0;
            SettingsOrderSyncButtonText.Text = "Sync Orders Now";
            OrdersSyncProgressBar.Visibility = Visibility.Collapsed;
            SyncOrdersNowBtn.IsEnabled = true;
        }
    }

    private async void SyncPaymentsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetPaymentsSyncLoadingState(true);

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Cannot synchronize: Odoo Server URL is not configured.", isWarning: true);
                return;
            }

            var paymentsDto = await OdooSyncService.Instance.FetchOdooPaymentsAsync(allowOfflineSimulation: true);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooPaymentsAsync(paymentsDto);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated ({paymentsDto.Count} payments).";

            await SqliteDatabaseService.Instance.UpdatePaymentSyncStatusAsync(DateTime.Now, statusMsg);

            LastPaymentsSyncedStatusText.Text = $"Last Synced: {syncTime}";
            PaymentsSyncDetailText.Text = statusMsg;
            PaymentsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await PaymentsViewControl.ReloadPaymentsFromDatabaseAsync();
            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            PaymentsSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            PaymentsSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetPaymentsSyncLoadingState(false);
        }
    }

    private void SetPaymentsSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            SyncPaymentsNowBtn.IsEnabled = false;
            SettingsPaymentSyncButtonText.Text = "Syncing Payments...";
            PaymentsSyncProgressBar.Visibility = Visibility.Visible;
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            SettingsPaymentSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            SettingsPaymentSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            SettingsPaymentSyncIconRotation.Angle = 0;
            SettingsPaymentSyncButtonText.Text = "Sync Payments Now";
            PaymentsSyncProgressBar.Visibility = Visibility.Collapsed;
            SyncPaymentsNowBtn.IsEnabled = true;
        }
    }

    private async void SyncPosConfigsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetPosConfigsSyncLoadingState(true);
            PosConfigsSyncDetailText.Text = "Connecting to Odoo & synchronizing pos.config...";
            PosConfigsSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var odooConfigs = await OdooSyncService.Instance.FetchOdooPosConfigsAsync(allowOfflineSimulation: true);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooPosConfigsAsync(odooConfigs);

            string syncTime = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated.";

            await SqliteDatabaseService.Instance.UpdatePosConfigSyncStatusAsync(DateTime.Now, statusMsg);

            LastPosConfigsSyncedStatusText.Text = $"Last Synced: {syncTime}";
            PosConfigsSyncDetailText.Text = statusMsg;
            PosConfigsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await DashboardViewControl.ReloadConfigsFromDatabaseAsync();
            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            PosConfigsSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            PosConfigsSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetPosConfigsSyncLoadingState(false);
        }
    }

    private void SetPosConfigsSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            SyncPosConfigsNowBtn.IsEnabled = false;
            SettingsPosConfigSyncButtonText.Text = "Syncing Shops...";
            PosConfigsSyncProgressBar.Visibility = Visibility.Visible;
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            SettingsPosConfigSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            SettingsPosConfigSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            SettingsPosConfigSyncIconRotation.Angle = 0;
            SettingsPosConfigSyncButtonText.Text = "Sync POS Shops Now";
            PosConfigsSyncProgressBar.Visibility = Visibility.Collapsed;
            SyncPosConfigsNowBtn.IsEnabled = true;
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
