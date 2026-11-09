using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OdooPosLogin;

public partial class MainShellView : UserControl
{
    private bool _isSidebarCollapsed = false;
    private readonly double _expandedSidebarWidth = 212.0;
    private readonly double _collapsedSidebarWidth = 64.0;

    private bool _isPasswordRevealed = false;
    private readonly DispatcherTimer _toastTimer;

    // Events to communicate with parent Window
    public event EventHandler? LogoutRequested;

    public string CurrentUser { get; private set; } = "Administrator";
    public PosDashboardView DashboardControl => DashboardViewControl;
    public CompanyManagementView CompaniesControl => CompaniesViewControl;
    public PaymentMethodManagementView PaymentMethodsControl => PaymentMethodsViewControl;
    public UserManagementView UsersControl => UsersViewControl;
    public PosCategoryManagementView CategoriesControl => CategoriesViewControl;
    public ProductAttributeManagementView AttributesControl => AttributesViewControl;
    public ProductTagManagementView TagsControl => TagsViewControl;
    public ProductManagementView ProductsControl => ProductsViewControl;
    public ComboChoiceManagementView CombosControl => ComboChoicesViewControl;
    public SessionManagementView SessionsControl => SessionsViewControl;
    public OrderManagementView OrdersControl => OrdersViewControl;
    public PaymentManagementView PaymentsControl => PaymentsViewControl;

    public MainShellView()
    {
        InitializeComponent();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ShellToastBanner.Visibility = Visibility.Collapsed;
        };

        UsersViewControl.RequestCloseToPos += (_, _) =>
        {
            SwitchToSettings();
        };

        // Wire Dashboard navigation
        DashboardViewControl.VM.OpenRegisterRequested += async config =>
        {
            bool showOpeningControl = config.IsOpeningControl || config.IsClosed;
            await RegisterViewControl.VM.InitializeRegisterAsync(config, showOpeningControl);
            RegisterViewControl.Visibility = Visibility.Visible;
        };
        RegisterViewControl.VM.ReturnToDashboardRequested += async () =>
        {
            RegisterViewControl.Visibility = Visibility.Collapsed;
            await DashboardViewControl.ReloadConfigsFromDatabaseAsync();
            await SessionsViewControl.ReloadSessionsFromDatabaseAsync(resetDetailView: false);
            await OrdersViewControl.ReloadOrdersFromDatabaseAsync(resetDetailView: false);
            await PaymentsViewControl.ReloadPaymentsFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();
        };
        DashboardViewControl.VM.ViewOrdersRequested += shopName =>
        {
            SwitchToOrders(shopName);
        };
        DashboardViewControl.VM.ViewSessionsRequested += shopName =>
        {
            SwitchToSessions(shopName);
        };
        DashboardViewControl.VM.ViewPaymentsRequested += shopName =>
        {
            SwitchToPayments(shopName);
        };
        DashboardViewControl.VM.ConfigureShopRequested += config =>
        {
            SwitchToPosSettings(config?.OdooId, openReceiptDesigns: false);
        };

        // Wire POS Settings navigation links
        PosSettingsViewControl.VM.NavigateToPaymentMethodsRequested += () =>
        {
            SwitchToPaymentMethods();
        };
        PosSettingsViewControl.VM.NavigateToCategoriesRequested += () =>
        {
            SwitchToCategories();
        };
        PosSettingsViewControl.VM.NavigateToAttributesRequested += () =>
        {
            SwitchToAttributes();
        };
        PosSettingsViewControl.VM.NavigateToReceiptDesignsRequested += () =>
        {
            SwitchToReceiptDesigns();
        };
        PosSettingsViewControl.VM.SettingsSaved += () =>
        {
            _ = DashboardViewControl.ReloadConfigsFromDatabaseAsync();
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

        // Refresh counts & timestamps when child views trigger their own sync
        CompaniesViewControl.CompaniesUpdated += (_, _) => _ = RefreshConfigAndCountsAsync();
        PaymentMethodsViewControl.PaymentMethodsUpdated += (_, _) => _ = RefreshConfigAndCountsAsync();
        UsersViewControl.UsersUpdated += () => _ = RefreshConfigAndCountsAsync();
        CategoriesViewControl.CategoriesUpdated += (_, _) => _ = RefreshConfigAndCountsAsync();
        AttributesViewControl.AttributesUpdated += (_, _) => _ = RefreshConfigAndCountsAsync();
        TagsViewControl.TagsUpdated += (_, _) => _ = RefreshConfigAndCountsAsync();
        ProductsViewControl.ProductsUpdated += (_, _) => _ = RefreshConfigAndCountsAsync();
        ComboChoicesViewControl.CombosUpdated += (_, _) => _ = RefreshConfigAndCountsAsync();
        DashboardViewControl.VM.ConfigsUpdated += (_, _) => _ = RefreshDatabaseCountsAsync();
        SessionsViewControl.VM.SessionsUpdated += (_, _) => _ = RefreshConfigAndCountsAsync();
        OrdersViewControl.VM.OrdersUpdated += (_, _) => _ = RefreshConfigAndCountsAsync();
        PaymentsViewControl.VM.PaymentsUpdated += (_, _) => _ = RefreshConfigAndCountsAsync();

        // Live queue counter updates
        OdooSyncService.Instance.QueueUpdated += () =>
        {
            Dispatcher.InvokeAsync(RefreshDatabaseCountsAsync);
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

        _ = LoadSavedConfigAsync();
    }

    private async void OpenPendingSyncQueueWindow_Click(object sender, RoutedEventArgs e)
    {
        var win = new PendingSyncWindow();
        if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            win.Owner = Application.Current.MainWindow;
        }
        win.ShowDialog();

        await DashboardViewControl.ReloadConfigsFromDatabaseAsync();
        await SessionsViewControl.ReloadSessionsFromDatabaseAsync(resetDetailView: false);
        await OrdersViewControl.ReloadOrdersFromDatabaseAsync(resetDetailView: false);
        await PaymentsViewControl.ReloadPaymentsFromDatabaseAsync(resetDetailView: false);
        await RefreshDatabaseCountsAsync();
    }

