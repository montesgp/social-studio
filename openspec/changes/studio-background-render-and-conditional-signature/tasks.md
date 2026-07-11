# Tasks: Studio Background Rendering (#58) + Conditional @handle Signature (#70)

Delivery strategy: `ask-on-risk`. Two independent capabilities — A (studio/frontend
read path) and B (generation/composer gating) — ship as separate slices/PRs. Strict
TDD is ACTIVE: every code WU starts with a failing test, then implementation to green.

Test runner (backend): `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c
Release`. Not SAC-blocked in recent sessions — run locally; if the block returns,
verify GREEN via CI dispatch. Frontend: `cd apps/web && npm run build && npm run lint`.

**Simplification override (maintainer, supersedes design/spec tri-state):** Capability
B is CLIENT-FIELD-ONLY. NO profile-analysis detection, NO tri-state `bool?`. Use a
plain `bool Client.SignsOwnSlides` (default `false`). Sign IFF `SignsOwnSlides ==
true`. The design's "detected" branch and the spec's tri-state/opt-out language are
dropped — treated as a documented deviation, not implemented. Terminology: "opt-in to
signing" everywhere (code, docs, ABM copy) — normalize away from the spec's "opt-out"
title wording.

Legend: `[P]` = can run in parallel with sibling `[P]` tasks.

---

## Capability A — Studio Background Rendering (#58)

### WU-A0 — Resolve design's open question: envelope source at query time
**Satisfies:** design Open Question / Residual Risk ("A envelope source"); gates WU-A1.

- [ ] Trace `EditItemVisualCommand.cs` (`Incoders.Hive.Application/Features/Social/ContentStudio/Operations/EditItemVisualCommand.cs`)
      and confirm whether it overwrites `item.Draft.VisualSpecMarkup` with edited markup
      only, or preserves the full `composition_spec_json` written by
      `MaterializeRunCommand.cs:81-85`.
- [ ] Document the finding inline (code comment on the new query, WU-A1) and choose the
      resolution source accordingly: `item.Draft.VisualSpecMarkup` if envelopes survive
      edits, otherwise the immutable format-result `composition_spec_json`.
- **Verify:** written confirmation (comment + this task checked) before WU-A1 starts;
  no test required (research task).
- **Depends on:** nothing.

### WU-A1 — Query: `GetContentItemSlideBackgroundQuery` resolves `BackgroundImageRef`
**Satisfies:** SBR-01 (all 3 scenarios), SBR-02 (no `FormatAsset` row).

- [ ] TDD: add failing tests in
      `Incoders.Hive.Tests/Application/Social/ContentStudio/GetContentItemSlideBackgroundQueryTests.cs`
      covering: storage-path ref resolves via `IGeneratedAssetStorage`; legacy `data:`
      URI decodes inline (mirrors `PptxExportAdapter.ResolvePictureBytesAsync`,
      `PptxExportAdapter.cs:106-114`); null/blank ref → not-found result; out-of-range
      or tombstoned `slideIndex` → not-found; storage open failure → not-found (no
      throw); asserts NO write to `content_generation_format_assets`/`Draft.Assets`.
- [ ] Create `Incoders.Hive.Application/Features/Social/ContentStudio/Queries/GetContentItemSlideBackgroundQuery.cs`
      parsing item envelopes per WU-A0's resolved source, resolving
      `envelope[slideIndex].BackgroundImageRef` via the branch above, streaming bytes.
      No mutation of `item.Draft`.
- **Verify:** new query tests GREEN.
- **Depends on:** WU-A0.

### WU-A2 — Endpoint: `GET /items/{itemId}/slides/{n}/background` [P]
**Satisfies:** SBR-01, SBR-02 (wiring only — no new persistence).

- [ ] TDD: add/extend an endpoint-mapping/integration test asserting the route exists,
      returns the image stream for a resolvable slide, and 404s for null/out-of-range
      per WU-A1's not-found cases.
- [ ] Modify `Incoders.Hive.ApiService/Endpoints/Social/MapContentStudioEndpoints.cs`:
      map `GET /items/{itemId}/slides/{n:int}/background` to `GetContentItemSlideBackgroundQuery`.
- **Verify:** endpoint test GREEN.
- **Depends on:** WU-A1.

### WU-A3 — ZIP/listing/Canva/delete non-regression guard
**Satisfies:** SBR-02 scenarios (deliverable count unchanged, delete/regenerate unaffected).

- [ ] Add/extend a test asserting that after rendering backgrounds via WU-A1/A2, ZIP
      package assembly, package listing, Canva export, and delete/regenerate
      deliverable counts are byte-for-byte unchanged vs. baseline (no extra
      `FormatAssetRow`).
- **Verify:** guard test GREEN.
- **Depends on:** WU-A1.

