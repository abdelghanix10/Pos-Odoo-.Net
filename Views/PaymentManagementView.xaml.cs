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

    public async Task ReloadPaymentsFromDatabaseAsync(string? sessionFilter = null, bool resetDetailView = true)
    {
        await VM.ReloadPaymentsAsync(resetDetailView);
        if (sessionFilter != null)
        {
            VM.SetSessionFilter(sessionFilter);
        }
    }

    private void SessionTextBox_GotFocus(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.SelectAll();
        }
        VM.IsSessionDropdownOpen = true;
        VM.UpdateSessionSuggestions();
    }

    private void SessionTextBox_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!VM.IsSessionDropdownOpen)
        {
            VM.IsSessionDropdownOpen = true;
            VM.UpdateSessionSuggestions();
        }
    }

    private void SessionTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            VM.CommitSessionSearch();
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            VM.IsSessionDropdownOpen = false;
        }
    }
}