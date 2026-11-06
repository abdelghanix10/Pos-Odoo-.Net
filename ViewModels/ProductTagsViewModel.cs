using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class ProductTagsViewModel : ViewModelBase
{
    private readonly List<ProductTagRecord> _allTags = [];
    private readonly List<ProductRecord> _allProducts = [];

    [ObservableProperty]
    private ObservableCollection<ProductTagRecord> _filteredTags = [];

    [ObservableProperty]
    private ObservableCollection<ProductRecord> _associatedProducts = [];

    [ObservableProperty]
    private string _associatedProductsCountDisplay = "0 Products";

    [ObservableProperty]
    private bool _hasNoAssociatedProducts = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private ProductTagRecord? _selectedTag;

    public const int PageSize = 80;

    [ObservableProperty]
    private int _currentPage = 1;

    private readonly List<ProductTagRecord> _matchingTags = [];

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

    public void ResetToMainList()
    {
        IsDetailViewVisible = false;
        UpdatePager();
    }

    public async Task ReloadTagsAsync(bool resetDetailView = false)
    {
        try
        {
            if (resetDetailView)
            {
                IsDetailViewVisible = false;
            }

            var tags = await SqliteDatabaseService.Instance.LoadAllTagsAsync();
            _allTags.Clear();
            _allTags.AddRange(tags);

            var products = await SqliteDatabaseService.Instance.LoadAllProductsAsync();
            _allProducts.Clear();
            _allProducts.AddRange(products);

            RefreshFilter();
            UpdateAssociatedProducts();
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

        _matchingTags.Clear();
        foreach (var t in source)
        {
            _matchingTags.Add(t);
        }

        IsEmptyStateVisible = _matchingTags.Count == 0;
        CurrentPage = 1;
        ApplyPaging();

        if (IsDetailViewVisible)
        {
            if (SelectedTag != null)
            {
                var match = _matchingTags.FirstOrDefault(t => t.OdooId == SelectedTag.OdooId);
                SelectedTag = match ?? _matchingTags.FirstOrDefault();
                if (SelectedTag == null)
                {
                    IsDetailViewVisible = false;
                }
            }
        }

        UpdatePager();
        UpdateAssociatedProducts();
    }

    private void ApplyPaging()
    {
        FilteredTags.Clear();
        var pageItems = _matchingTags
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);
        foreach (var t in pageItems)
        {
            FilteredTags.Add(t);
        }
        UpdatePager();
    }

    private void UpdateAssociatedProducts()
    {
        AssociatedProducts.Clear();
        if (SelectedTag == null)
        {
            AssociatedProductsCountDisplay = "0 Products";
            HasNoAssociatedProducts = true;
            return;
        }

        var matched = _allProducts
            .Where(p => p.HasTag(SelectedTag.Name))
            .ToList();

        foreach (var prod in matched)
        {
            AssociatedProducts.Add(prod);
        }

        if (AssociatedProducts.Count > 0 && SelectedTag.ProductsCount == 0)
        {
            SelectedTag.ProductsCount = AssociatedProducts.Count;
        }

        int count = Math.Max(AssociatedProducts.Count, SelectedTag.ProductsCount);
        AssociatedProductsCountDisplay = $"{count} {(count == 1 ? "Product" : "Products")}";
        HasNoAssociatedProducts = AssociatedProducts.Count == 0;
    }

    private void UpdatePager()
    {
        if (IsDetailViewVisible)
        {
            if (_matchingTags.Count == 0 || SelectedTag == null)
            {
                PagerText = "0 / 0";
                HasPreviousRecord = false;
                HasNextRecord = false;
                return;
            }

            int index = _matchingTags.IndexOf(SelectedTag);
            if (index < 0)
            {
                index = 0;
                SelectedTag = _matchingTags[0];
            }

            PagerText = $"{index + 1} / {_matchingTags.Count}";
            HasPreviousRecord = index > 0;
            HasNextRecord = index < _matchingTags.Count - 1;
        }
        else
        {
            int total = _matchingTags.Count;
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
    private async Task SelectTag(ProductTagRecord? tag)
    {
        if (tag == null) return;
        SelectedTag = tag;

        if (_allProducts.Count == 0)
        {
            var prods = await SqliteDatabaseService.Instance.LoadAllProductsAsync();
            _allProducts.Clear();
            _allProducts.AddRange(prods);
        }

        IsDetailViewVisible = true;
        UpdatePager();
        UpdateAssociatedProducts();
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
            if (SelectedTag == null || _matchingTags.Count == 0) return;
            int index = _matchingTags.IndexOf(SelectedTag);
            if (index > 0)
            {
                SelectedTag = _matchingTags[index - 1];
                UpdatePager();
                UpdateAssociatedProducts();
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
            if (SelectedTag == null || _matchingTags.Count == 0) return;
            int index = _matchingTags.IndexOf(SelectedTag);
            if (index >= 0 && index < _matchingTags.Count - 1)
            {
                SelectedTag = _matchingTags[index + 1];
                UpdatePager();
                UpdateAssociatedProducts();
            }
        }
        else
        {
            int maxPage = (int)Math.Ceiling((double)_matchingTags.Count / PageSize);
            if (CurrentPage < maxPage)
            {
                CurrentPage++;
                ApplyPaging();
            }
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

            var tags = await OdooSyncService.Instance.FetchOdooTagsAsync(config);
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