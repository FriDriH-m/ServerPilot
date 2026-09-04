import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ServerBackups } from "./server-backups";

describe("ServerBackups", () => {
  it("loads history only on demand and displays safe metadata and older pages", async () => {
    const listBackups = vi.fn().mockResolvedValueOnce({ items: [{ id: "backup-1", status: "Completed", createdAt: "2026-09-04T12:00:00Z", completedAt: null, sizeBytes: 120, checksum: "ABC", errorCode: null }], nextCursor: "older" })
      .mockResolvedValueOnce({ items: [{ id: "backup-2", status: "Failed", createdAt: "2026-09-04T12:00:00Z", completedAt: null, sizeBytes: null, checksum: null, errorCode: "BackupFileOperationFailed" }], nextCursor: null });
    const onCreate = vi.fn();
    render(<ServerBackups api={{ listBackups }} accessToken="token" serverId="server" canCreate onCreate={onCreate} />);
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
    render(<ServerBackups api={{ listBackups }} accessToken="token" serverId="server" canCreate={false} onCreate={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Create local backup" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Refresh backups" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Agent unavailable");
    await waitFor(() => expect(screen.getByRole("button", { name: "Refresh backups" })).toBeEnabled());
  });
});
