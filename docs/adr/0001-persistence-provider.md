# ADR-0001: Use EF Core with SQLite for the API foundation

- Status: Accepted
- Date: 2026-10-18

## Context

The .NET 10 API needs a relational persistence boundary and repeatable schema migrations before finance entities are introduced. No production hosting provider or managed database has been selected, and the development environment does not have Docker available. The earlier .NET Core 2.1 API referenced SQL Server but configured EF Core's in-memory provider, so it does not provide a working database choice to carry forward.

## Decision

Use Entity Framework Core with SQLite for the current API, including local development. Use a separate SQLite in-memory database connection per integration test, keeping the SQLite provider and relational behavior consistent between tests and the application.

Store the local database at `Finance API/App_Data/finance-api.db` by default. Override it with the standard `ConnectionStrings__Finance` configuration key. Do not commit database files or secrets.

Keep the database provider behind the EF Core `DbContext` boundary. Revisit this decision if the deployment target requires a server database or write concurrency beyond SQLite's operational fit.

## Consequences

- The API can run without provisioning a database service.
- EF Core migrations define the schema and are applied at application startup.
- Integration tests use real SQLite relational behavior without external services.
- SQLite-specific behavior must be covered before adopting a different production provider; changing providers may require provider-specific migration and test adjustments.
- This foundation migration intentionally creates no finance tables. Those are introduced alongside their domain stories.
