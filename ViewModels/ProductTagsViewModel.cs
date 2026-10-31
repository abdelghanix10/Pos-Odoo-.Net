using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class ProductTagsViewModel : ViewModelBase
{
    private readonly List<ProductTagRecord> _allTags = [];

    [ObservableProperty]
    private ObservableCollection<ProductTagRecord> _filteredTags = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private ProductTagRecord? _selectedTag;

    [ObservableProperty]
    private string _pagerText = "1 / 1";

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

    public event EventHandler? TagsUpdated;

    private readonly DispatcherTimer _toastTimer;

    public ProductTagsViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadTagsAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public async Task ReloadTagsAsync()
    {
        try
        {
            var tags = await SqliteDatabaseService.Instance.LoadAllTagsAsync();
            _allTags.Clear();
            _allTags.AddRange(tags);
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load tags: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredTags.Clear();

        IEnumerable<ProductTagRecord> source = _allTags;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(t =>
                t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.ColorNameDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var t in source)
        {
            FilteredTags.Add(t);
        }

        if (IsDetailViewVisible)
        {
            UpdatePager();
        }
    }

    private void UpdatePager()
    {
        if (FilteredTags.Count == 0 || SelectedTag == null)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredTags.IndexOf(SelectedTag);
        if (index < 0)
        {
            index = 0;
            SelectedTag = FilteredTags[0];
        }

        PagerText = $"{index + 1} / {FilteredTags.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredTags.Count - 1;
    }

    [RelayCommand]
    private void SelectTag(ProductTagRecord? tag)
    {
        if (tag == null) return;
        SelectedTag = tag;
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
        if (SelectedTag == null || FilteredTags.Count == 0) return;
        int index = FilteredTags.IndexOf(SelectedTag);
        if (index > 0)
        {
            SelectTag(FilteredTags[index - 1]);
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (SelectedTag == null || FilteredTags.Count == 0) return;
        int index = FilteredTags.IndexOf(SelectedTag);
        if (index >= 0 && index < FilteredTags.Count - 1)
        {
            SelectTag(FilteredTags[index + 1]);
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

            var tags = await OdooSyncService.Instance.FetchOdooTagsAsync(config, allowOfflineSimulation: true);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooTagsAsync(tags);
            await SqliteDatabaseService.Instance.UpdateTagSyncStatusAsync(DateTime.Now, "Success");

            await ReloadTagsAsync();
            TagsUpdated?.Invoke(this, EventArgs.Empty);

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
