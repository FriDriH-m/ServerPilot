import { useMemo, useState } from "react";
import type { ServerLogBuffer } from "../dashboard/dashboard-model";
import { formatTimestamp } from "../dashboard/dashboard-model";

interface ServerLogViewerProps {
  logs: ServerLogBuffer | null;
  paused: boolean;
  stateStale: boolean;
  onPausedChange(paused: boolean): void;
}

const statusCopy = {
  Unsupported: "This server profile has no configured log source.",
  Waiting: "Waiting for the Agent to inspect the configured log.",
  Missing: "The configured console log does not exist yet.",
  Unavailable: "The Agent could not safely read the configured console log.",
  Available: "Following the bounded recent console output.",
} as const;

export function ServerLogViewer({
  logs,
  paused,
  stateStale,
  onPausedChange,
}: ServerLogViewerProps) {
  const [filter, setFilter] = useState("");
  const visibleLines = useMemo(() => {
    const normalized = filter.trim().toLocaleLowerCase();
    if (!normalized) {
      return logs?.lines ?? [];
    }

    return (logs?.lines ?? []).filter((line) =>
      line.toLocaleLowerCase().includes(normalized),
    );
  }, [filter, logs?.lines]);
  const status = logs?.status ?? "Waiting";
  const stale = stateStale || logs?.isStale === true;

  return (
    <section className="server-log-viewer" aria-labelledby="server-log-title">
      <div className="log-toolbar">
        <div>
          <p className="eyebrow">Bounded local output</p>
          <h3 id="server-log-title">Recent server log</h3>
        </div>
        <button
          className="secondary-button compact-button"
          type="button"
          disabled={status === "Unsupported"}
          onClick={() => onPausedChange(!paused)}
        >
          {paused ? "Resume" : "Pause"}
        </button>
      </div>

      <div className="log-status-row">
        <span className={`log-status log-status-${status.toLocaleLowerCase()}`}>
          {status}
        </span>
        <span>{statusCopy[status]}</span>
        <span>
          Last checked: {formatTimestamp(logs?.reportedAt ?? null)}
        </span>
      </div>

      {paused ? (
        <p className="action-hint">Updates are paused in this browser tab.</p>
      ) : null}
      {stale && status !== "Unsupported" ? (
        <div className="stale-banner">
          Log output is stale because the Agent or its latest log report is offline.
        </div>
      ) : null}

      <label className="log-filter">
        Filter visible lines
        <input
          type="search"
          value={filter}
          placeholder="Search recent output"
          onChange={(event) => setFilter(event.target.value)}
        />
      </label>

      <pre className="log-output" aria-live={paused ? "off" : "polite"}>
        {visibleLines.length > 0
          ? visibleLines.join("\n")
          : status === "Available"
            ? "No complete log lines are available yet."
            : statusCopy[status]}
      </pre>
      <p className="log-limit-copy">
        At most 400 recent lines are kept. Filtering is local to this tab.
      </p>
    </section>
  );
}
