# Finance API

A GBP-only personal cash-flow planner API. The V1 goal is to help a user understand which money is already committed, what needs funding, and the lowest projected uncommitted cash balance through the end of the current calendar month.

The plan belongs to one user. They can include accounts marked personal or joint, but shared household access is not part of V1.

## Product scope

- Manually entered current-account, savings-account, and credit-card balances. Each balance has an “as of” date; balances older than 30 days should be flagged.
- Virtual pots tied to one account. Monthly-spend pots are budget envelopes; savings pots have a target balance and optional target date. Pot balances come from dated manual movements.
- One-off and recurring income and expenses. Supported schedules are weekly, fortnightly, monthly, and annual. If a scheduled day does not occur in a month, use that month’s last day; dates are not automatically shifted for weekends or bank holidays.
- Actual income and expense occurrences can be marked received, paid, or skipped, with actual dates and amounts.
- Planned credit-card purchases increase card debt rather than reducing cash at purchase time. Repayments are separate expenses from a cash account.
- User-ranked pot funding, date-aware account transfer recommendations, and a Home summary that supports partially completed setup.

## Safe to Spend

Safe to Spend is the lowest projected uncommitted cash balance from today through month-end. The forecast:

- Includes income only from its expected arrival date and expenses on their due dates.
- Protects hard bills due before funding lower-priority pot gaps.
- Treats a monthly pot target as one envelope: linked planned expenses are included in that budget, not counted a second time.
- Shows negative amounts and the earliest account shortfall instead of clamping the result to zero.
- Excludes credit limits. Interest rates may be stored for reference but are not forecast in V1.

Recommended transfers are informational only. Users make transfers outside the app; a recommendation does not change account or pot balances.

## Out of scope for V1

- Bank connections, transaction imports, or transaction categorisation.
- Shared household logins, foreign currencies, and foreign-exchange conversion.
- Automatic bank transfers or automatic interest calculations.

## API implementation status

The active development project is an ASP.NET Core Minimal API targeting .NET 10. Its foundation includes EF Core with SQLite, startup-applied migrations, minimal API request validation, RFC 7807 Problem Details, a readiness endpoint, development-only OpenAPI, JWT authentication, and finance endpoints.

