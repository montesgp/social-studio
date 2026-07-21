# Tasks: Content Bank Planner (v1 — Instagram-only publish-now)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 3,400–4,300 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (Publication domain + persistence) → PR 2 (Connector + credentials) → PR 3 (Bank query + lifecycle commands + Api) → PR 4 (Frontend) → PR 5 (Docs/ADR) |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Rough size | Notes |
|------|------|-----------|-----------|-------|
| 1 | `Domain/Social/Publications/` (`PublicationStatus`+transitions, VOs, `Publication` aggregate) + `Domain/Social/ClientAccountConnections/` (`ClientAccountConnection`) + EF entities/migration/repos | PR 1 | ~900–1,100 lines | Base: dev; pure domain first, then persistence in the same PR (mirrors design §8); one additive migration `Add_Publications`; all tests green before moving on |
| 2 | `IPublicationConnector` port + selector + `PublicationDestinationResolver` + native `InstagramGraphPublicationConnector` + credential management commands (`Create`/`Rotate`/`Test`/`List` `ClientAccountConnection`) + connection endpoints | PR 2 | ~700–900 lines | Base: PR 1 branch; provider isolation is architecture-gate critical here |
| 3 | Publish-now/lifecycle commands (`PublishItemNow`/`Schedule`/`Retry`/`Cancel`/`ReclaimStuck`/`GetPublication`) + Bank query (`ListByClientAsync` + `ListClientContentBankQuery` + `EnsureClientContentMaterializedCommand`) + Bank/Publication endpoints + orthogonality assertions | PR 3 | ~900–1,100 lines | Base: PR 2 branch; heaviest Application slice; depends on the connector from PR 2 |
| 4 | Frontend `/publications` workspace (Bank/Planner/Published tabs, shared components, api client, connections settings panel) | PR 4 | ~700–900 lines | Base: PR 3 branch; reuses `content-studio-panel.tsx` verbatim, no editing-logic duplication |
| 5 | ADR 0005, spec merges (`publishing`, `content-bank`, `content-studio`/`integrations` deltas), PRD/SDD sync, `state.yaml`, final gates | PR 5 | ~250–400 lines | Base: PR 4 branch; do NOT merge the old `content-studio` publishing handoff delta (design §16) |

> **Strict TDD is active.** Every backend domain rule, transition, handler, and query gets a failing test FIRST (`[RED]`) then the implementation (`[GREEN]`). Test runner: `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj`. Run `pwsh -File scripts/gates/Run-Gates.ps1` at each marked gate checkpoint. Work units follow `work-unit-commits` — each unit has a clear start/finish/verify boundary and produces one focused, reviewable commit. `[P]` marks tasks that can run in parallel with sibling `[P]` tasks in the same phase (no shared file).

---

## Phase 1 — Domain: PublicationStatus & Transitions (PR 1, WU 1) — refs PUB-02

- [x] 1.1 **[RED]** Add `Incoders.Hive.Tests/Domain/Social/Publications/PublicationStatusTransitionsTests.cs` — table-driven: every legal pair (`Scheduled→Publishing`, `Scheduled→Cancelled`, `Publishing→Published`, `Publishing→PublishFailed`, `PublishFailed→Publishing`, `PublishFailed→Cancelled`) is valid; a representative set of illegal pairs is rejected; `IsTerminal` true only for `Published`/`Cancelled`.
- [x] 1.2 **[GREEN]** Create `Incoders.Hive.Domain/Social/Publications/PublicationStatus.cs`, `PublicationStatusTransitions.cs`, `PublicationErrors.cs` (`IllegalPublicationTransition`, `ItemNotPublishable`, `DestinationUnresolved`).
- [x] 1.3 Run `dotnet test` — Phase 1 domain tests green.

## Phase 2 — Domain: Publication VOs & Aggregate (PR 1, WU 1) — refs PUB-01, PUB-02, PUB-03, PUB-06

