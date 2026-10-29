using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

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
    private BitmapImage? _stagedAvatarBitmap;
    private string _stagedStatus = "Confirmed";

    public UserManagementView()
    {
        InitializeComponent();

        _toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            UserActionToastBanner.Visibility = Visibility.Collapsed;
        };

        SeedDefaultOdooUsers();
        UsersKanbanItemsControl.ItemsSource = _filteredUsers;
        RefreshKanbanFilter();
        RefreshPosLoginUsers();
    }

    public void ShowToast(string message, bool isDelete = false)
    {
        UserActionToastText.Text = message;
        ToastIndicatorDot.Fill = isDelete
            ? (Brush)FindResource("DangerRedBrush")
            : (Brush)FindResource("ActiveGreenBrush");
        UserActionToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void SeedDefaultOdooUsers()
    {
        _allUsers.AddRange(
        [
            new PosUserRecord
            {
                Name = "Mitchell Admin",
                Login = "admin@yourcompany.com",
                Email = "admin@yourcompany.com",
                Phone = "+1 (555) 234-5678",
                Pin = "1234",
                Status = "Confirmed",
                IsPurpleTheme = true,
                IsVisibleInPos = true
            },
            new PosUserRecord
            {
                Name = "Marc Demo",
                Login = "marc.demo@yourcompany.com",
                Email = "marc.demo@yourcompany.com",
                Phone = "+1 (555) 876-5432",
                Pin = "1234",
                Status = "Confirmed",
                IsPurpleTheme = false,
                IsVisibleInPos = true
            },
            new PosUserRecord
            {
                Name = "Abigail Peterson",
                Login = "abigail.p@yourcompany.com",
                Email = "abigail.p@yourcompany.com",
                Phone = "+1 (555) 392-1100",
                Pin = "2580",
                Status = "Confirmed",
                IsPurpleTheme = true,
                IsVisibleInPos = true
            },
            new PosUserRecord
            {
                Name = "Joel Willis",
                Login = "joel.willis@yourcompany.com",
                Email = "joel.willis@yourcompany.com",
                Phone = "+1 (555) 601-9940",
                Pin = "9999",
                Status = "Confirmed",
                IsPurpleTheme = false,
                IsVisibleInPos = true
            },
            new PosUserRecord
            {
                Name = "Anita Oliver",
                Login = "anita.oliver@yourcompany.com",
                Email = "anita.oliver@yourcompany.com",
                Phone = "+1 (555) 482-7721",
                Pin = "4321",
                Status = "Invited",
                IsPurpleTheme = true,
                IsVisibleInPos = false
            },
            new PosUserRecord
            {
                Name = "Randall Lewis",
                Login = "randall.l@yourcompany.com",
                Email = "randall.l@yourcompany.com",
                Phone = "+1 (555) 910-3318",
                Pin = "0000",
                Status = "Confirmed",
                IsPurpleTheme = false,
                IsVisibleInPos = false
            }
        ]);
    }

    // ==================== KANBAN SEARCH & FILTER ====================
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

    private void RefreshKanbanFilter()
    {
        string query = SearchUsersTextBox.Text.Trim();
        _filteredUsers.Clear();

        foreach (PosUserRecord user in _allUsers)
        {
            if (!string.IsNullOrEmpty(query) &&
                !user.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !user.Login.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

    // ==================== NAVIGATION: KANBAN <-> FORM ====================
    private void NewUserButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUserForm(null);
    }

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

    private void OpenUserForm(PosUserRecord? userToEdit)
    {
        _editingUser = userToEdit;
        ValidationBanner.Visibility = Visibility.Collapsed;
        ResetValidationBorders();

        if (userToEdit is null)
        {
            // Create Mode
            BreadcrumbCurrentTitle.Text = "New";
            FormNameTextBox.Text = string.Empty;
            FormLoginTextBox.Text = string.Empty;
            FormEmailTextBox.Text = string.Empty;
            FormPhoneTextBox.Text = string.Empty;
            FormPasswordBox.Password = string.Empty;
            FormPinPasswordBox.Password = string.Empty;
            FormVisibleInPosCheckBox.IsChecked = true;
            DeleteUserButton.Visibility = Visibility.Collapsed;
            _stagedAvatarBitmap = null;
            _stagedStatus = "Invited";
        }
        else
        {
            // Edit Mode
            BreadcrumbCurrentTitle.Text = userToEdit.Name.Trim();
            FormNameTextBox.Text = userToEdit.Name.Trim();
            FormLoginTextBox.Text = userToEdit.Login;
            FormEmailTextBox.Text = userToEdit.Email;
            FormPhoneTextBox.Text = userToEdit.Phone;
            FormPasswordBox.Password = userToEdit.Password;
            FormPinPasswordBox.Password = userToEdit.Pin;
            FormVisibleInPosCheckBox.IsChecked = userToEdit.IsVisibleInPos;
            DeleteUserButton.Visibility = Visibility.Visible;
            _stagedAvatarBitmap = userToEdit.AvatarBitmap;
            _stagedStatus = userToEdit.Status;
        }

        UpdateAvatarUploaderPreview();
        UpdateFormUserPager();

        // Toggle Shared Top Control Bar states
        BreadcrumbFormSubPanel.Visibility = Visibility.Visible;
        FormSaveDiscardPanel.Visibility = Visibility.Visible;
        FormPagerContainer.Visibility = Visibility.Visible;
        BackToPosButton.Visibility = Visibility.Collapsed;

        KanbanSearchBarContainer.Visibility = Visibility.Collapsed;

        // Toggle Main View
        KanbanViewContainer.Visibility = Visibility.Collapsed;
        FormViewContainer.Visibility = Visibility.Visible;

        FormNameTextBox.Focus();
    }

    private void SwitchToKanbanView()
    {
        BreadcrumbFormSubPanel.Visibility = Visibility.Collapsed;
        FormSaveDiscardPanel.Visibility = Visibility.Collapsed;
        FormPagerContainer.Visibility = Visibility.Collapsed;
        BackToPosButton.Visibility = Visibility.Visible;

        KanbanSearchBarContainer.Visibility = Visibility.Visible;

        FormViewContainer.Visibility = Visibility.Collapsed;
        KanbanViewContainer.Visibility = Visibility.Visible;

        RefreshKanbanFilter();
    }

    // ==================== SAVE / DISCARD & VALIDATION ====================
    private void SaveUserButton_Click(object sender, RoutedEventArgs e)
    {
        string name = FormNameTextBox.Text.Trim();
        string login = FormLoginTextBox.Text.Trim();

        bool isNameMissing = string.IsNullOrWhiteSpace(name);
        bool isLoginMissing = string.IsNullOrWhiteSpace(login);

        if (isNameMissing || isLoginMissing)
        {
            Brush dangerBrush = (Brush)FindResource("DangerRedBrush");
            if (isNameMissing)
                NameUnderlineBorder.BorderBrush = dangerBrush;
            if (isLoginMissing)
                LoginInputBorder.BorderBrush = dangerBrush;

            ValidationBanner.Visibility = Visibility.Visible;
            return;
        }

        if (_editingUser is null)
        {
            var newUser = new PosUserRecord
            {
                Name = name,
                Login = login,
                Email = string.IsNullOrWhiteSpace(FormEmailTextBox.Text) ? login : FormEmailTextBox.Text.Trim(),
                Phone = FormPhoneTextBox.Text.Trim(),
                Password = FormPasswordBox.Password,
                Pin = FormPinPasswordBox.Password,
                Status = _stagedStatus,
                AvatarBitmap = _stagedAvatarBitmap,
                IsVisibleInPos = FormVisibleInPosCheckBox.IsChecked == true,
                IsPurpleTheme = _allUsers.Count % 2 == 0
            };
            _allUsers.Insert(0, newUser);
        }
        else
        {
            _editingUser.Name = name;
            _editingUser.Login = login;
            _editingUser.Email = FormEmailTextBox.Text.Trim();
            _editingUser.Phone = FormPhoneTextBox.Text.Trim();
            _editingUser.Password = FormPasswordBox.Password;
            _editingUser.Pin = FormPinPasswordBox.Password;
            _editingUser.Status = _stagedStatus;
            _editingUser.AvatarBitmap = _stagedAvatarBitmap;
            _editingUser.IsVisibleInPos = FormVisibleInPosCheckBox.IsChecked == true;
        }

        SwitchToKanbanView();
        NotifyUsersUpdated();
        ShowToast($"User \"{name}\" saved successfully.");
    }

    private void DiscardUserButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToKanbanView();
    }

    // ==================== FOCUS & PLACEHOLDER HANDLERS ====================
    private void RequiredField_GotFocus(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, FormNameTextBox))
        {
            NameUnderlineBorder.BorderBrush = (Brush)FindResource("OdooPurpleBrush");
        }
        else if (ReferenceEquals(sender, FormLoginTextBox))
        {
            LoginInputBorder.BorderBrush = (Brush)FindResource("OdooPurpleBrush");
        }
    }

    private void RequiredField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, FormNameTextBox))
        {
            NameUnderlineBorder.BorderBrush = string.IsNullOrWhiteSpace(FormNameTextBox.Text)
                ? (Brush)FindResource("DangerRedBrush")
                : (Brush)FindResource("InputBorderBrush");
        }
        else if (ReferenceEquals(sender, FormLoginTextBox))
        {
            LoginInputBorder.BorderBrush = string.IsNullOrWhiteSpace(FormLoginTextBox.Text)
                ? (Brush)FindResource("DangerRedBrush")
                : (Brush)FindResource("SubtleBorderBrush");
        }
    }

    private void ResetValidationBorders()
    {
        NameUnderlineBorder.BorderBrush = (Brush)FindResource("InputBorderBrush");
        LoginInputBorder.BorderBrush = (Brush)FindResource("SubtleBorderBrush");
    }

    private void FormNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NamePlaceholderText.Visibility = string.IsNullOrEmpty(FormNameTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(FormNameTextBox.Text))
        {
            BreadcrumbCurrentTitle.Text = FormNameTextBox.Text.Trim();
        }
    }

    private void FormLoginTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        LoginPlaceholderText.Visibility = string.IsNullOrEmpty(FormLoginTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void FormEmailTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        EmailPlaceholderText.Visibility = string.IsNullOrEmpty(FormEmailTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void FormPhoneTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PhonePlaceholderText.Visibility = string.IsNullOrEmpty(FormPhoneTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

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

    // Restrict PIN to Numeric Digits Only
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

    // ==================== AVATAR UPLOADER (96x96) ====================
    private void UploadAvatarButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select User Avatar Image",
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (dialog.ShowDialog() == true)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(dialog.FileName, UriKind.Absolute);
            bitmap.EndInit();

            _stagedAvatarBitmap = bitmap;
            UpdateAvatarUploaderPreview();
        }
    }

    private void UpdateAvatarUploaderPreview()
    {
        // Locate template elements inside the Upload button if applied
        if (FormViewContainer.FindName("AvatarUploadBtn") is Button) { }

        // Apply via visual tree helper on the template or direct binding
        ApplyAvatarToTemplate();
    }

    private void ApplyAvatarToTemplate()
    {
        // Find the Rectangle and ImageBrush inside the 96x96 Upload Button template
        if (TryFindChildByName<Rectangle>(FormViewContainer, "FormAvatarPreviewRect") is Rectangle previewRect &&
            previewRect.Fill is ImageBrush brush)
        {
            if (_stagedAvatarBitmap is not null)
            {
                brush.ImageSource = _stagedAvatarBitmap;
                previewRect.Visibility = Visibility.Visible;
            }
            else
            {
                previewRect.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static T? TryFindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;

            T? result = TryFindChildByName<T>(child, name);
            if (result is not null)
                return result;
        }
        return null;
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
            FormPagerCountText.Text = total > 0 ? $"New / {total}" : "New";
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

    private void DeleteUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingUser is null) return;

        DeleteModalPromptText.Text = $"Are you sure you want to delete user \"{_editingUser.Name.Trim()}\"? This action cannot be undone.";
        DeleteConfirmModalOverlay.Visibility = Visibility.Visible;
    }

    private void CancelDeleteModal_Click(object sender, RoutedEventArgs e)
    {
        DeleteConfirmModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void ConfirmDeleteUser_Click(object sender, RoutedEventArgs e)
    {
        if (_editingUser is null) return;

        string deletedName = _editingUser.Name.Trim();
        var userToDelete = _editingUser;

        _allUsers.Remove(userToDelete);
        _editingUser = null;

        DeleteConfirmModalOverlay.Visibility = Visibility.Collapsed;
        SwitchToKanbanView();
        NotifyUsersUpdated();

        ShowToast($"User \"{deletedName}\" has been deleted.", isDelete: true);
    }

    private void BackToPosButton_Click(object sender, RoutedEventArgs e)
    {
        RequestCloseToPos?.Invoke(this, EventArgs.Empty);
    }
}

// ==================== USER MODEL FOR KANBAN & FORM ====================
public class PosUserRecord
{
    public string Name { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = "••••••••";
    public string Pin { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed";
    public bool IsPurpleTheme { get; set; } = true;
    public BitmapImage? AvatarBitmap { get; set; }
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

    // Green (#10B981) for Active/Confirmed, Amber for Invited, Muted Gray for Inactive
    public Brush StatusDotBrush => Status switch
    {
        "Confirmed" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
        "Invited" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"))
    };
}