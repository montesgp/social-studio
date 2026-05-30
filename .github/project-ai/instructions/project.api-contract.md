# Project API Contract

Contracts must support frontend workflow state, async operation statuses, provider errors and idempotency keys.

## Single-Analyzer Endpoint

`POST /api/social/single-analyzer/analyze` — authenticated, tenant-scoped, synchronous (up to 120 s timeout).

Request body: `{ url: string, clientId?: string }`.
- AI provider selection is always org-integration-driven. `preferredAiProviderKey` / `preferredAiModel` are intentionally excluded from the request contract.

Response: `SingleContentAnalysisResponse` — snapshot + AI analysis + status + diagnostics in one envelope.

`GET /api/social/single-analyzer/analyze/lookup?url=<url>` - authenticated, tenant-scoped, user-scoped duplicate pre-flight.

Response body: `{ item: SingleContentAnalysisHistoryItem | null }`.
- The endpoint normalizes the submitted URL with the same domain value object used by analysis creation.
- It returns the newest matching saved analysis for the current organization/user, including latest content-generation summary when present.
- A null `item` means the frontend may continue queueing a new analysis without a duplicate warning.

Error codes follow the `Integration.*` / `ContentExtraction.*` / `AI.*` namespace. Provider timeout and rate-limit currently map to 500; a follow-up will add 502/503/504 mapping.

## Tenant Isolation Rule (all endpoints)

`OrganizationId` is ALWAYS extracted from the authenticated principal via `ICurrentTenant.TenantId` (resolved from the `X-Tenant-Id` header). It is NEVER accepted from the request body.