- [x] 2.1 **[RED]** Add `PublicationDestinationTests.cs` / `PublicationAttemptTests.cs` — `PublicationDestination(clientAccountConnectionId, socialNetworkId, networkKey)` value equality; `PublicationAttempt(attemptNumber, startedAtUtc, finishedAtUtc, outcome, errorMessage, externalReference)` value equality and ordering contract.
- [x] 2.2 **[GREEN]** Create `PublicationDestination.cs`, `PublicationAttempt.cs`, `PublicationAttemptOutcome.cs` (`Pending`/`Succeeded`/`Failed`).
- [x] 2.3 **[RED]** Add `PublicationTests.cs` — `PublishNow` starts `Publishing` with attempt #1 open (no `ScheduledForUtc`); `Schedule` rejects a past `scheduledForUtc` and starts `Scheduled`; `BeginPublishing` opens a new attempt; `MarkPublished` requires a non-empty external reference and closes the open attempt `Succeeded`; `MarkFailed` requires a non-empty error message and sets `PublishFailed`; `Retry` opens attempt `#N+1` only from `PublishFailed`; `Cancel` legal only from `Scheduled`/`PublishFailed`; `ReclaimStuck` moves `Publishing → PublishFailed` and closes the open attempt as `Failed` with the reclaim reason; every illegal transition returns `IllegalPublicationTransition` (never throws, never silently no-ops).
- [x] 2.4 **[GREEN]** Create `PublicationId.cs` and `Publication.cs` aggregate with `PublishNow`, `Schedule`, `Restore`, `BeginPublishing`, `MarkPublished`, `MarkFailed`, `Retry`, `Cancel`, `ReclaimStuck`, `AttemptCount`.
- [x] 2.5 **[RED]** Add `PublicationOrthogonalityTests.cs` — a `using`-graph assertion that `Domain/Social/Publications/**` imports no `Incoders.Hive.Domain.Social.ContentStudio` type (PUB-06 architecture-style guard, mirrors CS orthogonality discipline).
- [x] 2.6 Run `dotnet test` — Phase 1–2 domain tests green, no regressions.

## Phase 3 — Domain: ClientAccountConnection (PR 1, WU 1) — refs OQ-1 (design §4)

- [x] 3.1 **[RED]** Add `ClientAccountConnectionTests.cs` — `Create` rejects empty `externalAccountId` / empty ciphertext / empty `organizationIntegrationId`; `RotateToken` replaces ciphertext + expiry; `MarkTestResult(isSuccess, error, now)` sets `Connected`/`Failed` + `LastError`/`LastTestedAtUtc`; `Disable` is a terminal-ish guard (rejects further token rotation once disabled, per design intent).
- [x] 3.2 **[GREEN]** Create `ClientAccountConnectionId.cs`, `ClientAccountConnectionStatus.cs` (`Connected`/`Failed`/`Disabled`), `ClientAccountConnection.cs` aggregate.
- [x] 3.3 Run `dotnet test` — all Phase 1–3 domain tests green.

## Phase 4 — Infrastructure: Persistence (PR 1, WU 1) — refs design §8, §12

