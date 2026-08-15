# Exploration — niche-discovery-engine

**Phase**: sdd-explore | **Date**: 2026-08-14 | **Artifact store**: openspec (+ Engram `sdd/niche-discovery-engine/explore`)

## 1. Current state — verified

### 1.1 Social module map

`Incoders.Hive.Application/Features/Social/*` is organized by capability folder, each with Command/Query + Handler pairs (CQRS, no MediatR — direct handler classes). Existing capabilities: ClientReferentAccounts, ClientCompetitorAccounts, ClientOwnAccounts, ClientBrandManuals, Clients, Networks, ContentGeneration (Formats/Orchestration/Visual/Ideas/Discovery), Workflow (generator pipeline orchestration: `ContentWorkflowCatalog`/`Contracts`/`Handlers`/`Requests`/`Validators`), SingleAnalyzer (Analyze/History/Profile), ProfileAnalyzer, DiscoveryBankIntelligence (`AskDiscoveryBankQuestionQuery`, `ListDiscoveryBankQuery`, `DiscoveryBankQueryExecutor`/`PlanValidator` — an LLM-over-structured-data Q&A tool), ContentPlans, ContentStudio, GeneralStrategies, Niches.

Layering: `Incoders.Hive.Domain/Social/{Capability}/*` (aggregates, value objects, `Errors` static classes) ← `Incoders.Hive.Application/{Abstractions,Features}/Social/*` (ports in Abstractions, use-cases in Features) ← `Incoders.Hive.Infrastructure/Social/*` (EF entities/repositories/Postgres, provider adapters under `Social/Providers/{Provider}`) ← `Incoders.Hive.ApiService/Endpoints/Social/Map*Endpoints.cs` (minimal-API mapping, one file per capability).

Naming convention: `{Verb}{Noun}Command/Query` + `{Verb}{Noun}Handler`; `I{Noun}Repository` / `I{Noun}Resolver` / `I{Noun}Provider` ports in Abstractions; `Ef{Noun}Repository` in `Infrastructure/Persistence/Postgres/Repositories`; `{Noun}Errors` static class in Domain using `Error.Validation`/`Error.Failure`, with codes `social.{feature_snake}.{reason_snake}` plus a matching i18n key `social.{feature}.errors.{reason}`.

### 1.2 Existing discovery capability — already shipped (content-discovery-intelligence, apply-complete 2026-08-10)

This is NOT greenfield. A full content-discovery capability already exists end to end and must be reused, not rebuilt:

