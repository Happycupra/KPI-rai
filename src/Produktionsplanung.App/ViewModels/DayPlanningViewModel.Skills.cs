using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class DayPlanningViewModel
{
    private const string SkillAlertPrefix = "[Skill] ";
    public ObservableCollection<EmployeeSuggestion> EmployeeSuggestions { get; } = new();

    public string WorkstationSkillRequirementText
    {
        get
        {
            if (SelectedWorkstation is null)
                return "Kein Arbeitsplatz gewählt.";

            using var db = new AppDbContext();
            var workstation = db.Workstations.AsNoTracking()
                .Include(x => x.RequiredQualification)
                .FirstOrDefault(x => x.Id == SelectedWorkstation.Id);
            if (workstation is null ||
                !workstation.RequiredQualificationId.HasValue ||
                workstation.RequiredQualificationLevel <= 0)
                return "Für diesen Arbeitsplatz ist keine Pflichtqualifikation hinterlegt.";

            return $"Pflicht: {workstation.RequiredQualification?.Name ?? "Qualifikation"} · mindestens Level {workstation.RequiredQualificationLevel}.";
        }
    }

    partial void OnSelectedWorkstationChanged(WorkstationOption? value)
    {
        OnPropertyChanged(nameof(WorkstationSkillRequirementText));
        RefreshAllowedShifts();
        EnsureExistingAssignmentShiftVisible(value);
        RefreshEmployeeSuggestions();
    }

    private void EnsureExistingAssignmentShiftVisible(WorkstationOption? workstation)
    {
        if (workstation is null ||
            SelectedAssignment?.ShiftId is not int shiftId ||
            SelectedAssignment.WorkstationId != workstation.Id ||
            Shifts.Any(x => x.Id == shiftId))
            return;

        // Ein bereits gespeicherter Einsatz kann nach einer späteren Änderung des Maschinen-
        // Schichtmodells ausserhalb der aktuellen Freigabe liegen. Er muss beim Bearbeiten
        // trotzdem mit seiner ursprünglichen Schicht angezeigt werden, statt stillschweigend
        // auf die erste heute erlaubte Schicht umzuschalten. Speichern bleibt weiterhin durch
        // SaveValidated blockiert, bis eine gültige Schicht gewählt wurde.
        using var db = new AppDbContext();
        var existingShift = db.Shifts.AsNoTracking().FirstOrDefault(x => x.Id == shiftId);
        if (existingShift is null)
            return;

        Shifts.Add(new ShiftOption
        {
            Id = existingShift.Id,
            Name = existingShift.Name,
            StartTime = existingShift.StartTime,
            EndTime = existingShift.EndTime,
            BreakMinutes = existingShift.BreakMinutes
        });
    }

    partial void OnSelectedShiftChanged(ShiftOption? value) => RefreshEmployeeSuggestions();

    public void RefreshEmployeeSuggestions()
    {
        EmployeeSuggestions.Clear();
        if (SelectedWorkstation is null || SelectedShift is null)
            return;

        foreach (var suggestion in QualificationPlanningService.Suggest(
                     SelectedDate,
                     SelectedWorkstation.Id,
                     SelectedShift.Id,
                     SelectedAssignment?.Id))
            EmployeeSuggestions.Add(suggestion);
    }

    public void RefreshSkillAlerts()
    {
        for (var i = Alerts.Count - 1; i >= 0; i--)
        {
            if (Alerts[i].Message.StartsWith(SkillAlertPrefix, StringComparison.Ordinal))
                Alerts.RemoveAt(i);
        }

        using var db = new AppDbContext();
        var assignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Where(x => x.Date.Date == SelectedDate.Date)
            .ToList();

        foreach (var assignment in assignments)
        {
            var check = QualificationPlanningService.CheckEmployee(db, assignment.EmployeeId, assignment.WorkstationId);
            if (!check.IsQualified)
            {
                Alerts.Add(new PlanningAlert
                {
                    Severity = "Rot",
                    Message = $"{SkillAlertPrefix}{assignment.Employee.LastName}, {assignment.Employee.FirstName} erfüllt die Qualifikationspflicht für {assignment.Workstation.Name} nicht. {check.Message}"
                });
            }
        }
    }

    [RelayCommand]
    private void SaveValidated()
    {
        if (SelectedEmployee is not null && SelectedWorkstation is not null && SelectedShift is not null)
        {
            using var db = new AppDbContext();
            if (!ProductionScheduleService.IsShiftAllowedOnDate(
                    db,
                    SelectedWorkstation.Id,
                    SelectedShift.Id,
                    SelectedDate))
            {
                StatusMessage = $"Zuweisung blockiert: {SelectedShift.Name} ist für {SelectedWorkstation.Name} am {SelectedDate:dd.MM.yyyy} nicht freigegeben.";
                return;
            }

            var check = QualificationPlanningService.CheckEmployee(db, SelectedEmployee.Id, SelectedWorkstation.Id);
            if (!check.IsQualified)
            {
                StatusMessage = $"Zuweisung blockiert: {SelectedEmployee.DisplayName} erfüllt die Pflichtqualifikation für {SelectedWorkstation.Name} nicht. {check.Message}";
                return;
            }
        }

        Save();
    }

    [RelayCommand]
    private void UseSuggestion(EmployeeSuggestion? suggestion)
    {
        if (suggestion is null)
            return;

        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == suggestion.EmployeeId);
        StatusMessage = $"Vorschlag übernommen: {suggestion.EmployeeName} · {suggestion.SkillText} · {suggestion.LoadText}.";
    }
}
