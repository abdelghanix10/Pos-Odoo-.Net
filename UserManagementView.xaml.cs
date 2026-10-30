using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        UserActionToastText.Text = message;
        if (isWarning)
        {
            ToastIndicatorDot.Fill = (Brush)FindResource("WarningAmberBrush");
        }
        else if (isError)
        {
            ToastIndicatorDot.Fill = (Brush)FindResource("DangerRedBrush");
        }
        else
        {
            ToastIndicatorDot.Fill = (Brush)FindResource("ActiveGreenBrush");
        }
        UserActionToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ==================== ODOO SYNCHRONIZATION ====================
    private async void SyncUsersButton_Click(object sender, RoutedEventArgs e)
    {
        await TriggerOdooSyncAsync();
    }

    public async Task TriggerOdooSyncAsync()
    {
        // 1. Verify live Odoo Online connectivity
        bool isOnline = await OdooSyncService.Instance.CheckConnectivityAsync();
        if (!isOnline)
        {
            ShowToast("Cannot synchronize: Odoo Online is unreachable. Operating in Offline (Local Mode).", isWarning: true);
            return;
        }

        try
        {
            TopSyncFromOdooBtn.IsEnabled = false;

            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            var odooUsers = await OdooSyncService.Instance.FetchOdooUsersAsync(config, allowOfflineSimulation: true);

            var (imported, updated) = await SqliteDatabaseService.Instance.UpsertOdooUsersAsync(odooUsers);
            await SqliteDatabaseService.Instance.UpdateSyncStatusAsync(DateTime.Now, $"Success ({imported + updated} users)");

            await ReloadUsersFromDatabaseAsync();
            NotifyUsersUpdated();

            ShowToast($"Sync Complete: {imported} imported, {updated} updated from Odoo.");
        }
        catch (Exception ex)
        {
            ShowToast($"Sync Failed: {ex.Message}", isError: true);
        }
        finally
        {
            TopSyncFromOdooBtn.IsEnabled = true;
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

        UserCountText.Text = $"{_filteredUsers.Count} Users";
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
        SwitchToKanbanView();
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

        UpdateFormUserPager();

        // Toggle Top Control Bar elements
        BreadcrumbFormSubPanel.Visibility = Visibility.Visible;
        FormSaveDiscardPanel.Visibility = Visibility.Visible;
        FormPagerContainer.Visibility = Visibility.Visible;
        BackToPosButton.Visibility = Visibility.Collapsed;
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
        FormPagerContainer.Visibility = Visibility.Collapsed;
        BackToPosButton.Visibility = Visibility.Visible;
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

    // ==================== USER RECORD PAGER NAVIGATION ====================
    private IList<PosUserRecord> GetCurrentNavigationList()
    {
        if (_editingUser is not null && _filteredUsers.Contains(_editingUser))
            return _filteredUsers;
        if (_filteredUsers.Count > 0)
            return _filteredUsers;
        return _allUsers;
    }

    private void UpdateFormUserPager()
    {
        var list = GetCurrentNavigationList();
        int total = list.Count;

        if (_editingUser is null)
        {
            FormPagerCountText.Text = total > 0 ? $"1 / {total}" : "-";
            FormPrevUserButton.IsEnabled = false;
            FormNextUserButton.IsEnabled = false;
            return;
        }

        int index = list.IndexOf(_editingUser);
        if (index >= 0)
        {
            FormPagerCountText.Text = $"{index + 1} / {total}";
            FormPrevUserButton.IsEnabled = index > 0;
            FormNextUserButton.IsEnabled = index < total - 1;
        }
        else
        {
            FormPagerCountText.Text = $"- / {total}";
            FormPrevUserButton.IsEnabled = false;
            FormNextUserButton.IsEnabled = false;
        }
    }

    private void FormPrevUserButton_Click(object sender, RoutedEventArgs e)
    {
        var list = GetCurrentNavigationList();
        if (list.Count == 0 || _editingUser is null) return;

        int index = list.IndexOf(_editingUser);
        if (index > 0)
        {
            OpenUserForm(list[index - 1]);
        }
    }

    private void FormNextUserButton_Click(object sender, RoutedEventArgs e)
    {
        var list = GetCurrentNavigationList();
        if (list.Count == 0 || _editingUser is null) return;

        int index = list.IndexOf(_editingUser);
        if (index >= 0 && index < list.Count - 1)
        {
            OpenUserForm(list[index + 1]);
        }
    }

    private void BackToPosButton_Click(object sender, RoutedEventArgs e)
    {
        RequestCloseToPos?.Invoke(this, EventArgs.Empty);
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
