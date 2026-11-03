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

    public async Task ReloadSessionsFromDatabaseAsync(bool resetDetailView = true)
    {
        await VM.ReloadSessionsAsync(resetDetailView);
    }
}