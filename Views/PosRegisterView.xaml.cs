using System.Threading.Tasks;
using System.Windows;
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

        if (VM.IsRegisterViewActive && !VM.IsOpeningControlModalOpen && !VM.IsClosingRegisterModalOpen && !VM.IsHamburgerMenuOpen && !VM.IsDraftOrdersWarningModalOpen)
        {
            if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                VM.NumpadKeyCommand.Execute((e.Key - Key.D0).ToString());
                e.Handled = true;
            }
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                VM.NumpadKeyCommand.Execute((e.Key - Key.NumPad0).ToString());
                e.Handled = true;
            }
            else if (e.Key == Key.OemPeriod || e.Key == Key.Decimal)
            {
                VM.NumpadKeyCommand.Execute(".");
                e.Handled = true;
            }
            else if (e.Key == Key.Back)
            {
                VM.NumpadKeyCommand.Execute("⌫");
                e.Handled = true;
            }
        }
    }
}
