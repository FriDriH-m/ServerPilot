import { useEffect, useState, type FormEvent } from "react";
import type {
  AgentSummary,
  CreateServerInstanceRequest,
  ServerInstanceDetails,
} from "../api/server-pilot-api";
import { ErrorAlert } from "./error-alert";

interface ServerInstanceFormProps {
  agents: AgentSummary[];
  initial?: ServerInstanceDetails;
  busy: boolean;
  error: unknown;
  onCancel(): void;
  onSubmit(request: CreateServerInstanceRequest): Promise<void>;
}

const emptyRequest: CreateServerInstanceRequest = {
  agentId: "",
  name: "",
  executablePath: "",
  arguments: "",
  workingDirectory: "",
  processName: "",
};

function toRequest(initial: ServerInstanceDetails | undefined) {
  return initial
    ? {
        agentId: initial.agentId,
        name: initial.name,
        executablePath: initial.executablePath,
        arguments: initial.arguments,
        workingDirectory: initial.workingDirectory,
        processName: initial.processName,
      }
    : { ...emptyRequest };
}

export function ServerInstanceForm({
  agents,
  initial,
  busy,
  error,
  onCancel,
  onSubmit,
}: ServerInstanceFormProps) {
  const [request, setRequest] = useState<CreateServerInstanceRequest>(() =>
    toRequest(initial),
  );

  useEffect(() => setRequest(toRequest(initial)), [initial]);

  function update<K extends keyof CreateServerInstanceRequest>(
    field: K,
    value: CreateServerInstanceRequest[K],
  ) {
    setRequest((current) => ({ ...current, [field]: value }));
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await onSubmit({
      ...request,
      name: request.name.trim(),
      executablePath: request.executablePath.trim(),
      arguments: request.arguments.trim(),
      workingDirectory: request.workingDirectory.trim(),
      processName: request.processName.trim(),
    });
  }

  return (
    <section className="editor-panel" aria-labelledby="server-editor-title">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">Server configuration</p>
          <h2 id="server-editor-title">
            {initial ? `Edit ${initial.name}` : "Add a ServerInstance"}
          </h2>
        </div>
        <button className="text-button" type="button" onClick={onCancel}>
          Close
        </button>
      </div>

      {error ? <ErrorAlert error={error} /> : null}

      <form className="server-form" onSubmit={handleSubmit}>
        <label htmlFor="server-agent">Agent</label>
        <select
          id="server-agent"
          value={request.agentId}
          onChange={(event) => update("agentId", event.target.value)}
          disabled={Boolean(initial) || busy}
          required
        >
          <option value="">Select an Agent</option>
          {agents.map((agent) => (
            <option key={agent.id} value={agent.id}>
              {agent.name} · {agent.machineName} · {agent.status}
            </option>
          ))}
        </select>

        <label htmlFor="server-name">Name</label>
        <input
          id="server-name"
          value={request.name}
          onChange={(event) => update("name", event.target.value)}
          maxLength={100}
          disabled={busy}
          required
        />

        <label htmlFor="server-executable">Executable path</label>
        <input
          id="server-executable"
          value={request.executablePath}
          onChange={(event) => update("executablePath", event.target.value)}
          placeholder="C:\\Servers\\Game\\server.exe"
          maxLength={2048}
          disabled={busy}
          required
        />

        <label htmlFor="server-arguments">Arguments</label>
        <input
          id="server-arguments"
          value={request.arguments}
          onChange={(event) => update("arguments", event.target.value)}
          placeholder="-port 27015"
          maxLength={4096}
          disabled={busy}
        />

        <label htmlFor="server-working-directory">Working directory</label>
        <input
          id="server-working-directory"
          value={request.workingDirectory}
          onChange={(event) => update("workingDirectory", event.target.value)}
          placeholder="C:\\Servers\\Game"
          maxLength={2048}
          disabled={busy}
          required
        />

        <label htmlFor="server-process-name">Process name</label>
        <input
          id="server-process-name"
          value={request.processName}
          onChange={(event) => update("processName", event.target.value)}
          placeholder="server"
          maxLength={255}
          disabled={busy}
          required
        />

        <div className="form-actions">
          <button className="secondary-button" type="button" onClick={onCancel}>
            Cancel
          </button>
          <button className="primary-button" type="submit" disabled={busy}>
            {busy ? "Saving…" : initial ? "Save changes" : "Create server"}
          </button>
        </div>
      </form>
    </section>
  );
}
