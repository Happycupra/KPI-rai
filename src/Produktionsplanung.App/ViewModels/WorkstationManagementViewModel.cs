using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.ViewModels;

public partial class WorkstationManagementViewModel : ObservableObject
{
    public ObservableCollection<Workstation> Workstations { get; } = new();
    public ObservableCollection<WorkstationQualificationOption> Qualifications { get; } = new();
    public IReadOnlyList<SkillLevelChoice> SkillLevels { get; } = new[]
    {
        new SkillLevelChoice(1, "Level 1 · In Einarbeitung"),
        new SkillLevelChoice(2, "Level 2 · Qualifiziert"),
        new SkillLevelChoice(3, "Level 3 · Experte")
    };

    [ObservableProperty] private Workstation? selectedWorkstation;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string area = string.Empty;
    [ObservableProperty] private int minimumStaff = 1;
    [ObservableProperty] private int optimalStaff = 1;
    [ObservableProperty] private int maximumStaff = 1;
    [ObservableProperty] private WorkstationQualificationOption? selectedRequiredQualification;
    [ObservableProperty] private SkillLevelChoice? selectedRequiredQualificationLevel;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private string statusMessage = string.Empty;

    public WorkstationManagementViewModel() => Load();

    partial void OnSelectedWorkstationChanged(Workstation? value)
    {
        if (value is null) return;
        Name = value.Name;
        Area = value.Area;
        MinimumStaff = value.MinimumStaff;
        OptimalStaff = value.OptimalStaff;
        MaximumStaff = value.MaximumStaff;
        SelectedRequiredQualification = Qualifications.FirstOrDefault(x => x.Id == value.RequiredQualificationId)
                                        ?? Qualifications.FirstOrDefault(x => x.Id is null);
        SelectedRequiredQualificationLevel = SkillLevels.FirstOrDefault(x => x.Level == Math.Clamp(value.RequiredQualificationLevel, 1, 3))
                                             ?? SkillLevels[1];
        IsActive = value.IsActive;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void NewWorkstation()
    {
        SelectedWorkstation = null;
        Name = string.Empty;
        Area = string.Empty;
        MinimumStaff = 1;
        OptimalStaff = 1;
        MaximumStaff = 1;
        SelectedRequiredQualification = Qualifications.FirstOrDefault(x => x.Id is null);
        SelectedRequiredQualificationLevel = SkillLevels[1];
        IsActive = true;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            StatusMessage = "Bitte einen Namen eingeben.";
            return;
        }

        if (MinimumStaff < 0 || OptimalStaff < MinimumStaff || MaximumStaff < OptimalStaff)
        {
            StatusMessage = "Besetzung muss gelten: Minimum ≤ Optimal ≤ Maximum.";
            return;
        }

        using var db = new AppDbContext();
        Workstation entity;
        if (SelectedWorkstation is null)
        {
            entity = new Workstation();
            db.Workstations.Add(entity);
        }
        else
        {
            entity = db.Workstations.First(x => x.Id == SelectedWorkstation.Id);
        }

        entity.Name = Name.Trim();
        entity.Area = Area.Trim();
        entity.MinimumStaff = MinimumStaff;
        entity.OptimalStaff = OptimalStaff;
        entity.MaximumStaff = MaximumStaff;
        entity.RequiredQualificationId = SelectedRequiredQualification?.Id;
        entity.RequiredQualificationLevel = entity.RequiredQualificationId.HasValue
            ? SelectedRequiredQualificationLevel?.Level ?? 2
            : 0;
        entity.IsActive = IsActive;
        db.SaveChanges();

        Load(entity.Id);
        StatusMessage = entity.RequiredQualificationId.HasValue
            ? "Arbeitsplatz inklusive Qualifikationspflicht gespeichert."
            : "Arbeitsplatz gespeichert.";
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedWorkstation is null) return;
        using var db = new AppDbContext();
        var id = SelectedWorkstation.Id;
        if (db.PlanningAssignments.Any(x => x.WorkstationId == id) ||
            db.ProductionOrders.Any(x => x.WorkstationId == id))
        {
            StatusMessage = "Arbeitsplatz kann nicht gelöscht werden, da Planungen oder Produktionsaufträge vorhanden sind. Bitte deaktivieren.";
            return;
        }

        var entity = db.Workstations.First(x => x.Id == id);
        db.Workstations.Remove(entity);
        db.SaveChanges();
        Load();
        NewWorkstation();
        StatusMessage = "Arbeitsplatz gelöscht.";
    }

    private void Load(int? selectId = null)
    {
        using var db = new AppDbContext();

        var qualificationId = SelectedRequiredQualification?.Id;
        Qualifications.Clear();
        Qualifications.Add(new WorkstationQualificationOption(null, "Keine Pflichtqualifikation"));
        foreach (var q in db.Qualifications.AsNoTracking().OrderBy(x => x.Name))
            Qualifications.Add(new WorkstationQualificationOption(q.Id, q.Name));

        var items = db.Workstations.AsNoTracking()
            .Include(x => x.RequiredQualification)
            .OrderBy(x => x.Name)
            .ToList();
        Workstations.Clear();
        foreach (var item in items) Workstations.Add(item);

        SelectedRequiredQualification = Qualifications.FirstOrDefault(x => x.Id == qualificationId)
                                        ?? Qualifications.FirstOrDefault(x => x.Id is null);
        SelectedRequiredQualificationLevel ??= SkillLevels[1];
        SelectedWorkstation = selectId is null ? null : Workstations.FirstOrDefault(x => x.Id == selectId);
    }
}

public sealed record WorkstationQualificationOption(int? Id, string Name);
public sealed record SkillLevelChoice(int Level, string Name);
