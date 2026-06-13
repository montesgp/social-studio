# Tasks: Content Studio (Phase 1 — Foundation on Single Analyzer)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 1,600–2,100 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (Domain) → PR 2 (Infra + Application + API) → PR 3 (Handoff + Frontend + docs) |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | `Domain/Social/ContentStudio/` — VOs, `ReviewState` + transitions, `ContentItem` + `ContentSet` aggregates | PR 1 | Base: dev; pure domain — no migration; all tests green |
| 2 | EF entities + `Add_ContentStudio` migration + repositories; Application ports + materializer + feature slices; API endpoints | PR 2 | Base: PR 1 branch; single additive migration; heaviest slice |
| 3 | Handoff port + payload + Phase-1 adapter; frontend `ContentStudioModule` + Single Analyzer Studio tab + preview; docs sync; gates | PR 3 | Base: PR 2 branch; preview consumes projection (no second emulator) |

> **Strict TDD is active.** Every backend domain rule, application handler, and transition gets a failing test FIRST ([RED]) then the implementation ([GREEN]). Test runner: `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj`. Run `pwsh -File scripts/gates/Run-Gates.ps1` at each marked gate checkpoint.

---

## Phase 1 — Domain: Value Objects (PR 1, WU 1) — refs CS-01, CS-02

- [ ] 1.1 **[RED]** Add `Incoders.Hive.Tests/Domain/Social/ContentStudio/ContentOriginTests.cs` — asserts `ContentOrigin` holds `GeneratorKind` (enum, `SingleAnalyzer=0`), `SourceRunId`, nullable `SourcePackageId`, `FormatResultId`, `FormatKey`; value equality; no `SingleAnalyzer` type referenced.
- [ ] 1.2 **[GREEN]** Create `Incoders.Hive.Domain/Social/ContentStudio/GeneratorKind.cs`, `ContentOrigin.cs`.
- [ ] 1.3 **[RED]** Add `ContentKindTests.cs` / `TargetChannelTests.cs` / `ContentDraftTests.cs` / `EditTraceTests.cs` — `ContentMedium` enum dispatch (`Text/Visual/Video/Document`); `TargetChannel(network, placement)`; `ContentDraft(text, visualSpecMarkup, assets, targetChannel)` with `ContentAssetRef(role, storageUri, mime)`; `EditTrace.Initial(now)` = `Manual`, null timestamp.
- [ ] 1.4 **[GREEN]** Create `ContentMedium.cs`, `ContentKind.cs`, `TargetChannel.cs`, `ContentAssetRef.cs`, `ContentDraft.cs`, `EditSource.cs`, `EditTrace.cs`, `PeriodRef.cs`, `PlanContext.cs`.
- [ ] 1.5 Run `dotnet test` — Phase 1 VO tests green.

## Phase 2 — Domain: ReviewState + ContentItem + ContentSet (PR 1, WU 1) — refs CS-01, CS-03, CS-05, CS-06, CS-08, CS-09

- [ ] 2.1 **[RED]** Add `ReviewStateTransitionsTests.cs` — every legal transition in design §3.6 succeeds; every illegal transition is rejected; `HandedOff` is terminal.
- [ ] 2.2 **[GREEN]** Create `ReviewState.cs` and `ReviewStateTransitions.cs` (static allowed-transition map mirroring `ProfileAnalysisStatusTransitions`); `ContentStudioErrors.cs` (`IllegalReviewTransition`, `RejectionReasonRequired`, `TextEmpty`, `TextTooLong`, `StageRequiresApproved`).
- [ ] 2.3 **[RED]** Add `ContentItemTests.cs` — `Materialize` factory sets `State=Draft` + `EditTrace.Initial` (CS-01); `EditText` updates draft, sets `Edited`, validates non-empty + max length, leaves origin/source untouched (CS-06); `Approve`/`Reject`(reason required)/`Discard` guarded transitions (CS-03, CS-08); `StageForPublication` only from `Approved`, `MarkHandedOff` only from `ReadyForPublication` (CS-09); duplicate-style clone starts in `Draft` without reviewer/reason (CS-08).
- [ ] 2.4 **[GREEN]** Create `ContentItemId.cs` and `ContentItem.cs` aggregate with `Materialize`, `Restore`, `EditText`, `Approve`, `Reject`, `Discard`, `StageForPublication`, `MarkHandedOff`, and a `CloneAsDraft`/variant factory.
- [ ] 2.5 **[RED]** Add `ContentSetTests.cs` — `CreateForRun` (kind `GenerationRun`); `AddMember`/`RemoveMember` ordered; `ReviewSummary` roll-up counts by state (CS-05); removing a set does not delete items (soft membership).
- [ ] 2.6 **[GREEN]** Create `ContentSetId.cs`, `SetKind.cs`, `SetOrigin.cs`, `ContentSetMember.cs`, `ReviewSummary.cs`, `ContentSet.cs`.
- [ ] 2.7 Run `dotnet test` — all Phase 1–2 domain tests green, no regressions.

