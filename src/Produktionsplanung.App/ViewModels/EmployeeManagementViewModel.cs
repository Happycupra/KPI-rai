using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.ViewModels;

public partial class EmployeeManagementViewModel : ObservableObject
{
    private List<Employee> _allEmployees = new();

    public ObservableCollection<Employee> Employees { get; } = new();

    [ObservableProperty]
    private Employee? selectedEmployee;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool showInactive;

    [ObservableProperty]
    private int editingId;

    [ObservableProperty]
    private string personnelNumber = string.Empty;

    [ObservableProperty]
    private string firstName = string.Empty;

    [ObservableProperty]
    private string lastName = string.Empty;

    [ObservableProperty]
    private string role = string.Empty;

    [ObservableProperty]
    private string department = string.Empty;

    [ObservableProperty]
    private int workloadPercent = 100;

    [ObservableProperty]
    private double weeklyTargetHours = 40;

    [ObservableProperty]
    private bool isActive = true;

    public string EditorTitle => EditingId == 0 ? "Neuer Mitarbeiter" : $"Mitarbeiter bearbeiten – {FirstName} {LastName}";

    public EmployeeManagementViewModel()
    {
        LoadEmployees();
        NewEmployee();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnShowInactiveChanged(bool value) => ApplyFilter();

    partial void OnSelectedEmployeeChanged(Employee? value)
    {
        if (value is null) return;

        EditingId = value.Id;
        PersonnelNumber = value.PersonnelNumber;
        FirstName = value.FirstName;
        LastName = value.LastName;
        Role = value.Role;
        Department = value.Department;
        WorkloadPercent = value.WorkloadPercent;
        WeeklyTargetHours = value.WeeklyTargetHours;
        IsActive = value.IsActive;
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
            MessageBox.Show("Personalnummer, Vorname und Nachname sind Pflichtfelder.", "Eingabe prüfen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (WorkloadPercent is < 1 or > 100)
        {
            MessageBox.Show("Das Pensum muss zwischen 1 und 100 % liegen.", "Eingabe prüfen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (WeeklyTargetHours is < 0 or > 80)
        {
            MessageBox.Show("Die Sollstunden müssen zwischen 0 und 80 Stunden liegen.", "Eingabe prüfen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var db = new AppDbContext();
        var duplicate = db.Employees.Any(x => x.PersonnelNumber == PersonnelNumber && x.Id != EditingId);
        if (duplicate)
        {
            MessageBox.Show($"Die Personalnummer {PersonnelNumber} ist bereits vergeben.", "Doppelte Personalnummer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Employee entity;
        if (EditingId == 0)
        {
            entity = new Employee();
            db.Employees.Add(entity);
        }
        else
        {
            entity = db.Employees.First(x => x.Id == EditingId);
        }

        entity.PersonnelNumber = PersonnelNumber;
        entity.FirstName = FirstName;
        entity.LastName = LastName;
        entity.Role = Role;
        entity.Department = Department;
        entity.WorkloadPercent = WorkloadPercent;
        entity.WeeklyTargetHours = WeeklyTargetHours;
        entity.IsActive = IsActive;

        db.SaveChanges();
        var savedId = entity.Id;
        LoadEmployees();
        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == savedId);
    }

    [RelayCommand]
    private void Deactivate()
    {
        if (EditingId == 0) return;
        if (MessageBox.Show($"{FirstName} {LastName} wirklich deaktivieren?", "Mitarbeiter deaktivieren",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        using var db = new AppDbContext();
        var entity = db.Employees.First(x => x.Id == EditingId);
        entity.IsActive = false;
        db.SaveChanges();
        LoadEmployees();
        NewEmployee();
    }

    [RelayCommand]
    private void Activate()
    {
        if (EditingId == 0) return;
        using var db = new AppDbContext();
        var entity = db.Employees.First(x => x.Id == EditingId);
        entity.IsActive = true;
        db.SaveChanges();
        LoadEmployees();
        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == EditingId);
    }

    [RelayCommand]
    private void Delete()
    {
        if (EditingId == 0) return;

        using var db = new AppDbContext();
        var hasPlanning = db.PlanningAssignments.Any(x => x.EmployeeId == EditingId);
        var hasAbsences = db.Absences.Any(x => x.EmployeeId == EditingId);
        if (hasPlanning || hasAbsences)
        {
            MessageBox.Show("Dieser Mitarbeiter besitzt bereits Planungs- oder Abwesenheitsdaten und kann deshalb nicht endgültig gelöscht werden. Bitte deaktivieren Sie ihn stattdessen.",
                "Löschen nicht möglich", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"{FirstName} {LastName} endgültig löschen? Dieser Vorgang kann nicht rückgängig gemacht werden.",
                "Mitarbeiter löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var skillLinks = db.EmployeeQualifications.Where(x => x.EmployeeId == EditingId);
        db.EmployeeQualifications.RemoveRange(skillLinks);
        var entity = db.Employees.First(x => x.Id == EditingId);
        db.Employees.Remove(entity);
        db.SaveChanges();
        LoadEmployees();
        NewEmployee();
    }

    [RelayCommand]
    private void Refresh() => LoadEmployees();

    private void LoadEmployees()
    {
        using var db = new AppDbContext();
        _allEmployees = db.Employees.AsNoTracking()
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
                x.Department.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        Employees.Clear();
        foreach (var employee in query)
            Employees.Add(employee);
    }
}
