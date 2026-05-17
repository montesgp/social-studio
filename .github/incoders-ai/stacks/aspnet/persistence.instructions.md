# Persistence Instructions

## Purpose
Persistence must remain replaceable; business and application layers do not depend on the storage provider.

## Scope
Backend projects with any form of durable persistence (relational DB, NoSQL, cache, filesystem, blob store, BaaS).

## Load Conditions
Project manifest declares a backend profile.

## Do Not Load When
Pure presentation/UI repos with no persistence concerns.

## Rules
- MUST NOT let Domain or Application depend directly on a specific persistence provider (managed databases, BaaS, PostgreSQL/SQL-vendor features, ORM types, filesystem APIs, cache engines, external storage providers).
- MUST access persistence through repository or port abstractions defined in Application.
- MUST keep ORM/provider-specific code inside Infrastructure adapters.
- MUST keep persistence provider selection configurable; do not bind business logic to a single provider.
- MUST NOT leak provider-specific types (`DbContext`, vendor-specific records, BaaS client objects) into Application contracts.
- MUST treat schema evolution as migration-driven and traceable: cohesive, feature-oriented, readable, including indexes, constraints, policies, triggers, and grants when part of the same feature.
- MUST verify migrations locally before considering a task complete.
- SHOULD design with provider-replaceability in mind even when only one provider is currently in use (in-memory, relational, document, event-sourced, REST-backed, etc.).
- Provider-specific tooling and MCP integrations MAY be used for schema inspection, query validation, migration workflows, and advisors — without leaking architectural coupling into Application or Domain.

## Output Expectations
- Application packages contain zero references to vendor ORM/SDK types.
- Migrations are reviewable, reproducible, and document their intent.
- Replacing the persistence provider would only require touching Infrastructure modules.
