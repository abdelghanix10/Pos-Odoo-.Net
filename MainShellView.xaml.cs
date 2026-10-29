using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OdooPosLogin;

public partial class MainShellView : UserControl
{
    private bool _isSidebarCollapsed = false;
    private readonly double _expandedSidebarWidth = 180.0;
    private readonly double _collapsedSidebarWidth = 56.0;

    // Events to communicate with parent Window
    public event EventHandler? LogoutRequested;

    public string CurrentUser { get; private set; } = "Administrator";
    public UserManagementView UsersControl => UsersViewControl;

    public MainShellView()
    {
        InitializeComponent();

        // Hook up UserManagementView "Back to POS" to return to Settings view
        UsersViewControl.RequestCloseToPos += (_, _) =>
        {
            SwitchToSettings();
        };
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
    }
}
