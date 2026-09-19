using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class ManufacturingControlViewModel : ObservableObject
{
    public ObservableCollection<OperationDefinition> Operations { get; } = new();
    public ObservableCollection<ManufacturingRouting> Routings { get; } = new();
    public ObservableCollection<RoutingStepRow> RoutingSteps { get; } = new();
    public ObservableCollection<ProductionOrderOption> ProductionOrders { get; } = new();
    public ObservableCollection<JobCardRow> JobCards { get; } = new();
    public ObservableCollection<CapacityRow> CapacityRows { get; } = new();
    public ObservableCollection<Workstation> Workstations { get; } = new();
    public ObservableCollection<Qualification> Qualifications { get; } = new();
    public ObservableCollection<Employee> Employees { get; } = new();
    public ObservableCollection<JobCardEmployeeChoice> JobCardEmployeeChoices { get; } = new();
    public ObservableCollection<OrderCockpitRow> CockpitOrders { get; } = new();
    public ObservableCollection<JobCardRow> CapacityJobCards { get; } = new();

    [ObservableProperty] private OperationDefinition? selectedOperation;
    [ObservableProperty] private string operationCode = string.Empty;
    [ObservableProperty] private string operationName = string.Empty;
    [ObservableProperty] private string operationDescription = string.Empty;
    [ObservableProperty] private Workstation? selectedDefaultWorkstation;
    [ObservableProperty] private double defaultMinutes = 30;
    [ObservableProperty] private int operationRequiredStaff = 1;
    [ObservableProperty] private Qualification? selectedRequiredQualification;
    [ObservableProperty] private int requiredQualificationLevel = 1;
    [ObservableProperty] private bool operationIsActive = true;

    [ObservableProperty] private ManufacturingRouting? selectedRouting;
    [ObservableProperty] private string routingProduct = string.Empty;
    [ObservableProperty] private string routingName = string.Empty;
    [ObservableProperty] private bool routingIsActive = true;
    [ObservableProperty] private OperationDefinition? selectedStepOperation;
    [ObservableProperty] private Workstation? selectedStepWorkstation;
    [ObservableProperty] private double stepMinutes = 30;
    [ObservableProperty] private int stepRequiredStaff = 1;

    [ObservableProperty] private ProductionOrderOption? selectedProductionOrder;
    [ObservableProperty] private JobCardRow? selectedJobCard;
    [ObservableProperty] private JobCardEmployeeChoice? selectedJobCardEmployeeChoice;
    [ObservableProperty] private string qualificationStatusText = "Keine Arbeitskarte ausgewählt.";
    [ObservableProperty] private string qualificationStatusKind = "Neutral";
    [ObservableProperty] private double finishGoodQuantity;
    [ObservableProperty] private double finishScrapQuantity;
    [ObservableProperty] private string jobCardComment = string.Empty;
    [ObservableProperty] private OrderCockpitRow? selectedCockpitOrder;
    [ObservableProperty] private bool showOnlyActionNeeded;
    [ObservableProperty] private bool showProcessView = true;
    [ObservableProperty] private CapacityRow? selectedCapacityRow;
    [ObservableProperty] private int orderProgressPercent;
    [ObservableProperty] private string orderProgressText = "0 / 0 Arbeitsgänge";
    [ObservableProperty] private string workflowPlanState = "Pending";
    [ObservableProperty] private string workflowReadyState = "Pending";
    [ObservableProperty] private string workflowProductionState = "Pending";
    [ObservableProperty] private string workflowQualityState = "Pending";
    [ObservableProperty] private string workflowDoneState = "Pending";
    [ObservableProperty] private string readinessStatusText = "Kein Auftrag ausgewählt";
    [ObservableProperty] private string readinessStatusKind = "Neutral";
    [ObservableProperty] private string workstationReadinessText = "⚪ Arbeitsplatz · –";
    [ObservableProperty] private string capacityReadinessText = "⚪ Kapazität · –";
    [ObservableProperty] private string staffReadinessText = "⚪ Personal · –";
    [ObservableProperty] private string skillReadinessText = "⚪ Skills · –";
    [ObservableProperty] private string materialReadinessText = "⚪ Material · nicht in KPI-rai geführt";
    [ObservableProperty] private string currentOperationText = "Kein aktiver Arbeitsgang";
    [ObservableProperty] private string nextOperationText = "–";

    [ObservableProperty] private string statusMessage = string.Empty;

    public ManufacturingControlViewModel()
    {
        LoadMasterData();
        LoadOperations();
        LoadRoutings();
        LoadProductionOrders();
        LoadJobCards();
        RefreshCapacity();
        LoadCockpit();
        NewOperation();
        NewRouting();
    }

    partial void OnSelectedOperationChanged(OperationDefinition? value)
    {
        if (value is null) return;
        OperationCode = value.Code;
        OperationName = value.Name;
        OperationDescription = value.Description ?? string.Empty;
        SelectedDefaultWorkstation = Workstations.FirstOrDefault(x => x.Id == value.DefaultWorkstationId);
        DefaultMinutes = value.DefaultMinutes;
        OperationRequiredStaff = value.RequiredStaff;
        SelectedRequiredQualification = Qualifications.FirstOrDefault(x => x.Id == value.RequiredQualificationId);
        RequiredQualificationLevel = value.RequiredQualificationLevel <= 0 ? 1 : value.RequiredQualificationLevel;
        OperationIsActive = value.IsActive;
        StatusMessage = string.Empty;
    }

    partial void OnSelectedRoutingChanged(ManufacturingRouting? value)
    {
        if (value is null)
        {
            RoutingSteps.Clear();
            return;
        }

        RoutingProduct = value.Product;
        RoutingName = value.Name;
        RoutingIsActive = value.IsActive;
        LoadRoutingSteps(value.Id);
        StatusMessage = string.Empty;
    }

    partial void OnSelectedProductionOrderChanged(ProductionOrderOption? value)
    {
        LoadJobCards(value?.Id);
        if (value is not null)
        {
            var routing = Routings.FirstOrDefault(x =>
                x.IsActive && string.Equals(x.Product.Trim(), value.Product.Trim(), StringComparison.OrdinalIgnoreCase));
            if (routing is not null)
                SelectedRouting = routing;
        }
        UpdateOrderWorkflow();
    }

    partial void OnSelectedCockpitOrderChanged(OrderCockpitRow? value)
    {
        if (value is null) return;
        SelectedProductionOrder = ProductionOrders.FirstOrDefault(x => x.Id == value.Id);
    }

    partial void OnShowOnlyActionNeededChanged(bool value) => LoadCockpit();

    partial void OnSelectedCapacityRowChanged(CapacityRow? value) => LoadCapacityJobCards(value?.WorkstationId);

    partial void OnSelectedJobCardChanged(JobCardRow? value)
    {
        LoadJobCardEmployeeChoices(value, value?.EmployeeId);
        FinishGoodQuantity = value?.GoodQuantity ?? 0;
        FinishScrapQuantity = value?.ScrapQuantity ?? 0;
        JobCardComment = value?.Comment ?? string.Empty;
    }

    partial void OnSelectedJobCardEmployeeChoiceChanged(JobCardEmployeeChoice? value)
    {
        UpdateQualificationStatus(SelectedJobCard, value);
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadMasterData();
        LoadOperations();
        LoadRoutings();
        LoadProductionOrders();
        LoadJobCards(SelectedProductionOrder?.Id);
        RefreshCapacity();
        LoadCockpit();
        UpdateOrderWorkflow();
        StatusMessage = "Fertigungssteuerung aktualisiert.";
    }

    [RelayCommand]
    private void ShowProcess()
    {
        ShowProcessView = true;
    }

    [RelayCommand]
    private void ShowTable()
    {
        ShowProcessView = false;
    }

    [RelayCommand]
    private void SelectJobCard(JobCardRow? row)
    {
        if (row is not null)
            SelectedJobCard = row;
    }

    [RelayCommand]
    private void AutoAssignBestEmployee()
    {
        if (SelectedJobCard is null)
        {
            StatusMessage = "Bitte zuerst eine Arbeitskarte auswählen.";
            return;
        }

        var best = JobCardEmployeeChoices
            .Where(x => x.IsQualified && !x.IsAbsent)
            .OrderByDescending(x => x.IsPlannedAtWorkstation)
            .ThenBy(x => x.ActiveJobCards)
            .ThenByDescending(x => x.QualificationLevel)
            .ThenBy(x => x.DisplayName)
            .FirstOrDefault();

        if (best is null)
        {
            StatusMessage = "Kein verfügbarer und ausreichend qualifizierter Mitarbeiter gefunden.";
            return;
        }

        SelectedJobCardEmployeeChoice = best;
        StatusMessage = $"Vorschlag: {best.DisplayName} wurde anhand Skill, Verfügbarkeit und Auslastung ausgewählt.";
    }

    public void MoveRoutingStep(int sourceId, int targetId)
    {
        if (!EnsurePlanner() || SelectedRouting is null || sourceId == targetId) return;

        using var db = new AppDbContext();
        var steps = db.RoutingSteps
            .Where(x => x.ManufacturingRoutingId == SelectedRouting.Id)
            .OrderBy(x => x.SequenceNumber)
            .ToList();
        var source = steps.FirstOrDefault(x => x.Id == sourceId);
        var target = steps.FirstOrDefault(x => x.Id == targetId);
        if (source is null || target is null) return;

        steps.Remove(source);
        var targetIndex = steps.IndexOf(target);
        steps.Insert(Math.Max(0, targetIndex), source);

        using var transaction = db.Database.BeginTransaction();
        for (var i = 0; i < steps.Count; i++)
            steps[i].SequenceNumber = -(i + 1);
        db.SaveChanges();

        for (var i = 0; i < steps.Count; i++)
            steps[i].SequenceNumber = (i + 1) * 10;
        db.SaveChanges();
        transaction.Commit();

        LoadRoutingSteps(SelectedRouting.Id);
        StatusMessage = "Arbeitsplan-Reihenfolge aktualisiert.";
    }

    [RelayCommand]
    private void NewOperation()
    {
        SelectedOperation = null;
        OperationCode = string.Empty;
        OperationName = string.Empty;
        OperationDescription = string.Empty;
        SelectedDefaultWorkstation = Workstations.FirstOrDefault();
        DefaultMinutes = 30;
        OperationRequiredStaff = 1;
        SelectedRequiredQualification = null;
        RequiredQualificationLevel = 1;
        OperationIsActive = true;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void SaveOperation()
    {
        if (!EnsurePlanner()) return;

        var code = OperationCode.Trim();
        var name = OperationName.Trim();
        if (code.Length == 0 || name.Length == 0)
        {
            StatusMessage = "Code und Bezeichnung des Arbeitsgangs sind Pflichtfelder.";
            return;
        }
        if (!double.IsFinite(DefaultMinutes) || DefaultMinutes <= 0 || DefaultMinutes > 100000)
        {
            StatusMessage = "Die Sollzeit muss grösser als 0 Minuten sein.";
            return;
        }
        if (OperationRequiredStaff < 1 || OperationRequiredStaff > 100)
        {
            StatusMessage = "Der Personalbedarf muss zwischen 1 und 100 liegen.";
            return;
        }

        using var db = new AppDbContext();
        var id = SelectedOperation?.Id ?? 0;
        if (db.OperationDefinitions.Any(x => x.Code == code && x.Id != id))
        {
            StatusMessage = $"Der Arbeitsgang-Code {code} existiert bereits.";
            return;
        }

        OperationDefinition entity;
        if (id == 0)
        {
            entity = new OperationDefinition();
            db.OperationDefinitions.Add(entity);
        }
        else
        {
            entity = db.OperationDefinitions.First(x => x.Id == id);
        }

        entity.Code = code;
        entity.Name = name;
        entity.Description = string.IsNullOrWhiteSpace(OperationDescription) ? null : OperationDescription.Trim();
        entity.DefaultWorkstationId = SelectedDefaultWorkstation?.Id;
        entity.DefaultMinutes = DefaultMinutes;
        entity.RequiredStaff = OperationRequiredStaff;
        entity.RequiredQualificationId = SelectedRequiredQualification?.Id;
        entity.RequiredQualificationLevel = SelectedRequiredQualification is null ? 0 : Math.Max(1, RequiredQualificationLevel);
        entity.IsActive = OperationIsActive;
        db.SaveChanges();

        LoadOperations(entity.Id);
        StatusMessage = "Arbeitsgang gespeichert.";
    }

    [RelayCommand]
    private void NewRouting()
    {
        SelectedRouting = null;
        RoutingProduct = string.Empty;
        RoutingName = string.Empty;
        RoutingIsActive = true;
        RoutingSteps.Clear();
        SelectedStepOperation = Operations.FirstOrDefault(x => x.IsActive);
        ApplyOperationDefaultsToStep();
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void SaveRouting()
    {
        if (!EnsurePlanner()) return;

        var product = RoutingProduct.Trim();
        var name = RoutingName.Trim();
        if (product.Length == 0 || name.Length == 0)
        {
            StatusMessage = "Produkt und Name des Arbeitsplans sind Pflichtfelder.";
            return;
        }

        using var db = new AppDbContext();
        ManufacturingRouting entity;
        if (SelectedRouting is null)
        {
            entity = new ManufacturingRouting();
            db.ManufacturingRoutings.Add(entity);
        }
        else
        {
            entity = db.ManufacturingRoutings.First(x => x.Id == SelectedRouting.Id);
        }

        entity.Product = product;
        entity.Name = name;
        entity.IsActive = RoutingIsActive;
        db.SaveChanges();
        LoadRoutings(entity.Id);
        StatusMessage = "Arbeitsplan gespeichert.";
    }

    [RelayCommand]
    private void UseOperationDefaults()
    {
        ApplyOperationDefaultsToStep();
    }

    private void ApplyOperationDefaultsToStep()
    {
        if (SelectedStepOperation is null) return;
        SelectedStepWorkstation = Workstations.FirstOrDefault(x => x.Id == SelectedStepOperation.DefaultWorkstationId)
                                  ?? Workstations.FirstOrDefault();
        StepMinutes = SelectedStepOperation.DefaultMinutes;
        StepRequiredStaff = Math.Max(1, SelectedStepOperation.RequiredStaff);
    }

    [RelayCommand]
    private void AddRoutingStep()
    {
        if (!EnsurePlanner()) return;
        if (SelectedRouting is null)
        {
            StatusMessage = "Bitte den Arbeitsplan zuerst speichern.";
            return;
        }
        if (SelectedStepOperation is null || SelectedStepWorkstation is null)
        {
            StatusMessage = "Arbeitsgang und Arbeitsplatz auswählen.";
            return;
        }
        if (!double.IsFinite(StepMinutes) || StepMinutes <= 0 || StepRequiredStaff < 1)
        {
            StatusMessage = "Sollzeit und Personalbedarf des Schritts prüfen.";
            return;
        }

        using var db = new AppDbContext();
        var nextSequence = db.RoutingSteps
            .Where(x => x.ManufacturingRoutingId == SelectedRouting.Id)
            .Select(x => (int?)x.SequenceNumber)
            .Max() ?? 0;
        nextSequence = nextSequence == 0 ? 10 : nextSequence + 10;

        db.RoutingSteps.Add(new RoutingStep
        {
            ManufacturingRoutingId = SelectedRouting.Id,
            OperationDefinitionId = SelectedStepOperation.Id,
            SequenceNumber = nextSequence,
            WorkstationId = SelectedStepWorkstation.Id,
            PlannedMinutes = StepMinutes,
            RequiredStaff = StepRequiredStaff
        });
        db.SaveChanges();
        LoadRoutingSteps(SelectedRouting.Id);
        StatusMessage = "Arbeitsgang zum Arbeitsplan hinzugefügt.";
    }

    [RelayCommand]
    private void DeleteRoutingStep(RoutingStepRow? row)
    {
        if (!EnsurePlanner()) return;
        if (row is null || SelectedRouting is null) return;

        using var db = new AppDbContext();
        if (db.JobCards.Any(x => x.RoutingStepId == row.Id))
        {
            StatusMessage = "Dieser Arbeitsplan-Schritt wird bereits von Arbeitskarten verwendet und kann nicht gelöscht werden.";
            return;
        }
        var entity = db.RoutingSteps.FirstOrDefault(x => x.Id == row.Id);
        if (entity is null) return;
        db.RoutingSteps.Remove(entity);
        db.SaveChanges();
        LoadRoutingSteps(SelectedRouting.Id);
        StatusMessage = "Arbeitsplan-Schritt gelöscht.";
    }

    [RelayCommand]
    private void GenerateJobCards()
    {
        if (!EnsurePlanner()) return;
        if (SelectedProductionOrder is null)
        {
            StatusMessage = "Bitte einen Produktionsauftrag auswählen.";
            return;
        }

        using var db = new AppDbContext();
        var order = db.ProductionOrders.AsNoTracking().First(x => x.Id == SelectedProductionOrder.Id);
        if (db.JobCards.Any(x => x.ProductionOrderId == order.Id))
        {
            StatusMessage = "Für diesen Auftrag existieren bereits Arbeitskarten.";
            return;
        }

        var routing = db.ManufacturingRoutings.AsNoTracking()
            .Where(x => x.IsActive)
            .AsEnumerable()
            .FirstOrDefault(x => string.Equals(x.Product.Trim(), order.Product.Trim(), StringComparison.OrdinalIgnoreCase));
        if (routing is null)
        {
            StatusMessage = $"Kein aktiver Arbeitsplan für Produkt „{order.Product}“ gefunden.";
            return;
        }

        var steps = db.RoutingSteps.AsNoTracking()
            .Include(x => x.OperationDefinition)
                .ThenInclude(x => x.RequiredQualification)
            .Where(x => x.ManufacturingRoutingId == routing.Id)
            .OrderBy(x => x.SequenceNumber)
            .ToList();
        if (steps.Count == 0)
        {
            StatusMessage = "Der Arbeitsplan enthält noch keine Arbeitsgänge.";
            return;
        }

        foreach (var step in steps)
        {
            db.JobCards.Add(new JobCard
            {
                ProductionOrderId = order.Id,
                RoutingStepId = step.Id,
                SequenceNumber = step.SequenceNumber,
                OperationCode = step.OperationDefinition.Code,
                OperationName = step.OperationDefinition.Name,
                WorkstationId = step.WorkstationId,
                RequiredQualificationId = step.OperationDefinition.RequiredQualificationId,
                RequiredQualificationNameSnapshot = step.OperationDefinition.RequiredQualification?.Name,
                RequiredQualificationLevel = step.OperationDefinition.RequiredQualificationId.HasValue
                    ? Math.Max(1, step.OperationDefinition.RequiredQualificationLevel)
                    : 0,
                PlannedMinutes = step.PlannedMinutes,
                RequiredStaff = step.RequiredStaff,
                Status = "Bereit"
            });
        }

        var trackedOrder = db.ProductionOrders.First(x => x.Id == order.Id);
        trackedOrder.Status = "Bereit";
        db.SaveChanges();
        LoadProductionOrders();
        LoadJobCards(order.Id);
        RefreshCapacity();
        LoadCockpit();
        UpdateOrderWorkflow();
        StatusMessage = $"{steps.Count} Arbeitskarte(n) aus dem Arbeitsplan erzeugt.";
    }

    [RelayCommand]
    private void StartJobCard()
    {
        if (!EnsurePlanner() || SelectedJobCard is null) return;
        using var db = new AppDbContext();
        var card = db.JobCards.First(x => x.Id == SelectedJobCard.Id);
        if (card.Status == "Fertig")
        {
            StatusMessage = "Die Arbeitskarte ist bereits abgeschlossen.";
            return;
        }
        if (card.Status == "In Produktion")
        {
            StatusMessage = "Die Arbeitskarte läuft bereits.";
            return;
        }

        if (!TryResolveQualifiedEmployee(db, card, out var employeeId, out var qualificationMessage))
        {
            StatusMessage = qualificationMessage;
            return;
        }

        card.EmployeeId = employeeId;
        card.StartedAtUtc = DateTime.UtcNow;
        card.PauseStartedAtUtc = null;
        card.Status = "In Produktion";
        card.Comment = string.IsNullOrWhiteSpace(JobCardComment) ? card.Comment : JobCardComment.Trim();
        db.ProductionOrders.First(x => x.Id == card.ProductionOrderId).Status = "Läuft";
        db.SaveChanges();
        LoadProductionOrders();
        LoadJobCards(card.ProductionOrderId, card.Id);
        LoadCockpit();
        UpdateOrderWorkflow();
        StatusMessage = "Arbeitskarte gestartet.";
    }

    [RelayCommand]
    private void PauseJobCard()
    {
        if (!EnsurePlanner() || SelectedJobCard is null) return;
        using var db = new AppDbContext();
        var card = db.JobCards.First(x => x.Id == SelectedJobCard.Id);
        if (card.Status != "In Produktion" || !card.StartedAtUtc.HasValue)
        {
            StatusMessage = "Nur eine laufende Arbeitskarte kann pausiert werden.";
            return;
        }

        card.RunMinutes += Math.Max(0, (DateTime.UtcNow - card.StartedAtUtc.Value).TotalMinutes);
        card.StartedAtUtc = null;
        card.PauseStartedAtUtc = DateTime.UtcNow;
        card.Status = "Pausiert";
        db.ProductionOrders.First(x => x.Id == card.ProductionOrderId).Status = "Pausiert";
        db.SaveChanges();
        LoadProductionOrders();
        LoadJobCards(card.ProductionOrderId, card.Id);
        LoadCockpit();
        UpdateOrderWorkflow();
        StatusMessage = "Arbeitskarte pausiert.";
    }

    [RelayCommand]
    private void FinishJobCard()
    {
        if (!EnsurePlanner() || SelectedJobCard is null) return;
        if (FinishGoodQuantity < 0 || FinishScrapQuantity < 0)
        {
            StatusMessage = "Gut- und Ausschussmenge dürfen nicht negativ sein.";
            return;
        }

        using var db = new AppDbContext();
        var card = db.JobCards.First(x => x.Id == SelectedJobCard.Id);
        if (card.Status == "Fertig")
        {
            StatusMessage = "Die Arbeitskarte ist bereits abgeschlossen.";
            return;
        }

        if (!TryResolveQualifiedEmployee(db, card, out var employeeId, out var qualificationMessage))
        {
            StatusMessage = qualificationMessage;
            return;
        }

        if (card.Status == "In Produktion" && card.StartedAtUtc.HasValue)
            card.RunMinutes += Math.Max(0, (DateTime.UtcNow - card.StartedAtUtc.Value).TotalMinutes);

        card.StartedAtUtc = null;
        card.PauseStartedAtUtc = null;
        card.EmployeeId = employeeId;
        card.GoodQuantity = FinishGoodQuantity;
        card.ScrapQuantity = FinishScrapQuantity;
        card.Comment = string.IsNullOrWhiteSpace(JobCardComment) ? null : JobCardComment.Trim();
        card.CompletedAtUtc = DateTime.UtcNow;
        card.Status = "Fertig";
        db.SaveChanges();

        var nextCardId = db.JobCards
            .Where(x => x.ProductionOrderId == card.ProductionOrderId && x.Status != "Fertig" && x.SequenceNumber > card.SequenceNumber)
            .OrderBy(x => x.SequenceNumber)
            .Select(x => (int?)x.Id)
            .FirstOrDefault();
        nextCardId ??= db.JobCards
            .Where(x => x.ProductionOrderId == card.ProductionOrderId && x.Status != "Fertig")
            .OrderBy(x => x.SequenceNumber)
            .Select(x => (int?)x.Id)
            .FirstOrDefault();

        var remaining = nextCardId.HasValue;
        db.ProductionOrders.First(x => x.Id == card.ProductionOrderId).Status = remaining ? "Läuft" : "Abgeschlossen";
        db.SaveChanges();

        LoadProductionOrders();
        LoadJobCards(card.ProductionOrderId, nextCardId);
        RefreshCapacity();
        LoadCockpit();
        UpdateOrderWorkflow();
        StatusMessage = nextCardId.HasValue
            ? $"Arbeitsgang abgeschlossen. Nächster Arbeitsgang: {SelectedJobCard?.OperationName ?? "bereit"}."
            : "Arbeitsgang abgeschlossen. Auftrag vollständig fertig.";
    }

    [RelayCommand]
    private void RefreshCapacity()
    {
        using var db = new AppDbContext();
        var selectedId = SelectedCapacityRow?.WorkstationId;
        var start = DateTime.Today;
        var end = start.AddDays(7);
        var workstations = db.Workstations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToList();
        var rules = db.WorkstationShiftRules.AsNoTracking().Include(x => x.Shift).ToList();
        var openCards = db.JobCards.AsNoTracking()
            .Include(x => x.ProductionOrder)
            .Where(x => x.Status != "Fertig" && x.ProductionOrder.PlannedDate >= start && x.ProductionOrder.PlannedDate < end)
            .ToList();

        CapacityRows.Clear();
        foreach (var workstation in workstations)
        {
            var stationRules = rules.Where(x => x.WorkstationId == workstation.Id).ToList();
            double availableMinutes = 0;
            for (var day = start; day < end; day = day.AddDays(1))
            {
                foreach (var rule in stationRules.Where(x => IsAllowed(x, day.DayOfWeek)))
                    availableMinutes += NetShiftMinutes(rule.Shift);
            }

            var stationCards = openCards.Where(x => x.WorkstationId == workstation.Id).ToList();
            var loadMinutes = stationCards.Sum(x => x.PlannedMinutes);
            var utilization = availableMinutes > 0 ? loadMinutes / availableMinutes * 100d : (loadMinutes > 0 ? 999d : 0d);
            CapacityRows.Add(new CapacityRow
            {
                WorkstationId = workstation.Id,
                WorkstationName = workstation.Name,
                Area = workstation.Area,
                AvailableMinutes = availableMinutes,
                PlannedMinutes = loadMinutes,
                OpenJobCards = stationCards.Count,
                UtilizationPercent = utilization,
                Status = utilization >= 100 ? "Überlastet" : utilization >= 85 ? "Knapp" : "Frei"
            });
        }

        SelectedCapacityRow = selectedId.HasValue
            ? CapacityRows.FirstOrDefault(x => x.WorkstationId == selectedId.Value)
            : CapacityRows.FirstOrDefault();
    }

    private void LoadCockpit()
    {
        using var db = new AppDbContext();
        var selectedId = SelectedProductionOrder?.Id ?? SelectedCockpitOrder?.Id;
        var orders = db.ProductionOrders.AsNoTracking()
            .Include(x => x.Workstation)
            .OrderBy(x => x.PlannedDate)
            .ThenBy(x => x.OrderNumber)
            .ToList();
        var cards = db.JobCards.AsNoTracking()
            .Include(x => x.RequiredQualification)
            .ToList();
        var activeEmployees = db.Employees.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToHashSet();
        var qualifications = db.EmployeeQualifications.AsNoTracking().ToList();
        var capacity = CapacityRows.ToDictionary(x => x.WorkstationId);

        var rows = new List<OrderCockpitRow>();
        foreach (var order in orders)
        {
            var orderCards = cards.Where(x => x.ProductionOrderId == order.Id).OrderBy(x => x.SequenceNumber).ToList();
            var openCards = orderCards.Where(x => x.Status != "Fertig").ToList();
            var completed = orderCards.Count(x => x.Status == "Fertig");
            var total = orderCards.Count;
            var progress = total == 0 ? 0 : (int)Math.Round(completed * 100d / total);
            var missingSkill = openCards.Any(card =>
                card.RequiredQualificationId.HasValue && card.RequiredQualificationLevel > 0 &&
                !qualifications.Any(q => activeEmployees.Contains(q.EmployeeId) &&
                                         q.QualificationId == card.RequiredQualificationId.Value &&
                                         q.Level >= card.RequiredQualificationLevel));
            var unassigned = openCards.Any(x => !x.EmployeeId.HasValue);
            var paused = openCards.Any(x => x.Status == "Pausiert");
            var noCards = total == 0;
            var stationIds = openCards.Select(x => x.WorkstationId).Distinct().ToArray();
            var highestUtilization = stationIds
                .Where(capacity.ContainsKey)
                .Select(id => capacity[id].UtilizationPercent)
                .DefaultIfEmpty(0)
                .Max();
            var capacityCritical = highestUtilization >= 100;
            var capacityTight = highestUtilization >= 85;
            var issueCount = (noCards ? 1 : 0) + (missingSkill ? 1 : 0) + (unassigned ? 1 : 0) +
                             (paused ? 1 : 0) + (capacityCritical ? 1 : capacityTight ? 1 : 0);
            var readinessKind = noCards || missingSkill || capacityCritical
                ? "Danger"
                : unassigned || paused || capacityTight
                    ? "Warning"
                    : "Success";
            var readinessText = readinessKind switch
            {
                "Danger" => "ROT · Handlungsbedarf",
                "Warning" => "GELB · bedingt bereit",
                _ => total > 0 && completed == total ? "GRÜN · abgeschlossen" : "GRÜN · bereit"
            };
            var current = orderCards.FirstOrDefault(x => x.Status == "In Produktion")
                          ?? orderCards.FirstOrDefault(x => x.Status != "Fertig");

            var row = new OrderCockpitRow
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                Product = order.Product,
                PlannedDate = order.PlannedDate,
                WorkstationName = order.Workstation.Name,
                Status = order.Status,
                ProgressPercent = progress,
                CompletedSteps = completed,
                TotalSteps = total,
                IssueCount = issueCount,
                ReadinessKind = readinessKind,
                ReadinessText = readinessText,
                CurrentOperation = current?.OperationName ?? (total > 0 ? "Alle Arbeitsgänge abgeschlossen" : "Arbeitskarten fehlen"),
                WorkstationCheck = $"🟢 Arbeitsplatz · {order.Workstation.Name}",
                CapacityCheck = capacityCritical
                    ? $"🔴 Kapazität · {highestUtilization:N0} % überlastet"
                    : capacityTight
                        ? $"🟡 Kapazität · {highestUtilization:N0} %"
                        : $"🟢 Kapazität · {highestUtilization:N0} %",
                StaffCheck = unassigned ? "🟡 Personal · Zuweisung offen" : "🟢 Personal · zugewiesen",
                SkillCheck = missingSkill ? "🔴 Skills · Qualifikation fehlt" : "🟢 Skills · verfügbar"
            };
            if (!ShowOnlyActionNeeded || row.IssueCount > 0)
                rows.Add(row);
        }

        CockpitOrders.Clear();
        foreach (var row in rows) CockpitOrders.Add(row);

        SelectedCockpitOrder = selectedId.HasValue
            ? CockpitOrders.FirstOrDefault(x => x.Id == selectedId.Value) ?? CockpitOrders.FirstOrDefault()
            : CockpitOrders.FirstOrDefault();
        UpdateOrderWorkflow();
    }

    private void UpdateOrderWorkflow()
    {
        if (SelectedProductionOrder is null)
        {
            OrderProgressPercent = 0;
            OrderProgressText = "0 / 0 Arbeitsgänge";
            WorkflowPlanState = WorkflowReadyState = WorkflowProductionState = WorkflowQualityState = WorkflowDoneState = "Pending";
            ReadinessStatusKind = "Neutral";
            ReadinessStatusText = "Kein Auftrag ausgewählt";
            CurrentOperationText = "Kein aktiver Arbeitsgang";
            NextOperationText = "–";
            return;
        }

        var cards = JobCards.OrderBy(x => x.SequenceNumber).ToList();
        var total = cards.Count;
        var completed = cards.Count(x => x.Status == "Fertig");
        var active = cards.FirstOrDefault(x => x.Status == "In Produktion")
                     ?? cards.FirstOrDefault(x => x.Status == "Pausiert")
                     ?? cards.FirstOrDefault(x => x.Status != "Fertig");
        var quality = cards.FirstOrDefault(x =>
            x.OperationName.Contains("qual", StringComparison.OrdinalIgnoreCase) ||
            x.OperationName.Contains("prüf", StringComparison.OrdinalIgnoreCase) ||
            x.OperationCode.Contains("QS", StringComparison.OrdinalIgnoreCase));

        OrderProgressPercent = total == 0 ? 0 : (int)Math.Round(completed * 100d / total);
        OrderProgressText = $"{completed} / {total} Arbeitsgänge";
        WorkflowPlanState = "Done";
        WorkflowReadyState = total > 0 ? "Done" : "Pending";
        WorkflowProductionState = total == 0 ? "Pending"
            : completed == total ? "Done"
            : cards.Any(x => x.Status is "In Produktion" or "Pausiert") || completed > 0 ? "Active" : "Pending";
        WorkflowQualityState = quality is null ? "Pending"
            : quality.Status == "Fertig" ? "Done"
            : quality.Status is "In Produktion" or "Pausiert" ? "Active" : "Pending";
        WorkflowDoneState = total > 0 && completed == total ? "Done" : "Pending";

        CurrentOperationText = active is null
            ? total > 0 ? "Alle Arbeitsgänge abgeschlossen" : "Noch keine Arbeitskarten"
            : $"{active.SequenceNumber} · {active.OperationName} · {active.Status}";
        var next = active is null ? null : cards.FirstOrDefault(x => x.SequenceNumber > active.SequenceNumber && x.Status != "Fertig");
        NextOperationText = next is null ? (completed == total && total > 0 ? "Auftrag fertig" : "–") : $"{next.SequenceNumber} · {next.OperationName}";

        var cockpit = CockpitOrders.FirstOrDefault(x => x.Id == SelectedProductionOrder.Id);
        if (cockpit is not null)
        {
            ReadinessStatusKind = cockpit.ReadinessKind;
            ReadinessStatusText = cockpit.ReadinessText;
            WorkstationReadinessText = cockpit.WorkstationCheck;
            CapacityReadinessText = cockpit.CapacityCheck;
            StaffReadinessText = cockpit.StaffCheck;
            SkillReadinessText = cockpit.SkillCheck;
        }

        if (SelectedJobCard is null || SelectedJobCard.ProductionOrderId != SelectedProductionOrder.Id)
            SelectedJobCard = cards.FirstOrDefault(x => x.Status != "Fertig") ?? cards.LastOrDefault();
    }

    private void LoadCapacityJobCards(int? workstationId)
    {
        CapacityJobCards.Clear();
        if (!workstationId.HasValue) return;

        using var db = new AppDbContext();
        var start = DateTime.Today;
        var end = start.AddDays(7);
        var rows = db.JobCards.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Employee)
            .Include(x => x.RequiredQualification)
            .Include(x => x.ProductionOrder)
            .Where(x => x.WorkstationId == workstationId.Value &&
                        x.Status != "Fertig" &&
                        x.ProductionOrder.PlannedDate >= start &&
                        x.ProductionOrder.PlannedDate < end)
            .OrderBy(x => x.ProductionOrder.PlannedDate)
            .ThenBy(x => x.ProductionOrder.OrderNumber)
            .ThenBy(x => x.SequenceNumber)
            .ToList();

        foreach (var x in rows)
        {
            CapacityJobCards.Add(new JobCardRow
            {
                Id = x.Id,
                ProductionOrderId = x.ProductionOrderId,
                OrderNumber = x.ProductionOrder.OrderNumber,
                Product = x.ProductionOrder.Product,
                SequenceNumber = x.SequenceNumber,
                OperationCode = x.OperationCode,
                OperationName = x.OperationName,
                WorkstationId = x.WorkstationId,
                WorkstationName = x.Workstation.Name,
                PlannedDate = x.ProductionOrder.PlannedDate,
                EmployeeId = x.EmployeeId,
                EmployeeName = x.Employee is null ? "–" : $"{x.Employee.LastName}, {x.Employee.FirstName}",
                RequiredQualificationId = x.RequiredQualificationId,
                RequiredQualificationName = x.RequiredQualificationNameSnapshot ?? x.RequiredQualification?.Name ?? string.Empty,
                RequiredQualificationLevel = x.RequiredQualificationLevel,
                PlannedMinutes = x.PlannedMinutes,
                RunMinutes = x.RunMinutes,
                RequiredStaff = x.RequiredStaff,
                Status = x.Status,
                GoodQuantity = x.GoodQuantity,
                ScrapQuantity = x.ScrapQuantity,
                Comment = x.Comment
            });
        }
    }

    private void LoadMasterData()
    {
        using var db = new AppDbContext();
        var workstationId = SelectedDefaultWorkstation?.Id;
        var qualificationId = SelectedRequiredQualification?.Id;

        Workstations.Clear();
        foreach (var x in db.Workstations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name))
            Workstations.Add(x);

        Qualifications.Clear();
        foreach (var x in db.Qualifications.AsNoTracking().OrderBy(x => x.Name))
            Qualifications.Add(x);

        Employees.Clear();
        foreach (var x in db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.LastName).ThenBy(x => x.FirstName))
            Employees.Add(x);

        SelectedDefaultWorkstation = Workstations.FirstOrDefault(x => x.Id == workstationId) ?? Workstations.FirstOrDefault();
        SelectedRequiredQualification = Qualifications.FirstOrDefault(x => x.Id == qualificationId);
    }

    private void LoadOperations(int? selectId = null)
    {
        using var db = new AppDbContext();
        var currentId = selectId ?? SelectedOperation?.Id;
        var items = db.OperationDefinitions.AsNoTracking().OrderBy(x => x.Code).ToList();
        Operations.Clear();
        foreach (var item in items) Operations.Add(item);
        SelectedOperation = currentId.HasValue ? Operations.FirstOrDefault(x => x.Id == currentId.Value) : null;
        SelectedStepOperation ??= Operations.FirstOrDefault(x => x.IsActive);
    }

    private void LoadRoutings(int? selectId = null)
    {
        using var db = new AppDbContext();
        var currentId = selectId ?? SelectedRouting?.Id;
        var items = db.ManufacturingRoutings.AsNoTracking().OrderBy(x => x.Product).ThenBy(x => x.Name).ToList();
        Routings.Clear();
        foreach (var item in items) Routings.Add(item);
        SelectedRouting = currentId.HasValue ? Routings.FirstOrDefault(x => x.Id == currentId.Value) : null;
    }

    private void LoadRoutingSteps(int routingId)
    {
        using var db = new AppDbContext();
        var rows = db.RoutingSteps.AsNoTracking()
            .Include(x => x.OperationDefinition)
            .Include(x => x.Workstation)
            .Where(x => x.ManufacturingRoutingId == routingId)
            .OrderBy(x => x.SequenceNumber)
            .ToList();
        RoutingSteps.Clear();
        foreach (var x in rows)
        {
            RoutingSteps.Add(new RoutingStepRow
            {
                Id = x.Id,
                SequenceNumber = x.SequenceNumber,
                OperationCode = x.OperationDefinition.Code,
                OperationName = x.OperationDefinition.Name,
                WorkstationName = x.Workstation.Name,
                PlannedMinutes = x.PlannedMinutes,
                RequiredStaff = x.RequiredStaff
            });
        }
    }

    private void LoadProductionOrders()
    {
        using var db = new AppDbContext();
        var selectedId = SelectedProductionOrder?.Id;
        var items = db.ProductionOrders.AsNoTracking()
            .OrderByDescending(x => x.PlannedDate)
            .ThenBy(x => x.OrderNumber)
            .Select(x => new ProductionOrderOption
            {
                Id = x.Id,
                OrderNumber = x.OrderNumber,
                Product = x.Product,
                PlannedDate = x.PlannedDate,
                Status = x.Status
            }).ToList();

        ProductionOrders.Clear();
        foreach (var item in items) ProductionOrders.Add(item);
        SelectedProductionOrder = selectedId.HasValue
            ? ProductionOrders.FirstOrDefault(x => x.Id == selectedId.Value)
            : ProductionOrders.FirstOrDefault();
    }

    private void LoadJobCards(int? orderId = null, int? selectId = null)
    {
        using var db = new AppDbContext();
        var query = db.JobCards.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Employee)
            .Include(x => x.RequiredQualification)
            .Include(x => x.ProductionOrder)
            .AsQueryable();
        if (orderId.HasValue)
            query = query.Where(x => x.ProductionOrderId == orderId.Value);

        var items = query.OrderByDescending(x => x.ProductionOrder.PlannedDate)
            .ThenBy(x => x.ProductionOrder.OrderNumber)
            .ThenBy(x => x.SequenceNumber)
            .ToList();

        JobCards.Clear();
        foreach (var x in items)
        {
            JobCards.Add(new JobCardRow
            {
                Id = x.Id,
                ProductionOrderId = x.ProductionOrderId,
                OrderNumber = x.ProductionOrder.OrderNumber,
                Product = x.ProductionOrder.Product,
                SequenceNumber = x.SequenceNumber,
                OperationCode = x.OperationCode,
                OperationName = x.OperationName,
                WorkstationId = x.WorkstationId,
                WorkstationName = x.Workstation.Name,
                PlannedDate = x.ProductionOrder.PlannedDate,
                EmployeeId = x.EmployeeId,
                EmployeeName = x.Employee is null ? "–" : $"{x.Employee.LastName}, {x.Employee.FirstName}",
                RequiredQualificationId = x.RequiredQualificationId,
                RequiredQualificationName = x.RequiredQualificationNameSnapshot ?? x.RequiredQualification?.Name ?? string.Empty,
                RequiredQualificationLevel = x.RequiredQualificationLevel,
                PlannedMinutes = x.PlannedMinutes,
                RunMinutes = x.RunMinutes,
                RequiredStaff = x.RequiredStaff,
                Status = x.Status,
                GoodQuantity = x.GoodQuantity,
                ScrapQuantity = x.ScrapQuantity,
                Comment = x.Comment
            });
        }

        SelectedJobCard = selectId.HasValue ? JobCards.FirstOrDefault(x => x.Id == selectId.Value) : null;
    }

    private void LoadJobCardEmployeeChoices(JobCardRow? card, int? selectedEmployeeId)
    {
        JobCardEmployeeChoices.Clear();
        SelectedJobCardEmployeeChoice = null;

        if (card is null)
        {
            QualificationStatusKind = "Neutral";
            QualificationStatusText = "Keine Arbeitskarte ausgewählt.";
            return;
        }

        using var db = new AppDbContext();
        var employees = db.Employees.AsNoTracking()
            .Where(x => x.IsActive || x.Id == selectedEmployeeId)
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToList();

        Dictionary<int, int> skillLevels = new();
        if (card.RequiredQualificationId.HasValue)
        {
            skillLevels = db.EmployeeQualifications.AsNoTracking()
                .Where(x => x.QualificationId == card.RequiredQualificationId.Value)
                .ToDictionary(x => x.EmployeeId, x => x.Level);
        }

        var day = card.PlannedDate.Date;
        var absences = db.Absences.AsNoTracking()
            .Where(x => x.StartDate <= day && x.EndDate >= day)
            .Select(x => x.EmployeeId)
            .ToHashSet();
        var plannedHere = db.PlanningAssignments.AsNoTracking()
            .Where(x => x.Date == day && x.WorkstationId == card.WorkstationId)
            .Select(x => x.EmployeeId)
            .ToHashSet();
        var activeCardCounts = db.JobCards.AsNoTracking()
            .Where(x => x.Status == "In Produktion" && x.EmployeeId.HasValue)
            .GroupBy(x => x.EmployeeId!.Value)
            .Select(g => new { EmployeeId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.EmployeeId, x => x.Count);

        var requirementMissing = card.HasSkillRequirement && !card.RequiredQualificationId.HasValue;
        var choices = new List<JobCardEmployeeChoice>();
        foreach (var employee in employees)
        {
            var level = card.RequiredQualificationId.HasValue && skillLevels.TryGetValue(employee.Id, out var foundLevel)
                ? foundLevel
                : 0;
            var qualified = !card.HasSkillRequirement ||
                            (!requirementMissing && level >= card.RequiredQualificationLevel);
            var status = !card.HasSkillRequirement
                ? "✅ keine Skillpflicht"
                : requirementMissing
                    ? $"❌ Pflichtqualifikation „{card.RequiredQualificationName}“ fehlt in den Stammdaten"
                    : qualified
                        ? $"✅ {card.RequiredQualificationName} L{level}"
                        : level > 0
                            ? $"⚠ {card.RequiredQualificationName} L{level} < L{card.RequiredQualificationLevel}"
                            : $"❌ kein {card.RequiredQualificationName}-Skill";

            choices.Add(new JobCardEmployeeChoice
            {
                EmployeeId = employee.Id,
                DisplayName = $"{employee.LastName}, {employee.FirstName}",
                PersonnelNumber = employee.PersonnelNumber,
                QualificationLevel = level,
                IsQualified = qualified,
                IsAbsent = absences.Contains(employee.Id),
                IsPlannedAtWorkstation = plannedHere.Contains(employee.Id),
                ActiveJobCards = activeCardCounts.TryGetValue(employee.Id, out var activeCount) ? activeCount : 0,
                StatusText = status
            });
        }

        foreach (var choice in choices
                     .OrderByDescending(x => x.IsQualified)
                     .ThenBy(x => x.IsAbsent)
                     .ThenByDescending(x => x.IsPlannedAtWorkstation)
                     .ThenBy(x => x.ActiveJobCards)
                     .ThenByDescending(x => x.QualificationLevel)
                     .ThenBy(x => x.DisplayName))
            JobCardEmployeeChoices.Add(choice);

        SelectedJobCardEmployeeChoice = selectedEmployeeId.HasValue
            ? JobCardEmployeeChoices.FirstOrDefault(x => x.EmployeeId == selectedEmployeeId.Value)
            : null;
        UpdateQualificationStatus(card, SelectedJobCardEmployeeChoice);
    }

    private void UpdateQualificationStatus(JobCardRow? card, JobCardEmployeeChoice? choice)
    {
        if (card is null)
        {
            QualificationStatusKind = "Neutral";
            QualificationStatusText = "Keine Arbeitskarte ausgewählt.";
            return;
        }

        if (!card.HasSkillRequirement)
        {
            QualificationStatusKind = "Success";
            QualificationStatusText = "✅ Für diesen Arbeitsgang ist keine Pflichtqualifikation hinterlegt.";
            return;
        }

        if (!card.RequiredQualificationId.HasValue)
        {
            QualificationStatusKind = "Danger";
            QualificationStatusText = $"❌ Pflichtqualifikation „{card.RequiredQualificationName}“ ist nicht mehr in den Stammdaten vorhanden.";
            return;
        }

        if (choice is null)
        {
            QualificationStatusKind = "Warning";
            QualificationStatusText = $"⚠ Benötigt: {card.RequiredQualificationName} Level {card.RequiredQualificationLevel}. Bitte Mitarbeiter auswählen.";
            return;
        }

        if (choice.IsQualified && choice.IsAbsent)
        {
            QualificationStatusKind = "Warning";
            QualificationStatusText = $"⚠ Qualifikation erfüllt, aber {choice.DisplayName} ist am {card.PlannedDate:dd.MM.yyyy} abwesend.";
        }
        else if (choice.IsQualified)
        {
            QualificationStatusKind = "Success";
            QualificationStatusText = $"✅ Qualifiziert: {card.RequiredQualificationName} Level {choice.QualificationLevel} · benötigt Level {card.RequiredQualificationLevel}.";
        }
        else if (choice.QualificationLevel > 0)
        {
            QualificationStatusKind = "Warning";
            QualificationStatusText = $"⚠ Skill zu niedrig: {card.RequiredQualificationName} Level {choice.QualificationLevel} · benötigt Level {card.RequiredQualificationLevel}.";
        }
        else
        {
            QualificationStatusKind = "Danger";
            QualificationStatusText = $"❌ Nicht qualifiziert: {choice.DisplayName} besitzt die Qualifikation „{card.RequiredQualificationName}“ nicht.";
        }
    }

    private bool TryResolveQualifiedEmployee(AppDbContext db, JobCard card, out int? employeeId, out string message)
    {
        employeeId = SelectedJobCardEmployeeChoice?.EmployeeId ?? card.EmployeeId;
        message = string.Empty;

        if (employeeId.HasValue)
        {
            var resolvedEmployeeId = employeeId.Value;
            var employee = db.Employees.AsNoTracking().FirstOrDefault(x => x.Id == resolvedEmployeeId && x.IsActive);
            if (employee is null)
            {
                message = "Arbeitskarte gesperrt: Der ausgewählte Mitarbeiter ist nicht aktiv.";
                return false;
            }

            var plannedDate = db.ProductionOrders.AsNoTracking()
                .Where(x => x.Id == card.ProductionOrderId)
                .Select(x => x.PlannedDate)
                .First();
            var absent = db.Absences.AsNoTracking().Any(x =>
                x.EmployeeId == resolvedEmployeeId &&
                x.StartDate <= plannedDate &&
                x.EndDate >= plannedDate);
            if (absent)
            {
                message = $"Arbeitskarte gesperrt: {employee.FirstName} {employee.LastName} ist am {plannedDate:dd.MM.yyyy} abwesend.";
                return false;
            }
        }

        var hasRequirement = card.RequiredQualificationLevel > 0 &&
                             (!string.IsNullOrWhiteSpace(card.RequiredQualificationNameSnapshot) || card.RequiredQualificationId.HasValue);
        if (!hasRequirement)
            return true;

        var requirementName = string.IsNullOrWhiteSpace(card.RequiredQualificationNameSnapshot)
            ? "Pflichtqualifikation"
            : card.RequiredQualificationNameSnapshot;

        if (!card.RequiredQualificationId.HasValue)
        {
            message = $"Arbeitskarte gesperrt: Die Pflichtqualifikation „{requirementName}“ ist nicht mehr in den Stammdaten vorhanden.";
            return false;
        }

        if (!employeeId.HasValue)
        {
            message = $"Arbeitskarte gesperrt: {requirementName} Level {card.RequiredQualificationLevel} ist erforderlich. Bitte einen Mitarbeiter auswählen.";
            return false;
        }

        var id = employeeId.Value;
        var selectedEmployee = db.Employees.AsNoTracking().First(x => x.Id == id);
        var level = db.EmployeeQualifications.AsNoTracking()
            .Where(x => x.EmployeeId == id && x.QualificationId == card.RequiredQualificationId.Value)
            .Select(x => (int?)x.Level)
            .FirstOrDefault() ?? 0;

        if (level < card.RequiredQualificationLevel)
        {
            message = level == 0
                ? $"Arbeitskarte gesperrt: {selectedEmployee.FirstName} {selectedEmployee.LastName} besitzt die Qualifikation „{requirementName}“ nicht."
                : $"Arbeitskarte gesperrt: {selectedEmployee.FirstName} {selectedEmployee.LastName} hat {requirementName} Level {level}; benötigt wird Level {card.RequiredQualificationLevel}.";
            return false;
        }

        return true;
    }

    private bool EnsurePlanner()
    {
        if (SessionService.IsPlannerOrAdmin) return true;
        StatusMessage = "Nur Planer oder Administratoren dürfen Fertigungsdaten ändern.";
        return false;
    }

    private static bool IsAllowed(WorkstationShiftRule rule, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => rule.Monday,
        DayOfWeek.Tuesday => rule.Tuesday,
        DayOfWeek.Wednesday => rule.Wednesday,
        DayOfWeek.Thursday => rule.Thursday,
        DayOfWeek.Friday => rule.Friday,
        DayOfWeek.Saturday => rule.Saturday,
        DayOfWeek.Sunday => rule.Sunday,
        _ => false
    };

    private static double NetShiftMinutes(Shift shift)
    {
        var start = DateTime.Today + shift.StartTime;
        var end = DateTime.Today + shift.EndTime;
        if (end <= start) end = end.AddDays(1);
        return Math.Max(0, (end - start).TotalMinutes - shift.BreakMinutes);
    }
}

