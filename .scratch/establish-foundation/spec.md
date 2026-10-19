Issue: Establish finance API foundation

Spec: Prepare the current .NET 10 Minimal API project

Status: resolved

Summary:
- Added beginner-facing documentation: docs/ProgramFiles-BeginnerGuide.md
- Added inline comments to core files to help new developers:
  - Program.cs
  - Endpoints/HealthEndpoints.cs
  - Persistence/FinanceDbContext.cs
  - Persistence/FinanceDbContextFactory.cs
  - Tests (Finance API.Tests): ApiFoundationTests.cs and FinanceApiFactory.cs
- Added test comments and XML summaries to clarify test intent and usage.

What changed (high level):
- Documentation and inline comments only; no behavioral changes to the application logic.
- Test helpers (FinanceApiFactory) documented for maintainers.

Notes:
- Migrations and generated files were left intact; they may be documented in a follow-up.
- If you want these docs included in the Solution Explorer, add them as Solution Items or include via the .csproj.
