import type {
  ServerCommand,
  ServerInstanceDetails,
  ServerInstanceMetrics,
} from "../api/server-pilot-api";

const activeCommandStatuses = new Set(["Pending", "Claimed", "Running"]);
const startConflictStatuses = new Set(["Starting", "Running", "Stopping"]);

export interface CommandAvailability {
  canStart: boolean;
  canStop: boolean;
  reason?: string;
}

export function isActiveCommand(command: ServerCommand | undefined): boolean {
  return command ? activeCommandStatuses.has(command.status) : false;
}

export function getCommandAvailability(
  server: ServerInstanceDetails,
  agentStatus: string | undefined,
  latestCommand: ServerCommand | undefined,
): CommandAvailability {
  if (agentStatus !== "Online") {
    return {
      canStart: false,
      canStop: false,
      reason: "The assigned Agent is offline.",
    };
  }

  if (server.isStateStale || server.status === "Unreachable") {
    return {
      canStart: false,
      canStop: false,
      reason: "Wait for a fresh process-state report from the Agent.",
    };
  }

  if (latestCommand && isActiveCommand(latestCommand)) {
    return {
      canStart: false,
      canStop: false,
      reason: `A ${latestCommand.type} command is already ${latestCommand.status.toLowerCase()}.`,
    };
  }

  return {
    canStart: !startConflictStatuses.has(server.status),
    canStop: server.status === "Running",
  };
}

export function formatTimestamp(value: string | null): string {
  if (!value) {
    return "Never";
  }

  const timestamp = new Date(value);
  return Number.isNaN(timestamp.valueOf())
    ? "Unknown"
    : timestamp.toLocaleString([], {
        dateStyle: "medium",
        timeStyle: "short",
      });
}

export function appendMetricSample(
  current: ServerInstanceMetrics[],
  sample: ServerInstanceMetrics | null,
  limit = 30,
): ServerInstanceMetrics[] {
  if (!sample || sample.isStale || limit < 1) {
    return [];
  }

  const withoutDuplicate = current.filter(
    (item) => item.reportedAt !== sample.reportedAt,
  );
  return [...withoutDuplicate, sample]
    .sort((left, right) => Date.parse(left.reportedAt) - Date.parse(right.reportedAt))
    .slice(-limit);
}

export function formatMemory(bytes: number): string {
  return `${(Math.max(0, bytes) / 1024 / 1024).toFixed(1)} MiB`;
}

export function formatUptime(seconds: number): string {
  const totalMinutes = Math.floor(Math.max(0, seconds) / 60);
  const days = Math.floor(totalMinutes / 1_440);
  const hours = Math.floor((totalMinutes % 1_440) / 60);
  const minutes = totalMinutes % 60;
  if (days > 0) {
    return `${days}d ${hours}h`;
  }

  if (hours > 0) {
    return `${hours}h ${minutes}m`;
  }

  return `${minutes}m`;
}
