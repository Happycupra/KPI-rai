namespace Produktionsplanung.App.Models;

public class Employee
{
    public int Id { get; set; }
    public string PersonnelNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int WorkloadPercent { get; set; } = 100;
    public double WeeklyTargetHours { get; set; } = 40;
    public bool IsActive { get; set; } = true;
    public ICollection<EmployeeQualification> Qualifications { get; set; } = new List<EmployeeQualification>();
}

public class Qualification
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<EmployeeQualification> Employees { get; set; } = new List<EmployeeQualification>();
}

public class EmployeeQualification
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int QualificationId { get; set; }
    public Qualification Qualification { get; set; } = null!;
    public int Level { get; set; }
}

public class Workstation
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public int MinimumStaff { get; set; }
    public int OptimalStaff { get; set; }
    public int MaximumStaff { get; set; }
    public int? RequiredQualificationId { get; set; }
    public Qualification? RequiredQualification { get; set; }
    public int RequiredQualificationLevel { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Shift
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int BreakMinutes { get; set; }
}

public class Absence
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string Type { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Comment { get; set; }
}

public class OperatingCalendarDay
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsWorkingDay { get; set; }
    public double TargetHoursFactor { get; set; } = 1;
    public string? Comment { get; set; }
}

public class WorkTimeEntry
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateTime Date { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public string? Comment { get; set; }
}

public class PlanningAssignment
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int WorkstationId { get; set; }
    public Workstation Workstation { get; set; } = null!;
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public string? Comment { get; set; }
}

public class ProductionOrder
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Quantity { get; set; }
    public string Unit { get; set; } = "Stück";
    public string Priority { get; set; } = "Normal";
    public DateTime PlannedDate { get; set; }
    public TimeSpan? PlannedStart { get; set; }
    public TimeSpan? PlannedEnd { get; set; }
    public int WorkstationId { get; set; }
    public Workstation Workstation { get; set; } = null!;
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public int RequiredStaff { get; set; }
    public string Status { get; set; } = "Geplant";
    public string? Comment { get; set; }
    public ICollection<ProductionActual> Actuals { get; set; } = new List<ProductionActual>();
}

public class ProductionActual
{
    public int Id { get; set; }
    public int ProductionOrderId { get; set; }
    public ProductionOrder ProductionOrder { get; set; } = null!;
    public DateTime Date { get; set; }
    public double TotalQuantity { get; set; }
    public double GoodQuantity { get; set; }
    public double ScrapQuantity { get; set; }
    public double PlannedProductionMinutes { get; set; }
    public double RunMinutes { get; set; }
    public double IdealRatePerHour { get; set; }
    public string? Comment { get; set; }
    public ICollection<DowntimeEntry> Downtimes { get; set; } = new List<DowntimeEntry>();
}

public class DowntimeEntry
{
    public int Id { get; set; }
    public int ProductionActualId { get; set; }
    public ProductionActual ProductionActual { get; set; } = null!;
    public string Reason { get; set; } = string.Empty;
    public double Minutes { get; set; }
    public string? Comment { get; set; }
}

public class UserAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string Role { get; set; } = UserRoles.Observer;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }
}

public static class UserRoles
{
    public const string Administrator = "Administrator";
    public const string Planner = "Planer";
    public const string Observer = "Beobachter";
    public static readonly string[] All = { Administrator, Planner, Observer };
}

public class AuditLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Details { get; set; }
}
