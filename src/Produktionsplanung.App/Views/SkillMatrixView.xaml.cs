using System.Windows.Controls;
using System.Windows.Data;
using Produktionsplanung.App.ViewModels;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.Views;

public partial class SkillMatrixView : UserControl
{
    private readonly SkillMatrixViewModel _viewModel;

    public SkillMatrixView()
    {
        InitializeComponent();
        _viewModel = new SkillMatrixViewModel();
        _viewModel.MatrixStructureChanged += (_, _) => Dispatcher.Invoke(BuildColumns);
        DataContext = _viewModel;
        BuildColumns();
    }

    private void BuildColumns()
    {
        MatrixGrid.Columns.Clear();

        MatrixGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Personalnr.",
            Binding = new Binding(nameof(EmployeeSkillRow.PersonnelNumber)),
            IsReadOnly = true,
            Width = 100
        });

        MatrixGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Mitarbeiter",
            Binding = new Binding(nameof(EmployeeSkillRow.Name)),
            IsReadOnly = true,
            Width = 180
        });

        MatrixGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Funktion",
            Binding = new Binding(nameof(EmployeeSkillRow.Role)),
            IsReadOnly = true,
            Width = 160
        });

        foreach (var qualification in _viewModel.Qualifications)
        {
            MatrixGrid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = qualification.Name,
                ItemsSource = QualificationLevelCatalog.EmployeeChoices,
                DisplayMemberPath = nameof(QualificationLevelOption.Name),
                SelectedValuePath = nameof(QualificationLevelOption.Level),
                SelectedValueBinding = new Binding($"Levels[{qualification.Id}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = 120
            });
        }
    }
}
