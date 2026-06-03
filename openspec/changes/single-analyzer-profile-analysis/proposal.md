# Proposal: Single-Analyzer Profile Analysis ("Mundo 2")

## Change ID
`single-analyzer-profile-analysis`

## Summary
Add a second mode ("Mundo 2") to the single-analyzer tool: **Instagram profile analysis**. A marketer either (a) selects an existing client plus one of that client's OWN associated Instagram accounts, or (b) pastes an Instagram profile URL. The system scrapes the FULL profile via Apify (with automatic backup-actor fallback), runs the organization's default AI agent to produce an **expert marketing analysis**, persists a new first-class `ProfileAnalysis` aggregate, renders a dynamic modern dashboard with charts, and lets the user optionally IMPUTE detected fields into the client's brand manual / content plan / general strategy. The existing video→idea→content-package flow ("Mundo 1") stays UNTOUCHED.

## Problem
The single-analyzer today analyzes ONE piece of content (a video URL) to seed content generation. There is no way to analyze an entire account's posture: its audience size, posting cadence, engagement, content mix, and how all of that compares to a client's stated objectives. Marketers — especially when onboarding a NEW client whose brand manual and content plan are still empty — need a fast, expert read of a real Instagram profile that (1) summarizes the account as a marketing analyst would, (2) visualizes the underlying post dataset, and (3) can seed the client's ABM (brand manual, content plan, general strategy) with confirmed, per-field values. None of this exists; the current pipeline is single-video-shaped end to end (scrape contract, AI prompt, persisted record, and the 3360-line frontend monolith).

## Goals
- Add a top-level mode switch in the single-analyzer (Contenido | Perfil) that routes to a NEW, separate Profile Analyzer frontend module — Mundo 1's monolith is not extended.
- Support two profile inputs: client + own-account picker (filtered by Instagram `SocialNetworkId`; user picks when a client has multiple IG own accounts) OR a pasted profile URL.
- Scrape the full Instagram profile via the org-configured Apify actor, with AUTOMATIC in-run fallback to a backup actor when the default fails.
- Run the organization's DEFAULT AI agent (`IDefaultAiSelector` + `IAiCompletionGateway`) under a NEW AI flow key (`profile-analyzer`) with its own prompt version to produce an expert marketing analysis.
- Persist a NEW first-class `ProfileAnalysis` aggregate (own table + EF migration) storing BOTH server-derived stats AND the raw posts dataset.
- Render a dynamic, modern dashboard: AI narrative + charts (recharts via the shadcn/ui chart pattern) + objective comparison when a client is present.
- Let the user IMPUTE detected fields PER-FIELD with confirmation into existing write commands (brand manual, content plan, general strategy).
- Emit structured workflow logging/diagnostics across the profile pipeline, equivalent to the content flow (support/error review).
- Keep the scrape port/adapter network-agnostic so future networks can be added without rework.
- Respect strict hexagonal boundaries and pass all MUST gates.

## Out of Scope
- **Mundo 1 (existing single-content video→idea→content-package flow) — explicitly UNTOUCHED.** No changes to `AnalyzeSingleContentHandler`, `SingleContentAnalyzer`, the single-video scrape variant, `single_content_analyses`, or the existing `SocialContentAnalysisProfile` strategy.
- Networks other than Instagram (TikTok, YouTube, etc.) — port/adapter stays network-agnostic, but only Instagram is wired now.
- Bulk/scheduled profile analysis, profile-to-profile comparison, and historical trend tracking across runs.
- New write commands for impute — impute maps ONLY to EXISTING brand-manual / content-plan / general-strategy write paths.
- Decrypting or auto-provisioning the org's `actor_mappings_json` — the change DOCUMENTS the exact required entries; org operators configure them.
- Authentication/authorization model changes.

