import { describe, expect, it, vi } from "vitest";
import { ServerPilotApi } from "./server-pilot-api";

describe("ServerPilotApi", () => {
  it("binds the browser fetch receiver when no test implementation is injected", async () => {
    const nativeLikeFetch = vi.fn(function (this: unknown) {
      if (this !== globalThis) {
        return Promise.reject(new TypeError("Illegal invocation"));
      }

      return Promise.resolve(
        new Response(
          JSON.stringify({
            userId: "1f93fb9d-931e-4b74-9cd2-6c95d81ec8e8",
            email: "owner@example.test",
            accessToken: "not-a-real-token",
            expiresAt: "2099-07-29T12:00:00Z",
          }),
          {
            status: 200,
            headers: { "content-type": "application/json" },
          },
        ),
      );
    }) as typeof fetch;
    vi.stubGlobal("fetch", nativeLikeFetch);

    try {
      const api = new ServerPilotApi("/api");
      const result = await api.login({
        email: "owner@example.test",
        password: "not-a-real-password",
      });

      expect(result.email).toBe("owner@example.test");
      expect(nativeLikeFetch).toHaveBeenCalledOnce();
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it("maps validation Problem Details to one safe client error", async () => {
    const fetchImplementation = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          title: "One or more validation errors occurred.",
          status: 400,
          correlationId: "request-123",
          errors: {
            Email: ["The Email field is not a valid e-mail address."],
          },
        }),
        {
          status: 400,
          headers: { "content-type": "application/problem+json" },
        },
      ),
    );
    const api = new ServerPilotApi("/api/", fetchImplementation);

    const request = api.login({ email: "invalid", password: "secret" });

    await expect(request).rejects.toEqual(
      expect.objectContaining({
        message: "The Email field is not a valid e-mail address.",
        status: 400,
        correlationId: "request-123",
      }),
    );
    expect(fetchImplementation).toHaveBeenCalledWith(
      "/api/auth/login",
      expect.objectContaining({ method: "POST", credentials: "omit" }),
    );
  });

  it("does not expose an unexpected server response body", async () => {
    const fetchImplementation = vi.fn<typeof fetch>().mockResolvedValue(
      new Response("database password was accidentally included", {
        status: 500,
        headers: { "content-type": "text/plain" },
      }),
    );
    const api = new ServerPilotApi("/api", fetchImplementation);

    await expect(
      api.login({ email: "owner@example.test", password: "not-a-real-password" }),
    ).rejects.toThrow("ServerPilot is temporarily unavailable. Please try again.");
  });

  it("uses bearer authentication for management reads", async () => {
    const fetchImplementation = vi.fn<typeof fetch>().mockResolvedValue(
      Response.json([]),
    );
    const api = new ServerPilotApi("/api", fetchImplementation);

    await api.listAgents("access-token");

    expect(fetchImplementation).toHaveBeenCalledWith(
      "/api/agents?limit=100&page=1",
      expect.objectContaining({
        method: "GET",
        headers: expect.objectContaining({ Authorization: "Bearer access-token" }),
        credentials: "omit",
      }),
    );
  });

  it("encodes an opaque command-history cursor", async () => {
    const fetchImplementation = vi.fn<typeof fetch>().mockResolvedValue(
      Response.json({ items: [], nextCursor: null }),
    );
    const api = new ServerPilotApi("/api", fetchImplementation);

    await api.listServerCommands("access-token", "server-1", "time+id=/");

    expect(fetchImplementation).toHaveBeenCalledWith(
      "/api/server-instances/server-1/commands?limit=20&cursor=time%2Bid%3D%2F",
      expect.any(Object),
    );
  });

  it("accepts an empty successful delete response", async () => {
    const fetchImplementation = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(null, { status: 204 }),
    );
    const api = new ServerPilotApi("/api", fetchImplementation);

    await expect(
      api.deleteServerInstance("access-token", "server-1"),
    ).resolves.toBeUndefined();
  });
});
