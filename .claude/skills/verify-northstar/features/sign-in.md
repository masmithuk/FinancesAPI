# Sign in

A user signs in with their username and password to reach their own plan. A wrong password is rejected without revealing which part was wrong, and signing out returns to the sign-in screen.

## Sub-features

- `signin-ok`: a valid sign-in opens the Overview.
- `signin-wrong-password`: a wrong password shows an error and stays on the sign-in screen.
- `signin-out`: signing out returns to the sign-in screen.

## How to get to it (user POV)

- Open `http://localhost:5274` while signed out.
- Choose the user button at the bottom of the sidebar (`verify-user`, "Click to sign out") to sign out.

## Driving it with the built-in browser

Preconditions:

- Baseline seeded state, signed out (a fresh browser tab, or sign out first).

- **Wrong password.** Fill `Your username` = `verify-user`, `Your password` = `wrong-pass1`, and choose `Sign in`. An error message appears, and the textboxes and `Sign in` button are still on the page.
- **Sign in.** Fill `Your password` = `Verify-P@ss1` and choose `Sign in`. The page shows the heading `Your payday plan.` and `Transfers to make`.
- **Sign out.** Choose the sidebar user button `verify-user`. The `Your username` textbox is back.
- **Proof.** Save the page text after each step to `.verify/artifacts/sign-in/`.

## Gotchas

- The session token is kept in the browser, so a tab used for an earlier run may already be signed in. Sign out first to drive `signin-ok`.
- Repeated failures may hit the API's sign-in rate limit. Wait a minute rather than retrying in a loop.
