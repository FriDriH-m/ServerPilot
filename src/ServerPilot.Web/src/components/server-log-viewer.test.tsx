import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ServerLogBuffer } from "../dashboard/dashboard-model";
import { ServerLogViewer } from "./server-log-viewer";

const logs: ServerLogBuffer = {
  status: "Available",
  cursor: "cursor",
  reset: false,
  lines: ["INFO server started", "WARN password=[REDACTED]"],
  reportedAt: "2026-08-24T12:00:00Z",
  isStale: false,
};

describe("ServerLogViewer", () => {
  it("filters locally and exposes pause without rendering hidden lines", () => {
    const onPausedChange = vi.fn();
    render(
      <ServerLogViewer
        logs={logs}
        paused={false}
        stateStale={false}
        onPausedChange={onPausedChange}
      />,
    );

    fireEvent.change(screen.getByLabelText("Filter visible lines"), {
      target: { value: "warn" },
    });
    expect(screen.getByText("WARN password=[REDACTED]", { exact: false }))
      .toBeInTheDocument();
    expect(screen.queryByText("INFO server started", { exact: false }))
      .not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Pause" }));
    expect(onPausedChange).toHaveBeenCalledWith(true);
  });

  it("shows stale and unsupported states explicitly", () => {
    const { rerender } = render(
      <ServerLogViewer
        logs={{ ...logs, isStale: true }}
        paused={false}
        stateStale={false}
        onPausedChange={vi.fn()}
      />,
    );
    expect(screen.getByText(/Log output is stale/)).toBeInTheDocument();

    rerender(
      <ServerLogViewer
        logs={{ ...logs, status: "Unsupported", lines: [] }}
        paused={false}
        stateStale={false}
        onPausedChange={vi.fn()}
      />,
    );
    expect(screen.getByText(/no configured log source/i, { selector: "pre" }))
      .toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Pause" })).toBeDisabled();
  });
});
