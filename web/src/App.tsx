import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { api, ApiError } from "./api";
import ActivityPage from "./pages/ActivityPage";
import AccountsPage from "./pages/AccountsPage";
import OverviewPage from "./pages/OverviewPage";
import PotsPage from "./pages/PotsPage";
import type { Page } from "./types";

const navigation: Array<{ id: Page; label: string; icon: string }> = [
  { id: "overview", label: "Overview", icon: "◫" },
  { id: "accounts", label: "Accounts", icon: "▤" },
  { id: "pots", label: "Pots & goals", icon: "◉" },
  { id: "activity", label: "Activity", icon: "↗" },
];

function AuthScreen({ onAuthenticated }: { onAuthenticated: (token: string) => void }) {
  const [registering, setRegistering] = useState(false);
  const [userName, setUserName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    setBusy(true);
    try {
      if (registering) await api.register(userName, email, password);
      const result = await api.login(userName, password);
      onAuthenticated(result.token);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to sign in.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="auth-layout">
      <section className="auth-story">
        <a className="brand brand-light" href="/" aria-label="Northstar home">
          <span className="brand-mark">N</span>
          <span>northstar</span>
        </a>
        <div className="story-copy">
          <p className="eyebrow">A clearer view of your money</p>
          <h1>Feel good about where your money is going.</h1>
          <p className="story-text">
            Bring your accounts, plans and everyday spending into one calm, considered space.
          </p>
        </div>
        <div className="story-foot">
          <span className="story-spark">✳</span>
          <span>Thoughtful tools for your financial life.</span>
        </div>
      </section>
      <section className="auth-panel">
        <div className="auth-form-wrap">
          <p className="eyebrow">{registering ? "Get started" : "Welcome back"}</p>
          <h2>{registering ? "Create your account" : "Sign in to Northstar"}</h2>
          <p className="muted">
            {registering
              ? "A little clarity is a good place to start."
              : "Your financial picture, right where you left it."}
          </p>
          <form className="form-stack auth-form" onSubmit={submit}>
            <div className="grid gap-1.5">
              <Label htmlFor="auth-username">Username</Label>
              <Input
                id="auth-username"
                autoComplete="username"
                required
                value={userName}
                onChange={(event) => setUserName(event.target.value)}
                placeholder="Your username"
              />
            </div>
            {registering && (
              <div className="grid gap-1.5">
                <Label htmlFor="auth-email">Email address</Label>
                <Input
                  id="auth-email"
                  type="email"
                  autoComplete="email"
                  required
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  placeholder="you@example.com"
                />
              </div>
            )}
            <div className="grid gap-1.5">
              <Label htmlFor="auth-password">Password</Label>
              <Input
                id="auth-password"
                type="password"
                autoComplete={registering ? "new-password" : "current-password"}
                minLength={registering ? 6 : undefined}
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                placeholder={registering ? "At least 6 characters" : "Your password"}
              />
            </div>
            {error && (
              <Alert variant="destructive" className="notice notice-error">
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            )}
            <Button className="w-full" disabled={busy}>
              {busy ? "Please wait…" : registering ? "Create account" : "Sign in"}
              {!busy && <span aria-hidden="true">→</span>}
            </Button>
          </form>
          <p className="auth-switch">
            {registering ? "Already have an account?" : "New to Northstar?"}{" "}
            <Button
              variant="link"
              className="text-button"
              type="button"
              onClick={() => {
                setRegistering(!registering);
                setError("");
              }}
            >
              {registering ? "Sign in" : "Create an account"}
            </Button>
          </p>
          <p className="auth-disclaimer">Your account is private and secured with your password.</p>
        </div>
      </section>
    </main>
  );
}

function App() {
  const [token, setToken] = useState(() => localStorage.getItem("northstar-token"));
  const [userName, setUserName] = useState("");
  const [page, setPage] = useState<Page>("overview");
  const [authError, setAuthError] = useState("");

  useEffect(() => {
    if (!token) return;
    localStorage.setItem("northstar-token", token);
    api
      .me(token)
      .then((user) => setUserName(user.userName ?? "Your account"))
      .catch((error: unknown) => {
        if (error instanceof ApiError && error.status === 401) {
          localStorage.removeItem("northstar-token");
          setToken(null);
          setAuthError("Your session expired. Please sign in again.");
        } else {
          setAuthError(error instanceof Error ? error.message : "Could not load your profile.");
        }
      });
  }, [token]);

  const signOut = useCallback(() => {
    localStorage.removeItem("northstar-token");
    setToken(null);
    setUserName("");
    setAuthError("");
  }, []);

  if (!token) {
    return (
      <>
        {authError && (
          <Alert variant="destructive" className="session-notice">
            <AlertDescription>{authError}</AlertDescription>
          </Alert>
        )}
        <AuthScreen onAuthenticated={setToken} />
      </>
    );
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a
          className="brand"
          href="#"
          onClick={(event) => {
            event.preventDefault();
            setPage("overview");
          }}
        >
          <span className="brand-mark">N</span>
          <span>northstar</span>
        </a>
        <div className="nav-caption">YOUR FINANCES</div>
        <nav className="main-nav" aria-label="Main navigation">
          {navigation.map((item) => (
            <Button
              variant="ghost"
              type="button"
              key={item.id}
              className={`nav-item${page === item.id ? " active" : ""}`}
              onClick={() => setPage(item.id)}
              aria-current={page === item.id ? "page" : undefined}
            >
              <span className="nav-icon" aria-hidden="true">
                {item.icon}
              </span>
              {item.label}
              {item.id === "activity" && <span className="nav-arrow">↗</span>}
            </Button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="privacy-note">
            <span className="privacy-icon">✳</span>
            <div>
              <strong>Your money, your way.</strong>
              <span>Private by design</span>
            </div>
          </div>
          <Button variant="ghost" className="profile-button" onClick={signOut}>
            <span className="avatar">{userName.slice(0, 1).toUpperCase() || "N"}</span>
            <span className="profile-text">
              <strong>{userName || "Loading profile…"}</strong>
              <small>Sign out</small>
            </span>
            <span className="profile-more">⋯</span>
          </Button>
        </div>
      </aside>
      <main className="main-content">
        <header className="topbar">
          <div className="breadcrumb">
            Personal finance <span>/</span> {navigation.find((item) => item.id === page)?.label}
          </div>
          <div className="topbar-right">
            <span className="secure-dot" /> Secure, personal workspace
          </div>
        </header>
        {authError && (
          <Alert variant="destructive" className="page-alert">
            <AlertDescription>{authError}</AlertDescription>
          </Alert>
        )}
        <div className="page-content">
          {page === "overview" && <OverviewPage token={token} onNavigate={setPage} />}
          {page === "accounts" && <AccountsPage token={token} />}
          {page === "pots" && <PotsPage token={token} />}
          {page === "activity" && <ActivityPage token={token} />}
        </div>
      </main>
      <nav className="mobile-nav" aria-label="Main navigation">
        {navigation.map((item) => (
          <Button
            variant="ghost"
            type="button"
            key={item.id}
            className={page === item.id ? "active" : ""}
            onClick={() => setPage(item.id)}
          >
            <span aria-hidden="true">{item.icon}</span>
            {item.label}
          </Button>
        ))}
      </nav>
    </div>
  );
}

export default App;
