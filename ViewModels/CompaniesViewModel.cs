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

    public const int PageSize = 80;

    [ObservableProperty]
    private int _currentPage = 1;

    private readonly List<CompanyRecord> _matchingCompanies = [];

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

        _matchingCompanies.Clear();
        foreach (var c in source)
        {
            _matchingCompanies.Add(c);
        }

        IsEmptyStateVisible = _matchingCompanies.Count == 0;
        CurrentPage = 1;
        ApplyPaging();

        if (IsDetailViewVisible)
        {
            if (SelectedCompany != null && !_matchingCompanies.Contains(SelectedCompany))
            {
                SelectedCompany = _matchingCompanies.FirstOrDefault();
                if (SelectedCompany == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
    }

    private void ApplyPaging()
    {
        FilteredCompanies.Clear();
        var pageItems = _matchingCompanies
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);
        foreach (var c in pageItems)
        {
            FilteredCompanies.Add(c);
        }
        UpdatePager();
    }

    private void UpdatePager()
    {
        if (IsDetailViewVisible)
        {
            if (_matchingCompanies.Count == 0 || SelectedCompany == null)
            {
                PagerText = "0 / 0";
                HasPreviousRecord = false;
                HasNextRecord = false;
                return;
            }

            int index = _matchingCompanies.IndexOf(SelectedCompany);
            if (index < 0)
            {
                index = 0;
                SelectedCompany = _matchingCompanies[0];
            }

            PagerText = $"{index + 1} / {_matchingCompanies.Count}";
            HasPreviousRecord = index > 0;
            HasNextRecord = index < _matchingCompanies.Count - 1;
        }
        else
        {
            int total = _matchingCompanies.Count;
            int maxPage = total > 0 ? (int)Math.Ceiling((double)total / PageSize) : 1;
            if (CurrentPage > maxPage) CurrentPage = maxPage;
            if (CurrentPage < 1) CurrentPage = 1;

            if (total == 0)
            {
                ListCounterText = "0-0 / 0";
                HasPreviousRecord = false;
                HasNextRecord = false;
            }
            else
            {
                int start = (CurrentPage - 1) * PageSize + 1;
                int end = Math.Min(CurrentPage * PageSize, total);
                ListCounterText = $"{start}-{end} / {total}";
                HasPreviousRecord = CurrentPage > 1;
                HasNextRecord = CurrentPage < maxPage;
            }
        }
    }

    [RelayCommand]
    private void SwitchToKanban()
    {
        IsKanbanView = true;
        IsListView = false;
        IsDetailViewVisible = false;
        UpdatePager();
    }

    [RelayCommand]
    private void SwitchToList()
    {
        IsListView = true;
        IsKanbanView = false;
        IsDetailViewVisible = false;
        UpdatePager();
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
        UpdatePager();
    }

    [RelayCommand]
    private void PreviousRecord()
    {
        if (IsDetailViewVisible)
        {
            if (SelectedCompany == null || _matchingCompanies.Count == 0) return;
            int index = _matchingCompanies.IndexOf(SelectedCompany);
            if (index > 0)
            {
                SelectedCompany = _matchingCompanies[index - 1];
                UpdatePager();
            }
        }
        else
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                ApplyPaging();
            }
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (IsDetailViewVisible)
        {
            if (SelectedCompany == null || _matchingCompanies.Count == 0) return;
            int index = _matchingCompanies.IndexOf(SelectedCompany);
            if (index >= 0 && index < _matchingCompanies.Count - 1)
            {
                SelectedCompany = _matchingCompanies[index + 1];
                UpdatePager();
            }
        }
        else
        {
            int maxPage = (int)Math.Ceiling((double)_matchingCompanies.Count / PageSize);
            if (CurrentPage < maxPage)
            {
                CurrentPage++;
                ApplyPaging();
            }
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