## Phase 3 — Infrastructure: Persistence (PR 2, WU 2) — refs CS-10, CS-11

- [ ] 3.1 Create EF entities `ContentStudioItemData.cs`, `ContentStudioSetData.cs`, `ContentStudioSetItemData.cs` (columns per design §5.1). Register `DbSet`s in `AppIdentityDbContext`; map `EditTrace` and `PlanContext` via `OwnsOne` (flat prefixed columns); enums via `HasConversion<short>()`; `draft_assets_json` jsonb; **no** navigation to packages/format-results/content-plans (soft Guids).
- [ ] 3.2 Add fluent config: indexes per design §5.2 (`organization_id`, `client_id`, `source_run_id`, `(organization_id, review_state)`, `variant_group_id`; set `origin_source_run_id`; join unique `(set_id, item_id)`); FKs to `platform.organizations` + `social.clients` cascade; unique `(organization_id, origin_source_run_id)` on `GenerationRun` sets (idempotency guard, design §11).
- [ ] 3.3 Add EF migration `Add_ContentStudio` (additive only): create the three tables, owned-type columns, indexes, FKs. No data migration.
- [ ] 3.4 Run gates: `pwsh -File scripts/gates/Run-Gates.ps1` — build + architecture gate must pass (no provider leakage; DbContext snapshot updated).
- [ ] 3.5 **[RED]** Add `Incoders.Hive.Tests/Infrastructure/Persistence/ContentStudioEntityMappingTests.cs` — round-trip an item (all owned columns, enums, jsonb assets) and a set + join row; assert soft-Guid columns (no FK to generation).
- [ ] 3.6 **[GREEN]** Create `EfContentItemRepository.cs` (`IContentItemRepository`) and `EfContentSetRepository.cs` (`IContentSetRepository`) — paged/filterable queries; `GetReviewSummaryAsync` as an aggregation query (not N loads); map `Restore(...)`; no navigations.
- [ ] 3.7 Run `dotnet test` + gates — mapping/repository tests green.

## Phase 4 — Application: Ports, Materializer, Feature Slices (PR 2, WU 2) — refs CS-04, CS-06, CS-07, CS-08

- [ ] 4.1 Add ports under `Abstractions/Social/ContentStudio/`: `IContentItemRepository.cs`, `IContentSetRepository.cs`.
- [ ] 4.2 **[RED]** Add `Incoders.Hive.Tests/Application/Social/ContentStudio/ContentItemMaterializerTests.cs` — CS-04: materializes one item per successful format with valid `ContentOrigin` + initial draft; idempotent re-materialization returns existing set; non-mutating (asserts no generation write via fakes); draft decoupled from source.
- [ ] 4.3 **[GREEN]** Create `Features/Social/ContentStudio/Materialize/` (`MaterializeRunCommand`, handler, validator) + `ContentItemMaterializer` application service reading existing generation read ports (`IContentGenerationFormatResultRepository`, format-asset repo, `IGeneratedPackageRepository`) — initializes `ContentDraft` (text/markup/assets/targetChannel) per format key.
- [ ] 4.4 **[GREEN]** Create query slices: `GetItem`, `GetSet` (+ `ReviewSummaryDto`), `ListItems` (paged, `?state=`), `Preview/GetContentPreviewQuery` → `ContentPreviewProjection` with a channel-constraints catalog (CS-07, presentation-only).
- [ ] 4.5 **[RED]** Add `ContentItemOperationsTests.cs` — Application-level: `EditItemTextCommand` (CS-06 validation), `Approve/Reject/Discard/Duplicate/Delete` (CS-08), each asserting the guarded transition and persistence.
- [ ] 4.6 **[GREEN]** Create command slices: `EditText`, `Approve`, `Reject`, `Discard`, `Duplicate`, `Delete` under `Features/Social/ContentStudio/**`.
- [ ] 4.7 Register all ports → EF repositories and the materializer in the DI composition root.
- [ ] 4.8 Run `dotnet test` + gates.

