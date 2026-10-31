using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class ProductAttributesViewModel : ViewModelBase
{
    private readonly List<ProductAttributeRecord> _allAttributes = [];

    [ObservableProperty]
    private ObservableCollection<ProductAttributeRecord> _filteredAttributes = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private ProductAttributeRecord? _selectedAttribute;

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

    public event EventHandler? AttributesUpdated;

    private readonly DispatcherTimer _toastTimer;

    public ProductAttributesViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadAttributesAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public async Task ReloadAttributesAsync()
    {
        try
        {
            var attributes = await SqliteDatabaseService.Instance.LoadAllAttributesAsync();
            _allAttributes.Clear();
            _allAttributes.AddRange(attributes);
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load attributes: {ex.Message}", "Error");
        }
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FilteredAttributes.Clear();

        IEnumerable<ProductAttributeRecord> source = _allAttributes;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.DisplayTypeFormatted.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.CreateVariantFormatted.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var a in source)
        {
            FilteredAttributes.Add(a);
        }

        if (IsDetailViewVisible)
        {
            UpdatePager();
        }
    }

    private void UpdatePager()
    {
        if (FilteredAttributes.Count == 0 || SelectedAttribute == null)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FilteredAttributes.IndexOf(SelectedAttribute);
        if (index < 0)
        {
            index = 0;
            SelectedAttribute = FilteredAttributes[0];
        }

        PagerText = $"{index + 1} / {FilteredAttributes.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FilteredAttributes.Count - 1;
    }

    [RelayCommand]
    private void SelectAttribute(ProductAttributeRecord? attribute)
    {
        if (attribute == null) return;
        SelectedAttribute = attribute;
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
        if (SelectedAttribute == null || FilteredAttributes.Count == 0) return;
        int index = FilteredAttributes.IndexOf(SelectedAttribute);
        if (index > 0)
        {
            SelectAttribute(FilteredAttributes[index - 1]);
        }
    }

    [RelayCommand]
    private void NextRecord()
    {
        if (SelectedAttribute == null || FilteredAttributes.Count == 0) return;
        int index = FilteredAttributes.IndexOf(SelectedAttribute);
        if (index >= 0 && index < FilteredAttributes.Count - 1)
        {
            SelectAttribute(FilteredAttributes[index + 1]);
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

            var (attrs, vals) = await OdooSyncService.Instance.FetchOdooAttributesAsync(config, allowOfflineSimulation: true);
            var (aImp, aUpd, aDel, vImp, vUpd, vDel) = await SqliteDatabaseService.Instance.UpsertOdooAttributesAsync(attrs, vals);
            await SqliteDatabaseService.Instance.UpdateAttributeSyncStatusAsync(DateTime.Now, "Success");

            await ReloadAttributesAsync();
            AttributesUpdated?.Invoke(this, EventArgs.Empty);

            string summary = $"Sync Complete: {aImp + aUpd} attributes, {vImp + vUpd} values";
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
