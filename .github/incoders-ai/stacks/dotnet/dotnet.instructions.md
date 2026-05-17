# .NET Instructions

## Purpose
Standards for .NET backends in this ecosystem.

## Scope
.NET 8+ projects.

## Load Conditions
Project manifest declares a `stacks/dotnet` layer.

## Do Not Load When
Non-.NET backends.

## Rules
- MUST use dependency injection intentionally; register services through capability-named DI modules (`AddApplication`, `AddInfrastructure`, `AddPersistence`, `AddAuthentication`, `AddObservability`, `AddApiPresentation` or equivalents).
- MUST NOT create giant `Program.cs`/`Startup.cs` files that mix presentation, application, infrastructure, persistence, auth, and observability registrations.
- MUST keep Application abstractions independent from Infrastructure implementations.
- MUST prefer async APIs for IO; avoid sync-over-async.
- MUST use FluentValidation (or equivalent) at the application boundary; do not scatter manual guard code through controllers.
- MUST centralize exception-to-response mapping (e.g. ProblemDetails middleware/exception handlers).
- MUST NOT inject `DbContext` directly into API/Application handlers.
- MUST keep request/response DTOs separate from domain entities. Map at the boundary.
- SHOULD use MediatR (or equivalent mediator) when orchestration complexity benefits from CQRS slices.
- SHOULD use `IUnitOfWork` + repository abstractions for transactional consistency.
- MUST default endpoints to bearer-auth-required and opt out explicitly for public endpoints.
- SHOULD use policy-based authorization with explicit naming.
- MUST keep persistence concerns inside Infrastructure (see `stacks/aspnet/persistence.instructions.md`).
- MUST use `IHttpClientFactory` for all outbound HTTP. No ad-hoc `new HttpClient(...)`.

## Output Expectations
- Composition root reads as a list of capability registrations.
- No business logic inside controllers/endpoints.
- All outbound HTTP shares one factory and one resilience policy surface.