## Scope Boundaries
- New work lives in a NEW backend feature namespace (NOT `Application/Features/Social/SingleAnalyzer/Profile/`, which already holds the unrelated `SocialContentAnalysisProfile` strategy) and a NEW frontend module/route tree.
- Profile scraping uses a DEDICATED `variantKey` (`profile`) under the existing `instagram` `networkKey` to avoid colliding with the single-video Instagram scrape, which already uses `networkKey "instagram"`.
- Provider-specific concerns (Apify, EF/Npgsql, Supabase, AspNetCore) stay behind ports/adapters in Infrastructure/Api; the AI model stays a configurable port.
- The org's `actor_mappings_json` is Data-Protection-encrypted and not readable from the DB; the EXACT required mapping entries (default + backup, `networkKey "instagram"`, `variantKey "profile"`) are documented as part of this change.

## Approach (High Level)

### 1. Profile scrape capability (Infrastructure)
Add an Instagram **profile** extraction variant distinct from the single-video path:
- A dedicated platform adapter for profile scraping (builds actor input: `profiles[]`, `maxPostsPerProfile`, and a MANDATORY `proxyConfiguration` with `apifyProxyGroups: ["RESIDENTIAL"]` — Instagram blocks datacenter IPs).
- Routing by `variantKey "profile"` (not by reusing `PlatformKey`), so the same `instagram` network routes single-video vs profile to different actors via the existing `actor_mappings_json` mechanism — no schema change for routing.
- **Automatic in-run actor fallback** (NEW capability): on default-actor failure, retry with the configured backup actor. The current pipeline has NO fallback; this is added behind the scrape port and applies to the profile variant.
- Output parsing nuances (from the live probe): the default actor returns an ARRAY of 3 items `[profile, quality_report, bandwidth_report]` → select by `resultType == "profile"`, NOT index `[0]`. Map profile fields + `latestPosts[]` + `latestReels[]` + pre-computed aggregates. Filter `latestPosts` by `ownerId == instagramId` for owner-only metrics (the array can include tagged/coauthored non-owner posts). Annotate/handle `_degraded` posts. `latestReels[]` shape is unconfirmed (verify at apply) — mirror `latestPosts`.

### 2. ProfileAnalysis aggregate + persistence (Domain + Infrastructure)
New first-class `ProfileAnalysis` aggregate in a distinct domain namespace with its own table and EF migration (`social` schema, `organization_id` scoped, optional `client_id` / `own_account_id` soft references). The persisted record is HYBRID: it stores BOTH the server-derived stats (engagement, cadence, content-mix breakdowns) AND the raw posts dataset, so the dashboard can re-derive and chart without re-scraping. Domain stays pure (no EF/Npgsql/Supabase types); EF mappings and JSON columns live only in Infrastructure.

### 3. AI marketing analysis (Application)
A new application flow under AI flow key `profile-analyzer` (its own `PromptVersionKey`) that assembles the scraped profile context, calls `IDefaultAiSelector` + `IAiCompletionGateway` to produce a structured expert marketing analysis (narrative + structured insights suitable for impute), and persists it into the `ProfileAnalysis` record. The AI model remains a configurable port — no hardcoded model.

### 4. Dashboard + impute (Api + Frontend)
- New API endpoints to start a profile analysis, poll/fetch it, and execute per-field impute against EXISTING brand-manual / content-plan / general-strategy write commands.
- New frontend module/route tree with a top-level mode switch (Contenido | Perfil). The Profile Analyzer renders the AI narrative, charts the post dataset (recharts — NEW dependency via the shadcn/ui chart pattern; no chart lib exists today), and shows an objective-comparison panel computed at render by crossing the analysis with the client's `ContentPlan` / `GeneralStrategy`. The comparison is SKIPPED for the URL-only path (no client context).
- Impute UI: per-field, each with explicit confirmation, mapping to existing write commands — brand manual (ColorPalette, Typography, BackgroundTemplates, Logos, VoiceAndTone), content plan (ObjectiveText, ObjectiveKpis, ContentStrategyText, Slots), general strategy (ContentPillars, ToneKeywords, TargetAudience, PostingFrequency).

