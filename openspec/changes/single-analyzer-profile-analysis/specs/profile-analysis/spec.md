# Spec Delta: Single-Analyzer Profile Analysis ("Mundo 2")

<!-- Change: single-analyzer-profile-analysis -->
<!-- ADR required: docs/architecture/adr/0003-profile-analysis-model.md -->

## Purpose

Define the delta requirements and acceptance scenarios for the Instagram profile analysis
capability ("Mundo 2") added to the single-analyzer tool. After this change is applied,
a community manager can analyse an entire Instagram account — by selecting a client's own
associated account or by pasting a profile URL — receive an expert AI marketing analysis,
view a charted dashboard of account metrics, and optionally impute detected fields into
the client's brand manual, content plan, and general strategy. The existing video→idea→
content-package flow ("Mundo 1") is explicitly unchanged and isolated by a regression
boundary requirement.

---

## PRD Traceability

| Requirement ID | Proposal Section / PRD Anchor |
|---|---|
| PA-01, PA-02, PA-03 | PRD §6 step 2 — "own accounts" and "manual URL list" as profile source inputs |
| PA-04, PA-05, PA-06 | PRD §9 — "Each organization can configure its own third-party integration credentials"; Apify scraping and automatic fallback |
| PA-07, PA-08, PA-09 | PRD §9 — owner-scoped metrics accuracy; degraded-data handling |
| PA-10, PA-11, PA-12 | PRD §9 — "A CM can create/select a client and run a workflow"; AI analysis result |
| PA-13, PA-14, PA-15 | PRD §8 — `ProfileAnalysis` as new persisted artifact; status lifecycle |
| PA-16, PA-17, PA-18, PA-19 | PRD §9 — "The frontend shows workflow state and errors clearly"; dashboard |
| PA-20, PA-21, PA-22 | PRD §6 step 2 — feeding own-account insights back into the client's ABM |
| PA-23, PA-24 | PRD §9 — structured diagnostics and provider error review |
| PA-REG-01 | Mundo 1 non-regression; SDD §5 bounded-context isolation |

---

## ADDED Requirements

---

### Requirement: PA-01 — Profile Source: Client + Own Account Selection

The system MUST allow a user to initiate a profile analysis by selecting an active client
scoped to their organization and then selecting one of that client's own associated
Instagram accounts. Only accounts with category `PROPIA` (own accounts) and
`SocialNetworkId` matching the Instagram network identifier MUST appear in the picker.
Referent and competitor accounts MUST be excluded. When a client has more than one
qualifying own Instagram account the user MUST select exactly one before the analysis
can be submitted.

#### Scenario: Single own Instagram account auto-resolved

- GIVEN an authenticated user with access to an organization
- AND the selected client has exactly one own associated account with SocialNetworkId = Instagram
- WHEN the user selects that client as the profile source
- THEN the system auto-resolves the account and the user is not shown an account picker

#### Scenario: Multiple own Instagram accounts require explicit pick

- GIVEN the selected client has two or more own Instagram associated accounts
- WHEN the user selects that client as the profile source
- THEN the system presents a picker listing ONLY the client's own Instagram accounts
- AND the user MUST select one account before proceeding
- AND analysis cannot be submitted with zero or multiple accounts selected

#### Scenario: Client with no own Instagram account rejects submission

- GIVEN the selected client has no associated account with category PROPIA and
  SocialNetworkId = Instagram
- WHEN the user attempts to start a profile analysis with that client
- THEN the system returns a validation error indicating no qualifying account is available
- AND no Apify call is made

#### Scenario: Referent and competitor accounts excluded from picker

- GIVEN the selected client has one referent Instagram account and one own Instagram account
- WHEN the own-account picker is shown
- THEN only the own account appears in the picker
- AND the referent account is not selectable

---

### Requirement: PA-02 — Profile Source: Manual URL

The system MUST allow a user to initiate a profile analysis by pasting an Instagram
profile URL directly, without selecting a client. The URL path carries no client context
and the resulting ProfileAnalysis record MUST have no linked client or own account.

#### Scenario: Valid Instagram profile URL accepted

