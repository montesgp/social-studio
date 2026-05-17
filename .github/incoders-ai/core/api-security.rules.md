# API Security Rules (Global Standard)

## Purpose
Define concrete, implementation-level security standards for HTTP APIs that complement the high-level security baseline in `best-practice.rules.md`. These rules close the gaps between principle and practice: rate limiting, injection prevention, security headers, brute-force protection, input/output hygiene, and error-response discipline.

## Scope
All HTTP API surfaces (REST, minimal API, controllers) in `Api` and `Infrastructure` layers. Frontend or BFF layers that make direct external calls are also in scope.

## Load Conditions
Always loaded for backend, fullstack, and monorepo profiles.

## Do Not Load When
Documentation-only changes; frontend-only changes with no API surface modifications.

---

## 1) Rate Limiting & Throttling (MUST)

- Register `UseRateLimiter()` middleware in the ASP.NET Core pipeline for all production APIs.
- Apply a **fixed-window or sliding-window policy** on authentication endpoints (`/login`, `/register`, `/token`, `/reset-password`) with a low limit (e.g., 10 req/min per IP) and a `429 Too Many Requests` response.
- Apply a **concurrency or token-bucket policy** on resource-intensive endpoints (file upload, AI inference, batch operations).
- Rate limit policies MUST be centralized in a single registration point (e.g., `AddRateLimiter` extension); inline per-endpoint configuration is not allowed.
- Include `Retry-After` header in `429` responses.
- SHOULD distinguish between authenticated and anonymous limits (authenticated users may get higher quotas).

## 2) Injection Prevention (MUST)

- MUST NOT concatenate user-supplied input into SQL strings, shell commands, XPath expressions, or LDAP filters. Use parameterized queries or ORM-generated queries exclusively.
- When using EF Core: avoid `FromSqlRaw` with string interpolation; prefer `FromSqlInterpolated` or `ExecuteSqlInterpolated`, which use `DbParameter` under the hood.
- When using Dapper: every query receiving external input MUST use anonymous-object parameters, never string format/concatenation.
- MUST validate and sanitize inputs before passing them to any persistence, messaging, or external-integration boundary (see `external-integrations.rules.md`).
- SHOULD use `FluentValidation` at the Application boundary for all command/query inputs; validation failures MUST return `400 Bad Request` with a structured `ProblemDetails` body.
- File uploads MUST validate MIME type, extension allowlist, and max file size server-side; never rely solely on `Content-Type` header from the client.

## 3) Security Headers (MUST)

Configure the following headers in the ASP.NET Core pipeline or reverse-proxy layer. Missing headers MUST be treated as a SHOULD warning; incorrect values that weaken security are MUST failures.