public sealed class RoutingStepRow
{
    public int Id { get; set; }
    public int SequenceNumber { get; set; }
    public string OperationCode { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public string WorkstationName { get; set; } = string.Empty;
    public double PlannedMinutes { get; set; }
    public int RequiredStaff { get; set; }
}

public sealed class ProductionOrderOption
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public DateTime PlannedDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DisplayName => $"{OrderNumber} · {Product} · {PlannedDate:dd.MM.yyyy}";
}

public sealed class JobCardRow
{
    public int Id { get; set; }
    public int ProductionOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public string OperationCode { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public int WorkstationId { get; set; }
    public string WorkstationName { get; set; } = string.Empty;
    public DateTime PlannedDate { get; set; }
    public int? EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public int? RequiredQualificationId { get; set; }
    public string RequiredQualificationName { get; set; } = string.Empty;
    public int RequiredQualificationLevel { get; set; }
    public bool HasSkillRequirement => RequiredQualificationLevel > 0 && (!string.IsNullOrWhiteSpace(RequiredQualificationName) || RequiredQualificationId.HasValue);
    public string RequirementText => HasSkillRequirement
        ? $"{RequiredQualificationName} L{RequiredQualificationLevel}"
        : "Keine";
    public double PlannedMinutes { get; set; }
    public double RunMinutes { get; set; }
    public int RequiredStaff { get; set; }
    public string Status { get; set; } = string.Empty;
    public double GoodQuantity { get; set; }
    public double ScrapQuantity { get; set; }
    public string? Comment { get; set; }
    public string TimeText => $"{RunMinutes:N0} / {PlannedMinutes:N0} min";
}

public sealed class JobCardEmployeeChoice
{
    public int EmployeeId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string PersonnelNumber { get; set; } = string.Empty;
    public int QualificationLevel { get; set; }
    public bool IsQualified { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsPlannedAtWorkstation { get; set; }
    public int ActiveJobCards { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string AvailabilityText => IsAbsent ? "abwesend" : IsPlannedAtWorkstation ? "am Arbeitsplatz geplant" : ActiveJobCards > 0 ? $"{ActiveJobCards} aktive Karte(n)" : "verfügbar";
    public string DisplayText => $"{DisplayName} · {StatusText} · {AvailabilityText}";
}

public sealed class OrderCockpitRow
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public DateTime PlannedDate { get; set; }
    public string WorkstationName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public int CompletedSteps { get; set; }
    public int TotalSteps { get; set; }
    public int IssueCount { get; set; }
    public string ReadinessKind { get; set; } = "Neutral";
    public string ReadinessText { get; set; } = string.Empty;
    public string CurrentOperation { get; set; } = string.Empty;
    public string WorkstationCheck { get; set; } = string.Empty;
    public string CapacityCheck { get; set; } = string.Empty;
    public string StaffCheck { get; set; } = string.Empty;
    public string SkillCheck { get; set; } = string.Empty;
    public string ProgressText => TotalSteps == 0 ? "keine Arbeitskarten" : $"{CompletedSteps}/{TotalSteps} · {ProgressPercent} %";
    public string IssueText => IssueCount == 0 ? "kein Handlungsbedarf" : $"{IssueCount} Punkt(e) offen";
}

public sealed class CapacityRow
{
    public int WorkstationId { get; set; }
    public string WorkstationName { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public double AvailableMinutes { get; set; }
    public double PlannedMinutes { get; set; }
    public int OpenJobCards { get; set; }
    public double UtilizationPercent { get; set; }
    public string Status { get; set; } = string.Empty;
    public string AvailableText => $"{AvailableMinutes / 60d:N1} h";
    public string PlannedText => $"{PlannedMinutes / 60d:N1} h";
    public string UtilizationText => UtilizationPercent >= 999 ? ">999 %" : $"{UtilizationPercent:N0} %";
    public double BarValue => Math.Min(120, UtilizationPercent);
    public string DeltaText => UtilizationPercent > 100
        ? $"+{Math.Max(0, PlannedMinutes - AvailableMinutes) / 60d:N1} h Überlastung"
        : $"{Math.Max(0, AvailableMinutes - PlannedMinutes) / 60d:N1} h frei";
}