- [x] 4.1 Add ports under `Abstractions/Social/Publications/`: `IPublicationRepository.cs` (`GetByIdAsync`, `ListByContentItemsAsync`, `ListByClientAsync`, `AddAsync`, `UpdateAsync`) and `IClientAccountConnectionRepository.cs` (`GetByIdAsync`, `ListByClientAsync`, `GetByClientNetworkAsync`, `AddAsync`, `UpdateAsync`).
- [x] 4.2 Create EF entities `PublicationData.cs`, `PublicationAttemptData.cs`, `ClientAccountConnectionData.cs` (columns per design §8.1). Register `DbSet`s in `AppIdentityDbContext`; map attempts as an owned one-to-many collection with cascade; enums (`status`, `outcome`, connection `status`) via `HasConversion<short>()`; **no** navigation to `ContentStudioItemData` / `ClientOwnAccount` / `OrganizationIntegration` (soft Guids per design §1).
- [x] 4.3 Add fluent config: indexes per design §8.2 (`ix_publications_client`, `ix_publications_content_item`, `ix_publications_status`, `ix_publications_scheduled`; `publication_attempts` unique `(publication_id, attempt_number)` + index `(publication_id)`; `client_account_connections` unique `(organization_id, client_id, social_network_id)` + index `(organization_id, client_id)`); FKs to `platform.organizations` + `social.clients` (cascade) on `publications` and `client_account_connections`; `client_account_connections.organization_integration_id` FK to `platform.organization_integrations`; **no** FK from `publications.content_item_id`.
- [x] 4.4 Add EF migration `Add_Publications` (additive only): creates `social.publications`, `social.publication_attempts`, `social.client_account_connections`. No data migration, no existing table modified.
- [x] 4.5 Run gates: `pwsh -File scripts/gates/Run-Gates.ps1` — build + architecture gate must pass (no provider leakage; DbContext snapshot updated).
- [x] 4.6 **[RED]** Add `Incoders.Hive.Tests/Infrastructure/Persistence/PublicationEntityMappingTests.cs` and `ClientAccountConnectionEntityMappingTests.cs` — round-trip a publication with 2+ attempts (owned collection, enum conversions), and a connection (ciphertext column, status enum); assert `content_item_id` has no FK constraint.
- [x] 4.7 **[GREEN]** Create `EfPublicationRepository.cs` and `EfClientAccountConnectionRepository.cs` implementing the Phase 4.1 ports; `Restore(...)` mapping; no navigations.
- [x] 4.8 Run `dotnet test` + gates — mapping/repository tests green. **PR 1 checkpoint.**

## Phase 5 — Application: Connector Port & Credential Resolution (PR 2, WU 2) — refs PUB-04, PUB-05

- [ ] 5.1 **[RED]** Add `Incoders.Hive.Tests/Application/Social/Publications/PublicationDestinationResolverTests.cs` (fakes for `IClientAccountConnectionRepository` / `IIntegrationResolver` / `IIntegrationSecretProtector`) — resolves both levels when connected; fails with `DestinationUnresolved` when the org-level integration is not `Connected` (delegates to the existing `IntegrationNotConnected` from `ResolveProviderAsync`) or when the `ClientAccountConnection` is missing; the unprotected token never appears outside the returned request object.
- [ ] 5.2 **[GREEN]** Create `Abstractions/Social/Publications/IPublicationConnector.cs` (`NetworkKey`, `PublishAsync(ResolvedPublicationRequest, ct) → Result<PublicationResult>`), `ResolvedPublicationRequest.cs`, `PublicationResult.cs`, `PublicationAssetRef.cs`, `IPublicationConnectorSelector.cs`, and `Features/Social/Publications/PublicationDestinationResolver.cs` implementing design §6.2's 4-step resolution.
- [ ] 5.3 Run `dotnet test` — Application resolver tests green.

## Phase 6 — Infrastructure: Instagram Connector (PR 2, WU 2) — refs PUB-05

- [ ] 6.1 **[RED]** Add `Incoders.Hive.Tests/Infrastructure/Social/Publications/InstagramGraphPublicationConnectorTests.cs` — stubbed Graph `HttpMessageHandler`: success (media container create → publish) returns `Result<PublicationResult>` with the external media id; failure (non-2xx / provider error body) returns a failed `Result` with no exception; asserts no Graph SDK/HTTP type is required by the caller (compile-time — only domain/application types cross the port).
- [ ] 6.2 **[GREEN]** Create `Infrastructure/Social/Publications/InstagramGraphPublicationConnector.cs` implementing `IPublicationConnector` (`NetworkKey = "instagram"`); register it + `IPublicationConnectorSelector` in DI.
- [ ] 6.3 Run gates: `pwsh -File scripts/gates/Run-Gates.ps1` — architecture gate must confirm no Instagram/Graph API type is visible from Domain or Application.

## Phase 7 — Application: Credential Management Commands (PR 2, WU 2) — refs OQ-1 (design §2), INT-BANK-01