### WU-A4 — Frontend: `fetchSlideBackground` API helper [P]
**Satisfies:** SBR-01 (fetch by URL/reference, not embedded inline).

- [ ] Modify `apps/web/lib/api/content-generation.ts` (+ `apps/web/lib/api/types.ts`
      as needed): add `fetchSlideBackground(itemId, slideIndex, token)` calling
      WU-A2's endpoint.
- **Verify:** `npm run build` + `npm run lint` pass; manual/unit check of the helper's
  request shape if the file has existing test coverage.
- **Depends on:** WU-A2 (endpoint contract must exist, though FE can stub during dev).

### WU-A5 — Frontend: render background layer in studio panel
**Satisfies:** SBR-01 (all 3 scenarios rendered in UI).

- [ ] Modify `apps/web/components/social/content-studio-panel.tsx`
      (`SlideThumbnail` ~1680, `AssetFallbackImage` ~1740): call WU-A4's helper per
      slide, render the AI background layer BEHIND the composed layout/text layer;
      no background present → render layout/text only, no broken-image state.
- **Verify:** `npm run build` + `npm run lint` pass; manual check against a slide with
  a storage-path background, a legacy `data:` background, and no background.
- **Depends on:** WU-A4.

---

## Capability B — Conditional @handle Signature (#70, simplified — plain boolean)

### WU-B1 — Domain: `Client.SignsOwnSlides` boolean field [P]
**Satisfies:** CAC-CG-03 (opt-in field), CAC-CG-05 (default-off migration behavior).

- [ ] TDD: add a failing test on `Client` (mirror existing `BackgroundSourcingPolicy`
      setter tests) asserting `SignsOwnSlides` defaults to `false` and
      `SetSignsOwnSlides(bool, nowUtc)` updates it.
- [ ] Modify `Incoders.Hive.Domain/Social/Clients/Client.cs`: add
      `bool SignsOwnSlides { get; private set; } = false` +
      `SetSignsOwnSlides(bool value, DateTime nowUtc)`.
- **Verify:** new domain test GREEN.
- **Depends on:** nothing.

### WU-B2 — EF: entity mapping + migration [P]
**Satisfies:** CAC-CG-05 (additive, no backfill, opt-out-by-default), replayability
constraint from user instructions.

- [ ] Modify `Incoders.Hive.Infrastructure/Persistence/Postgres/Entities/SocialClientData.cs`
      (+ EF configuration): add `bool SignsOwnSlides` column mapping, default `false`.
- [ ] Create migration `Incoders.Hive.Infrastructure/Persistence/Postgres/Migrations/*_Add_SignsOwnSlides.cs`:
      `signs_own_slides boolean NOT NULL DEFAULT false` on `social.clients`. Down =
      drop column.
- [ ] Verify the full migration chain replays from empty (`dotnet ef database update`
      against a clean DB, or existing migration-chain test if the project has one).
- **Verify:** migration applies cleanly; existing `EfSocialClientRepositoryPolicyTests`
  / `ContentStudioEntityMappingTests` unaffected; new mapping round-trip test GREEN.
- **Depends on:** WU-B1.

### WU-B3 — Snapshot: load `SignsOwnSlides` into `ClientProfileSnapshot`
**Satisfies:** CAC-CG-03 (signal reaches the composer via `BrandSnapshot`).

- [ ] TDD: extend `Incoders.Hive.Tests/Infrastructure/Social/ContentGeneration/ClientBrandSnapshotLoaderRoleTests.cs`
      with a failing case asserting `ClientProfileSnapshot.SignsOwnSlides` reflects the
      client row's value.
- [ ] Modify `Incoders.Hive.Domain/Social/ContentGeneration/ClientProfileSnapshot.cs`:
      add `bool SignsOwnSlides = false` (additive).
- [ ] Modify `Incoders.Hive.Infrastructure/Social/ContentGeneration/EfClientBrandSnapshotLoader.cs`
      (`:107-114`): load `clientRow.SignsOwnSlides` into the snapshot.
- **Verify:** loader test GREEN.
- **Depends on:** WU-B2.

### WU-B4 — Gate: `AttributionPolicy.ShouldSign(brand)`
**Satisfies:** CAC-CG-03 (gate: Handle present AND SignsOwnSlides == true).

- [ ] TDD: add a failing test on `AttributionPolicy` covering: Handle present +
      SignsOwnSlides true → sign; Handle present + SignsOwnSlides false → don't sign;
      Handle null + SignsOwnSlides true → don't sign (Handle stays necessary).
- [ ] Modify `.../Shared/AttributionPolicy.cs`: add
      `ShouldSign(BrandSnapshot brand) => brand.Handle is not null && brand.SignsOwnSlides;`
      Leave `ResolveSignature` untouched.
