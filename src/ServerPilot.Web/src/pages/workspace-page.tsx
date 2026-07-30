import { useCallback, useEffect, useMemo, useState } from "react";
import {
  serverPilotApi,
  type AgentSummary,
  type CreateServerInstanceRequest,
  type ManagementApi,
  type ServerCommand,
  type ServerCommandAction,
  type ServerInstanceDetails,
  type ServerInstanceSummary,
  type UpdateServerInstanceRequest,
} from "../api/server-pilot-api";
import { useAuth } from "../auth/auth-context";
import { CommandHistory } from "../components/command-history";
import { ErrorAlert } from "../components/error-alert";
import { ServerInstanceForm } from "../components/server-instance-form";
import { StatusPill } from "../components/status-pill";
import {
  formatTimestamp,
  getCommandAvailability,
  isActiveCommand,
} from "../dashboard/dashboard-model";
import { Link } from "../router";

interface WorkspacePageProps {
  api?: ManagementApi;
}

type FormMode = "create" | "edit" | null;

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError";
}

function mergeCommands(
  current: ServerCommand[],
  additional: ServerCommand[],
): ServerCommand[] {
  const byId = new Map(current.map((command) => [command.id, command]));
  for (const command of additional) {
    byId.set(command.id, command);
  }
  return [...byId.values()].sort(
    (left, right) => Date.parse(right.createdAt) - Date.parse(left.createdAt),
  );
}