    public async Task RefreshConfigAndCountsAsync()
    {
        await LoadSavedConfigAsync();
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

            var lastCompSync = await SqliteDatabaseService.Instance.GetLastCompanySyncStatusAsync();
            LastCompaniesSyncedStatusText.Text = string.IsNullOrEmpty(lastCompSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastCompSync}";

            var lastPayMethodSync = await SqliteDatabaseService.Instance.GetLastPaymentMethodSyncStatusAsync();
            LastPaymentMethodsSyncedStatusText.Text = string.IsNullOrEmpty(lastPayMethodSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastPayMethodSync}";

            var lastCatSync = await SqliteDatabaseService.Instance.GetLastCategorySyncStatusAsync();
            LastCategoriesSyncedStatusText.Text = string.IsNullOrEmpty(lastCatSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastCatSync}";

            var lastAttrSync = await SqliteDatabaseService.Instance.GetLastAttributeSyncStatusAsync();
            LastAttributesSyncedStatusText.Text = string.IsNullOrEmpty(lastAttrSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastAttrSync}";

            var lastTagSync = await SqliteDatabaseService.Instance.GetLastTagSyncStatusAsync();
            LastTagsSyncedStatusText.Text = string.IsNullOrEmpty(lastTagSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastTagSync}";

            var lastProdSync = await SqliteDatabaseService.Instance.GetLastProductSyncStatusAsync();
            LastProductsSyncedStatusText.Text = string.IsNullOrEmpty(lastProdSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastProdSync}";

            var lastComboSync = await SqliteDatabaseService.Instance.GetLastComboSyncStatusAsync();
            LastCombosSyncedStatusText.Text = string.IsNullOrEmpty(lastComboSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastComboSync}";

            var lastSessionSync = await SqliteDatabaseService.Instance.GetLastSessionSyncStatusAsync();
            LastSessionsSyncedStatusText.Text = string.IsNullOrEmpty(lastSessionSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastSessionSync}";

            var lastOrderSync = await SqliteDatabaseService.Instance.GetLastOrderSyncStatusAsync();
            LastOrdersSyncedStatusText.Text = string.IsNullOrEmpty(lastOrderSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastOrderSync}";

            var lastPaymentSync = await SqliteDatabaseService.Instance.GetLastPaymentSyncStatusAsync();
            LastPaymentsSyncedStatusText.Text = string.IsNullOrEmpty(lastPaymentSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastPaymentSync}";

            var lastConfigSync = await SqliteDatabaseService.Instance.GetLastPosConfigSyncStatusAsync();
            LastPosConfigsSyncedStatusText.Text = string.IsNullOrEmpty(lastConfigSync)
                ? "Last Synced: Never"
                : $"Last Synced: {lastConfigSync}";

            if (!string.IsNullOrEmpty(config.LastStatus))
            {
                ConnectionStatusText.Text = config.LastStatus;
                if (config.LastStatus.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) ||
                    config.LastStatus.Equals("Odoo Online", StringComparison.OrdinalIgnoreCase))
                {
                    ConnectionStatusDot.Fill = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));
                }
            }

            await RefreshDatabaseCountsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading Odoo config: {ex.Message}");
        }
    }

    public async Task RefreshDatabaseCountsAsync()
    {
        try
        {
            var counts = await SqliteDatabaseService.Instance.GetDatabaseCountsAsync();

            static string FormatCount(int count) => count == 1 ? "1 record" : $"{count} records";

            CompaniesCountBadgeText.Text = FormatCount(counts.CompaniesCount);
            PaymentMethodsCountBadgeText.Text = FormatCount(counts.PaymentMethodsCount);
            PosConfigsCountBadgeText.Text = FormatCount(counts.PosConfigsCount);
            SessionsCountBadgeText.Text = FormatCount(counts.SessionsCount);
            OrdersCountBadgeText.Text = FormatCount(counts.OrdersCount);
            PaymentsCountBadgeText.Text = FormatCount(counts.PaymentsCount);
            ProductsCountBadgeText.Text = FormatCount(counts.ProductsCount);
            CombosCountBadgeText.Text = FormatCount(counts.CombosCount);
            CategoriesCountBadgeText.Text = FormatCount(counts.CategoriesCount);
            AttributesCountBadgeText.Text = FormatCount(counts.AttributesCount);
            TagsCountBadgeText.Text = FormatCount(counts.TagsCount);
            UsersCountBadgeText.Text = FormatCount(counts.UsersCount);

            SidebarPendingCountText.Text = counts.PendingSyncCount.ToString();
            OpenPendingQueueBtnText.Text = $"Unsynced Queue ({counts.PendingSyncCount})";
        }
        catch { }
    }

    public async Task ReloadAllChildViewsAsync()
    {
        await CompaniesViewControl.ReloadCompaniesFromDatabaseAsync(resetDetailView: false);
        await PaymentMethodsViewControl.ReloadPaymentMethodsFromDatabaseAsync(resetDetailView: false);
        await DashboardViewControl.ReloadConfigsFromDatabaseAsync();
        await SessionsViewControl.ReloadSessionsFromDatabaseAsync(resetDetailView: false);
        await OrdersViewControl.ReloadOrdersFromDatabaseAsync(resetDetailView: false);
        await PaymentsViewControl.ReloadPaymentsFromDatabaseAsync(resetDetailView: false);
        await ProductsViewControl.ReloadProductsFromDatabaseAsync(resetDetailView: false);
        await ComboChoicesViewControl.ReloadCombosFromDatabaseAsync(resetDetailView: false);
        await CategoriesViewControl.ReloadCategoriesFromDatabaseAsync(resetDetailView: false);
        await AttributesViewControl.ReloadAttributesFromDatabaseAsync(resetDetailView: false);
        await TagsViewControl.ReloadTagsFromDatabaseAsync(resetDetailView: false);
        await UsersViewControl.ReloadUsersFromDatabaseAsync(resetDetailView: false);
        UsersViewControl.NotifyUsersUpdated();
        await RefreshDatabaseCountsAsync();
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
        Visibility labelVis = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        SidebarMenuColumn.Width = new GridLength(_isSidebarCollapsed ? _collapsedSidebarWidth : _expandedSidebarWidth);

        SidebarBrandTitle.Visibility = labelVis;
        SectionRegisterHeader.Visibility = labelVis;
        SectionCatalogHeader.Visibility = labelVis;
        SectionAdminHeader.Visibility = labelVis;

        DashboardLabel.Visibility = labelVis;
        SessionsLabel.Visibility = labelVis;
        OrdersLabel.Visibility = labelVis;
        PaymentsLabel.Visibility = labelVis;
        PaymentMethodsLabel.Visibility = labelVis;
        ProductsLabel.Visibility = labelVis;
        CombosLabel.Visibility = labelVis;
        CategoriesLabel.Visibility = labelVis;
        AttributesLabel.Visibility = labelVis;
        ProductTagsLabel.Visibility = labelVis;
        CompaniesLabel.Visibility = labelVis;
        UsersLabel.Visibility = labelVis;
        PendingSyncLabel.Visibility = labelVis;
        SidebarPendingCountBadge.Visibility = labelVis;
        PosSettingsLabel.Visibility = labelVis;
        ReceiptDesignsLabel.Visibility = labelVis;
        SettingsLabel.Visibility = labelVis;
        LogoutLabel.Visibility = labelVis;

        ToggleIconScale.ScaleX = _isSidebarCollapsed ? -1 : 1;
        SidebarToggleButton.HorizontalAlignment = _isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        SidebarToggleButton.ToolTip = _isSidebarCollapsed
            ? "Expand Navigation Sidebar (>|)"
            : "Collapse Navigation Sidebar (|<)";
    }

    // =========================================================================
    // 2. PRIMARY NAVIGATION (ALWAYS RESETS OPEN DETAIL PAGES BACK TO MAIN VIEW)
    // =========================================================================
    private void NavDashboard_Click(object sender, RoutedEventArgs e) => SwitchToDashboard();
    private void NavSessions_Click(object sender, RoutedEventArgs e) => SwitchToSessions();
    private void NavOrders_Click(object sender, RoutedEventArgs e) => SwitchToOrders();
    private void NavPayments_Click(object sender, RoutedEventArgs e) => SwitchToPayments();
    private void NavPaymentMethods_Click(object sender, RoutedEventArgs e) => SwitchToPaymentMethods();
    private void NavProducts_Click(object sender, RoutedEventArgs e) => SwitchToProducts();
    private void NavCombos_Click(object sender, RoutedEventArgs e) => SwitchToCombos();
    private void NavCategories_Click(object sender, RoutedEventArgs e) => SwitchToCategories();
    private void NavAttributes_Click(object sender, RoutedEventArgs e) => SwitchToAttributes();
    private void NavProductTags_Click(object sender, RoutedEventArgs e) => SwitchToTags();
    private void NavCompanies_Click(object sender, RoutedEventArgs e) => SwitchToCompanies();
    private void NavUsers_Click(object sender, RoutedEventArgs e) => SwitchToUsers();
    private void NavPosSettings_Click(object sender, RoutedEventArgs e) => SwitchToPosSettings();
    private void NavReceiptDesigns_Click(object sender, RoutedEventArgs e) => SwitchToReceiptDesigns();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => SwitchToSettings();

    private void HideAllMainViews()
    {
        DashboardViewControl.Visibility = Visibility.Collapsed;
        SettingsViewPanel.Visibility = Visibility.Collapsed;
        PosSettingsViewControl.Visibility = Visibility.Collapsed;
        CompaniesViewControl.Visibility = Visibility.Collapsed;
        PaymentMethodsViewControl.Visibility = Visibility.Collapsed;
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

        Button[] allButtons =
        [
            NavDashboardBtn, NavSessionsBtn, NavOrdersBtn, NavPaymentsBtn, NavPaymentMethodsBtn,
            NavProductsBtn, NavCombosBtn, NavCategoriesBtn, NavAttributesBtn, NavProductTagsBtn,
            NavCompaniesBtn, NavUsersBtn, NavPosSettingsBtn, NavReceiptDesignsBtn, NavSettingsBtn
        ];

        TextBlock[] allLabels =
        [
            DashboardLabel, SessionsLabel, OrdersLabel, PaymentsLabel, PaymentMethodsLabel,
            ProductsLabel, CombosLabel, CategoriesLabel, AttributesLabel, ProductTagsLabel,
            CompaniesLabel, UsersLabel, PosSettingsLabel, ReceiptDesignsLabel, SettingsLabel
        ];

        foreach (var btn in allButtons)
            btn.Style = (btn == activeButton) ? activeStyle : defaultStyle;

        foreach (var lbl in allLabels)
            lbl.Foreground = (lbl == activeLabel) ? purpleBrush : primaryTextBrush;
    }

    public void SwitchToDashboard()
    {
        HideAllMainViews();
        DashboardViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavDashboardBtn, DashboardLabel);
        _ = DashboardViewControl.ReloadConfigsFromDatabaseAsync();
    }

    public void SwitchToSessions(string? shopFilter = null)
    {
        HideAllMainViews();
        SessionsViewControl.VM.ResetToMainList();
        SessionsViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavSessionsBtn, SessionsLabel);
        _ = SessionsViewControl.ReloadSessionsFromDatabaseAsync(shopFilter, resetDetailView: true);
    }

    public void SwitchToOrders(string? sessionFilter = null)
    {
        HideAllMainViews();
        OrdersViewControl.VM.ResetToMainList();
        OrdersViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavOrdersBtn, OrdersLabel);
        _ = OrdersViewControl.ReloadOrdersFromDatabaseAsync(sessionFilter, resetDetailView: true);
    }

    public void SwitchToPayments(string? sessionFilter = null)
    {
        HideAllMainViews();
        PaymentsViewControl.VM.ResetToMainList();
        PaymentsViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavPaymentsBtn, PaymentsLabel);
        _ = PaymentsViewControl.ReloadPaymentsFromDatabaseAsync(sessionFilter, resetDetailView: true);
    }

    public void SwitchToPaymentMethods()
    {
        HideAllMainViews();
        PaymentMethodsViewControl.VM.ResetToMainList();
        PaymentMethodsViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavPaymentMethodsBtn, PaymentMethodsLabel);
        _ = PaymentMethodsViewControl.ReloadPaymentMethodsFromDatabaseAsync(resetDetailView: true);
    }

    public void SwitchToProducts()
    {
        HideAllMainViews();
        ProductsViewControl.ViewModel.ResetToMainList();
        ProductsViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavProductsBtn, ProductsLabel);
        _ = ProductsViewControl.ReloadProductsFromDatabaseAsync(resetDetailView: true);
    }

    public void SwitchToCombos()
    {
        HideAllMainViews();
        ComboChoicesViewControl.ViewModel.ResetToMainList();
        ComboChoicesViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavCombosBtn, CombosLabel);
        _ = ComboChoicesViewControl.ReloadCombosFromDatabaseAsync(resetDetailView: true);
    }

    public void SwitchToCategories()
    {
        HideAllMainViews();
        CategoriesViewControl.ViewModel.ResetToMainList();
        CategoriesViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavCategoriesBtn, CategoriesLabel);
        _ = CategoriesViewControl.ReloadCategoriesFromDatabaseAsync(resetDetailView: true);
    }

    public void SwitchToAttributes()
    {
        HideAllMainViews();
        AttributesViewControl.ViewModel.ResetToMainList();
        AttributesViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavAttributesBtn, AttributesLabel);
        _ = AttributesViewControl.ReloadAttributesFromDatabaseAsync(resetDetailView: true);
    }

    public void SwitchToTags()
    {
        HideAllMainViews();
        TagsViewControl.ViewModel.ResetToMainList();
        TagsViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavProductTagsBtn, ProductTagsLabel);
        _ = TagsViewControl.ReloadTagsFromDatabaseAsync(resetDetailView: true);
    }

    public void SwitchToCompanies()
    {
        HideAllMainViews();
        CompaniesViewControl.VM.ResetToMainList();
        CompaniesViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavCompaniesBtn, CompaniesLabel);
        _ = CompaniesViewControl.ReloadCompaniesFromDatabaseAsync(resetDetailView: true);
    }

    public void SwitchToUsers()
    {
        HideAllMainViews();
        UsersViewControl.ViewModel.ResetToMainList();
        UsersViewControl.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavUsersBtn, UsersLabel);
        _ = UsersViewControl.ReloadUsersFromDatabaseAsync(resetDetailView: true);
    }

    public void SwitchToPosSettings(int? configOdooId = null, bool openReceiptDesigns = false)
    {
        HideAllMainViews();
        PosSettingsViewControl.Visibility = Visibility.Visible;
        PosSettingsViewControl.VM.IsReceiptDesignsSubTabSelected = openReceiptDesigns;
        if (openReceiptDesigns)
            SetActiveSidebarButton(NavReceiptDesignsBtn, ReceiptDesignsLabel);
        else
            SetActiveSidebarButton(NavPosSettingsBtn, PosSettingsLabel);
        _ = PosSettingsViewControl.VM.LoadDataAsync(configOdooId);
    }

    public void SwitchToReceiptDesigns()
    {
        SwitchToPosSettings(configOdooId: null, openReceiptDesigns: true);
    }

    public void SwitchToSettings()
    {
        HideAllMainViews();
        SettingsViewPanel.Visibility = Visibility.Visible;
        SetActiveSidebarButton(NavSettingsBtn, SettingsLabel);
        _ = RefreshConfigAndCountsAsync();
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
        TabGeneral.Style = activeStyle;
        clickedTab.Style = activeStyle;

        GeneralSettingsScrollViewer.Visibility = Visibility.Visible;
        OtherTabsEmptyContainer.Visibility = Visibility.Collapsed;
    }

    // =========================================================================
    // 4. PASSWORD MASK / REVEAL TOGGLE & CREDENTIAL PERSISTENCE HELPER
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

    private async Task<OdooConfigRecord> GetAndSaveCurrentFormConfigAsync()
    {
        string url = CfgServerUrlTextBox.Text.Trim();
        string db = CfgDatabaseTextBox.Text.Trim();
        string user = CfgUsernameTextBox.Text.Trim();
        string pwd = GetCurrentPassword();

        if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(db) && !string.IsNullOrWhiteSpace(user))
        {
            var config = new OdooConfigRecord(url, db, user, pwd, null, ConnectionStatusText.Text);
            await SqliteDatabaseService.Instance.SaveOdooConfigAsync(config);
            return config;
        }

        return await SqliteDatabaseService.Instance.GetOdooConfigAsync();
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

    private static void SetButtonSpinAnimation(Button button, RotateTransform rotation, ProgressBar? progressBar, TextBlock buttonText, bool isLoading, string loadingText, string defaultText)
    {
        if (isLoading)
        {
            button.IsEnabled = false;
            buttonText.Text = loadingText;
            if (progressBar != null) progressBar.Visibility = Visibility.Visible;

            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            rotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            rotation.Angle = 0;
            buttonText.Text = defaultText;
            if (progressBar != null) progressBar.Visibility = Visibility.Collapsed;
            button.IsEnabled = true;
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
    // 6. MASTER ONE-CLICK SYNCHRONIZATION & CLEAR LOCAL DATA
    // =========================================================================
    private async void SyncAllNowBtn_Click(object sender, RoutedEventArgs e)
    {
        await RunFullSynchronizationAsync();
    }

    public async Task<bool> RunFullSynchronizationAsync()
    {
        try
        {
            SetButtonSpinAnimation(SyncAllNowBtn, MasterSyncIconRotation, null, MasterSyncButtonText, true, "Synchronizing All 12 Odoo Models...", "Sync All Data from Odoo");
            ClearLocalCacheBtn.IsEnabled = false;

            var config = await GetAndSaveCurrentFormConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl) || string.IsNullOrWhiteSpace(config.DatabaseName) || string.IsNullOrWhiteSpace(config.Username))
            {
                MasterSyncStepText.Text = "Configuration Incomplete";
                MasterSyncSummaryText.Text = "Please enter your Odoo Server URL, Database Name, Username, and Password/API Key first.";
                MasterSyncSummaryText.Foreground = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
                ShowToast("Please fill in your Odoo connection credentials first.", isWarning: true);
                return false;
            }

            var progress = new Progress<SyncProgressReport>(report =>
            {
                MasterSyncStepText.Text = $"Step {report.StepNumber}/{report.TotalSteps}: {report.StatusMessage}";
                MasterSyncPercentText.Text = $"{report.Percentage:0}%";
                MasterSyncProgressBar.Value = report.Percentage;
            });

            var summary = await OdooSyncService.Instance.SyncAllFromOdooAsync(config, progress);

            MasterSyncStepText.Text = "All 12 Odoo Modules Synchronized Successfully";
            MasterSyncPercentText.Text = "100%";
            MasterSyncProgressBar.Value = 100;
            MasterSyncSummaryText.Text = summary.Message;
            MasterSyncSummaryText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await LoadSavedConfigAsync();
            await ReloadAllChildViewsAsync();

            ShowToast(summary.Message);
            return true;
        }
        catch (Exception ex)
        {
            MasterSyncStepText.Text = "Synchronization Interrupted";
            MasterSyncSummaryText.Text = $"Sync Failed: {ex.Message}";
            MasterSyncSummaryText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
            return false;
        }
        finally
        {
            SetButtonSpinAnimation(SyncAllNowBtn, MasterSyncIconRotation, null, MasterSyncButtonText, false, "Synchronizing All 12 Odoo Models...", "Sync All Data from Odoo");
            ClearLocalCacheBtn.IsEnabled = true;
        }
    }

    private async void ClearLocalCacheBtn_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Are you sure you want to clear all locally cached Companies, POS Shops, Payment Methods, Catalog, Order, Session, and User records?\n\nYour Odoo Server connection credentials will be kept intact.",
            "Clear Local Database",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await SqliteDatabaseService.Instance.ClearAllLocalDataAsync();
            MasterSyncProgressBar.Value = 0;
            MasterSyncPercentText.Text = "0%";
            MasterSyncStepText.Text = "Local database cleared";
            MasterSyncSummaryText.Text = "All local tables are now empty. Click 'Sync All Data from Odoo' to pull fresh records.";
            MasterSyncSummaryText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            await LoadSavedConfigAsync();
            await ReloadAllChildViewsAsync();
            ShowToast("All local entity data cleared.");
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to clear local data: {ex.Message}", isError: true);
        }
    }

    // =========================================================================
    // 7. INDIVIDUAL MODULE SYNCHRONIZATION HANDLERS (ALL 12 MODELS)
    // =========================================================================
    private async void SyncCompaniesNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncCompaniesNowBtn, SettingsCompanySyncIconRotation, CompaniesSyncProgressBar, SettingsCompanySyncButtonText, true, "Syncing...", "Sync Companies");
            CompaniesSyncDetailText.Text = "Connecting to Odoo & reconciling res.company...";
            CompaniesSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var companies = await OdooSyncService.Instance.FetchOdooCompaniesAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooCompaniesAsync(companies);

            string statusMsg = $"Companies synced: {imported} added, {updated} updated, {deleted} removed ({companies.Count} total).";
            await SqliteDatabaseService.Instance.UpdateCompanySyncStatusAsync(DateTime.Now, statusMsg);

            LastCompaniesSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            CompaniesSyncDetailText.Text = statusMsg;
            CompaniesSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await CompaniesViewControl.ReloadCompaniesFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();

            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            CompaniesSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            CompaniesSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetButtonSpinAnimation(SyncCompaniesNowBtn, SettingsCompanySyncIconRotation, CompaniesSyncProgressBar, SettingsCompanySyncButtonText, false, "Syncing...", "Sync Companies");
        }
    }

    private async void SyncPaymentMethodsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncPaymentMethodsNowBtn, SettingsPayMethodSyncIconRotation, PaymentMethodsSyncProgressBar, SettingsPayMethodSyncButtonText, true, "Syncing...", "Sync Methods");
            PaymentMethodsSyncDetailText.Text = "Connecting to Odoo & reconciling pos.payment.method...";
            PaymentMethodsSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var methods = await OdooSyncService.Instance.FetchOdooPaymentMethodsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooPaymentMethodsAsync(methods);

            string statusMsg = $"Payment methods synced: {imported} added, {updated} updated, {deleted} removed ({methods.Count} total).";
            await SqliteDatabaseService.Instance.UpdatePaymentMethodSyncStatusAsync(DateTime.Now, statusMsg);

            LastPaymentMethodsSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            PaymentMethodsSyncDetailText.Text = statusMsg;
            PaymentMethodsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await PaymentMethodsViewControl.ReloadPaymentMethodsFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();

            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            PaymentMethodsSyncDetailText.Text = $"Sync Failed: {ex.Message}";
            PaymentMethodsSyncDetailText.Foreground = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetButtonSpinAnimation(SyncPaymentMethodsNowBtn, SettingsPayMethodSyncIconRotation, PaymentMethodsSyncProgressBar, SettingsPayMethodSyncButtonText, false, "Syncing...", "Sync Methods");
        }
    }

    private async void SyncUsersNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncUsersNowBtn, SettingsSyncIconRotation, SyncProgressBar, SettingsSyncButtonText, true, "Syncing...", "Sync Users");
            SyncDetailText.Text = "Connecting to Odoo & reconciling res.users...";
            SyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var odooUsers = await OdooSyncService.Instance.FetchOdooUsersAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooUsersAsync(odooUsers);

            string statusMsg = $"Users synced: {imported} added, {updated} updated, {deleted} removed ({odooUsers.Count} total).";
            await SqliteDatabaseService.Instance.UpdateSyncStatusAsync(DateTime.Now, statusMsg);

            LastSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            SyncDetailText.Text = statusMsg;
            SyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await UsersViewControl.ReloadUsersFromDatabaseAsync(resetDetailView: false);
            UsersViewControl.NotifyUsersUpdated();
            await RefreshDatabaseCountsAsync();

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
            SetButtonSpinAnimation(SyncUsersNowBtn, SettingsSyncIconRotation, SyncProgressBar, SettingsSyncButtonText, false, "Syncing...", "Sync Users");
        }
    }

    private async void SyncCategoriesNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncCategoriesNowBtn, SettingsCatSyncIconRotation, CategoriesSyncProgressBar, SettingsCatSyncButtonText, true, "Syncing...", "Sync Categories");
            CategoriesSyncDetailText.Text = "Connecting to Odoo & reconciling pos.category...";
            CategoriesSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var odooCategories = await OdooSyncService.Instance.FetchOdooCategoriesAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooCategoriesAsync(odooCategories);

            string statusMsg = $"Categories synced: {imported} added, {updated} updated, {deleted} removed ({odooCategories.Count} total).";
            await SqliteDatabaseService.Instance.UpdateCategorySyncStatusAsync(DateTime.Now, statusMsg);

            LastCategoriesSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            CategoriesSyncDetailText.Text = statusMsg;
            CategoriesSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await CategoriesViewControl.ReloadCategoriesFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();

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
            SetButtonSpinAnimation(SyncCategoriesNowBtn, SettingsCatSyncIconRotation, CategoriesSyncProgressBar, SettingsCatSyncButtonText, false, "Syncing...", "Sync Categories");
        }
    }

    private async void SyncAttributesNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncAttributesNowBtn, SettingsAttrSyncIconRotation, AttributesSyncProgressBar, SettingsAttrSyncButtonText, true, "Syncing...", "Sync Attributes");
            AttributesSyncDetailText.Text = "Connecting to Odoo & reconciling product.attribute...";
            AttributesSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var (odooAttrs, odooVals) = await OdooSyncService.Instance.FetchOdooAttributesAsync(config);
            var (aImp, aUpd, aDel, vImp, vUpd, vDel) = await SqliteDatabaseService.Instance.UpsertOdooAttributesAsync(odooAttrs, odooVals);

            string statusMsg = $"Attributes synced: {aImp} added, {aUpd} updated, {aDel} removed ({odooVals.Count} values).";
            await SqliteDatabaseService.Instance.UpdateAttributeSyncStatusAsync(DateTime.Now, statusMsg);

            LastAttributesSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            AttributesSyncDetailText.Text = statusMsg;
            AttributesSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await AttributesViewControl.ReloadAttributesFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();

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
            SetButtonSpinAnimation(SyncAttributesNowBtn, SettingsAttrSyncIconRotation, AttributesSyncProgressBar, SettingsAttrSyncButtonText, false, "Syncing...", "Sync Attributes");
        }
    }

    private async void SyncTagsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncTagsNowBtn, SettingsTagSyncIconRotation, TagsSyncProgressBar, SettingsTagSyncButtonText, true, "Syncing...", "Sync Tags");
            TagsSyncDetailText.Text = "Connecting to Odoo & reconciling product.tag...";
            TagsSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var odooTags = await OdooSyncService.Instance.FetchOdooTagsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooTagsAsync(odooTags);

            string statusMsg = $"Tags synced: {imported} added, {updated} updated, {deleted} removed ({odooTags.Count} total).";
            await SqliteDatabaseService.Instance.UpdateTagSyncStatusAsync(DateTime.Now, statusMsg);

            LastTagsSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            TagsSyncDetailText.Text = statusMsg;
            TagsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await TagsViewControl.ReloadTagsFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();

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
            SetButtonSpinAnimation(SyncTagsNowBtn, SettingsTagSyncIconRotation, TagsSyncProgressBar, SettingsTagSyncButtonText, false, "Syncing...", "Sync Tags");
        }
    }

    private async void SyncProductsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncProductsNowBtn, SettingsProdSyncIconRotation, ProductsSyncProgressBar, SettingsProdSyncButtonText, true, "Syncing...", "Sync Products");
            ProductsSyncDetailText.Text = "Connecting to Odoo & reconciling product.template...";
            ProductsSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var odooProducts = await OdooSyncService.Instance.FetchOdooProductsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooProductsAsync(odooProducts);

            string statusMsg = $"Products synced: {imported} added, {updated} updated, {deleted} removed ({odooProducts.Count} total).";
            await SqliteDatabaseService.Instance.UpdateProductSyncStatusAsync(DateTime.Now, statusMsg);

            LastProductsSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            ProductsSyncDetailText.Text = statusMsg;
            ProductsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await ProductsViewControl.ReloadProductsFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();

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
            SetButtonSpinAnimation(SyncProductsNowBtn, SettingsProdSyncIconRotation, ProductsSyncProgressBar, SettingsProdSyncButtonText, false, "Syncing...", "Sync Products");
        }
    }

    private async void SyncCombosNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncCombosNowBtn, SettingsComboSyncIconRotation, CombosSyncProgressBar, SettingsComboSyncButtonText, true, "Syncing...", "Sync Combos");
            CombosSyncDetailText.Text = "Connecting to Odoo & reconciling Combo Choices...";
            CombosSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var (combos, lines) = await OdooSyncService.Instance.FetchOdooComboChoicesAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooCombosAsync(combos, lines);

            string statusMsg = $"Combos synced: {imported} added, {updated} updated, {deleted} removed ({combos.Count} combos, {lines.Count} items).";
            await SqliteDatabaseService.Instance.UpdateComboSyncStatusAsync(DateTime.Now, statusMsg);

            LastCombosSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            CombosSyncDetailText.Text = statusMsg;
            CombosSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await ComboChoicesViewControl.ReloadCombosFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();

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
            SetButtonSpinAnimation(SyncCombosNowBtn, SettingsComboSyncIconRotation, CombosSyncProgressBar, SettingsComboSyncButtonText, false, "Syncing...", "Sync Combos");
        }
    }

    private async void SyncSessionsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncSessionsNowBtn, SettingsSessionSyncIconRotation, SessionsSyncProgressBar, SettingsSessionSyncButtonText, true, "Syncing...", "Sync Sessions");
            SessionsSyncDetailText.Text = "Connecting to Odoo & reconciling pos.session...";
            SessionsSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var sessionsDto = await OdooSyncService.Instance.FetchOdooSessionsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooSessionsAsync(sessionsDto);

            string statusMsg = $"Sessions synced: {imported} added, {updated} updated, {deleted} removed ({sessionsDto.Count} total).";
            await SqliteDatabaseService.Instance.UpdateSessionSyncStatusAsync(DateTime.Now, statusMsg);

            LastSessionsSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            SessionsSyncDetailText.Text = statusMsg;
            SessionsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await SessionsViewControl.ReloadSessionsFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();

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
            SetButtonSpinAnimation(SyncSessionsNowBtn, SettingsSessionSyncIconRotation, SessionsSyncProgressBar, SettingsSessionSyncButtonText, false, "Syncing...", "Sync Sessions");
        }
    }

    private async void SyncOrdersNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncOrdersNowBtn, SettingsOrderSyncIconRotation, OrdersSyncProgressBar, SettingsOrderSyncButtonText, true, "Syncing...", "Sync Orders");
            OrdersSyncDetailText.Text = "Connecting to Odoo & reconciling pos.order & pos.order.line...";
            OrdersSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var (orders, lines, payments) = await OdooSyncService.Instance.FetchOdooOrdersAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooOrdersAsync(orders, lines, payments);

            string statusMsg = $"Orders synced: {imported} added, {updated} updated, {deleted} removed ({orders.Count} orders, {lines.Count} lines).";
            await SqliteDatabaseService.Instance.UpdateOrderSyncStatusAsync(DateTime.Now, statusMsg);

            LastOrdersSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            OrdersSyncDetailText.Text = statusMsg;
            OrdersSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await OrdersViewControl.ReloadOrdersFromDatabaseAsync(resetDetailView: false);
            await PaymentsViewControl.ReloadPaymentsFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();

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
            SetButtonSpinAnimation(SyncOrdersNowBtn, SettingsOrderSyncIconRotation, OrdersSyncProgressBar, SettingsOrderSyncButtonText, false, "Syncing...", "Sync Orders");
        }
    }

    private async void SyncPaymentsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncPaymentsNowBtn, SettingsPaymentSyncIconRotation, PaymentsSyncProgressBar, SettingsPaymentSyncButtonText, true, "Syncing...", "Sync Payments");
            PaymentsSyncDetailText.Text = "Connecting to Odoo & reconciling pos.payment...";
            PaymentsSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var paymentsDto = await OdooSyncService.Instance.FetchOdooPaymentsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooPaymentsAsync(paymentsDto);

            string statusMsg = $"Payments synced: {imported} added, {updated} updated, {deleted} removed ({paymentsDto.Count} total).";
            await SqliteDatabaseService.Instance.UpdatePaymentSyncStatusAsync(DateTime.Now, statusMsg);

            LastPaymentsSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            PaymentsSyncDetailText.Text = statusMsg;
            PaymentsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await PaymentsViewControl.ReloadPaymentsFromDatabaseAsync(resetDetailView: false);
            await RefreshDatabaseCountsAsync();

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
            SetButtonSpinAnimation(SyncPaymentsNowBtn, SettingsPaymentSyncIconRotation, PaymentsSyncProgressBar, SettingsPaymentSyncButtonText, false, "Syncing...", "Sync Payments");
        }
    }

    private async void SyncPosConfigsNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetButtonSpinAnimation(SyncPosConfigsNowBtn, SettingsPosConfigSyncIconRotation, PosConfigsSyncProgressBar, SettingsPosConfigSyncButtonText, true, "Syncing...", "Sync Shops");
            PosConfigsSyncDetailText.Text = "Connecting to Odoo & reconciling pos.config...";
            PosConfigsSyncDetailText.Foreground = GetSafeBrush("SecondaryTextBrush", Color.FromRgb(0x6B, 0x72, 0x80));

            var config = await GetAndSaveCurrentFormConfigAsync();
            var odooConfigs = await OdooSyncService.Instance.FetchOdooPosConfigsAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooPosConfigsAsync(odooConfigs);

            string statusMsg = $"POS Shops synced: {imported} added, {updated} updated, {deleted} removed ({odooConfigs.Count} total).";
            await SqliteDatabaseService.Instance.UpdatePosConfigSyncStatusAsync(DateTime.Now, statusMsg);

            LastPosConfigsSyncedStatusText.Text = $"Last Synced: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            PosConfigsSyncDetailText.Text = statusMsg;
            PosConfigsSyncDetailText.Foreground = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));

            await DashboardViewControl.ReloadConfigsFromDatabaseAsync();
            await RefreshDatabaseCountsAsync();

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
            SetButtonSpinAnimation(SyncPosConfigsNowBtn, SettingsPosConfigSyncIconRotation, PosConfigsSyncProgressBar, SettingsPosConfigSyncButtonText, false, "Syncing...", "Sync Shops");
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