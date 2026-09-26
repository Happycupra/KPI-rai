using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;
using Produktionsplanung.App.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static partial class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Produktionsplanung.App.App();
        app.InitializeComponent();
        var tests = new (string Name, Action Run)[]
        {
            ("Batch comparison uses actuals without double counting", BatchComparisonMetrics),
            ("Batch comparison marks missing, partial and inconsistent data", BatchComparisonMissingData),
            ("Batch reports export multipage and missing-data PDFs", BatchReportExports),
            ("Articles create independent batches and preserve identity", ArticleBatchCreation),
            ("Batch lifecycle completes and reopens with audit", BatchLifecycle),
            ("Batch roles and historical actual snapshots", BatchRolesAndActuals),
            ("Batch counts, filters and detail navigation", BatchCountsAndViews),
            ("Batch backup, export and repeat migration", BatchBackupExportMigration),
            ("Legacy batch migration preserves ambiguous data", LegacyBatchMigration),
            ("Explicit batch routing and archive date filters", BatchRoutingAndArchiveFilters),
            ("Demo seeding preserves cleared data and disabled shift models", DemoSeederPreservesChanges),
            ("Day planning only offers shifts allowed for workstation and date", DayPlanningAllowedShifts),
            ("Order status edits preserve existing production run slots", OrderStatusKeepsRunSlots),
            ("Job cards enforce skill matrix qualification levels", JobCardSkillValidation),
            ("Qualification level 5 admin is persisted and enforced", QualificationAdminLevelFive),
            ("Master-data Excel import previews, commits and schedules safely", MasterDataExcelImport),
            ("Manufacturing workflow advances to next job card", ManufacturingWorkflowAutoAdvance),
            ("Routing steps can be reordered and renumbered", RoutingStepReorder),
            ("Calendar weekend filter applies to week and month", CalendarWeekendFilter),
            ("Unified planning staffs production and uses three-letter initials", UnifiedPlanningStaffing),
            ("Planning side panels can be hidden and restored", PlanningPanelToggle),
            ("Planning sidebar is compact with filters collapsed by default", CompactPlanningSidebar),
            ("Sidebar modules keep distinct colors and active-state highlighting", ColoredNavigationActiveState),
            ("Standard action buttons are centered and consistently sized", StandardActionButtons),
            ("Guided app tour is role-aware and contextual help is available", GuidedAppTour),
            ("Employee directory is compact and opens editor on demand", CompactEmployeeDirectory),
            ("Employee skills are edited inline and grouping is available", EmployeeSkillsAndGrouping),
            ("Planning shows team only on production slots and allows removal", PlanningTeamRemoval),
            ("Employee drag staffing updates production team initials", DragStaffToProduction),
            ("Weekly and planning calendar PDF exports finalize cleanly", CalendarPdfExports),
            ("Online week plan package is Firebase-ready and excludes absence details", OnlineWeekPlanPackage),
            ("Production Firebase defaults target solution-compact", FirebaseProductionDefaults),
            ("SQLite TimeSpan queries and null shifts", QuerySmoke),
            ("Manufacturing capacity tab renders read-only metrics", ManufacturingCapacityTabRenders),
            ("Production actual choices sort by date and shift time", ProductionActualOrdering),
            ("Workstation with orders cannot be deleted", WorkstationDeletion),
            ("Order history protected in UI and database", OrderDeletion),
            ("Existing downtime prevents incompatible runtime edits", DowntimeEdit),
            ("Downtime additions use saved production time", DowntimeAddition),
            ("Sunday night copy respects following Monday absence", WeekBoundary),
            ("OEE aggregation is invariant under unit conversion", OeeUnits),
            ("OEE keeps same-name workstations separate", WorkstationIdentity),
            ("Password entry is masked and cleared", PasswordInput),
            ("Work-time status timeline belongs to the logged-in employee", WorkTimeStatusTimeline),
            ("Initial administrator is bound to a stable company tenant", CompanyRegistrationAndInitialAdmin),
            ("Existing installations receive a non-breaking company identity migration", LegacyCompanyIdentityMigration),
            ("Backup restore cannot cross company tenants", BackupTenantIsolation),
            ("Administrator can create a new program user", UserAdminCreatesUser),
            ("Internal user hints persist and require read acknowledgement", UserMessagesPersistAndAcknowledge),
            ("Failed restore preserves active session", FailedRestore),
            ("Successful restore invalidates old session", RestoreSession)
        };
        var failed = 0;
        foreach (var test in tests)
        {
            var path = Path.Combine(Path.GetTempPath(), "kpi-rai-test-" + Guid.NewGuid().ToString("N"));
            AppPaths.RootDirectoryOverride = path;
            SessionService.SignOut();
            // Each fixture represents a fresh process; restore deliberately makes this flag sticky.
            typeof(SessionService).GetProperty(nameof(SessionService.RequiresRestart))!.SetValue(null, false);
            try
            {
                Console.WriteLine("START " + test.Name);
                Console.Out.Flush();
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
        Planner();
        int workstationId;
        int[] orderIds;
        using (var db = new AppDbContext())
        {
            var workstation = db.Workstations.OrderBy(x => x.Id).First();
            workstationId = workstation.Id;
            workstation.IsActive = false;
            db.WorkstationShiftRules.RemoveRange(
                db.WorkstationShiftRules.Where(x => x.WorkstationId == workstationId));
            orderIds = db.ProductionOrders.Select(x => x.Id).ToArray();
            db.SaveChanges();
        }

        foreach (var orderId in orderIds)
            RecycleBinService.MoveProductionOrderToTrash(orderId, "Regressionstest: bewusst geleerte Auftragsliste");

        using (var db = new AppDbContext())
        {
            DemoDataSeeder.Seed(db);

            Check(!db.WorkstationShiftRules.Any(x => x.WorkstationId == workstationId),
                "Seeder recreated deliberately removed workstation shift rules");
            Check(!db.ProductionOrders.Any(),
                "Seeder recreated demo production orders after user cleared them");
            Check(db.ProductionOrders.IgnoreQueryFilters().Count(x => x.IsDeleted) == orderIds.Length,
                "Soft-deleted demo orders were not preserved in history");
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
        Planner();
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

    private static void QualificationAdminLevelFive()
    {
        Planner();
        int qualificationId;
        int workstationId;
        int adminEmployeeId;
        int expertEmployeeId;

        using (var db = new AppDbContext())
        {
            var qualification = db.Qualifications.Single(x => x.Name == "Linie 1");
            var workstation = db.Workstations.Single(x => x.Name == "Linie 1");
            var admin = db.Employees.Single(x => x.PersonnelNumber == "1001");
            var expert = db.Employees.Single(x => x.PersonnelNumber == "1002");

            qualificationId = qualification.Id;
            workstationId = workstation.Id;
            adminEmployeeId = admin.Id;
            expertEmployeeId = expert.Id;

            workstation.RequiredQualificationId = qualification.Id;
            workstation.RequiredQualificationLevel = QualificationLevelCatalog.Administrator;

            var adminLink = db.EmployeeQualifications.Single(x => x.EmployeeId == admin.Id && x.QualificationId == qualification.Id);
            adminLink.Level = QualificationLevelCatalog.Administrator;
            var expertLink = db.EmployeeQualifications.Single(x => x.EmployeeId == expert.Id && x.QualificationId == qualification.Id);
            expertLink.Level = QualificationLevelCatalog.Expert;
            db.SaveChanges();
        }

        using (var db = new AppDbContext())
        {
            var adminCheck = QualificationPlanningService.CheckEmployee(db, adminEmployeeId, workstationId);
            var expertCheck = QualificationPlanningService.CheckEmployee(db, expertEmployeeId, workstationId);
            Check(adminCheck.IsQualified && adminCheck.EmployeeLevel == 5 && adminCheck.RequiredLevel == 5,
                "Admin level 5 was not accepted for a level 5 requirement");
            Check(!expertCheck.IsQualified && expertCheck.EmployeeLevel == 3 && expertCheck.RequiredLevel == 5,
                "Level 3 incorrectly satisfied a level 5 requirement");
        }

        var employeeVm = new EmployeeManagementViewModel();
        employeeVm.EditEmployee(adminEmployeeId);
        var skill = employeeVm.SkillEditorRows.Single(x => x.QualificationId == qualificationId);
        skill.Level = QualificationLevelCatalog.Administrator;
        employeeVm.SaveCommand.Execute(null);
        using (var db = new AppDbContext())
            Check(db.EmployeeQualifications.Single(x => x.EmployeeId == adminEmployeeId && x.QualificationId == qualificationId).Level == 5,
                "Employee editor did not persist admin level 5");

        var workstationVm = new WorkstationManagementViewModel();
        workstationVm.SelectedWorkstation = workstationVm.Workstations.Single(x => x.Id == workstationId);
        Check(workstationVm.SkillLevels.Any(x => x.Level == 5 && x.Name.Contains("Admin")),
            "Workstation qualification choices do not expose admin level 5");
        Check(workstationVm.SelectedRequiredQualificationLevel?.Level == 5,
            "Workstation editor did not load an existing level 5 requirement");

        var manufacturingVm = new ManufacturingControlViewModel();
        Check(manufacturingVm.QualificationLevelChoices.Any(x => x.Level == 5 && x.Name.Contains("Admin")),
            "Manufacturing operation editor does not expose admin level 5");

        var matrix = new SkillMatrixView();
        var grid = (DataGrid)matrix.FindName("MatrixGrid");
        var qualificationColumn = grid.Columns.OfType<DataGridComboBoxColumn>().FirstOrDefault();
        Check(qualificationColumn?.ItemsSource is IEnumerable<QualificationLevelOption> options &&
              options.Any(x => x.Level == 5 && x.Name.Contains("Admin")),
            "Skill matrix does not expose admin level 5");
    }

    private static void MasterDataExcelImport()
    {
        Planner();
        ArticleService.Save(new ArticleMaster
        {
            ArticleNumber = "0001",
            Name = "Import Testartikel",
            Unit = "Stück",
            DefaultQuantity = 1000,
            DefaultIdealRatePerHour = 120,
            IsActive = true
        });

        var path = Path.Combine(AppPaths.RootDirectory, "masterdata-import.xlsx");
        MasterDataExcelImportService.CreateTemplate(path);

        var preview = MasterDataExcelImportService.Import(path, dryRun: true);
        Check(preview.Count >= 5 && preview.All(x => x.Errors.Count == 0),
            "Master-data dry-run reported unexpected errors");

        using (var db = new AppDbContext())
        {
            Check(!db.Employees.Any(x => x.PersonnelNumber == "P001"), "Dry-run persisted employee data");
            Check(!db.Workstations.Any(x => x.Name == "Import Linie"), "Dry-run persisted workstation data");
            Check(!db.Shifts.Any(x => x.Name == "Import Früh"), "Dry-run persisted shift data");
            Check(!db.ProductionOrders.Any(x => x.OrderNumber == "IMPORT-AUF-001"), "Dry-run persisted production order data");
        }

        var imported = MasterDataExcelImportService.Import(path, dryRun: false);
        Check(imported.All(x => x.Errors.Count == 0), "Master-data import reported unexpected errors");

        using (var db = new AppDbContext())
        {
            var employee = db.Employees.Single(x => x.PersonnelNumber == "P001");
            var adminQualification = db.Qualifications.Single(x => x.Name == "Reinigung");
            Check(db.EmployeeQualifications.Single(x => x.EmployeeId == employee.Id && x.QualificationId == adminQualification.Id).Level == 5,
                "Excel import reduced admin qualification level 5");

            var workstation = db.Workstations.Single(x => x.Name == "Import Linie");
            var shift = db.Shifts.Single(x => x.Name == "Import Früh");
            Check(db.WorkstationShiftRules.Any(x => x.WorkstationId == workstation.Id && x.ShiftId == shift.Id && x.Monday && x.Friday),
                "Excel import did not create workstation-shift rules");

            var order = db.ProductionOrders.Single(x => x.OrderNumber == "IMPORT-AUF-001");
            Check(order.ArticleMasterId.HasValue && order.BatchNumber == "IMPORT-CH-001",
                "Excel order import did not preserve article/batch identity");
            Check(db.ProductionRunSlots.Count(x => x.ProductionOrderId == order.Id) == order.PlannedShiftCount,
                "Excel order import did not generate production run slots");
        }

        var repeated = MasterDataExcelImportService.Import(path, dryRun: false);
        var orderResult = repeated.Single(x => x.Area == "Produktionsaufträge");
        Check(orderResult.SkippedCount >= 1, "Duplicate order was not reported as skipped");
        using var check = new AppDbContext();
        Check(check.ProductionOrders.Count(x => x.OrderNumber == "IMPORT-AUF-001") == 1,
            "Repeated Excel import duplicated the production order");
    }

    private static void JobCardSkillValidation()
    {
        int cardId;
        int orderId;
        int qualifiedEmployeeId;
        int underqualifiedEmployeeId;

        using (var db = new AppDbContext())
        {
            var qualification = db.Qualifications.Single(x => x.Name == "Linie 1");
            var qualifiedEmployee = db.Employees.Single(x => x.PersonnelNumber == "1001"); // Level 3
            var underqualifiedEmployee = db.Employees.Single(x => x.PersonnelNumber == "1002"); // Level 2
            var order = db.ProductionOrders.OrderBy(x => x.Id).First();

            orderId = order.Id;
            qualifiedEmployeeId = qualifiedEmployee.Id;
            underqualifiedEmployeeId = underqualifiedEmployee.Id;

            var card = new JobCard
            {
                ProductionOrderId = order.Id,
                SequenceNumber = 900,
                OperationCode = "SKILL-TEST",
                OperationName = "Skill-Prüfung",
                WorkstationId = order.WorkstationId,
                RequiredQualificationId = qualification.Id,
                RequiredQualificationNameSnapshot = qualification.Name,
                RequiredQualificationLevel = 3,
                PlannedMinutes = 30,
                RequiredStaff = 1,
                Status = "Bereit"
            };
            db.JobCards.Add(card);
            db.SaveChanges();
            cardId = card.Id;
        }

        SessionService.SignIn(new UserAccount
        {
            Username = "regression-planner",
            DisplayName = "Regression Planer",
            Role = UserRoles.Planner,
            IsActive = true
        });

        var vm = new ManufacturingControlViewModel();
        vm.SelectedProductionOrder = vm.ProductionOrders.Single(x => x.Id == orderId);
        vm.SelectedJobCard = vm.JobCards.Single(x => x.Id == cardId);

        vm.AutoAssignBestEmployeeCommand.Execute(null);
        Check(vm.SelectedJobCardEmployeeChoice is { IsQualified: true, IsAbsent: false },
            "Automatic staffing did not choose an available qualified employee");

        var underqualified = vm.JobCardEmployeeChoices.Single(x => x.EmployeeId == underqualifiedEmployeeId);
        Check(!underqualified.IsQualified && underqualified.QualificationLevel == 2,
            "Skill matrix did not mark level-2 employee as underqualified for level 3");
        vm.SelectedJobCardEmployeeChoice = underqualified;
        vm.StartJobCardCommand.Execute(null);

        using (var check = new AppDbContext())
        {
            var blocked = check.JobCards.Single(x => x.Id == cardId);
            Check(blocked.Status == "Bereit", "Underqualified employee was allowed to start job card");
            Check(!blocked.EmployeeId.HasValue, "Blocked skill assignment was persisted");
        }
        Check(vm.StatusMessage.Contains("Level 2") && vm.StatusMessage.Contains("Level 3"),
            "Skill block message does not explain actual and required level");

        vm.SelectedJobCard = vm.JobCards.Single(x => x.Id == cardId);
        var qualified = vm.JobCardEmployeeChoices.Single(x => x.EmployeeId == qualifiedEmployeeId);
        Check(qualified.IsQualified && qualified.QualificationLevel == 3,
            "Skill matrix did not mark level-3 employee as qualified");
        vm.SelectedJobCardEmployeeChoice = qualified;
        vm.StartJobCardCommand.Execute(null);

        using var finalCheck = new AppDbContext();
        var started = finalCheck.JobCards.Single(x => x.Id == cardId);
        Check(started.Status == "In Produktion", "Qualified employee could not start job card");
        Check(started.EmployeeId == qualifiedEmployeeId, "Qualified employee assignment was not persisted");
    }

    private static void ManufacturingWorkflowAutoAdvance()
    {
        int orderId;
        int firstId;
        int secondId;
        using (var db = new AppDbContext())
        {
            var order = db.ProductionOrders.OrderBy(x => x.Id).First();
            orderId = order.Id;
            var first = new JobCard
            {
                ProductionOrderId = order.Id, SequenceNumber = 10, OperationCode = "A",
                OperationName = "Vorbereitung", WorkstationId = order.WorkstationId,
                PlannedMinutes = 20, RequiredStaff = 1, Status = "Bereit"
            };
            var second = new JobCard
            {
                ProductionOrderId = order.Id, SequenceNumber = 20, OperationCode = "B",
                OperationName = "Montage", WorkstationId = order.WorkstationId,
                PlannedMinutes = 30, RequiredStaff = 1, Status = "Bereit"
            };
            db.JobCards.AddRange(first, second);
            db.SaveChanges();
            firstId = first.Id;
            secondId = second.Id;
        }

        SessionService.SignIn(new UserAccount
        {
            Username = "workflow-planner", DisplayName = "Workflow Planer",
            Role = UserRoles.Planner, IsActive = true
        });

        var vm = new ManufacturingControlViewModel();
        vm.SelectedProductionOrder = vm.ProductionOrders.Single(x => x.Id == orderId);
        Check(vm.CockpitOrders.Any(x => x.Id == orderId), "Order cockpit does not contain production order");
        vm.SelectedJobCard = vm.JobCards.Single(x => x.Id == firstId);
        vm.FinishGoodQuantity = 1;
        vm.FinishJobCardCommand.Execute(null);

        using var check = new AppDbContext();
        Check(check.JobCards.Single(x => x.Id == firstId).Status == "Fertig",
            "Completed job card was not saved");
        Check(vm.SelectedJobCard?.Id == secondId,
            "Workflow did not advance to the next open job card");
        Check(vm.CurrentOperationText.Contains("Montage"),
            "Workflow cockpit did not update the current operation");
    }

    private static void RoutingStepReorder()
    {
        int routingId;
        int firstId;
        int secondId;
        using (var db = new AppDbContext())
        {
            var workstation = db.Workstations.OrderBy(x => x.Id).First();
            var op1 = new OperationDefinition
            {
                Code = "R-10", Name = "Schritt A", DefaultWorkstationId = workstation.Id,
                DefaultMinutes = 10, RequiredStaff = 1
            };
            var op2 = new OperationDefinition
            {
                Code = "R-20", Name = "Schritt B", DefaultWorkstationId = workstation.Id,
                DefaultMinutes = 20, RequiredStaff = 1
            };
            var routing = new ManufacturingRouting { Product = "Routing-Test", Name = "Testplan", IsActive = true };
            db.AddRange(op1, op2, routing);
            db.SaveChanges();
            var first = new RoutingStep
            {
                ManufacturingRoutingId = routing.Id, OperationDefinitionId = op1.Id,
                SequenceNumber = 10, WorkstationId = workstation.Id, PlannedMinutes = 10, RequiredStaff = 1
            };
            var second = new RoutingStep
            {
                ManufacturingRoutingId = routing.Id, OperationDefinitionId = op2.Id,
                SequenceNumber = 20, WorkstationId = workstation.Id, PlannedMinutes = 20, RequiredStaff = 1
            };
            db.RoutingSteps.AddRange(first, second);
            db.SaveChanges();
            routingId = routing.Id;
            firstId = first.Id;
            secondId = second.Id;
        }

        SessionService.SignIn(new UserAccount
        {
            Username = "routing-planner", DisplayName = "Routing Planer",
            Role = UserRoles.Planner, IsActive = true
        });
        var vm = new ManufacturingControlViewModel();
        vm.SelectedRouting = vm.Routings.Single(x => x.Id == routingId);
        vm.MoveRoutingStep(secondId, firstId);

        using var check = new AppDbContext();
        var ordered = check.RoutingSteps.Where(x => x.ManufacturingRoutingId == routingId)
            .OrderBy(x => x.SequenceNumber).ToList();
        Check(ordered.Count == 2 && ordered[0].Id == secondId && ordered[0].SequenceNumber == 10 &&
              ordered[1].Id == firstId && ordered[1].SequenceNumber == 20,
            "Routing drag/drop reorder did not persist or renumber steps");
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

    private static void UnifiedPlanningStaffing()
    {
        Check(EmployeeInitialsService.Build3("Irajet", "Ramadani") == "IRA",
            "Three-letter initials are not first-name plus two surname letters");

        DateTime date;
        int runSlotId;
        int employeeId;
        using (var db = new AppDbContext())
        {
            var slot = db.ProductionRunSlots
                .Include(x => x.ProductionOrder)
                .OrderBy(x => x.Date)
                .ThenBy(x => x.Id)
                .First();
            date = slot.Date.Date;
            runSlotId = slot.Id;
            var suggestion = QualificationPlanningService
                .Suggest(date, slot.ProductionOrder.WorkstationId, slot.ShiftId)
                .FirstOrDefault();
            Check(suggestion is not null, "No employee available for unified planning staffing test");
            employeeId = suggestion!.EmployeeId;
        }

        SessionService.SignIn(new UserAccount
        {
            Username = "unified-planner",
            DisplayName = "Unified Planner",
            Role = UserRoles.Planner,
            IsActive = true
        });

        var vm = new PlanningCalendarViewModel
        {
            SelectedDate = date,
            SelectedViewIndex = 0
        };
        var employee = vm.DayEmployees.Single(x => x.EmployeeId == employeeId);
        Check(employee.Initials.Length == 3,
            $"Planning employee initials are not three letters: {employee.Initials}");

        var entry = vm.DayTimedEntries.Single(x => x.EntryType == "Auftrag" && x.EntryId == runSlotId);
        Check(vm.AssignEmployeeToProduction(employeeId, entry),
            $"Unified planning rejected valid staffing: {vm.StatusMessage}");

        using var check = new AppDbContext();
        Check(check.PlanningAssignments.Any(x =>
                x.EmployeeId == employeeId &&
                x.Date.Date == date &&
                x.WorkstationId == entry.WorkstationId &&
                x.ShiftId == entry.ShiftId),
            "Unified planning did not persist employee assignment");

        var refreshed = vm.DayTimedEntries.Single(x => x.EntryType == "Auftrag" && x.EntryId == runSlotId);
        Check(refreshed.TeamText.Contains(employee.Initials, StringComparison.Ordinal),
            "Unified planning did not refresh three-letter team initials");
    }

    private static void PlanningPanelToggle()
    {
        var view = new PlanningCalendarView();
        var rightButton = view.FindName("CalendarRightToggleButton") as Button;
        var rightColumn = view.FindName("CalendarDetailColumn") as ColumnDefinition;
        var leftButton = view.FindName("CalendarLeftToggleButton") as Button;
        var leftColumn = view.FindName("CalendarLeftColumn") as ColumnDefinition;
        Check(rightButton is not null && rightColumn is not null && leftButton is not null && leftColumn is not null,
            "Planning panel toggle controls are missing");

        rightButton!.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        Check(rightColumn!.Width.Value == 0, "Right planning panel did not collapse");
        rightButton.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        Check(rightColumn.Width.Value > 0, "Right planning panel could not be restored");

        leftButton!.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        Check(leftColumn!.Width.Value == 0, "Left planning panel did not collapse");
        leftButton.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        Check(leftColumn.Width.Value > 0, "Left planning panel could not be restored");
    }

    private static void CompactPlanningSidebar()
    {
        var view = new PlanningCalendarView();
        var leftColumn = view.FindName("CalendarLeftColumn") as ColumnDefinition;
        var filter = view.FindName("PlanningFilterExpander") as Expander;

        Check(leftColumn is not null && leftColumn.Width.Value <= 205,
            $"Planning sidebar is not compact: {leftColumn?.Width.Value}");
        Check(filter is not null && !filter.IsExpanded,
            "Planning filters should be collapsed by default");
    }

    private static void StandardActionButtons()
    {
        Planner();
        var actionStyle = (Style)Application.Current.FindResource("ActionButtonStyle");
        var primaryStyle = (Style)Application.Current.FindResource("PrimaryActionButtonStyle");
        var dangerStyle = (Style)Application.Current.FindResource("DangerActionButtonStyle");
        var compactStyle = (Style)Application.Current.FindResource("CompactButtonStyle");
        var textBoxStyle = (Style)Application.Current.FindResource(typeof(TextBox));
        var comboBoxStyle = (Style)Application.Current.FindResource(typeof(ComboBox));
        var datePickerStyle = (Style)Application.Current.FindResource(typeof(DatePicker));
        var passwordBoxStyle = (Style)Application.Current.FindResource(typeof(PasswordBox));

        static object? SetterValue(Style style, DependencyProperty property) =>
            style.Setters.OfType<Setter>().LastOrDefault(x => x.Property == property)?.Value;

        foreach (var style in new[] { actionStyle, primaryStyle, dangerStyle })
        {
            Check(Equals(SetterValue(style, Control.HorizontalContentAlignmentProperty), HorizontalAlignment.Center),
                "Action button style is not horizontally centered.");
            Check(Equals(SetterValue(style, Control.VerticalContentAlignmentProperty), VerticalAlignment.Center),
                "Action button style is not vertically centered.");
            Check(Convert.ToDouble(SetterValue(style, FrameworkElement.MinHeightProperty)) >= 38,
                "Action button minimum height is below the UI standard.");
            Check(Convert.ToDouble(SetterValue(style, FrameworkElement.MinWidthProperty)) >= 104,
                "Action button minimum width is below the UI standard.");
        }

        Check(Equals(SetterValue(compactStyle, Control.HorizontalContentAlignmentProperty), HorizontalAlignment.Center),
            "Compact icon button style is not horizontally centered.");
        Check(Equals(SetterValue(compactStyle, Control.VerticalContentAlignmentProperty), VerticalAlignment.Center),
            "Compact icon button style is not vertically centered.");

        foreach (var inputStyle in new[] { textBoxStyle, comboBoxStyle, datePickerStyle, passwordBoxStyle })
        {
            Check(Convert.ToDouble(SetterValue(inputStyle, FrameworkElement.HeightProperty)) >= 38,
                "A global input style is below the 38px UI standard.");
            Check(Equals(SetterValue(inputStyle, FrameworkElement.HorizontalAlignmentProperty), HorizontalAlignment.Stretch),
                "A global input style does not stretch consistently in forms.");
            Check(Equals(SetterValue(inputStyle, Control.VerticalContentAlignmentProperty), VerticalAlignment.Center),
                "A global input style is not vertically centered.");
        }

        var multilineTrigger = textBoxStyle.Triggers.OfType<Trigger>()
            .FirstOrDefault(x => x.Property == TextBox.AcceptsReturnProperty && Equals(x.Value, true));
        Check(multilineTrigger is not null, "TextBox style has no multiline writing-area trigger.");
        var multilineMinHeight = multilineTrigger!.Setters.OfType<Setter>()
            .FirstOrDefault(x => x.Property == FrameworkElement.MinHeightProperty)?.Value;
        Check(multilineMinHeight is not null && Convert.ToDouble(multilineMinHeight) >= 72,
            "Multiline writing areas are below the 72px minimum height.");

        var views = new FrameworkElement[]
        {
            new ShiftsView(),
            new AbsencesView(),
            new WorkstationsView(),
            new ArticlesView(),
            new ProductionOrdersView(),
            new ProductionActualView(),
            new SkillMatrixView(),
            new DashboardView(),
            new PlanningCalendarView(),
            new DayPlanningView(),
            new WeekPlanningView(),
            new WorkTimeCalendarView(),
            new AnalyticsView(),
            new ManufacturingControlView(),
            new SettingsView()
        };

        foreach (var view in views)
        {
            foreach (var button in LogicalDescendants<Button>(view))
            {
                Check(button.HorizontalContentAlignment == HorizontalAlignment.Center,
                    $"Button '{button.Content}' in {view.GetType().Name} is not horizontally centered.");
                Check(button.VerticalContentAlignment == VerticalAlignment.Center,
                    $"Button '{button.Content}' in {view.GetType().Name} is not vertically centered.");

                if (button.Style == actionStyle || button.Style == primaryStyle || button.Style == dangerStyle)
                    Check(button.MinHeight >= 38 || button.Height >= 38,
                        $"Button '{button.Content}' in {view.GetType().Name} is below the standard height.");
            }

            foreach (var textBox in LogicalDescendants<TextBox>(view))
            {
                if (textBox.AcceptsReturn)
                {
                    Check(textBox.MinHeight >= 72 || (!double.IsNaN(textBox.Height) && textBox.Height >= 72),
                        $"Multiline TextBox in {view.GetType().Name} is too small.");
                }
                else
                {
                    Check(!double.IsNaN(textBox.Height) && textBox.Height >= 38,
                        $"TextBox in {view.GetType().Name} is below the standard height.");
                }
            }

            foreach (var combo in LogicalDescendants<ComboBox>(view))
                Check(!double.IsNaN(combo.Height) && combo.Height >= 38,
                    $"ComboBox in {view.GetType().Name} is below the standard height.");

            foreach (var picker in LogicalDescendants<DatePicker>(view))
                Check(!double.IsNaN(picker.Height) && picker.Height >= 38,
                    $"DatePicker in {view.GetType().Name} is below the standard height.");
        }
    }

    private static void ColoredNavigationActiveState()
    {
        Planner();
        var window = new Produktionsplanung.App.MainWindow();
        try
        {
            var names = new[]
            {
                "DashboardButton", "PlanningCalendarButton", "WorkTimeCalendarButton",
                "BatchesButton", "ProductionOrdersButton", "ManufacturingControlButton",
                "ProductionActualButton", "AnalyticsButton", "ArticlesButton", "EmployeesButton",
                "WorkstationsButton", "AbsencesButton", "UserAdminButton", "SettingsButton"
            };
            var buttons = names.Select(name => (Button?)window.FindName(name))
                .Where(x => x is not null)
                .Cast<Button>()
                .ToArray();

            Check(buttons.Length == names.Length, "Not all navigation buttons were found.");
            Check(buttons.All(x => x.CommandParameter is not null),
                "A navigation module is missing its persistent accent color.");
            Check(buttons.Select(x => x.CommandParameter!.ToString()).Distinct().Count() == buttons.Length,
                "Navigation module accent colors are not unique.");

            var dashboard = (Button)window.FindName("DashboardButton");
            var orders = (Button)window.FindName("ProductionOrdersButton");

            Check(dashboard.BorderBrush is SolidColorBrush activeDashboard && activeDashboard.Color.A > 0,
                "Dashboard is not visibly marked as the active area after startup.");
            Check(orders.BorderBrush is SolidColorBrush inactiveOrders && inactiveOrders.Color.A == 0,
                "Inactive navigation area should not show an active border.");

            orders.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Check(orders.BorderBrush is SolidColorBrush activeOrders && activeOrders.Color.A > 0,
                "Selected production orders area did not receive its active accent.");
            Check(orders.Background is SolidColorBrush activeBackground && activeBackground.Color.A > 0,
                "Selected production orders area did not receive a tinted active background.");
            Check(dashboard.BorderBrush is SolidColorBrush inactiveDashboard && inactiveDashboard.Color.A == 0,
                "Previous navigation area stayed active after switching modules.");

            var toggle = (Button)window.FindName("SidebarToggleButton");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var sidebarColumn = (ColumnDefinition)window.FindName("SidebarColumn");

            Check(sidebarColumn.Width.Value == 78,
                $"Collapsed sidebar width is not the compact standard: {sidebarColumn.Width.Value}");
            Check(buttons.All(x => x.Width == 48 && x.Height == 48),
                "Collapsed navigation buttons are not fixed to 48x48.");
            Check(buttons.All(x => x.HorizontalAlignment == HorizontalAlignment.Center &&
                                   x.HorizontalContentAlignment == HorizontalAlignment.Center),
                "Collapsed navigation icons are not centered.");
            Check(buttons.All(x => string.IsNullOrEmpty(x.Content?.ToString()) && x.ToolTip is not null),
                "Collapsed navigation should hide labels but retain tooltips.");
            Check(orders.BorderThickness.Left >= 1 && orders.BorderBrush is SolidColorBrush collapsedActive &&
                  collapsedActive.Color.A > 0,
                "Active collapsed navigation icon is not visually emphasized.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void GuidedAppTour()
    {
        SessionService.SignIn(new UserAccount
        {
            Id = 501,
            Username = "tour-observer",
            DisplayName = "Tour Observer",
            Role = UserRoles.Observer,
            IsActive = true
        });
        var observer = AppTourCatalog.GetAvailableSteps();
        Check(observer.Any(x => x.Key == "welcome") && observer.Any(x => x.Key == "analytics"),
            "Observer tour is missing general introduction steps");
        Check(observer.All(x => x.Key != "planning" && x.Key != "users" && x.Key != "settings"),
            "Observer tour exposes planner/admin-only steps");

        Planner();
        var planner = AppTourCatalog.GetAvailableSteps();
        Check(planner.Any(x => x.Key == "planning") && planner.Any(x => x.Key == "employees"),
            "Planner tour is missing operational steps");
        Check(planner.All(x => x.Key != "users" && x.Key != "settings"),
            "Planner tour exposes administrator-only steps");

        SessionService.SignIn(new UserAccount
        {
            Id = 502,
            Username = "tour-admin",
            DisplayName = "Tour Admin",
            Role = UserRoles.Administrator,
            IsActive = true
        });
        var admin = AppTourCatalog.GetAvailableSteps();
        Check(admin.Any(x => x.Key == "users") && admin.Any(x => x.Key == "settings"),
            "Administrator tour is missing administration steps");
        Check(AppTourCatalog.GetContextHint("PlanningCalendarButton").Contains("Tages-", StringComparison.Ordinal) ||
              AppTourCatalog.GetContextHint("PlanningCalendarButton").Contains("planst", StringComparison.OrdinalIgnoreCase),
            "Planning context hint is missing");

        AppSettingsService.UpdateCurrentUserPreferences(p =>
        {
            p.ShowContextHints = false;
            p.AppTourLastShownVersion = AppTourCatalog.CurrentVersion;
            p.AppTourCompletedVersion = AppTourCatalog.CurrentVersion;
        });
        var prefs = AppSettingsService.LoadCurrentUserPreferences();
        Check(!prefs.ShowContextHints &&
              prefs.AppTourLastShownVersion == AppTourCatalog.CurrentVersion &&
              prefs.AppTourCompletedVersion == AppTourCatalog.CurrentVersion,
            "Tour/context-help preferences were not persisted per user");

        var main = new Produktionsplanung.App.MainWindow();
        try
        {
            Check(main.FindName("HelpButton") is Button,
                "Main window does not expose the top-bar help button");
            Check(main.FindName("CurrentPageHint") is TextBlock,
                "Main window does not expose contextual page help");

            var tour = new Produktionsplanung.App.AppTourWindow(main, 0);
            try
            {
                Check(tour.FindName("StepTitleText") is TextBlock title &&
                      !string.IsNullOrWhiteSpace(title.Text),
                    "Guided tour window did not render its first step");
            }
            finally
            {
                tour.Close();
            }
        }
        finally
        {
            main.Close();
        }
    }

    private static void CompactEmployeeDirectory()
    {
        var vm = new EmployeeManagementViewModel();
        Check(!vm.IsEditorOpen, "Employee editor should be closed when directory opens");
        Check(vm.EmployeeRows.Count > 0, "Compact employee directory is empty");

        var row = vm.EmployeeRows.First();
        Check(row.Initials.Length == 3,
            $"Employee directory initials are not three letters: {row.Initials}");
        Check(!string.IsNullOrWhiteSpace(row.FullName),
            "Compact employee row does not show a name");

        vm.EditEmployee(row.Id);
        Check(vm.IsEditorOpen, "Right-click edit target did not open employee editor");
        Check(vm.EditingId == row.Id, "Employee editor opened the wrong employee");
        Check(vm.PersonnelNumber == row.PersonnelNumber &&
              vm.FirstName == row.FirstName &&
              vm.LastName == row.LastName,
            "Employee editor did not load all base employee data");

        vm.CloseEditor();
        Check(!vm.IsEditorOpen, "Employee editor could not be closed");
    }

    private static void EmployeeSkillsAndGrouping()
    {
        int employeeId;
        int qualificationId;
        using (var db = new AppDbContext())
        {
            var employee = db.Employees.OrderBy(x => x.Id).First();
            employeeId = employee.Id;
            var qualification = new Qualification { Name = "Regression Skill" };
            db.Qualifications.Add(qualification);
            db.SaveChanges();
            qualificationId = qualification.Id;
        }

        var vm = new EmployeeManagementViewModel();
        vm.EditEmployee(employeeId);
        var skill = vm.SkillEditorRows.Single(x => x.QualificationId == qualificationId);
        skill.Level = 3;
        vm.SaveCommand.Execute(null);

        using (var check = new AppDbContext())
        {
            var link = check.EmployeeQualifications.AsNoTracking()
                .SingleOrDefault(x => x.EmployeeId == employeeId && x.QualificationId == qualificationId);
            Check(link is not null && link.Level == 3,
                "Inline employee skill editor did not persist qualification level");
        }

        vm.SelectedGroupMode = "Funktion";
        Check(vm.EmployeeRows.All(x => !string.IsNullOrWhiteSpace(x.GroupKey)),
            "Employee grouping by function produced empty group keys");
        vm.SelectedGroupMode = "Abteilung";
        Check(vm.EmployeeRows.All(x => !string.IsNullOrWhiteSpace(x.GroupKey)),
            "Employee grouping by department produced empty group keys");
    }

    private static void PlanningTeamRemoval()
    {
        DateTime date;
        int runSlotId;
        int employeeId;

        using (var db = new AppDbContext())
        {
            var slot = db.ProductionRunSlots
                .Include(x => x.ProductionOrder)
                .OrderBy(x => x.Date)
                .ThenBy(x => x.Id)
                .First();
            date = slot.Date.Date;
            runSlotId = slot.Id;

            var suggestion = QualificationPlanningService
                .Suggest(date, slot.ProductionOrder.WorkstationId, slot.ShiftId)
                .FirstOrDefault();
            Check(suggestion is not null, "No employee available for planning team removal test");
            employeeId = suggestion!.EmployeeId;
        }

        SessionService.SignIn(new UserAccount
        {
            Username = "team-planner",
            DisplayName = "Team Planner",
            Role = UserRoles.Planner,
            IsActive = true
        });

        var assign = ProductionStaffingService.AssignEmployeeToRunSlot(employeeId, runSlotId);
        Check(assign.Success, $"Could not prepare production team: {assign.Message}");

        var vm = new PlanningCalendarViewModel
        {
            SelectedDate = date,
            SelectedViewIndex = 0
        };

        var slotEntry = vm.DayTimedEntries.Single(x => x.EntryType == "Auftrag" && x.RunSlotId == runSlotId);
        Check(!vm.DayTimedEntries.Any(x => x.EntryType == "Einsatz" && x.EmployeeId == employeeId),
            "Production staffing is still duplicated as a separate calendar assignment card");

        vm.SelectEntry(slotEntry);
        var member = vm.SelectedProductionTeam.Single(x => x.EmployeeId == employeeId);
        vm.RemoveProductionTeamMemberCommand.Execute(member);

        using var check = new AppDbContext();
        Check(!check.PlanningAssignments.Any(x =>
                x.EmployeeId == employeeId &&
                x.Date.Date == date &&
                x.WorkstationId == slotEntry.WorkstationId &&
                x.ShiftId == slotEntry.ShiftId),
            "Removing employee from production slot did not delete planning assignment");
        Check(vm.SelectedProductionTeam.All(x => x.EmployeeId != employeeId),
            "Removed employee is still shown in selected production team");
    }

    private static void DragStaffToProduction()
    {
        DateTime date;
        int runSlotId;
        int employeeId;

        using (var db = new AppDbContext())
        {
            var slot = db.ProductionRunSlots
                .Include(x => x.ProductionOrder)
                .OrderBy(x => x.Date)
                .ThenBy(x => x.Id)
                .First();
            date = slot.Date.Date;
            runSlotId = slot.Id;

            var suggestion = QualificationPlanningService
                .Suggest(date, slot.ProductionOrder.WorkstationId, slot.ShiftId)
                .FirstOrDefault();
            Check(suggestion is not null, "No employee available for drag/drop staffing regression test");
            employeeId = suggestion!.EmployeeId;
        }

        SessionService.SignIn(new UserAccount
        {
            Username = "drag-planner",
            DisplayName = "Drag Planner",
            Role = UserRoles.Planner,
            IsActive = true
        });

        var vm = new DayPlanningViewModel { SelectedDate = date };
        vm.RefreshProductionOrderCoverage();
        var coverage = vm.ProductionOrderCoverage.Single(x => x.RunSlotId == runSlotId);
        var employee = vm.Employees.Single(x => x.Id == employeeId);

        Check(vm.AssignEmployeeToProduction(employeeId, coverage),
            "Drag/drop staffing was rejected for a valid available employee");

        using var check = new AppDbContext();
        Check(check.PlanningAssignments.Any(x =>
                x.EmployeeId == employeeId &&
                x.Date.Date == date &&
                x.WorkstationId == coverage.WorkstationId &&
                x.ShiftId == coverage.ShiftId),
            "Drag/drop staffing did not persist the planning assignment");

        var refreshed = vm.ProductionOrderCoverage.Single(x => x.RunSlotId == runSlotId);
        Check(refreshed.TeamInitials.Contains(employee.Initials, StringComparison.Ordinal),
            "Production team initials were not refreshed after drag/drop staffing");
    }

    private static void CalendarPdfExports()
    {
        var weeklyPath = Path.Combine(AppPaths.RootDirectory, "weekly-no-weekend.pdf");
        var weekly = WeeklyPlanPdfService.Export(new DateTime(2030, 1, 14), weeklyPath, includeWeekends: false);
        Check(File.Exists(weeklyPath) && new FileInfo(weeklyPath).Length > 0,
            "Weekly PDF export did not create a file");
        Check(weekly.PageCount > 0 && !weekly.IncludesWeekends,
            "Weekly PDF export did not preserve the weekend option");

        var calendar = new PlanningCalendarViewModel
        {
            SelectedDate = new DateTime(2030, 1, 14),
            SelectedViewIndex = 1,
            ShowWeekends = false
        };
        var calendarPath = Path.Combine(AppPaths.RootDirectory, "planning-calendar.pdf");
        var calendarResult = PlanningCalendarPdfService.Export(calendar, calendarPath);
        Check(File.Exists(calendarPath) && new FileInfo(calendarPath).Length > 0,
            "Planning calendar PDF export did not create a file");
        Check(calendarResult.PageCount > 0 && !calendarResult.IncludesWeekends,
            "Planning calendar PDF did not preserve the visible weekend setting");
    }

    private static void FirebaseProductionDefaults()
    {
        var settings = new AppSettings();
        Check(settings.FirebaseProjectId == "solution-compact",
            "Firebase production project id default is incorrect");
        Check(settings.FirebaseWebApiKey == "AIzaSyDvPkzX6B5vmA2VWjZooDW08Pw17mRA29Y",
            "Firebase web API key default is incorrect");
        Check(settings.FirebaseAuthEndpoint == "https://europe-west1-solution-compact.cloudfunctions.net/login",
            "Firebase login endpoint default is incorrect");
        Check(settings.FirebasePublishEndpoint == "https://europe-west1-solution-compact.cloudfunctions.net/publishWeekPlan",
            "Firebase publish endpoint default is incorrect");
        Check(settings.FirebaseHostingUrl == "https://solution-compact.web.app",
            "Firebase hosting URL default is incorrect");
        Check(!settings.OnlineWeekPlanEnabled,
            "Online week plan must stay disabled until Firebase deployment is completed");
    }

    private static void OnlineWeekPlanPackage()
    {
        var monday = new DateTime(2030, 1, 14);
        var company = CompanyIdentityService.RegisterLocalCompany("Muster Produktion AG", "MUSTER-AG");
        Check(company.Success, "Test company registration failed");
        SessionService.SignIn(new UserAccount
        {
            Id = 999,
            Username = "online-admin",
            DisplayName = "Online Admin",
            Role = UserRoles.Administrator,
            IsActive = true
        });

        string employeeName;
        using (var db = new AppDbContext())
        {
            var employee = db.Employees.First(x => x.IsActive);
            var workstation = db.Workstations.First(x => x.IsActive);
            var shift = db.Shifts.First();
            employeeName = $"{employee.LastName}, {employee.FirstName}";
            db.PlanningAssignments.Add(new PlanningAssignment
            {
                EmployeeId = employee.Id,
                WorkstationId = workstation.Id,
                ShiftId = shift.Id,
                Date = monday,
                StartTime = shift.StartTime,
                EndTime = shift.EndTime,
                BreakMinutes = shift.BreakMinutes,
                Comment = "Interner Einsatzkommentar"
            });
            db.Absences.Add(new Absence
            {
                EmployeeId = employee.Id,
                Type = "Krank vertraulich",
                StartDate = monday,
                EndDate = monday,
                Comment = "Darf nicht online erscheinen"
            });
            db.SaveChanges();
        }

        var snapshot = OnlineWeekPlanService.BuildSnapshot(monday);
        Check(snapshot.SchemaVersion == "1.1" &&
              snapshot.CompanyCode == "MUSTER-AG" &&
              snapshot.CompanyId == company.Settings!.CompanyId &&
              snapshot.Entries.Any(x => x.EmployeeName == employeeName),
            "Online week plan snapshot did not include the planned employee");
        Check(snapshot.Entries.All(x => !x.Note.Contains("vertraulich", StringComparison.OrdinalIgnoreCase)),
            "Online week plan leaked absence details");

        var package = OnlineWeekPlanService.PreparePackage(monday);
        Check(File.Exists(package.FilePath), "Online week plan JSON package was not created");
        var json = File.ReadAllText(package.FilePath);
        Check(json.Contains("\"schemaVersion\": \"1.1\"", StringComparison.Ordinal) &&
              json.Contains("\"companyCode\": \"MUSTER-AG\"", StringComparison.Ordinal) &&
              !json.Contains("Interner Einsatzkommentar", StringComparison.Ordinal) &&
              !json.Contains("Krank vertraulich", StringComparison.Ordinal) &&
              !json.Contains("Darf nicht online erscheinen", StringComparison.Ordinal),
            "Online week plan package content is incomplete or leaks absence data");

        var settings = AppSettingsService.Load();
        Check(settings.LastOnlineWeekPreparedId == package.WeekId && settings.LastOnlineWeekPreparedAtUtc.HasValue,
            "Prepared online week plan status was not saved");

        SessionService.SignIn(new UserAccount { Id = 1000, Username = "observer", Role = UserRoles.Observer });
        try
        {
            OnlineWeekPlanService.PreparePackage(monday);
            throw new InvalidOperationException("Observer unexpectedly prepared an online week plan");
        }
        catch (InvalidOperationException ex)
        {
            Check(ex.Message.Contains("Administrator", StringComparison.OrdinalIgnoreCase),
                "Observer rejection did not explain the administrator requirement");
        }
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
        _ = new ManufacturingControlView();
        _ = new EmployeesView();
        _ = new WorkstationsView();
        _ = new ShiftsView();
        _ = new WorkplacesShiftsView();
        _ = new AbsencesView();
        _ = new SkillMatrixView();
        _ = new SettingsView();
        _ = new Produktionsplanung.App.LoginWindow();
        _ = new Produktionsplanung.App.ChangePasswordWindow();
        _ = new Produktionsplanung.App.MessagePopupWindow(new UserMessageRow
        {
            Id = 1,
            Partner = "Kollege",
            Subject = "Hinweis",
            Body = "Testnachricht",
            CreatedAtUtc = DateTime.UtcNow
        });
        _ = new Produktionsplanung.App.MessageCenterWindow();
    }

    private static void ManufacturingCapacityTabRenders()
    {
        var view = new ManufacturingControlView
        {
            Width = 1280,
            Height = 900
        };
        var capacityTab = LogicalDescendants<TabItem>(view)
            .Single(x => string.Equals(x.Header?.ToString(), "Kapazität", StringComparison.Ordinal));
        capacityTab.IsSelected = true;

        view.Measure(new Size(1280, 900));
        view.Arrange(new Rect(0, 0, 1280, 900));
        view.UpdateLayout();

        Check(capacityTab.IsSelected, "Capacity tab could not be rendered");

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KPI_BATCH_PREVIEW_DIRECTORY")))
        {
            Planner();
            var mainWindow = new Produktionsplanung.App.MainWindow();
            RenderPreview((FrameworkElement)mainWindow.Content, "navigation");
            mainWindow.Close();
        }
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in LogicalDescendants<T>(child))
                yield return descendant;
        }
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
        Planner();
        var actualId = AddActual();
        int orderId;
        using (var db = new AppDbContext())
            orderId = db.ProductionActuals.Single(x => x.Id == actualId).ProductionOrderId;

        RecycleBinService.MoveProductionOrderToTrash(orderId);

        using (var db = new AppDbContext())
        {
            Check(!db.ProductionOrders.Any(x => x.Id == orderId), "Trashed order still visible in active queries");
            var archived = db.ProductionOrders.IgnoreQueryFilters().Single(x => x.Id == orderId);
            Check(archived.IsDeleted && archived.DeletedAtUtc.HasValue, "Order was not soft-deleted");
            Check(db.ProductionActuals.Any(x => x.Id == actualId) && db.DowntimeEntries.Any(), "Production history lost");
            Check(db.RecycleBinItems.Any(x => x.EntityType == nameof(ProductionOrder) && x.EntityId == orderId.ToString()), "Recycle-bin entry missing");

            var blocked = false;
            try { db.Database.ExecuteSqlInterpolated($"DELETE FROM ProductionOrders WHERE Id = {orderId}"); }
            catch (SqliteException) { blocked = true; }
            Check(blocked, "Database allowed hard deletion of a production order");
        }

        SessionService.SignIn(new UserAccount { Id = 999, Username = "audit-admin", Role = UserRoles.Administrator, IsActive = true });
        long recycleId;
        using (var db = new AppDbContext())
            recycleId = db.RecycleBinItems.Where(x => x.EntityType == nameof(ProductionOrder) && x.EntityId == orderId.ToString())
                .OrderByDescending(x => x.Id).Select(x => x.Id).First();
        RecycleBinService.Restore(recycleId);
        using var restored = new AppDbContext();
        Check(restored.ProductionOrders.Any(x => x.Id == orderId), "Recycle-bin restore did not reactivate the order");
    }

    private static void DowntimeEdit()
    {
        Planner();
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
        Planner();
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

    private static void LegacyCompanyIdentityMigration()
    {
        using (var db = new AppDbContext())
        {
            var (hash, salt) = PasswordService.HashPassword("LegacyTest123");
            db.UserAccounts.Add(new UserAccount
            {
                Username = "legacy-admin",
                DisplayName = "Legacy Admin",
                Role = UserRoles.Administrator,
                IsActive = true,
                PasswordHash = hash,
                PasswordSalt = salt,
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var migrated = CompanyIdentityService.EnsureExistingInstallationIdentity();
        Check(Guid.TryParseExact(migrated.CompanyId, "N", out _),
            "Legacy installation did not receive a stable company id");
        Check(!string.IsNullOrWhiteSpace(migrated.CompanyCode),
            "Legacy installation did not receive a company code");
        Check(migrated.CompanyRegistrationMode == CompanyIdentityService.LegacyMigrationMode,
            "Legacy installation migration mode was not recorded");

        var second = CompanyIdentityService.EnsureExistingInstallationIdentity();
        Check(second.CompanyId == migrated.CompanyId && second.CompanyCode == migrated.CompanyCode,
            "Legacy company identity changed on repeated startup");
    }

    private static void BackupTenantIsolation()
    {
        var registered = CompanyIdentityService.RegisterLocalCompany("Firma Alpha AG", "ALPHA");
        Check(registered.Success, "Could not register backup test company");
        var alpha = AppSettingsService.Load();
        var backup = BackupService.CreateBackup(
            Path.Combine(AppPaths.BackupsDirectory, "tenant-alpha.kpibackup"), alpha);

        var betaId = Guid.NewGuid().ToString("N");
        AppSettingsService.Update(settings =>
        {
            settings.CompanyId = betaId;
            settings.CompanyCode = "BETA";
            settings.CompanyName = "Firma Beta AG";
            settings.CompanyRegistrationMode = CompanyIdentityService.SellerCloudMode;
            settings.CompanyRegisteredAtUtc = DateTime.UtcNow;
        });

        var blocked = false;
        try
        {
            BackupService.RestoreBackup(backup);
        }
        catch (InvalidDataException ex)
        {
            blocked = ex.Message.Contains("anderen Firma", StringComparison.OrdinalIgnoreCase);
        }

        Check(blocked, "Cross-company backup restore was not blocked");
        var current = AppSettingsService.Load();
        Check(current.CompanyId == betaId && current.CompanyCode == "BETA",
            "Blocked cross-company restore changed current company identity");
    }

    private static void WorkTimeStatusTimeline()
    {
        int employeeId;
        UserAccount user;
        using (var db = new AppDbContext())
        {
            var employee = db.Employees.First(x => x.IsActive);
            employeeId = employee.Id;
            var (hash, salt) = PasswordService.HashPassword("Temporary123");
            user = new UserAccount
            {
                EmployeeId = employeeId,
                Username = "worktime-user",
                DisplayName = $"{employee.FirstName} {employee.LastName}",
                Role = UserRoles.Observer,
                IsActive = true,
                PasswordHash = hash,
                PasswordSalt = salt,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.UserAccounts.Add(user);
            db.SaveChanges();
        }

        SessionService.SignIn(user);
        var vm = new WorkTimeCalendarViewModel();
        Check(vm.CanLogTime, "Mapped user could not enter work time");
        vm.EntryDate = new DateTime(2035, 2, 12);
        vm.StartTimeText = "08:00";
        vm.EndTimeText = "09:00";
        vm.BreakMinutes = 0;
        vm.EntryStatus = "";
        vm.SaveEntryCommand.Execute(null);
        Check(vm.StatusMessage.Contains("Pflichtfeld"), "Missing status was not rejected");

        vm.EntryStatus = "Mischen";
        vm.SelectedArticle = vm.Articles.FirstOrDefault();
        vm.SelectedOrder = vm.Orders.FirstOrDefault();
        vm.SaveEntryCommand.Execute(null);

        vm.NewEntryCommand.Execute(null);
        vm.EntryDate = new DateTime(2035, 2, 12);
        vm.StartTimeText = "09:00";
        vm.EndTimeText = "10:00";
        vm.BreakMinutes = 0;
        vm.EntryStatus = "Produzieren";
        vm.SaveEntryCommand.Execute(null);

        using var check = new AppDbContext();
        var entries = check.WorkTimeEntries
            .Where(x => x.EmployeeId == employeeId && x.Date.Date == new DateTime(2035, 2, 12))
            .AsEnumerable()
            .OrderBy(x => x.StartTime)
            .ToList();
        Check(entries.Count == 2, "Sequential work-time entries were not stored");
        Check(entries[0].Status == "Mischen" && entries[1].Status == "Produzieren", "Work-time statuses were not stored");
        Check(check.WorkTimeStatuses.Any(x => x.Name == "Mischen") && check.WorkTimeStatuses.Any(x => x.Name == "Produzieren"),
            "Free-text work-time statuses were not persisted for reuse");
        Check(entries.All(x => x.EmployeeId == employeeId), "Work-time entry was not tied to the logged-in employee");
    }

    private static void CompanyRegistrationAndInitialAdmin()
    {
        var result = AuthenticationService.CreateInitialAdministrator(
            "Muster Maschinen AG",
            "muster maschinen ag",
            "firmen-admin",
            "Firmen Admin",
            "AdminTest123");

        Check(result.Success, $"Initial company/admin setup failed: {result.Message}");

        var settings = AppSettingsService.Load();
        Check(settings.CompanyName == "Muster Maschinen AG",
            "Company name was not registered");
        Check(settings.CompanyCode == "MUSTER-MASCHINEN-AG",
            "Company code was not normalized");
        Check(Guid.TryParseExact(settings.CompanyId, "N", out _),
            "Stable company id was not generated");
        Check(settings.CompanyRegistrationMode == CompanyIdentityService.LocalRegistrationMode &&
              settings.CompanyRegisteredAtUtc.HasValue,
            "Company registration metadata is incomplete");

        using var db = new AppDbContext();
        var admin = db.UserAccounts.Single(x => x.Username == "firmen-admin");
        Check(admin.Role == UserRoles.Administrator && admin.IsActive,
            "First company administrator was not persisted");

        var second = AuthenticationService.CreateInitialAdministrator(
            "Andere Firma", "ANDERE", "zweiter-admin", "Zweiter Admin", "AdminTest123");
        Check(!second.Success, "A second initial administrator unexpectedly re-ran company setup");
    }

    private static void UserAdminCreatesUser()
    {
        UserAccount administrator;
        using (var db = new AppDbContext())
        {
            var (hash, salt) = PasswordService.HashPassword("AdminTest123");
            administrator = new UserAccount
            {
                Username = "admin-test",
                DisplayName = "Admin Test",
                Role = UserRoles.Administrator,
                IsActive = true,
                PasswordHash = hash,
                PasswordSalt = salt,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.UserAccounts.Add(administrator);
            db.SaveChanges();
        }

        SessionService.SignIn(administrator);
        var vm = new UserAdminViewModel();
        vm.NewUserCommand.Execute(null);
        vm.Username = "new-user";
        vm.DisplayName = "New User";
        vm.SelectedRole = UserRoles.Planner;
        vm.IsActive = true;
        vm.NewPassword = "Temporary123";
        vm.ConfirmNewPassword = "Temporary123";
        vm.SaveUserCommand.Execute(null);

        Check(vm.StatusMessage == "Benutzer gespeichert.",
            $"User creation did not report success: {vm.StatusMessage}");

        using var check = new AppDbContext();
        var user = check.UserAccounts.AsNoTracking().SingleOrDefault(x => x.Username == "new-user");
        Check(user is not null, "New program user was not persisted");
        Check(user!.DisplayName == "New User" && user.Role == UserRoles.Planner && user.IsActive,
            "New program user fields were not persisted correctly");
        Check(PasswordService.Verify("Temporary123", user.PasswordHash, user.PasswordSalt),
            "New program user password was not hashed/persisted correctly");
    }

    private static void UserMessagesPersistAndAcknowledge()
    {
        UserAccount sender;
        UserAccount recipient;
        using (var db = new AppDbContext())
        {
            var (senderHash, senderSalt) = PasswordService.HashPassword("SenderTest123");
            var (recipientHash, recipientSalt) = PasswordService.HashPassword("RecipientTest123");
            sender = new UserAccount
            {
                Username = "message-sender",
                DisplayName = "Sender Test",
                Role = UserRoles.Planner,
                IsActive = true,
                PasswordHash = senderHash,
                PasswordSalt = senderSalt,
                CreatedAtUtc = DateTime.UtcNow
            };
            recipient = new UserAccount
            {
                Username = "message-recipient",
                DisplayName = "Recipient Test",
                Role = UserRoles.Observer,
                IsActive = true,
                PasswordHash = recipientHash,
                PasswordSalt = recipientSalt,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.UserAccounts.AddRange(sender, recipient);
            db.SaveChanges();
        }

        SessionService.SignIn(sender);
        var message = UserMessageService.Send(recipient.Id, "Schicht-Hinweis", "Bitte Auftrag A prüfen.", "Wichtig");
        Check(message.Id > 0, "Internal hint was not persisted");
        Check(UserMessageService.GetSent().Single(x => x.Id == message.Id).StatusText == "Noch nicht bestätigt",
            "Sender did not see pending read acknowledgement");

        SessionService.SignIn(recipient);
        Check(UserMessageService.GetUnreadCount() == 1, "Recipient unread badge count is incorrect");
        var inbox = UserMessageService.GetInbox();
        var incoming = inbox.Single(x => x.Id == message.Id);
        Check(incoming.Partner == "Sender Test" && incoming.Subject == "Schicht-Hinweis" && !incoming.IsAcknowledged,
            "Recipient inbox did not preserve sender/message data");
        Check(UserMessageService.Acknowledge(message.Id), "Recipient could not acknowledge message");
        Check(UserMessageService.GetUnreadCount() == 0, "Acknowledged message remains unread");

        SessionService.SignIn(sender);
        var sent = UserMessageService.GetSent().Single(x => x.Id == message.Id);
        Check(sent.IsAcknowledged && sent.AcknowledgedAtUtc.HasValue && sent.StatusText.StartsWith("Gelesen", StringComparison.Ordinal),
            "Sender cannot see the read acknowledgement");

        using var check = new AppDbContext();
        var stored = check.UserMessages.AsNoTracking().Single(x => x.Id == message.Id);
        Check(stored.SenderDisplayNameSnapshot == "Sender Test" &&
              stored.RecipientDisplayNameSnapshot == "Recipient Test" &&
              stored.AcknowledgedAtUtc.HasValue,
            "Internal hint history was not retained after acknowledgement");
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
