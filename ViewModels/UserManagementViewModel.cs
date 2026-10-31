using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class UserManagementViewModel : ViewModelBase
{
    private readonly List<PosUserRecord> _allUsers = [];

    [ObservableProperty]
    private ObservableCollection<PosUserRecord> _filteredUsers = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private PosUserRecord? _selectedUser;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private bool _isVisibleInPos = true;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncButtonText = "Sync from Odoo";

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private string _toastSeverity = "Success";

    public event Action? UsersUpdated;
    public event EventHandler? RequestCloseToPos;

    private readonly DispatcherTimer _toastTimer;

    public UserManagementViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadUsersAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public async Task ReloadUsersAsync()
    {
        try
        {
            var users = await SqliteDatabaseService.Instance.LoadAllUsersAsync();
            _allUsers.Clear();
            _allUsers.AddRange(users);
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load users: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredUsers.Clear();

        IEnumerable<PosUserRecord> source = _allUsers;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(u =>
                u.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                u.Login.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var u in source)
        {
            FilteredUsers.Add(u);
        }
    }

    [RelayCommand]
    public void SelectUser(PosUserRecord? user)
    {
        if (user == null) return;
        SelectedUser = user;
        Password = user.Password ?? string.Empty;
        Pin = user.Pin ?? string.Empty;
        IsVisibleInPos = user.IsVisibleInPos;
        IsDetailViewVisible = true;
    }

    [RelayCommand]
    private void BackToList()
    {
        if (IsDetailViewVisible)
        {
            IsDetailViewVisible = false;
        }
        else
        {
            RequestCloseToPos?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Discard()
    {
        IsDetailViewVisible = false;
    }

    [RelayCommand]
    public async Task SaveCredentialsAsync()
    {
        if (SelectedUser == null) return;

        SelectedUser.Password = Password;
        SelectedUser.Pin = Pin;
        SelectedUser.IsVisibleInPos = IsVisibleInPos;

        await SqliteDatabaseService.Instance.SaveUserRecordAsync(SelectedUser);

        IsDetailViewVisible = false;
        await ReloadUsersAsync();
        UsersUpdated?.Invoke();
        ShowToast($"Credentials for \"{SelectedUser.Name.Trim()}\" updated successfully.", "Success");
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    public async Task SyncFromOdooAsync()
    {
        IsSyncing = true;
        SyncButtonText = "Syncing...";

        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Odoo connection not configured in Settings.", "Warning");
                return;
            }

            var odooUsers = await OdooSyncService.Instance.FetchOdooUsersAsync(config, allowOfflineSimulation: false);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooUsersAsync(odooUsers);
            await SqliteDatabaseService.Instance.UpdateSyncStatusAsync(DateTime.Now, "Success");

            await ReloadUsersAsync();
            UsersUpdated?.Invoke();

            string summary = $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed";
            ShowToast(summary, "Success");
        }
        catch (Exception ex)
        {
            ShowToast($"Sync Failed: {ex.Message}", "Error");
        }
        finally
        {
            IsSyncing = false;
            SyncButtonText = "Sync from Odoo";
        }
    }

    public void ShowToast(string message, string severity = "Success")
    {
        ToastMessage = message;
        ToastSeverity = severity;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
