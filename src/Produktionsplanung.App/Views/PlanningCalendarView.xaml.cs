using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class PlanningCalendarView : UserControl
{
    private readonly PlanningCalendarViewModel viewModel;

    public PlanningCalendarView()
    {
        InitializeComponent();
        viewModel = new PlanningCalendarViewModel();
        DataContext = viewModel;
        PreviewMouseLeftButtonUp += CalendarEntry_PreviewMouseLeftButtonUp;
        PreviewMouseMove += CalendarEntry_PreviewMouseMove;
        MouseLeave += (_, _) => Cursor = Cursors.Arrow;
    }

    private void CalendarEntry_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        Cursor = TryGetInteractiveEntry(e.OriginalSource as DependencyObject, out _)
            ? Cursors.Hand
            : Cursors.Arrow;
    }

    private void CalendarEntry_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!TryGetInteractiveEntry(e.OriginalSource as DependencyObject, out var entry))
            return;

        if (Application.Current.MainWindow is not MainWindow mainWindow)
            return;

        if (entry.EmployeeId.HasValue)
            mainWindow.OpenEmployeeQuickCard(entry.EmployeeId.Value, entry.Date);
        else if (entry.EntryType == "Auftrag")
            mainWindow.OpenDayPlanning(entry.Date);
        else
            return;

        e.Handled = true;
    }

    private static bool TryGetInteractiveEntry(DependencyObject? source, out CalendarEntryRow entry)
    {
        entry = null!;
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: CalendarEntryRow row } &&
                (row.EmployeeId.HasValue || row.EntryType == "Auftrag"))
            {
                entry = row;
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void SelectCalendarDate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime date })
            return;

        viewModel.SelectDate(date);
    }

    private void OpenDayPlanning_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.OpenDayPlanning(viewModel.SelectedDate);
    }
}
