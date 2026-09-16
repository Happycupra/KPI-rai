using System.IO;
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
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionActual> ProductionActuals => Set<ProductionActual>();
    public DbSet<DowntimeEntry> DowntimeEntries => Set<DowntimeEntry>();

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

        modelBuilder.Entity<ProductionOrder>()
            .HasIndex(x => x.OrderNumber)
            .IsUnique();

        modelBuilder.Entity<ProductionOrder>()
            .HasIndex(x => new { x.PlannedDate, x.WorkstationId, x.ShiftId });

        modelBuilder.Entity<ProductionOrder>()
            .HasOne(x => x.Workstation)
            .WithMany()
            .HasForeignKey(x => x.WorkstationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProductionOrder>()
            .HasOne(x => x.Shift)
            .WithMany()
            .HasForeignKey(x => x.ShiftId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ProductionActual>()
            .HasIndex(x => new { x.ProductionOrderId, x.Date });

        modelBuilder.Entity<ProductionActual>()
            .HasOne(x => x.ProductionOrder)
            .WithMany(x => x.Actuals)
            .HasForeignKey(x => x.ProductionOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DowntimeEntry>()
            .HasIndex(x => x.ProductionActualId);

        modelBuilder.Entity<DowntimeEntry>()
            .HasOne(x => x.ProductionActual)
            .WithMany(x => x.Downtimes)
            .HasForeignKey(x => x.ProductionActualId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
