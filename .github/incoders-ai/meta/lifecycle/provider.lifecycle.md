# Provider Lifecycle

## Purpose
Manage the full lifecycle of provider modules (infrastructure, AI, testing, design, data, social): create new providers, modify capabilities/configuration, deprecate retired providers.

## Placement Decision
- `.github/incoders-ai/providers/<category>/<provider>/<provider>.instructions.md`.
- Categories in use: `ai`, `data`, `design`, `infrastructure`, `social`, `testing`.

## Create
Trigger: a new external system is being integrated for which no provider module exists.

Required sections (per `core/documentation-standards.instructions.md`):
- Purpose
- Capabilities
- Abstractions (ports the application layer uses)
- Security (credentials, RLS, token handling)
- Configuration (env vars, secrets, allowed regions)
- Failure Modes (rate limits, timeouts, retries)
- Testing (how integration tests are run)

Provider-specific concerns MUST stay behind ports/adapters; provider types MUST NOT leak into Domain or Application.

## Modify
Trigger: capabilities expanded, configuration changed, or security/failure semantics evolved.

Rules:
- Preserve file paths; manifests reference providers by path.
- Provider manifests (`.github/incoders-ai/manifests/provider-<name>.manifest.md`) MUST stay in sync with the provider folder contents — no LOAD lines pointing to non-existent paths.
- Update `.github/project-ai/project.manifest.md`'s `Active Product Providers` section if the project uses this provider.
- Bump `.github/incoders-ai/VERSION.md` (minor for additive, major for breaking).
- Record the change in `CHANGELOG.md`.

## Deprecate
Trigger: provider replaced, removed from the catalog, or no longer supported.

Rules:
- Replace the body with a one-line pointer to the successor or removal date.
- Remove the provider's manifest LOAD entries from any project manifest using it.
- Remove the provider's manifest file (`provider-<name>.manifest.md`) once no consumer references it.
- Keep the folder for one minor cycle before deletion.
- Note the deprecation in `CHANGELOG.md`.
