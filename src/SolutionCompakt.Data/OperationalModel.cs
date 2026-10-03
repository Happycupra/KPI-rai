using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Produktionsplanung.App.Models;

namespace SolutionCompakt.Data;

public static class OperationalModel
{
    public static void Configure(ModelBuilder m, bool central, string? schema = null)
    {
        if (central)
        {
            if (string.IsNullOrWhiteSpace(schema)) throw new ArgumentException("Central models require a company schema.", nameof(schema));
            m.HasDefaultSchema(schema);
        }
        m.Entity<ArticleMaster>().HasOne(x => x.DefaultRouting).WithMany().HasForeignKey(x => x.DefaultRoutingId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionOrder>().HasOne(x => x.ArticleMaster).WithMany().HasForeignKey(x => x.ArticleMasterId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionOrder>().HasOne(x => x.ManufacturingRouting).WithMany().HasForeignKey(x => x.ManufacturingRoutingId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionOrder>().HasIndex(x => new { x.ArticleMasterId, x.BatchNumber }).IsUnique().HasFilter("\"ArticleMasterId\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        m.Entity<ProductionOrder>().HasIndex(x => new { x.Status, x.CompletedAtUtc });
        m.Entity<ProductionOrder>().HasQueryFilter(x => !x.IsDeleted);
        m.Entity<Employee>().HasQueryFilter(x => !x.IsDeleted);
        m.Entity<ProductionActual>().HasQueryFilter(x => !x.IsDeleted);
        m.Entity<DowntimeEntry>().HasQueryFilter(x => !x.IsDeleted);

        m.Entity<ArticleMaster>().HasIndex(x => x.ArticleNumber).IsUnique();
        m.Entity<ArticleMaster>().HasIndex(x => x.Name);
        m.Entity<Employee>().HasIndex(x => x.PersonnelNumber).IsUnique();
        m.Entity<EmployeeQualification>().HasKey(x => new { x.EmployeeId, x.QualificationId });
        m.Entity<EmployeeQualification>().HasOne(x => x.Employee).WithMany(x => x.Qualifications).HasForeignKey(x => x.EmployeeId);
        m.Entity<EmployeeQualification>().HasOne(x => x.Qualification).WithMany(x => x.Employees).HasForeignKey(x => x.QualificationId);
        m.Entity<Workstation>().HasOne(x => x.RequiredQualification).WithMany().HasForeignKey(x => x.RequiredQualificationId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<WorkstationShiftRule>().HasIndex(x => new { x.WorkstationId, x.ShiftId }).IsUnique();
        m.Entity<WorkstationShiftRule>().HasOne(x => x.Workstation).WithMany(x => x.ShiftRules).HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<WorkstationShiftRule>().HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<OperatingCalendarDay>().HasIndex(x => x.Date).IsUnique();
        m.Entity<WorkTimeEntry>().HasIndex(x => new { x.EmployeeId, x.Date });
        m.Entity<WorkTimeEntry>().HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionOrder>().HasIndex(x => x.OrderNumber).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        m.Entity<ProductionOrder>().HasIndex(x => new { x.PlannedDate, x.WorkstationId, x.ShiftId });
        m.Entity<ProductionOrder>().HasOne(x => x.Workstation).WithMany().HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionOrder>().HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<ProductionRunSlot>().HasIndex(x => new { x.ProductionOrderId, x.SequenceNumber }).IsUnique();
        m.Entity<ProductionRunSlot>().HasIndex(x => new { x.Date, x.ShiftId });
        m.Entity<ProductionRunSlot>().HasOne(x => x.ProductionOrder).WithMany(x => x.RunSlots).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<ProductionRunSlot>().HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ProductionActual>().HasIndex(x => new { x.ProductionOrderId, x.Date });
        m.Entity<ProductionActual>().HasIndex(x => x.ProductionRunSlotId);
        m.Entity<ProductionActual>().HasOne(x => x.ProductionOrder).WithMany(x => x.Actuals).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<ProductionActual>().HasOne(x => x.ProductionRunSlot).WithMany(x => x.Actuals).HasForeignKey(x => x.ProductionRunSlotId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<DowntimeEntry>().HasIndex(x => x.ProductionActualId);
        m.Entity<DowntimeEntry>().HasOne(x => x.ProductionActual).WithMany(x => x.Downtimes).HasForeignKey(x => x.ProductionActualId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<OperationDefinition>().HasIndex(x => x.Code).IsUnique();
        m.Entity<OperationDefinition>().HasOne(x => x.DefaultWorkstation).WithMany().HasForeignKey(x => x.DefaultWorkstationId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<OperationDefinition>().HasOne(x => x.RequiredQualification).WithMany().HasForeignKey(x => x.RequiredQualificationId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<ManufacturingRouting>().HasIndex(x => x.Product);
        m.Entity<RoutingStep>().HasIndex(x => new { x.ManufacturingRoutingId, x.SequenceNumber }).IsUnique();
        m.Entity<RoutingStep>().HasOne(x => x.ManufacturingRouting).WithMany(x => x.Steps).HasForeignKey(x => x.ManufacturingRoutingId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<RoutingStep>().HasOne(x => x.OperationDefinition).WithMany(x => x.RoutingSteps).HasForeignKey(x => x.OperationDefinitionId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<RoutingStep>().HasOne(x => x.Workstation).WithMany().HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<JobCard>().HasIndex(x => new { x.ProductionOrderId, x.SequenceNumber }).IsUnique();
        m.Entity<JobCard>().HasIndex(x => new { x.WorkstationId, x.Status });
        m.Entity<JobCard>().HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<JobCard>().HasOne(x => x.RoutingStep).WithMany().HasForeignKey(x => x.RoutingStepId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<JobCard>().HasOne(x => x.Workstation).WithMany().HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<JobCard>().HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<JobCard>().HasOne(x => x.RequiredQualification).WithMany().HasForeignKey(x => x.RequiredQualificationId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<UserAccount>().HasIndex(x => x.Username).IsUnique();
        m.Entity<AuditLog>().HasIndex(x => x.TimestampUtc);
        m.Entity<AuditLog>().HasIndex(x => x.Username);
        m.Entity<RecycleBinItem>().HasIndex(x => x.DeletedAtUtc);
        m.Entity<RecycleBinItem>().HasIndex(x => new { x.EntityType, x.EntityId });
        m.Entity<ShiftHandover>().HasIndex(x => new { x.HandoverDate, x.Status });
        m.Entity<ShiftHandover>().HasOne(x => x.FromShift).WithMany().HasForeignKey(x => x.FromShiftId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<ShiftHandover>().HasOne(x => x.ToShift).WithMany().HasForeignKey(x => x.ToShiftId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<ShiftHandover>().HasOne(x => x.Workstation).WithMany().HasForeignKey(x => x.WorkstationId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<ShiftHandover>().HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<UserMessage>().HasIndex(x => new { x.RecipientUserId, x.AcknowledgedAtUtc, x.CreatedAtUtc });
        m.Entity<UserMessage>().HasIndex(x => new { x.SenderUserId, x.CreatedAtUtc });

        if (central)
        {
            foreach (var entityType in m.Model.GetEntityTypes().Where(x => !x.IsKeyless))
            {
                m.Entity(entityType.ClrType).Property<Guid>("ConcurrencyToken").IsConcurrencyToken();
                // Match the established desktop schema without relying on a process-wide Npgsql switch.
                foreach (var property in entityType.GetProperties().Where(x => x.ClrType == typeof(DateTime) || x.ClrType == typeof(DateTime?)))
                {
                    property.SetColumnType("timestamp without time zone");
                    var utc = property.Name.EndsWith("Utc", StringComparison.Ordinal);
                    property.SetValueConverter(new ValueConverter<DateTime, DateTime>(
                        value => DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
                        value => DateTime.SpecifyKind(value, utc ? DateTimeKind.Utc : DateTimeKind.Unspecified)));
                }
            }
        }
    }
}
