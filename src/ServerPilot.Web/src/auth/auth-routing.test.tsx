import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AppRoutes } from "../app";
import type {
  AuthenticationApi,
  AuthenticationSession,
  ManagementApi,
} from "../api/server-pilot-api";
import { AuthProvider } from "./auth-context";

function createSession(): AuthenticationSession {
  return {
    userId: "1f93fb9d-931e-4b74-9cd2-6c95d81ec8e8",
    email: "owner@example.test",
    accessToken: "not-a-real-token",
    expiresAt: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
  };
}

function createManagementApi(): ManagementApi {
  return {
    listAgents: vi.fn().mockResolvedValue([]),
    listServerInstances: vi.fn().mockResolvedValue([]),
    getServerInstance: vi.fn(),
    createServerInstance: vi.fn(),
    updateServerInstance: vi.fn(),
    deleteServerInstance: vi.fn(),
    createServerCommand: vi.fn(),
    listServerCommands: vi.fn(),
  };
}

function renderRoutes(api: AuthenticationApi, initialPath: string) {
  window.history.replaceState(null, "", initialPath);
  return render(
    <AuthProvider api={api}>
      <AppRoutes managementApi={createManagementApi()} />
    </AuthProvider>,
  );
}

describe("authentication routing", () => {
  it("redirects an anonymous user away from protected routes", async () => {
    const api: AuthenticationApi = {
      login: vi.fn(),
      register: vi.fn(),
    };

    renderRoutes(api, "/app");

    expect(
      await screen.findByRole("heading", { name: "Sign in" }),
    ).toBeInTheDocument();
    expect(screen.queryByText("Operate from backend truth.")).not.toBeInTheDocument();
  });

  it("moves from login to an authenticated session and logs out", async () => {
    const login = vi.fn().mockResolvedValue(createSession());
    const api: AuthenticationApi = {
      login,
      register: vi.fn(),
    };

    renderRoutes(api, "/app");
    await screen.findByRole("heading", { name: "Sign in" });
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: " owner@example.test " },
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "not-a-real-password" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(
      await screen.findByRole("heading", { name: "Operate from backend truth." }),
    ).toBeInTheDocument();
    expect(login).toHaveBeenCalledWith({
      email: "owner@example.test",
      password: "not-a-real-password",
    });

    fireEvent.click(screen.getByRole("button", { name: "Log out" }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Sign in" })).toBeInTheDocument(),
    );
  });

  it("registers a valid user and enters the protected workspace", async () => {
    const register = vi.fn().mockResolvedValue(createSession());
    const api: AuthenticationApi = {
      login: vi.fn(),
      register,
    };

    renderRoutes(api, "/register");
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "owner@example.test" },
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "a-valid-password" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Create account" }));

    expect(
      await screen.findByRole("heading", { name: "Operate from backend truth." }),
    ).toBeInTheDocument();
    expect(register).toHaveBeenCalledOnce();
  });
});
