using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class EmployeeManagementViewModel : ObservableObject
{
    private List<Employee> _allEmployees = new();

    public ObservableCollection<Employee> Employees { get; } = new();
    public ObservableCollection<EmployeeDirectoryRow> EmployeeRows { get; } = new();
    public ObservableCollection<EmployeeSkillEditorRow> SkillEditorRows { get; } = new();
    public ObservableCollection<Qualification> Qualifications { get; } = new();
    public string[] GroupModeOptions { get; } = new[] { "Keine", "Abteilung", "Funktion" };

    [ObservableProperty] private Employee? selectedEmployee;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private bool showInactive;
    [ObservableProperty] private int editingId;
    [ObservableProperty] private string personnelNumber = string.Empty;
    [ObservableProperty] private string firstName = string.Empty;
    [ObservableProperty] private string lastName = string.Empty;
    [ObservableProperty] private string role = string.Empty;
    [ObservableProperty] private string department = string.Empty;
    [ObservableProperty] private int workloadPercent = 100;
    [ObservableProperty] private double weeklyTargetHours = 40;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private bool isEditorOpen;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string selectedGroupMode = "Abteilung";
    [ObservableProperty] private string newQualificationName = string.Empty;
    [ObservableProperty] private Qualification? selectedQualification;

    public string EditorTitle => EditingId == 0
        ? "Neuer Mitarbeiter"
        : $"Mitarbeiter bearbeiten · {FirstName} {LastName}";

    public int VisibleEmployeeCount => EmployeeRows.Count;

    public EmployeeManagementViewModel()
    {
        LoadEmployees();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowInactiveChanged(bool value) => ApplyFilter();
    partial void OnSelectedGroupModeChanged(string value) => ApplyFilter();

    partial void OnSelectedEmployeeChanged(Employee? value)
    {
        if (value is null)
            return;

        EditingId = value.Id;
        PersonnelNumber = value.PersonnelNumber;
        FirstName = value.FirstName;
        LastName = value.LastName;
        Role = value.Role;
        Department = value.Department;
        WorkloadPercent = value.WorkloadPercent;
        WeeklyTargetHours = value.WeeklyTargetHours;
        IsActive = value.IsActive;
        IsEditorOpen = true;
        StatusMessage = string.Empty;
        LoadSkillEditor(value.Id);
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void NewEmployee()
    {
        SelectedEmployee = null;
        EditingId = 0;
        PersonnelNumber = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        Role = string.Empty;
        Department = "Produktion";
        WorkloadPercent = 100;
        WeeklyTargetHours = 40;
        IsActive = true;
        IsEditorOpen = true;
        StatusMessage = string.Empty;
        LoadSkillEditor(null);
        OnPropertyChanged(nameof(EditorTitle));
    }

    public void EditEmployee(int employeeId)
    {
        var employee = _allEmployees.FirstOrDefault(x => x.Id == employeeId);
        if (employee is null)
            return;

        if (!employee.IsActive)
            ShowInactive = true;

        SelectedEmployee = employee;
    }

    public void CloseEditor()
    {
        SelectedEmployee = null;
        EditingId = 0;
        PersonnelNumber = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        Role = string.Empty;
        Department = string.Empty;
        WorkloadPercent = 100;
        WeeklyTargetHours = 40;
        IsActive = true;
        IsEditorOpen = false;
        SkillEditorRows.Clear();
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void Save()
    {
        PersonnelNumber = PersonnelNumber.Trim();
        FirstName = FirstName.Trim();
        LastName = LastName.Trim();
        Role = Role.Trim();
        Department = Department.Trim();

        if (string.IsNullOrWhiteSpace(PersonnelNumber) ||
            string.IsNullOrWhiteSpace(FirstName) ||
            string.IsNullOrWhiteSpace(LastName))
        {
            MessageBox.Show("Personalnummer, Vorname und Nachname sind Pflichtfelder.", "Eingabe prüfen");
            return;
        }

        if (WorkloadPercent is < 1 or > 100)
        {
            MessageBox.Show("Das Pensum muss zwischen 1 und 100 % liegen.", "Eingabe prüfen");
            return;
        }

        if (!double.IsFinite(WeeklyTargetHours) || WeeklyTargetHours is < 0 or > 80)
        {
            MessageBox.Show("Die Sollstunden müssen eine gültige Zahl zwischen 0 und 80 Stunden sein.", "Eingabe prüfen");
            return;
        }

        var invalidSkill = SkillEditorRows.FirstOrDefault(x => !QualificationLevelCatalog.IsSupportedEmployeeLevel(x.Level));
        if (invalidSkill is not null)
        {
            MessageBox.Show($"Ungültiges Skill-Level bei „{invalidSkill.QualificationName}“. Erlaubt sind 0, 1, 2, 3 und 5 (Admin).", "Eingabe prüfen");
            return;
        }

        using var db = new AppDbContext();
        if (db.Employees.Any(x => x.PersonnelNumber == PersonnelNumber && x.Id != EditingId))
        {
            MessageBox.Show($"Die Personalnummer {PersonnelNumber} ist bereits vergeben.", "Doppelte Personalnummer");
            return;
        }

        Employee employee;
        if (EditingId == 0)
        {
            employee = new Employee();
            db.Employees.Add(employee);
        }
        else
        {
            employee = db.Employees.First(x => x.Id == EditingId);
        }

        employee.PersonnelNumber = PersonnelNumber;
        employee.FirstName = FirstName;
        employee.LastName = LastName;
        employee.Role = Role;
        employee.Department = Department;
        employee.WorkloadPercent = WorkloadPercent;
        employee.WeeklyTargetHours = WeeklyTargetHours;
        employee.IsActive = IsActive;

        db.SaveChanges();
        var id = employee.Id;

        var existingSkills = db.EmployeeQualifications
            .Where(x => x.EmployeeId == id)
            .ToList();

        foreach (var skill in SkillEditorRows)
        {
            var level = skill.Level;
            var existing = existingSkills.FirstOrDefault(x => x.QualificationId == skill.QualificationId);
            if (level == 0)
            {
                if (existing is not null)
                    db.EmployeeQualifications.Remove(existing);
            }
            else if (existing is null)
            {
                db.EmployeeQualifications.Add(new EmployeeQualification
                {
                    EmployeeId = id,
                    QualificationId = skill.QualificationId,
                    Level = level
                });
            }
            else
            {
                existing.Level = level;
            }
        }

        db.SaveChanges();

        LoadEmployees();
        StatusMessage = $"{FirstName} {LastName} gespeichert.";
        IsEditorOpen = false;
        EditingId = 0;
        SelectedEmployee = null;

        var saved = _allEmployees.FirstOrDefault(x => x.Id == id);
        if (saved is not null)
            OnPropertyChanged(nameof(VisibleEmployeeCount));
    }

    [RelayCommand]
    private void Deactivate()
    {
        if (EditingId == 0)
            return;

        if (MessageBox.Show(
                $"{FirstName} {LastName} wirklich deaktivieren?",
                "Mitarbeiter deaktivieren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        using var db = new AppDbContext();
        db.Employees.First(x => x.Id == EditingId).IsActive = false;
        db.SaveChanges();

        StatusMessage = $"{FirstName} {LastName} deaktiviert.";
        LoadEmployees();
        CloseEditor();
    }

    [RelayCommand]
    private void Activate()
    {
        if (EditingId == 0)
            return;

        using var db = new AppDbContext();
        db.Employees.First(x => x.Id == EditingId).IsActive = true;
        db.SaveChanges();

        StatusMessage = $"{FirstName} {LastName} aktiviert.";
        var id = EditingId;
        LoadEmployees();
        EditEmployee(id);
    }

    [RelayCommand]
    private void Delete()
    {
        if (EditingId == 0)
            return;

        using var db = new AppDbContext();
        var refs = new List<string>();
        if (db.PlanningAssignments.Any(x => x.EmployeeId == EditingId)) refs.Add("Planungen");
        if (db.Absences.Any(x => x.EmployeeId == EditingId)) refs.Add("Abwesenheiten");
        if (db.WorkTimeEntries.Any(x => x.EmployeeId == EditingId)) refs.Add("Arbeitszeitbuchungen");

        if (refs.Count > 0)
        {
            MessageBox.Show(
                $"Dieser Mitarbeiter kann nicht endgültig gelöscht werden, weil folgende Daten vorhanden sind: {string.Join(", ", refs)}. Bitte deaktivieren Sie ihn stattdessen.",
                "Löschen nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                $"{FirstName} {LastName} endgültig löschen? Dieser Vorgang kann nicht rückgängig gemacht werden.",
                "Mitarbeiter löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        db.EmployeeQualifications.RemoveRange(db.EmployeeQualifications.Where(x => x.EmployeeId == EditingId));
        db.Employees.Remove(db.Employees.First(x => x.Id == EditingId));

        try
        {
            db.SaveChanges();
            StatusMessage = $"{FirstName} {LastName} gelöscht.";
            LoadEmployees();
            CloseEditor();
        }
        catch (DbUpdateException)
        {
            MessageBox.Show(
                "Der Mitarbeiter wird noch von anderen Daten verwendet und konnte nicht gelöscht werden. Bitte deaktivieren Sie ihn stattdessen.",
                "Löschen nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadEmployees();
        if (IsEditorOpen)
            LoadSkillEditor(EditingId > 0 ? EditingId : null);
        StatusMessage = "Mitarbeiterliste aktualisiert.";
    }

    [RelayCommand]
    private void AddQualification()
    {
        var name = NewQualificationName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Bitte einen Namen für die Qualifikation eingeben.";
            return;
        }

        using var db = new AppDbContext();
        if (db.Qualifications.Any(x => x.Name.ToLower() == name.ToLower()))
        {
            StatusMessage = "Diese Qualifikation existiert bereits.";
            return;
        }

        db.Qualifications.Add(new Qualification { Name = name });
        db.SaveChanges();
        NewQualificationName = string.Empty;
        LoadQualifications();
        LoadEmployees();
        if (IsEditorOpen)
            LoadSkillEditor(EditingId > 0 ? EditingId : null);
        StatusMessage = $"Qualifikation „{name}“ hinzugefügt.";
    }

    [RelayCommand]
    private void DeleteQualification()
    {
        if (SelectedQualification is null)
            return;

        var qualificationId = SelectedQualification.Id;
        var qualificationName = SelectedQualification.Name;

        if (MessageBox.Show(
                $"Qualifikation „{qualificationName}“ inklusive aller Mitarbeiter-Zuordnungen löschen?",
                "Qualifikation löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        using var db = new AppDbContext();
        foreach (var workstation in db.Workstations.Where(x => x.RequiredQualificationId == qualificationId))
        {
            workstation.RequiredQualificationId = null;
            workstation.RequiredQualificationLevel = 0;
        }

        db.EmployeeQualifications.RemoveRange(
            db.EmployeeQualifications.Where(x => x.QualificationId == qualificationId));
        var qualification = db.Qualifications.FirstOrDefault(x => x.Id == qualificationId);
        if (qualification is not null)
            db.Qualifications.Remove(qualification);

        db.SaveChanges();
        SelectedQualification = null;
        LoadQualifications();
        LoadEmployees();
        if (IsEditorOpen)
            LoadSkillEditor(EditingId > 0 ? EditingId : null);
        StatusMessage = $"Qualifikation „{qualificationName}“ gelöscht.";
    }

    private void LoadEmployees()
    {
        LoadQualifications();
        using var db = new AppDbContext();
        _allEmployees = db.Employees
            .AsNoTracking()
            .Include(x => x.Qualifications)
                .ThenInclude(x => x.Qualification)
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToList();

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = _allEmployees.AsEnumerable();

        if (!ShowInactive)
            query = query.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(x =>
                x.PersonnelNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.FirstName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.LastName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Role.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Department.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Qualifications.Any(q =>
                    q.Qualification.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var filtered = query.ToList();

        Employees.Clear();
        EmployeeRows.Clear();

        foreach (var employee in filtered)
        {
            Employees.Add(employee);
            EmployeeRows.Add(new EmployeeDirectoryRow
            {
                Id = employee.Id,
                PersonnelNumber = employee.PersonnelNumber,
                FirstName = employee.FirstName,
                LastName = employee.LastName,
                FullName = $"{employee.LastName}, {employee.FirstName}",
                Initials = EmployeeInitialsService.Build3(employee.FirstName, employee.LastName),
                Role = employee.Role,
                Department = employee.Department,
                WorkloadPercent = employee.WorkloadPercent,
                WeeklyTargetHours = employee.WeeklyTargetHours,
                IsActive = employee.IsActive,
                GroupKey = SelectedGroupMode switch
                {
                    "Funktion" => string.IsNullOrWhiteSpace(employee.Role) ? "Ohne Funktion" : employee.Role,
                    "Abteilung" => string.IsNullOrWhiteSpace(employee.Department) ? "Ohne Abteilung" : employee.Department,
                    _ => string.Empty
                },
                QualificationSummary = employee.Qualifications.Count == 0
                    ? "Keine Qualifikationen hinterlegt"
                    : string.Join(" · ", employee.Qualifications
                        .OrderBy(x => x.Qualification.Name)
                        .Select(x => $"{x.Qualification.Name} L{x.Level}"))
            });
        }

        OnPropertyChanged(nameof(VisibleEmployeeCount));
    }

    private void LoadQualifications()
    {
        using var db = new AppDbContext();
        var selectedId = SelectedQualification?.Id;
        Qualifications.Clear();
        foreach (var qualification in db.Qualifications.AsNoTracking().OrderBy(x => x.Name))
            Qualifications.Add(qualification);
        SelectedQualification = selectedId.HasValue
            ? Qualifications.FirstOrDefault(x => x.Id == selectedId.Value)
            : null;
    }

    private void LoadSkillEditor(int? employeeId)
    {
        using var db = new AppDbContext();
        var qualifications = db.Qualifications.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToList();
        var levels = employeeId.HasValue
            ? db.EmployeeQualifications.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId.Value)
                .ToDictionary(x => x.QualificationId, x => x.Level)
            : new Dictionary<int, int>();

        SkillEditorRows.Clear();
        foreach (var qualification in qualifications)
        {
            SkillEditorRows.Add(new EmployeeSkillEditorRow
            {
                QualificationId = qualification.Id,
                QualificationName = qualification.Name,
                Level = levels.TryGetValue(qualification.Id, out var level) ? level : 0
            });
        }
    }
}

public sealed class EmployeeDirectoryRow
{
    public int Id { get; set; }
    public string PersonnelNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int WorkloadPercent { get; set; }
    public double WeeklyTargetHours { get; set; }
    public bool IsActive { get; set; }
    public string GroupKey { get; set; } = string.Empty;
    public string QualificationSummary { get; set; } = string.Empty;

    public string StatusText => IsActive ? "Aktiv" : "Inaktiv";
    public string StatusBackground => IsActive ? "#ECFDF5" : "#F1F5F9";
    public string StatusForeground => IsActive ? "#15803D" : "#64748B";
    public string RoleDepartmentText => string.Join(" · ",
        new[] { Role, Department }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string WorkloadText => $"{WorkloadPercent}% · {WeeklyTargetHours:0.#} h/Woche";
}

public sealed class EmployeeSkillEditorRow : ObservableObject
{
    public int QualificationId { get; set; }
    public string QualificationName { get; set; } = string.Empty;

    public IReadOnlyList<QualificationLevelOption> LevelChoices => QualificationLevelCatalog.EmployeeChoices;

    private int level;
    public int Level
    {
        get => level;
        set => SetProperty(ref level, QualificationLevelCatalog.IsSupportedEmployeeLevel(value) ? value : QualificationLevelCatalog.None);
    }

    public string LevelText => QualificationLevelCatalog.DisplayName(Level);
}
