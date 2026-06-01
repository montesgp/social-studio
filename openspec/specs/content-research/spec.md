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
- Single-content package regeneration MAY include `regenerationObjection` text from the user; when provided, prompt context MUST include that correction and the backend MUST reject values longer than 1000 characters.
- `manifest.json` MUST include `formats.<format_key>.publication.description` for `carousel` and `flyer` when that publication copy is generated.
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
- Before the "By URL" path queues a new analysis, the frontend MUST call the backend URL lookup and warn when the current organization/user already has a saved analysis for the normalized URL.
- The duplicate warning MUST let the user either analyze again, preserving the current new-analysis flow, or resume the saved analysis without re-running extraction/AI.
- Resuming a saved analysis MUST hydrate the existing snapshot/analysis and latest content-generation job so generation actions are available again.
- Analyze-by-URL in the frontend MUST reject unsupported or invalid URL input before calling the backend analyzer endpoint.
- When a saved analysis is resumed, the analyze-by-URL/search input tool MUST be hidden so the user can focus on regeneration actions for that analysis.
- If a user regenerates a format that already has generated content for the loaded analysis, the frontend MUST confirm that the current generated content will be replaced before starting the new generation request.
- When regenerate actions are shown, the frontend MUST provide a "regenerate with objections" path so users can send free-text corrections to the generation pipeline.
- In the Single Analyzer convert panel, the objection text area MUST be hidden by default and shown only after the user chooses a regenerate-with-objections path for already generated content.
- Regenerate controls MUST use one primary regenerate action per target and then let the user choose plain regeneration or regeneration with objections.
- The Single Analyzer generation action panel MUST remain hidden while Client context is "No client context"; format selection and generate/regenerate actions become available only after a client is selected.
- Saved single-content analyses MUST expose a compact tool area with help, save work, and new analysis actions; save work must clear local unsent drafts while preserving the already persisted analysis/generation state, and new analysis must reset the analyzer to its initial input state.
- The leave-without-saving confirmation MUST appear only for local user changes that have not been sent to the API, not merely because a persisted analysis is visible or the user expanded/collapsed UI sections.
- When a saved analysis is opened from history, the Video Analysis section MUST be collapsed by default.
- Long scraped descriptions MAY be collapsed in the snapshot card, but the UI MUST allow the user to expand and collapse the full description.
- Scraped hashtags MUST NOT be repeated as a separate live-data block when they are already present in the video description.
- When the scraped thumbnail is missing or unavailable, the snapshot card MUST show a placeholder image and MUST NOT render the scraped description text block.
- When the scraped title is missing or matches the scraped description, the snapshot card SHOULD prioritize a detected hook as the displayed title.
- In the Single Analyzer convert panel, individual format generation actions (script/carousel/flyer) SHOULD be visually prioritized above the convert-all section.
- The `package.zip` action MUST be shown as a section-level conversion action and MUST package all formats already generated for the loaded analysis (single-format generations and/or convert-all).

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
- A CM pasting a previously analyzed URL is warned before a new provider run and can resume the saved analysis for content generation.
- A CM can intentionally regenerate existing generated formats only after confirming the replacement.
- A CM can regenerate with objection notes and those notes are forwarded to the generation prompt context.
- A CM can regenerate each individual format with objections, not only the regenerate-all path.
- A CM only sees the objections field after selecting a regenerate-with-objections option for content that already exists.
- A CM sees no format or generation actions while "No client context" is selected.
- A CM can leave a persisted analysis without an unsaved-work warning unless they have local notes or changes that have not been sent to the API.
- A CM can save the current historical review state or reset the analyzer to start a new analysis from the saved-analysis tool area.
- A CM can expand and collapse a truncated scraped description from the video snapshot card.
- A CM sees hashtags in the video description only, without a duplicate hashtags block under live metrics.
- A CM sees a placeholder thumbnail (and no description text block) when the scraper does not provide a valid image.
- A CM sees individual format conversion controls before convert-all controls in the convert panel.
- A CM can download `package.zip` from a general conversion action, and that package includes all formats already generated for the analysis.
- A CM cannot submit analyze-by-URL with invalid/unsupported input from the analyzer UI.
- When no AI integration is configured, the endpoint returns 422 with `code: "AI.NoProviderConfigured"`.
- Tenant isolation: the organizationId is always sourced from the authenticated principal, never from the request body.