- **Domain**: `Domain.Social.Niches.Niche` aggregate (platform-level global catalog: `NicheId`, `Code`, `DisplayName`, `IconKey`, `SearchHashtags[]`, `IsActive`) + `INicheRepository`, seeded by `NicheCatalogSeeder` (deterministic GUIDs, upsert-on-missing-only, 5 starter niches). `Domain.Social.DiscoveryBankIntelligence.DiscoveryBankErrors` (validation-only, no aggregate — discovery candidates deliberately have no Domain lifecycle).
- **Application/Abstractions/Social/ContentDiscovery/**: `IContentDiscoveryService.DiscoverAsync(ContentDiscoveryRequest) -> Task<ContentDiscoveryPlan>`; `DiscoveredContent` (Title, Summary, NetworkSource, ContentUrl, ThumbnailUrl, nullable Views/Likes/Comments/Shares/Saves — **null = network does not report, 0 = reported zero, never zero-filled**, PublishedAtUtc, SourceHandle, SourceKey); `ContentDiscoveryRequest(OrgId, ClientId, NetworkSources[], MaxResults, SearchCriterion, ForceRefresh)`; `ContentDiscoveryPlan(Candidates, Shortfall, FailedSourceKeys, UnconfiguredSourceKeys, Origin: Fresh|Bank, BankObservedAtUtc)`; `IContentDiscoverySource{SourceKey, Order, Supports(criterion), DiscoverAsync}`; `SourceDiscoveryResult(Outcome: Ran|Unconfigured|Failed, ...)`; `IContentCandidateRepository`, `IAccountBaselineRepository`, `INicheSearchResolver`, `INicheSearchConfigurationProbe`, `CandidateIdentity.From(network, url)`.
- **Application/Features/Social/ContentGeneration/Discovery/**: `ContentDiscoveryChain` (ordered chain, mirrors `ContentIdeaChain`, IS the registered `IContentDiscoveryService`), `ReferenceAccountDiscoverySource`, `OwnAccountDiscoverySource`, `TikTokNicheSearchSource`, `OpportunityScorer` (pure, I/O-free, availability-aware weighted denominator, `nowUtc` as a parameter for determinism), `TrendingProvenClassifier` (Undetermined/Trending/Proven — single-snapshot proxy, explicitly NOT velocity), `CoOccurrenceSignal` (lexical cross-account repetition proxy, K=3 — explicitly NOT clustering), `AccountBaselineProvider` (read-through TTL cache).
- **Infrastructure**: `Social/Providers/ApifyNicheSearchActorConfiguration.cs` — single source of truth for which network has a working niche-search adapter (`SupportedNetworks` = TikTok only today) and the actor resolution order: (1) enabled workflow-step route from `IWorkflowConfigurationResolver`, (2) fallback to raw `actor_mappings_json` via `ApifyActorMappingLookup.GetAllMappedActors`, (3) empty → `SourceOutcome.Unconfigured`. **Never a literal actor id in code.** Plus `ApifyNicheSearchResolver`, `TikTokNicheSearchAdapter`, `EfContentCandidateRepository`, `EfAccountBaselineRepository`, `NicheCatalogSeeder`, `DiscoverySeedImporter`.
- **Persistence**: EF Core + Postgres, schema `social`, snake_case, `organization_id` tenant boundary FK'd to `TenantData` with cascade delete. Tables `social.discovered_candidates` (append-only observations keyed by `candidate_key`, nullable metric columns, `opportunity_score`, `classification` smallint, `observed_at`/`created_at`, unique constraint making double-write a hard error) and `social.account_baselines` (one row per author per org, TTL staleness via `computed_at`). Migrations under `Incoders.Hive.Infrastructure/Persistence/Postgres/Migrations/`. The `migration-drift` gate runs `dotnet ef migrations has-pending-model-changes` with zero DB access.
- **Actor/step configuration surface** (reusable for any new provider capability): a new `ActorVariantKey` carried by the existing generic `(networkKey, variantKey) -> actor-id` mapping. Discovery is configured as analysis type `content-discovery` (step `discover-niche-content`) nested under the EXISTING `single-analyzer` workflow key — not a new top-level key — so it shares AI-provider config and renders in the settings UI. A workflow key with no checklist group renders "not configurable yet" (`WorkflowConfigurationCatalog.cs`) — a real trap.
- **Frontend**: `apps/web/lib/api/*.ts` thin clients over a shared `apiRequest` helper; UI-decision logic pushed out into pure, unit-tested `apps/web/lib/social/*.ts`; components under `apps/web/components/social/{capability}/`, with `discovery/` already present.
- **AI seam**: `IAiCompletionGateway.CompleteJsonAsync<TResponse>(AiJsonCompletionRequest)` — provider-agnostic structured JSON completion (CapabilityKey + PromptVersionKey routing, optional vision inputs), already used by `DiscoveryBankQueryExecutor` for narration over deterministic query results. This is the reusable seam for the semantic classification/clustering/naming layer.
- **Reference artifacts**: `openspec/changes/content-discovery-intelligence/{exploration,design,tasks}.md` and `docs/architecture/adr/0012-content-discovery-source-chain-and-candidate-persistence.md`. Its `state.yaml` roadmap already lists phase 2 = Instagram/YouTube niche search (deferred) and phase 3 = clustering + patterns table (deferred) — exactly what this change needs.

### 1.3 YouTube reality

No niche/keyword/channel search capability exists for YouTube anywhere. What exists: `YouTubeApifyScrapeAdapter` (Infrastructure/Social/Providers/Apify) — single-URL scrape via an Apify actor, used only by the single-analyzer URL pipeline (`ISocialPlatformScrapeAdapter`). It is not search-shaped and not channel-shaped. `youtube` is not seeded in `SocialNetworkCatalogSeeder`. No YouTube Data API adapter and no Google API credential surface exist (`.github/project-ai/providers/data/` has Apify and data365 only).

### 1.4 Persistence stack

EF Core + Postgres (`AppIdentityDbContext`), schema per bounded context, migrations generated with `dotnet ef migrations add` and committed with designer + snapshot. No Supabase-specific types in Domain/Application. The one in-memory exception is `InMemoryContentWorkflowService` (workflow session state, explicitly not persisted).

### 1.5 Frontend structure

One thin API client file per backend capability under `apps/web/lib/api/`; pure decision logic under `apps/web/lib/social/`; screens under `apps/web/components/social/{capability}/`.

## 2. Architecture boundary risks

1. **DI override trap** — a second registration of a port silently wins `GetRequiredService`. Any NicheDiscovery port needs the established "exactly one implementation" composition test.
2. **Workflow-key-without-checklist-group trap** — a new top-level workflow key with no checklist group renders "not configurable yet" in Settings even when wired.
3. **Actor-id literal leakage** — a repo-wide guard test forbids literal actor ids in Application/Infrastructure. Provider resolution must go exclusively through `IWorkflowConfigurationResolver` / `actor_mappings_json`.
4. **Metric zero-fill trap** — nullable metrics everywhere a network may not report; zero-filling silently penalizes whole networks or markets in scoring.
5. **DTO leakage into the domain** — Apify/YouTube JSON shapes map to normalized records inside `Infrastructure/Social/Providers/**` before crossing into Application. `NicheOpportunity`, `Metric`, `Signal`, `Evidence` must carry zero `JsonElement`/`HttpClient`/EF types.
6. **AI-invents-metrics trap** — `IAiCompletionGateway` is only for classification/clustering/naming/explanation, never numeric aggregation. `OpportunityScorer` (pure, `nowUtc` parameter, availability-aware denominator) is the exact pattern to replicate.
7. **Hardcoded-market trap** — `SourceMarket`/`TargetMarket` are normalized codes routed through a catalog, never an enum member or constant named `Argentina`. Argentina is seed data, exactly like `NicheCatalogSeeder` seeds niches as data.
8. **Taxonomy collision** — the existing flat `Domain.Social.Niches.Niche` versus the requested Niche → SubNiche → ContentPattern → Topic hierarchy. Reuse-and-extend or parallel-hierarchy-cross-referenced is a design decision, not an explore one.

## 3. Naming adaptation (repository-consistent)

- Namespaces: `Domain.Social.NicheDiscovery`, `Application.Abstractions.Social.NicheDiscovery`, `Application.Features.Social.NicheDiscovery.{DiscoveryRuns,Scoring,MarketComparison}`, `Infrastructure.Social.NicheDiscovery` — mirroring the `Social.ContentDiscovery` capability folder shape.
- Ports: `IDiscoveryRunRepository`, `INicheOpportunityRepository`, `IYouTubeChannelSearchResolver` (mirrors `INicheSearchResolver`), a market catalog port, `INicheDiscoveryEvidenceRepository`.
- Endpoints: `Incoders.Hive.ApiService/Endpoints/Social/MapNicheDiscoveryEndpoints.cs`.
- Errors: `Domain.Social.NicheDiscovery.NicheDiscoveryErrors`, codes `social.niche_discovery.{reason}`.
- Forbidden by requirement: `YouTubeNiche`, `IsViableInArgentina()`, `ArgentinaCompetitionScore`. Use `NicheOpportunity`, `EvaluateTargetMarket()`, `TargetMarketCompetition`, `TargetMarketGap`.
- Vocabulary hazard: "Content Discovery" (existing — finds content to generate FROM) versus "Niche Discovery" (new — finds market opportunities to build channels around). Both share the word "discovery"; docs and comments must disambiguate explicitly.

## 4. Provider path options (YouTube data)

- **A. Apify YouTube search/channel actor** — reuses the exact `(networkKey, variantKey) -> actor_mappings_json` resolution built for TikTok. Zero new provider integration, zero new credential surface, same three-outcome contract. Risk: actor response schema unverified without a live spike.
- **B. YouTube Data API v3 direct adapter** — new port, new API-key credential type, new Infrastructure adapter, new provider category in `WorkflowConfigurationCatalog`. Official and stable schema, generous free quota, no scraping ToS exposure. Slower to ship, no precedent in this repo.

## 5. Scope slicing recommendation

1. Contracts only — `NicheOpportunity`, `Metric`/`Signal`/`Evidence`, `DiscoveryRun` shape, repository ports, market catalog seed (Argentina as data).
2. YouTube candidate discovery via the chosen provider path, mirroring the TikTok niche-search source 1:1.
3. Deterministic analytics (pure Application) — median views, outlier ratio and density, growth proxy, cross-channel repetition.
4. Persistence — `DiscoveryRun` + `NicheOpportunity` + evidence tables, one migration, EF repositories.
5. `OpportunityScore` + `ConfidenceScore` composition — pure, configurable weights with renormalization, confidence independent.
6. Market comparison — source traction versus target demand and gap.
7. Semantic layer (AI classification/clustering/naming) behind `IAiCompletionGateway` — additive, does not block the deterministic pipeline.
8. API endpoints + viability dashboard and opportunity detail.

Honest cut: slices 1–5 are the true MVP. Slices 6–7 are valuable but the walking skeleton is demoable without them if the dashboard shows ranked deterministic candidates with raw evidence.

## 6. Open questions for the proposal phase (ranked)

1. Apify YouTube search versus YouTube Data API v3 — different code shape, config surface, and cost/reliability profile.
2. Taxonomy: reuse/extend the existing `Niche` aggregate as the taxonomy root, or introduce a parallel hierarchy cross-referenced by `NicheId`.
3. Where the semantic/clustering layer lands relative to the MVP.
4. The contract boundary for the future NicheOpportunity → ContentPattern → existing Content Generator integration, decided now even if not built now.
5. Review budget versus true slice size — content-discovery-intelligence delivered 2600–3400 lines against a 1500 budget (accepted exception); an 800-line budget here is tighter by roughly half.