- GIVEN a user pastes a syntactically valid Instagram profile URL
- WHEN the analysis is submitted
- THEN the system accepts the request and starts scraping
- AND the resulting ProfileAnalysis record has null clientId and null ownAccountId

#### Scenario: Non-Instagram or malformed URL rejected

- GIVEN a user pastes a URL that is not a valid Instagram profile URL
- WHEN the analysis is submitted
- THEN the system returns a 400 validation error before making any Apify call

---

### Requirement: PA-03 — Profile Source Mutually Exclusive

The system MUST enforce that each profile analysis request uses exactly one of the two
input paths (client + own account OR URL). Supplying both or neither MUST be rejected.

#### Scenario: Both source paths supplied

- GIVEN a request containing both a clientId/ownAccountId and a profileUrl
- WHEN the analysis endpoint receives the request
- THEN the system returns 400 with a validation error before making any Apify call

#### Scenario: Neither source path supplied

- GIVEN a request with neither clientId/ownAccountId nor profileUrl
- WHEN the analysis endpoint receives the request
- THEN the system returns 400 with a validation error

---

### Requirement: PA-04 — Profile Scrape via Apify with RESIDENTIAL Proxy

The system MUST scrape the full Instagram profile using the organization's configured
Apify actor routed by `networkKey = "instagram"` and `variantKey = "profile"`.
The actor input MUST include `proxyConfiguration: { useApifyProxy: true,
apifyProxyGroups: ["RESIDENTIAL"] }`. Omitting the RESIDENTIAL proxy group MUST cause
the adapter to reject the configuration before submitting to Apify.

#### Scenario: Profile scrape dispatched with residential proxy

- GIVEN a valid profile analysis request
- AND the organization has a configured and connected Apify integration
- AND actor_mappings_json contains entries for networkKey "instagram" variantKey "profile"
- WHEN the scrape adapter builds the Apify actor input
- THEN proxyConfiguration.apifyProxyGroups MUST contain "RESIDENTIAL"
- AND the actor invocation uses the actor id mapped to (networkKey="instagram", variantKey="profile")

#### Scenario: Missing actor mapping fails with diagnostic

- GIVEN the organization's actor_mappings_json has no entry for variantKey "profile"
- WHEN a profile analysis is requested
- THEN the system returns 422 with code "Integration.ProfileActorNotConfigured"
- AND no Apify call is made

#### Scenario: Missing Apify integration fails fast

- GIVEN the organization has no connected Apify integration
- WHEN a profile analysis is requested
- THEN the system returns 422 with code "Integration.ApifyNotConfigured"

---

### Requirement: PA-05 — Automatic Backup-Actor Fallback Within the Same Run

When the default actor returns an error or an empty profile result the system MUST
automatically retry with the configured backup actor during the same pipeline run,
without user intervention. The fallback MUST use the backup actor id from the
actor_mappings_json entry for `(networkKey="instagram", variantKey="profile")`.
The single-video scrape path MUST NOT be affected by this fallback logic.

#### Scenario: Default actor failure triggers backup actor

- GIVEN the default actor for (networkKey="instagram", variantKey="profile") returns
  a non-profile result or an Apify-level error
- WHEN the profile scrape adapter handles the response
- THEN the adapter retries the same profile username against the backup actor
- AND the retry uses the same RESIDENTIAL proxy configuration
- AND the fallback is recorded in the provider diagnostics of the run

#### Scenario: Both actors fail — run marked Failed

- GIVEN the default actor fails AND the backup actor also fails
- WHEN both attempts have been exhausted
- THEN the ProfileAnalysis record is set to status Failed
- AND diagnostics contain failure reasons for both actor attempts
- AND the endpoint returns a response indicating analysis failure with the diagnostic reason

#### Scenario: Single-video scrape path unaffected

- GIVEN a single-video analysis request is submitted concurrently
- WHEN the profile fallback path executes
- THEN the single-video pipeline runs without any modification to its actor selection logic

---

### Requirement: PA-06 — Profile Result Selection by resultType

The system MUST select the profile result from the Apify actor response by finding the
item where `resultType == "profile"`, NOT by positional index. Items with other
resultType values (e.g., quality_report, bandwidth_report) MUST be discarded silently.