- [ ] 7.1 **[RED]** Add `ClientAccountConnectionCommandsTests.cs` — `CreateClientAccountConnectionCommand` protects the plaintext token via `IIntegrationSecretProtector.Protect` BEFORE constructing the aggregate (fake protector asserts it was called with the raw value, never persisted); `RotateClientAccountTokenCommand` re-protects and replaces ciphertext; `TestClientAccountConnectionCommand` calls the connector's verify path (or a cheap Graph `GET me` behind the same connector abstraction) and records `MarkTestResult`; `ListClientAccountConnectionsQuery` → `ClientAccountConnectionResponse` NEVER includes the token field, only `ExternalAccountId`/status/expiry/lastTested.
- [ ] 7.2 **[GREEN]** Create `Features/Social/Publications/Connections/**` (`CreateClientAccountConnectionCommand`, `RotateClientAccountTokenCommand`, `TestClientAccountConnectionCommand`, `ListClientAccountConnectionsQuery`) + handlers + validators; register in DI.
- [ ] 7.3 Run `dotnet test`.

## Phase 8 — Api: Connection Management Endpoints (PR 2, WU 2) — refs design §9, INT-BANK-01

- [ ] 8.1 Add connection routes to a new `Incoders.Hive.ApiService/Endpoints/Social/MapPublicationConnectionEndpoints.cs`: `GET /api/social/clients/{clientId}/account-connections`, `POST /api/social/clients/{clientId}/account-connections`, `PUT /api/social/account-connections/{connectionId}/token`, `POST /api/social/account-connections/{connectionId}/test`. Org-scope via `ICurrentTenant.TenantId`; gate connection-management routes to org `admin` role (design §15). Register in the API route builder.
- [ ] 8.2 **[RED]** Add `Incoders.Hive.Tests/Api/Social/PublicationConnectionEndpointTests.cs` — smoke tests: create returns a token-free DTO, list never includes the token, cross-organization denial, non-admin denial.
- [ ] 8.3 Run `dotnet test` + gates. **PR 2 checkpoint.**

## Phase 9 — Application: Publish-Now & Lifecycle Commands (PR 3, WU 3) — refs PUB-01, PUB-02, PUB-03, PUB-06

