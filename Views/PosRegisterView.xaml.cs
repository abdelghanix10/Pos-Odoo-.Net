using System.Windows.Controls;
using System.Windows.Input;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class PosRegisterView : UserControl
{
    public PosRegisterViewModel VM => (PosRegisterViewModel)DataContext;

    public PosRegisterView()
    {
        InitializeComponent();
        PreviewKeyDown += PosRegisterView_PreviewKeyDown;
    }

    private void PosRegisterView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
            return;

        if (VM.IsPosLandingScreenOpen || VM.IsOpeningControlModalOpen || VM.IsClosingRegisterModalOpen ||
            VM.IsHamburgerMenuOpen || VM.IsDraftOrdersWarningModalOpen || VM.IsNoteModalOpen)
        {
            return;
        }

        string? keyToken = null;
        if (e.Key >= Key.D0 && e.Key <= Key.D9)
            keyToken = (e.Key - Key.D0).ToString();
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            keyToken = (e.Key - Key.NumPad0).ToString();
        else if (e.Key == Key.OemPeriod || e.Key == Key.Decimal)
            keyToken = ".";
        else if (e.Key == Key.Back)
            keyToken = "⌫";

        if (keyToken == null) return;

        if (VM.IsPaymentViewOpen)
        {
            VM.PaymentNumpadKeyCommand.Execute(keyToken);
            e.Handled = true;
        }
        else if (VM.IsRegisterViewActive)
        {
            VM.NumpadKeyCommand.Execute(keyToken);
            e.Handled = true;
        }
    }
}