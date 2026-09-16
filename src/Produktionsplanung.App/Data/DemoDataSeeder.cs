using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Data;

public static class DemoDataSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (!db.Employees.Any())
            SeedBaseData(db);

        SeedProductionOrders(db);
    }

    private static void SeedBaseData(AppDbContext db)
    {
        var line1 = new Qualification { Name = "Linie 1" };
        var line2 = new Qualification { Name = "Linie 2" };
        var packaging = new Qualification { Name = "Verpackung" };
        var qc = new Qualification { Name = "Qualitätskontrolle" };
        var operatorQualification = new Qualification { Name = "Anlagenführer" };

        var peter = new Employee { PersonnelNumber = "1001", FirstName = "Peter", LastName = "Müller", Role = "Anlagenführer", Department = "Produktion" };
        var anna = new Employee { PersonnelNumber = "1002", FirstName = "Anna", LastName = "Keller", Role = "Produktionsmitarbeiterin", Department = "Produktion" };
        var marco = new Employee { PersonnelNumber = "1003", FirstName = "Marco", LastName = "Meier", Role = "Produktionsmitarbeiter", Department = "Produktion" };
        var sarah = new Employee { PersonnelNumber = "1004", FirstName = "Sarah", LastName = "Frei", Role = "Teamleiterin", Department = "Produktion" };

        db.AddRange(line1, line2, packaging, qc, operatorQualification);
        db.AddRange(peter, anna, marco, sarah);
        db.SaveChanges();

        db.EmployeeQualifications.AddRange(
            new EmployeeQualification { EmployeeId = peter.Id, QualificationId = line1.Id, Level = 3 },
            new EmployeeQualification { EmployeeId = peter.Id, QualificationId = line2.Id, Level = 2 },
            new EmployeeQualification { EmployeeId = peter.Id, QualificationId = operatorQualification.Id, Level = 3 },
            new EmployeeQualification { EmployeeId = anna.Id, QualificationId = line1.Id, Level = 2 },
            new EmployeeQualification { EmployeeId = anna.Id, QualificationId = packaging.Id, Level = 3 },
            new EmployeeQualification { EmployeeId = marco.Id, QualificationId = line2.Id, Level = 3 },
            new EmployeeQualification { EmployeeId = marco.Id, QualificationId = packaging.Id, Level = 2 },
            new EmployeeQualification { EmployeeId = sarah.Id, QualificationId = line1.Id, Level = 3 },
            new EmployeeQualification { EmployeeId = sarah.Id, QualificationId = line2.Id, Level = 3 },
            new EmployeeQualification { EmployeeId = sarah.Id, QualificationId = qc.Id, Level = 3 }
        );

        db.Workstations.AddRange(
            new Workstation { Name = "Linie 1", Area = "Produktion", MinimumStaff = 3, OptimalStaff = 4, MaximumStaff = 5 },
            new Workstation { Name = "Linie 2", Area = "Produktion", MinimumStaff = 2, OptimalStaff = 3, MaximumStaff = 4 },
            new Workstation { Name = "Verpackung", Area = "Verpackung", MinimumStaff = 3, OptimalStaff = 5, MaximumStaff = 6 },
            new Workstation { Name = "Qualitätskontrolle", Area = "Qualität", MinimumStaff = 1, OptimalStaff = 1, MaximumStaff = 2 }
        );

        db.Shifts.AddRange(
            new Shift { Name = "Frühschicht", StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(14, 0, 0), BreakMinutes = 30 },
            new Shift { Name = "Spätschicht", StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(22, 0, 0), BreakMinutes = 30 },
            new Shift { Name = "Nachtschicht", StartTime = new TimeSpan(22, 0, 0), EndTime = new TimeSpan(6, 0, 0), BreakMinutes = 30 }
        );

        db.SaveChanges();
    }

    private static void SeedProductionOrders(AppDbContext db)
    {
        if (db.ProductionOrders.Any()) return;

        var line1 = db.Workstations.FirstOrDefault(x => x.Name == "Linie 1");
        var line2 = db.Workstations.FirstOrDefault(x => x.Name == "Linie 2");
        var early = db.Shifts.FirstOrDefault(x => x.Name == "Frühschicht");
        var late = db.Shifts.FirstOrDefault(x => x.Name == "Spätschicht");
        if (line1 is null || line2 is null || early is null || late is null) return;

        db.ProductionOrders.AddRange(
            new ProductionOrder
            {
                OrderNumber = "PO-1042",
                Product = "Produkt A",
                Quantity = 5000,
                Unit = "Stück",
                Priority = "Hoch",
                PlannedDate = DateTime.Today,
                PlannedStart = early.StartTime,
                PlannedEnd = early.EndTime,
                WorkstationId = line1.Id,
                ShiftId = early.Id,
                RequiredStaff = 4,
                Status = "Bereit",
                Comment = "Demo-Auftrag für die Frühschicht"
            },
            new ProductionOrder
            {
                OrderNumber = "PO-1043",
                Product = "Produkt B",
                Quantity = 3200,
                Unit = "Stück",
                Priority = "Normal",
                PlannedDate = DateTime.Today,
                PlannedStart = late.StartTime,
                PlannedEnd = late.EndTime,
                WorkstationId = line2.Id,
                ShiftId = late.Id,
                RequiredStaff = 3,
                Status = "Geplant",
                Comment = "Demo-Auftrag für die Spätschicht"
            });

        db.SaveChanges();
    }
}
