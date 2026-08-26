# PRD - Incoders Social

## 1. Product Summary
Incoders Social helps agencies and community managers manage multiple clients, discover high-performing content in a niche, transform references into structured insights, generate publication ideas with AI, evaluate those ideas, produce creative assets, publish through external providers and learn from metrics.

## 2. Problem
Content operators repeatedly research viral content, interpret why it worked, ideate variations, create assets, publish, and report results. This process is manual, inconsistent and difficult to scale across many clients.

## 3. Goals
- Centralize client/account/content operations.
- Automate content research and idea generation.
- Support multi-model AI routing and evaluation.
- Create a repeatable publication workflow from reference content to published asset.
- Track metrics and create a feedback loop for future ideas.

## 4. Non-Goals for MVP
- Full billing system.
- Native editing suite replacing Canva.
- Direct support for every social network at launch.
- Fully autonomous publishing without human review.

## 5. Primary Users
- Agency owner/admin.
- Community manager.
- Content strategist.
- Client viewer/approver.

## 6. MVP Success Path
1. User selects an organization/client.
2. User chooses content source strategy: niche, reference accounts, own accounts, or manual URL list.
3. System retrieves candidate high-performing content through configured data provider.
4. System transcribes/maps content into structured attributes: hook, format, topic, angle, CTA, visual pattern, platform metrics.
5. System sends mapped context to selected/default AI model to generate ideas.
6. System runs evaluation model to score and normalize ideas.
7. User selects idea and creates a canvas/design draft.
8. System sends publication draft to publishing provider.
9. System stores publication status and metrics snapshot.

## 7. Open Questions
- Supported social networks for MVP.
- Whether UGC sales flows are part of MVP.
- Whether creative preview is built internally or delegated to Canva first.
- Exact approval workflow for clients.
- Which content providers are legally/contractually acceptable per platform.

## 8. Core Entities
- Organization
- User
- Client
- SocialAccount
- ContentSource
- ReferenceContent
- ContentInsight
- Idea
- IdeaEvaluation
- CanvasDraft
- Publication
- PublicationProvider
- AiProvider
- AiModel
- IntegrationProvider
- OrganizationIntegration
- OrganizationIntegrationCredential
- PromptVersion
- Campaign
- MetricsSnapshot

## 9. Acceptance Criteria
- A CM can create/select a client and run a content research workflow.
- A sysadmin can temporarily hard-delete a client during MVP cleanup after explicit confirmation; the operation removes client-scoped data and stored assets in cascade.
- A sysadmin can temporarily hard-delete an organization during MVP cleanup after explicit confirmation; the operation removes organization-scoped users, clients, integrations, generated packages, stored assets and all client chains in cascade.
- Organization admins can archive/deactivate clients, but cannot hard-delete them.
- The system can persist retrieved references and AI-generated ideas.
- The system can evaluate ideas with a configurable model.
- The single-content URL analyzer warns before re-analyzing a video already present in the user's analysis history, and lets the user resume the saved analysis for content generation.
- Regenerating an already generated single-content format requires explicit confirmation because it replaces the current generated content for that analysis.
- Each organization can configure its own third-party integration credentials (AI, Canva, Blotato, etc.) without relying only on appsettings.
- The frontend shows workflow state and errors clearly.
- Backend use cases are provider-agnostic.
- **Profile Analyzer (Mundo 2)**: A CM can analyse an entire Instagram account by selecting a client+own-account or pasting a profile URL. The system scrapes the profile, computes engagement metrics, runs AI marketing analysis, and presents a charted dashboard. Detected fields can be optionally imputed into the client's Brand Manual, Content Plan, and General Strategy. The existing single-video flow (Mundo 1) is untouched.

---

## 10. Content Generation — Format Taxonomy

> Added by `content-format-taxonomy`.

The content generation step (step 7 in the MVP Success Path) presents the user with a structured format picker instead of a flat list of format keys.

