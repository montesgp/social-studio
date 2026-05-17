# Observability (Global Rule)

## Purpose
Make runtime behavior diagnosable in production without leaking sensitive data.

## Scope
All services, workers, and background jobs that produce logs, metrics, or traces.

## Load Conditions
Always loaded when any backend or fullstack profile is active.

## Do Not Load When
Pure documentation or design-only changes.

## Rules
- MUST emit structured logs with diagnostic context for failures and meaningful execution boundaries.
- MUST protect secrets, tokens, credentials, and PII from logs and error payloads.
- MUST centralize logger configuration through infrastructure modules; presentation/application layers do not configure logging.
- SHOULD propagate correlation/trace identifiers across request boundaries.
- SHOULD avoid duplicate or noisy logs at the same severity for the same event.
- MUST instrument critical paths (auth, write transactions, external provider calls, scheduled jobs) with metrics or traces.
- MUST expose health checks for critical services and their declared dependencies.

## Output Expectations
- Failures land in logs with enough context to reproduce or diagnose without re-running.
- Logs do not contain raw bearer tokens, passwords, full payment data, or secrets.
- Production traffic can be correlated end-to-end through trace identifiers.
