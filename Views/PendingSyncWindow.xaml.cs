using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OdooPosLogin;

public class PendingSyncDisplayItem
{
    public long QueueId { get; set; }
    public string SequenceDisplay => $"#{QueueId}";
    public string OperationType { get; set; } = string.Empty;
    public string OperationTitle { get; set; } = string.Empty;
    public Brush OperationBadgeBackground { get; set; } = Brushes.LightGray;
    public Brush OperationBadgeForeground { get; set; } = Brushes.Black;
    public string ShopDisplay { get; set; } = string.Empty;
    public string PrimarySummary { get; set; } = string.Empty;
    public string SecondarySummary { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string StatusDisplay { get; set; } = "Pending";
    public Brush StatusBadgeBackground { get; set; } = Brushes.LightGray;
    public Brush StatusBadgeForeground { get; set; } = Brushes.Black;
    public string? LastError { get; set; }
    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(LastError) ? Visibility.Collapsed : Visibility.Visible;
}

public partial class PendingSyncWindow : Window
{
    private readonly ObservableCollection<PendingSyncDisplayItem> _items = [];
    private readonly DispatcherTimer _autoRefreshTimer;

    public PendingSyncWindow()
    {
        InitializeComponent();
        QueueItemsControl.ItemsSource = _items;

        UpdateOnlineIndicator(OdooSyncService.Instance.IsOnline);
        OdooSyncService.Instance.OnlineStatusChanged += OnOnlineStatusChanged;
        OdooSyncService.Instance.PendingQueueFlushed += OnPendingQueueFlushed;

        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _autoRefreshTimer.Tick += async (_, _) => await LoadQueueItemsAsync();
        _autoRefreshTimer.Start();

        Closed += (_, _) =>
        {
            _autoRefreshTimer.Stop();
            OdooSyncService.Instance.OnlineStatusChanged -= OnOnlineStatusChanged;
            OdooSyncService.Instance.PendingQueueFlushed -= OnPendingQueueFlushed;
        };

        _ = LoadQueueItemsAsync();
    }

    private void OnOnlineStatusChanged(bool isOnline)
    {
        Dispatcher.Invoke(() => UpdateOnlineIndicator(isOnline));
    }

