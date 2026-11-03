using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class UserManagementViewModel : ViewModelBase
{
    private readonly List<PosUserRecord> _allUsers = [];
    public IReadOnlyList<PosUserRecord> AllUsers => _allUsers;

    [ObservableProperty]
    private ObservableCollection<PosUserRecord> _filteredUsers = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isKanbanView = true;

    [ObservableProperty]
    private bool _isListView;

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
    private string _pagerText = "0 / 0";

    [ObservableProperty]
    private string _listCounterText = "0-0 / 0";

    [ObservableProperty]
    private bool _hasPreviousRecord;

    [ObservableProperty]
    private bool _hasNextRecord;

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

    [ObservableProperty]
    private bool _isEmptyStateVisible;

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

    public void ResetToMainList()
    {
        IsDetailViewVisible = false;
        UpdatePager();
    }

    public async Task ReloadUsersAsync(bool resetDetailView = false)
    {
        try
        {
            if (resetDetailView)
            {
                IsDetailViewVisible = false;
            }

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
                u.Email.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                u.Role.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var u in source)
        {
            FilteredUsers.Add(u);
        }

        IsEmptyStateVisible = FilteredUsers.Count == 0;
        ListCounterText = FilteredUsers.Count > 0
            ? $"1-{FilteredUsers.Count} / {FilteredUsers.Count}"
            : "0-0 / 0";

        if (IsDetailViewVisible)
        {
            if (SelectedUser != null)
            {
                var match = FilteredUsers.FirstOrDefault(u => u.Login == SelectedUser.Login);
                SelectedUser = match ?? FilteredUsers.FirstOrDefault();
                if (SelectedUser == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void UpdatePager()
    {
        if (FilteredUsers.Count == 0 || SelectedUser == null)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredUsers.IndexOf(SelectedUser);
        if (index < 0)
        {
            index = 0;
            SelectedUser = FilteredUsers[0];
        }

        PagerText = $"{index + 1} / {FilteredUsers.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredUsers.Count - 1;
    }

    [RelayCommand]
    private void SwitchToKanban()
    {
        IsKanbanView = true;
        IsListView = false;
        IsDetailViewVisible = false;
    }

    [RelayCommand]
    private void SwitchToList()
    {
        IsListView = true;
        IsKanbanView = false;
        IsDetailViewVisible = false;
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
        UpdatePager();
    }

    [RelayCommand]
    private void PreviousRecord()
    {
        if (SelectedUser == null || FilteredUsers.Count == 0) return;
        int index = FilteredUsers.IndexOf(SelectedUser);
        if (index > 0)
        {
            SelectUser(FilteredUsers[index - 1]);
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (SelectedUser == null || FilteredUsers.Count == 0) return;
        int index = FilteredUsers.IndexOf(SelectedUser);
        if (index >= 0 && index < FilteredUsers.Count - 1)
        {
            SelectUser(FilteredUsers[index + 1]);
        }
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
        if (IsSyncing) return;
        IsSyncing = true;
        SyncButtonText = "Syncing...";

        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl) || string.IsNullOrWhiteSpace(config.DatabaseName))
            {
                ShowToast("Odoo connection is not configured in Settings.", "Warning");
                return;
            }

            var odooUsers = await OdooSyncService.Instance.FetchOdooUsersAsync(config);
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