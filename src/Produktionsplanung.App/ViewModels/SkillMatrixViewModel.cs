using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class SkillMatrixViewModel : ObservableObject
{
    public ObservableCollection<Qualification> Qualifications { get; } = new();
    public ObservableCollection<EmployeeSkillRow> Rows { get; } = new();

    [ObservableProperty]
    private Qualification? selectedQualification;

    [ObservableProperty]
    private string newQualificationName = string.Empty;

    public event EventHandler? MatrixStructureChanged;

    public SkillMatrixViewModel()
    {
        Load();
    }

    [RelayCommand]
    public void Load()
    {
        using var db = new AppDbContext();
        var qualifications = db.Qualifications.AsNoTracking().OrderBy(x => x.Name).ToList();
        var employees = db.Employees.AsNoTracking()
            .Where(x => x.IsActive)
            .Include(x => x.Qualifications)
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToList();

        Qualifications.Clear();
        foreach (var qualification in qualifications)
            Qualifications.Add(qualification);

        Rows.Clear();
        foreach (var employee in employees)
        {
            var row = new EmployeeSkillRow
            {
                EmployeeId = employee.Id,
                PersonnelNumber = employee.PersonnelNumber,
                Name = $"{employee.FirstName} {employee.LastName}",
                Role = employee.Role
            };

            foreach (var qualification in qualifications)
            {
                var level = employee.Qualifications
                    .FirstOrDefault(x => x.QualificationId == qualification.Id)?.Level ?? 0;
                row.Levels[qualification.Id] = level;
            }
            Rows.Add(row);
        }

        MatrixStructureChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void AddQualification()
    {
        var name = NewQualificationName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Bitte geben Sie einen Namen für die Qualifikation ein.", "Qualifikation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var db = new AppDbContext();
        if (db.Qualifications.Any(x => x.Name.ToLower() == name.ToLower()))
        {
            MessageBox.Show("Diese Qualifikation existiert bereits.", "Qualifikation",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        db.Qualifications.Add(new Qualification { Name = name });
        db.SaveChanges();
        NewQualificationName = string.Empty;
        Load();
    }

    [RelayCommand]
    private void DeleteQualification()
    {
        if (SelectedQualification is null) return;

        if (MessageBox.Show($"Qualifikation '{SelectedQualification.Name}' inklusive aller Mitarbeiter-Zuordnungen löschen? Pflichtzuordnungen an Arbeitsplätzen werden dabei ebenfalls entfernt.",
                "Qualifikation löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        using var db = new AppDbContext();
        var qualificationId = SelectedQualification.Id;

        var workstations = db.Workstations.Where(x => x.RequiredQualificationId == qualificationId).ToList();
        foreach (var workstation in workstations)
        {
            workstation.RequiredQualificationId = null;
            workstation.RequiredQualificationLevel = 0;
        }

        foreach (var operation in db.OperationDefinitions.Where(x => x.RequiredQualificationId == qualificationId))
        {
            operation.RequiredQualificationId = null;
            operation.RequiredQualificationLevel = 0;
        }

        var links = db.EmployeeQualifications.Where(x => x.QualificationId == qualificationId);
        db.EmployeeQualifications.RemoveRange(links);
        var qualification = db.Qualifications.First(x => x.Id == qualificationId);
        RecycleBinService.ArchiveDeletion(db, qualification, qualification.Id.ToString(), $"Qualifikation {qualification.Name}");
        db.Qualifications.Remove(qualification);
        db.SaveChanges();
        SelectedQualification = null;
        Load();
    }

    [RelayCommand]
    private void SaveMatrix()
    {
        using var db = new AppDbContext();
        var existing = db.EmployeeQualifications.ToList();

        foreach (var row in Rows)
        {
            foreach (var qualification in Qualifications)
            {
                var desiredLevel = row.Levels.TryGetValue(qualification.Id, out var level) ? level : 0;
                if (!QualificationLevelCatalog.IsSupportedEmployeeLevel(desiredLevel))
                {
                    MessageBox.Show($"Ungültiges Skill-Level {desiredLevel} bei {row.Name} / {qualification.Name}. Erlaubt sind 0 bis 5.", "Skill-Matrix", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var link = existing.FirstOrDefault(x =>
                    x.EmployeeId == row.EmployeeId && x.QualificationId == qualification.Id);

                if (desiredLevel == 0)
                {
                    if (link is not null)
                        db.EmployeeQualifications.Remove(link);
                }
                else if (link is null)
                {
                    db.EmployeeQualifications.Add(new EmployeeQualification
                    {
                        EmployeeId = row.EmployeeId,
                        QualificationId = qualification.Id,
                        Level = desiredLevel
                    });
                }
                else
                {
                    link.Level = desiredLevel;
                }
            }
        }

        db.SaveChanges();
        MessageBox.Show("Skill-Matrix wurde gespeichert.", "Gespeichert",
            MessageBoxButton.OK, MessageBoxImage.Information);
        Load();
    }
}

public class EmployeeSkillRow : INotifyPropertyChanged
{
    public int EmployeeId { get; set; }
    public string PersonnelNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Dictionary<int, int> Levels { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyLevelChanged(int qualificationId) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Levels[{qualificationId}]"));
}
