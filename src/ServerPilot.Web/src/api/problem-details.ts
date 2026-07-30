export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
}

export class ApiProblemError extends Error {
  readonly status: number;
  readonly correlationId?: string;
  readonly validationErrors: readonly string[];

  constructor(
    message: string,
    status: number,
    correlationId?: string,
    validationErrors: readonly string[] = [],
  ) {
    super(message);
    this.name = "ApiProblemError";
    this.status = status;
    this.correlationId = correlationId;
    this.validationErrors = validationErrors;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readString(value: unknown): string | undefined {
  return typeof value === "string" && value.trim().length > 0
    ? value.trim()
    : undefined;
}

function readValidationErrors(value: unknown): Record<string, string[]> | undefined {
  if (!isRecord(value)) {
    return undefined;
  }

  const errors: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(value)) {
    if (!Array.isArray(messages)) {
      continue;
    }

    const safeMessages = messages
      .map(readString)
      .filter((message): message is string => message !== undefined);
    if (safeMessages.length > 0) {
      errors[field] = safeMessages;
    }
  }

  return Object.keys(errors).length > 0 ? errors : undefined;
}

export function parseProblemDetails(value: unknown): ProblemDetails | undefined {
  if (!isRecord(value)) {
    return undefined;
  }

  const status = typeof value.status === "number" ? value.status : undefined;
  const errors = readValidationErrors(value.errors);
  const problem: ProblemDetails = {
    type: readString(value.type),
    title: readString(value.title),
    status,
    detail: readString(value.detail),
    instance: readString(value.instance),
    correlationId: readString(value.correlationId),
    errors,
  };

  return problem.title || problem.detail || problem.status || problem.errors
    ? problem
    : undefined;
}

export function toApiProblemError(
  responseStatus: number,
  problem: ProblemDetails | undefined,
): ApiProblemError {
  const validationErrors = problem?.errors
    ? Object.values(problem.errors).flat()
    : [];

  let message = "The request could not be completed.";
  if (responseStatus >= 500) {
    message = "ServerPilot is temporarily unavailable. Please try again.";
  } else if (validationErrors.length > 0) {
    [message] = validationErrors;
  } else if (problem?.detail) {
    message = problem.detail;
  } else if (problem?.title) {
    message = problem.title;
  }

  return new ApiProblemError(
    message,
    responseStatus,
    problem?.correlationId,
    validationErrors,
  );
}
