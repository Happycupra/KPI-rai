using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ProductionActualView : UserControl, IUnsavedChangesAware
{
    private readonly ProductionActualViewModel viewModel;
    private string baseline = string.Empty;

    public ProductionActualView(int? orderId = null)
    {
        InitializeComponent();
        viewModel = new ProductionActualViewModel();
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        if (orderId.HasValue) viewModel.FocusOrder(orderId.Value);
        CaptureBaseline();
    }

    public bool HasUnsavedChanges => baseline != BuildSnapshot();
    public string UnsavedChangesDescription => "Ist-Produktion / OEE";

    public bool TrySaveChanges()
    {
        viewModel.SaveCommand.Execute(null);
        if (!string.Equals(viewModel.StatusMessage, "Ist-Produktion der konkreten Auftragsschicht gespeichert.", StringComparison.Ordinal))
            return false;

        if (HasPendingDowntimeDraft())
        {
            if (viewModel.DowntimeMinutes <= 0)
                return false;

            viewModel.AddDowntimeCommand.Execute(null);
            if (!string.Equals(viewModel.StatusMessage, "Stillstand erfasst.", StringComparison.Ordinal))
                return false;
        }

        CaptureBaseline();
        return true;
    }

    public void DiscardChanges()
    {
        viewModel.NewActualCommand.Execute(null);
        CaptureBaseline();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProductionActualViewModel.SelectedActual))
        {
            Dispatcher.BeginInvoke(new Action(CaptureBaseline));
            return;
        }

        if (e.PropertyName == nameof(ProductionActualViewModel.StatusMessage) &&
            (string.Equals(viewModel.StatusMessage, "Ist-Produktion der konkreten Auftragsschicht gespeichert.", StringComparison.Ordinal) ||
             string.Equals(viewModel.StatusMessage, "Stillstand erfasst.", StringComparison.Ordinal)))
        {
            Dispatcher.BeginInvoke(new Action(CaptureBaseline));
        }
    }

    private void ActualRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: ProductionActualRow row } gridRow)
        {
            gridRow.IsSelected = true;
            viewModel.SelectedActual = row;
        }
    }

    private static ProductionActualRow? ContextActual(object sender)
    {
        if (sender is not MenuItem item ||
            item.Parent is not ContextMenu menu ||
            menu.PlacementTarget is not DataGridRow { DataContext: ProductionActualRow row })
            return null;
        return row;
    }

    private MainWindow? Host => Window.GetWindow(this) as MainWindow;

    private void ActualBatchContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextActual(sender) is { } row) Host?.OpenBatch(row.ProductionOrderId);
    }

    private void ActualOrderContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextActual(sender) is { } row) Host?.OpenProductionOrder(row.ProductionOrderId);
    }

    private void ActualControlContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextActual(sender) is { } row) Host?.OpenManufacturingControl(row.ProductionOrderId);
    }

    private void ActualArticlesContext_Click(object sender, RoutedEventArgs e) => Host?.OpenArticles();

    private bool HasPendingDowntimeDraft() =>
        viewModel.DowntimeMinutes > 0 ||
        !string.IsNullOrWhiteSpace(viewModel.DowntimeComment) ||
        !string.Equals(viewModel.DowntimeReason, "Störung", StringComparison.Ordinal);

    private void CaptureBaseline() => baseline = BuildSnapshot();

    private string BuildSnapshot() => string.Join("\u001f",
        viewModel.SelectedActual?.Id ?? 0,
        viewModel.SelectedOrder?.RunSlotId ?? 0,
        viewModel.ActualDate.Date,
        viewModel.TotalQuantity,
        viewModel.GoodQuantity,
        viewModel.ScrapQuantity,
        viewModel.PlannedProductionMinutes,
        viewModel.RunMinutes,
        viewModel.IdealRatePerHour,
        viewModel.Comment,
        viewModel.DowntimeReason,
        viewModel.DowntimeMinutes,
        viewModel.DowntimeComment);
}
