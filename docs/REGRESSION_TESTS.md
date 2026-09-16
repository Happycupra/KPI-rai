# Regression checks

Run on Windows with the .NET 8 SDK:

```powershell
dotnet run --project tests/Produktionsplanung.RegressionTests --configuration Release
```

The dependency-free test executable uses a fresh temporary SQLite database for
each scenario. It never opens the user's production database. A non-zero exit
code fails both the Windows build and release workflows.

Coverage includes SQLite query execution, WPF view construction, null shifts,
CSV export, workstation deletion guards, database-level production history
protection, downtime consistency, Sunday-night absence handling, OEE unit
invariance, distinct same-name workstations, masked password input and restore
session invalidation. Interactive installer testing is still a manual check.

## OEE aggregation

For a period containing different products or units, quantities cannot be added
to derive a common performance or quality ratio. Each record is converted to
ideal production minutes using its own ideal rate:

- Ideal minutes = total quantity / ideal hourly rate × 60.
- Good ideal minutes = good quantity / ideal hourly rate × 60.
- Availability = sum(run minutes) / sum(planned minutes).
- Performance = sum(ideal minutes) / sum(run minutes).
- Quality = sum(good ideal minutes) / sum(ideal minutes).
- OEE = availability × performance × quality.

Ratios retain the existing 0–100% bounds; zero denominators give zero. Aggregate
quality is therefore weighted by ideal production time. Quantities are displayed
separately per unit. Workstations are grouped by ID, not by display name.

## Data compatibility

No existing rows are deleted or rewritten. Startup adds an idempotent SQLite
trigger protecting production orders with actual records, including databases
created by older builds. Orders without actual records require confirmation to
delete. A successful restore invalidates the old session and closes the app;
the next start applies schema updates and requires a new login.

The first Windows run also exposed an invalid DataGrid RowHeight value and a
pooled SQLite snapshot handle preventing ZIP creation. Both are covered by the
view-construction and backup round-trip tests. Temporary backup connections now
disable pooling so Windows file handles are closed before ZIP/copy operations.
