import {
  parseProblemDetails,
  toApiProblemError,
  type ProblemDetails,
} from "./problem-details";

export interface AuthenticationRequest {
  email: string;
  password: string;
}

export interface AuthenticationSession {
  userId: string;
  email: string;
  accessToken: string;
  expiresAt: string;
}

export interface AuthenticationApi {
  login(request: AuthenticationRequest): Promise<AuthenticationSession>;
  register(request: AuthenticationRequest): Promise<AuthenticationSession>;
}

function normalizeBaseUrl(value: string | undefined): string {
  const baseUrl = value?.trim() || "/api";
  return baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
}

export class ServerPilotApi implements AuthenticationApi {
  private readonly baseUrl: string;
  private readonly fetchImplementation: typeof fetch;

  constructor(baseUrl: string | undefined, fetchImplementation?: typeof fetch) {
    this.baseUrl = normalizeBaseUrl(baseUrl);
    this.fetchImplementation =
      fetchImplementation ?? globalThis.fetch.bind(globalThis);
  }

  login(request: AuthenticationRequest): Promise<AuthenticationSession> {
    return this.send<AuthenticationSession>("/auth/login", request);
  }

  register(request: AuthenticationRequest): Promise<AuthenticationSession> {
    return this.send<AuthenticationSession>("/auth/register", request);
  }

  private async send<T>(path: string, body: unknown): Promise<T> {
    const response = await this.fetchImplementation(`${this.baseUrl}${path}`, {
      method: "POST",
      headers: {
        Accept: "application/json, application/problem+json",
        "Content-Type": "application/json",
      },
      body: JSON.stringify(body),
      credentials: "omit",
    });

    if (!response.ok) {
      let problem: ProblemDetails | undefined;
      const contentType = response.headers.get("content-type") ?? "";
      if (contentType.includes("application/problem+json")) {
        try {
          problem = parseProblemDetails(await response.json());
        } catch {
          problem = undefined;
        }
      }

      throw toApiProblemError(response.status, problem);
    }

    return (await response.json()) as T;
  }
}

export const serverPilotApi = new ServerPilotApi(import.meta.env.VITE_API_BASE_URL);
