import { useAuth } from "../auth/auth-context";
import { Link } from "../router";

export function WorkspacePage() {
  const { session, logout } = useAuth();

  if (!session) {
    return null;
  }

  return (
    <div className="workspace-shell">
      <header className="workspace-header">
        <Link className="wordmark" to="/app" aria-label="ServerPilot workspace">
          <span className="wordmark-icon">SP</span>
          <span>ServerPilot</span>
        </Link>
        <div className="account-actions">
          <span>{session.email}</span>
          <button className="secondary-button" type="button" onClick={logout}>
            Log out
          </button>
        </div>
      </header>

      <main className="workspace-content">
        <p className="eyebrow">Authenticated workspace</p>
        <h1>Your workspace is ready.</h1>
        <p className="workspace-lead">
          Authentication and protected routing are connected to the ServerPilot API.
          Agent and server management arrive in the next focused issue.
        </p>

        <section className="foundation-grid" aria-label="Web client foundation status">
          <article>
            <span className="status-dot" aria-hidden="true" />
            <h2>Session active</h2>
            <p>
              The access token stays in memory and expires at{" "}
              <time dateTime={session.expiresAt}>
                {new Date(session.expiresAt).toLocaleTimeString([], {
                  hour: "2-digit",
                  minute: "2-digit",
                })}
              </time>
              .
            </p>
          </article>
          <article>
            <span className="status-dot status-dot-muted" aria-hidden="true" />
            <h2>Management intentionally scoped out</h2>
            <p>
              This foundation stops before dashboards and command controls so issue #36
              can add them against a stable client shell.
            </p>
          </article>
        </section>
      </main>
    </div>
  );
}
