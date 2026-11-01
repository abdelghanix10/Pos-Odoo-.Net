using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class PaymentManagementView : UserControl
{
    public PaymentsViewModel VM => (PaymentsViewModel)DataContext;

    public PaymentManagementView()
    {
        InitializeComponent();
    }

    public async Task ReloadPaymentsFromDatabaseAsync(string? sessionFilter = null)
    {
        if (sessionFilter != null)
        {
            VM.SetSessionFilter(sessionFilter);
        }
        await VM.ReloadPaymentsAsync();
    }
}
