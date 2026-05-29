# Content Research Spec

## Purpose
Content research retrieves candidate high-performing content by client, niche, reference accounts or own account sources.

## Requirements
- Hive Social clients MUST be scoped to an organization with `organization_id`.
- A user MUST select or resolve an organization before creating/listing social clients.
- The system MUST persist clients in the `social` module schema.
- Client lookup/list operations MUST NOT return clients from another organization.
- Sysadmins MAY list clients across organizations and filter the list by organization.
- Sysadmin client list views MUST show the organization each client belongs to.
- Single-content analysis workflows MAY run without a selected client for analysis-only use cases.
- Single-content package generation MUST resolve a client from either the analysis record or the generation request before invoking AI format generation.
- Single-content package generation MUST reject a generation request when the requested client differs from the analysis client.
- Single-content package generation prompts MUST include the resolved client's profile, active brand manual, strategy, current/latest monthly objective and KPIs, active referent accounts, and active competitor accounts.
- Generated visual package assets MUST use explicit client brand palette and typography when present, falling back only for missing brand fields.
- Generated flyer package assets MUST be a single 9:16 story PNG. If the active brand manual has a background template, the flyer MUST use that template; otherwise the system MUST create a branded background from the generated flyer direction and client context.
- Generated flyer package assets MUST apply active brand manual notes that affect visuals, including required signatures or handles.
- When a single-content analysis workflow offers client selection, selectable clients MUST be active and scoped to the resolved organization.
- Client edit forms MUST keep unsaved child collection changes in frontend state until the user saves the client.
- Referent accounts edited in the client form MUST be synchronized as a complete ordered list on client save, not persisted on each row add/edit/reorder action.
- Client form validation failures MUST keep the form/dialog open and mark the invalid field inline without discarding entered data.
- Brand manual typography entry uses MUST offer a common-font picker while still allowing custom font family values.

## Single-Content Analysis Pipeline

The single-analyzer pipeline is a backend-owned, synchronous flow:

### Endpoint
`POST /api/social/single-analyzer/analyze` — authenticated, tenant-scoped.

### Request
```json
{ "url": "<social_content_url>", "clientId": "<optional_guid>" }
```
- `PreferredAiProviderKey` and `PreferredAiModel` are NOT accepted. AI provider selection is always driven by the organization's integration configuration (is_default_ai → most recently configured/updated AiModel integration).

### Response
Returns `SingleContentAnalysisResponse` (200 OK) with:
- `id`, `organizationId`, `userId`, `clientId`, `sourceUrl`, `normalizedUrl`, `platformKey`, `canonicalUrl`
- `status`: one of `Requested | ResolvingContent | ContentResolved | ContentResolvedPartial | Analyzing | Analyzed | Failed`
- `snapshot`: `{ platformKey, canonicalUrl, title, description, author, thumbnailUrl, durationSeconds, metrics, transcript, mediaReferences, rawPayloadReference }`
- `analysis`: `{ summary, detectedHooks, likelyAudience, narrativeStructure, strengths, weaknesses, derivedIdeas, clientAdaptationRecommendations, confidence, warnings }`
- `warnings`, `diagnostics`, `aiProviderKey`, `aiModel`, `createdAtUtc`, `updatedAtUtc`, `completedAtUtc`

### Platform Support
Content extraction uses per-platform Apify adapters. Supported platforms:
- `tiktok` — actor `GdWCkxBtKWOsKjdch`, input shape `{ postURLs, resultsPerPage, shouldDownload*: false }`
- `instagram` — actor `shu8hvrXbJbY3Eb9W`, input shape `{ resultsType: "posts", directUrls, resultsLimit: 1, addParentData: false }`
- Adding a new platform requires only a new adapter class and an `actor_mappings_json` entry.

### AI Provider Selection (per-org)
1. Organization integration marked `is_default_ai = true` + Connected is used first.
2. If none, the most recently configured/updated (`UpdatedAtUtc DESC`) connected AiModel integration is the fallback.
3. If no AiModel integration exists, analysis fails with `AI.NoProviderConfigured`.

### Error HTTP Mapping

| Failure | HTTP | Code |
|---|---|---|
| Missing Apify integration | 422 | `Integration.ApifyNotConfigured` |
| Unsupported platform | 422 | `ContentExtraction.UnsupportedPlatform` |
| No AI provider configured | 422 | `AI.NoProviderConfigured` |
| Invalid URL | 400 | `ContentExtraction.InvalidUrl` |
| Empty Apify response | 422 | `ContentExtraction.EmptyResponse` |
| Provider timeout/rate-limit | 500 (TODO: map to 502/503/504 in follow-up) | `Integration.*` |

### Frontend Integration
- `apps/web/lib/api/single-analyzer.ts` → `analyzeSingleContent(payload, token)` — calls the backend endpoint via `apiRequest`.
- The Next.js route handler `app/api/single-analyzer/scrape/route.ts` has been DELETED. No Apify token is accessible from the browser.
- The "By URL" UI path triggers a single backend call that populates both the snapshot card and the analysis section.
- The "By Search" path remains mock (deferred).

## Events
- TBD

## Acceptance Criteria
- A CM can create a client for the resolved organization.
- A CM can list clients for the resolved organization.
- A client id from another organization returns not found.
- A CM can run single-content analysis with no client selected for analysis-only review.
- A CM must choose an active client before generating a single-content package when the analysis does not already have a client.
- Generated single-content packages use the resolved client's brand, strategy, objectives/KPIs, and account context.
- A CM can paste a TikTok or Instagram URL; the backend scrapes and analyzes it in one call; no Apify request is visible in the browser.
- When no AI integration is configured, the endpoint returns 422 with `code: "AI.NoProviderConfigured"`.
- Tenant isolation: the organizationId is always sourced from the authenticated principal, never from the request body.
