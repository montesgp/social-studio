# SDD - Incoders Social

## 1. System Style
Workflow-oriented, event-aware, AI-native SaaS module inside a monorepo orchestrated by .NET Aspire.

## 2. Architecture
- Frontend: Next.js app.
- Backend API: ASP.NET Core using hexagonal architecture.
- AppHost: .NET Aspire orchestration.
- Infrastructure: Supabase initial adapter; other providers must be replaceable.
- External providers: content data, AI models, design, publishing, testing.

## 3. Monorepo Layout
```txt
apps/
  web/                          # Next.js frontend
Incoders.Hive.AppHost/          # .NET Aspire orchestrator
Incoders.Hive.ApiService/       # ASP.NET Core API (transport / HTTP mapping)
Incoders.Hive.Application/      # Use cases, ports, commands/queries
Incoders.Hive.Domain/           # Entities, value objects, domain events
Incoders.Hive.Infrastructure/   # Adapters (Supabase, AI providers, etc.)
Incoders.Hive.ServiceDefaults/  # Shared Aspire service defaults
Incoders.Hive.Tests/            # Unit + integration tests
docs/
openspec/
```

> Projects sit at the repo root (flat layout), not under `src/`/`tests/`. The solution file is `Incoders.Hive.slnx`.

## 4. Backend Hexagonal Layers
- Domain: workflow entities, value objects, domain events.
- Application: use cases, ports, commands, queries.
- Infrastructure: Supabase, AI providers, data providers, Canva, Blotato, TestSprite integrations.
- Api: endpoints, auth, HTTP mapping.

## 5. Bounded Contexts
- Identity & Tenancy
- Client Management
- Social Account Management
- Content Research
- Insight Mapping
- AI Idea Generation
- AI Evaluation
- Asset/Canvas Creation
- Publishing
- Analytics & Feedback
- Provider Configuration
- Integrations (platform catalog + organization credentials)

## 6. Workflow Events
- ClientSelected
- ContentResearchRequested
- ReferenceContentRetrieved
- ContentMapped
- IdeaGenerationRequested
- IdeasGenerated
- IdeaEvaluationRequested
- IdeaEvaluated
- CanvasDraftCreated
- PublicationRequested
- PublicationPublished
- MetricsSnapshotCaptured

## 7. AI Orchestration
Model routing is configurable. Use cases call application ports such as `IAiIdeaGenerator`, `IAiIdeaEvaluator`, `ITranscriptionMapper`. Infrastructure resolves provider/model.

## 8. Persistence
Supabase is initial infrastructure provider. Domain and Application must not depend on Supabase SDKs. RLS must support organization/client boundaries.

Client-managed image assets (for example brand manual logos and backgrounds) are uploaded through backend endpoints and stored behind a storage adapter abstraction. Persisted business records keep the resulting URL reference, never raw file payloads.

API startup applies pending EF Core migrations before hosted background workers process schema-dependent jobs. Developer fast-startup shortcuts may skip data seeding, but must not skip migrations because content generation workers rely on tables such as `social.content_generation_format_assets`.

Persistence schemas:
- `identity` for authentication/authorization data.
- `platform` for organizations, memberships, system settings and shared provider/model catalogs.
- `social` for Hive Social / Incoders Social module data.

Organization-owned product tables must include `organization_id`. Shared system catalogs such as AI providers and AI models are platform-owned; organization-specific credentials, availability and preferences reference those platform records.

Integrations persistence model:
- `platform.integration_providers` is global catalog data (provider metadata, frontend icon keys, categories, credential schema, supported connection modes).
- `platform.organization_integrations` is organization-scoped provider state (mode/status/default model flags).
- `platform.organization_integration_credentials` stores encrypted organization credential values.
- Product modules such as Social consume integration resolution through an abstraction (`IIntegrationResolver`) instead of reading provider credentials or appsettings directly.

Social network catalog model:
- Platform social networks store an `icon_key` selected from the frontend social-network icon allowlist, not arbitrary icon text, so admin users can preview the assigned icon and downstream UI can render it consistently.

Platform configuration model:
- Email templates for authentication/system notifications are platform-owned settings stored as typed JSON in `platform.system_settings` under stable `email_templates.*` keys. Sysadmins edit HTML bodies with preview; runtime mail delivery will resolve the same keys.
- API startup seeds branded default templates for confirmation, magic-link, password reset, and invitation flows, without overwriting already configured templates.
- Platform integrations that are not organization/content integrations, such as SMTP email delivery and future WhatsApp messaging, are platform-owned settings stored under stable `platform_integrations.*` keys. Secret values must be protected before persistence and returned to UI only as configured/not-configured flags.
- Workspace module availability is configured by sysadmins per `(organization_id, user_id)` mapping in `platform.organization_user_modules`, with explicit `sort_order` for selector ordering. Organization-scoped users can belong to only one organization at a time; changing the assigned organization removes previous memberships and module assignments. Platform sysadmins have no organization membership and see the full module catalog ordered by module creation date descending.

## 9. Frontend Design
Use Next.js App Router. Build a workflow dashboard around client selection, content research, insight mapping, idea generation/evaluation and publication pipeline. Use shadcn/ui and Radix primitives for accessible components.

## 10. Testing
- Unit tests for domain/application rules.
- Integration tests for provider adapters.
- API contract tests for frontend/backend sync.
- Optional TestSprite validation for E2E workflow journeys.