#### Scenario: Profile item selected by resultType field

- GIVEN the Apify actor response contains an array with items having resultType values
  "profile", "quality_report", "bandwidth_report" in any order
- WHEN the profile result mapper processes the response
- THEN the item with resultType == "profile" is selected
- AND other items are discarded

#### Scenario: No profile item in response — run fails

- GIVEN the Apify actor response contains no item with resultType == "profile"
- WHEN the profile result mapper processes the response
- THEN the system treats this as an empty/failed response
- AND triggers the backup actor fallback (PA-05)

---

### Requirement: PA-07 — Owner Metrics Filtered to Owner Posts Only

All aggregate metrics derived from `latestPosts` MUST be computed only from posts where
`ownerId == profile.instagramId`. Posts where the profile is tagged or listed as a
coauthor but is not the owner MUST be excluded from metric computations. The exclusion
MUST be applied before any engagement, cadence, or weekday aggregations are calculated.

#### Scenario: Tagged non-owner posts excluded from metrics

- GIVEN a latestPosts array containing 8 posts owned by the profile and 2 posts where
  the profile is tagged but ownerId != profile.instagramId
- WHEN owner metrics are computed
- THEN all computed aggregates (engagement, cadence, weekday averages) are based on
  the 8 owner posts only
- AND the 2 tagged posts do not influence any computed metric

#### Scenario: Owner filter applied before weekday aggregation

- GIVEN the latestPosts include non-owner posts
- WHEN average engagement by weekday is computed
- THEN only owner posts' timestamps and metrics feed the weekday buckets

---

### Requirement: PA-08 — Degraded Post Handling

Posts where `_degraded == true` MUST be excluded from metric computations and annotated
in the stored dataset. A run with some degraded posts MUST yield status `ProfileScrapedPartial`
and proceed to the AI analysis stage. A run is only marked `Failed` if no usable (non-degraded)
owner posts remain after filtering.

#### Scenario: Degraded posts excluded and run marked Partial

- GIVEN a latestPosts array of 10 owner posts where 3 have _degraded == true
- WHEN the profile pipeline processes the posts
- THEN the 3 degraded posts are excluded from all metric computations
- AND the 7 non-degraded owner posts are used for analysis
- AND the ProfileAnalysis status is set to ProfileScrapedPartial (not Failed)
- AND the stored posts dataset annotates the degraded posts

#### Scenario: All owner posts degraded — run fails

- GIVEN all owner posts in latestPosts have _degraded == true after owner filtering
- WHEN the profile pipeline processes the posts
- THEN the ProfileAnalysis status is set to Failed
- AND diagnostics contain reason "NoUsableOwnerPosts"

---

### Requirement: PA-09 — Median Views Computed Over Video/Reel Posts Only

The "median views" metric MUST be computed from `videoViewCount` values of posts where
`mediaType` is `Video` or `Reel` (i.e., posts that have a non-null videoViewCount).
Posts with mediaType Image or Carousel MUST be excluded from the median views computation.
When no video or reel posts are present, median views MUST be null (not zero).

#### Scenario: Median views from video posts only

- GIVEN an owner post set containing 4 Video posts with videoViewCount values [100, 200, 300, 400]
  and 3 Image posts
- WHEN median views is computed
- THEN the result is 250 (median of [100, 200, 300, 400])
- AND the Image posts do not contribute to the computation

#### Scenario: No video posts yields null median views

- GIVEN an owner post set containing only Image and Carousel posts
- WHEN median views is computed
- THEN the result is null

#### Scenario: Carousel posts excluded even if videoViewCount accidentally non-null

- GIVEN a Carousel post in the owner set has a non-null videoViewCount
- WHEN median views is computed
- THEN the Carousel post is excluded (mediaType takes precedence over field presence)

---

### Requirement: PA-10 — AI Marketing Analysis via Org Default AI Agent

The AI marketing analysis MUST be executed using the organization's default AI agent,
resolved via `IDefaultAiSelector` and invoked via `IAiCompletionGateway`. The AI flow
key MUST be `"profile-analyzer"` with its own `PromptVersionKey`, separate from
`"single-analyzer"`. No model name or provider key MUST be hardcoded in Domain or
Application layers.

