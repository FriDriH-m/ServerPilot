import { ApiProblemError } from "../api/problem-details";

interface ErrorAlertProps {
  error: unknown;
}

export function ErrorAlert({ error }: ErrorAlertProps) {
  const message =
    error instanceof Error
      ? error.message
      : "The request could not be completed.";
  const correlationId =
    error instanceof ApiProblemError ? error.correlationId : undefined;

  return (
    <div className="error-alert" role="alert">
      <strong>Something went wrong</strong>
      <span>{message}</span>
      {correlationId ? <small>Reference: {correlationId}</small> : null}
    </div>
  );
}
