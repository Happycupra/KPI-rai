using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;
using Produktionsplanung.App.Views;
using System.IO;
using System.Windows.Controls;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Produktionsplanung.App.App();
        app.InitializeComponent();
        var tests = new (string Name, Action Run)[]
        {
            ("Demo seeding preserves cleared data and disabled shift models", DemoSeederPreservesChanges),
            ("Day planning only offers shifts allowed for workstation and date", DayPlanningAllowedShifts),
            ("Order status edits preserve existing production run slots", OrderStatusKeepsRunSlots),
            ("Calendar weekend filter applies to week and month", CalendarWeekendFilter),
            ("SQLite TimeSpan queries and null shifts", QuerySmoke),
            ("Production actual choices sort by date and shift time", ProductionActualOrdering),
            ("Workstation with orders cannot be deleted", WorkstationDeletion),
            ("Order history protected in UI and database", OrderDeletion),
            ("Existing downtime prevents incompatible runtime edits", DowntimeEdit),
            ("Downtime additions use saved production time", DowntimeAddition),
            ("Sunday night copy respects following Monday absence", WeekBoundary),
            ("OEE aggregation is invariant under unit conversion", OeeUnits),
            ("OEE keeps same-name workstations separate", WorkstationIdentity),
            ("Password entry is masked and cleared", PasswordInput),
            ("Failed restore preserves active session", FailedRestore),
            ("Successful restore invalidates old session", RestoreSession)
        };
        var failed = 0;
        foreach (var test in tests)
        {
            var path = Path.Combine(Path.GetTempPath(), "kpi-rai-test-" + Guid.NewGuid().ToString("N"));
            AppPaths.RootDirectoryOverride = path;
            SessionService.SignOut();
            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.EnsureCreated();
                    DatabaseSchemaUpdater.Apply(db);
                    DemoDataSeeder.Seed(db);
                }
                test.Run();
                Console.WriteLine("PASS " + test.Name);
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine("FAIL " + test.Name + "\n" + ex);
            }
            finally
            {
                SessionService.SignOut();
                SqliteConnection.ClearAllPools();
                Directory.Delete(path, recursive: true);
                AppPaths.RootDirectoryOverride = null;
            }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} regression tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Near(double actual, double expected) =>
        Check(Math.Abs(actual - expected) < 0.000001, $"Expected {expected}, got {actual}");

    private static void DemoSeederPreservesChanges()
    {
        int workstationId;
        using (var db = new AppDbContext())
        {
            var workstation = db.Workstations.OrderBy(x => x.Id).First();
            workstationId = workstation.Id;
            workstation.IsActive = false;
            db.WorkstationShiftRules.RemoveRange(
                db.WorkstationShiftRules.Where(x => x.WorkstationId == workstationId));
            db.ProductionOrders.RemoveRange(db.ProductionOrders);
            db.SaveChanges();

            DemoDataSeeder.Seed(db);

            Check(!db.WorkstationShiftRules.Any(x => x.WorkstationId == workstationId),
                "Seeder recreated deliberately removed workstation shift rules");
            Check(!db.ProductionOrders.Any(),
                "Seeder recreated demo production orders after user cleared them");
        }

        var vm = new WorkstationManagementViewModel();
        vm.SelectedWorkstation = vm.Workstations.Single(x => x.Id == workstationId);
        Check(vm.ShiftRules.All(x => !x.IsEnabled),
            "Workstation editor interpreted zero saved rules as all shifts enabled");
    }

    private static void DayPlanningAllowedShifts()
    {
        var date = new DateTime(2030, 1, 14); // Monday
        int workstationId;
        int allowedShiftId;
        using (var db = new AppDbContext())
        {
            var workstation = db.Workstations.OrderBy(x => x.Id).First();
            var shift = db.Shifts.Single(x => x.Name == "Frühschicht");
            workstationId = workstation.Id;
            allowedShiftId = shift.Id;

            db.WorkstationShiftRules.RemoveRange(
                db.WorkstationShiftRules.Where(x => x.WorkstationId == workstationId));
            db.WorkstationShiftRules.Add(new WorkstationShiftRule
            {
                WorkstationId = workstationId,
                ShiftId = allowedShiftId,
                Monday = true,
                Tuesday = false,
                Wednesday = false,
                Thursday = false,
                Friday = false,
                Saturday = false,
                Sunday = false
            });
            db.SaveChanges();
        }

        var vm = new DayPlanningViewModel { SelectedDate = date };
        vm.SelectedWorkstation = vm.Workstations.Single(x => x.Id == workstationId);
        Check(vm.Shifts.Count == 1 && vm.Shifts[0].Id == allowedShiftId,
            "Day planning exposes shifts that are not allowed for the workstation/date");
    }

    private static void OrderStatusKeepsRunSlots()
    {
        int orderId;
        List<(int Id, int Sequence, DateTime Date, int ShiftId)> before;
        using (var db = new AppDbContext())
        {
            var order = db.ProductionOrders.OrderBy(x => x.Id).First();
            orderId = order.Id;
            before = db.ProductionRunSlots.AsNoTracking()
                .Where(x => x.ProductionOrderId == orderId)
                .OrderBy(x => x.SequenceNumber)
                .AsEnumerable()
                .Select(x => (x.Id, x.SequenceNumber, x.Date, x.ShiftId))
                .ToList();
        }

        var vm = new ProductionOrderManagementViewModel();
        vm.SelectedOrder = vm.Orders.Single(x => x.Id == orderId);
        vm.Status = "Läuft";
        vm.Priority = "Dringend";
        vm.Comment = "Nur Statusänderung";
        vm.SaveCommand.Execute(null);

        using var check = new AppDbContext();
        var after = check.ProductionRunSlots.AsNoTracking()
            .Where(x => x.ProductionOrderId == orderId)
            .OrderBy(x => x.SequenceNumber)
            .AsEnumerable()
            .Select(x => (x.Id, x.SequenceNumber, x.Date, x.ShiftId))
            .ToList();
        Check(before.SequenceEqual(after), "Status-only order edit rebuilt production run slots");
        Check(check.ProductionOrders.Single(x => x.Id == orderId).Status == "Läuft",
            "Status-only order edit was not saved");
    }

    private static void CalendarWeekendFilter()
    {
        var vm = new PlanningCalendarViewModel { SelectedDate = new DateTime(2030, 1, 15) };
        vm.ShowWeekends = false;
        Check(vm.WeekDays.Count == 5, "Week view still shows weekend columns");
        Check(vm.MonthColumnCount == 5, "Month view did not switch to five columns");
        Check(vm.MonthDays.Count == 30, "Six-week month grid did not reduce to 30 weekday cells");
        Check(vm.MonthDays.All(x => x.Date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday),
            "Month view still contains weekend days while weekend filter is disabled");

        vm.ShowWeekends = true;
        Check(vm.WeekDays.Count == 7, "Week view did not restore weekends");
        Check(vm.MonthColumnCount == 7 && vm.MonthDays.Count == 42,
            "Month view did not restore the seven-column 42-day grid");
    }

    private static void QuerySmoke()
    {
        using (var db = new AppDbContext())
        {
            var order = db.ProductionOrders.First();
            order.ShiftId = null;
            db.PlanningAssignments.Add(new PlanningAssignment
            {
                EmployeeId = db.Employees.First().Id, WorkstationId = order.WorkstationId,
                Date = DateTime.Today, StartTime = TimeSpan.FromHours(22), EndTime = TimeSpan.FromHours(6)
            });
            db.SaveChanges();
        }
        _ = new DashboardViewModel();
        var day = new DayPlanningViewModel();
        day.RefreshProductionOrderCoverage();
        var week = new WeekPlanningViewModel();
        week.RefreshProductionOrderCoverage();
        _ = new ShiftManagementViewModel();
        _ = new ProductionOrderManagementViewModel();
        _ = new ProductionActualViewModel();
        var analytics = new AnalyticsViewModel();
        analytics.RefreshOeeAnalytics();
        var export = CsvExportService.ExportAll(AppPaths.ExportsDirectory, new AppSettings());
        Check(File.Exists(Path.Combine(export, "planung.csv")), "Planning export missing");
        // Construct the actual WPF views to check their bindings and resources can load.
        _ = new Produktionsplanung.App.MainWindow();
        _ = new AnalyticsView();
        _ = new DayPlanningView();
        _ = new WeekPlanningView();
        _ = new PlanningCalendarView();
        _ = new ProductionActualView();
        _ = new ProductionOrdersView();
        _ = new EmployeesView();
        _ = new WorkstationsView();
        _ = new ShiftsView();
        _ = new AbsencesView();
        _ = new SkillMatrixView();
        _ = new SettingsView();
        _ = new Produktionsplanung.App.LoginWindow();
        _ = new Produktionsplanung.App.ChangePasswordWindow();
    }

    private static void ProductionActualOrdering()
    {
        var date = new DateTime(2030, 1, 14);
        int earlyId, lateId, nextDayId;
        using (var db = new AppDbContext())
        {
            var order = db.ProductionOrders.First();
            var early = db.Shifts.Single(x => x.Name == "Frühschicht");
            var late = db.Shifts.Single(x => x.Name == "Spätschicht");
            // Insert late before early so insertion order cannot satisfy the assertion.
            var lateSlot = new ProductionRunSlot { ProductionOrderId = order.Id,
                SequenceNumber = 20, Date = date, ShiftId = late.Id };
            var earlySlot = new ProductionRunSlot { ProductionOrderId = order.Id,
                SequenceNumber = 21, Date = date, ShiftId = early.Id };
            var nextDaySlot = new ProductionRunSlot { ProductionOrderId = order.Id,
                SequenceNumber = 22, Date = date.AddDays(1), ShiftId = late.Id };
            db.ProductionRunSlots.AddRange(lateSlot, earlySlot, nextDaySlot);
            db.SaveChanges();
            earlyId = earlySlot.Id;
            lateId = lateSlot.Id;
            nextDayId = nextDaySlot.Id;
        }
        var vm = new ProductionActualViewModel();
        var ids = vm.Orders.Where(x => x.RunSlotId == earlyId || x.RunSlotId == lateId || x.RunSlotId == nextDayId)
            .Select(x => x.RunSlotId!.Value).ToArray();
        Check(ids.SequenceEqual(new[] { nextDayId, earlyId, lateId }), "Production shifts are not ordered by descending date and ascending start time");
        vm.RefreshCommand.Execute(null);
        var refreshed = vm.Orders.Where(x => ids.Contains(x.RunSlotId ?? -1))
            .Select(x => x.RunSlotId!.Value).ToArray();
        Check(refreshed.SequenceEqual(ids), "Refreshing changed production shift ordering");
    }

    private static void WorkstationDeletion()
    {
        using var db = new AppDbContext();
        var id = db.ProductionOrders.First().WorkstationId;
        var vm = new WorkstationManagementViewModel();
        vm.SelectedWorkstation = vm.Workstations.Single(x => x.Id == id);
        vm.DeleteCommand.Execute(null);
        Check(db.Workstations.Any(x => x.Id == id), "Referenced workstation deleted");
        Check(vm.StatusMessage.Contains("Produktionsaufträge"), "Missing deletion explanation");
    }

    private static int AddActual()
    {
        using var db = new AppDbContext();
        var slot = db.ProductionRunSlots.OrderBy(x => x.Id).First();
        var actual = new ProductionActual
        {
            ProductionOrderId = slot.ProductionOrderId, ProductionRunSlotId = slot.Id, Date = slot.Date,
            PlannedProductionMinutes = 450, RunMinutes = 400, IdealRatePerHour = 100,
            TotalQuantity = 100, GoodQuantity = 100
        };
        actual.Downtimes.Add(new DowntimeEntry { Reason = "Störung", Minutes = 50 });
        db.Add(actual);
        db.SaveChanges();
        return actual.Id;
    }

    private static void OrderDeletion()
    {
        var actualId = AddActual();
        using var db = new AppDbContext();
        var orderId = db.ProductionActuals.Single(x => x.Id == actualId).ProductionOrderId;
        var vm = new ProductionOrderManagementViewModel();
        vm.SelectedOrder = vm.Orders.Single(x => x.Id == orderId);
        vm.DeleteCommand.Execute(null);
        Check(vm.StatusMessage.Contains("Produktionshistorie"), "History warning missing");
        var blocked = false;
        try { db.Database.ExecuteSqlInterpolated($"DELETE FROM ProductionOrders WHERE Id = {orderId}"); }
        catch (SqliteException) { blocked = true; }
        Check(blocked, "Database allowed cascading history deletion");
        Check(db.ProductionActuals.Any(x => x.Id == actualId) && db.DowntimeEntries.Any(), "History lost");
    }

    private static void DowntimeEdit()
    {
        var id = AddActual();
        var vm = new ProductionActualViewModel();
        vm.SelectedActual = vm.Actuals.Single(x => x.Id == id);
        vm.RunMinutes = 450;
        vm.SaveCommand.Execute(null);
        using var db = new AppDbContext();
        Near(db.ProductionActuals.Single(x => x.Id == id).RunMinutes, 400);
        Check(vm.StatusMessage.Contains("Stillstände"), "Missing downtime validation");
        vm.RunMinutes = 390;
        vm.SaveCommand.Execute(null);
        using var fresh = new AppDbContext();
        Near(fresh.ProductionActuals.Single(x => x.Id == id).RunMinutes, 390);
    }

    private static void DowntimeAddition()
    {
        var id = AddActual();
        var vm = new ProductionActualViewModel();
        vm.SelectedActual = vm.Actuals.Single(x => x.Id == id);
        vm.RunMinutes = 300; // Unsaved: actual loss is still only 50 minutes, already allocated.
        vm.DowntimeMinutes = 20;
        vm.AddDowntimeCommand.Execute(null);
        using var db = new AppDbContext();
        Near(db.DowntimeEntries.Where(x => x.ProductionActualId == id).Sum(x => x.Minutes), 50);
    }

    private static void WeekBoundary()
    {
        var monday = new DateTime(2026, 9, 14);
        using (var db = new AppDbContext())
        {
            var employee = db.Employees.First();
            var shift = db.Shifts.Single(x => x.Name == "Nachtschicht");
            db.PlanningAssignments.Add(new PlanningAssignment
            {
                EmployeeId = employee.Id, WorkstationId = db.Workstations.First().Id,
                ShiftId = shift.Id, Date = monday.AddDays(-1),
                StartTime = shift.StartTime, EndTime = shift.EndTime
            });
            db.Absences.Add(new Absence
            {
                EmployeeId = employee.Id, Type = "Ferien",
                StartDate = monday.AddDays(7), EndDate = monday.AddDays(7)
            });
            db.SaveChanges();
        }
        var vm = new WeekPlanningViewModel { WeekStart = monday };
        vm.CopyPreviousWeekCommand.Execute(null);
        using var check = new AppDbContext();
        Check(!check.PlanningAssignments.Any(x => x.Date == monday.AddDays(6)), "Copied absent Sunday night employee");
        // Removing the absence must not bypass the workstation's Sunday restriction.
        check.Absences.RemoveRange(check.Absences);
        check.SaveChanges();
        vm.CopyPreviousWeekCommand.Execute(null);
        Check(!check.PlanningAssignments.Any(x => x.Date == monday.AddDays(6)), "Copied a shift that is not allowed on Sunday");
        // Only an explicitly permitted Sunday night may now be copied.
        var source = check.PlanningAssignments.Single(x => x.Date == monday.AddDays(-1));
        var rule = check.WorkstationShiftRules.Single(x =>
            x.WorkstationId == source.WorkstationId && x.ShiftId == source.ShiftId);
        rule.Sunday = true;
        check.SaveChanges();
        // With the rule enabled, the next-day absence must still block the copy.
        var absence = new Absence { EmployeeId = source.EmployeeId, Type = "Ferien",
            StartDate = monday.AddDays(7), EndDate = monday.AddDays(7) };
        check.Absences.Add(absence);
        check.SaveChanges();
        vm.CopyPreviousWeekCommand.Execute(null);
        Check(!check.PlanningAssignments.Any(x => x.Date == monday.AddDays(6)), "Allowed Sunday shift ignored following Monday absence");
        check.Absences.Remove(absence);
        check.SaveChanges();
        vm.CopyPreviousWeekCommand.Execute(null);
        Check(check.PlanningAssignments.Any(x => x.Date == monday.AddDays(6)), "Valid Sunday night not copied");
        var employeeId = check.Employees.First().Id;
        check.Absences.Add(new Absence { EmployeeId = employeeId, Type = "Ferien", StartDate = monday.AddDays(7), EndDate = monday.AddDays(7) });
        check.SaveChanges();
        vm.RefreshCommand.Execute(null);
        Check(vm.Alerts.Any(x => x.Message.Contains("Ferien")), "Following Monday warning missing");
    }

    private static ProductionActual MetricActual(int station, string unit, double quantity, double good, double rate) => new()
    {
        ProductionOrder = new ProductionOrder
        {
            WorkstationId = station, Workstation = new Workstation { Id = station, Name = "Linie" }, Unit = unit
        },
        PlannedProductionMinutes = 60, RunMinutes = 60, TotalQuantity = quantity,
        GoodQuantity = good, ScrapQuantity = quantity - good, IdealRatePerHour = rate
    };

    private static void OeeUnits()
    {
        var kg = new[] { MetricActual(1, "kg", 100, 50, 100), MetricActual(1, "kg", 1, 1, 1) };
        var mixed = new[] { MetricActual(1, "kg", 100, 50, 100), MetricActual(1, "g", 1000, 1000, 1000) };
        var first = OeeAnalyticsService.Summarize(kg);
        var second = OeeAnalyticsService.Summarize(mixed);
        Near(first.OeePercent, 75);
        Near(second.OeePercent, first.OeePercent);
        Near(second.QualityPercent, first.QualityPercent);
        Check(second.TotalQuantityText.Contains("kg") && second.TotalQuantityText.Contains(" g"), "Mixed-unit totals not separated");
        Near(OeeAnalyticsService.Summarize(Array.Empty<ProductionActual>()).OeePercent, 0);
        var single = OeeAnalyticsService.Summarize(new[] { kg[0] });
        Near(single.OeePercent, OeeAnalyticsService.Calculate(60, 60, 100, 50, 100).OeePercent);
    }

    private static void WorkstationIdentity()
    {
        var summary = OeeAnalyticsService.Summarize(new[]
        {
            MetricActual(1, "Stück", 100, 50, 100), MetricActual(2, "Stück", 100, 100, 100)
        });
        Check(summary.WorkstationRows.Count == 2, "Same-name workstations merged");
        Near(summary.WorkstationRows.Single(x => x.WorkstationId == 1).OeePercent, 50);
        Near(summary.WorkstationRows.Single(x => x.WorkstationId == 2).OeePercent, 100);
    }

    private static void PasswordInput()
    {
        var view = new UserAdminView();
        var box = view.FindName("NewPasswordBox") as PasswordBox;
        Check(box is not null, "Password input is not masked");
        var vm = (UserAdminViewModel)view.DataContext;
        box!.Password = "Temporary123";
        Check(vm.NewPassword == "Temporary123", "Password not passed to VM");
        vm.NewUserCommand.Execute(null);
        Check(box.Password == string.Empty, "Password not cleared on new user");
    }

    private static void FailedRestore()
    {
        SessionService.SignIn(new UserAccount { Role = UserRoles.Administrator });
        var failed = false;
        try { BackupService.RestoreBackup(Path.Combine(AppPaths.RootDirectory, "missing.kpibackup")); }
        catch (FileNotFoundException) { failed = true; }
        Check(failed && SessionService.IsAuthenticated && !SessionService.RequiresRestart, "Failed restore changed session");
    }

    private static void RestoreSession()
    {
        SessionService.SignIn(new UserAccount { Role = UserRoles.Administrator });
        var backup = BackupService.CreateBackup(Path.Combine(AppPaths.BackupsDirectory, "test.kpibackup"), new AppSettings());
        BackupService.RestoreBackup(backup);
        Check(!SessionService.IsAuthenticated && !SessionService.IsAdministrator && SessionService.RequiresRestart,
            "Old administrator remains authenticated after restore");
    }
}
