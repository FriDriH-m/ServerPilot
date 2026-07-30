import type { ServerCommand, ServerInstanceDetails } from "../api/server-pilot-api";

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
