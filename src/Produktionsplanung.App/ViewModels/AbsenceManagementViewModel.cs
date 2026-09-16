using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.ViewModels;

public partial class AbsenceManagementViewModel : ObservableObject
{
    public ObservableCollection<Employee> Employees { get; } = new();
    public ObservableCollection<AbsenceRow> Absences { get; } = new();
    public IReadOnlyList<string> AbsenceTypes { get; } = new[]
    {
        "Ferien", "Krankheit", "Unfall", "Weiterbildung", "Militär / Zivildienst", "Unbezahlter Urlaub", "Sonstige"
    };

    [ObservableProperty] private AbsenceRow? selectedAbsence;
    [ObservableProperty] private Employee? selectedEmployee;
    [ObservableProperty] private string absenceType = "Ferien";
    [ObservableProperty] private DateTime startDate = DateTime.Today;
    [ObservableProperty] private DateTime endDate = DateTime.Today;
    [ObservableProperty] private string comment = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    public AbsenceManagementViewModel() => Load();

    partial void OnSelectedAbsenceChanged(AbsenceRow? value)
    {
        if (value is null) return;
        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == value.EmployeeId);
        AbsenceType = value.Type;
        StartDate = value.StartDate;
        EndDate = value.EndDate;
        Comment = value.Comment ?? string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void NewAbsence()
    {
        SelectedAbsence = null;
        SelectedEmployee = Employees.FirstOrDefault();
        AbsenceType = AbsenceTypes[0];
        StartDate = DateTime.Today;
        EndDate = DateTime.Today;
        Comment = string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedEmployee is null)
        {
            StatusMessage = "Bitte einen Mitarbeiter auswählen.";
            return;
        }

        if (EndDate.Date < StartDate.Date)
        {
            StatusMessage = "Enddatum darf nicht vor dem Startdatum liegen.";
            return;
        }

        using var db = new AppDbContext();
        var employeeId = SelectedEmployee.Id;
        var conflict = db.Absences.Any(x =>
            x.EmployeeId == employeeId &&
            (SelectedAbsence == null || x.Id != SelectedAbsence.Id) &&
            x.StartDate.Date <= EndDate.Date && x.EndDate.Date >= StartDate.Date);

        if (conflict)
        {
            StatusMessage = "Für diesen Mitarbeiter existiert bereits eine überschneidende Abwesenheit.";
            return;
        }

        Absence entity;
        if (SelectedAbsence is null)
        {
            entity = new Absence();
            db.Absences.Add(entity);
        }
        else
        {
            entity = db.Absences.First(x => x.Id == SelectedAbsence.Id);
        }

        entity.EmployeeId = employeeId;
        entity.Type = AbsenceType;
        entity.StartDate = StartDate.Date;
        entity.EndDate = EndDate.Date;
        entity.Comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();
        db.SaveChanges();

        Load(entity.Id);
        StatusMessage = "Abwesenheit gespeichert.";
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedAbsence is null) return;
        using var db = new AppDbContext();
        var entity = db.Absences.First(x => x.Id == SelectedAbsence.Id);
        db.Absences.Remove(entity);
        db.SaveChanges();
        Load();
        NewAbsence();
        StatusMessage = "Abwesenheit gelöscht.";
    }

    private void Load(int? selectId = null)
    {
        using var db = new AppDbContext();
        Employees.Clear();
        foreach (var employee in db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.LastName).ThenBy(x => x.FirstName))
            Employees.Add(employee);

        Absences.Clear();
        var items = db.Absences.AsNoTracking()
            .Include(x => x.Employee)
            .OrderByDescending(x => x.StartDate)
            .ThenBy(x => x.Employee.LastName)
            .ToList();

        foreach (var item in items)
        {
            Absences.Add(new AbsenceRow
            {
                Id = item.Id,
                EmployeeId = item.EmployeeId,
                EmployeeName = $"{item.Employee.LastName}, {item.Employee.FirstName}",
                Type = item.Type,
                StartDate = item.StartDate,
                EndDate = item.EndDate,
                Comment = item.Comment
            });
        }

        SelectedAbsence = selectId is null ? null : Absences.FirstOrDefault(x => x.Id == selectId);
        if (SelectedEmployee is null) SelectedEmployee = Employees.FirstOrDefault();
    }
}

public class AbsenceRow
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Comment { get; set; }
}
