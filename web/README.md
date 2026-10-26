# Northstar web client

Northstar is a Vite+ React client for the active Finance API project.

## Run locally

Start the API from the repository root:

```powershell
dotnet run --project "Finance API\Finance API.csproj" --launch-profile http
```

In another terminal, start the client:

```powershell
cd web
npm install
npm run dev
```

Open the local URL printed by Vite+. The development server proxies `/api` requests to `http://localhost:5106`; use the API's HTTP launch profile while developing the client. HTTPS redirection is disabled in Development so the proxy can forward authenticated requests without the browser following a cross-origin redirect; HTTPS redirection remains enabled outside Development.
When calling the API directly from the Vite origin, the Development environment permits CORS from `http://localhost:5173`.

Run the formatter, linter, TypeScript check, tests, and production build with:

```powershell
npm run check
npm test
npm run build
```

## Tests

Unit tests use Vitest through Vite+ (`vp test`) and live next to the code they cover as
`*.test.ts` files. Run them once with `npm test`, or in watch mode with `npx vp test watch`.

| Module                    | What it covers                                                                                                                                                       |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/lib/statementCsv.ts` | CSV parsing (delimiters, quotes, line endings, large files), bank column detection, date format detection, signed amount parsing, rounding, and invalid row counting |
| `src/lib/planMatch.ts`    | Plan occurrence expansion, name and amount matching of transactions to plans, one claim per occurrence                                                               |
| `src/shared/format.ts`    | GBP formatting, timezone-safe date labels, and the local `today()` date                                                                                              |
| `src/lib/utils.ts`        | The `cn` class name helper                                                                                                                                           |
| `src/api.ts`              | Request headers and bodies, and friendly, non-leaking error messages for network failures, 4xx, 5xx, and malformed responses                                         |

## Libraries and utilities

- `src/lib/statementCsv.ts` parses bank statement CSVs in the browser and maps them to
  importable transactions. `parseCsv` reads the text, `detectMapping` guesses the columns from
  the header names (Monzo works unchanged), and `mapStatement` produces signed amounts and ISO
  dates, counting rows it can't read. The Activity page's import panel
  (`src/shared/StatementImport.tsx`) uses these and sends the rows to
  `POST /api/accounts/{id}/occurrences/import`.
- `src/lib/planMatch.ts` suggests which recurring plan occurrence an imported transaction pays.
  A transaction matches a same-direction plan by shared name words (partial words count, within
  half the plan's period) or by an identical amount within three days. Each plan occurrence is
  claimed once. Suggestions are shown in the import panel for the user to untick, and a final
  "Are you sure?" step confirms the import. Linked activity (`scheduledItemId` and `planDate`)
  replaces that plan occurrence in the payday plan, forecast and overview so it isn't counted
  twice. Unmatched activity can be linked or unlinked later from the Activity list
  (`PUT /api/accounts/{id}/occurrences/{occurrenceId}/plan`).
- `src/shared/format.ts` holds display helpers: `money`, `dateLabel` and `today`.
- `src/lib/utils.ts` re-exports `cn` for the shadcn/ui components.
- `src/api.ts` is the API client. Failures always surface as an `ApiError` with a message that
  is safe to show: network problems and 5xx responses never expose server internals.

## UI components

The client uses shadcn/ui-style source components with Tailwind CSS v4. Shared button,
card, alert, input, and label primitives are checked into `src/components/ui`;
selects and income toggles use native HTML controls. Add new components with the shadcn CLI:

```powershell
npx shadcn@latest add <component>
```

Compose these primitives for application-specific layouts instead of adding duplicate
button, input, alert, or card styling. Components that require an additional runtime
library should be reviewed before adding that dependency.

## API coverage

The client uses the existing register/login and authenticated endpoints for the overview, setup status, accounts and balances, pots and goals, recurring schedules, occurrences, forecasts, running balances, and reconciliation. Recorded activity and recurring plans can be edited, moved between accounts, or deleted. Editing or deleting a reconciled activity also applies balance corrections.

For production, serve the client and API on the same origin or configure a reverse proxy for `/api`.
