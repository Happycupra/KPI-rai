using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Data;

public class AppDbContext : DbContext
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();
    public DbSet<EmployeeQualification> EmployeeQualifications => Set<EmployeeQualification>();
    public DbSet<Workstation> Workstations => Set<Workstation>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<PlanningAssignment> PlanningAssignments => Set<PlanningAssignment>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Produktionsplanung",
            "Data");

        Directory.CreateDirectory(baseDir);
        var dbPath = Path.Combine(baseDir, "produktionsplanung.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>()
            .HasIndex(x => x.PersonnelNumber)
            .IsUnique();

        modelBuilder.Entity<EmployeeQualification>()
            .HasKey(x => new { x.EmployeeId, x.QualificationId });

        modelBuilder.Entity<EmployeeQualification>()
            .HasOne(x => x.Employee)
            .WithMany(x => x.Qualifications)
            .HasForeignKey(x => x.EmployeeId);

        modelBuilder.Entity<EmployeeQualification>()
            .HasOne(x => x.Qualification)
            .WithMany(x => x.Employees)
            .HasForeignKey(x => x.QualificationId);
    }
}
