using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class OrderManagementView : UserControl
{
    public OrdersViewModel VM => (OrdersViewModel)DataContext;

    public OrderManagementView()
    {
        InitializeComponent();
    }

    public async Task ReloadOrdersFromDatabaseAsync(string? sessionFilter = null)
    {
        if (sessionFilter != null)
        {
            VM.SetSessionFilter(sessionFilter);
        }
        await VM.ReloadOrdersAsync();
    }
}
