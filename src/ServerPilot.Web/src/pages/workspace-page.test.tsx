import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type {
  AgentSummary,
  AuthenticationApi,
  AuthenticationSession,
  ManagementApi,
  ServerCommand,
  ServerInstanceDetails,
} from "../api/server-pilot-api";
import { AppRoutes } from "../app";
import { AuthProvider } from "../auth/auth-context";

const agent: AgentSummary = {
  id: "agent-1",
  name: "Windows Agent",
  machineName: "GAME-HOST",
  operatingSystem: "Windows",
  version: "1.0.0",
  registeredAt: "2026-07-30T08:00:00Z",
  lastSeenAt: "2026-07-30T09:00:00Z",
  status: "Online",
};

const server: ServerInstanceDetails = {
  id: "server-1",
  agentId: agent.id,
  profile: "Generic",
  name: "Project Zomboid",
  status: "Running",
  reportedStatus: "Running",
  lastProcessId: 4242,
  lastProcessStartedAt: "2026-07-30T08:45:00Z",
  lastStatusReportedAt: "2026-07-30T09:00:00Z",
  isStateStale: false,
  createdAt: "2026-07-30T08:00:00Z",
  updatedAt: "2026-07-30T09:00:00Z",
  executablePath: "C:\\Servers\\Zomboid\\server.exe",
  arguments: "-port 16261",
  workingDirectory: "C:\\Servers\\Zomboid",
  processName: "server",
  dataDirectory: null,
  projectZomboidPaths: null,
};

function createSession(): AuthenticationSession {
  return {
    userId: "user-1",
    email: "owner@example.test",
    accessToken: "access-token",
    expiresAt: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
  };
}

function createManagementApi(
  overrides: Partial<ManagementApi> = {},
): ManagementApi {
  return {
    listAgents: vi.fn().mockResolvedValue([agent]),
    listServerInstances: vi.fn().mockResolvedValue([server]),
    getServerInstance: vi.fn().mockResolvedValue(server),
    createServerInstance: vi.fn().mockResolvedValue(server),
    updateServerInstance: vi.fn().mockResolvedValue(server),
    deleteServerInstance: vi.fn().mockResolvedValue(undefined),
    createServerCommand: vi.fn(),
    listServerCommands: vi.fn().mockResolvedValue({ items: [], nextCursor: null }),
    ...overrides,
  };
}

function createDeferred<T>() {
  let resolve: (value: T) => void;
  const promise = new Promise<T>((completion) => {
    resolve = completion;
  });

  return { promise, resolve: resolve! };
}

function createServerPage(start: number): ServerInstanceDetails[] {
  return Array.from({ length: 100 }, (_, index) => {
    const number = start + index;
    return {
      ...server,
      id: `server-${number}`,
      name: `Server ${number}`,
    };
  });
}

async function renderAuthenticated(api: ManagementApi) {
  const authenticationApi: AuthenticationApi = {
    login: vi.fn().mockResolvedValue(createSession()),
    register: vi.fn(),
  };
  window.history.replaceState(null, "", "/app");
  render(
    <AuthProvider api={authenticationApi}>
      <AppRoutes managementApi={api} />
    </AuthProvider>,
  );

  fireEvent.change(await screen.findByLabelText("Email"), {
    target: { value: "owner@example.test" },
  });
  fireEvent.change(screen.getByLabelText("Password"), {
    target: { value: "not-a-real-password" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Sign in" }));
  await screen.findByRole("heading", { name: "Operate from backend truth." });
}

async function renderAuthenticatedWithFakeTimers(api: ManagementApi) {
  const authenticationApi: AuthenticationApi = {
    login: vi.fn().mockResolvedValue(createSession()),
    register: vi.fn(),
  };
  window.history.replaceState(null, "", "/app");
  render(
    <AuthProvider api={authenticationApi}>
      <AppRoutes managementApi={api} />
    </AuthProvider>,
  );

  fireEvent.change(screen.getByLabelText("Email"), {
    target: { value: "owner@example.test" },
  });
  fireEvent.change(screen.getByLabelText("Password"), {
    target: { value: "not-a-real-password" },
  });
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));
    await Promise.resolve();
    await Promise.resolve();
  });
  await act(async () => {});
}

