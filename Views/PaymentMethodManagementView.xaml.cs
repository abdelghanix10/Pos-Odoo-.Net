using System.Windows.Controls;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class PaymentMethodManagementView : UserControl
{
    public PaymentMethodsViewModel VM => (PaymentMethodsViewModel)DataContext;

    public event EventHandler? PaymentMethodsUpdated;

    public PaymentMethodManagementView()
    {
        InitializeComponent();

        if (DataContext is PaymentMethodsViewModel vm)
        {
            vm.PaymentMethodsUpdated += (_, _) => PaymentMethodsUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReloadPaymentMethodsFromDatabaseAsync(bool resetDetailView = true)
    {
        await VM.ReloadPaymentMethodsAsync(resetDetailView);
    }
}