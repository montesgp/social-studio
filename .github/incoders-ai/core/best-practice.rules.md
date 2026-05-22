# Best Practices Rules (Global Reusable Standard)

## Scope and Enforcement
- Applies to all repositories loading Incoders AI manifests.
- Rule levels:
  - **MUST**: mandatory baseline.
  - **SHOULD**: strong default unless justified.
  - **MAY**: optional improvement.
- Project rules can tighten standards but MUST NOT weaken safety/security controls.

## 1) Engineering Principles (MUST)
- Optimize for correctness, maintainability, operability, and security.
- Prefer explicit behavior over magic/implicit side effects.
- Make decisions reversible and document tradeoffs.
- Deliver in small vertical slices with testable outcomes.

## 2) Architecture & System Design (MUST)
- Enforce clean/hexagonal boundaries; dependencies point inward.
- Domain/Application MUST remain framework/provider agnostic.
- External systems MUST be isolated behind ports/adapters.
- Use ADRs for consequential architecture decisions.
- Prefer bounded contexts and clear ownership of modules.

## 3) Application Patterns (SHOULD)
- Use CQRS for non-trivial workflows and evolvable business domains.
- Use MediatR (or equivalent mediator) when orchestration complexity benefits.
- Use UnitOfWork + repository abstractions for transactional consistency.
- Commands SHOULD be idempotent when retries/distributed execution are possible.
- Prefer composition over inheritance unless inheritance is the clearest model.

## 4) Clean Code & SOLID (MUST)
- SRP, DIP, ISP MUST guide module/interface design.
- Naming MUST be intention-revealing and domain-driven.
- Keep methods/classes cohesive; avoid god objects and anemic wrappers.
- Minimize mutable shared state and hidden temporal coupling.
- Remove dead code and avoid “TODO-driven architecture.”

## 5) API & Contract Governance (MUST)
- Contracts MUST be explicit, typed, and version-safe.
- Use RFC7807/ProblemDetails-compatible error contracts.
- Validate all boundary inputs (request, message, config, env).
- Breaking changes MUST include migration notes and compatibility strategy.
- Critical operations SHOULD support idempotency keys.

## 6) Persistence & Data Integrity (MUST)
- Schema evolution MUST be migration-driven and reviewable.
- Avoid hard-delete for core entities unless explicitly approved.
- Enforce tenant isolation and authorization in queries/commands.
- Business invariants MUST be enforced in Domain/Application, not only DB.
- Provider-specific types MUST NOT leak into business contracts.

## 7) Security Baseline (MUST)
- Never commit secrets, tokens, private keys, or production credentials.
- Use least privilege for all identities (human, service, pipeline).
- Centralize authN/authZ checks; deny-by-default for protected resources.
- Sanitize logs and errors to prevent data leakage.
- Protect against OWASP Top 10 classes:
  - injection, broken auth, sensitive data exposure, SSRF, insecure deserialization, etc.
- Validate and encode untrusted input/output contexts (API, HTML, SQL, URLs).
- External persistence providers (managed databases, BaaS, schema-changing APIs) MUST require explicit user authorization for every write/DDL/secret/deploy operation. Read operations may run freely.
- Before any authorized write to an external provider: show the exact SQL/command that will execute and request authorization. Do not run writes as side effects of "cleanup" steps.
- Never instantiate ad-hoc HTTP clients in code paths that talk to external providers; use a centralized client factory with timeouts, retries, and resilience policies.

## 8) Secure Development Lifecycle (SHOULD)
- Threat-model high-impact features before implementation.
- Use dependency scanning and patch known critical/high vulnerabilities promptly.
- Prefer secure defaults in config (TLS, strict cookies, token expiry/rotation).
- Use key rotation and secret revocation strategy for long-lived credentials.

## 9) Testing Methodology (MUST)
- TDD-first SHOULD be used for business-critical behavior.
- Test pyramid:
  - fast unit tests for domain/application rules
  - focused integration tests for adapters/persistence/contracts
  - selective end-to-end for critical user journeys
