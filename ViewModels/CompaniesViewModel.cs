using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class CompaniesViewModel : ViewModelBase
{
    private readonly List<CompanyRecord> _allCompanies = [];

    [ObservableProperty]
    private ObservableCollection<CompanyRecord> _filteredCompanies = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isKanbanView = true;

    [ObservableProperty]
    private bool _isListView;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private CompanyRecord? _selectedCompany;

    [ObservableProperty]
    private int _activeTabIndex = 1; // 1: General Information, 2: Branches

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

    public event EventHandler? CompaniesUpdated;

    private readonly DispatcherTimer _toastTimer;

    public CompaniesViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadCompaniesAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public void ResetToMainList()
    {
        IsDetailViewVisible = false;
        UpdatePager();
    }

    public async Task ReloadCompaniesAsync(bool resetDetailView = false)
    {
        try
        {
            if (resetDetailView)
            {
                IsDetailViewVisible = false;
            }

            var companies = await SqliteDatabaseService.Instance.LoadAllCompaniesAsync();
            _allCompanies.Clear();
            _allCompanies.AddRange(companies);
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load companies: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredCompanies.Clear();

        IEnumerable<CompanyRecord> source = _allCompanies;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.City.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.CountryName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Email.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Phone.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Vat.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Ice.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var c in source)
        {
            FilteredCompanies.Add(c);
        }

        IsEmptyStateVisible = FilteredCompanies.Count == 0;
        ListCounterText = FilteredCompanies.Count > 0
            ? $"1-{FilteredCompanies.Count} / {FilteredCompanies.Count}"
            : "0-0 / 0";

        if (IsDetailViewVisible)
        {
            if (SelectedCompany != null && !FilteredCompanies.Contains(SelectedCompany))
            {
                SelectedCompany = FilteredCompanies.FirstOrDefault();
                if (SelectedCompany == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void UpdatePager()
    {
        if (FilteredCompanies.Count == 0 || SelectedCompany == null)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredCompanies.IndexOf(SelectedCompany);
        if (index < 0)
        {
            index = 0;
            SelectedCompany = FilteredCompanies[0];
        }

        PagerText = $"{index + 1} / {FilteredCompanies.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredCompanies.Count - 1;
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
    private void SelectCompany(CompanyRecord? company)
    {
        if (company == null) return;
        SelectedCompany = company;
        ActiveTabIndex = 1;
        IsDetailViewVisible = true;
        UpdatePager();
    }

    [RelayCommand]
    private void BackToList()
    {
        IsDetailViewVisible = false;
    }

    [RelayCommand]
    private void PreviousRecord()
    {
        if (SelectedCompany == null || FilteredCompanies.Count == 0) return;
        int index = FilteredCompanies.IndexOf(SelectedCompany);
        if (index > 0)
        {
            SelectedCompany = FilteredCompanies[index - 1];
            UpdatePager();
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (SelectedCompany == null || FilteredCompanies.Count == 0) return;
        int index = FilteredCompanies.IndexOf(SelectedCompany);
        if (index >= 0 && index < FilteredCompanies.Count - 1)
        {
            SelectedCompany = FilteredCompanies[index + 1];
            UpdatePager();
        }
    }

    [RelayCommand]
    private void SelectTab(string tabIndexStr)
    {
        if (int.TryParse(tabIndexStr, out int tabIndex))
        {
            ActiveTabIndex = tabIndex;
        }
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

            var companies = await OdooSyncService.Instance.FetchOdooCompaniesAsync(config);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooCompaniesAsync(companies);
            await SqliteDatabaseService.Instance.UpdateCompanySyncStatusAsync(DateTime.Now, "Success");

            await ReloadCompaniesAsync();
            CompaniesUpdated?.Invoke(this, EventArgs.Empty);

            ShowToast($"Companies synced: {imported} imported, {updated} updated, {deleted} removed.", "Success");
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