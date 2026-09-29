using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class EmployeesView : UserControl, IUnsavedChangesAware
{
    private readonly EmployeeManagementViewModel viewModel;
    private string baseline = string.Empty;

    public EmployeesView() : this(null)
    {
    }

    public EmployeesView(int? employeeId)
    {
        InitializeComponent();
        ConfigureHeaderActions();
        viewModel = new EmployeeManagementViewModel();
        if (employeeId.HasValue)
            viewModel.SelectEmployeeById(employeeId.Value);

        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        ApplyGrouping();
        CaptureBaseline();
    }

    public bool HasUnsavedChanges => baseline != BuildSnapshot();
    public string UnsavedChangesDescription => "Mitarbeiterdaten";

    public bool TrySaveChanges()
    {
        if (!viewModel.IsEditorOpen)
            return true;

        viewModel.SaveCommand.Execute(null);
        return !HasUnsavedChanges;
    }

    public void DiscardChanges()
    {
        viewModel.CloseEditor();
        CaptureBaseline();
    }

    private void ConfigureHeaderActions()
    {
        if (Content is not Grid root || root.Children.OfType<Grid>().FirstOrDefault() is not { } header)
            return;

        var newEmployeeButton = header.Children.OfType<Button>()
            .FirstOrDefault(x => Equals(x.Content, "+ Neuer Mitarbeiter"));
        if (newEmployeeButton is null)
            return;

        header.Children.Remove(newEmployeeButton);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(actions, 1);

        var templateButton = new Button
        {
            Content = "Excel-Vorlage",
            Height = 36,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Einfache Excel-Vorlage nur für Mitarbeitende speichern"
        };
        templateButton.Click += SaveEmployeeTemplate_Click;

        var importButton = new Button
        {
            Content = "Excel importieren",
            Height = 36,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Mitarbeitende aus Excel importieren oder anhand der Personalnummer aktualisieren"
        };
        importButton.Click += ImportEmployees_Click;

        newEmployeeButton.Margin = new Thickness(0);
        actions.Children.Add(templateButton);
        actions.Children.Add(importButton);
        actions.Children.Add(newEmployeeButton);
        header.Children.Add(actions);
    }

    private void SaveEmployeeTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Mitarbeiter-Excel-Vorlage speichern",
            Filter = "Excel-Arbeitsmappe (*.xlsx)|*.xlsx",
            FileName = "SolutionCompakt_Mitarbeitende.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            EmployeeExcelImportService.CreateTemplate(dialog.FileName);
            MessageBox.Show(
                "Die Mitarbeiter-Vorlage wurde gespeichert.",
                "Excel-Vorlage",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Die Excel-Vorlage konnte nicht gespeichert werden:\n\n{ex.Message}",
                "Excel-Vorlage",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ImportEmployees_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReplaceEditor())
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Mitarbeitende aus Excel importieren",
            Filter = "Excel-Arbeitsmappe (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            var preview = EmployeeExcelImportService.Import(dialog.FileName, dryRun: true);
            var errorText = preview.Errors.Count == 0
                ? string.Empty
                : "\n\nFehler:\n" + string.Join("\n", preview.Errors.Take(12));
            if (preview.Errors.Count > 12)
                errorText += $"\n… und {preview.Errors.Count - 12} weitere.";

            var prompt = $"Vorschau:\n{preview.Summary}{errorText}\n\nImport jetzt durchführen?";
            if (MessageBox.Show(
                    prompt,
                    "Mitarbeiter importieren",
                    MessageBoxButton.YesNo,
                    preview.Errors.Count == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var result = EmployeeExcelImportService.Import(dialog.FileName, dryRun: false);
            viewModel.RefreshCommand.Execute(null);
            CaptureBaseline();

            var finalErrorText = result.Errors.Count == 0
                ? string.Empty
                : "\n\nFehler:\n" + string.Join("\n", result.Errors.Take(12));
            if (result.Errors.Count > 12)
                finalErrorText += $"\n… und {result.Errors.Count - 12} weitere.";

            MessageBox.Show(
                result.Summary + finalErrorText,
                "Mitarbeiter importieren",
                MessageBoxButton.OK,
                result.Errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Der Mitarbeiter-Import konnte nicht ausgeführt werden:\n\n{ex.Message}",
                "Mitarbeiter importieren",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EmployeeManagementViewModel.SelectedGroupMode))
            ApplyGrouping();

        if (e.PropertyName is nameof(EmployeeManagementViewModel.SelectedEmployee) or
            nameof(EmployeeManagementViewModel.EditingId) or
            nameof(EmployeeManagementViewModel.IsEditorOpen))
        {
            CaptureBaseline();
        }
    }

    private void NewEmployee_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReplaceEditor())
            return;

        viewModel.NewEmployeeCommand.Execute(null);
        CaptureBaseline();
    }

    private void EditEmployeeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReplaceEditor() || ContextEmployee(sender) is not { } row)
            return;

        viewModel.EditEmployee(row.Id);
        CaptureBaseline();
    }

    private void EmployeeCardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ContextEmployee(sender) is { } row)
            (Window.GetWindow(this) as MainWindow)?.OpenEmployeeQuickCard(row.Id, DateTime.Today);
    }

    private void EmployeeCurrentPlanMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ContextEmployee(sender) is not { } row)
            return;

        viewModel.EditEmployee(row.Id);
        (Window.GetWindow(this) as MainWindow)?.OpenEmployeeQuickCard(row.Id, DateTime.Today);
        CaptureBaseline();
    }

    private static EmployeeDirectoryRow? ContextEmployee(object sender)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu contextMenu ||
            contextMenu.PlacementTarget is not FrameworkElement { DataContext: EmployeeDirectoryRow row })
            return null;
        return row;
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsEditorOpen && HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "Ungespeicherte Änderungen verwerfen?",
                "Bearbeitung schliessen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        viewModel.CloseEditor();
        CaptureBaseline();
    }

    private bool ConfirmReplaceEditor()
    {
        if (!viewModel.IsEditorOpen || !HasUnsavedChanges)
            return true;

        var result = MessageBox.Show(
            "Es gibt ungespeicherte Änderungen. Diese verwerfen und eine andere Bearbeitung öffnen?",
            "Ungespeicherte Änderungen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return false;

        viewModel.CloseEditor();
        CaptureBaseline();
        return true;
    }

    private void ApplyGrouping()
    {
        var view = CollectionViewSource.GetDefaultView(viewModel.EmployeeRows);
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EmployeeDirectoryRow.GroupKey)));
        view.Refresh();
    }

    private void CaptureBaseline() => baseline = BuildSnapshot();

    private string BuildSnapshot() => string.Join("\u001f",
        viewModel.IsEditorOpen,
        viewModel.EditingId,
        viewModel.PersonnelNumber,
        viewModel.FirstName,
        viewModel.LastName,
        viewModel.Role,
        viewModel.Department,
        viewModel.WorkloadPercent,
        viewModel.WeeklyTargetHours,
        viewModel.IsActive,
        string.Join(",", viewModel.SkillEditorRows.Select(x => $"{x.QualificationId}:{x.Level}")));
}