## Phase 5 — Handoff Contract (PR 3, WU 3) — refs CS-09, PUB-HANDOFF-01, PUB-HANDOFF-02

- [ ] 5.1 **[RED]** Add `StageItemForPublicationTests.cs` — staging an `Approved` item builds a `PublicationHandoffPayload` (`SchemaVersion=1`, all fields per design §6.4), calls `IPublicationHandoff`, then transitions to `HandedOff`; staging a non-`Approved` item fails (illegal transition) and emits nothing; Content Studio calls no publishing provider (CS-09 / PUB-HANDOFF-02).
- [ ] 5.2 **[GREEN]** Create `PublicationHandoffPayload.cs`, `IPublicationHandoff.cs` (Application), `Features/Social/ContentStudio/Stage/StageItemForPublicationCommand.cs` (+ handler), and the Phase-1 `OutboxPublicationHandoff` adapter (records the handoff to an outbox/log row; no provider send). Register in DI.
- [ ] 5.3 Run `dotnet test` + gates.

## Phase 6 — Api: Endpoints (PR 2/3, WU 2) — refs CS-11, CS-12

- [ ] 6.1 Create `Incoders.Hive.ApiService/Endpoints/Social/MapContentStudioEndpoints.cs` with the routes in design §7 (materialize, get/list set + items, get item, preview, edit-text, approve, reject, discard, duplicate, delete, stage). Org-scope every route via existing auth/tenancy; map illegal transitions to HTTP 409 (`content-studio.illegal_transition`). Register in the API route builder.
- [ ] 6.2 **[RED]** Add `Incoders.Hive.Tests/Api/Social/ContentStudioEndpointTests.cs` — smoke tests for materialize (idempotent), get set + items, edit-text, approve→stage→handoff, and cross-organization denial (CS-11).
- [ ] 6.3 Run `dotnet test` + full gate suite.

## Phase 7 — Frontend: Studio Surface (PR 3, WU 3) — refs CS-07, CS-12

- [ ] 7.1 Add a lazy-loaded `ContentStudioModule` (isolated like `ProfileAnalyzerModule`): set view with `ReviewSummary`, item list, item detail (draft + state badges), text editor, and per-item action controls (approve/reject/discard/duplicate/delete/stage) wired to the endpoints.
- [ ] 7.2 Build the destination **preview emulator** as a presentation component driven by the `ContentPreviewProjection` (IG Feed/Carousel/Story/Reel, TikTok, YT Shorts). Do NOT build a second emulator — this is the single preview home; note the `generate-social-content-workflow` wizard emulator as a follow-up to converge onto this projection.
- [ ] 7.3 Add an additive **Studio tab** to the Single Analyzer surface that calls `materialize` (idempotent) on open with the analysis id and renders the set. The existing analysis/generation/Package-tab behavior is untouched (CS-12).
- [ ] 7.4 Frontend build + type-check pass; component tests for the editor, action controls, and preview projection rendering.

## Phase 8 — Docs Sync, Diagram, and Cleanup (PR 3, WU 3) — refs CS-01–CS-12

- [ ] 8.1 Create `openspec/specs/content-studio/spec.md` (merge the change spec) and update `openspec/specs/content-generation/spec.md`, `openspec/specs/content-plan/spec.md`, `openspec/specs/publishing/spec.md` with the merged deltas (at archive time).
- [ ] 8.2 Add a Content Studio section to `docs/PRD.md` and `docs/SDD.md` (mirror the Profile Analysis "Mundo" sections); reference ADR 0004.
- [ ] 8.3 Add `docs/architecture/diagrams/content-studio-flow.md` (Mermaid) + `.excalidraw` showing Generators → Materializer → ContentSet/ContentItem → ReviewState → Handoff, and the immutability/isolation boundaries.
- [ ] 8.4 Update `openspec/changes/content-studio/state.yaml` to mark phases complete.
- [ ] 8.5 Final gate run: `pwsh -File scripts/gates/Run-Gates.ps1` — all MUST gates green before declaring done.
