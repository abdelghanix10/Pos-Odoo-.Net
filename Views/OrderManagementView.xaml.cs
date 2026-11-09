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

    public async Task ReloadOrdersFromDatabaseAsync(string? sessionFilter = null, bool resetDetailView = true)
    {
        await VM.ReloadOrdersAsync(resetDetailView);
        if (sessionFilter != null)
        {
            VM.SetSessionFilter(sessionFilter);
        }
    }
}