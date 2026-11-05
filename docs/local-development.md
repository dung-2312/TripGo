# Local development and testing

## Which database, and why it matters

`TripgoDb` is read-only. Everything that writes (tests, migrations, seeding, the running
application in Development) uses `TripgoDb_IsolatedTest`, and these layers enforce that:

| Layer | What it refuses |
|---|---|
| `AppDbContext` | Tracked saves to any database other than `TripgoDb_IsolatedTest` |
| `DatabaseTargetGuard` | Migrations or seeding on any other database, or on a server that is not approved |
| `AppDbContextFactory` (EF tooling) | Unapproved servers and databases |
| `Program.cs` | Starting in Development against any other database |
| `TestDatabase` (tests) | Any test connection string naming another database |

These are checks in code. They do not replace SQL Server permissions; see
[sql-server-permissions.md](sql-server-permissions.md).

## Machines without `localhost\MSSQLSERVER01`

The team default server is `localhost\MSSQLSERVER01`. On any other machine, name a local
server explicitly. The opt-in accepts only servers on the local machine, and never changes
the database name rule. LocalDB works and keeps the data away from other projects:

```bash
export TRIPGO_APPROVED_LOCAL_SQL_SERVER='(localdb)\MSSQLLocalDB'
export TRIPGO_LOCALDB_CS='Server=(localdb)\MSSQLLocalDB;Database=TripgoDb_IsolatedTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;'
export TRIPGO_EF_CONNECTION_STRING="$TRIPGO_LOCALDB_CS"
export TRIPGO_TEST_CONNECTION_STRING="$TRIPGO_LOCALDB_CS"
export ConnectionStrings__DefaultConnection="$TRIPGO_LOCALDB_CS"
export ASPNETCORE_ENVIRONMENT=Development
```

Create the database, then turn off AUTO_CLOSE, which LocalDB enables by default and which
makes the database repeatedly shut down and restart during test runs:

```bash
dotnet tool install --global dotnet-ef --version 8.0.20
dotnet ef database update --project src/Tripgo.Infrastructure --startup-project src/Tripgo.Web
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -Q "ALTER DATABASE [TripgoDb_IsolatedTest] SET AUTO_CLOSE OFF"
```

## Data in a fresh database

A freshly migrated database has no transport data, and the imported data set is not in this
repository. On first start (or first test run) `SandboxFixtureService` creates a fictional
operator, one bus and 14 routes, all labelled `[SANDBOX]`, plus rolling sandbox departures.
It does nothing if any route already exists. Nothing in it describes a real operator, fare or
timetable.

## Sign-in accounts

No password is stored in source control. To get local accounts, set these before first start:

- `TRIPGO_SANDBOX_ADMIN_PASSWORD` creates `admin@tripgo.test`
- `TRIPGO_SANDBOX_CUSTOMER_PASSWORD` creates `customer@tripgo.test`

If a database still contains `admin@tripgo.vn` or `customer@tripgo.vn` from older builds, their
passwords were in source control. Delete or re-password them.

## Running the tests

```bash
dotnet test Tripgo.sln
```

Browser journeys are skipped unless a running instance is named. They use the installed Edge,
so no browser download is needed:

```bash
export TRIPGO_E2E_BASE_URL=http://localhost:5065
dotnet test tests/Tripgo.Tests --filter "FullyQualifiedName~BrowserJourneyTests"
```

The admin journey also reads `TRIPGO_SANDBOX_ADMIN_PASSWORD`. If your dev certificate is not
trusted, the project's `http` profile (`http://localhost:5065`) works for local testing.

## Intermittent 30-second SQL timeouts

Seen on a machine with little free memory: LocalDB's memory target shrinks and queries wait for
a memory grant (`RESOURCE_SEMAPHORE` in `sys.dm_os_wait_stats`, with a maximum wait of about
30,000 ms). The queries themselves are small. Test contexts allow 120 seconds for this reason.
If you see it, free up memory before suspecting the code.