#### Scenario: Default AI provider resolved and invoked

- GIVEN an organization with a connected AI integration marked is_default_ai = true
- AND a ProfileAnalysis record in status ProfileScraped or ProfileScrapedPartial
- WHEN the AI analysis step executes
- THEN the system resolves the provider via IDefaultAiSelector
- AND invokes IAiCompletionGateway with flowKey = "profile-analyzer"
- AND the prompt version key is the active version for "profile-analyzer"

#### Scenario: No AI provider configured fails with explicit error

- GIVEN an organization with no connected AI model integration
- WHEN the AI analysis step executes
- THEN the ProfileAnalysis record status is set to Failed
- AND diagnostics contain reason "AI.NoProviderConfigured"

#### Scenario: AI flow key isolation from single-analyzer

- GIVEN both Mundo 1 and a profile analysis run in the same organization
- WHEN the AI step executes for the profile analysis
- THEN the flow key used is "profile-analyzer", not "single-analyzer"
- AND the prompt version retrieved is for "profile-analyzer" independently

---

### Requirement: PA-11 — AI Analysis Content Fields

The AI marketing analysis stored in the ProfileAnalysis record MUST include all of the
following structured fields: content pillars detected, audience profile description,
posting cadence and recommended days, top content types, detected brand colors,
detected fonts, detected background image styles, voice and tone description, recommended
KPIs, monthly objective suggestion, content strategy suggestion, strengths,
weaknesses/gaps, opportunities, and a confidence score (0.0–1.0). Warnings MUST be
emitted when the confidence score is below a configured threshold or when the post sample
is too sparse to yield reliable insights.

#### Scenario: Full analysis fields present on successful run

- GIVEN a successful AI completion for a profile with sufficient post data
- WHEN the analysis result is mapped and persisted
- THEN the ProfileAnalysis.AiAnalysis record contains non-null values for all required fields
- AND confidenceScore is between 0.0 and 1.0

#### Scenario: Low confidence emits warning

- GIVEN the AI returns a confidence score below the configured threshold (e.g., 0.5)
- WHEN the analysis is persisted
- THEN the ProfileAnalysis.Warnings collection contains at least one entry indicating
  low confidence

#### Scenario: Sparse data warning on small post sample

- GIVEN the profile's latestPosts after owner + degraded filtering contains fewer posts
  than the configured minimum sample size
- WHEN the AI analysis step executes
- THEN the prompt includes a sparse-data notice
- AND the ProfileAnalysis.Warnings contains a sparse-data warning

---

### Requirement: PA-12 — Average Engagement and Cadence by Weekday

The system MUST compute average likes, average comments, and average views per weekday
bucket (Monday–Sunday) from owner posts. Post timestamps MUST be parsed as ISO8601 UTC
and converted to the weekday using UTC-based arithmetic. The result MUST be stored as
part of the server-derived stats in the ProfileAnalysis record.

#### Scenario: Weekday averages computed from UTC timestamps

- GIVEN a set of owner posts with timestamps distributed across multiple weekdays
- WHEN weekday aggregation is computed
- THEN the result contains one entry per weekday (0=Monday, 6=Sunday) that had at least
  one post, with the correct average likes, average comments, and average views for that day
- AND weekdays with no posts are omitted or represented as zero/null

#### Scenario: Non-UTC timestamps rejected or converted

- GIVEN a post timestamp with an explicit non-UTC offset (e.g., "2026-05-01T10:00:00+03:00")
- WHEN the weekday computation runs
- THEN the system normalizes the timestamp to UTC before computing the weekday bucket

---

### Requirement: PA-13 — ProfileAnalysis Aggregate Persistence

The system MUST persist a `ProfileAnalysis` aggregate in a dedicated database table within
the `social` schema, scoped by `organization_id`. The record MUST store: the raw posts
dataset (JSON column), the server-derived stats (JSON column), the AI analysis result
(JSON column), a status field, provider diagnostics, and soft-references to `client_id`
and `own_account_id` when the client+account path was used. `client_id` and
`own_account_id` MUST be null for the URL-only path. EF mappings and JSON column handling
MUST live exclusively in the Infrastructure layer; the Domain aggregate MUST NOT reference
EF or Npgsql types.

