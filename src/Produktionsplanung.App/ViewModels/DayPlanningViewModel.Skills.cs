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

            if (workstation is null || !workstation.RequiredQualificationId.HasValue || workstation.RequiredQualificationLevel <= 0)
                return "Für diesen Arbeitsplatz ist keine Pflichtqualifikation hinterlegt.";

            return $"Pflicht: {workstation.RequiredQualification?.Name ?? "Qualifikation"} · mindestens Level {workstation.RequiredQualificationLevel}.";
        }
    }

    partial void OnSelectedWorkstationChanged(WorkstationOption? value)
    {
        OnPropertyChanged(nameof(WorkstationSkillRequirementText));
        RefreshEmployeeSuggestions();
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
        {
            EmployeeSuggestions.Add(suggestion);
        }
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
            if (check.IsQualified)
                continue;

            Alerts.Add(new PlanningAlert
            {
                Severity = "Rot",
                Message = $"{SkillAlertPrefix}{assignment.Employee.LastName}, {assignment.Employee.FirstName} erfüllt die Qualifikationspflicht für {assignment.Workstation.Name} nicht. {check.Message}"
            });
        }
    }

    [RelayCommand]
    private void SaveValidated()
    {
        if (SelectedEmployee is not null && SelectedWorkstation is not null)
        {
            using var db = new AppDbContext();
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
