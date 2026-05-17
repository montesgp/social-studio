# Backend Observability

## Purpose
Make runtime flows diagnosable in production.

## Scope
Any backend code path that performs IO, transactions, external calls, or long-running work.

## Load Conditions
A backend or fullstack profile is active.

## Do Not Load When
Pure documentation, IaC, or design-only changes.

## Inputs
- The flow being implemented and its critical decision points.

## Procedure
1. Identify meaningful execution boundaries (request entry, transaction boundaries, external call dispatch and response, retry, fallback, failure).
2. Emit a structured log entry at each boundary with: operation name, correlation id, key entity ids, outcome (success/failure/retry), and duration when relevant.
3. Sanitize the entry — never log raw tokens, secrets, full payment data, or PII.
4. Wire the flow to the shared correlation/trace propagation.
5. Add a metric or trace span for performance-critical or business-critical paths.
6. Add a health check for any new outbound dependency.

## Output Expectations
- A failure can be reconstructed from logs without re-running the operation.
- Sensitive values do not appear in logs.
- Correlation ids flow end-to-end across requests and outbound calls.