#### Scenario: Record persisted with organization scope

- GIVEN a profile analysis is submitted for an organization
- WHEN the analysis completes
- THEN a ProfileAnalysis row exists in social.profile_analyses with the correct organization_id
- AND client_id and own_account_id match the submitted source (or are null for URL path)
- AND raw_posts_dataset, server_derived_stats, and ai_analysis columns are populated

#### Scenario: URL-path record has null client and account references

- GIVEN a profile analysis submitted via the URL path (no client selected)
- WHEN the record is persisted
- THEN client_id IS NULL and own_account_id IS NULL
- AND profile_url contains the submitted URL

#### Scenario: Organization isolation enforced

- GIVEN two organizations each have ProfileAnalysis records
- WHEN organization A queries its profile analyses
- THEN only records with organization_id = A are returned
- AND no record from organization B is visible

---

### Requirement: PA-14 — ProfileAnalysis Status Lifecycle

The ProfileAnalysis record MUST transition through a defined status lifecycle. Valid
transitions are: `Requested → ScrapingProfile → ProfileScraped | ProfileScrapedPartial →
Analyzing → Analyzed` (terminal success). The terminal failure status is `Failed`,
reachable from any non-terminal state. No other status transitions are permitted.

#### Scenario: Happy-path status progression

- GIVEN a valid profile analysis request is received
- WHEN each pipeline stage completes successfully
- THEN the status transitions are:
  Requested → ScrapingProfile → ProfileScraped → Analyzing → Analyzed
- AND each transition is recorded with a timestamp

#### Scenario: Partial scrape proceeds to AI analysis

- GIVEN the scrape completes with some degraded posts
- WHEN the scrape stage sets the status
- THEN status is set to ProfileScrapedPartial (not Failed)
- AND the pipeline proceeds to the Analyzing step

#### Scenario: Invalid transition rejected

- GIVEN a ProfileAnalysis record in status Analyzed
- WHEN code attempts to set status to ScrapingProfile
- THEN the domain rejects the transition and raises an invariant violation

#### Scenario: Failed is terminal

- GIVEN a ProfileAnalysis record in status Failed
- WHEN code attempts any further status transition
- THEN the domain rejects the transition

---

### Requirement: PA-15 — ProfileAnalysis Retrievable for Dashboard and History

The system MUST expose API endpoints to start a profile analysis, poll its status,
retrieve the completed record (with all stats and AI analysis), and list past analyses
for the authenticated organization. All queries MUST be scoped to organization_id from
the authenticated principal, never from the request body.

#### Scenario: Start analysis returns record id

- GIVEN a valid profile analysis request
- WHEN the start endpoint is called
- THEN the response contains the new ProfileAnalysis id and initial status "Requested"
- AND the pipeline is triggered asynchronously

#### Scenario: Poll endpoint returns current status

- GIVEN an in-progress ProfileAnalysis
- WHEN the status poll endpoint is called with the analysis id
- THEN the response contains the current status and diagnostics

#### Scenario: Fetch completed analysis returns full record

- GIVEN a ProfileAnalysis in status Analyzed
- WHEN the fetch endpoint is called with the analysis id
- THEN the response contains profile metadata, server-derived stats, AI analysis fields,
  and provider diagnostics

#### Scenario: List returns only organization-scoped records

- GIVEN the organization has 3 completed ProfileAnalysis records
- WHEN the list endpoint is called
- THEN exactly 3 records are returned
- AND no record from another organization is included

---

### Requirement: PA-16 — Dashboard: Overview and Core Metrics

The profile analysis dashboard MUST render the following from the ProfileAnalysis record:
followers count, total posts count, engagement rate, median views (video scope, null when
absent), average views and average engagement by weekday as a chart, content-type mix
(Image/Video/Carousel percentages), posting cadence (average days between posts), and
the AI narrative analysis. All values MUST be sourced from the persisted server-derived
stats or AI analysis fields — no re-scraping or live computation at render time.

#### Scenario: Dashboard hydrated from persisted record

