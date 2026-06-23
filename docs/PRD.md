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

### Non-Goals

- Scheduling or automating repeated profile analyses.
- Comparing multiple accounts head-to-head.
- Real-time metrics (all data is from a single Apify snapshot).
