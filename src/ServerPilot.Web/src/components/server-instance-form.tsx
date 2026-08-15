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
  profile: "Generic",
  name: "",
  executablePath: "",
  arguments: "",
  workingDirectory: "",
  processName: "",
  dataDirectory: null,
};

function toRequest(initial: ServerInstanceDetails | undefined) {
  return initial
    ? {
        agentId: initial.agentId,
        profile: initial.profile,
        name: initial.name,
        executablePath: initial.executablePath,
        arguments: initial.arguments,
        workingDirectory: initial.workingDirectory,
        processName: initial.processName,
        dataDirectory: initial.dataDirectory,
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

  function updateProfile(profile: CreateServerInstanceRequest["profile"]) {
    setRequest((current) => ({
      ...current,
      profile,
      arguments: profile === "ProjectZomboid" ? "" : current.arguments,
      workingDirectory:
        profile === "ProjectZomboid" ? "" : current.workingDirectory,
      processName: profile === "ProjectZomboid" ? "java" : "",
      dataDirectory: profile === "ProjectZomboid" ? current.dataDirectory : null,
    }));
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
      dataDirectory: request.dataDirectory?.trim() || null,
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

        <label htmlFor="server-profile">Profile</label>
        <select
          id="server-profile"
          value={request.profile}
          onChange={(event) =>
            updateProfile(
              event.target.value as CreateServerInstanceRequest["profile"],
            )
          }
          disabled={busy}
        >
          <option value="Generic">Generic executable</option>
          <option value="ProjectZomboid">Project Zomboid</option>
        </select>

        <label htmlFor="server-executable">Executable path</label>
        <input
          id="server-executable"
          value={request.executablePath}
          onChange={(event) => update("executablePath", event.target.value)}
          placeholder={
            request.profile === "ProjectZomboid"
              ? "C:\\Servers\\ProjectZomboid\\StartServer64.bat"
              : "C:\\Servers\\Game\\server.exe"
          }
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
          disabled={busy || request.profile === "ProjectZomboid"}
        />

        <label htmlFor="server-working-directory">Working directory</label>
        <input
          id="server-working-directory"
          value={request.workingDirectory}
          onChange={(event) => update("workingDirectory", event.target.value)}
          placeholder="C:\\Servers\\Game"
          maxLength={2048}
          disabled={busy || request.profile === "ProjectZomboid"}
          required={request.profile === "Generic"}
        />

        <label htmlFor="server-process-name">Process name</label>
        <input
          id="server-process-name"
          value={request.processName}
          onChange={(event) => update("processName", event.target.value)}
          placeholder="server"
          maxLength={255}
          disabled={busy || request.profile === "ProjectZomboid"}
          required={request.profile === "Generic"}
        />

        {request.profile === "ProjectZomboid" ? (
          <>
            <label htmlFor="server-data-directory">
              Project Zomboid data directory
            </label>
            <input
              id="server-data-directory"
              value={request.dataDirectory ?? ""}
              onChange={(event) => update("dataDirectory", event.target.value)}
              placeholder="C:\\ServerPilotData\\ProjectZomboid"
              maxLength={2048}
              disabled={busy}
              required
            />
            <p className="field-hint">
              The canonical servertest configuration must already exist under
              Server\\servertest.ini. ServerPilot derives the working directory
              and manages the bundled java process.
            </p>
          </>
        ) : null}

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