- [ ] 9.1 **[RED]** Add `PublishItemNowCommandTests.cs` — **gate test**: an item not in `{Approved, ReadyForPublication}` returns `ItemNotPublishable`, creates NO publication, and does not touch the item. Happy path: creates a `Publishing` publication (persist-before-call verified via fake repository call order), invokes the connector, `MarkPublished` on success / `MarkFailed` on connector failure, persists the final state.
- [ ] 9.2 **[GREEN]** Create `Features/Social/Publications/PublishNow/PublishItemNowCommand.cs` (+ handler + validator) implementing the design §7 flow.
- [ ] 9.3 **[RED]** Add `SchedulePublicationCommandTests.cs` (future-`scheduledForUtc` validation + same editorial gate), `RetryPublicationCommandTests.cs` (only from `PublishFailed`, re-runs the connector flow), `CancelPublicationCommandTests.cs` (`Scheduled | PublishFailed → Cancelled`), `ReclaimStuckPublicationCommandTests.cs` (staleness-threshold guard on the open attempt's `StartedAtUtc`; cannot race an in-flight publish), `GetPublicationQueryTests.cs` (→ `PublicationResponse` with attempts, scheduledForUtc, externalRef).
- [ ] 9.4 **[GREEN]** Create `Features/Social/Publications/{Schedule,Retry,Cancel,Reclaim,GetPublication}/**` command/query slices + handlers + validators; register in DI.
- [ ] 9.5 Run `dotnet test`.

## Phase 10 — Application: Content Bank Query & Lazy Materialize (PR 3, WU 3) — refs CB-01, CB-02, CB-03, CB-04, CS-BANK-01

- [ ] 10.1 **[RED]** Add `Incoders.Hive.Tests/Application/Social/ContentStudio/ContentItemRepositoryListByClientTests.cs` — contract test (fakes/in-memory) asserting: returns items across two different `ContentSet`s and origins for the client (CS-BANK-01 scenario 1); existing `GetByIdAsync`/`ListByRunAsync`/`ListBySetAsync` unchanged (CS-BANK-01 scenario 2); scoped strictly to the requesting client (CB-01 scenario 2).
- [ ] 10.2 **[GREEN]** Add `ContentBankFilter.cs` (`FromUtc?`, `ToUtc?`, `ReviewState?`) and `ListByClientAsync(organizationId, clientId, filter, ct)` to `IContentItemRepository`; implement in the existing EF adapter using `ix_content_studio_items_client_id` + `created_at_utc` range + optional `review_state` predicate, ordered `created_at_utc DESC`.
- [ ] 10.3 **[RED]** Add `ListClientContentBankQueryTests.cs` — returns cross-set/cross-origin items for the client (CB-01); applies date-range + state filters, and confirms no date range still returns full history (CB-02); resolves thumbnails via `StudioVisualMarkupResolver.ResolveBackgroundRefsAsync` (CB-03); attaches `PublicationSummaryDto[]` per item via `IPublicationRepository.ListByContentItemsAsync`.
- [ ] 10.4 **[GREEN]** Create `Features/Social/Publications/ListClientContentBank/ListClientContentBankQuery.cs` (+ handler), `ContentBankItemResponse.cs`, `PublicationSummaryDto.cs`.
- [ ] 10.5 **[RED]** Add `EnsureClientContentMaterializedCommandTests.cs` — materializes only completed-but-unmaterialized analyses for the client (CB-04 scenario 1); idempotent — a second run creates no duplicate `ContentSet`/`ContentItem`s (CB-04 scenario 2).
- [ ] 10.6 **[GREEN]** Create `Features/Social/Publications/EnsureClientContentMaterialized/EnsureClientContentMaterializedCommand.cs` (+ handler) orchestrating the existing `MaterializeAnalysisCommand` per unmaterialized completed analysis.
- [ ] 10.7 Run `dotnet test` + gates.

## Phase 11 — Api: Bank & Publication Endpoints (PR 3, WU 3) — refs design §9

- [ ] 11.1 Create `MapContentBankEndpoints.cs` (`GET /api/social/content-bank/clients/{clientId}/items` — runs `EnsureClientContentMaterializedCommand` before `ListClientContentBankQuery`; `POST /api/social/content-bank/items/{itemId}/publish`; `POST /api/social/content-bank/items/{itemId}/schedule`) and `MapPublicationEndpoints.cs` (`GET /api/social/publications/{publicationId}`; `POST .../retry`; `POST .../cancel`; `POST .../reclaim`). Org-scope every route via `ICurrentTenant.TenantId` (never from body — Tenant Isolation Rule). Map `IllegalPublicationTransition` → 409 `publications.illegal_transition`; `ItemNotPublishable` → 409 `publications.item_not_publishable`; `DestinationUnresolved` → 422/409 `publications.destination_unresolved`. Register both in the API route builder.
- [ ] 11.2 **[RED]** Add `Incoders.Hive.Tests/Api/Social/ContentBankEndpointTests.cs` and `PublicationEndpointTests.cs` — smoke tests: Bank listing runs lazy materialization first, publish-now happy path, editorial-gate 409, illegal-transition 409, reclaim happy path, cross-organization denial.
- [ ] 11.3 Run `dotnet test` + full gate suite.

## Phase 12 — Orthogonality & Architecture Assertions (PR 3, WU 3) — refs PUB-06, design §12

- [ ] 12.1 **[RED]** Add `PublicationEditorialOrthogonalityTests.cs` — a full publish cycle (`PublishNow → MarkPublished`) leaves the source `ContentItem` row byte-for-byte unchanged (`ReviewState` still `Approved`, `ContentDraft` untouched); asserts no `Publications` command type references `IContentItemRepository` beyond a read-only load.
- [ ] 12.2 **[GREEN]** (only if the assertion surfaces a gap — expected to already pass given §6.3/§7 design).
- [ ] 12.3 Run gates: `pwsh -File scripts/gates/Run-Gates.ps1` — architecture gate green. **PR 3 checkpoint.**

## Phase 13 — Frontend: Publications Workspace (PR 4, WU 4) — refs design §10

- [ ] 13.1 Create `apps/web/lib/api/publications.ts` (mirrors `content-studio.ts`) — typed client for Bank items, publications (publish/schedule/retry/cancel/reclaim), and account connections, per the DTOs in design §9.
- [ ] 13.2 Replace `apps/web/app/(dashboard)/publications/page.tsx` (currently `PlaceholderPage`) with `PublicationsPage` → `PublicationsWorkspace` (container: client selector, date-range filter state, fetches Bank items, holds shared item+publication data, renders tab strip `[Bank · Planner · Published]`).
- [ ] 13.3 **[P]** Build **Bank tab**: `ContentBankGrid` (presentational grid, thumbnails from resolved markup, date-range picker defaulting to last 7 days per OQ-4, `ReviewState` filter chips), `DestinationPicker` (lists the client's Instagram `ClientAccountConnection`s), "Publish now" action on Approved/ReadyForPublication cards (Schedule action hidden in v1 per design §15). Clicking a card opens `content-studio-panel.tsx` (reused verbatim, unmodified).
- [ ] 13.4 **[P]** Build **Planner tab**: `PublicationPlannerBoard` (presentational kanban) with columns per design §2/OQ-3 (`Ready · Scheduled · Publishing · Published · Failed` lane, `Rejected` as a filter chip not a column, `Cancelled` hidden-by-default via filter). Card actions issue commands (publish-now/retry/cancel); the board is a pure projection, never owns state. Multi-destination items render as multiple independent publication-cards.
- [ ] 13.5 **[P]** Build **Published tab**: `PublishedList` (recency-weighted list of `Published` publications with external reference links, filterable by range).
- [ ] 13.6 Build shared components: `PublicationCard.tsx`, `PublicationStatusBadge.tsx`, `AttemptHistoryPopover.tsx`.
- [ ] 13.7 Build `ClientAccountConnectionsPanel.tsx` (settings surface reachable from the client context; token input is write-only — never pre-filled, never returned; gated to org-admin per design §15).
- [ ] 13.8 Frontend build + type-check pass; component tests for `ContentBankGrid`, `PublicationPlannerBoard` column placement, and `PublicationCard`/`PublicationStatusBadge` rendering. **PR 4 checkpoint.**

## Phase 14 — Docs Sync, ADR, and Cleanup (PR 5, WU 5) — refs PUB-01–PUB-06, CB-01–CB-04, CS-BANK-01, INT-BANK-01, INT-BANK-02

- [ ] 14.1 Author `docs/architecture/adr/0005-publication-axis-and-two-level-credentials.md` (context, decision, rationale, rejected alternatives, consequences per design §14; depth mirrors `0004-content-studio-review-model.md`).
- [ ] 14.2 Move `openspec/specs/publishing/spec.md` from the TBD stub to the merged v1 spec (this change's `specs/publishing/spec.md` is canonical); add `openspec/specs/content-bank/spec.md`; merge the `content-studio` and `integrations` deltas into `openspec/specs/content-studio/spec.md` and `openspec/specs/integrations/spec.md`. **Do NOT** merge the older unmerged `content-studio` publishing handoff delta (`openspec/changes/content-studio/specs/publishing/spec-delta.md`) — it is superseded per design §16.
- [ ] 14.3 Add a Content Bank Planner section to `docs/PRD.md` and `docs/SDD.md` (mirror the Content Studio "Mundo" section); reference ADR 0005.
- [ ] 14.4 Update `openspec/changes/content-bank-planner/state.yaml` — mark phases 4–7 progress, `migrations[0].applied: true`, `delivery.strategy`/`delivery.prs` recorded.
- [ ] 14.5 Final gate run: `pwsh -File scripts/gates/Run-Gates.ps1` — all MUST gates green before declaring done; no `--no-verify`, no MUST-gate skip. **PR 5 checkpoint.**
