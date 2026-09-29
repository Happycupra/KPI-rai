using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class DayPlanningView : UserControl
{
    private bool? compactLayout;
    private Point employeeDragStartPoint;
    private EmployeeOption? draggedEmployee;

    public DayPlanningView() : this(DateTime.Today)
    {
    }

    public DayPlanningView(DateTime initialDate)
    {
        InitializeComponent();
        var viewModel = new DayPlanningViewModel
        {
            SelectedDate = initialDate.Date
        };
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = viewModel;
        PreviewMouseLeftButtonUp += EmployeeName_PreviewMouseLeftButtonUp;
        PreviewMouseMove += EmployeeName_PreviewMouseMove;
        MouseLeave += (_, _) => Cursor = Cursors.Arrow;
        Loaded += (_, _) => UpdateResponsiveLayout(ActualWidth);
        RefreshSupplementalPlanningData(viewModel);
    }

    private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        var compact = width < 1100;
        if (compactLayout == compact)
            return;

        compactLayout = compact;
        if (compact)
        {
            MasterDetailGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            MasterDetailGrid.ColumnDefinitions[1].Width = new GridLength(0);
            MasterDetailGrid.RowDefinitions[0].Height = new GridLength(55, GridUnitType.Star);
            MasterDetailGrid.RowDefinitions[1].Height = new GridLength(45, GridUnitType.Star);

            Grid.SetRow(PlanningOverviewPanel, 0);
            Grid.SetColumn(PlanningOverviewPanel, 0);
            PlanningOverviewPanel.Margin = new Thickness(0, 0, 0, 12);

            Grid.SetRow(AssignmentEditorPanel, 1);
            Grid.SetColumn(AssignmentEditorPanel, 0);
            AssignmentEditorPanel.Margin = new Thickness(0);
        }
        else
        {
            MasterDetailGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            MasterDetailGrid.ColumnDefinitions[1].Width = new GridLength(410);
            MasterDetailGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            MasterDetailGrid.RowDefinitions[1].Height = new GridLength(0);

            Grid.SetRow(PlanningOverviewPanel, 0);
            Grid.SetColumn(PlanningOverviewPanel, 0);
            PlanningOverviewPanel.Margin = new Thickness(0, 0, 18, 0);

            Grid.SetRow(AssignmentEditorPanel, 0);
            Grid.SetColumn(AssignmentEditorPanel, 1);
            AssignmentEditorPanel.Margin = new Thickness(0);
        }
    }

    private void EmployeeChip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        draggedEmployee = (sender as FrameworkElement)?.DataContext as EmployeeOption;
        employeeDragStartPoint = e.GetPosition(this);
    }

    private void EmployeeChip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedEmployee is null)
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - employeeDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - employeeDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var employee = draggedEmployee;
        draggedEmployee = null;
        var data = new DataObject(typeof(EmployeeOption), employee);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        e.Handled = true;
    }

    private void ProductionCoverageGrid_PreviewDragOver(object sender, DragEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        e.Effects = row?.Item is ProductionOrderCoverageRow &&
                    e.Data.GetDataPresent(typeof(EmployeeOption))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ProductionCoverageGrid_Drop(object sender, DragEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not ProductionOrderCoverageRow coverage ||
            e.Data.GetData(typeof(EmployeeOption)) is not EmployeeOption employee ||
            DataContext is not DayPlanningViewModel viewModel)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        viewModel.SelectedProductionOrderCoverage = coverage;
        e.Effects = viewModel.AssignEmployeeToProduction(employee.Id, coverage)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void EmployeeName_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        Cursor = TryGetEmployee(e.OriginalSource as DependencyObject, out _, out _)
            ? Cursors.Hand
            : Cursors.Arrow;
    }

    private void EmployeeName_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!TryGetEmployee(e.OriginalSource as DependencyObject, out var employeeId, out var employeeName))
            return;

        if (Application.Current.MainWindow is MainWindow mainWindow && DataContext is DayPlanningViewModel viewModel)
        {
            mainWindow.OpenEmployeeQuickCard(employeeId, viewModel.SelectedDate);
            e.Handled = true;
        }
    }

    private static bool TryGetEmployee(DependencyObject? source, out int employeeId, out string employeeName)
    {
        employeeId = 0;
        employeeName = string.Empty;
        var textBlock = FindAncestor<TextBlock>(source);
        if (textBlock is null)
            return false;

        switch (textBlock.DataContext)
        {
            case DayAssignmentRow row when string.Equals(textBlock.Text, row.EmployeeName, StringComparison.Ordinal):
                employeeId = row.EmployeeId;
                employeeName = row.EmployeeName;
                return true;
            case EmployeeSuggestion suggestion when string.Equals(textBlock.Text, suggestion.EmployeeName, StringComparison.Ordinal):
                employeeId = suggestion.EmployeeId;
                employeeName = suggestion.EmployeeName;
                return true;
            default:
                return false;
        }
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

    private static void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DayPlanningViewModel viewModel) return;
        if (e.PropertyName is nameof(DayPlanningViewModel.SelectedDate) or nameof(DayPlanningViewModel.StatusMessage))
            RefreshSupplementalPlanningData(viewModel);
    }

    private static void RefreshSupplementalPlanningData(DayPlanningViewModel viewModel)
    {
        viewModel.RefreshProductionOrderCoverage();
        viewModel.RefreshSkillAlerts();
        viewModel.RefreshOperatingCalendarAlert();
        viewModel.RefreshEmployeeSuggestions();
    }
    private MainWindow? Host => Window.GetWindow(this) as MainWindow;

    private void CoverageRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: ProductionOrderCoverageRow row } gridRow &&
            DataContext is DayPlanningViewModel vm)
        {
            gridRow.IsSelected = true;
            vm.SelectedProductionOrderCoverage = row;
        }
    }

    private static ProductionOrderCoverageRow? ContextCoverage(object sender)
    {
        if (sender is not MenuItem item ||
            item.Parent is not ContextMenu menu ||
            menu.PlacementTarget is not DataGridRow { DataContext: ProductionOrderCoverageRow row })
            return null;
        return row;
    }

    private void CoverageDetailsContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextCoverage(sender) is { } row) Host?.OpenBatch(row.OrderId);
    }

    private void CoverageEditContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextCoverage(sender) is { } row) Host?.OpenProductionOrder(row.OrderId);
    }

    private void CoverageActualContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextCoverage(sender) is { } row) Host?.OpenProductionActual(row.OrderId);
    }

    private void CoverageArticlesContext_Click(object sender, RoutedEventArgs e) => Host?.OpenArticles();

    private void AssignmentRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: DayAssignmentRow row } gridRow &&
            DataContext is DayPlanningViewModel vm)
        {
            gridRow.IsSelected = true;
            vm.SelectedAssignment = row;
        }
    }

    private static DayAssignmentRow? ContextAssignment(object sender)
    {
        if (sender is not MenuItem item ||
            item.Parent is not ContextMenu menu ||
            menu.PlacementTarget is not DataGridRow { DataContext: DayAssignmentRow row })
            return null;
        return row;
    }

    private void AssignmentEmployeeCardContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAssignment(sender) is { } row && DataContext is DayPlanningViewModel vm)
            Host?.OpenEmployeeQuickCard(row.EmployeeId, vm.SelectedDate);
    }

    private void AssignmentEmployeeMasterContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAssignment(sender) is { } row) Host?.OpenEmployee(row.EmployeeId);
    }

    private void AssignmentEmployeePlanContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAssignment(sender) is not null && DataContext is DayPlanningViewModel vm)
            Host?.OpenDayPlanning(vm.SelectedDate);
    }

    private void AssignmentRemoveContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAssignment(sender) is not { } row || DataContext is not DayPlanningViewModel vm)
            return;
        vm.SelectedAssignment = row;
        ConfirmAndDeleteAssignment(vm, row);
    }

    private void DeleteAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DayPlanningViewModel vm && vm.SelectedAssignment is { } row)
            ConfirmAndDeleteAssignment(vm, row);
    }

    private void ConfirmAndDeleteAssignment(DayPlanningViewModel vm, DayAssignmentRow row)
    {
        var answer = MessageBox.Show(
            Window.GetWindow(this),
            $"{row.EmployeeName} am {vm.SelectedDate:dd.MM.yyyy} wirklich aus der Planung entfernen?\n\nDie Zuweisung wird im Papierkorb archiviert.",
            "Geplanten Mitarbeiter entfernen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
            vm.DeleteCommand.Execute(null);
    }

}
