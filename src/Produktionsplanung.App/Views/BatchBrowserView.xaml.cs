using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class BatchBrowserView : UserControl
{
    public BatchBrowserView() : this("Heute") { }
    public BatchBrowserView(string mode, int? articleId = null, bool archive = false)
    {
        InitializeComponent();
        DataContext = new BatchBrowserViewModel(mode, articleId, archive);
        ArchiveFilters.Visibility = archive ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => ((BatchBrowserViewModel)DataContext).Refresh();
    }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BatchRow row)
            (Window.GetWindow(this) as MainWindow)?.OpenBatch(row.Id);
    }
    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ((BatchBrowserViewModel)DataContext).SelectedBatch is not { } row) return;
        (Window.GetWindow(this) as MainWindow)?.OpenBatch(row.Id);
        e.Handled = true;
    }
    private void NewBatch_Click(object sender, RoutedEventArgs e)
    {
        var vm = (BatchBrowserViewModel)DataContext;
        (Window.GetWindow(this) as MainWindow)?.CreateBatch(vm.FixedArticleId ?? vm.SelectedArticle?.Id);
        vm.Refresh();
    }

    private void BatchRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow { DataContext: BatchRow row } gridRow)
            return;
        gridRow.IsSelected = true;
        ((BatchBrowserViewModel)DataContext).SelectedBatch = row;
    }

    private static BatchRow? ContextBatch(object sender)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu contextMenu ||
            contextMenu.PlacementTarget is not DataGridRow { DataContext: BatchRow row })
            return null;
        return row;
    }

    private MainWindow? Host => Window.GetWindow(this) as MainWindow;

    private void OpenContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextBatch(sender) is { } row) Host?.OpenBatch(row.Id);
    }

    private void EditContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextBatch(sender) is { } row) Host?.OpenProductionOrder(row.Id);
    }

    private void ControlContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextBatch(sender) is { } row) Host?.OpenManufacturingControl(row.Id);
    }

    private void ActualContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextBatch(sender) is { } row) Host?.OpenProductionActual(row.Id);
    }

    private void ArticlesContext_Click(object sender, RoutedEventArgs e) => Host?.OpenArticles();

    private void TrashContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextBatch(sender) is not { } row || !SessionService.IsPlannerOrAdmin)
            return;

        if (MessageBox.Show(
                Window.GetWindow(this),
                $"Charge {row.BatchLabel} / Auftrag {row.OrderNumber} wirklich in den Papierkorb verschieben?\n\nDie gesamte Produktionshistorie bleibt erhalten.",
                "Charge in Papierkorb",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            RecycleBinService.MoveProductionOrderToTrash(row.Id);
            var vm = (BatchBrowserViewModel)DataContext;
            vm.Refresh();
            vm.Message = "Charge in den Papierkorb verschoben.";
        }
        catch (Exception ex)
        {
            ((BatchBrowserViewModel)DataContext).Message = $"Charge konnte nicht in den Papierkorb verschoben werden: {ex.Message}";
        }
    }
}
