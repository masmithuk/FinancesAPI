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
- A finance user interface; this repository covers the API.

## API implementation status

The current development project is an ASP.NET Core Minimal API targeting .NET 10. It is still the generated sample scaffold: finance endpoints, authentication, and persistence have not yet been implemented. The persistence provider and detailed API contract are implementation decisions to settle in the foundation work.

The repository also retains the earlier API implementation in `FinancesAPI/`. The current solution and development project are `Finance API.slnx` and `Finance API/Finance API.csproj`.

### Run the current scaffold

```powershell
dotnet run --project "Finance API/Finance API.csproj"
```

The sample OpenAPI document is available in Development. Replace the generated sample endpoint as finance API work is implemented.

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
