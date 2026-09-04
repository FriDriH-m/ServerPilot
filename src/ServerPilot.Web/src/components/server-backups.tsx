import { useEffect, useRef, useState } from "react";
import type { BackupHistoryPage, ManagementApi } from "../api/server-pilot-api";
import { ErrorAlert } from "./error-alert";
import { StatusPill } from "./status-pill";

interface Props {
  api: Pick<ManagementApi, "listBackups">;
  accessToken: string;
  serverId: string;
  canCreate: boolean;
  onCreate: () => void;
}

export function ServerBackups({ api, accessToken, serverId, canCreate, onCreate }: Props) {
  const [page, setPage] = useState<BackupHistoryPage | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);

  async function load(cursor?: string) {
    if (busy) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setError(null);
    try {
      const result = await api.listBackups(accessToken, serverId, cursor, controller.signal);
      if (!controller.signal.aborted) setPage(result);
    } catch (failure) {
      if (!controller.signal.aborted) setError(failure);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  return (
    <section aria-label="Local backups">
      <h3>Local backups</h3>
      <p>Project Zomboid only. Stop the server first. The Agent needs a configured local backup directory. Archives stay on the Agent; restore and download are not available.</p>
      <div className="command-actions">
        <button type="button" className="primary-button" disabled={!canCreate} onClick={onCreate}>Create local backup</button>
        <button type="button" className="secondary-button" disabled={busy} onClick={() => void load()}>Refresh backups</button>
      </div>
      {error ? <ErrorAlert error={error} /> : null}
      {!page ? <p>History is loaded on demand, not polled automatically.</p> : null}
      {page?.items.length === 0 ? <p>No local backups yet.</p> : null}
      <ul>
        {page?.items.map((backup) => (
          <li key={backup.id}>
            <code>{backup.id}</code> <StatusPill status={backup.status} />
            <p>Created {new Date(backup.createdAt).toLocaleString()}{backup.completedAt ? ` · finished ${new Date(backup.completedAt).toLocaleString()}` : ""}</p>
            {backup.sizeBytes !== null ? <p>{backup.sizeBytes.toLocaleString()} bytes</p> : null}
            {backup.checksum ? <p className="backup-checksum">SHA-256: <code>{backup.checksum}</code></p> : null}
            {backup.errorCode ? <p>Failure: {backup.errorCode}</p> : null}
          </li>
        ))}
      </ul>
      {page?.nextCursor ? <button type="button" disabled={busy} onClick={() => void load(page.nextCursor!)}>Older backups</button> : null}
    </section>
  );
}