export function WorkspacePage({ api = serverPilotApi }: WorkspacePageProps) {
  const { session, logout } = useAuth();
  const [agents, setAgents] = useState<AgentSummary[]>([]);
  const [servers, setServers] = useState<ServerInstanceSummary[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null);
  const [selectedServer, setSelectedServer] =
    useState<ServerInstanceDetails | null>(null);
  const [commands, setCommands] = useState<ServerCommand[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [initialLoading, setInitialLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [formMode, setFormMode] = useState<FormMode>(null);
  const [mutation, setMutation] = useState<string | null>(null);
  const [overviewError, setOverviewError] = useState<unknown>(null);
  const [detailError, setDetailError] = useState<unknown>(null);
  const [mutationError, setMutationError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const accessToken = session?.accessToken ?? "";

  const refreshOverview = useCallback(
    async (background = false) => {
      if (!accessToken) {
        return;
      }

      if (!background) {
        setInitialLoading(true);
      }
      try {
        const [agentItems, serverItems] = await Promise.all([
          api.listAgents(accessToken),
          api.listServerInstances(accessToken),
        ]);
        setAgents(agentItems);
        setServers(serverItems);
        setSelectedServerId((current) =>
          current && serverItems.some((server) => server.id === current)
            ? current
            : (serverItems[0]?.id ?? null),
        );
        setOverviewError(null);
      } catch (error) {
        if (!isAbortError(error)) {
          setOverviewError(error);
        }
      } finally {
        if (!background) {
          setInitialLoading(false);
        }
      }
    },
    [accessToken, api],
  );

  useEffect(() => {
    void refreshOverview();
    const timer = window.setInterval(() => void refreshOverview(true), 10_000);
    return () => window.clearInterval(timer);
  }, [refreshOverview]);

  useEffect(() => {
    if (!accessToken || !selectedServerId) {
      setSelectedServer(null);
      setCommands([]);
      setNextCursor(null);
      return undefined;
    }

    const controller = new AbortController();
    let disposed = false;
    setSelectedServer(null);
    setCommands([]);
    setNextCursor(null);
    setDetailError(null);

    async function loadSelection() {
      setDetailLoading(true);
      try {
        const [server, history] = await Promise.all([
          api.getServerInstance(accessToken, selectedServerId!, controller.signal),
          api.listServerCommands(
            accessToken,
            selectedServerId!,
            undefined,
            controller.signal,
          ),
        ]);
        if (!disposed) {
          setSelectedServer(server);
          setCommands(history.items);
          setNextCursor(history.nextCursor);
          setDetailError(null);
        }
      } catch (error) {
        if (!disposed && !isAbortError(error)) {
          setDetailError(error);
        }
      } finally {
        if (!disposed) {
          setDetailLoading(false);
        }
      }
    }

    async function refreshState() {
      try {
        const [server, history] = await Promise.all([
          api.getServerInstance(
            accessToken,
            selectedServerId!,
            controller.signal,
          ),
          api.listServerCommands(
            accessToken,
            selectedServerId!,
            undefined,
            controller.signal,
          ),
        ]);
        if (!disposed) {
          setSelectedServer(server);
          setCommands((current) => mergeCommands(current, history.items));
          setDetailError(null);
        }
      } catch (error) {
        if (!disposed && !isAbortError(error)) {
          setDetailError(error);
        }
      }
    }

    void loadSelection();
    const timer = window.setInterval(() => void refreshState(), 5_000);
    return () => {
      disposed = true;
      controller.abort();
      window.clearInterval(timer);
    };
  }, [accessToken, api, selectedServerId]);

  const selectedAgent = useMemo(
    () => agents.find((agent) => agent.id === selectedServer?.agentId),
    [agents, selectedServer?.agentId],
  );
  const latestCommand = commands[0];
  const commandAvailability = selectedServer
    ? getCommandAvailability(selectedServer, selectedAgent?.status, latestCommand)
    : { canStart: false, canStop: false, reason: "Select a server." };
  const serverIsActive = selectedServer
    ? ["Starting", "Running", "Stopping"].includes(selectedServer.status) ||
      isActiveCommand(latestCommand)
    : false;

  if (!session) {
    return null;
  }

  async function submitServer(request: CreateServerInstanceRequest) {
    setMutation("server-form");
    setMutationError(null);
    setNotice(null);
    try {
      let saved: ServerInstanceDetails;
      if (formMode === "edit" && selectedServer) {
        const update: UpdateServerInstanceRequest = {
          name: request.name,
          executablePath: request.executablePath,
          arguments: request.arguments,
          workingDirectory: request.workingDirectory,
          processName: request.processName,
        };
        saved = await api.updateServerInstance(
          accessToken,
          selectedServer.id,
          update,
        );
        setNotice(`${saved.name} was updated from the API response.`);
      } else {
        saved = await api.createServerInstance(accessToken, request);
        setNotice(`${saved.name} was created.`);
      }

      setSelectedServer(saved);
      setSelectedServerId(saved.id);
      setFormMode(null);
      await refreshOverview(true);
    } catch (error) {
      setMutationError(error);
    } finally {
      setMutation(null);
    }
  }

  async function deleteServer() {
    if (
      !selectedServer ||
      !window.confirm(`Delete ${selectedServer.name}? This cannot be undone.`)
    ) {
      return;
    }

    setMutation("delete");
    setMutationError(null);
    try {
      await api.deleteServerInstance(accessToken, selectedServer.id);
      setNotice(`${selectedServer.name} was deleted.`);
      setSelectedServer(null);
      setSelectedServerId(null);
      setCommands([]);
      setFormMode(null);
      await refreshOverview(true);
    } catch (error) {
      setMutationError(error);
    } finally {
      setMutation(null);
    }
  }

  async function createCommand(action: ServerCommandAction) {
    if (!selectedServer) {
      return;
    }

    const label = action === "start" ? "Start" : "Stop";
    if (!window.confirm(`${label} ${selectedServer.name}?`)) {
      return;
    }

    setMutation(action);
    setMutationError(null);
    setNotice(null);
    try {
      const command = await api.createServerCommand(
        accessToken,
        selectedServer.id,
        action,
      );
      setCommands((current) => mergeCommands(current, [command]));
      setNotice(
        `${command.type} was accepted with status ${command.status}. ` +
          "The displayed process state will change only after the Agent reports it.",
      );
      await refreshOverview(true);
    } catch (error) {
      setMutationError(error);
    } finally {
      setMutation(null);
    }
  }

  async function loadMoreCommands() {
    if (!selectedServerId || !nextCursor) {
      return;
    }

    setLoadingMore(true);
    setDetailError(null);
    try {
      const page = await api.listServerCommands(
        accessToken,
        selectedServerId,
        nextCursor,
      );
      setCommands((current) => mergeCommands(current, page.items));
      setNextCursor(page.nextCursor);
    } catch (error) {
      setDetailError(error);
    } finally {
      setLoadingMore(false);
    }
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

      <main className="dashboard-shell">
        <section className="dashboard-intro">
          <div>
            <p className="eyebrow">Management dashboard</p>
            <h1>Operate from backend truth.</h1>
            <p>
              Agents, process state and command results refresh automatically. A queued
              command is never presented as a completed process action.
            </p>
          </div>
          <button
            className="primary-button"
            type="button"
            disabled={agents.length === 0}
            onClick={() => {
              setFormMode("create");
              setMutationError(null);
            }}
          >
            Add server
          </button>
        </section>

        {overviewError ? <ErrorAlert error={overviewError} /> : null}
        {mutationError && !formMode ? <ErrorAlert error={mutationError} /> : null}
        {notice ? <div className="notice-banner" role="status">{notice}</div> : null}

        {formMode ? (
          <ServerInstanceForm
            agents={agents}
            initial={formMode === "edit" ? selectedServer ?? undefined : undefined}
            busy={mutation === "server-form"}
            error={mutationError}
            onCancel={() => {
              setFormMode(null);
              setMutationError(null);
            }}
            onSubmit={submitServer}
          />
        ) : null}

        <div className="dashboard-grid">
          <section className="dashboard-panel agent-panel" aria-labelledby="agents-title">
            <div className="panel-heading compact-heading">
              <div>
                <p className="eyebrow">Machines</p>
                <h2 id="agents-title">Agents</h2>
              </div>
              <span className="count-badge">{agents.length}</span>
            </div>
            {initialLoading ? (
              <p className="empty-copy">Loading Agents…</p>
            ) : agents.length === 0 ? (
              <p className="empty-copy">
                No Agents are registered yet. Register the Windows Agent before adding
                a server.
              </p>
            ) : (
              <div className="agent-list">
                {agents.map((agent) => (
                  <article key={agent.id}>
                    <div>
                      <strong>{agent.name}</strong>
                      <span>{agent.machineName}</span>
                    </div>
                    <StatusPill status={agent.status} />
                    <small>Last seen: {formatTimestamp(agent.lastSeenAt)}</small>
                  </article>
                ))}
              </div>
            )}
          </section>

          <section className="dashboard-panel server-panel" aria-labelledby="servers-title">
            <div className="panel-heading compact-heading">
              <div>
                <p className="eyebrow">Processes</p>
                <h2 id="servers-title">Servers</h2>
              </div>
              <span className="count-badge">{servers.length}</span>
            </div>
            {initialLoading ? (
              <p className="empty-copy">Loading servers…</p>
            ) : servers.length === 0 ? (
              <p className="empty-copy">
                No ServerInstances yet. Add one after an Agent is registered.
              </p>
            ) : (
              <div className="server-list">
                {servers.map((server) => (
                  <button
                    className={server.id === selectedServerId ? "selected" : ""}
                    type="button"
                    key={server.id}
                    onClick={() => {
                      setSelectedServerId(server.id);
                      setFormMode(null);
                      setNotice(null);
                    }}
                  >
                    <span>
                      <strong>{server.name}</strong>
                      <small>
                        Reported {formatTimestamp(server.lastStatusReportedAt)}
                      </small>
                    </span>
                    <StatusPill status={server.status} stale={server.isStateStale} />
                  </button>
                ))}
              </div>
            )}
          </section>

          <section className="dashboard-panel detail-panel" aria-labelledby="server-detail-title">
            {detailLoading && !selectedServer ? (
              <p className="empty-copy">Loading server details…</p>
            ) : detailError ? (
              <ErrorAlert error={detailError} />
            ) : !selectedServer ? (
              <p className="empty-copy">Select a server to manage it.</p>
            ) : (
              <>
                <div className="panel-heading">
                  <div>
                    <p className="eyebrow">Selected server</p>
                    <h2 id="server-detail-title">{selectedServer.name}</h2>
                  </div>
                  <StatusPill
                    status={selectedServer.status}
                    stale={selectedServer.isStateStale}
                  />
                </div>

                {selectedServer.isStateStale ? (
                  <div className="stale-banner">
                    The effective state is stale. Last Agent report: {" "}
                    {formatTimestamp(selectedServer.lastStatusReportedAt)}; reported
                    state: {selectedServer.reportedStatus}.
                  </div>
                ) : null}

                <dl className="server-facts">
                  <div>
                    <dt>Agent</dt>
                    <dd>{selectedAgent?.name ?? "Unknown Agent"}</dd>
                  </div>
                  <div>
                    <dt>Process</dt>
                    <dd>{selectedServer.processName}</dd>
                  </div>
                  <div>
                    <dt>PID</dt>
                    <dd>{selectedServer.lastProcessId ?? "—"}</dd>
                  </div>
                  <div>
                    <dt>State reported</dt>
                    <dd>{formatTimestamp(selectedServer.lastStatusReportedAt)}</dd>
                  </div>
                  <div className="wide-fact">
                    <dt>Executable</dt>
                    <dd title={selectedServer.executablePath}>
                      {selectedServer.executablePath}
                    </dd>
                  </div>
                </dl>

                <div className="command-actions">
                  <button
                    className="primary-button"
                    type="button"
                    disabled={
                      detailLoading ||
                      !commandAvailability.canStart ||
                      mutation !== null
                    }
                    onClick={() => void createCommand("start")}
                  >
                    {mutation === "start" ? "Queueing…" : "Start server"}
                  </button>
                  <button
                    className="danger-button"
                    type="button"
                    disabled={
                      detailLoading ||
                      !commandAvailability.canStop ||
                      mutation !== null
                    }
                    onClick={() => void createCommand("stop")}
                  >
                    {mutation === "stop" ? "Queueing…" : "Stop server"}
                  </button>
                </div>
                {commandAvailability.reason ? (
                  <p className="action-hint">{commandAvailability.reason}</p>
                ) : null}

                {latestCommand ? (
                  <section className="latest-command" aria-label="Latest command result">
                    <div>
                      <span>Latest command</span>
                      <strong>{latestCommand.type}</strong>
                    </div>
                    <StatusPill status={latestCommand.status} />
                    <p>
                      Created {formatTimestamp(latestCommand.createdAt)}
                      {latestCommand.errorCode
                        ? ` · failure code ${latestCommand.errorCode}`
                        : ""}
                    </p>
                  </section>
                ) : null}

                <div className="management-actions">
                  <button
                    className="secondary-button"
                    type="button"
                    disabled={detailLoading || serverIsActive || mutation !== null}
                    onClick={() => {
                      setFormMode("edit");
                      setMutationError(null);
                    }}
                  >
                    Edit configuration
                  </button>
                  <button
                    className="text-button danger-text"
                    type="button"
                    disabled={detailLoading || serverIsActive || mutation !== null}
                    onClick={() => void deleteServer()}
                  >
                    {mutation === "delete" ? "Deleting…" : "Delete server"}
                  </button>
                </div>

                <CommandHistory
                  commands={commands}
                  hasMore={nextCursor !== null}
                  loadingMore={loadingMore}
                  onLoadMore={loadMoreCommands}
                />
              </>
            )}
          </section>
        </div>
      </main>
    </div>
  );
}