| Header | Required Value / Guidance |
|---|---|
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` (HSTS, non-Development only) |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` or `SAMEORIGIN` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Permissions-Policy` | Explicitly disable unused browser features (e.g., `camera=(), microphone=()`) |
| `Content-Security-Policy` | Deny-by-default (`default-src 'none'`); add explicit allow-lists per content type |

- Use `app.UseHsts()` outside Development and `app.UseHttpsRedirection()` globally.
- API-only services that serve no HTML MAY omit CSP but MUST still set the other headers.
- SHOULD use a middleware or helper (e.g., `NWebsec`, custom `IApplicationBuilder` extension) to centralize header registration, not per-endpoint `Response.Headers.Add`.

## 4) CORS Policy (MUST)

- MUST define an explicit CORS policy with a named allowlist of origins; never use `AllowAnyOrigin()` in production.
- `AllowAnyOrigin()` combined with `AllowCredentials()` is forbidden at all times (also enforced by `security-baseline.gate.md`).
- Allowed origins MUST be driven by configuration (environment variable or `appsettings`), not hardcoded.
- SHOULD restrict allowed methods and headers to the minimum set the API actually uses.
- Pre-flight cache duration (`MaxAge`) SHOULD be set to at least 600 seconds to reduce OPTIONS overhead.

## 5) Authentication & Token Hygiene (MUST)

- JWTs MUST be signed with RS256 or ES256 in production; HS256 is only acceptable for single-service deployments where the signing key is not shared.
- Access tokens MUST have a short TTL (≤ 15 minutes for user-facing tokens).
- Refresh tokens MUST be rotated on use and invalidated on logout; store only a hash server-side.
- MUST NOT include sensitive claims (passwords, PII beyond what's needed for authZ) in the JWT payload.
- Token transmission MUST use `Authorization: Bearer` header; MUST NOT accept tokens in URL query parameters.
- SHOULD implement token revocation via a short-lived blocklist or opaque token strategy for high-risk operations.

## 6) Brute-Force & Account Lockout (MUST)

- Authentication endpoints MUST implement account lockout after a configurable number of consecutive failures (default: 5 attempts within 10 minutes).
- Lockout duration SHOULD be progressive (e.g., 5 min → 15 min → 60 min) to reduce DoS risk from self-lockout.
- Lock-out state SHOULD be stored server-side (cache or database), not in the JWT or cookie.
- Failed authentication responses MUST be identical for invalid-user and invalid-password cases (generic `401`, no distinguishing message) to prevent user enumeration.
- SHOULD log failed attempts with IP, timestamp, and username hash (not plaintext) for anomaly detection.

## 7) Request & Response Size Limits (MUST)

- MUST configure `MaxRequestBodySize` via Kestrel or `IIS` settings; do not rely on the framework default (30 MB) for all endpoints.
- Endpoints accepting file uploads MUST declare an explicit, lower limit per endpoint using `[RequestSizeLimit]`.
- API responses MUST NOT include unbounded result sets; enforce server-side pagination (default page size, max page size).
- JSON serialization MUST NOT deserialize into types with circular references or unbounded depth; configure `MaxDepth` on `JsonSerializerOptions`.

## 8) Error Response Discipline (MUST)

- Internal exception details (stack traces, inner exceptions, file paths, SQL) MUST NOT appear in HTTP responses outside Development.
- Use RFC 7807 `ProblemDetails` for all error responses; the `detail` field MUST contain a user-safe message only.
- Distinguish between client errors (`4xx`) and server errors (`5xx`) at the response level; do not map all unhandled exceptions to `500`.
- SHOULD use a global exception filter or `UseExceptionHandler` with a custom handler, not per-controller try/catch blocks.
- Correlation IDs MUST be included in error responses (`traceId` field in `ProblemDetails`) to enable log-to-response tracing.

## 9) Sensitive Data in Transit & Storage (MUST)

- MUST NOT include sensitive values (tokens, keys, passwords, PII) in URL paths or query parameters; use request body or headers.
- SHOULD mask or truncate sensitive fields in structured logs (see `observability.rules.md`).
- MUST NOT cache authenticated responses in shared caches; include `Cache-Control: no-store` on all authenticated endpoints.
- MUST NOT return full credit card numbers, SSNs, or raw passwords in API responses; apply masking at the Application layer before returning.

## 10) CSRF Protection (SHOULD)

- APIs that authenticate via cookies (session or cookie-stored JWT) MUST implement CSRF protection using the Synchronizer Token Pattern or `SameSite=Strict`/`Lax` cookies.
- Pure bearer-token APIs (no cookies) are not subject to CSRF but MUST reject requests without a valid `Authorization` header.
- SHOULD set `SameSite=Strict` on authentication cookies; `Lax` is acceptable for read-only flows.
- Anti-forgery tokens MUST be validated on all state-changing operations (POST, PUT, PATCH, DELETE) for cookie-auth APIs.

## 11) Dependency & Supply-Chain (SHOULD)

- All third-party packages MUST pass the vulnerability gate (`vulnerabilities.gate.md`) before merge.
- SHOULD pin package versions in production deployments; floating ranges (`*`, `^major`) are allowed in development only.
- SHOULD audit `npm audit` / `dotnet list package --vulnerable` in CI and block on Critical/High findings.

---

## Output Expectations

- All MUST rules are blocking: a task touching API surfaces is NOT complete if any MUST rule is violated.
- Rate limiting, security headers, and CORS policy MUST be verifiable via the `security-baseline.gate.md` checks or manual inspection before marking a feature complete.
- Error response discipline failures (stack traces in responses) MUST be caught in integration tests or manual QA before merge.
