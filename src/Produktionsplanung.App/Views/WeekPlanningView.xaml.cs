using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class WeekPlanningView : UserControl
{
    public WeekPlanningView()
    {
        InitializeComponent();
        var viewModel = new WeekPlanningViewModel();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = viewModel;
        PreviewMouseLeftButtonUp += EmployeeName_PreviewMouseLeftButtonUp;
        PreviewMouseMove += EmployeeName_PreviewMouseMove;
        MouseLeave += (_, _) => Cursor = Cursors.Arrow;
        viewModel.RefreshProductionOrderCoverage();
    }

    private void EmployeeName_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        Cursor = TryGetEmployeeRow(e.OriginalSource as DependencyObject, out _)
            ? Cursors.Hand
            : Cursors.Arrow;
    }

    private void EmployeeName_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!TryGetEmployeeRow(e.OriginalSource as DependencyObject, out var row))
            return;

        if (Application.Current.MainWindow is MainWindow mainWindow && DataContext is WeekPlanningViewModel viewModel)
        {
            mainWindow.OpenEmployeeQuickCard(row.EmployeeId, viewModel.WeekStart);
            e.Handled = true;
        }
    }

    private static bool TryGetEmployeeRow(DependencyObject? source, out EmployeeWeekRow row)
    {
        row = null!;
        var textBlock = FindAncestor<TextBlock>(source);
        if (textBlock?.DataContext is not EmployeeWeekRow candidate)
            return false;
        if (!string.Equals(textBlock.Text, candidate.EmployeeName, StringComparison.Ordinal))
            return false;
        row = candidate;
        return true;
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
        if (sender is not WeekPlanningViewModel viewModel) return;
        if (e.PropertyName is nameof(WeekPlanningViewModel.WeekStart) or nameof(WeekPlanningViewModel.StatusMessage))
            viewModel.RefreshProductionOrderCoverage();
    }
}
