import { useEffect, useRef, useState } from "react";
import type { BackupHistoryPage, ManagementApi } from "../api/server-pilot-api";
import { ErrorAlert } from "./error-alert";
import { StatusPill } from "./status-pill";

interface Props {
  api: Pick<ManagementApi, "listBackups" | "restoreBackup" | "applyBackupRetention">;
  accessToken: string;
  serverId: string;
  canCreate: boolean;
  onCreate: () => void;
}

export function ServerBackups({ api, accessToken, serverId, canCreate, onCreate }: Props) {
  const [page, setPage] = useState<BackupHistoryPage | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [keepCount, setKeepCount] = useState(10);
  const [confirmation, setConfirmation] = useState<{ backupId?: string } | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);
  const submitting = useRef(false);
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

  async function maintain() {
    if (!confirmation || submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError(null);
    try {
      const result = confirmation.backupId
        ? await api.restoreBackup(accessToken, serverId, confirmation.backupId)
        : await api.applyBackupRetention(accessToken, serverId, keepCount);
      setMessage(result ? `Command ${result.commandId} queued. Follow its progress and result in Command history below.` : "Retention satisfied. No archives need deletion.");
      setConfirmation(null);
      setPage(null);
    } catch (failure) { setError(failure); }
    finally { submitting.current = false; setBusy(false); }
  }

  return (
    <section aria-label="Local backups">
      <h3>Local backups</h3>
      <p>Project Zomboid only. Stop the server first. Restore replaces the current server data with a verified local archive. The Agent preserves the previous data for recovery. Downloads are not available.</p>
      <div className="command-actions">
        <button type="button" className="primary-button" disabled={!canCreate} onClick={onCreate}>Create local backup</button>
        <button type="button" className="secondary-button" disabled={busy} onClick={() => void load()}>Refresh backups</button>
        <label>Keep latest backups <input type="number" min={1} max={1000} value={keepCount} disabled={busy || confirmation !== null} onChange={(event) => setKeepCount(Number(event.target.value))} /></label>
        <button type="button" disabled={!canCreate || busy || !Number.isInteger(keepCount) || keepCount < 1 || keepCount > 1000} onClick={() => setConfirmation({})}>Apply retention</button>
      </div>
      {confirmation ? <div role="alertdialog" aria-label="Confirm backup operation">
        <p>{confirmation.backupId ? `Replace current server data using backup ${confirmation.backupId}? The server must remain stopped.` : `Permanently delete older ServerPilot archives, keeping the latest ${keepCount} completed backups? Previously interrupted deletions will also finish.`}</p>
        <button type="button" disabled={busy || !canCreate} onClick={() => void maintain()}>Confirm</button>
        <button type="button" disabled={busy} onClick={() => setConfirmation(null)}>Cancel</button>
      </div> : null}
      {message ? <p role="status">{message}</p> : null}
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
            {backup.status === "Completed" ? <button type="button" disabled={!canCreate || busy} onClick={() => setConfirmation({ backupId: backup.id })}>Restore {backup.id}</button> : null}
          </li>
        ))}
      </ul>
      {page?.nextCursor ? <button type="button" disabled={busy} onClick={() => void load(page.nextCursor!)}>Older backups</button> : null}
    </section>
  );
}