    private void OnPendingQueueFlushed(int count)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            FooterStatusText.Text = $"Uploaded {count} transaction(s) to Odoo.";
            await LoadQueueItemsAsync();
        });
    }

    private void UpdateOnlineIndicator(bool isOnline)
    {
        if (isOnline)
        {
            ConnectionDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            ConnectionStatusLabel.Text = "Odoo Online";
            ConnectionStatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
            ConnectionBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
            ConnectionBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
        }
        else
        {
            ConnectionDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            ConnectionStatusLabel.Text = "Offline (Local Mode)";
            ConnectionStatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E"));
            ConnectionBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
            ConnectionBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
        }
    }

    public async Task LoadQueueItemsAsync()
    {
        try
        {
            var rawRecords = await SqliteDatabaseService.Instance.GetPendingSyncQueueAsync();
            var configs = await SqliteDatabaseService.Instance.LoadAllPosConfigsAsync();
            var configNameById = configs.ToDictionary(c => c.OdooId, c => c.Name);

            _items.Clear();
            foreach (var rec in rawRecords)
            {
                _items.Add(BuildDisplayItem(rec, configNameById));
            }

            PendingCountText.Text = _items.Count == 1 ? "1 unsynced item" : $"{_items.Count} unsynced items";
            EmptyStateCard.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = $"Error loading queue: {ex.Message}";
        }
    }

    private static PendingSyncDisplayItem BuildDisplayItem(PendingSyncItemRecord rec, Dictionary<int, string> configNameById)
    {
        string shopName = rec.PosConfigOdooId.HasValue && configNameById.TryGetValue(rec.PosConfigOdooId.Value, out var cn)
            ? cn
            : (rec.PosConfigOdooId.HasValue ? $"POS #{rec.PosConfigOdooId.Value}" : "Point of Sale");

        string opTitle = rec.OperationType;
        string bgHex = "#F3F4F6";
        string fgHex = "#111827";
        string primary = rec.EntityUuid;
        string secondary = "";

        try
        {
            switch (rec.OperationType)
            {
                case "OPEN_SESSION":
                {
                    opTitle = "Open Register";
                    bgHex = "#E6F4F5";
                    fgHex = "#017E84";
                    var p = JsonSerializer.Deserialize<OpenSessionSyncPayload>(rec.PayloadJson);
                    if (p != null)
                    {
                        if (!string.IsNullOrWhiteSpace(p.ConfigName)) shopName = p.ConfigName;
                        primary = $"Opening Cash: $ {p.OpeningBalance:N2}  •  Cashier: {p.CashierName}";
                        secondary = string.IsNullOrWhiteSpace(p.OpeningNote)
                            ? "Opens POS session in Odoo and sets opening control balance."
                            : $"Opening Note: {p.OpeningNote}";
                    }
                    break;
                }
                case "CREATE_ORDER":
                {
                    opTitle = "POS Order";
                    bgHex = "#F3ECF1";
                    fgHex = "#714B67";
                    var p = JsonSerializer.Deserialize<CreateOrderSyncPayload>(rec.PayloadJson);
                    if (p != null)
                    {
                        primary = $"Order #{p.TrackingNumber}  (Receipt: {p.PosReference})  —  Total: $ {p.AmountTotal:N2}";
                        string itemsPreview = string.Join(", ", p.Lines.Select(l => $"{l.ProductName} x{l.Qty:0.##}"));
                        string methodsPreview = string.Join(", ", p.Payments.Select(pay => $"{pay.PaymentMethodName}: $ {pay.Amount:N2}"));
                        secondary = $"Items: {itemsPreview}   |   Payment: {methodsPreview}";
                    }
                    break;
                }
                case "CASH_IN_OUT":
                {
                    opTitle = "Cash In/Out";
                    bgHex = "#FFFBEB";
                    fgHex = "#B45309";
                    var p = JsonSerializer.Deserialize<CashInOutSyncPayload>(rec.PayloadJson);
                    if (p != null)
                    {
                        string dir = p.Amount >= 0 ? "Cash In" : "Cash Out";
                        primary = $"{dir}: $ {Math.Abs(p.Amount):N2}";
                        secondary = $"Reason: {p.Reason}";
                    }
                    break;
                }
                case "CLOSE_SESSION":
                {
                    opTitle = "Close Register";
                    bgHex = "#FEF2F2";
                    fgHex = "#DC2626";
                    var p = JsonSerializer.Deserialize<CloseSessionSyncPayload>(rec.PayloadJson);
                    if (p != null)
                    {
                        primary = $"Counted Closing Cash: $ {p.ClosingCashCount:N2}";
                        secondary = string.IsNullOrWhiteSpace(p.ClosingNote)
                            ? "Posts closing cash details and closes POS session in Odoo."
                            : $"Closing Note: {p.ClosingNote}";
                    }
                    break;
                }
            }
        }
        catch { }

        bool isFailed = string.Equals(rec.Status, "Failed", StringComparison.OrdinalIgnoreCase) || rec.RetryCount > 0;
        string statusText = isFailed ? $"Retry ({rec.RetryCount})" : "Pending";
        string statusBg = isFailed ? "#FEF2F2" : "#FFFBEB";
        string statusFg = isFailed ? "#DC2626" : "#B45309";

        return new PendingSyncDisplayItem
        {
            QueueId = rec.Id,
            OperationType = rec.OperationType,
            OperationTitle = opTitle,
            OperationBadgeBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)),
            OperationBadgeForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex)),
            ShopDisplay = shopName,
            PrimarySummary = primary,
            SecondarySummary = secondary,
            CreatedAt = rec.CreatedAt,
            StatusDisplay = statusText,
            StatusBadgeBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusBg)),
            StatusBadgeForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusFg)),
            LastError = rec.LastError
        };
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        FooterStatusText.Text = "Refreshing queue...";
        await OdooSyncService.Instance.CheckConnectivityAsync();
        await LoadQueueItemsAsync();
        FooterStatusText.Text = $"Last refreshed at {DateTime.Now:HH:mm:ss}";
    }

    private async void SyncNowBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncNowBtn.IsEnabled = false;
            SyncNowBtn.Content = "Syncing with Odoo...";
            FooterStatusText.Text = "Uploading queued transactions to Odoo in sequential order...";

            int syncedCount = await OdooSyncService.Instance.ProcessPendingQueueAsync();
            await LoadQueueItemsAsync();

            if (_items.Count == 0)
            {
                FooterStatusText.Text = $"All transactions synchronized successfully ({syncedCount} uploaded).";
            }
            else
            {
                FooterStatusText.Text = $"Uploaded {syncedCount} transaction(s). {_items.Count} item(s) still pending.";
            }
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = $"Sync error: {ex.Message}";
        }
        finally
        {
            SyncNowBtn.Content = "↻  Sync All to Odoo Now";
            SyncNowBtn.IsEnabled = true;
        }
    }

    private async void RemoveQueueItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long queueId })
        {
            var confirm = MessageBox.Show(
                $"Remove item #{queueId} from the Odoo synchronization queue?",
                "Remove Queued Operation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                await SqliteDatabaseService.Instance.MarkSyncQueueItemCompletedAsync(queueId);
                await LoadQueueItemsAsync();
                FooterStatusText.Text = $"Removed item #{queueId} from queue.";
            }
        }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}