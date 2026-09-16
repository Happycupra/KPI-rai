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
        RefreshSupplementalPlanningData(viewModel);
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
}
