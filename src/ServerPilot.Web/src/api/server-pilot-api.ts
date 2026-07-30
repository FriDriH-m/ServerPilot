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

export interface AgentSummary {
  id: string;
  name: string;
  machineName: string;
  operatingSystem: string;
  version: string;
  registeredAt: string;
  lastSeenAt: string | null;
  status: string;
}

export interface ServerInstanceSummary {
  id: string;
  agentId: string;
  name: string;
  status: string;
  reportedStatus: string;
  lastProcessId: number | null;
  lastProcessStartedAt: string | null;
  lastStatusReportedAt: string | null;
  isStateStale: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ServerInstanceDetails extends ServerInstanceSummary {
  executablePath: string;
  arguments: string;
  workingDirectory: string;
  processName: string;
}

export interface CreateServerInstanceRequest {
  agentId: string;
  name: string;
  executablePath: string;
  arguments: string;
  workingDirectory: string;
  processName: string;
}

export type UpdateServerInstanceRequest = Omit<
  CreateServerInstanceRequest,
  "agentId"
>;

export interface ServerCommand {
  id: string;
  agentId: string;
  serverInstanceId: string;
  type: string;
  status: string;
  createdAt: string;
  claimedAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
  errorCode: string | null;
  attemptCount: number;
  correlationId: string;
}

export interface ServerCommandHistoryPage {
  items: ServerCommand[];
  nextCursor: string | null;
}

export type ServerCommandAction = "start" | "stop";

export interface ManagementApi {
  listAgents(accessToken: string, signal?: AbortSignal): Promise<AgentSummary[]>;
  listServerInstances(
    accessToken: string,
    signal?: AbortSignal,
  ): Promise<ServerInstanceSummary[]>;
  getServerInstance(
    accessToken: string,
    id: string,
    signal?: AbortSignal,
  ): Promise<ServerInstanceDetails>;
  createServerInstance(
    accessToken: string,
    request: CreateServerInstanceRequest,
  ): Promise<ServerInstanceDetails>;
  updateServerInstance(
    accessToken: string,
    id: string,
    request: UpdateServerInstanceRequest,
  ): Promise<ServerInstanceDetails>;
  deleteServerInstance(accessToken: string, id: string): Promise<void>;
  createServerCommand(
    accessToken: string,
    serverInstanceId: string,
    action: ServerCommandAction,
  ): Promise<ServerCommand>;
  listServerCommands(
    accessToken: string,
    serverInstanceId: string,
    cursor?: string,
    signal?: AbortSignal,
  ): Promise<ServerCommandHistoryPage>;
}

interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: unknown;
  accessToken?: string;
  signal?: AbortSignal;
}

function normalizeBaseUrl(value: string | undefined): string {
  const baseUrl = value?.trim() || "/api";
  return baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
}

export class ServerPilotApi implements AuthenticationApi, ManagementApi {
  private readonly baseUrl: string;
  private readonly fetchImplementation: typeof fetch;

  constructor(baseUrl: string | undefined, fetchImplementation?: typeof fetch) {
    this.baseUrl = normalizeBaseUrl(baseUrl);
    this.fetchImplementation =
      fetchImplementation ?? globalThis.fetch.bind(globalThis);
  }

  login(request: AuthenticationRequest): Promise<AuthenticationSession> {
    return this.send<AuthenticationSession>("/auth/login", {
      method: "POST",
      body: request,
    });
  }

  register(request: AuthenticationRequest): Promise<AuthenticationSession> {
    return this.send<AuthenticationSession>("/auth/register", {
      method: "POST",
      body: request,
    });
  }

  listAgents(accessToken: string, signal?: AbortSignal): Promise<AgentSummary[]> {
    return this.send<AgentSummary[]>("/agents?limit=100&page=1", {
      accessToken,
      signal,
    });
  }

  listServerInstances(
    accessToken: string,
    signal?: AbortSignal,
  ): Promise<ServerInstanceSummary[]> {
    return this.send<ServerInstanceSummary[]>(
      "/server-instances?limit=100&page=1",
      { accessToken, signal },
    );
  }

  getServerInstance(
    accessToken: string,
    id: string,
    signal?: AbortSignal,
  ): Promise<ServerInstanceDetails> {
    return this.send<ServerInstanceDetails>(`/server-instances/${id}`, {
      accessToken,
      signal,
    });
  }

  createServerInstance(
    accessToken: string,
    request: CreateServerInstanceRequest,
  ): Promise<ServerInstanceDetails> {
    return this.send<ServerInstanceDetails>("/server-instances", {
      method: "POST",
      accessToken,
      body: request,
    });
  }

  updateServerInstance(
    accessToken: string,
    id: string,
    request: UpdateServerInstanceRequest,
  ): Promise<ServerInstanceDetails> {
    return this.send<ServerInstanceDetails>(`/server-instances/${id}`, {
      method: "PUT",
      accessToken,
      body: request,
    });
  }

  deleteServerInstance(accessToken: string, id: string): Promise<void> {
    return this.send<void>(`/server-instances/${id}`, {
      method: "DELETE",
      accessToken,
    });
  }

  createServerCommand(
    accessToken: string,
    serverInstanceId: string,
    action: ServerCommandAction,
  ): Promise<ServerCommand> {
    return this.send<ServerCommand>(
      `/server-instances/${serverInstanceId}/commands/${action}`,
      { method: "POST", accessToken },
    );
  }

  listServerCommands(
    accessToken: string,
    serverInstanceId: string,
    cursor?: string,
    signal?: AbortSignal,
  ): Promise<ServerCommandHistoryPage> {
    const cursorQuery = cursor ? `&cursor=${encodeURIComponent(cursor)}` : "";
    return this.send<ServerCommandHistoryPage>(
      `/server-instances/${serverInstanceId}/commands?limit=20${cursorQuery}`,
      { accessToken, signal },
    );
  }

  private async send<T>(path: string, options: RequestOptions = {}): Promise<T> {
    const headers: Record<string, string> = {
      Accept: "application/json, application/problem+json",
    };
    if (options.body !== undefined) {
      headers["Content-Type"] = "application/json";
    }
    if (options.accessToken) {
      headers.Authorization = `Bearer ${options.accessToken}`;
    }

    const response = await this.fetchImplementation(`${this.baseUrl}${path}`, {
      method: options.method ?? "GET",
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      credentials: "omit",
      signal: options.signal,
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

    if (response.status === 204) {
      return undefined as T;
    }

    return (await response.json()) as T;
  }
}

export const serverPilotApi = new ServerPilotApi(import.meta.env.VITE_API_BASE_URL);