describe("management dashboard", () => {
  it("shows backend state and does not treat an accepted command as process success", async () => {
    const pendingStop: ServerCommand = {
      id: "command-1",
      agentId: agent.id,
      serverInstanceId: server.id,
      type: "StopServer",
      status: "Pending",
      createdAt: "2026-07-30T09:01:00Z",
      claimedAt: null,
      startedAt: null,
      completedAt: null,
      errorCode: null,
      attemptCount: 0,
      correlationId: "correlation-1",
    };
    const createServerCommand = vi.fn().mockResolvedValue(pendingStop);
    const api = createManagementApi({ createServerCommand });
    vi.spyOn(window, "confirm").mockReturnValue(true);

    await renderAuthenticated(api);

    expect(await screen.findByText("PID")).toBeInTheDocument();
    expect(screen.getByText("4242")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Start server" })).toBeDisabled();
    const stopButton = screen.getByRole("button", { name: "Stop server" });
    expect(stopButton).toBeEnabled();
    fireEvent.click(stopButton);

    expect(
      await screen.findByText(/accepted with status Pending/i),
    ).toBeInTheDocument();
    expect(screen.getAllByText("Running").length).toBeGreaterThan(0);
    expect(createServerCommand).toHaveBeenCalledWith(
      "access-token",
      server.id,
      "stop",
    );
    expect(screen.getByRole("button", { name: "Stop server" })).toBeDisabled();
  });

  it("makes offline and stale state explicit and disables process actions", async () => {
    const offlineAgent = { ...agent, status: "Offline" };
    const staleServer = {
      ...server,
      status: "Unreachable",
      reportedStatus: "Running",
      isStateStale: true,
    };
    const api = createManagementApi({
      listAgents: vi.fn().mockResolvedValue([offlineAgent]),
      listServerInstances: vi.fn().mockResolvedValue([staleServer]),
      getServerInstance: vi.fn().mockResolvedValue(staleServer),
    });

    await renderAuthenticated(api);

    expect(await screen.findByText(/effective state is stale/i)).toBeInTheDocument();
    expect(screen.getAllByText("Offline").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Start server" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Stop server" })).toBeDisabled();
  });

  it("creates a ServerInstance from the Agent-scoped form", async () => {
    const created = { ...server, status: "Unknown", reportedStatus: "Unknown" };
    const createServerInstance = vi.fn().mockResolvedValue(created);
    const api = createManagementApi({
      listServerInstances: vi.fn().mockResolvedValue([]),
      createServerInstance,
    });

    await renderAuthenticated(api);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Add server" })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: "Add server" }));
    fireEvent.change(screen.getByLabelText("Agent"), {
      target: { value: agent.id },
    });
    fireEvent.change(screen.getByLabelText("Name"), {
      target: { value: "Project Zomboid" },
    });
    fireEvent.change(screen.getByLabelText("Executable path"), {
      target: { value: server.executablePath },
    });
    fireEvent.change(screen.getByLabelText("Arguments"), {
      target: { value: server.arguments },
    });
    fireEvent.change(screen.getByLabelText("Working directory"), {
      target: { value: server.workingDirectory },
    });
    fireEvent.change(screen.getByLabelText("Process name"), {
      target: { value: server.processName },
    });
    fireEvent.click(screen.getByRole("button", { name: "Create server" }));

    await waitFor(() => expect(createServerInstance).toHaveBeenCalledOnce());
    expect(createServerInstance).toHaveBeenCalledWith(
      "access-token",
      expect.objectContaining({ agentId: agent.id, name: "Project Zomboid" }),
    );
    expect(await screen.findByText("Project Zomboid was created.")).toBeInTheDocument();
  });

  it("submits the bounded Project Zomboid profile fields", async () => {
    const created: ServerInstanceDetails = {
      ...server,
      profile: "ProjectZomboid",
      executablePath: "C:\\Servers\\ProjectZomboid\\StartServer64.bat",
      arguments: "",
      workingDirectory: "C:\\Servers\\ProjectZomboid",
      processName: "java",
      dataDirectory: "C:\\ServerPilotData\\ProjectZomboid",
    };
    const createServerInstance = vi.fn().mockResolvedValue(created);
    const api = createManagementApi({
      listServerInstances: vi.fn().mockResolvedValue([]),
      createServerInstance,
    });

    await renderAuthenticated(api);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Add server" })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: "Add server" }));
    fireEvent.change(screen.getByLabelText("Agent"), {
      target: { value: agent.id },
    });
    fireEvent.change(screen.getByLabelText("Name"), {
      target: { value: "Project Zomboid" },
    });
    fireEvent.change(screen.getByLabelText("Profile"), {
      target: { value: "ProjectZomboid" },
    });
    fireEvent.change(screen.getByLabelText("Executable path"), {
      target: { value: created.executablePath },
    });
    fireEvent.change(screen.getByLabelText("Project Zomboid data directory"), {
      target: { value: created.dataDirectory },
    });
    fireEvent.click(screen.getByRole("button", { name: "Create server" }));

    await waitFor(() => expect(createServerInstance).toHaveBeenCalledOnce());
    expect(createServerInstance).toHaveBeenCalledWith("access-token", {
      agentId: agent.id,
      profile: "ProjectZomboid",
      name: "Project Zomboid",
      executablePath: created.executablePath,
      arguments: "",
      workingDirectory: "",
      processName: "java",
      dataDirectory: created.dataDirectory,
    });
  });

  it("schedules non-overlapping refreshes within the default API rate-limit budget", async () => {
    const setIntervalSpy = vi.spyOn(window, "setInterval");
    const setTimeoutSpy = vi.spyOn(window, "setTimeout");
    const api = createManagementApi();

    try {
      await renderAuthenticated(api);
      await waitFor(() => {
        expect(api.getServerInstance).toHaveBeenCalledOnce();
        expect(api.listServerCommands).toHaveBeenCalledOnce();
      });
      await waitFor(() => {
        const scheduledDelays = setTimeoutSpy.mock.calls.map((call) => call[1]);
        expect(scheduledDelays).toContain(15_000);
        expect(scheduledDelays).toContain(10_000);
      });

      const intervalDelays = setIntervalSpy.mock.calls.map((call) => call[1]);
      expect(intervalDelays).not.toContain(5_000);
      expect(intervalDelays).not.toContain(10_000);
      expect(intervalDelays).not.toContain(15_000);
    } finally {
      setIntervalSpy.mockRestore();
      setTimeoutSpy.mockRestore();
    }
  });

  it("keeps background dashboard refreshes within the authenticated-user limit", async () => {
    vi.useFakeTimers();
    try {
      const listAgents = vi.fn().mockResolvedValue([agent]);
      const listServerInstances = vi.fn().mockResolvedValue([server]);
      const getServerInstance = vi.fn().mockResolvedValue(server);
      const listServerCommands = vi
        .fn()
        .mockResolvedValue({ items: [], nextCursor: null });
      const api = createManagementApi({
        listAgents,
        listServerInstances,
        getServerInstance,
        listServerCommands,
      });

      await renderAuthenticatedWithFakeTimers(api);
      await act(async () => {
        await vi.advanceTimersByTimeAsync(60_000);
      });

      expect(listAgents).toHaveBeenCalledTimes(5);
      expect(listServerInstances).toHaveBeenCalledTimes(5);
      expect(getServerInstance).toHaveBeenCalledTimes(7);
      expect(listServerCommands).toHaveBeenCalledTimes(7);
      expect(
        listAgents.mock.calls.length +
          listServerInstances.mock.calls.length +
          getServerInstance.mock.calls.length +
          listServerCommands.mock.calls.length,
      ).toBeLessThanOrEqual(30);
    } finally {
      vi.useRealTimers();
    }
  });

  it("loads a later server page and ignores an older page response", async () => {
    const firstPage = createServerPage(1);
    const secondPage = [{ ...server, id: "server-101", name: "Server 101" }];
    const stalePage = createDeferred<ServerInstanceDetails[]>();
    const nextPage = createDeferred<ServerInstanceDetails[]>();
    let firstPageRequestCount = 0;
    let staleRequestSignal: AbortSignal | undefined;
    const listServerInstances = vi.fn(
      (_accessToken: string, page: number, signal?: AbortSignal) => {
        if (page === 2) {
          return nextPage.promise;
        }

        firstPageRequestCount += 1;
        if (firstPageRequestCount === 1) {
          return Promise.resolve(firstPage);
        }

        staleRequestSignal = signal;
        return stalePage.promise;
      },
    );
    const api = createManagementApi({ listServerInstances });

    vi.useFakeTimers();
    try {
      await renderAuthenticatedWithFakeTimers(api);
      expect(screen.getByText("Server 1")).toBeInTheDocument();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(15_000);
      });
      expect(listServerInstances).toHaveBeenCalledTimes(2);

      fireEvent.click(
        within(
          screen.getByRole("navigation", { name: "ServerInstances pagination" }),
        ).getByRole("button", { name: "Next" }),
      );
      await act(async () => {});
      expect(listServerInstances).toHaveBeenCalledTimes(3);

      await act(async () => {
        nextPage.resolve(secondPage);
        await nextPage.promise;
      });
      expect(screen.getByText("Server 101")).toBeInTheDocument();

      await act(async () => {
        stalePage.resolve(firstPage);
        await stalePage.promise;
      });

      expect(staleRequestSignal?.aborted).toBe(true);
      expect(screen.getByText("Server 101")).toBeInTheDocument();
      expect(screen.queryByText("Server 1")).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });
});