**Three-button surface picker + layout toggle**: the UI exposes exactly three root options — Script, Story, Feed — each mapped to the `Surface` axis. Story and Feed each have a Single ↔ Carousel layout toggle. Script has no toggle.

The resolved selections map to these five catalog keys:
- Script → `reel-script`
- Story + Single → `story-single`
- Story + Carousel → `story-carousel`
- Feed + Single → `feed-single`
- Feed + Carousel → `feed-carousel`

**No schema migration required**: the `format_key` column remains string-based; the taxonomy lives entirely in the C# catalog. Legacy `story`, `feed-post`, and `carousel` keys in existing rows continue to assemble correctly via the read-path alias layer.

**Attribution safety**: source authors, URLs, channels, referent accounts, and competitor accounts are analysis inspiration/metadata only. Generated deliverables must use target-client/brand attribution, never source-origin signatures.

**Extensibility**: adding a future video format (e.g. Story + Video) requires one catalog row plus a generator/composer registration. No changes to the axis types, catalog API, or derive/normalize layer are needed.

---

## 11. Social Module — Profile Analysis (Mundo 2)

> Added by `single-analyzer-profile-analysis` (2026-06-02).
> Amended by `client-aesthetic-generation-hardening` (2026-08-25) — brand-mark extraction now
> derives and stores a durable asset from analysed reference imagery, not just categorical
> analysis fields. ADR: [`docs/architecture/adr/0014-brand-mark-asset-port.md`](architecture/adr/0014-brand-mark-asset-port.md).
> Amended by `client-aesthetic-visual-sophistication` (2026-08-26, **partial delivery**) —
> extraction now also captures a recurring divider-rule style and an open composition-archetype
> classification, and generated flat-color content renders both (a client-coloured divider glyph;
> archetype-driven text/CTA geometry), falling back byte-identically to today's layout when no
> signal is measured. A third signal, a recurring decorative ornament cropped and rendered from the
> client's own posts, was **scoped but NOT delivered**: its detection schema shipped, but the
> real-world reliability gate for locating it (bounding-box extraction) did not pass with available
> production data, so no ornament ever renders and no owner-visible degrade badge for it exists yet.
> ADR: [`docs/architecture/adr/0015-schema-fatigue-norm.md`](architecture/adr/0015-schema-fatigue-norm.md).
> Gate record: `openspec/changes/client-aesthetic-visual-sophistication/wu6-reliability-gate.md`.

### User Story

