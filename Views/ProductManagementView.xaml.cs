using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OdooPosLogin.ViewModels;

namespace OdooPosLogin;

public partial class ProductManagementView : UserControl
{
    public ProductsViewModel ViewModel => (ProductsViewModel)DataContext;

    public event EventHandler? ProductsUpdated;

    private Point _dragStartPoint;
    private ProductRecord? _draggedProduct;
    private bool _isDragging;

    public ProductManagementView()
    {
        InitializeComponent();

        if (DataContext is ProductsViewModel vm)
        {
            vm.ProductsUpdated += (_, _) => ProductsUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReloadProductsFromDatabaseAsync(bool resetDetailView = true)
    {
        await ViewModel.ReloadProductsAsync(resetDetailView);
    }

    public void ShowToast(string message, bool isError = false, bool isWarning = false)
    {
        string severity = isError ? "Error" : isWarning ? "Warning" : "Success";
        ViewModel.ShowToast(message, severity);
    }

    private void ProductItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedProduct = (sender as FrameworkElement)?.DataContext as ProductRecord;
        _isDragging = false;
    }

    private void ProductItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedProduct == null || _isDragging)
            return;

        Point currentPos = e.GetPosition(null);
        Vector diff = _dragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isDragging = true;
            var productToMove = _draggedProduct;
            _draggedProduct = null;

            DragDrop.DoDragDrop(
                (DependencyObject)sender,
                new DataObject(typeof(ProductRecord), productToMove),
                DragDropEffects.Move);

            _isDragging = false;
        }
    }

    private void ProductItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging && (sender as FrameworkElement)?.DataContext is ProductRecord product)
        {
            ViewModel.SelectProductCommand.Execute(product);
        }
        _draggedProduct = null;
        _isDragging = false;
    }

    private void ProductItem_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ProductRecord)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        if (sender is Border bdr)
        {
            bdr.BorderBrush = BrushCache.Get("#71639E");
            bdr.BorderThickness = bdr.Name == "RowBdr"
                ? new Thickness(0, 2.5, 0, 1)
                : new Thickness(2);
            bdr.Background = BrushCache.Get("#F6F4FA");
        }
        e.Handled = true;
    }

    private void ProductItem_DragLeave(object sender, DragEventArgs e)
    {
        ResetItemVisual(sender);
    }

    private async void ProductItem_Drop(object sender, DragEventArgs e)
    {
        ResetItemVisual(sender);

        if (e.Data.GetDataPresent(typeof(ProductRecord)) &&
            e.Data.GetData(typeof(ProductRecord)) is ProductRecord source &&
            (sender as FrameworkElement)?.DataContext is ProductRecord target)
        {
            await ViewModel.MoveProductAsync(source, target);
        }
        e.Handled = true;
    }

    private static void ResetItemVisual(object sender)
    {
        if (sender is Border bdr)
        {
            bdr.BorderBrush = BrushCache.Get("#E5E7EB");
            bdr.BorderThickness = bdr.Name == "RowBdr"
                ? new Thickness(0, 0, 0, 1)
                : new Thickness(1);
            bdr.Background = BrushCache.Get("#FFFFFF");
        }
    }
}