### 5. Diagnostics + diagram (Cross-cutting + Docs)
- Structured workflow logging/diagnostics across the profile pipeline, REUSING the existing `ProviderDiagnosticsJson` + `hive-api.log` patterns so support can review each run's provider activity and errors — equivalent to Mundo 1.
- An Excalidraw conceptual workflow diagram under `docs/architecture/diagrams/`, mirroring the existing `content-plan-flow` pair (committed `.excalidraw` source + a Mermaid-rendered `.md`).

## Affected and Created Capabilities / Specs

| Spec (`openspec/specs/`) | Effect | Why |
|---|---|---|
| NEW `profile-analysis/spec.md` | Created | The profile-analysis capability has no home today; defines inputs, scrape contract, AI flow, persisted aggregate, dashboard, impute, and acceptance criteria. |
| `integrations/spec.md` | Touched (delta) | Documents the required `actor_mappings_json` entries (default + backup, `networkKey "instagram"`, `variantKey "profile"`) and the automatic actor-fallback behavior. |
| `content-research/spec.md` | Referenced | Profile scrape reuses the same Apify resolution mechanism; cross-reference, no behavior change to existing variants. |
| `ai-orchestration/spec.md` | Referenced | New `profile-analyzer` flow key + prompt version follows the configurable-model port contract. |

## New vs Changed Components per Layer

| Layer | New | Changed |
|---|---|---|
| Domain | `ProfileAnalysis` aggregate (+ value objects for stats / post dataset) in a NEW namespace | — (Mundo 1 domain untouched) |
| Application | New profile-analysis feature (start/run/fetch + impute orchestration), `profile-analyzer` AI flow + prompt version, fallback-aware scrape orchestration | — (existing `SingleContentAnalyzer` untouched) |
| Infrastructure | Instagram **profile** scrape adapter, `variantKey "profile"` routing, AUTOMATIC backup-actor fallback in the scrape port, `ProfileAnalysis` EF mapping + migration, profile diagnostics wiring | Apify extractor gains fallback path (additive; single-video path unchanged) |
| Api | Profile-analysis endpoints (start/poll/fetch) + per-field impute endpoints | — |
| Frontend (`apps/web`) | NEW Profile Analyzer module/route tree, mode switch (Contenido \| Perfil), own-account picker, recharts charts, impute UI | Mount point that hosts the mode switch (minimal; Mundo 1 component not edited) |
| Docs | Excalidraw + Mermaid profile-flow diagram, ADR(s), `actor_mappings_json` config note | PRD/SDD/spec sync per the docs-sync rule |
| Deps | `recharts` (frontend charting) | — |

## PRD Traceability
- **PRD `docs/PRD.md` §6 (MVP Success Path), step 2** — "User chooses content source strategy: niche, reference accounts, **own accounts**, or manual URL list." Profile analysis operationalizes the "own accounts" and "manual URL" sources for account-level analysis.
- **PRD §9 (Acceptance Criteria)** — extends the single-content analyzer criteria to account-level analysis ("A CM can create/select a client and run a … workflow"; "Each organization can configure its own third-party integration credentials … without relying only on appsettings" — covered by the documented `actor_mappings_json` entries; "The frontend shows workflow state and errors clearly" — covered by the diagnostics deliverable).
- **PRD §8 (Core Entities)** — introduces `ProfileAnalysis` as a new persisted artifact alongside `ContentInsight`/`ContentSource`; consumes `Client`, `SocialAccount` (own accounts), `OrganizationIntegration(Credential)`, `AiProvider`/`AiModel`, `PromptVersion`.
- **SDD `docs/SDD.md` §5 (Bounded Contexts: Content Research, Insight Mapping, Integrations, Provider Configuration), §7 (AI Orchestration — configurable model ports), §8 (Persistence — `social` schema, `organization_id` scoping).**

## ADR Decision
**An ADR IS REQUIRED** (`require_adr_for_architecture_changes: true`). This change introduces architecture-level decisions:

1. **New `ProfileAnalysis` first-class aggregate** (new table + migration, distinct namespace) — a new persisted artifact and domain boundary.
2. **Profile-scrape routing + automatic actor fallback** — a dedicated `variantKey "profile"` routing seam plus a NEW in-run fallback capability behind the scrape port (the pipeline has none today).
3. **Frontend charting dependency (`recharts`)** — first charting library in `apps/web`; record the choice and the shadcn/ui chart pattern rationale.

Proposed location: `docs/architecture/adr/0003-profile-analysis-model.md` (next sequence after `0002`), following the existing ADR format (Status / Context / Decision / Consequences / Related). The ADR is authored in the design phase; this proposal records that it is required and what it must cover.

## Risks
- **Actor-mapping mismatch (HIGH).** `actor_mappings_json` is encrypted and unreadable from the DB; if the `variantKey "profile"` entries are missing/misconfigured, the profile scrape silently routes wrong or fails. Mitigation: document exact required entries + automatic fallback + clear diagnostics.
- **Routing collision (MEDIUM).** Single-video Instagram scrape already uses `networkKey "instagram"`. The new `variantKey "profile"` MUST be enforced end to end or it will hit the wrong actor. Mitigation: dedicated variant key, covered by spec + tests.
- **`latestReels[]` shape unconfirmed (MEDIUM).** The probe account had no reels; reel mapping is assumed to mirror `latestPosts`. Mitigation: verify against a reel-heavy account at apply; degrade gracefully.
- **Owner-filtering correctness (MEDIUM).** `latestPosts` can include tagged/coauthored non-owner posts; owner metrics must filter by `ownerId == instagramId`. Wrong filtering skews engagement/cadence stats.
- **Proxy requirement (MEDIUM).** RESIDENTIAL proxy is mandatory; missing it → immediate IG block. Mitigation: enforce in adapter input, assert in tests.
- **Impute data quality (MEDIUM).** AI-detected fields written into a client's ABM could pollute real data. Mitigation: per-field explicit confirmation; never auto-apply; provenance preserved.
- **Frontend dependency + bundle (LOW/MEDIUM).** `recharts` is a new dependency and bundle weight; isolate to the Profile Analyzer module (lazy-load) so Mundo 1 is unaffected.
- **Scope creep into Mundo 1 (LOW, high blast radius).** Any edit to the 3360-line monolith or the single-video pipeline risks regressions. Mitigation: hard out-of-scope boundary; new module/namespace only.

## Review Workload Note
This is a **LARGE, multi-layer change** (Domain aggregate + migration, new Application flow + AI prompt, Infrastructure adapter + fallback + diagnostics, Api endpoints, a new frontend module with charts and impute UI, plus an ADR and a diagram). It will almost certainly exceed a single ~400-line PR budget. **Expect chained/stacked PRs**, suggested slicing:

1. Domain `ProfileAnalysis` + EF migration + spec skeleton.
2. Infrastructure profile scrape adapter + `variantKey "profile"` routing + automatic fallback + diagnostics.
3. Application `profile-analyzer` AI flow + prompt version + persistence wiring.
4. Api endpoints (start/poll/fetch) + per-field impute.
5. Frontend Profile Analyzer module + mode switch + recharts dashboard + impute UI.
6. Docs: ADR `0003` + Excalidraw/Mermaid diagram + `actor_mappings_json` config note (can fold into the slice that introduces each).

Rough size sense: well above the 400-line budget; the `sdd-tasks` Review Workload Forecast should flag chained PRs and a high budget risk. The frontend slice (5) is the heaviest single unit.

## Traceability
- PRD: `docs/PRD.md` §6, §8, §9.
- SDD: `docs/SDD.md` §5, §7, §8, §9.
- Governance: `docs/ai-governance.md` (configurable model selection + traceability).
- Exploration: engram `sdd/single-analyzer-profile-analysis/explore` (obs #511).
- Apify probe: engram `sdd/single-analyzer-profile-analysis/apify-probe` (obs #513).
- Kickoff decisions: engram `sdd/single-analyzer-profile-analysis/kickoff-decisions` (obs #512).
