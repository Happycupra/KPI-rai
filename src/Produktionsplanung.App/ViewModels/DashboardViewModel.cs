using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty]
    private int plannedEmployees;

    [ObservableProperty]
    private int activeEmployees;

    [ObservableProperty]
    private int absentEmployees;

    [ObservableProperty]
    private int openProblems;

    public List<Workstation> Workstations { get; private set; } = new();
    public List<Employee> Employees { get; private set; } = new();
    public string TodayText => DateTime.Now.ToString("dddd, dd.MM.yyyy");

    public DashboardViewModel()
    {
        Load();
    }

    private void Load()
    {
        using var db = new AppDbContext();
        Employees = db.Employees.AsNoTracking().OrderBy(x => x.LastName).ToList();
        Workstations = db.Workstations.AsNoTracking().OrderBy(x => x.Name).ToList();

        ActiveEmployees = Employees.Count(x => x.IsActive);
        PlannedEmployees = db.PlanningAssignments.Count(x => x.Date.Date == DateTime.Today);
        AbsentEmployees = db.Absences.Count(x => x.StartDate.Date <= DateTime.Today && x.EndDate.Date >= DateTime.Today);
        OpenProblems = 0;
    }
}
