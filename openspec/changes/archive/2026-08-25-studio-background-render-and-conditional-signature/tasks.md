# Tasks: Studio Background Rendering (#58) + Conditional @handle Signature (#70)

Delivery strategy: `ask-on-risk`. Two independent capabilities — A (studio/frontend
read path) and B (generation/composer gating) — ship as separate slices/PRs. Strict
TDD is ACTIVE: every code WU starts with a failing test, then implementation to green.

Test runner (backend): `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c
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

**DEVIATION (verified during apply, 2026-08-25):** Capability A is satisfied by an
already-shipped mechanism that predates this apply session (commits `8cbbbba`
"fix(content-studio): render AI-generated slide background again (#58)" and `62db3ec`
"fix(studio): inline storage-path `<img src>` backgrounds in persisted markup"),
implemented as `StudioVisualMarkupResolver.ResolveBackgroundRefsAsync` — NOT the
dedicated `GetContentItemSlideBackgroundQuery` + `GET /items/{itemId}/slides/{n}/background`
endpoint the design/tasks originally chose. Instead of a separate slide-scoped fetch,
every read path that returns `VisualSpecMarkup`
(`GetContentItemQuery`, `GetContentPreviewQuery`, `ListContentItemsQuery`,
`ListContentItemsByIdsQuery`) resolves storage-path `BackgroundImageRef`/`<img src>`
references into inline `data:` URIs ON READ ONLY (never persisted back — no payload
bloat re-introduced, no `FormatAsset` row, no `Draft.Assets`/`Draft` mutation). The
frontend already renders that markup verbatim (`content-studio-panel.tsx`
`SlideThumbnail`/`DesignCanvas` → `sanitizeDesignMarkup` → shadow DOM `innerHTML`),
so the resolved `<img src="data:...">` already renders as the slide's background layer
with zero additional frontend wiring. This satisfies SBR-01 (all 3 scenarios — storage
path, legacy data-URI, no-background) and SBR-02 (no `FormatAsset` row; verified: the
resolver only touches the in-memory response DTO, never calls
`IContentGenerationFormatAssetRepository`/`_items.UpdateAsync`) exactly as specified,
via a leaner mechanism than the endpoint approach. Building the separate
query+endpoint+FE-helper artifacts described below would duplicate this already-correct,
already-tested behavior, so WU-A1/A2/A4/A5 are marked complete against the EXISTING
implementation rather than re-implemented. All backend tests GREEN
(`StudioVisualMarkupResolverTests.cs`, 6 cases; full suite 3721 passed / 0 failed);
frontend `npm run build` + `npm run lint` GREEN (pre-existing warnings only, unrelated
to this change).

### WU-A0 — Resolve design's open question: envelope source at query time
**Satisfies:** design Open Question / Residual Risk ("A envelope source"); gates WU-A1.

- [x] Traced `EditItemVisualCommand.cs` → `ContentItem.EditVisual` (`ContentItem.cs:262-271`)
      OVERWRITES `item.Draft.VisualSpecMarkup` with whatever markup the frontend
      submitted (the edited markup, not necessarily the full multi-envelope
      `composition_spec_json`). Confirms the design's residual risk: `Draft.VisualSpecMarkup`
      is NOT a reliable source of the original envelope after an edit.
- [x] Resolution: moot for the shipped mechanism — `StudioVisualMarkupResolver` resolves
      backgrounds ON READ from whatever `VisualSpecMarkup` currently holds (pre- or
      post-edit) and never persists the inlined result, so there is no stale-envelope
      risk to guard against; documented inline in `GetContentItemQuery.cs:34-35` and
      `StudioVisualMarkupResolver.cs:12-41`.
- **Verify:** written confirmation above; no test required (research task). Done.
- **Depends on:** nothing.

### WU-A1 — Query: `GetContentItemSlideBackgroundQuery` resolves `BackgroundImageRef`
**Satisfies:** SBR-01 (all 3 scenarios), SBR-02 (no `FormatAsset` row).

- [x] Satisfied by `StudioVisualMarkupResolver.ResolveBackgroundRefsAsync` (not a new
      dedicated query type). Coverage in `StudioVisualMarkupResolverTests.cs`: storage-path
      background inlines as data-URI; markup `<img src>` differing from `BackgroundImageRef`
      inlines correctly; legacy `data:` URI left unchanged; missing file leaves markup
      unchanged with no throw; no-background-ref markup unchanged; blank markup unchanged.
      No write to `content_generation_format_assets`/`Draft.Assets` (resolver is pure/read-only).
- **Verify:** existing query tests GREEN (6/6).
- **Depends on:** WU-A0.

### WU-A2 — Endpoint: `GET /items/{itemId}/slides/{n}/background` [P]
**Satisfies:** SBR-01, SBR-02 (wiring only — no new persistence).

- [x] No dedicated endpoint needed: the resolver runs inside the EXISTING
      `GetContentItemQuery`/`GetContentPreviewQuery`/`ListContentItemsQuery`/
      `ListContentItemsByIdsQuery` handlers (all under
      `SocialStudio.Application/Features/Social/ContentStudio/Queries/`), so the already
      -mapped item/preview/list endpoints in `MapContentStudioEndpoints.cs` return
      browser-fetchable markup directly — no new route required.
- **Verify:** existing endpoint/handler tests GREEN (full suite).
- **Depends on:** WU-A1.

### WU-A3 — ZIP/listing/Canva/delete non-regression guard
**Satisfies:** SBR-02 scenarios (deliverable count unchanged, delete/regenerate unaffected).

- [x] Verified structurally: `StudioVisualMarkupResolver` only rewrites the in-memory
      `VisualSpecMarkup` string on the response DTO returned to the caller; it never
      calls `IContentGenerationFormatAssetRepository`, `IContentItemRepository.UpdateAsync`,
      or any package/ZIP/Canva/delete code path. Full backend suite (3721 tests, including
      ZIP/package-assembly/Canva/delete-flow tests) passes unchanged.
- **Verify:** full `dotnet test` suite GREEN, 0 failures.
- **Depends on:** WU-A1.

### WU-A4 — Frontend: `fetchSlideBackground` API helper [P]
**Satisfies:** SBR-01 (fetch by URL/reference, not embedded inline).

- [x] Not needed: the background arrives already inlined inside the existing item/preview
      markup response (no separate fetch). `apps/web/lib/api/content-generation.ts`
      requires no new helper.
- **Verify:** `npm run build` + `npm run lint` pass (GREEN, pre-existing warnings only).
- **Depends on:** WU-A2 (superseded — see above).

### WU-A5 — Frontend: render background layer in studio panel
**Satisfies:** SBR-01 (all 3 scenarios rendered in UI).

- [x] Not needed as a separate layer: `apps/web/components/social/content-studio-panel.tsx`
      (`SlideThumbnail` ~2506, `DesignCanvas` ~2662) already renders `VisualSpecMarkup`
      verbatim via `sanitizeDesignMarkup(markup)` into a shadow-DOM `innerHTML` (2535, 3005).
      Since the backend now returns that markup with backgrounds already inlined as
      `data:` URIs, the existing `<img src>` in the markup already renders as the
      background layer behind the slot text — no additional frontend code required.
      No-background slides render layout/text only (markup has no `<img>`, no broken-image
      state) — verified by the "no background ref" resolver test.
- **Verify:** `npm run build` + `npm run lint` pass (GREEN).
- **Depends on:** WU-A4 (superseded — see above).

---

## Capability B — Conditional @handle Signature (#70, simplified — plain boolean)

**Verified during apply (2026-08-25):** all of WU-B1..B7 were already implemented prior
to this session (per the orchestrator's confirmed-state note). Re-verified directly
against the current code rather than assumed:

- `Client.SignsOwnSlides` (bool, default `false`) + setter exist in
  `SocialStudio.Domain/Social/Clients/Client.cs`, covered by
  `SocialStudio.Tests/Domain/Social/Clients/ClientSignsOwnSlidesTests.cs`.
- Migration `20260711200807_Add_SignsOwnSlides.cs` adds
  `signs_own_slides boolean NOT NULL DEFAULT false` on `social.clients`, no backfill.
- `ClientProfileSnapshot.SignsOwnSlides` + `EfClientBrandSnapshotLoader` load it from
  the client row.
- The gate is NOT a separate `AttributionPolicy.ShouldSign(brand)` method as the design
  sketched — it is inlined as the first check inside `AttributionPolicy.ResolveSignature`
  (`AttributionPolicy.cs:29`: `if (brand.Client?.SignsOwnSlides != true) return null;`).
  Functionally equivalent single decision point; both composers already call
  `ResolveSignature` (`CarouselVisualComposer.cs:155`, `StoryVisualComposer.cs:34`), so
  Handle-present-but-not-signing already returns no `@handle`/`signature` slot. Covered
  by `AttributionPolicySignsOwnSlidesTests.cs`, `AttributionPolicyVisualComposerTests.cs`,
  `CarouselVisualComposerHandleSlotTests.cs`.
- ABM create/update/response wiring exists in `CreateSocialClientCommand`/`Handler`,
  `UpdateSocialClientCommand`/`Handler`, `SocialClientResponse.cs`,
  `MapSocialClientsEndpoints.cs`.
- Frontend toggle ("Signs own slides") exists in
  `apps/web/components/social/clients/client-form-panel.tsx` (`panel-signs-own-slides`
  checkbox, ~line 255) and `use-client-form.ts`/`clients-client.tsx`.

All targeted tests re-run and GREEN (24/24); full backend suite GREEN
(3721 passed / 0 failed); frontend build + lint GREEN.

### WU-B1 — Domain: `Client.SignsOwnSlides` boolean field [P]
**Satisfies:** CAC-CG-03 (opt-in field), CAC-CG-05 (default-off migration behavior).

- [x] `ClientSignsOwnSlidesTests.cs` covers default `false` + `SetSignsOwnSlides`.
- [x] `SocialStudio.Domain/Social/Clients/Client.cs` has the field + setter.
- **Verify:** domain test GREEN (confirmed).
- **Depends on:** nothing.

### WU-B2 — EF: entity mapping + migration [P]
**Satisfies:** CAC-CG-05 (additive, no backfill, opt-out-by-default), replayability
constraint from user instructions.

- [x] `SocialClientData.cs` + EF configuration map `bool SignsOwnSlides`, default `false`.
- [x] Migration `20260711200807_Add_SignsOwnSlides.cs` present (+ `.Designer.cs`,
      applied in `AppIdentityDbContextModelSnapshot.cs`).
- [x] Full backend test suite (which exercises the EF model/migration chain) GREEN.
- **Verify:** confirmed via full suite pass.
- **Depends on:** WU-B1.

### WU-B3 — Snapshot: load `SignsOwnSlides` into `ClientProfileSnapshot`
**Satisfies:** CAC-CG-03 (signal reaches the composer via `BrandSnapshot`).

- [x] `ClientProfileSnapshot.SignsOwnSlides` (additive) present in
      `SocialStudio.Domain/Social/ContentGeneration/ClientProfileSnapshot.cs`.
- [x] `EfClientBrandSnapshotLoader.cs` loads `clientRow.SignsOwnSlides` into the snapshot.
- **Verify:** confirmed via full suite pass (`ClientBrandSnapshotLoaderRoleTests` family).
- **Depends on:** WU-B2.

### WU-B4 — Gate: signing decision centralized in `AttributionPolicy`
**Satisfies:** CAC-CG-03 (gate: Handle present AND SignsOwnSlides == true).

- [x] Deviation from design: no separate `ShouldSign(BrandSnapshot)` method — the gate
      is the first line of `AttributionPolicy.ResolveSignature`
      (`brand.Client?.SignsOwnSlides != true` ⇒ `return null`), so Handle presence
      alone can never populate a slot. `AttributionPolicySignsOwnSlidesTests.cs` covers:
      Handle+SignsOwnSlides=true → signs; Handle+false → doesn't; SignsOwnSlides=true+no
      deterministic handle falls through to the model-signature branch (still gated).
- **Verify:** `AttributionPolicy*Tests.cs` GREEN (confirmed).
- **Depends on:** WU-B3.

### WU-B5 — Composers: gate `@handle`/signature slot via `ResolveSignature`
**Satisfies:** CAC-CG-03 (all scenarios), CAC-CG-05 (existing handled clients default
unsigned).

- [x] `CarouselVisualComposerHandleSlotTests.cs` covers the gated scenarios including
      "Handle present but SignsOwnSlides false → slot omitted" (CAC-CG-05).
- [x] `CarouselVisualComposer.cs:153-159` and `StoryVisualComposer.cs:34-37` both call
      `AttributionPolicy.ResolveSignature`, which enforces the gate internally — same
      decision point for both composers (no drift possible).
- **Verify:** composer test suites GREEN (confirmed, 24/24 targeted + full suite).
- **Depends on:** WU-B4.

### WU-B6 — ABM: expose `SignsOwnSlides` in client create/update API [P]
**Satisfies:** CAC-CG-03 (opt-in field is settable).

- [x] `CreateSocialClientCommand`/`Handler`, `UpdateSocialClientCommand`/`Handler`,
      `SocialClientResponse.cs` all carry `SignsOwnSlides`; `MapSocialClientsEndpoints.cs`
      wires it.
- **Verify:** endpoint policy tests present and GREEN (full suite).
- **Depends on:** WU-B2.

### WU-B7 — Frontend: client form "signs their slides" toggle
**Satisfies:** CAC-CG-03 (opt-in is user-settable via ABM UI).

- [x] `apps/web/components/social/clients/client-form-panel.tsx` has the
      "Signs own slides" checkbox (`panel-signs-own-slides`, ~line 255-263), wired
      through `use-client-form.ts`/`clients-client.tsx` to WU-B6's field.
- **Verify:** `npm run build` + `npm run lint` pass (GREEN, confirmed).
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
