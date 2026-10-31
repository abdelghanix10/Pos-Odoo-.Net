using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public enum ShellViewType
{
    Users,
    Products,
    Combos,
    Categories,
    Attributes,
    Tags,
    Settings
}

public partial class MainShellViewModel : ViewModelBase
{
    [ObservableProperty]
    private ShellViewType _activeView = ShellViewType.Settings;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private double _sidebarWidth = 240;

    [ObservableProperty]
    private string _currentUser = "Administrator";

    // Odoo Configuration
    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _databaseName = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isPasswordMasked = true;

    [ObservableProperty]
    private string _connectionStatusText = "Offline (Local Mode)";

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private int _activeSettingsTabIndex = 1;

    // Timestamps
    [ObservableProperty]
    private string _lastUsersSyncedText = "Last Synced: Never";

    [ObservableProperty]
    private string _lastCategoriesSyncedText = "Last Synced: Never";

    [ObservableProperty]
    private string _lastAttributesSyncedText = "Last Synced: Never";

    [ObservableProperty]
    private string _lastTagsSyncedText = "Last Synced: Never";

    [ObservableProperty]
    private string _lastProductsSyncedText = "Last Synced: Never";

    [ObservableProperty]
    private string _lastCombosSyncedText = "Last Synced: Never";

    // Module Syncing Busy States
    [ObservableProperty]
    private bool _isUsersSyncing;

    [ObservableProperty]
    private bool _isCategoriesSyncing;

    [ObservableProperty]
    private bool _isAttributesSyncing;

    [ObservableProperty]
    private bool _isTagsSyncing;

    [ObservableProperty]
    private bool _isProductsSyncing;

    [ObservableProperty]
    private bool _isCombosSyncing;

    // Shell Toast
    [ObservableProperty]
    private string _shellToastMessage = string.Empty;

    [ObservableProperty]
    private bool _isShellToastVisible;

    [ObservableProperty]
    private string _shellToastSeverity = "Success";

    public event EventHandler? LogoutRequested;

    private readonly DispatcherTimer _toastTimer;

    public MainShellViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsShellToastVisible = false;
        };

        // Listen for live online status updates
        OdooSyncService.Instance.OnlineStatusChanged += online =>
        {
            IsOnline = online;
            ConnectionStatusText = online ? "Odoo Online" : "Offline (Local Mode)";
        };

        _ = LoadSavedConfigAsync();
    }

    public async Task LoadSavedConfigAsync()
    {
        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            ServerUrl = config.ServerUrl;
            DatabaseName = config.DatabaseName;
            Username = config.Username;
            Password = config.ApiKey ?? "";

            if (!string.IsNullOrEmpty(config.LastSyncedAt))
            {
                LastUsersSyncedText = $"Last Synced: {config.LastSyncedAt}";
            }

            var lastCat = await SqliteDatabaseService.Instance.GetLastCategorySyncStatusAsync();
            if (!string.IsNullOrEmpty(lastCat)) LastCategoriesSyncedText = $"Last Synced: {lastCat}";

            var lastAttr = await SqliteDatabaseService.Instance.GetLastAttributeSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastAttr)) LastAttributesSyncedText = $"Last Synced: {lastAttr}";

            var lastTag = await SqliteDatabaseService.Instance.GetLastTagSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastTag)) LastTagsSyncedText = $"Last Synced: {lastTag}";

            var lastProd = await SqliteDatabaseService.Instance.GetLastProductSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastProd)) LastProductsSyncedText = $"Last Synced: {lastProd}";

            var lastCombo = await SqliteDatabaseService.Instance.GetLastComboSyncStatusAsync();
            if (!string.IsNullOrEmpty(lastCombo)) LastCombosSyncedText = $"Last Synced: {lastCombo}";

            if (!string.IsNullOrEmpty(config.LastStatus))
            {
                ConnectionStatusText = config.LastStatus;
                IsOnline = config.LastStatus.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) ||
                           config.LastStatus.Equals("Odoo Online", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        SidebarWidth = IsSidebarCollapsed ? 68 : 240;
    }

    [RelayCommand]
    public void Navigate(ShellViewType targetView)
    {
        ActiveView = targetView;
    }

    [RelayCommand]
    private void Logout()
    {
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void TogglePasswordMask()
    {
        IsPasswordMasked = !IsPasswordMasked;
    }

    [RelayCommand]
    private void SelectSettingsTab(string tabIndexStr)
    {
        if (int.TryParse(tabIndexStr, out int tab))
        {
            ActiveSettingsTabIndex = tab;
        }
    }

    [RelayCommand]
    public async Task SaveConfigAsync()
    {
        try
        {
            var config = new OdooConfigRecord(
                ServerUrl.Trim(),
                DatabaseName.Trim(),
                Username.Trim(),
                Password,
                null,
                ConnectionStatusText
            );

            await SqliteDatabaseService.Instance.SaveOdooConfigAsync(config);
            ShowShellToast("Odoo connection settings saved.");
            _ = OdooSyncService.Instance.CheckConnectivityAsync();
        }
        catch (Exception ex)
        {
            ShowShellToast($"Failed to save: {ex.Message}", "Error");
        }
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        IsTestingConnection = true;
        ConnectionStatusText = "Verifying connection...";

        try
        {
            var config = new OdooConfigRecord(
                ServerUrl.Trim(),
                DatabaseName.Trim(),
                Username.Trim(),
                Password,
                null,
                null
            );

            var result = await OdooSyncService.Instance.TestConnectionAsync(config);
            if (result.Success)
            {
                IsOnline = true;
                ConnectionStatusText = result.Message;
                ShowShellToast("Odoo connection verified successfully!");
            }
            else
            {
                IsOnline = false;
                ConnectionStatusText = result.Message;
                ShowShellToast(result.Message, "Error");
            }

            await SqliteDatabaseService.Instance.SaveOdooConfigAsync(config with { LastStatus = ConnectionStatusText });
        }
        catch (Exception ex)
        {
            IsOnline = false;
            ConnectionStatusText = $"Error: {ex.Message}";
            ShowShellToast(ex.Message, "Error");
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    public void ShowShellToast(string message, string severity = "Success")
    {
        ShellToastMessage = message;
        ShellToastSeverity = severity;
        IsShellToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
