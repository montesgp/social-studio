# Project Rules - Engineering Standards (Social Studio)

> Base reusable standards are defined globally in:
> `.github/incoders-ai/core/best-practice.rules.md`
> This project file can tighten or specialize those rules.

## 1) Architecture & Boundaries (Mandatory)
- Keep hexagonal boundaries strict:
  - Domain: pure business logic (no infrastructure dependencies).
  - Application: use cases, ports, orchestration.
  - Infrastructure: adapters/persistence/providers.
  - Api: transport, auth, HTTP mapping.
- Dependencies must point inward only.
- Do not leak Supabase/EF/provider-specific types into Domain/Application.

## 2) CQRS + MediatR (Default pattern for backend features)
- Use CQRS for feature slices:
  - `Command` for state changes.
  - `Query` for reads.
- Use MediatR handlers for application use cases.
- Add validators (FluentValidation) for commands/queries with input constraints.
- Keep handlers focused and small (orchestration, not heavy domain logic).

## 3) Unit of Work + Repositories
- Application layer uses abstractions (`IUnitOfWork`, repositories, services).
- Infrastructure implements persistence details.
- All mutating command handlers must persist through `IUnitOfWork`.
- Avoid direct `DbContext` usage in Application/Api.

## 4) API Design
- Use ProblemDetails-compatible errors.
- Keep contracts explicit and version-safe.
- Prefer archive/deactivate over physical delete for core business entities unless explicitly safe.

## 5) Clean Code & Design Practices
- Single responsibility per class/file where practical.
- Explicit naming over clever naming.
- Keep methods short and intention-revealing.
- Avoid hidden side effects.
- Prefer composition over inheritance unless justified.

## 6) Testing Strategy (TDD-first where practical)
- Follow strict TDD for backend feature work when tests are feasible.
- Minimum expected tests per slice:
  - Domain rules/unit tests.
  - Application handler tests.
  - Critical API behavior tests.
- Test behavior/contract, not private implementation details.

## 7) Coverage Policy
- 100% coverage for all artifacts is NOT mandatory as a blanket rule.
- Quality target:
  - Critical business rules (Domain/Application): aim for near-complete coverage.
  - Overall backend modules: maintain high and improving coverage trend.
- Any untested critical path must be justified in PR notes.

## 8) Security & Secrets
- Never commit real secrets.
- Use placeholders in tracked config.
- Use local user-secrets/env for real values.
- `.mcp.json` must remain local/ignored.

## 9) Delivery Discipline
- Keep vertical slices small and end-to-end.
- Update docs/contracts when behavior changes.
- Do not introduce speculative architecture without active requirement.