As a community manager, I want to analyse an Instagram account (my client's own account or any public account) and receive an expert AI marketing analysis, so I can understand the account's strengths and weaknesses and optionally apply the detected insights to the client's content strategy.

### MVP Acceptance Criteria

- CM can initiate a profile analysis by selecting a client + own Instagram account, or by pasting a profile URL.
- Analysis runs in the background; CM polls for status via a progress indicator.
- On completion, a dashboard shows: engagement metrics, weekday performance charts, content type mix, AI narrative (summary, strengths, weaknesses, opportunities, recommendations).
- The CM can export the visible profile dashboard as a PDF that preserves the on-screen layout, chart positioning, and complete sections deterministically.
- When a client is linked, a comparison panel shows detected account state vs. stated objectives.
- CM can select individual detected fields and impute them into the client's Brand Manual, Content Plan, or General Strategy (per-field, opt-in, confirmed before applying).
- Diagnostics (actor ids, fallback indicator, posts counts, AI provider) are persisted per run.
- Operator must configure two Apify actor entries with `variantKey="profile"` (see `docs/operators/profile-analysis-setup.md`).
- When the detected client aesthetic includes a recurring brand mark, the analysis classifies it as
  a text signature or an icon/symbol. A text mark's literal wording is captured and rendered through
  the client's own typography on generated content. An icon mark's clearest occurrence in an already
  analysed reference image is cropped and stored as a durable asset, then rendered directly on
  generated content instead of an empty coloured placeholder. Either kind gracefully degrades to a
  minimal coloured-chip placeholder — never a broken image and never a generation failure — when the
  mark cannot be reliably classified, located, or cropped.

### Non-Goals

- Scheduling or automating repeated profile analyses.
- Comparing multiple accounts head-to-head.
- Real-time metrics (all data is from a single Apify snapshot).

---

## 12. Content Bank Planner (v1 — Instagram-only publish-now)

> Added by `content-bank-planner` (2026-07-21). ADR: [`docs/architecture/adr/0005-publication-axis-and-two-level-credentials.md`](architecture/adr/0005-publication-axis-and-two-level-credentials.md). Builds on Content Studio (ADR 0004).

### User Story

As a community manager, I want a client-scoped view of every reviewable content item — regardless of which generator or generation run produced it — so I can see everything ready to go out, publish an approved item to the client's Instagram account now, and track its publication status and history in one place.

### The two independent axes

Content moves along two orthogonal lifecycles:

- **Editorial** (Content Studio, ADR 0004): `Draft → InReview → Edited → Approved → ReadyForPublication`, plus `Rejected`/`Discarded`.
- **Publication** (this change, ADR 0005): `Scheduled → Publishing → Published | PublishFailed`, plus `Cancelled`. An item can have zero, one, or many independent publications (one per publish action, one per destination); publishing never mutates the editorial state.

### MVP Acceptance Criteria

- CM can open a per-client **Content Bank**: a grid of all reviewable items across content sets and generator origins for that client, filterable by date range (defaults to last 7 days) and editorial state.
- CM can click "Publish now" on an `Approved`/`ReadyForPublication` item to publish it to a connected Instagram account for that client. Publishing is gated: items in any other editorial state cannot be published (`ItemNotPublishable`).
- CM can view a **Planner** board with lanes for `Ready`/`Scheduled`/`Publishing`/`Published`/`Failed` publications, retry a failed publication, or cancel a scheduled/failed one.
- CM can view a **Published** list of everything successfully published, with a link to the external post.
- Org admins can connect, rotate, and test a client's Instagram account credential (a `ClientAccountConnection`, separate from the org-level `OrganizationIntegration`) before any publish action can succeed.
- Re-publishing the same item to the same destination is allowed and creates a new, independent publication record — history is preserved, never overwritten.
- Content Bank materializes reviewable items lazily on load (no background job in v1) by reusing the existing idempotent Content Studio materialization command.

### Non-Goals (v1)

- Firing scheduled publications automatically — `Scheduled` + a future publish time are modeled, but nothing dispatches them yet (no background worker).
- Networks other than Instagram.
- Post-publish metrics/insights (likes, reach, etc.).
- A hard cap on retry attempts.
- Multiple Instagram accounts connected per client on the same network.

### Deferred Roadmap

| Phase | Item | Status |
|---|---|---|
| 2 | Scheduler/dispatcher worker to fire `Scheduled → Publishing` on `ScheduledForUtc` | Deferred — additive, no model change needed |
| 3 | Additional network connectors (Meta variants, TikTok, YouTube, X) behind `IPublicationConnector` | Deferred — port is channel-agnostic |
| 4 | Background materialization on generation completion (instead of lazy-on-load) | Deferred |
| 5 | Blotato multi-network consolidation | Deferred — native per-network adapters ship first |
| 6 | Published-history recency polish + post-publish metrics/insights | Deferred |

---

## 13. Niche Discovery — Channel-Launch Opportunity Scoring

> Added by `niche-discovery-engine` (2026-08-14). ADR: [`docs/architecture/adr/0013-niche-discovery-multi-origin-provider-and-opportunity-evidence-model.md`](architecture/adr/0013-niche-discovery-multi-origin-provider-and-opportunity-evidence-model.md).

### Why this section exists

§6 (MVP Success Path) starts at *"user selects an organization/client"*. Every other social-module
section in this PRD — Profile Analysis (§11), Content Bank Planner (§12), and Content Discovery
(referenced from ADR-0012) — analyses or discovers content **for an existing client**. Niche
Discovery answers a question that has to be asked **before** a client exists: *"is this niche worth
building a new channel around, and in which market?"* Stretching §1's product summary to cover this
would misrepresent it as a client-facing feature; it is a market-research tool an agency runs to
decide whether to pitch or launch a new channel at all.

### Vocabulary — do not conflate with Content Discovery

**Content Discovery** finds content to generate FROM, for a client that already exists. **Niche
Discovery** (this section) finds market opportunities to build NEW channels around, with no client
in the loop. The two share the word "discovery" and nothing else in the implementation — see
ADR-0013's vocabulary guard.

### User Story

As an agency owner or strategist, I want to run a parameterised discovery over a niche/format/market
combination and see ranked opportunities with independent viability and confidence scores, so I can
decide which new channel concepts are worth pitching or launching before committing any client
budget to them.

### The `DiscoveryRun` → `NicheOpportunity` outcome

An operator starts a `DiscoveryRun` with a source market, a target market, a network, a content
format, a time window, and a depth (Quick/Standard/Deep — see the operator guide for the quota
tradeoffs). The run sources channel/video evidence, computes deterministic analytics, classifies
content patterns via AI (labels only, never a score), compares the source and target markets, and
produces a ranked list of append-only `NicheOpportunity` rows — one observation per run, never
updated in place. A later run on the same niche/market pair produces new rows; the earlier ones stay
retrievable by their own run.

### The OpportunityScore / ConfidenceScore distinction

Every opportunity carries **two independent numbers**, always shown together and never averaged:

- **OpportunityScore** — how strong the opportunity looks, combining viral performance, cross-channel
  validation, demand momentum, target-market gap, automation suitability, evergreen potential, and
  monetization potential. A signal with no data source in this iteration (`CrossPlatformValidation`)
  is excluded and the remaining weights renormalized — never zero-filled.
- **ConfidenceScore** — how much evidence backs that score (channel/video counts, sample size,
  timespan, source count, freshness, diversity, consistency), computed independently of
  OpportunityScore. A 94/37 opportunity is a very different bet than an 88/95 one, and the dashboard
  never collapses that distinction into one number.

### The market-catalog-is-data model

Target markets come from a seeded catalog (`MarketCode` + display i18n key), never from an enum
member, constant, or literal named after a country. Adding a second target market to the platform is
one catalog row — the entire pipeline (sourcing, analytics, market comparison, scoring) runs against
it with zero code change, proven by an automated test in the change that shipped this capability.

### MVP Acceptance Criteria

- An operator can start a discovery run against a seeded target market, a network, a content format,
  a window (7–365 days, default 90), and a depth (Quick/Standard/Deep), optionally overriding the
  scoring weights or seeding search terms.
- The run executes in the background (start-and-poll, matching the Profile Analysis idiom); the
  operator polls status until it reaches a terminal state (`Completed`, `PartiallyCompleted`, or
  `Failed`), each with a structured, distinguishable reason.
- On completion, the viability dashboard lists ranked opportunities, sortable and filterable by
  opportunity score, confidence score, market, format, trend, competition, and automation
  suitability.
- Each opportunity's detail view exposes: name/description, niche/sub-niche, formats and content
  patterns found, both scores, channels and videos analyzed, analysis period, data freshness, median
  views, outlier distribution, independent-channel repetition count, estimated target-market
  competition, automation suitability, main content patterns, risks/limitations, and navigable
  evidence linking to the real channels and videos.
- An unconfigured origin (no API key connected) is reported distinctly from a run that executed and
  genuinely found nothing.

### Non-Goals (this iteration)

- No "pursued / dismissed / tracked" operator action on an opportunity — comparison over time is done
  by querying across runs, not by mutating a stored state.
- No automatic hand-off into content generation. A seam (`INicheOpportunityActivationSource`) is
  named in the codebase for a future change to consume, but ships with no implementation and no
  registration in this iteration.
- Apify as a second sourcing origin is designed for (the port supports multiple configuration-selected
  origins per stage) but not implemented in this iteration — YouTube Data API v3 is the only shipped
  origin.
