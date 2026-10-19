Authentication and migration notes

This note records the minimal next steps after adding ASP.NET Core Identity + JWT to the project. It is intended for a developer running the code locally.

Migrations
- Changing FinanceDbContext to inherit from IdentityDbContext<ApplicationUser> adds Identity tables to the model. You must create and apply an EF Core migration to add the necessary schema:
  1. Install dotnet-ef if not present: dotnet tool install --global dotnet-ef
  2. From the project directory (where the .csproj lives):
	 - dotnet ef migrations add AddIdentitySchema
	 - dotnet ef database update

- Because the repository already has an InitialFinanceSchema migration, you may prefer to create a new migration that only contains Identity additions.

JWT key & configuration
- For local development a placeholder symmetric key is stored in appsettings.Development.json. Replace this value before deploying to production.
- In production store signing keys securely (Azure Key Vault, AWS Secrets Manager, environment variables, etc.) and avoid checking secrets into source control.

Testing
- Tests using an in-memory SQLite will need to ensure the Identity tables are created in the test database. Running migrations at startup (as the app already does) will create the tables when tests start. If you prefer not to run migrations automatically in tests, you can call database.Database.EnsureCreated() in the test factory.

Security reminder
- Use Identity's built-in password hashing and consider enabling email confirmation and lockout for production.
