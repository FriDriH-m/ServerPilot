import { describe, expect, it } from "vitest";
import type {
  ServerCommand,
  ServerInstanceDetails,
  ServerInstanceMetrics,
} from "../api/server-pilot-api";
import {
  appendMetricSample,
  formatMemory,
  formatUptime,
  getCommandAvailability,
} from "./dashboard-model";

const server: ServerInstanceDetails = {
  id: "server-1",
  agentId: "agent-1",
  profile: "Generic",
  name: "Game server",
  status: "Stopped",
  reportedStatus: "Stopped",
  lastProcessId: null,
  lastProcessStartedAt: null,
  lastStatusReportedAt: "2026-07-30T09:00:00Z",
  isStateStale: false,
  createdAt: "2026-07-30T08:00:00Z",
  updatedAt: "2026-07-30T09:00:00Z",
  executablePath: "C:\\Servers\\Game\\server.exe",
  arguments: "",
  workingDirectory: "C:\\Servers\\Game",
  processName: "server",
  dataDirectory: null,
  projectZomboidPaths: null,
  metrics: null,
};

const activeCommand: ServerCommand = {
  id: "command-1",
  agentId: "agent-1",
  serverInstanceId: "server-1",
  type: "StartServer",
  status: "Pending",
  createdAt: "2026-07-30T09:01:00Z",
  claimedAt: null,
  startedAt: null,
  completedAt: null,
  errorCode: null,
  attemptCount: 0,
  correlationId: "correlation-1",
};

describe("command availability", () => {
  it("allows only Start for a fresh stopped server on an online Agent", () => {
    expect(getCommandAvailability(server, "Online", undefined)).toEqual({
      canStart: true,
      canStop: false,
    });
  });

  it("blocks commands when Agent state is offline or process state is stale", () => {
    expect(getCommandAvailability(server, "Offline", undefined)).toEqual(
      expect.objectContaining({ canStart: false, canStop: false }),
    );
    expect(
      getCommandAvailability({ ...server, isStateStale: true }, "Online", undefined),
    ).toEqual(expect.objectContaining({ canStart: false, canStop: false }));
  });

  it("blocks conflicting actions while the latest command is active", () => {
    expect(getCommandAvailability(server, "Online", activeCommand)).toEqual(
      expect.objectContaining({
        canStart: false,
        canStop: false,
        reason: expect.stringContaining("already pending"),
      }),
    );
  });

  it("allows only Stop while the backend reports Running", () => {
    expect(
      getCommandAvailability({ ...server, status: "Running" }, "Online", undefined),
    ).toEqual({ canStart: false, canStop: true });
  });
});

describe("process metrics", () => {
  it("keeps bounded ordered history and clears it when metrics become stale", () => {
    const samples = Array.from({ length: 35 }, (_, index): ServerInstanceMetrics => ({
      cpuUsagePercent: index,
      workingSetBytes: index * 1024,
      uptimeSeconds: index,
      reportedAt: new Date(Date.UTC(2026, 7, 23, 9, 0, index)).toISOString(),
      isStale: false,
    }));

    const history = samples.reduce(
      (current, sample) => appendMetricSample(current, sample),
      [] as ServerInstanceMetrics[],
    );
    const duplicate = appendMetricSample(history, samples.at(-1)!);
    const stale = appendMetricSample(duplicate, {
      ...samples[0],
      reportedAt: "2026-08-23T10:00:00.000Z",
      isStale: true,
    });

    expect(history).toHaveLength(30);
    expect(history[0].cpuUsagePercent).toBe(5);
    expect(duplicate).toHaveLength(30);
    expect(stale).toEqual([]);
  });

  it("formats memory and uptime with explicit units", () => {
    expect(formatMemory(268_435_456)).toBe("256.0 MiB");
    expect(formatUptime(900)).toBe("15m");
    expect(formatUptime(90_000)).toBe("1d 1h");
  });
});
