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

    private void ProductsScrollViewer_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (DataContext is not PosRegisterViewModel vm) return;
        double available = e.NewSize.Width - 34.0; // Padding + scrollbar margin
        if (available <= 180.0) return;

        int cols = Math.Max(2, (int)Math.Floor(available / 175.0));
        double cardWidth = Math.Floor((available / cols) - 8.5);
        vm.ProductCardWidth = Math.Clamp(cardWidth, 140.0, 240.0);
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

    // Clicking anywhere on the Change (confirmation) screen background continues the flow.
    // Clicks that originate on a button (Print / Continue) are left to the button itself.
    private void ChangeScreenBackground_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!VM.IsPaymentConfirmationOpen) return;
        if (IsInsideButton(e.OriginalSource as System.Windows.DependencyObject)) return;

        VM.ContinueAfterPaymentCommand.Execute(null);
        e.Handled = true;
    }

    private static bool IsInsideButton(System.Windows.DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.Button) return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    // Clicking the dark backdrop outside the 3-dot session popup closes it.
    private void HamburgerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource) && VM.IsHamburgerMenuOpen)
        {
            VM.CloseHamburgerMenuCommand.Execute(null);
            e.Handled = true;
        }
    }
}