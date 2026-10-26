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

Run the formatter, linter, TypeScript check, and production build with:

```powershell
npm run check
npm run build
```

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