- GIVEN a ProfileAnalysis in status Analyzed with fully populated stats and AI analysis
- WHEN the dashboard component receives the analysis data
- THEN followers, posts, engagement rate, posting cadence, and content-type mix are rendered
- AND the weekday chart uses the weekday aggregation from server_derived_stats
- AND the AI narrative is displayed without re-calling any AI service

#### Scenario: Null median views shown as "N/A"

- GIVEN a ProfileAnalysis where no video posts existed (median views is null)
- WHEN the dashboard renders
- THEN the median views card displays "N/A" or an equivalent absent-data indicator
- AND no error or blank render occurs

#### Scenario: Partial analysis renders available data

- GIVEN a ProfileAnalysis in status ProfileScrapedPartial or Analyzed with warnings
- WHEN the dashboard renders
- THEN available metrics are displayed normally
- AND a warning indicator is shown for any metric that could not be computed

---

### Requirement: PA-17 — Dashboard: Client Objective Comparison Panel

WHEN a ProfileAnalysis record has a non-null `client_id` AND the linked client has at
least one persisted ContentPlan or GeneralStrategy, the dashboard MUST render an
objective comparison panel. The panel MUST show the detected account state (from AI
analysis) versus the client's stated objectives and strategy (from the client's active
ContentPlan and GeneralStrategy). The comparison MUST be computed at render time by
crossing analysis data with ContentPlan/GeneralStrategy; it MUST NOT be persisted.
When `client_id` is null (URL path), the comparison panel MUST be omitted entirely.

#### Scenario: Comparison panel rendered when client has objectives

- GIVEN a ProfileAnalysis with a non-null client_id
- AND the client has an active ContentPlan with an objective and a GeneralStrategy
- WHEN the dashboard renders
- THEN the comparison panel is shown with detected vs stated values side by side

#### Scenario: Comparison panel omitted for URL-only path

- GIVEN a ProfileAnalysis with null client_id
- WHEN the dashboard renders
- THEN no comparison panel is rendered
- AND no ContentPlan or GeneralStrategy lookup is attempted

#### Scenario: Comparison panel omitted when client has no plan data

- GIVEN a ProfileAnalysis with a non-null client_id
- AND the client has no ContentPlan and no GeneralStrategy
- WHEN the dashboard renders
- THEN the comparison panel is omitted (no empty panel shown)

---

### Requirement: PA-18 — Dashboard Adapts to Available Data

The dashboard MUST render successfully regardless of which optional fields are present
in the analysis. Sections for missing or null data MUST degrade gracefully (show absent
indicator or be hidden) without throwing rendering errors.

#### Scenario: Dashboard renders with minimal analysis data

- GIVEN a ProfileAnalysis in status Analyzed where brand colors and fonts were not detected
- WHEN the dashboard renders
- THEN the brand colors section shows an absent-data indicator or is hidden
- AND all other sections with data render correctly

---

### Requirement: PA-19 — Mode Switch: Contenido | Perfil

The single-analyzer entry point MUST expose a top-level mode switch between "Contenido"
(Mundo 1) and "Perfil" (Mundo 2). Selecting "Perfil" MUST navigate to the new Profile
Analyzer module/route. Selecting "Contenido" MUST navigate to or remain on the existing
single-analyzer view. The Mundo 1 component (`single-analyzer-client.tsx` and its handler
tree) MUST NOT be modified as part of this change.

#### Scenario: Mode switch navigates to Profile Analyzer

- GIVEN the user is on the single-analyzer entry and selects "Perfil"
- WHEN the mode switch fires
- THEN the user is navigated to the Profile Analyzer route
- AND the Mundo 1 component is not rendered or modified

#### Scenario: Mode switch returns to Contenido

- GIVEN the user is in the Profile Analyzer route and selects "Contenido"
- WHEN the mode switch fires
- THEN the user is navigated back to the Mundo 1 single-analyzer view
- AND no Profile Analyzer state is carried into Mundo 1

---

### Requirement: PA-20 — Impute: Per-Field Opt-In with Confirmation

