using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class SessionManagementView : UserControl
{
    public SessionsViewModel VM => (SessionsViewModel)DataContext;

    public SessionManagementView()
    {
        InitializeComponent();
    }

    public async Task ReloadSessionsFromDatabaseAsync()
    {
        await VM.ReloadSessionsAsync();
    }
}
