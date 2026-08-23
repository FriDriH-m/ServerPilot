import type { ServerInstanceMetrics } from "../api/server-pilot-api";
import { formatMemory, formatTimestamp, formatUptime } from "../dashboard/dashboard-model";

interface ServerMetricsProps {
  current: ServerInstanceMetrics | null;
  history: ServerInstanceMetrics[];
}

export function ServerMetrics({ current, history }: ServerMetricsProps) {
  if (!current) {
    return (
      <section className="metrics-panel" aria-labelledby="metrics-title">
        <div className="metrics-heading">
          <h3 id="metrics-title">Process metrics</h3>
          <span className="metrics-state stale">Unavailable</span>
        </div>
        <p className="empty-copy">
          Metrics appear after the Agent observes a running process.
        </p>
      </section>
    );
  }

  const cpuHistory = history.flatMap((sample) =>
    sample.cpuUsagePercent === null ? [] : [sample.cpuUsagePercent],
  );
  const memoryHistory = history.map((sample) => sample.workingSetBytes / 1024 / 1024);

  return (
    <section className="metrics-panel" aria-labelledby="metrics-title">
      <div className="metrics-heading">
        <div>
          <h3 id="metrics-title">Process metrics</h3>
          <small>Sampled {formatTimestamp(current.reportedAt)}</small>
        </div>
        <span className={`metrics-state ${current.isStale ? "stale" : "fresh"}`}>
          {current.isStale ? "Stale" : "Live"}
        </span>
      </div>

      <div className="metric-cards">
        <article>
          <span>CPU</span>
          <strong>
            {current.cpuUsagePercent === null
              ? "Collecting…"
              : `${current.cpuUsagePercent.toFixed(1)}%`}
          </strong>
          <Sparkline values={cpuHistory} maximum={100} label="Recent CPU usage" />
        </article>
        <article>
          <span>Memory</span>
          <strong>{formatMemory(current.workingSetBytes)}</strong>
          <Sparkline values={memoryHistory} label="Recent working-set memory" />
        </article>
        <article>
          <span>Uptime</span>
          <strong>{formatUptime(current.uptimeSeconds)}</strong>
          <small>Verified process lifetime</small>
        </article>
      </div>
      {current.isStale ? (
        <p className="metrics-warning">
          The last sample is retained for context; it is not current process telemetry.
        </p>
      ) : null}
    </section>
  );
}

interface SparklineProps {
  values: number[];
  maximum?: number;
  label: string;
}

function Sparkline({ values, maximum, label }: SparklineProps) {
  if (values.length < 2) {
    return <small className="sparkline-placeholder">Waiting for another sample</small>;
  }

  const upperBound = maximum ?? Math.max(...values, 1);
  const points = values
    .map((value, index) => {
      const x = (index / (values.length - 1)) * 100;
      const y = 28 - (Math.min(Math.max(value, 0), upperBound) / upperBound) * 26;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");

  return (
    <svg
      className="metric-sparkline"
      viewBox="0 0 100 30"
      preserveAspectRatio="none"
      role="img"
      aria-label={label}
    >
      <polyline points={points} />
    </svg>
  );
}