The system MUST allow the user to impute AI-detected fields from a completed
ProfileAnalysis into the linked client's brand manual, content plan, and general strategy.
Each field MUST be presented individually and MUST require explicit per-field confirmation
before being written. No field MUST be auto-applied. The impute action MUST use existing
write commands only; no new write commands MUST be created. Impute MUST be unavailable
for ProfileAnalysis records where `client_id` is null.

Eligible impute targets and their write command mappings:
- Brand manual: ColorPalette, Typography (fonts), BackgroundTemplates, Logos, VoiceAndTone
- Content plan: ObjectiveText, ObjectiveKpis, ContentStrategyText, Slots (publication days)
- General strategy: ContentPillars, ToneKeywords, TargetAudience, PostingFrequency

#### Scenario: Impute unavailable on URL-only path

- GIVEN a ProfileAnalysis with null client_id
- WHEN the frontend renders the analysis result
- THEN no impute action, button, or panel is shown

#### Scenario: Per-field confirmation required before write

- GIVEN a completed ProfileAnalysis with a linked client
- WHEN the user selects fields to impute and triggers the impute action
- THEN the system shows a confirmation step listing each selected field and its new value
- AND the write commands are only dispatched after the user confirms
- AND unconfirmed fields are not written

#### Scenario: Only selected fields written

- GIVEN a user selects 3 of 10 available fields for impute
- WHEN the user confirms
- THEN exactly 3 write commands are dispatched (one per selected field)
- AND the 7 unselected fields remain unchanged in the client's ABM

---

### Requirement: PA-21 — Impute Is Idempotent

Imputing the same field with the same value from the same ProfileAnalysis more than once
MUST produce the same result as a single impute. The write commands used for impute MUST
be idempotent by contract. Re-running impute MUST NOT create duplicate records or
accumulate duplicate entries in collection fields.

#### Scenario: Re-impute same field produces same result

- GIVEN a ProfileAnalysis that has already imputed ColorPalette into the client's brand manual
- WHEN the same impute action is triggered again with the same value
- THEN the client's brand manual ColorPalette has the same value as after the first impute
- AND no duplicate ColorPalette entry is created

---

### Requirement: PA-22 — Impute Unavailable Until Analysis Is Complete

The impute action MUST only be available when the ProfileAnalysis record is in status
`Analyzed`. Attempting impute on a record in any other status MUST be rejected by the API.

#### Scenario: Impute rejected on non-Analyzed record

- GIVEN a ProfileAnalysis in status Analyzing (or any non-Analyzed status)
- WHEN the impute endpoint is called
- THEN the API returns 422 with code "ProfileAnalysis.NotAnalyzed"
- AND no write commands are dispatched

---

### Requirement: PA-23 — Structured Workflow Logging and Provider Diagnostics

Every pipeline stage MUST emit structured log entries and the ProfileAnalysis record MUST
carry provider diagnostics (mirroring the ProviderDiagnosticsJson pattern used in the
single-video pipeline). Diagnostics MUST include: actor id(s) used, fallback indicator,
number of posts received vs processed, degraded post count, AI provider key and model
used, and any provider-level error messages. Log entries MUST be emitted to hive-api.log.

#### Scenario: Diagnostics populated after successful run

- GIVEN a ProfileAnalysis that completes successfully
- WHEN the record is retrieved
- THEN provider_diagnostics_json contains actor_id, fallback_used (false or true),
  posts_received, posts_processed, degraded_post_count, ai_provider_key, and ai_model

#### Scenario: Diagnostics populated on fallback run

- GIVEN the default actor failed and the backup actor succeeded
- WHEN the record is retrieved
- THEN provider_diagnostics_json.fallback_used is true
- AND both default_actor_error and backup_actor_id are present in diagnostics

#### Scenario: Diagnostics populated on failed run

- GIVEN a run where both actors failed
- WHEN the record is retrieved
- THEN provider_diagnostics_json contains failure reasons for both attempts
- AND status is Failed

---

### Requirement: PA-24 — variantKey "profile" Routing Isolation

The profile scrape adapter MUST route Apify actor selection using `variantKey = "profile"`,
distinct from the `variantKey` used by the single-video scrape path for Instagram.
The routing key MUST be enforced throughout the Infrastructure adapter chain so that no
actor substitution can silently occur between the single-video and profile-scrape paths.

