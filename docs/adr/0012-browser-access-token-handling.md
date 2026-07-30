# ADR 0012: Browser access-token handling

## Context

Issue #35 adds the first browser client and therefore extends the user/API trust
boundary into a JavaScript runtime. The API returns a short-lived bearer JWT after
registration or login and does not yet provide refresh tokens or a server-managed
browser session. The client needs authenticated routing without embedding secrets or
silently exposing unexpected API responses.

## Decision

- Keep the user JWT only in React memory. Do not persist it in `localStorage`,
  `sessionStorage`, IndexedDB, a URL, logs or source-controlled configuration.
- Treat a reload, tab close, explicit logout or token expiry as the end of the browser
  session. Clear the in-memory session automatically at `expiresAt`.
- Default the browser API base URL to same-origin `/api`. During local development,
  Vite proxies `/api` to the configured loopback API target so the backend does not
  need a broader CORS policy.
- Treat `VITE_API_BASE_URL` as public build configuration, never as a secret. The
  deployment operator is responsible for pointing it only at the intended API and
  using HTTPS outside loopback development.
- Use a small History API router for the current login, registration and protected
  workspace routes. Accept a post-login return path only when it is a single-rooted
  local path without a protocol-relative prefix or backslash.
- Parse error bodies only when the response declares `application/problem+json`.
  Show validation messages and safe expected-error details, retain the correlation ID,
  and replace all unexpected `5xx` bodies with a generic message.

## Alternatives considered

- `localStorage` or `sessionStorage`: rejected because any same-origin script can read
  the bearer token and persistence increases the useful lifetime of a stolen value.
- An HttpOnly, Secure, SameSite cookie: preferable for a longer-lived browser session,
  but deferred because it requires a deliberate backend session/CSRF design rather
  than client-only work in #35.
- A routing framework: not justified for three routes, and the evaluated current
  React Router releases had unresolved high-severity advisories in their wider
  SSR/RSC feature surface. Re-evaluate a patched library if #36 makes routing complex.
- A direct cross-origin development API URL: supported through the public base-URL
  setting only when the API explicitly permits that origin, but not the default because
  it would expand backend CORS policy solely for local tooling.

## Consequences

- Refreshing the page requires the user to sign in again. Multiple tabs do not share
  authentication state.
- Memory-only storage reduces persistence risk but cannot protect an active token from
  malicious same-origin JavaScript, a compromised browser extension or an already
  compromised device.
- The existing 30-minute JWT remains non-revocable until expiry; logout removes only
  the browser's copy.
- A future cookie or refresh-token design must replace this ADR explicitly and include
  CSRF, rotation and revocation decisions.
- Local browser traffic stays same-origin from the browser's perspective, while the
  development proxy target remains server-side tooling configuration.

## Verification evidence

- Frontend tests cover anonymous protected-route rejection, login, registration,
  logout and safe Problem Details mapping.
- API-client tests prove that an arbitrary `5xx` response body is not rendered.
- The production TypeScript/Vite build succeeds without embedding an API credential.
- `npm audit --audit-level=high` reports no vulnerabilities in the resolved lockfile.
- `eng/verify.ps1` runs deterministic `npm ci`, audit, frontend tests and build together
  with the existing backend verification.
