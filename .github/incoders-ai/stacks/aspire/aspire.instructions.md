# .NET Aspire Instructions

## Purpose
Standards for using .NET Aspire as the local/dev orchestration layer.

## Scope
Projects with an Aspire AppHost and service composition.

## Load Conditions
Project manifest declares a `stacks/aspire` layer.

## Do Not Load When
Non-Aspire .NET projects or non-.NET projects.

## Rules
- MUST use Aspire as local/dev orchestration for API, frontend, workers, databases, and observability resources.
- MUST NOT assume the frontend is Blazor; Next.js, Angular, or other npm apps MAY be orchestrated as external resources.
- AppHost wires environment and resources; business logic stays in services.
- MUST keep AppHost registrations modular and readable; do not let AppHost become a wall of resource declarations mixing unrelated concerns.
- SHOULD propagate correlation/trace IDs through Aspire's observability surface and into outbound calls (see `core/observability.rules.md`).
- MUST NOT bind production deployment topology to AppHost specifics; AppHost is for local/dev orchestration.

## Output Expectations
- A new resource (database, queue, service) is added through one Aspire registration.
- The orchestrated stack starts and stops reliably for any developer on the team.
- Production deployment artifacts do not depend on AppHost.
