using System.Collections.ObjectModel;
using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class UserManagementView : UserControl
{
    public UserManagementViewModel ViewModel => (UserManagementViewModel)DataContext;

    public ObservableCollection<PosUserRecord> PosLoginUsers { get; } = [];

    public event Action? UsersUpdated;
    public event EventHandler? RequestCloseToPos;

    public UserManagementView()
    {
        InitializeComponent();

        if (DataContext is UserManagementViewModel vm)
        {
            vm.UsersUpdated += () =>
            {
                RefreshPosLoginUsers();
                UsersUpdated?.Invoke();
            };

            vm.RequestCloseToPos += (s, e) => RequestCloseToPos?.Invoke(s, e);
        }

        RefreshPosLoginUsers();
    }

    public async Task ReloadUsersFromDatabaseAsync()
    {
        await ViewModel.ReloadUsersAsync();
        RefreshPosLoginUsers();
    }

    public void NotifyUsersUpdated()
    {
        RefreshPosLoginUsers();
        UsersUpdated?.Invoke();
    }

    public void RefreshPosLoginUsers()
    {
        PosLoginUsers.Clear();
        foreach (var user in ViewModel.FilteredUsers)
        {
            if (user.IsVisibleInPos)
            {
                PosLoginUsers.Add(user);
            }
        }
    }

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        string severity = isError ? "Error" : isWarning ? "Warning" : "Success";
        ViewModel.ShowToast(message, severity);
    }
}