- **Verify:** new `AttributionPolicy` tests GREEN.
- **Depends on:** WU-B3.

### WU-B5 — Composers: gate `@handle`/signature slot on `ShouldSign`
**Satisfies:** CAC-CG-03 (all scenarios), CAC-CG-05 (existing handled clients default
unsigned).

- [ ] TDD: reconcile `Incoders.Hive.Tests/Application/Social/ContentGeneration/Visual/CarouselVisualComposerHandleSlotTests.cs`
      and the story composer test suite — replace "Handle non-null → signed" cases
      with `ShouldSign`-gated cases per WU-B4; add the "Handle present but
      SignsOwnSlides false → slot omitted" regression case (CAC-CG-05 migration
      behavior).
- [ ] Modify `.../Visual/Composers/CarouselVisualComposer.cs` (`:150-157`): replace the
      `Handle is not null` guard with `AttributionPolicy.ShouldSign(brand)`.
- [ ] Modify `.../Visual/Composers/StoryVisualComposer.cs` (`:34-38`): same gate.
- **Verify:** reconciled composer test suites GREEN; both composers reach the same
  signing decision for identical `SignsOwnSlides`/`Handle` state.
- **Depends on:** WU-B4.

### WU-B6 — ABM: expose `SignsOwnSlides` in client create/update API [P]
**Satisfies:** CAC-CG-03 (opt-in field is settable).

- [ ] TDD: extend `Incoders.Hive.Tests/Api/Social/SocialClientsEndpointPolicyTests.cs`
      (mirror `BackgroundSourcingPolicy` coverage) with failing cases for
      create/update round-tripping `SignsOwnSlides`.
- [ ] Modify `Incoders.Hive.Application/Features/Social/Clients/Create/CreateSocialClientCommand.cs`
      + `CreateSocialClientHandler.cs`, `Update/UpdateSocialClientCommand.cs` +
      `UpdateSocialClientHandler.cs`, `SocialClientResponse.cs`: add
      `SignsOwnSlides` (default `false`) to request/response/handler wiring.
- [ ] Modify `Incoders.Hive.ApiService/Endpoints/Social/MapSocialClientsEndpoints.cs`
      if request/response shape needs explicit mapping.
- **Verify:** endpoint policy tests GREEN.
- **Depends on:** WU-B2.

### WU-B7 — Frontend: client form "signs their slides" toggle
**Satisfies:** CAC-CG-03 (opt-in is user-settable via ABM UI).

- [ ] Modify `apps/web/lib/api/social-clients.ts` (client types/API calls),
      `apps/web/components/social/clients/use-client-form.ts`, and
      `apps/web/components/social/clients/client-form-panel.tsx`: add a "Client signs
      their slides" toggle, default off, wired to WU-B6's field.
- **Verify:** `npm run build` + `npm run lint` pass; manual toggle check against
  create/update flows.
- **Depends on:** WU-B6.

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~520-680 total (A: ~230-300; B: ~290-380) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 = Capability A (WU-A0..A5, ~230-300 lines); PR 2 = Capability B (WU-B1..B7, ~290-380 lines) — independent, either order |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| WU-A0..A5 | Studio background rendering (#58): query, endpoint, non-regression guard, FE helper + render | PR 1 | Independent of Capability B; base = feature/tracker or main depending on chosen chain strategy |
| WU-B1..B7 | Conditional @handle signing (#70, plain boolean): domain field, migration, snapshot, gate, composers, ABM API + FE toggle | PR 2 | Independent of Capability A; can proceed in parallel or as the second slice |

Rationale for High risk: Capability A touches a new query, new endpoint, a
non-regression guard test, and 2 frontend files (~230-300 lines with tests).
Capability B touches domain + EF entity + migration + snapshot + loader + policy +
2 composers + ABM (create/update/response/endpoint) + 2 frontend files (~290-380
lines with tests). Combined, comfortably over the 400-line single-PR budget; each
capability alone is close to or under budget and should ship as its own PR.

## Cross-Cutting Notes

- WU-A0 is a research/confirmation task (design's open question) — MUST complete
  before WU-A1 implements the query against the correct envelope source.
- Capability A and B share no files and no runtime coupling — safe to parallelize
  across two work streams once each capability's internal WU-dependency order is
  respected.
- CAC-CG-05's "migration" requirement is satisfied structurally by WU-B1/B2's
  default-`false`, no-backfill migration — no separate data-migration task needed.
- Deviation from spec: the tri-state (`bool?`) and profile-analysis "detected" signal
  described in the spec's CAC-CG-03/CAC-CG-05 scenarios are NOT implemented per the
  maintainer's simplification — only the explicit opt-in field scenarios apply.