Implemented in this update: basic scheduling endpoints and persistence for recurring income/expenses (issue #5). These provide create/list operations for per-account schedules; occurrence generation and reconciliation are tracked in subsequent issues.

Implemented in this update: recording actual occurrences and one-time reconciliation (issue #6). This includes:

- A new Occurrence entity to represent recorded income/expense events and optional ExternalReference for idempotent imports.
- Endpoints to create and list occurrences and to reconcile an occurrence. Reconciliation applies a Balance entry exactly once and marks the occurrence reconciled.
- EF Core migrations to add the ScheduledItems and Occurrences tables; tests and HTTP requests for manual and automated verification.

Notes and usage:
- To reliably avoid duplicate recordings from an external source, provide an ExternalReference when creating occurrences; the server enforces a unique index on ExternalReference.
- Reconciliation is idempotent: repeated reconcile calls are no-ops after the first application.

Implemented in this update: dated cash-flow forecasts and Safe to Spend (issue #7).

Notes on forecasting (V1):
- The forecast aggregates account-level dated Balances, expands scheduled items into dated occurrences (weekly, fortnightly, monthly, annual) within the current month, and includes unreconciled Occurrence entries as pending movements.
- Safe to Spend is the lowest projected cumulative uncommitted cash balance from today through the end of the current month.
- Pot funding prioritisation is not implemented in V1; pot shortfalls and detailed prioritisation are planned for a follow-up issue.

Implemented in this update: Home summary, setup status, and transfer recommendations (issue #8).

Notes on Home summary (V1):
- Setup status endpoint (/api/setup) returns a checklist of missing items to finish initial setup (accounts, balances, pots when goals exist).
- Financial overview endpoint (/api/financialoverview) returns per-account available funds, identified pot shortfalls, a SafeToSpend figure (from the forecast), and simple transfer recommendations.
- Transfer recommendations are informational only and use a greedy algorithm to suggest moving funds from accounts with available balances to cover pot shortfalls.

Usage:
- GET /api/setup — returns setup checklist
- GET /api/financialoverview — returns home summary with recommendations

These endpoints are protected and require authentication.
- Run migrations locally with the dotnet-ef CLI (see the "Run the API" section). Tests use an in-memory SQLite provider and the SkipMigrations setting.

The repository also retains the earlier API implementation in `FinancesAPI/`. The current solution and development project are `Finance API.slnx` and `Finance API/Finance API.csproj`.

### Run the API

```powershell
dotnet run --project "Finance API\Finance API.csproj" --launch-profile http
```

### Run the web client

The Vite+ React client is in [`web/`](web/README.md). Start the API using its HTTP profile, then run the client from the repository root:

```powershell
cd web
npm install
npm run dev
```

The development server proxies `/api` requests to `http://localhost:5106`. The UI supports login/registration, the financial overview, and create, edit, move, and delete workflows for one-off activity and recurring plans. Editing or deleting a reconciled occurrence adds balance corrections so account totals remain accurate.

The API applies pending EF Core migrations at startup and stores its local SQLite database at `Finance API/App_Data/finance-api.db`. Override the connection string with the `ConnectionStrings__Finance` environment variable. The local `dotnet-ef` tool is pinned in `dotnet-tools.json`; add future migrations from the repository root with:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add NewMigrationName --project "Finance API\Finance API.csproj" --startup-project "Finance API\Finance API.csproj" --output-dir Persistence\Migrations
```

If you encounter "no such table" errors (for example: "no such table: Accounts" or "no such table: AspNetUsers") follow these steps to update your local database schema:

1. Ensure the EF CLI tool is available:

```powershell
dotnet tool restore
```

2. Create a migration that captures your model changes. Use a descriptive name:

```powershell
dotnet tool run dotnet-ef migrations add AddIdentityAndAccounts --project "Finance API\Finance API.csproj" --startup-project "Finance API\Finance API.csproj" --context FinanceDbContext
```

3. Apply the migration to the configured database (this updates the SQLite file used by the app):

```powershell
dotnet tool run dotnet-ef database update --project "Finance API\Finance API.csproj" --startup-project "Finance API\Finance API.csproj" --context FinanceDbContext
```

Notes:
- Use the explicit --project and --startup-project options because the project path contains spaces and to ensure the correct startup host is used.
- The migration will create the required tables so the runtime no longer throws "no such table" exceptions.
- Tests run with an in-memory database and a SkipMigrations setting that falls back to EnsureCreated(); do not rely on EnsureCreated for production.
- Alternatively, use Visual Studio's Package Manager Console and run Add-Migration / Update-Database with Default project set to "Finance API".

`GET /api/health` reports API/database readiness. In Development, the OpenAPI document is at `/openapi/v1.json`. The initial migration establishes EF Core migration tracking; domain tables are introduced alongside their API stories. Persistence and test-provider decisions are recorded in [ADR-0001](docs/adr/0001-persistence-provider.md).

### Run tests

```powershell
dotnet test "Finance API.Tests\Finance API.Tests.csproj"
```

Integration tests use an isolated SQLite in-memory database and do not require Docker or a separate database server.

## API roadmap

Implementation is tracked in [GitHub Issues](https://github.com/masmithuk/FinancesAPI/issues). The initial API stories and foundation task are:

1. [Establish the finance API foundation](https://github.com/masmithuk/FinancesAPI/issues/1)
2. [Register securely and keep each finance plan private](https://github.com/masmithuk/FinancesAPI/issues/2)
3. [Manage accounts and dated balances](https://github.com/masmithuk/FinancesAPI/issues/3)
4. [Manage virtual pots, goals, and running balances](https://github.com/masmithuk/FinancesAPI/issues/4)
5. [Schedule income and expenses with recurring occurrences](https://github.com/masmithuk/FinancesAPI/issues/5)
6. [Record actual occurrences and reconcile balances exactly once](https://github.com/masmithuk/FinancesAPI/issues/6)
7. [Calculate dated cash-flow forecasts and Safe to Spend](https://github.com/masmithuk/FinancesAPI/issues/7)
8. [Provide setup status, Home summary, and transfer recommendations](https://github.com/masmithuk/FinancesAPI/issues/8)

Each issue contains a user story (or foundation task), acceptance criteria, implementation checklist, and prerequisite issues.