#### Scenario: Profile scrape uses variantKey "profile"

- GIVEN a profile analysis request
- WHEN the Infrastructure adapter resolves the Apify actor
- THEN the actor lookup key is (networkKey="instagram", variantKey="profile")
- AND the resulting actor id corresponds to the profile actor, not the single-video actor

#### Scenario: Single-video scrape unaffected by profile variantKey

- GIVEN a single-video analysis request for an Instagram URL
- WHEN the Infrastructure adapter resolves the Apify actor
- THEN the actor lookup key is the existing single-video key (networkKey="instagram",
  variantKey not equal to "profile")
- AND the profile actor is not invoked

---

### Requirement: PA-REG-01 — Regression Boundary: Mundo 1 Unchanged

The existing single-video analysis and content-generation pipeline ("Mundo 1") MUST remain
fully functional and unmodified by this change. No changes MUST be made to:
`AnalyzeSingleContentHandler`, `SingleContentAnalyzer`, the single-video Apify scrape
adapter, the `single_content_analyses` table schema, the `SocialContentAnalysisProfile`
strategy class, or any Mundo 1 application/domain types. The single-analyzer frontend
component (`apps/web` Mundo 1 module) MUST NOT be edited. Any modification that touches
these artifacts without an explicit regression justification MUST be treated as a scope
violation.

#### Scenario: Mundo 1 analysis unaffected after profile feature ships

- GIVEN the profile analysis feature is fully implemented
- WHEN a Mundo 1 single-video analysis is submitted
- THEN it follows the existing pipeline end to end without any code-path change
- AND the response contract matches the pre-change content-research spec exactly

#### Scenario: Profile feature shares no domain types with Mundo 1

- GIVEN the completed implementation
- WHEN the Domain and Application namespaces for profile analysis are inspected
- THEN no Mundo 1 domain type (SingleContentAnalysis, SocialContentAnalysisProfile, etc.)
  is referenced from or imported into the profile analysis feature namespace

#### Scenario: single_content_analyses table schema unchanged

- GIVEN the EF migrations applied by this change
- WHEN the social.single_content_analyses table is inspected
- THEN its columns, indexes, and constraints are identical to the pre-change schema

---

## MODIFIED Requirements

### Requirement: INT-DELTA-01 — Integrations Spec: actor_mappings_json Profile Entries

The integrations spec MUST document that actor_mappings_json for the Apify integration
MUST contain entries for `networkKey = "instagram"` and `variantKey = "profile"` for
both the default actor (`get-leads~all-in-one-instagram-scraper`) and the backup actor
(`apify~instagram-profile-scraper`). This is an additive documentation requirement; no
schema change to the integration model is needed.

#### Scenario: actor_mappings_json documented entries verified

- GIVEN the actor_mappings_json integration credential configuration guide
- WHEN the guide is read
- THEN the required entry format for (networkKey="instagram", variantKey="profile") with
  both default and backup actor identifiers is present
- AND the automatic fallback behavior triggered when the default actor fails is described

---

## REMOVED Requirements

None. This is an additive change.

---

## Implementation Boundary Notes (non-normative, for test authoring)

These notes are informational for xUnit test authors and do not define behavior.

- All PA-01 through PA-24 requirements are verifiable by xUnit integration tests against
  the backend API surface and unit tests against domain/application handlers.
- PA-REG-01 can be partially enforced by an architecture test (e.g., NetArchTest) that
  asserts no type in the `ProfileAnalysis` feature namespace imports from the
  `SingleAnalyzer` feature namespace (and vice versa).
- Weekday aggregation (PA-12) MUST be covered by pure unit tests with deterministic
  ISO8601 timestamp fixtures.
- Owner filtering (PA-07) and degraded post handling (PA-08) MUST be covered by unit
  tests with controlled post fixtures; do not rely on live Apify data for these assertions.
- Actor fallback (PA-05) MUST be covered by an integration test that injects a mock
  Apify gateway returning a failure for the default actor and success for the backup.
- Impute idempotency (PA-21) MUST be covered by a test that calls the impute endpoint
  twice and asserts stable state.
