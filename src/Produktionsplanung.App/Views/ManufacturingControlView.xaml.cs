using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ManufacturingControlView : UserControl
{
    private Point dragStartPoint;
    private RoutingStepRow? draggedRoutingStep;

    public ManufacturingControlView(int? orderId = null)
    {
        InitializeComponent();
        var vm = new ManufacturingControlViewModel();
        DataContext = vm;
        if (orderId.HasValue) vm.SelectedProductionOrder = vm.ProductionOrders.FirstOrDefault(x => x.Id == orderId.Value);
    }

    private void RoutingStepsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStartPoint = e.GetPosition(null);
        draggedRoutingStep = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as RoutingStepRow;
    }

    private void RoutingStepsGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedRoutingStep is null)
            return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(RoutingStepsGrid, draggedRoutingStep, DragDropEffects.Move);
    }

    private void RoutingStepsGrid_Drop(object sender, DragEventArgs e)
    {
        if (draggedRoutingStep is null || DataContext is not ManufacturingControlViewModel vm)
            return;

        var target = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as RoutingStepRow;
        if (target is null || target.Id == draggedRoutingStep.Id)
            return;

        vm.MoveRoutingStep(draggedRoutingStep.Id, target.Id);
        draggedRoutingStep = null;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
