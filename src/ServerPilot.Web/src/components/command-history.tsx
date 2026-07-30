import type { ServerCommand } from "../api/server-pilot-api";
import { formatTimestamp } from "../dashboard/dashboard-model";
import { StatusPill } from "./status-pill";

interface CommandHistoryProps {
  commands: ServerCommand[];
  hasMore: boolean;
  loadingMore: boolean;
  onLoadMore(): Promise<void>;
}

export function CommandHistory({
  commands,
  hasMore,
  loadingMore,
  onLoadMore,
}: CommandHistoryProps) {
  return (
    <section className="history-panel" aria-labelledby="command-history-title">
      <div className="panel-heading compact-heading">
        <div>
          <p className="eyebrow">Audit trail</p>
          <h2 id="command-history-title">Command history</h2>
        </div>
        <span className="count-badge">{commands.length}</span>
      </div>

      {commands.length === 0 ? (
        <p className="empty-copy">No commands have been created for this server.</p>
      ) : (
        <div className="history-list">
          {commands.map((command) => (
            <article className="history-item" key={command.id}>
              <div>
                <strong>{command.type}</strong>
                <span>{formatTimestamp(command.createdAt)}</span>
              </div>
              <StatusPill status={command.status} />
              <dl>
                <div>
                  <dt>Attempts</dt>
                  <dd>{command.attemptCount}</dd>
                </div>
                <div>
                  <dt>Correlation</dt>
                  <dd title={command.correlationId}>
                    {command.correlationId.slice(0, 8)}
                  </dd>
                </div>
                {command.errorCode ? (
                  <div>
                    <dt>Failure code</dt>
                    <dd>{command.errorCode}</dd>
                  </div>
                ) : null}
              </dl>
            </article>
          ))}
        </div>
      )}

      {hasMore ? (
        <button
          className="secondary-button load-more-button"
          type="button"
          disabled={loadingMore}
          onClick={() => void onLoadMore()}
        >
          {loadingMore ? "Loading…" : "Load older commands"}
        </button>
      ) : null}
    </section>
  );
}
