using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class WorkstationManagementViewModel : ObservableObject
{
    public ObservableCollection<Workstation> Workstations { get; } = new();
    public ObservableCollection<WorkstationQualificationOption> Qualifications { get; } = new();
    public ObservableCollection<WorkstationShiftRuleRow> ShiftRules { get; } = new();
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
    [ObservableProperty] private string shiftModelText = "Nicht konfiguriert";

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
        LoadShiftRules(value.Id);
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
        LoadShiftRules(null);
        ApplyShiftPreset("1");
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void ApplyShiftPreset(string? preset)
    {
        if (ShiftRules.Count == 0)
            return;

        if (int.TryParse(preset, out var shiftCount))
        {
            shiftCount = Math.Clamp(shiftCount, 1, ShiftRules.Count);

            // Eine zusätzliche Tagschicht (z. B. 07:00–16:00) darf bei 2-/3-Schicht-
            // Schnellwahlen nicht zwischen Früh-/Spät-/Nachtschicht geraten. Solange genügend
            // andere Schichten vorhanden sind, werden explizite Tagschichten daher ausgelassen.
            var nonDayShiftRows = ShiftRules
                .Where(x => !x.ShiftName.Contains("tag", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var candidates = nonDayShiftRows.Count >= shiftCount
                ? nonDayShiftRows
                : ShiftRules.ToList();
            var enabledShiftIds = candidates
                .Take(shiftCount)
                .Select(x => x.ShiftId)
                .ToHashSet();

            foreach (var row in ShiftRules)
            {
                var enabled = enabledShiftIds.Contains(row.ShiftId);
                row.IsEnabled = enabled;
                row.Monday = row.Tuesday = row.Wednesday = row.Thursday = row.Friday = enabled;
                row.Saturday = row.Sunday = false;
            }
        }
        else if (string.Equals(preset, "MoFr", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var row in ShiftRules.Where(x => x.IsEnabled))
            {
                row.Monday = row.Tuesday = row.Wednesday = row.Thursday = row.Friday = true;
                row.Saturday = row.Sunday = false;
            }
        }
        else if (string.Equals(preset, "7Tage", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var row in ShiftRules.Where(x => x.IsEnabled))
                row.Monday = row.Tuesday = row.Wednesday = row.Thursday = row.Friday = row.Saturday = row.Sunday = true;
        }

        RefreshShiftModelText();
    }

    [RelayCommand]
    private void RefreshShiftModel() => RefreshShiftModelText();

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

        var configuredRules = ShiftRules
            .Where(x => x.IsEnabled && x.HasAnyDay)
            .ToList();
        if (IsActive && configuredRules.Count == 0)
        {
            StatusMessage = "Für einen aktiven Arbeitsplatz muss mindestens eine Schicht an mindestens einem Wochentag freigegeben sein.";
            return;
        }

        using var db = new AppDbContext();

        var incompatible = 0;
        if (SelectedWorkstation is not null &&
            HaveShiftRulesChanged(db, SelectedWorkstation.Id, configuredRules))
        {
            incompatible = CountIncompatibleFutureSlots(db, SelectedWorkstation.Id, configuredRules);
            if (incompatible > 0)
            {
                var answer = MessageBox.Show(
                    $"Durch dieses Schichtmodell liegen {incompatible} bereits geplante Produktionsschicht(en) künftig ausserhalb der Freigabe.\n\n" +
                    "Die bestehenden Produktionsschichten werden nicht automatisch verschoben. Schichtmodell trotzdem speichern?",
                    "Schichtmodell ändern",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    StatusMessage = "Speichern abgebrochen. Das bisherige Schichtmodell bleibt unverändert.";
                    return;
                }
            }
        }

        using var tx = db.Database.BeginTransaction();
        Workstation entity;
        try
        {
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

            // Bei Neuanlagen wird die Id für die nachfolgenden Schichtregeln benötigt.
            db.SaveChanges();

            var oldRules = db.WorkstationShiftRules
                .Where(x => x.WorkstationId == entity.Id)
                .ToList();
            if (oldRules.Count > 0)
                db.WorkstationShiftRules.RemoveRange(oldRules);

            foreach (var row in configuredRules)
            {
                db.WorkstationShiftRules.Add(new WorkstationShiftRule
                {
                    WorkstationId = entity.Id,
                    ShiftId = row.ShiftId,
                    Monday = row.Monday,
                    Tuesday = row.Tuesday,
                    Wednesday = row.Wednesday,
                    Thursday = row.Thursday,
                    Friday = row.Friday,
                    Saturday = row.Saturday,
                    Sunday = row.Sunday
                });
            }

            db.SaveChanges();
            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            StatusMessage = $"Arbeitsplatz und Schichtmodell wurden nicht gespeichert: {ex.Message}";
            return;
        }

        var entityId = entity.Id;
        var hasQualification = entity.RequiredQualificationId.HasValue;
        Load(entityId);
        StatusMessage = incompatible > 0
            ? $"Arbeitsplatz und Schichtmodell gespeichert. {incompatible} bestehende Produktionsschicht(en) liegen weiterhin ausserhalb des neuen Modells und wurden bewusst nicht verschoben."
            : hasQualification
                ? "Arbeitsplatz inklusive Qualifikationspflicht und Schichtmodell gespeichert."
                : "Arbeitsplatz inklusive Schichtmodell gespeichert.";
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
            .Include(x => x.ShiftRules)
                .ThenInclude(x => x.Shift)
            .OrderBy(x => x.Name)
            .ToList();
        Workstations.Clear();
        foreach (var item in items)
        {
            item.ShiftModelSummary = BuildShiftModelSummary(item.ShiftRules);
            Workstations.Add(item);
        }

        SelectedRequiredQualification = Qualifications.FirstOrDefault(x => x.Id == qualificationId)
                                        ?? Qualifications.FirstOrDefault(x => x.Id is null);
        SelectedRequiredQualificationLevel ??= SkillLevels[1];
        SelectedWorkstation = selectId is null ? null : Workstations.FirstOrDefault(x => x.Id == selectId);
        if (selectId is null && ShiftRules.Count == 0)
            LoadShiftRules(null);
    }

    private void LoadShiftRules(int? workstationId)
    {
        using var db = new AppDbContext();
        var shifts = db.Shifts.AsNoTracking()
            .AsEnumerable()
            .OrderBy(x => x.StartTime)
            .ThenBy(x => x.Name)
            .ToList();
        var saved = workstationId.HasValue
            ? db.WorkstationShiftRules.AsNoTracking().Where(x => x.WorkstationId == workstationId.Value).ToList()
            : new List<WorkstationShiftRule>();

        ShiftRules.Clear();
        foreach (var shift in shifts)
        {
            var rule = saved.FirstOrDefault(x => x.ShiftId == shift.Id);
            ShiftRules.Add(new WorkstationShiftRuleRow
            {
                ShiftId = shift.Id,
                ShiftName = shift.Name,
                TimeText = $"{shift.StartTime:hh\\:mm}–{shift.EndTime:hh\\:mm}",
                IsEnabled = rule is not null,
                Monday = rule?.Monday ?? false,
                Tuesday = rule?.Tuesday ?? false,
                Wednesday = rule?.Wednesday ?? false,
                Thursday = rule?.Thursday ?? false,
                Friday = rule?.Friday ?? false,
                Saturday = rule?.Saturday ?? false,
                Sunday = rule?.Sunday ?? false
            });
        }
        RefreshShiftModelText();
    }

    private static bool HaveShiftRulesChanged(
        AppDbContext db,
        int workstationId,
        IReadOnlyCollection<WorkstationShiftRuleRow> configuredRules)
    {
        var saved = db.WorkstationShiftRules.AsNoTracking()
            .Where(x => x.WorkstationId == workstationId)
            .ToList();
        if (saved.Count != configuredRules.Count)
            return true;

        foreach (var row in configuredRules)
        {
            var existing = saved.FirstOrDefault(x => x.ShiftId == row.ShiftId);
            if (existing is null ||
                existing.Monday != row.Monday ||
                existing.Tuesday != row.Tuesday ||
                existing.Wednesday != row.Wednesday ||
                existing.Thursday != row.Thursday ||
                existing.Friday != row.Friday ||
                existing.Saturday != row.Saturday ||
                existing.Sunday != row.Sunday)
                return true;
        }

        return false;
    }

    private static int CountIncompatibleFutureSlots(
        AppDbContext db,
        int workstationId,
        IReadOnlyCollection<WorkstationShiftRuleRow> configuredRules)
    {
        var futureSlots = db.ProductionRunSlots.AsNoTracking()
            .Include(x => x.ProductionOrder)
            .Where(x => x.ProductionOrder.WorkstationId == workstationId && x.Date.Date >= DateTime.Today)
            .ToList();

        return futureSlots.Count(slot =>
        {
            var rule = configuredRules.FirstOrDefault(x => x.ShiftId == slot.ShiftId);
            return rule is null || !rule.IsAllowed(slot.Date.DayOfWeek);
        });
    }

    private void RefreshShiftModelText() => ShiftModelText = BuildShiftModelSummary(ShiftRules);

    private static string BuildShiftModelSummary(IEnumerable<WorkstationShiftRule> rules)
    {
        var rows = rules.Select(x => new WorkstationShiftRuleRow
        {
            ShiftId = x.ShiftId,
            ShiftName = x.Shift?.Name ?? string.Empty,
            IsEnabled = true,
            Monday = x.Monday,
            Tuesday = x.Tuesday,
            Wednesday = x.Wednesday,
            Thursday = x.Thursday,
            Friday = x.Friday,
            Saturday = x.Saturday,
            Sunday = x.Sunday
        });
        return BuildShiftModelSummary(rows);
    }

    private static string BuildShiftModelSummary(IEnumerable<WorkstationShiftRuleRow> source)
    {
        var rows = source.Where(x => x.IsEnabled && x.HasAnyDay).ToList();
        if (rows.Count == 0)
            return "Nicht konfiguriert";

        var baseName = rows.Count == 1 && rows[0].ShiftName.Contains("tag", StringComparison.OrdinalIgnoreCase)
            ? "Tagschicht"
            : rows.Count == 1 ? "1-Schicht"
            : $"{rows.Count}-Schicht";

        var moFr = rows.All(x => x.Monday && x.Tuesday && x.Wednesday && x.Thursday && x.Friday && !x.Saturday && !x.Sunday);
        var allDays = rows.All(x => x.Monday && x.Tuesday && x.Wednesday && x.Thursday && x.Friday && x.Saturday && x.Sunday);
        var days = allDays ? "7 Tage" : moFr ? "Mo–Fr" : "individuell";
        return $"{baseName} · {days}";
    }
}

public sealed record WorkstationQualificationOption(int? Id, string Name);
public sealed record SkillLevelChoice(int Level, string Name);

public partial class WorkstationShiftRuleRow : ObservableObject
{
    public int ShiftId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] private bool monday;
    [ObservableProperty] private bool tuesday;
    [ObservableProperty] private bool wednesday;
    [ObservableProperty] private bool thursday;
    [ObservableProperty] private bool friday;
    [ObservableProperty] private bool saturday;
    [ObservableProperty] private bool sunday;

    public bool HasAnyDay => Monday || Tuesday || Wednesday || Thursday || Friday || Saturday || Sunday;

    public bool IsAllowed(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        DayOfWeek.Sunday => Sunday,
        _ => false
    };
}
