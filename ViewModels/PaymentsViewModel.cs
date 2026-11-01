using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OdooPosLogin.ViewModels;

public partial class PaymentsViewModel : ViewModelBase
{
    private readonly List<PosPaymentRecord> _allPayments = [];

    [ObservableProperty]
    private ObservableCollection<PaymentGroupRecord> _groupedPayments = [];

    [ObservableProperty]
    private ObservableCollection<PosPaymentRecord> _flatPayments = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDetailViewVisible;

    [ObservableProperty]
    private PosPaymentRecord? _selectedPayment;

    [ObservableProperty]
    private string _pagerText = "1 / 1";

    [ObservableProperty]
    private bool _hasPreviousRecord;

    [ObservableProperty]
    private bool _hasNextRecord;

    [ObservableProperty]
    private string _grandTotalText = "$ 0.00";

    [ObservableProperty]
    private string? _sessionFilter;

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

    public event EventHandler? PaymentsUpdated;

    private readonly DispatcherTimer _toastTimer;

    public PaymentsViewModel()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _ = ReloadPaymentsAsync();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();
    partial void OnSessionFilterChanged(string? value) => RefreshFilter();

    public async Task ReloadPaymentsAsync()
    {
        try
        {
            var payments = await SqliteDatabaseService.Instance.LoadAllPaymentsAsync();
            _allPayments.Clear();
            _allPayments.AddRange(payments);
            RefreshFilter();
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load payments: {ex.Message}", "Error");
        }
    }

    public void SetSessionFilter(string? sessionName)
    {
        SessionFilter = sessionName;
        RefreshFilter();
    }

    [RelayCommand]
    public void ClearSessionFilter()
    {
        SessionFilter = null;
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        string query = SearchText.Trim();
        FlatPayments.Clear();
        GroupedPayments.Clear();

        IEnumerable<PosPaymentRecord> source = _allPayments;

        if (!string.IsNullOrWhiteSpace(SessionFilter))
        {
            source = source.Where(p =>
                string.Equals(p.SessionName, SessionFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(p =>
                p.OrderName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.PaymentMethodName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.SessionName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.CashierName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.AmountDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        decimal grandTotal = 0.00m;
        var list = source.ToList();
        foreach (var p in list)
        {
            FlatPayments.Add(p);
            grandTotal += p.Amount;
        }

        GrandTotalText = $"$ {grandTotal.ToString("N2", CultureInfo.InvariantCulture)}";

        // Group by Payment Method (e.g. Cash, Bank)
        var groups = list
            .GroupBy(p => p.PaymentMethodName)
            .OrderBy(g => g.Key);

        foreach (var g in groups)
        {
            var groupRecord = new PaymentGroupRecord
            {
                GroupName = g.Key,
                Subtotal = g.Sum(p => p.Amount),
                IsExpanded = true
            };
            foreach (var item in g)
            {
                groupRecord.Payments.Add(item);
            }
            GroupedPayments.Add(groupRecord);
        }

        if (IsDetailViewVisible)
        {
            if (SelectedPayment != null && !FlatPayments.Contains(SelectedPayment))
            {
                SelectedPayment = FlatPayments.FirstOrDefault();
                if (SelectedPayment == null)
                {
                    IsDetailViewVisible = false;
                }
            }
            UpdatePager();
        }
    }

    [RelayCommand]
    public void ToggleGroup(PaymentGroupRecord? group)
    {
        if (group == null) return;
        group.IsExpanded = !group.IsExpanded;
    }

    [RelayCommand]
    public void SelectPayment(PosPaymentRecord? payment)
    {
        if (payment == null) return;
        SelectedPayment = payment;
        IsDetailViewVisible = true;
        UpdatePager();
    }

    [RelayCommand]
    public void CloseDetail()
    {
        IsDetailViewVisible = false;
        SelectedPayment = null;
    }

    private void UpdatePager()
    {
        if (SelectedPayment == null || FlatPayments.Count == 0)
        {
            PagerText = "0 / 0";
            HasPreviousRecord = false;
            HasNextRecord = false;
            return;
        }

        int index = FlatPayments.IndexOf(SelectedPayment);
        if (index < 0)
        {
            PagerText = $"1 / {FlatPayments.Count}";
            HasPreviousRecord = false;
            HasNextRecord = FlatPayments.Count > 1;
            return;
        }

        PagerText = $"{index + 1} / {FlatPayments.Count}";
        HasPreviousRecord = index > 0;
        HasNextRecord = index < FlatPayments.Count - 1;
    }

    [RelayCommand]
    public void PreviousRecord()
    {
        if (SelectedPayment == null || FlatPayments.Count == 0) return;
        int index = FlatPayments.IndexOf(SelectedPayment);
        if (index > 0)
        {
            SelectedPayment = FlatPayments[index - 1];
            UpdatePager();
        }
    }

    [RelayCommand]
    public void NextRecord()
    {
        if (SelectedPayment == null || FlatPayments.Count == 0) return;
        int index = FlatPayments.IndexOf(SelectedPayment);
        if (index >= 0 && index < FlatPayments.Count - 1)
        {
            SelectedPayment = FlatPayments[index + 1];
            UpdatePager();
        }
    }

    [RelayCommand]
    public async Task SyncPaymentsAsync()
    {
        if (IsSyncing) return;
        IsSyncing = true;
        SyncButtonText = "Syncing...";

        try
        {
            var paymentsDto = await OdooSyncService.Instance.FetchOdooPaymentsAsync(allowOfflineSimulation: true);
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooPaymentsAsync(paymentsDto);

            DateTime now = DateTime.Now;
            await SqliteDatabaseService.Instance.UpdatePaymentSyncStatusAsync(now, "Success");

            await ReloadPaymentsAsync();
            PaymentsUpdated?.Invoke(this, EventArgs.Empty);

            ShowToast($"Payments synced: {imported} added, {updated} updated, {deleted} removed.", "Success");
        }
        catch (Exception ex)
        {
            ShowToast($"Sync failed: {ex.Message}", "Error");
        }
        finally
        {
            IsSyncing = false;
            SyncButtonText = "Sync from Odoo";
        }
    }

    private void ShowToast(string message, string severity = "Success")
    {
        ToastMessage = message;
        ToastSeverity = severity;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
