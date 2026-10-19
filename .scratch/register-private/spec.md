Issue: Register securely and keep each finance plan private

Status: claimed

User story
- As a user I want to register securely and have my finance plans isolated and private so only I (and invited collaborators) can access them.

Initial plan / acceptance criteria
1. Investigate authentication and authorization options appropriate for this project (JWT, ASP.NET Core Identity, external providers).
2. Add a minimal authentication scaffold that supports user registration and per-user ownership of finance plans.
3. Ensure API endpoints enforce ownership (e.g., policies or resource checks).
4. Add tests demonstrating registration, authenticated requests, and isolation of data between users.

Current action taken
- Status set to claimed. I will start by investigating which auth approach is simplest and secure for this repo (recommendation: JWT + ASP.NET Core Identity or IdentityServer depending on needs).

Next steps (planned)
- step-1: Research & choose authentication approach — produce short recommendation.
- step-2: Add initial authentication scaffolding and sample user entity.
- step-3: Add ownership fields to a sample finance-plan entity and secure endpoints.
- step-4: Add tests for authentication and data isolation.
