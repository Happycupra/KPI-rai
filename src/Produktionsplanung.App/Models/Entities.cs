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
