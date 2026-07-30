interface StatusPillProps {
  status: string;
  stale?: boolean;
}

function statusTone(status: string): string {
  switch (status.toLowerCase()) {
    case "online":
    case "running":
    case "completed":
      return "positive";
    case "starting":
    case "stopping":
    case "pending":
    case "claimed":
      return "progress";
    case "offline":
    case "unreachable":
    case "unknown":
      return "muted";
    case "crashed":
    case "failed":
    case "cancelled":
    case "timedout":
      return "danger";
    default:
      return "neutral";
  }
}

export function StatusPill({ status, stale = false }: StatusPillProps) {
  return (
    <span className={`status-pill status-${statusTone(status)}`}>
      {status}
      {stale ? " · stale" : ""}
    </span>
  );
}
