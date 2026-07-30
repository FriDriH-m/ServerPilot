import { useState, type FormEvent } from "react";
import { ErrorAlert } from "../components/error-alert";
import { useAuth } from "../auth/auth-context";
import { Link, navigate } from "../router";

type AuthMode = "login" | "register";

interface AuthPageProps {
  mode: AuthMode;
}

export function AuthPage({ mode }: AuthPageProps) {
  const { login, register } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const isRegistration = mode === "register";

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    const normalizedEmail = email.trim();
    if (!normalizedEmail || !password) {
      setError(new Error("Email and password are required."));
      return;
    }

    if (isRegistration && password.length < 12) {
      setError(new Error("Registration passwords must contain at least 12 characters."));
      return;
    }

    setIsSubmitting(true);
    try {
      const request = { email: normalizedEmail, password };
      if (isRegistration) {
        await register(request);
      } else {
        await login(request);
      }

      const returnTo = new URLSearchParams(window.location.search).get("returnTo");
      const safeReturnTo =
        returnTo?.startsWith("/") &&
        !returnTo.startsWith("//") &&
        !returnTo.includes("\\")
          ? returnTo
          : "/app";
      navigate(safeReturnTo, true);
    } catch (caughtError) {
      setError(caughtError);
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <main className="auth-shell">
      <section className="auth-intro" aria-labelledby="auth-heading">
        <div className="brand-mark" aria-hidden="true">
          SP
        </div>
        <p className="eyebrow">Remote process control</p>
        <h1 id="auth-heading">A calm control plane for the servers you run.</h1>
        <p>
          Connect a Windows Agent, keep process execution bounded, and operate through
          one authenticated workspace.
        </p>
        <div className="signal-row" aria-label="ServerPilot principles">
          <span>Typed commands</span>
          <span>Owner scoped</span>
          <span>Observable</span>
        </div>
      </section>

      <section className="auth-panel" aria-labelledby="form-heading">
        <div className="auth-card">
          <p className="eyebrow">ServerPilot account</p>
          <h2 id="form-heading">{isRegistration ? "Create account" : "Sign in"}</h2>
          <p className="muted">
            {isRegistration
              ? "Start with a user account. Agent setup comes next."
              : "Use the credentials registered with your ServerPilot API."}
          </p>

          {error ? <ErrorAlert error={error} /> : null}

          <form className="auth-form" onSubmit={handleSubmit} noValidate>
            <label htmlFor={`${mode}-email`}>Email</label>
            <input
              id={`${mode}-email`}
              name="email"
              type="email"
              autoComplete="email"
              maxLength={254}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              disabled={isSubmitting}
            />

            <label htmlFor={`${mode}-password`}>Password</label>
            <input
              id={`${mode}-password`}
              name="password"
              type="password"
              autoComplete={isRegistration ? "new-password" : "current-password"}
              maxLength={128}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              disabled={isSubmitting}
            />
            {isRegistration ? (
              <small className="field-hint">Use at least 12 characters.</small>
            ) : null}

            <button className="primary-button" type="submit" disabled={isSubmitting}>
              {isSubmitting
                ? "Working…"
                : isRegistration
                  ? "Create account"
                  : "Sign in"}
            </button>
          </form>

          <p className="auth-switch">
            {isRegistration ? "Already registered?" : "New to ServerPilot?"}{" "}
            <Link to={isRegistration ? "/login" : "/register"}>
              {isRegistration ? "Sign in" : "Create an account"}
            </Link>
          </p>
        </div>
      </section>
    </main>
  );
}
