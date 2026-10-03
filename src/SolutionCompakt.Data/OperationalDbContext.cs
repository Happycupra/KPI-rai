using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Models;

namespace SolutionCompakt.Data;

public abstract class OperationalDbContext : DbContext
{
    protected OperationalDbContext() { }
    protected OperationalDbContext(DbContextOptions options) : base(options) { }

    public DbSet<ArticleMaster> ArticleMasters => Set<ArticleMaster>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();
    public DbSet<EmployeeQualification> EmployeeQualifications => Set<EmployeeQualification>();
    public DbSet<Workstation> Workstations => Set<Workstation>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<WorkstationShiftRule> WorkstationShiftRules => Set<WorkstationShiftRule>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<OperatingCalendarDay> OperatingCalendarDays => Set<OperatingCalendarDay>();
    public DbSet<WorkTimeEntry> WorkTimeEntries => Set<WorkTimeEntry>();
    public DbSet<PlanningAssignment> PlanningAssignments => Set<PlanningAssignment>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionRunSlot> ProductionRunSlots => Set<ProductionRunSlot>();
    public DbSet<ProductionActual> ProductionActuals => Set<ProductionActual>();
    public DbSet<DowntimeEntry> DowntimeEntries => Set<DowntimeEntry>();
    public DbSet<OperationDefinition> OperationDefinitions => Set<OperationDefinition>();
    public DbSet<ManufacturingRouting> ManufacturingRoutings => Set<ManufacturingRouting>();
    public DbSet<RoutingStep> RoutingSteps => Set<RoutingStep>();
    public DbSet<JobCard> JobCards => Set<JobCard>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RecycleBinItem> RecycleBinItems => Set<RecycleBinItem>();
    public DbSet<ShiftHandover> ShiftHandovers => Set<ShiftHandover>();
    public DbSet<UserMessage> UserMessages => Set<UserMessage>();

}