- Tests MUST be deterministic, isolated, and stable in CI.
- Regressions MUST include a reproducing test.
- Entity end-to-end slices MUST include deterministic tests for the full supported lifecycle, not only the happy-path create/read case. When an entity exposes lifecycle operations, tests MUST cover create, get by id, list one/many, filtering/search, update, deactivate/archive, reactivate, delete only when delete is a real product operation, tenant/ownership boundaries, authorization rules, validation failures, and relevant business invariants.
- If an entity intentionally omits a lifecycle operation (for example, archive instead of hard delete), tests MUST assert the real contract and the omission MUST be clear in the spec or review notes.

## 10) Coverage Policy (MUST)
- 100% global coverage is not a blanket requirement.
- Critical logic (domain invariants, auth, money/data integrity, tenancy) SHOULD be near-complete.
- Coverage MUST trend upward over time for core modules.
- Untested critical paths require explicit justification in review artifacts.

## 11) Observability & Runtime Reliability (MUST)
- Add structured logs with correlation/trace identifiers.
- Expose health checks for critical services and dependencies.
- Define timeouts, retries with backoff, and cancellation boundaries.
- Fail fast on invalid startup configuration.
- Instrument critical paths with metrics/traces.

## 12) Performance & Scalability (SHOULD)
- Measure before optimizing; use profiling and realistic benchmarks.
- Avoid unnecessary allocations/chatty IO and N+1 query patterns.
- Use pagination/streaming for large result sets.
- Cache only when justified, with explicit invalidation semantics.

## 13) CI/CD Quality Gates (MUST)
- Required gates: build, tests, lint/static analysis, security/dependency scan.
- Block merge on failed mandatory gates.
- Release artifacts SHOULD be reproducible and traceable to commit + version.

## 14) Documentation & Knowledge Management (MUST)
- Update docs/specs/contracts when behavior changes.
- Keep architecture diagrams/ADRs synchronized with code reality.
- Capture important decisions, incidents, and postmortems in reusable artifacts.

## 15) Delivery Discipline & Review (MUST)
- Keep PRs small, focused, and reviewable.
- Include “why” and impact/risk in change descriptions.
- Use checklists for security/testing/rollback readiness on impactful changes.
- Apply boy-scout rule: leave touched code cleaner than found.
- Before staging anything, list every file that will be included with a one-line description of what changed in each.
- Propose a commit message and show the full staged diff summary before asking permission. Never commit silently.
- Never push silently. Never use `--no-verify`, `--force`, or `--force-with-lease` without explicit user instruction.
- One logical change per commit. If multiple unrelated changes are staged, ask whether to split them.
- Commit messages in imperative present tense, focused on "why" rather than "what".
- If a pre-commit hook fails, report the failure and ask how to proceed. Never bypass the hook.

## 16) Reuse-First Discipline (MUST)
- Before creating a helper, mapper, validator, utility, service, adapter, dialog, button variant, filter panel, form shell, table shell, modal, loader, status badge, or notification primitive: search for an existing reusable abstraction.
- Extend the existing primitive if its shape is close. Only create a new component if there is a real gap.
- Never introduce parallel utility/UI ecosystems (two filter panels, two confirmation dialogs, two HTTP wrappers, two key generators, two date-range validators).
- When fixing a partially broken feature that already gestures toward a shared pattern: stabilize the shared primitive first, then refactor the feature to consume it. A broken local implementation is not a license to bypass shared architecture.

## 17) Contract Stability & Versioning (SHOULD)
- Stable contracts come before convenience. Breaking changes to external-facing contracts (HTTP endpoints, message schemas, public function signatures) MUST include a migration note and a compatibility window.
- Design contracts to serve multiple clients (web, mobile, third parties), not the immediate consumer only.
- Avoid overfitting an API to a single UI implementation; durable names and stable shapes outlast any specific client.
- When a contract changes, coordinate frontend and backend changes in the same delivery window.

## 18) Mock-First Development (SHOULD)
- A missing backend MUST NOT block frontend implementation. Define typed contracts, port/repository interfaces, and a mock implementation; build the full feature against that boundary.
- Components MUST consume services/facades/gateways/repositories — never the network transport directly. Mock and real implementations expose the same public contract.
- Mark mock-backed implementations explicitly in their file name or registration. Document why mock mode exists and which real integration replaces it.
- Do not place temporary direct infrastructure logic inside presentational components as a substitute for a repository boundary.
