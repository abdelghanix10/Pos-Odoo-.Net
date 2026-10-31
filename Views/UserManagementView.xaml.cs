using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace OdooPosLogin;

public partial class UserManagementView : UserControl
{
    private readonly List<PosUserRecord> _allUsers = [];
    private readonly ObservableCollection<PosUserRecord> _filteredUsers = [];

    public ObservableCollection<PosUserRecord> PosLoginUsers { get; } = [];
    public event Action? UsersUpdated;
    public event EventHandler? RequestCloseToPos;

    private readonly System.Windows.Threading.DispatcherTimer _toastTimer;
    private PosUserRecord? _editingUser;

    public UserManagementView()
    {
        InitializeComponent();

        _toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            UserActionToastBanner.Visibility = Visibility.Collapsed;
        };

        UsersKanbanItemsControl.ItemsSource = _filteredUsers;
        _ = ReloadUsersFromDatabaseAsync();
    }

    public async Task ReloadUsersFromDatabaseAsync()
    {
        var users = await SqliteDatabaseService.Instance.LoadAllUsersAsync();
        _allUsers.Clear();
        _allUsers.AddRange(users);
        RefreshKanbanFilter();
        RefreshPosLoginUsers();
    }

    private Brush GetSafeBrush(string resourceKey, Color fallbackColor)
    {
        if (TryFindResource(resourceKey) is Brush brush)
            return brush;
        return new SolidColorBrush(fallbackColor);
    }

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        UserActionToastText.Text = message;
        if (isWarning)
        {
            ToastIndicatorDot.Fill = GetSafeBrush("WarningAmberBrush", Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else if (isError)
        {
            ToastIndicatorDot.Fill = GetSafeBrush("DangerRedBrush", Color.FromRgb(0xDC, 0x26, 0x26));
        }
        else
        {
            ToastIndicatorDot.Fill = GetSafeBrush("ActiveGreenBrush", Color.FromRgb(0x10, 0xB9, 0x81));
        }
        UserActionToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ==================== ODOO SYNCHRONIZATION ====================
    private void SetSyncLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            TopSyncFromOdooBtn.IsEnabled = false;
            TopSyncButtonText.Text = "Syncing...";
            var spinAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            UserSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            UserSyncIconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            UserSyncIconRotation.Angle = 0;
            TopSyncButtonText.Text = "Sync from Odoo";
            TopSyncFromOdooBtn.IsEnabled = true;
        }
    }

    private async void SyncUsersButton_Click(object sender, RoutedEventArgs e)
    {
        await TriggerOdooSyncAsync();
    }

    public async Task TriggerOdooSyncAsync()
    {
        try
        {
            SetSyncLoadingState(true);

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                ShowToast("Cannot synchronize: Odoo Server URL is not configured. Please open Settings.", isWarning: true);
                return;
            }

            // Fetch live users directly from Odoo
            var odooUsers = await OdooSyncService.Instance.FetchOdooUsersAsync(config, allowOfflineSimulation: false);

            // Reconcile users in local SQLite: upsert users present in Odoo, DELETE users NOT in Odoo
            var (imported, updated, deleted) = await SqliteDatabaseService.Instance.UpsertOdooUsersAsync(odooUsers);

            string statusMsg = deleted > 0
                ? $"Sync Complete: {imported} imported, {updated} updated, {deleted} removed."
                : $"Sync Complete: {imported} imported, {updated} updated.";

            await SqliteDatabaseService.Instance.UpdateSyncStatusAsync(DateTime.Now, statusMsg);

            await ReloadUsersFromDatabaseAsync();
            NotifyUsersUpdated();

            ShowToast(statusMsg);
        }
        catch (Exception ex)
        {
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetSyncLoadingState(false);
        }
    }

    public void RefreshPosLoginUsers()
    {
        PosLoginUsers.Clear();
        foreach (PosUserRecord user in _allUsers.Where(u => u.IsVisibleInPos))
        {
            PosLoginUsers.Add(user);
        }
    }

    public void NotifyUsersUpdated()
    {
        RefreshKanbanFilter();
        RefreshPosLoginUsers();
        UsersUpdated?.Invoke();
    }

    // ==================== KANBAN SEARCH & FILTER ====================
    private void RefreshKanbanFilter()
    {
        string query = SearchUsersTextBox.Text.Trim();
        _filteredUsers.Clear();

        IEnumerable<PosUserRecord> source = _allUsers;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(u =>
                u.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                u.Login.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var user in source)
        {
            _filteredUsers.Add(user);
        }
    }

    private void SearchUsersTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(SearchUsersTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshKanbanFilter();
    }

    // ==================== NAVIGATION: KANBAN <-> DETAIL VIEW ====================
    private void KanbanUserCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PosUserRecord selectedUser })
        {
            OpenUserForm(selectedUser);
        }
    }

    private void BreadcrumbUsers_Click(object sender, RoutedEventArgs e)
    {
        if (FormViewContainer.Visibility == Visibility.Visible)
        {
            SwitchToKanbanView();
        }
        else
        {
            RequestCloseToPos?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OpenUserForm(PosUserRecord userToEdit)
    {
        _editingUser = userToEdit;

        // Display Read-Only Profile Information from Odoo
        BreadcrumbCurrentTitle.Text = userToEdit.Name.Trim();
        FormNameTextBox.Text = userToEdit.Name.Trim();
        FormLoginTextBox.Text = userToEdit.Login;
        FormEmailTextBox.Text = string.IsNullOrWhiteSpace(userToEdit.Email) ? "-" : userToEdit.Email;
        FormPhoneTextBox.Text = string.IsNullOrWhiteSpace(userToEdit.Phone) ? "-" : userToEdit.Phone;

        // Avatar Image Display
        if (userToEdit.AvatarBitmap is not null)
        {
            FormAvatarImage.Source = userToEdit.AvatarBitmap;
            FormAvatarImage.Visibility = Visibility.Visible;
        }
        else
        {
            FormAvatarImage.Source = null;
            FormAvatarImage.Visibility = Visibility.Collapsed;
        }

        // Local Editable POS Credentials
        FormPasswordBox.Password = userToEdit.Password;
        FormPinPasswordBox.Password = userToEdit.Pin;
        FormVisibleInPosCheckBox.IsChecked = userToEdit.IsVisibleInPos;

        // Toggle Top Control Bar elements
        BreadcrumbFormSubPanel.Visibility = Visibility.Visible;
        FormSaveDiscardPanel.Visibility = Visibility.Visible;
        TopSyncFromOdooBtn.Visibility = Visibility.Collapsed;
        KanbanSearchBarContainer.Visibility = Visibility.Collapsed;

        // Toggle Views
        KanbanViewContainer.Visibility = Visibility.Collapsed;
        FormViewContainer.Visibility = Visibility.Visible;
    }

    private void SwitchToKanbanView()
    {
        BreadcrumbFormSubPanel.Visibility = Visibility.Collapsed;
        FormSaveDiscardPanel.Visibility = Visibility.Collapsed;
        TopSyncFromOdooBtn.Visibility = Visibility.Visible;
        KanbanSearchBarContainer.Visibility = Visibility.Visible;

        FormViewContainer.Visibility = Visibility.Collapsed;
        KanbanViewContainer.Visibility = Visibility.Visible;

        RefreshKanbanFilter();
    }

    // ==================== SAVE LOCAL CREDENTIALS & DISCARD ====================
    private async void SaveUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingUser is null) return;

        // Only update local POS security credentials
        _editingUser.Password = FormPasswordBox.Password;
        _editingUser.Pin = FormPinPasswordBox.Password;
        _editingUser.IsVisibleInPos = FormVisibleInPosCheckBox.IsChecked == true;

        await SqliteDatabaseService.Instance.SaveUserRecordAsync(_editingUser);

        SwitchToKanbanView();
        NotifyUsersUpdated();
        ShowToast($"Credentials for \"{_editingUser.Name.Trim()}\" updated successfully.");
    }

    private void DiscardUserButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToKanbanView();
    }

    // ==================== PASSWORD & PIN INPUT HANDLERS ====================
    private void FormPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        PasswordPlaceholderText.Visibility = string.IsNullOrEmpty(FormPasswordBox.Password)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void FormPinPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        PinPlaceholderText.Visibility = string.IsNullOrEmpty(FormPinPasswordBox.Password)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex NumericRegex();

    private void FormPinPasswordBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !NumericRegex().IsMatch(e.Text);
    }

    private void FormPinPasswordBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            string pastedText = (string)e.DataObject.GetData(typeof(string))!;
            if (!NumericRegex().IsMatch(pastedText))
            {
                e.CancelCommand();
            }
        }
        else
        {
            e.CancelCommand();
        }
    }
}

// ==================== USER MODEL FOR KANBAN & FORM ====================
public class PosUserRecord
{
    public int? OdooId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = "••••••••";
    public string Pin { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed";
    public bool IsPurpleTheme { get; set; } = true;
    public BitmapImage? AvatarBitmap { get; set; }
    public string? AvatarBase64 { get; set; }
    public bool IsVisibleInPos { get; set; } = true;

    public Visibility PosBadgeVisibility => IsVisibleInPos ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InitialsVisibility => AvatarBitmap is null ? Visibility.Visible : Visibility.Collapsed;

    public string Initials
    {
        get
        {
            string[] parts = Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "U";
            if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
            return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
        }
    }

    public Visibility HasCustomImageVisibility =>
        AvatarBitmap is not null ? Visibility.Visible : Visibility.Collapsed;

    public Brush AvatarSoftBrush => IsPurpleTheme
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3ECF1"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F4F5"));

    public Brush AvatarAccentBrush => IsPurpleTheme
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#714B67"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#017E84"));

    public Brush StatusDotBrush => Status switch
    {
        "Confirmed" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
        "Invited" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"))
    };
}
