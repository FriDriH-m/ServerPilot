import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ServerBackups } from "./server-backups";

describe("ServerBackups", () => {
  it("requires explicit restore confirmation and reports the queued command", async () => {
    const restoreBackup = vi.fn().mockResolvedValue({ commandId: "restore-command" });
    const listBackups = vi.fn().mockResolvedValue({ items: [{ id: "backup-1", status: "Completed", createdAt: "2026-09-04T12:00:00Z", completedAt: null, sizeBytes: 120, checksum: "ABC", errorCode: null }], nextCursor: null });
    render(<ServerBackups api={{ listBackups, restoreBackup, applyBackupRetention: vi.fn() }} accessToken="token" serverId="server" canCreate onCreate={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Refresh backups" }));
    fireEvent.click(await screen.findByRole("button", { name: "Restore backup-1" }));
    expect(restoreBackup).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog")).toHaveTextContent("Replace current server data");
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(restoreBackup).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Restore backup-1" }));
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));
    expect(await screen.findByRole("status")).toHaveTextContent("restore-command queued");
    expect(restoreBackup).toHaveBeenCalledWith("token", "server", "backup-1");
  });

  it("validates retention count and confirms permanent deletion", async () => {
    const applyBackupRetention = vi.fn().mockResolvedValue(undefined);
    render(<ServerBackups api={{ listBackups: vi.fn(), restoreBackup: vi.fn(), applyBackupRetention }} accessToken="token" serverId="server" canCreate onCreate={vi.fn()} />);
    fireEvent.change(screen.getByRole("spinbutton"), { target: { value: "0" } });
    expect(screen.getByRole("button", { name: "Apply retention" })).toBeDisabled();
    fireEvent.change(screen.getByRole("spinbutton"), { target: { value: "5" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply retention" }));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("Permanently delete");
    expect(applyBackupRetention).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));
    expect(await screen.findByRole("status")).toHaveTextContent("No archives need deletion");
    expect(applyBackupRetention).toHaveBeenCalledWith("token", "server", 5);
  });

  it("loads history only on demand and displays safe metadata and older pages", async () => {
    const listBackups = vi.fn().mockResolvedValueOnce({ items: [{ id: "backup-1", status: "Completed", createdAt: "2026-09-04T12:00:00Z", completedAt: null, sizeBytes: 120, checksum: "ABC", errorCode: null }], nextCursor: "older" })
      .mockResolvedValueOnce({ items: [{ id: "backup-2", status: "Failed", createdAt: "2026-09-04T12:00:00Z", completedAt: null, sizeBytes: null, checksum: null, errorCode: "BackupFileOperationFailed" }], nextCursor: null });
    const onCreate = vi.fn();
    render(<ServerBackups api={{ listBackups, restoreBackup: vi.fn(), applyBackupRetention: vi.fn() }} accessToken="token" serverId="server" canCreate onCreate={onCreate} />);
    expect(listBackups).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Create local backup" }));
    expect(onCreate).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByRole("button", { name: "Refresh backups" }));
    expect(await screen.findByText("backup-1")).toBeInTheDocument();
    expect(screen.getByText("120 bytes")).toBeInTheDocument();
    expect(screen.getByText("ABC")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Older backups" }));
    expect(await screen.findByText("Failure: BackupFileOperationFailed")).toBeInTheDocument();
    expect(listBackups).toHaveBeenLastCalledWith("token", "server", "older", expect.any(AbortSignal));
    expect(screen.queryByText("backup-1")).not.toBeInTheDocument();
  });

  it("disables unsafe creation and preserves an actionable fetch error", async () => {
    const listBackups = vi.fn().mockRejectedValue(new Error("Agent unavailable"));
    render(<ServerBackups api={{ listBackups, restoreBackup: vi.fn(), applyBackupRetention: vi.fn() }} accessToken="token" serverId="server" canCreate={false} onCreate={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Create local backup" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Refresh backups" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Agent unavailable");
    await waitFor(() => expect(screen.getByRole("button", { name: "Refresh backups" })).toBeEnabled());
  });
});
