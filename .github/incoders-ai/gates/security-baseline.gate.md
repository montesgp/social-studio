# Security Baseline Gate

## Purpose
Enforce a small, high-signal set of security defaults that complement the secrets, vulnerability, and architecture gates without requiring heavy security tooling in Phase 1.

## Scope
Applies to:
- All `appsettings*.json` and `*.env*` template files in tracked sources.
- All `*.cs` files in `Api`/`Infrastructure` layers (per `project.gates.yml`).

## Load Conditions
Every CI run; every local pre-task validation.

## Do Not Load When
Documentation-only changes.

## Rules

### Configuration Hygiene (default level: **MUST**)
- Tracked `appsettings*.json` MUST NOT contain non-empty values for keys whose name matches `*Secret`, `*Password`, `*ConnectionString`, `Authentication:Jwt:Secret`, `*ApiKey` unless the value is one of: `""`, `null`, `<placeholder>`, `${env:*}`, `from-user-secrets`, `change-me`.
- Real secrets MUST come from environment variables, dotnet user-secrets, or Aspire secret bindings.

### Auth Hardening (default level: **SHOULD**)
- JWT signing keys SHOULD have minimum length declared in `project.gates.yml -> security.jwt_min_length` (default 32).
- Endpoints in `Api` projects SHOULD declare `[Authorize]` or be explicitly mapped under an anonymous-allowed route. The gate emits a SHOULD warning for endpoints without either.

### Transport (default level: **SHOULD**)
- HTTP-only routes outside Development MUST go through HTTPS. The gate emits SHOULD warning when `appsettings*.json` contains plain `http://` URLs in non-test, non-example sections.

### Insecure Defaults (default level: **MUST**)
- `*.cs` MUST NOT contain `ServerCertificateCustomValidationCallback = (_, _, _, _) => true` or equivalent unconditional bypasses.
- `*.cs` MUST NOT contain `AllowAnyOrigin().AllowCredentials()` combinations (CORS misconfiguration).

## Output Expectations
- `[ MUST ] security.config: pass|fail — <count> findings`.
- `[ SHOULD ] security.auth: pass|warn — <count> warnings`.
- `[ SHOULD ] security.transport: pass|warn — <count> warnings`.
- `[ MUST ] security.insecure-defaults: pass|fail — <count> findings`.
- Non-zero exit code on any MUST failure.

## Future Enhancements (Phase 2 / Phase 3)
- CodeQL on PRs.
- OWASP Dependency-Check / Snyk integration.
- Threat models per high-impact feature.
- Runtime governance (CSP, security headers verification, observability for auth failures).
