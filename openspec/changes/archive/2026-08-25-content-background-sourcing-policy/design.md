# Design — content-background-sourcing-policy

Hexagonal design for the per-client **background sourcing policy** (decision tree A/B/C, extensible to D), the **carousel-coherence fix** (one shared plan over per-slide selection), the **structured color-role scale**, and **advisory concision**. Every provider concern (EF Core, Apify, the configured AI provider, ASP.NET) stays behind an Application port with an Infrastructure adapter; Domain and Application stay provider-free. This document fixes the open questions, the component map, the new flow (component + sequence), the coordinated contract-change blast radius with a **test-first ordering**, the migration/backfill plan, and risks/mitigations. It does **not** enumerate task steps — that is the `sdd-tasks` phase.

## Status
Design — architecture decisions resolved. Implements the technical HOW for `proposal.md`. Architecture change (new Application port family + a domain-contract shape change) → an ADR under `docs/architecture/adr/` is authored at apply time (`require_adr_for_architecture_changes`).

## Scope reminder (Non-Goals are hard boundaries)
Covers the **shape, ports, persistence, flow, and contracts** of the policy + coherence + color-role + concision work. It does **not** build CASE C's real post-image pipeline (Apify `displayUrl` extraction — deferred), does **not** add a hard concision validator, and does **not** ship any concrete CASE D strategy (seam only). Rendering, guardrails, and the AI-designer/Chromium stack are **reused, not replaced**.

---

## 0. The decisions (read this first)

| # | Open question | Decision | One-line rationale |
|---|---------------|----------|--------------------|
| **OQ-1** | CASE A multi-bg selection: deterministic or random? | **Deterministic, seeded by `(JobId)` with a date fallback.** A pure function `index = stableHash(seed, slideIndex) % setCount` rotates the set; stable on retry. | Retries/regenerations must reproduce the same carousel unless inputs change (coherence + reviewability). Truly random would re-shuffle on every retry. |
| **OQ-2** | Color-role representation | **`ColorRole` enum `{ Primary, Secondary, Accent, Neutral }` + integer `Rank`** on `ColorEntry`. Prevalence = role ordinal then `Rank`. | Enum gives a stable, queryable, UI-orderable scale; `Rank` breaks ties and orders multiple entries within a role. Free-text `Role?` is migrated into the enum. |
| **OQ-3** | CASE C: text descriptors enough for MVP? | **Yes — text-descriptor MVP ships.** `DetectedBackgroundImages` is passed as aesthetic guidance via a new `BackgroundSelection.ClientAesthetic` variant. Apify image-URL extraction is the deferred follow-up. | The seam delivers value now with zero Apify change; the actor's image-URL capability is unconfirmed (open dependency), so it cannot be committed. |
| **OQ-4** | Policy on `Client` vs sibling aggregate | **On the `Client` aggregate now** (nullable `BackgroundSourcingPolicy`), one migration. Sibling `ClientGenerationPolicy` recorded as the future home if CASE D needs richer config. | Policy is client-level and currently a single scalar; a sibling aggregate is ceremony with no present payoff. The strategy seam makes a later extraction non-breaking. |
| **OQ-5** | DU/plan shape | **Add a `CarouselBackgroundPlan` value object (per-slide `BackgroundSelection[]`) AND a closed new variant `BackgroundSelection.ClientAesthetic(descriptors)`.** The plan is the carousel-level decision; the variant is the per-slide payload for CASE B/C. | The plan solves coherence (one decision, N slides); the variant keeps the designer's per-slide consumption a single exhaustive switch. Both are pure domain types. |
| **OQ-6** | Policy ↔ regeneration | **Regeneration honors the SAME policy and re-derives the plan deterministically.** Seed = `JobId` (stable across a job's retries). A `RegenerationObjection` does NOT change the seed; it only changes copy inputs, so the plan stays stable unless the objection itself alters set membership/policy. | Deterministic seed = stable plan on retry; the objection steers copy, not background rotation, so coherence survives a correction loop. |

Each decision is expanded in [Decisions in depth](#9-decisions-in-depth).

---

## 1. Architecture approach

Dependency rule, unchanged: **Domain ← Application ← Infrastructure / Api**. No `DocumentFormat.OpenXml`/EF/Npgsql/Apify/`HttpClient`/ASP.NET types in Domain or Application.

- **Domain** (`Domain/Social/`) gains: a `BackgroundSourcingPolicy` value type on `Client`; a structured `ColorRole` + `Rank` on `ColorEntry`; a `WeightedColor` shape in `BrandManualSnapshot.ColorPalette`; a `CarouselBackgroundPlan` value object and a new `BackgroundSelection.ClientAesthetic` variant. All pure, `Result<T>`-validated, with `Restore(...)` for trusted EF rehydration.
- **Application** (`Features/Social/ContentGeneration/**`, ports under `Abstractions/Social/ContentGeneration/`) gains: an `IBackgroundSourcingStrategy` port (one implementation per CASE A/B/C), an `IBackgroundStrategyResolver` that maps `policy → strategy`, and the processor refactor that computes ONE `CarouselBackgroundPlan` before the per-slide loop.
- **Infrastructure** carries only wiring: load the policy and the structured palette in `EfClientBrandSnapshotLoader`; surface the weighted palette + aesthetic descriptors in `AiVisualDesigner`'s prompt DTOs; the EF migration (policy column on `social.clients`, jsonb shape evolution on `color_palette`); DI registration of the strategies/resolver.
- **Api** extends the existing client + brand-manual endpoint groups with the policy field and the color-role ordering.
- **Frontend** adds a policy selector to the client form and a role/rank ordering control to the brand-manual color editor.

The strategy family lives in **Application**; the AI/Apify wiring stays in **Infrastructure** adapters. `BackgroundSelector` (today a Domain static) becomes the **legacy/CASE-A adapter** behind the new port — it is wrapped, not deleted, so the default (UNSET) path reproduces today's 3-tier behavior exactly.

---

## 2. Component map — ports, adapters, layers

### 2.1 Domain (pure)

| Element | Kind | Path | Change |
|---------|------|------|--------|
| `BackgroundSourcingPolicy` | enum value type | `Domain/Social/Clients/BackgroundSourcingPolicy.cs` | **New** — `{ Unset = 0, ClientDefinedSet = 1, AiPalette = 2, ClientAesthetic = 3 }` |
| `Client` | aggregate | `Domain/Social/Clients/Client.cs` | **Modified** — nullable `BackgroundSourcingPolicy? Policy`; `Create`/`Restore` + a `SetBackgroundSourcingPolicy(...)` mutator |
| `ColorRole` | enum | `Domain/Social/ClientBrandManuals/ColorRole.cs` | **New** — `{ Primary = 0, Secondary = 1, Accent = 2, Neutral = 3 }` |
| `ColorEntry` | record | `Domain/Social/ClientBrandManuals/ColorEntry.cs` | **Modified** — `Role` becomes `ColorRole?` + `int Rank`; validation + back-compat `Restore` |
| `WeightedColor` | record | `Domain/Social/ContentGeneration/WeightedColor.cs` | **New** — `(string Hex, ColorRole? Role, int Rank)` snapshot shape |
| `BrandManualSnapshot.ColorPalette` | record field | `Domain/Social/ContentGeneration/BrandManualSnapshot.cs` | **Modified (breaking)** — `IReadOnlyList<string>` → `IReadOnlyList<WeightedColor>` |
| `BackgroundSelection.ClientAesthetic` | DU variant | `Domain/Social/ContentGeneration/BackgroundSelection.cs` | **New** — `ClientAesthetic(IReadOnlyList<string> Descriptors)` |
| `CarouselBackgroundPlan` | value object | `Domain/Social/ContentGeneration/CarouselBackgroundPlan.cs` | **New** — carousel-level plan: `Policy`, `IReadOnlyList<BackgroundSelection> PerSlide`, `string SeedToken` |
| `BackgroundSelector` | static selector | `Domain/Social/ContentGeneration/BackgroundSelector.cs` | **Modified** — gains a set-rotation helper for CASE A; stays the legacy 3-tier engine |

### 2.2 Application (ports + use cases)

| Element | Kind | Path | Change |
|---------|------|------|--------|
| `IBackgroundSourcingStrategy` | Port | `Application/Abstractions/Social/ContentGeneration/IBackgroundSourcingStrategy.cs` | **New** |
| `IBackgroundStrategyResolver` | Port | `Application/Abstractions/Social/ContentGeneration/IBackgroundStrategyResolver.cs` | **New** — `policy → strategy` |
| `LegacyTierStrategy` | Adapter (wraps `BackgroundSelector`) | `Application/Features/Social/ContentGeneration/Background/LegacyTierStrategy.cs` | **New** — UNSET/null default |
| `ClientDefinedSetStrategy` (CASE A) | Strategy | `…/Background/ClientDefinedSetStrategy.cs` | **New** — deterministic set rotation |
| `AiPaletteStrategy` (CASE B) | Strategy | `…/Background/AiPaletteStrategy.cs` | **New** — emits `Creative` per slide; palette weighting feeds the designer |
| `ClientAestheticStrategy` (CASE C) | Strategy | `…/Background/ClientAestheticStrategy.cs` | **New** — emits `ClientAesthetic(descriptors)` per slide |
| `BackgroundStrategyResolver` | Adapter | `…/Background/BackgroundStrategyResolver.cs` | **New** — DI-backed map; unknown policy → `LegacyTierStrategy` |
| `IClientAestheticDescriptorReader` | Port (CASE C seam) | `Application/Abstractions/Social/ContentGeneration/IClientAestheticDescriptorReader.cs` | **New** — read-only `DetectedBackgroundImages` for `(orgId, clientId)` |
| `ContentGenerationJobProcessor` | Use case | `…/Pipeline/ContentGenerationJobProcessor.cs` | **Modified** — pre-loop plan; `ResolveEnforcedBackground` set rotation |
| `CarouselVisualComposer` | Composer | `…/Visual/Composers/CarouselVisualComposer.cs` | **Modified** — consume the plan, drop the redundant `BackgroundSelector` call |
| `GenerationPromptContext` | Prompt builder | `…/Shared/GenerationPromptContext.cs` | **Modified** — weighted palette + shared `ConcisionConstraint` |
| `Carousel/Story/FeedPostGenerator` | Generators | `…/Formats/**` | **Modified** — concision directives in prompts |
| `ContentGenerationPromptVersions` | Constants | `…/Shared/ContentGenerationPromptVersions.cs` | **Modified** — bump touched format versions |
| `VisualGuardrailValidator` | Guardrail | `…/Visual/VisualGuardrailValidator.cs` | **Modified** — palette check reads `WeightedColor.Hex` |

### 2.3 Infrastructure (adapters + persistence)

| Element | Change | Where |
|---------|--------|-------|
| `SocialClientData` | **Modified** — `BackgroundSourcingPolicy smallint NULL` | `…/Entities/SocialClientData.cs` |
| `social.clients` mapping | **Modified** — map the nullable enum via `HasConversion<short?>()`, column `background_sourcing_policy` | `Infrastructure/Identity/AppIdentityDbContext.cs` (`SocialClientData` block) |
| `color_palette` jsonb shape | **Modified** — `ColorEntry` jsonb gains `role` (smallint?) + `rank` (int) | same `ConfigureJsonbCollection<ClientBrandManualData, ColorEntry>` mapping; no relational column change |
| EF migration `Add_BackgroundSourcingPolicy_And_ColorRole` | **New** — add the policy column; jsonb backfill of `role`/`rank` | `…/Migrations/` — the only new migration |
| `EfClientBrandSnapshotLoader` | **Modified** — load `Client.Policy`; map palette to `WeightedColor` (line 102) | `…/Social/ContentGeneration/EfClientBrandSnapshotLoader.cs` |
| `EfClientAestheticDescriptorReader` | **New** — reads `ProfileAiAnalysis.DetectedBackgroundImages` (read-only) | `…/Social/ContentGeneration/EfClientAestheticDescriptorReader.cs` |
| `AiVisualDesigner` | **Modified** — weighted palette in DTO + prompt; `ClientAesthetic` descriptors guidance | `…/Social/ContentGeneration/AiVisualDesigner.cs` |
| DI registration | **New** — strategies, resolver, descriptor reader | Infrastructure DI module |

> **Naming correction (verified):** the table is `social.clients` (entity `SocialClientData`, mapped `entity.ToTable("clients", SocialSchema)`), NOT `social.social_clients` as the proposal phrased it. The migration targets `social.clients`. Likewise `ColorPalette` is persisted as a **jsonb collection of the `ColorEntry` domain record** (`AppIdentityDbContext.cs:588`), so the role/rank change is a **jsonb shape evolution**, not a relational column migration — materially lighter than the proposal assumed.

### 2.4 Api + Frontend

| Element | Change | Where |
|---------|--------|-------|
| Social-clients endpoints | **Modified** — accept/return `backgroundSourcingPolicy` on create/update/get | `ApiService/Endpoints/Social/MapSocialClientsEndpoints.cs` |
| Brand-manual endpoints | **Modified** — `ColorPalette` entries carry `role` + `rank` | `ApiService/Endpoints/Social/MapClientBrandManualsEndpoints.cs` |
| Client form | **Modified** — policy selector (UNSET default with legacy explainer) | `apps/web/components/social/clients/client-form-panel.tsx` (+ `use-client-form.ts`) |
| Brand-manual color editor | **Modified** — role dropdown + rank/reorder per color | `apps/web/components/social/clients/client-brand-manual-section.tsx` |
| Web API client | **Modified** — policy + role fields | `apps/web/lib/api/*` (social clients + brand manuals) |

---

## 3. Domain model (the new + changed types)

### 3.1 `BackgroundSourcingPolicy` (on `Client`)
```
public enum BackgroundSourcingPolicy
{
    Unset           = 0,  // legacy 3-tier behavior (default for existing + new clients until set)
    ClientDefinedSet = 1, // CASE A — combine the client's background SET, carousel-coherent
    AiPalette        = 2, // CASE B — fully AI-generated honoring palette + scale
    ClientAesthetic  = 3  // CASE C — aesthetic from profile-analysis text descriptors (seam)
}
```
- Stored nullable on `Client` (`BackgroundSourcingPolicy? Policy`). `null` ≡ `Unset` ≡ legacy. The enum value `Unset = 0` exists so the resolver can treat "explicitly chose legacy" and "never set" identically without a null check leaking into strategy code.
- `Client.SetBackgroundSourcingPolicy(BackgroundSourcingPolicy? policy, DateTime nowUtc)` mutator; `Create`/`Restore` extended with the nullable field (defaulted, so all existing call sites compile).

### 3.2 `ColorRole` + `ColorEntry` (structured scale, OQ-2)
```
public enum ColorRole { Primary = 0, Secondary = 1, Accent = 2, Neutral = 3 }

public sealed record ColorEntry(string Name, string Hex, ColorRole? Role, int Rank)
{
    public static Result<ColorEntry> Create(string name, string hex, ColorRole? role, int rank);
    // Back-compat trusted rehydration (jsonb may carry null role / rank from pre-migration rows):
    public static ColorEntry Restore(string name, string hex, ColorRole? role, int rank);
}
```
- Prevalence ordering = `(Role ordinal, Rank, original index)`. A null `Role` sorts **after** all classified roles (treated as unranked neutral) so legacy palettes keep a deterministic order.
- `Rank` defaults to `0`; the UI assigns ranks within a role when the user reorders. Validation: `Rank >= 0`; `Role` optional.
- The previous free-text `Role?` (max 50 chars) is migrated: a best-effort case-insensitive map (`"primary"→Primary`, `"secondary"→Secondary`, `"accent"→Accent`, `"neutral"→Neutral`); unrecognized strings → `null` role (the value is dropped, logged once at migration time).

### 3.3 `WeightedColor` + `BrandManualSnapshot` (the breaking contract change)
```
public sealed record WeightedColor(string Hex, ColorRole? Role, int Rank);

public sealed record BrandManualSnapshot(
    IReadOnlyList<WeightedColor> ColorPalette,   // WAS IReadOnlyList<string>
    IReadOnlyList<string> Logos,
    IReadOnlyList<BrandBackgroundTemplateSnapshot> BackgroundTemplates,
    IReadOnlyList<string> Typography,
    string? VoiceAndTone,
    string? Notes);
```
- This is the **frozen contract change** flagged HIGH in the proposal. Every consumer of `BrandManualSnapshot.ColorPalette` changes in the SAME unit (blast radius in §6).
- `WeightedColor` is the snapshot mirror of `ColorEntry` (snapshot types must not reference `ClientBrandManuals` aggregates — `WeightedColor` lives in `ContentGeneration` alongside the other snapshot types, consistent with `BrandBackgroundTemplateSnapshot`).
- The palette is materialized **pre-sorted by prevalence** at the loader so every consumer receives the scale order for free.

### 3.4 `BackgroundSelection` — new variant (OQ-5)
```
public abstract record BackgroundSelection
{
    public sealed record ForcedDefault(string Url, string Name) : BackgroundSelection;
    public sealed record References(IReadOnlyList<BrandBackgroundTemplateSnapshot> Entries) : BackgroundSelection;
    public sealed record Creative : BackgroundSelection;
    public sealed record ClientAesthetic(IReadOnlyList<string> Descriptors) : BackgroundSelection; // NEW (CASE C)
}
```
- `ClientAesthetic` is the per-slide payload for CASE C: the designer receives the client's known aesthetic descriptors and generates freely *within* that aesthetic. It is a `Creative`-like behavior with extra guidance — never hard-enforced (no URL to enforce). Adding the variant forces every exhaustive `switch` (the designer, `ResolveEnforcedBackground`, the composer) to handle it — that compile-time pressure is the point.

### 3.5 `CarouselBackgroundPlan` — the coherence type (OQ-5)
```
public sealed record CarouselBackgroundPlan(
    BackgroundSourcingPolicy Policy,
    IReadOnlyList<BackgroundSelection> PerSlide,   // index i → slide i+1
    string SeedToken)                              // audit: the seed used (JobId or date fallback)
{
    public BackgroundSelection ForSlide(int slideIndex);   // 1-based; clamps to last on overflow
    public bool IsCoherent { get; }                        // all entries share the same style family
}
```
- For single-asset formats (story, feed-post) the plan has exactly one entry; the same type covers singles and carousels (no special case).
- The plan is computed **once per format per job** before the slide loop; the per-slide selection is a pure read from `PerSlide`.

---

## 4. Application ports

### 4.1 `IBackgroundSourcingStrategy` (the port)
```
public interface IBackgroundSourcingStrategy
{
    BackgroundSourcingPolicy Policy { get; }   // which policy this strategy serves

    // Produces ONE carousel-level plan for all slides of a format, BEFORE the slide loop.
    CarouselBackgroundPlan Plan(BackgroundPlanRequest request);
}

public sealed record BackgroundPlanRequest(
    BrandManualSnapshot Brand,                              // backgrounds + weighted palette
    string FormatKey,                                       // "carousel" | "story-single" | "feed-single" | ...
    int SlideCount,                                         // 1 for singles, N for carousels
    string SeedToken,                                       // deterministic seed (JobId; date fallback)
    IReadOnlyList<string> ClientAestheticDescriptors);      // empty unless CASE C (pre-resolved by the processor)
```
- **Inputs**: client policy (implicit in which strategy was resolved), brand snapshot (backgrounds + weighted palette), `FormatKey`, slide count, deterministic seed, and the CASE-C descriptors (pre-fetched so the strategy stays free of the descriptor port — keeps strategies pure and unit-testable without a repository fake).
- **Output**: a `CarouselBackgroundPlan` (per-slide `BackgroundSelection`).
- The port is framework-free: parameters/returns are Domain records + `Result`-free pure return (a plan is always producible; emptiness degrades to `Creative`). This mirrors the `IVisualDesignerContractTests.Port_HasNoInfrastructureTypes` discipline.

### 4.2 `IBackgroundStrategyResolver` (policy → strategy)
```
public interface IBackgroundStrategyResolver
{
    IBackgroundSourcingStrategy Resolve(BackgroundSourcingPolicy? policy);
}
```
- `null`/`Unset`/unknown → `LegacyTierStrategy` (backward-compatible default). The resolver is a thin map over the DI-registered `IBackgroundSourcingStrategy` set, keyed by `strategy.Policy`. **CASE D plugs in here**: register a new `IBackgroundSourcingStrategy` with a new `BackgroundSourcingPolicy` value — A/B/C are untouched (open/closed verified by a test that resolves an injected fake D without editing the resolver).

### 4.3 The strategies

| Strategy | Policy | `Plan(...)` behavior |
|----------|--------|----------------------|
| `LegacyTierStrategy` | `Unset` | For each slide, returns `BackgroundSelector.Select(brand.BackgroundTemplates, formatKey)`. Identical to today **except** it is now computed once per slide *index* into a plan; the result per slide is the same deterministic 3-tier selection (so the default path is behavior-preserving). |
| `ClientDefinedSetStrategy` | `ClientDefinedSet` (CASE A) | Builds the eligible background SET (manual templates applicable to the format). `setCount == 0` → `Creative` for all slides. `setCount == 1` → that one `ForcedDefault`-equivalent `References([single])` for ALL slides (strict). `setCount > 1` → **deterministic rotation**: slide `i` → `References([ set[ stableHash(seed, i) % setCount ] ])`, guaranteeing one coherent style family across the carousel while varying per slide. |
| `AiPaletteStrategy` | `AiPalette` (CASE B) | Returns `Creative` for every slide (the AI designs the background); the weighted palette in `BrandManualSnapshot` drives the designer prompt. No template enforcement. |
| `ClientAestheticStrategy` | `ClientAesthetic` (CASE C) | Returns `ClientAesthetic(descriptors)` for every slide when descriptors are present; empty descriptors degrade to `Creative` (logged). |

- `stableHash` is a pure deterministic hash (e.g. FNV-1a over `seed + ":" + slideIndex`), defined in Domain so the rotation is testable without infrastructure and reproducible across processes.

### 4.4 `IClientAestheticDescriptorReader` (CASE C seam)
```
public interface IClientAestheticDescriptorReader
{
    Task<IReadOnlyList<string>> GetDescriptorsAsync(Guid orgId, Guid clientId, CancellationToken ct);
}
```
- Read-only port over the existing profile-analysis aggregate; the Infrastructure adapter reads the latest `ProfileAiAnalysis.DetectedBackgroundImages` for the client. **No** Apify call, **no** analysis behavior change. When no analysis exists → empty list → CASE C degrades to `Creative`. The deferred Apify image-URL path would later add a richer port returning URLs; this text-descriptor port is the stable MVP seam.

---

## 5. New flow — component + sequence

### 5.1 Component view (the pre-loop plan)
```
ContentGenerationJobProcessor.RunAsync
  brand = BrandSnapshot (now carries Client.Policy via ClientProfileSnapshot.Policy)
  │
  ├─ policy = brand.Client?.Policy
  ├─ strategy = IBackgroundStrategyResolver.Resolve(policy)
  │
  └─ for each formatKey in job.RequestedFormats:
        specs = composer.ComposeManyAsync(formatKey, payload, brand)   // N specs for carousel
        descriptors = (policy == ClientAesthetic)
                        ? await IClientAestheticDescriptorReader.GetDescriptorsAsync(org, client)
                        : []
        ┌─────────────────────────────────────────────────────────────┐
        │ plan = strategy.Plan(new BackgroundPlanRequest(              │  ← ONE plan, BEFORE the slide loop
        │            brand.Brand, formatKey, specs.Count,              │
        │            seedToken: job.Id.ToString(), descriptors))       │
        └─────────────────────────────────────────────────────────────┘
        for specIndex in 0..specs.Count-1:
            selection = plan.ForSlide(specIndex + 1)        // pure read — no per-slide re-decision
            designed  = designer.ComposeAsync(..., selection)
            enforced  = ResolveEnforcedBackground(selection, plan, specIndex)   // set-rotation aware
            markup    = enforced is not null ? TemplateBacked(enforced) : aiMarkup ?? Fallback
            render + persist
```
- The composer no longer calls `BackgroundSelector` itself (§7.2). The processor owns the single plan; the composer receives the plan or its per-slide selection so both call sites read the SAME decision (kills the divergence at `CarouselVisualComposer.cs:61`).

### 5.2 Sequence — CASE A carousel (deterministic set rotation)
```
Processor
  → resolver.Resolve(ClientDefinedSet) → ClientDefinedSetStrategy
  → strategy.Plan(brand, "carousel", slideCount=4, seed=jobId, descriptors=[])
      → set = eligible templates for "carousel" = [bgA, bgB, bgC]
      → per slide i: References([ set[ stableHash(jobId, i) % 3 ] ])
      → CarouselBackgroundPlan { Policy=ClientDefinedSet, PerSlide=[A,C,B,A], SeedToken=jobId }
  → loop slide 1..4:
       selection_i = plan.ForSlide(i)                       // References([A]), References([C]), …
       designer.ComposeAsync(spec_i, selection_i)           // text/components over that bg
       ResolveEnforcedBackground(selection_i, plan, i-1)    // enforce that slide's rotated template
  → 4 coherent, varied slides; re-running the job (same jobId) yields the identical plan
```

### 5.3 Sequence — CASE B single + carousel (palette-weighted AI)
```
Processor
  → resolver.Resolve(AiPalette) → AiPaletteStrategy
  → strategy.Plan(...) → PerSlide = [Creative, Creative, …]    // AI designs every background
  → designer.ComposeAsync(spec, Creative)
       AiVisualDesigner.BuildInputJson:
         brandPalette = brand.ColorPalette (WeightedColor[], pre-sorted)   ← prevalence/weighting
         system prompt: "Use --brand-primary most, --brand-accent sparingly" derived from roles/rank
  → AI honors the scale; guardrail palette check reads WeightedColor.Hex (advisory, unchanged level)
```

---

## 6. Coordinated contract change — blast radius + test-first ordering (Strict TDD)

The `BrandManualSnapshot.ColorPalette` shape change (`IReadOnlyList<string>` → `IReadOnlyList<WeightedColor>`) is a **single coordinated unit**. Strict TDD is active (`dotnet test`): write/adjust the failing test first, then the production change, per row, in this order.

| # | Unit (test-first) | What the test pins | Then change |
|---|-------------------|--------------------|-------------|
| 1 | `ColorEntry` role/rank validation + free-text migration map | `Create`/`Restore` with `ColorRole?` + `Rank`; `"primary"→Primary`, unknown→null | `ColorEntry.cs` |
| 2 | `WeightedColor` + `BrandManualSnapshot` shape | snapshot exposes `IReadOnlyList<WeightedColor>`, pre-sorted by prevalence | `WeightedColor.cs`, `BrandManualSnapshot.cs` |
| 3 | `VisualGuardrailValidator` palette check | off-palette detection now reads `WeightedColor.Hex` (advisory level unchanged) | `VisualGuardrailValidator.ValidatePalette` |
| 4 | `BuildTemplateBackedMarkup` palette gradient | deterministic gradient still builds from hex values out of `WeightedColor` | `ContentGenerationJobProcessor` markup builder |
| 5 | `AiVisualDesigner.BuildInputJson` / `BuildSystemPrompt` | DTO carries weighted palette; prompt expresses prevalence (primary dominant, accent sparing) | `AiVisualDesigner.cs` |
| 6 | `GenerationPromptContext.FromBrand` | `colorPalette` projection emits `{hex, role, rank}` | `GenerationPromptContext.cs` |
| 7 | `EfClientBrandSnapshotLoader` mapping (line 102) | loader maps `ColorEntry`→`WeightedColor` (role + rank carried, pre-sorted); loads `Client.Policy` | `EfClientBrandSnapshotLoader.cs` |
| 8 | `BrandSnapshot.FromBrandData` default empty brand | the empty-brand fallback uses `Array.Empty<WeightedColor>()` | `BrandSnapshot.cs` |
| 9 | Existing snapshot/guardrail/loader tests | all `ColorPalette` test fixtures updated from `["#fff"]` to `[new WeightedColor("#fff", …)]` | test fixtures |

The compiler is the safety net: until every consumer is updated the solution does not build, so the breaking change cannot ship half-applied. Items 1–9 land in ONE work unit (one PR or one stacked slice) because partial application leaves the build red.

### 6.1 Coherence refactor (separate test-first sequence, HIGH risk)
| # | Test-first | Pins | Then change |
|---|-----------|------|-------------|
| C1 | `CarouselBackgroundPlan.ForSlide` + `stableHash` | 1-based slide read; rotation deterministic for a fixed seed; clamp on overflow | new domain types |
| C2 | `ClientDefinedSetStrategy.Plan` | setCount 0/1/N behavior; same seed → same plan | strategy |
| C3 | `LegacyTierStrategy.Plan` parity | plan per slide == today's `BackgroundSelector.Select` per slide (behavior preservation) | strategy |
| C4 | Processor: plan computed ONCE before the loop | one `strategy.Plan` call per format; N `plan.ForSlide` reads; no per-slide `BackgroundSelector` | processor refactor |
| C5 | `CarouselVisualComposer` consumes the plan | composer's `AssetSlots` background == `plan.ForSlide(i)` (no second selector call) | composer refactor |
| C6 | `ResolveEnforcedBackground` set rotation | for `References([rotatedSingle])` the enforced template is that slide's rotated entry, not always `Entries[0]` | processor helper |

> **Why C6 matters (verified):** today `ResolveEnforcedBackground` takes `References.Entries.FirstOrDefault(... Url)` — first-entry-wins (`ContentGenerationJobProcessor.cs:955`). CASE A rotation puts a DIFFERENT single-entry `References([rotated])` per slide into the plan, so first-entry-wins now correctly enforces the rotated template **per slide** (the plan already did the rotation; the helper just enforces whatever single entry the plan put on that slide). No signature change is strictly required, but C6 pins that the per-slide selection — not a shared `Entries[0]` — is what gets enforced. If a strategy ever emits a multi-entry `References` per slide, the helper's first-entry-wins must consult the plan's slide index; the test fixes that contract now.

---

## 7. Processor + composer refactor (concrete)

### 7.1 `ContentGenerationJobProcessor` (the per-slide loop, ~286-398; `ResolveEnforcedBackground` ~943)
- **Before the format's slide loop** (after `specs` is known, ~line 286): resolve the strategy once for the job, fetch CASE-C descriptors only when the policy is `ClientAesthetic`, and compute `plan = strategy.Plan(...)`.
- **Inside the loop** (replacing line 315-316): `var selection = plan.ForSlide(specIndex + 1);` — delete the per-slide `BackgroundSelector.Select(...)` call.
- `ResolveEnforcedBackground(selection)` stays, now fed the per-slide selection from the plan (C6). `ClientAesthetic` returns `null` from `ResolveEnforcedBackground` (nothing to enforce — it is creative-with-guidance), so the AI markup path is taken.
- Seed: `job.Id.ToString()`. If `job.Id` is somehow empty, fall back to a date token (`yyyy-MM-dd`) — recorded in `plan.SeedToken` for audit (OQ-1/OQ-6).

### 7.2 `CarouselVisualComposer` (line 61 redundant call)
- Today `BuildSlideSpec` calls `BackgroundSelector.Select(templates, FormatKey)` per slide for the `AssetSlots` background hint. This is the **second, uncoordinated** call site.
- Refactor: the composer receives the `CarouselBackgroundPlan` (threaded through `ComposeManyAsync`'s context or a per-call parameter) and reads `plan.ForSlide(slideIndex)` for the slot hint — the SAME decision the processor enforces. The composer no longer imports `BackgroundSelector`.
- `SurfaceScopedCarouselVisualComposer` (the `story-carousel`/`feed-carousel` decorator) inherits the change for free — it delegates to the same inner composer.

---

## 8. Advisory concision (prompt-only)

### 8.1 Shared constraint
Add a sibling to `GenerationPromptContext.BrandConstraint`:
```
public const string ConcisionConstraint =
    "CONCISION (advisory, firm): social formats are skim-read. Singles (story, feed) = ONE super-summary " +
    "idea; carousels = ONE concept molded across slides, conceptual not exhaustive. Per text slot: short, " +
    "punchy, scannable; prefer phrases over sentences; avoid paragraphs. Headlines tight; body minimal; " +
    "no walls of text. Quality over length.";
```
- Each generator (`CarouselGenerator`, `StoryGenerator`, `FeedPostGenerator`) appends `ConcisionConstraint` to its `instruction` and adds per-slot length hints to its `expectedOutput` shape (e.g. `title = "string - max ~6 words"`, `body = "string|null - max ~2 short lines"`). Carousel additionally states "one concept across slides; ~3-5 slides".

### 8.2 Logging, not rejection
- Concision is **advisory** — no hard validator (explicit non-goal). The existing guardrail pipeline already logs advisory palette/typography deviations; mirror that for length: after copy generation, a lightweight, log-only check (estimated length per slot vs. the slot's hinted ceiling) emits a structured `LogWarning` ("concision advisory: slot {Slot} exceeds ~{N} chars") and renders anyway. No new `Result` failure, no re-reject loop.

### 8.3 Prompt-version bumps
- Bump ONLY the touched formats: `Carousel = "carousel.v3"`, `Story = "story-single.v2"`, `FeedPost = "feed-single.v2"`. Untouched formats (reel-script, caption, hook, etc.) keep their versions. Accept the first-run cache-invalidation cost; advisory-only avoids re-reject loops that would compound the spike.

---

## 9. Decisions in depth

### OQ-1 / OQ-6 — Deterministic, seeded CASE A rotation; regeneration honors the same plan
**Decision.** Rotation is a pure function of `(SeedToken, slideIndex)` with `SeedToken = JobId`. A retry of the same job re-derives the identical plan; a `RegenerationObjection` changes copy inputs but NOT the seed, so the background plan is stable across a correction loop unless the objection itself changes the policy or the eligible set.
**Rationale.** Carousel coherence and reviewability demand reproducibility: the reviewer who re-runs a job must see the same backgrounds. Truly random rotation would re-shuffle on every retry, defeating "fix one thing without disturbing the rest." `JobId` is already the natural per-run identity threaded through the processor.
**Rejected.** Truly random per render (non-reproducible, breaks retry stability). Seeding by `DateTime.Now` (changes every run). Seeding by client id (identical across all that client's carousels — too sticky).

### OQ-2 — Enum + rank (not free text, not a single ordered list)
**Decision.** `ColorRole { Primary, Secondary, Accent, Neutral }` + integer `Rank`.
**Rationale.** The enum gives a stable, semantically meaningful scale the designer prompt can weight ("primary dominant, accent sparing"), the guardrail can reason over, and the UI can render as a labeled dropdown. `Rank` orders multiple colors within a role (e.g. two accents). A single ordered list loses the *semantic* role (which one is the accent vs. just "third color"); free text (today) is unsortable and unmappable to prompt weighting.
**Rejected.** Keep free-text `Role?` (cannot weight or sort reliably). Single ordered `string[]` (loses role semantics the designer needs).

### OQ-3 — CASE C text-descriptor MVP; Apify image URLs deferred
**Decision.** Ship `BackgroundSelection.ClientAesthetic(descriptors)` fed by `ProfileAiAnalysis.DetectedBackgroundImages` (text). No Apify change.
**Rationale.** Verified: `ProfilePost` has no image-URL field and `DetectedBackgroundImages` returns text descriptions only (exploration WU-01). The configured Apify actor's ability to return post `displayUrl`s is **unconfirmed** — committing the image path now would be building on an unverified dependency. The text seam delivers aesthetic guidance immediately and the richer port is an additive follow-up (a new `IClientAestheticImageReader` returning URLs) once the actor capability is confirmed.
**Rejected.** Block CASE C until the image pipeline exists (delays all value for an unconfirmed dependency). Extend Apify speculatively now (risk: actor can't do it).

### OQ-4 — Policy on `Client`, not a sibling aggregate
**Decision.** Nullable `BackgroundSourcingPolicy` on the `Client` aggregate; one additive migration on `social.clients`.
**Rationale.** Policy is client-level (independent of brand-manual version — a manual can be re-versioned without changing how backgrounds are sourced) and is currently a single scalar. A `ClientGenerationPolicy` sibling aggregate is real ceremony (new table, FK, repository, lifecycle) with no present payoff. The strategy seam means a later extraction to a sibling aggregate is non-breaking (the resolver reads a policy value regardless of where it is stored).
**Rejected.** `ClientGenerationPolicy` sibling now (premature). Policy on `ClientBrandManual` (couples policy to manual version — wrong semantic boundary, versioning complications).

### OQ-5 — Plan value object + closed new DU variant
**Decision.** Add BOTH a `CarouselBackgroundPlan` (carousel-level decision) and a `BackgroundSelection.ClientAesthetic` variant (per-slide payload).
**Rationale.** Two distinct concerns. Coherence is a *carousel-level* decision (one plan, N slides) → a plan value object owns it. The per-slide *payload* the designer consumes is still a `BackgroundSelection` → CASE C needs a fourth variant so the designer's exhaustive switch handles aesthetic guidance explicitly rather than smuggling descriptors through a side channel. Threading descriptors through a separate channel (e.g. an extra designer parameter) would split the background contract across two inputs and lose the compile-time exhaustiveness the closed DU gives.
**Rejected.** Plan only, keep DU closed at 3 (CASE C descriptors would need an out-of-band channel — leaky). New variant only, no plan (coherence has nowhere to live — back to per-slide divergence).

---

## 10. Persistence + migration + backfill

### 10.1 Policy column (relational, additive)
- `social.clients` gains `background_sourcing_policy smallint NULL`. EF: `SocialClientData.BackgroundSourcingPolicy` (`BackgroundSourcingPolicy?`) mapped `HasConversion<short?>()` in the `SocialClientData` block. Enum→`smallint` keeps the provider mapping out of Domain.
- Backfill: none — `NULL` is the legacy default by design; existing + new clients without an explicit policy run the `LegacyTierStrategy`.

### 10.2 Color role/rank (jsonb shape evolution, NOT relational)
- Verified: `color_palette` is a **jsonb collection of the `ColorEntry` record** (`ConfigureJsonbCollection<ClientBrandManualData, ColorEntry>`, `AppIdentityDbContext.cs:588`). Adding `Role` (smallint?) + `Rank` (int) to `ColorEntry` evolves the jsonb document shape — no `ALTER TABLE` for the colors.
- **Backfill of existing jsonb rows**: a data step in the migration rewrites each `client_brand_manuals.color_palette` document: map the legacy free-text `role` string to the new `role` enum ordinal via the case-insensitive map (§3.2), set `rank = arrayIndex` (preserve current order), drop unrecognized role strings to `null`. Run as a guarded SQL/EF data migration over existing rows; idempotent (re-running maps already-numeric roles to themselves).
- Forward-compat: new `ColorEntry.Restore` tolerates missing `role`/`rank` keys (older un-backfilled documents) → `null` role, `rank = 0`. So the read path is safe even if backfill is staged separately.

### 10.3 Single migration
`Add_BackgroundSourcingPolicy_And_ColorRole` — (a) add the policy column, (b) backfill the `color_palette` jsonb role/rank. The only new migration. Gate-verified (`pwsh -File scripts/gates/Run-Gates.ps1`).

---

## 11. Architecture-gate compliance
- **No provider types in Domain/Application.** `BackgroundSourcingPolicy`, `ColorRole`, `ColorEntry`, `WeightedColor`, `BackgroundSelection.*`, `CarouselBackgroundPlan`, `IBackgroundSourcingStrategy`, `IBackgroundStrategyResolver`, `IClientAestheticDescriptorReader` are pure. EF mapping (enum→`smallint`, jsonb shape) lives only in `AppIdentityDbContext`/`*Data`. Apify/AI calls stay in Infrastructure adapters.
- **Strategy port in Application; AI/Apify wiring in Infrastructure** (proposal boundary). The strategies are pure (they take a snapshot + pre-fetched descriptors, return a plan) — no repository/provider dependency inside a strategy, so they unit-test with plain inputs.
- **Dependency direction preserved**: Domain ← Application ← Infrastructure/Api.
- **No hardcoded AI model**: `AiVisualDesigner` continues to resolve the org's configured route via `IWorkflowConfigurationResolver` (unchanged); only the prompt/DTO payload grows.
- **Architecture change** (`require_adr_for_architecture_changes: true`) → ADR authored at apply time (new port family + the `BrandManualSnapshot` contract change).
- **Docs-sync**: `openspec/specs/*` updated for the new/modified capabilities in the spec phase.
- Run `pwsh -File scripts/gates/Run-Gates.ps1` before completion.

---

## 12. Risks and mitigations (design-level)

| Risk | Severity | Mitigation |
|------|----------|------------|
| Carousel coherence regression from the pre-loop plan (two call sites) | **High** | Single `CarouselBackgroundPlan` computed once; both processor and composer read it. Test-first sequence C1-C6 BEFORE the refactor (Strict TDD); C3 pins `LegacyTierStrategy` parity with today's per-slide selection. |
| `BrandManualSnapshot.ColorPalette` breaking contract change | **High** | Treated as ONE coordinated unit (§6 items 1-9); the compiler blocks partial application; every consumer (guardrail, template markup, designer, prompt context, loader, empty-brand fallback, fixtures) changed together. |
| jsonb backfill mis-maps legacy free-text roles | **Medium** | Case-insensitive map; unknown → `null` (value dropped, logged once); `Restore` tolerates missing keys so an un-backfilled row still reads. Backfill is idempotent. |
| `ResolveEnforcedBackground` first-entry-wins breaks CASE A rotation | **Medium** | Strategy emits a single-entry `References([rotated])` per slide → first-entry-wins enforces the correct rotated template per slide; C6 pins it. Multi-entry-per-slide would need plan-index consultation — flagged, test in place. |
| Prompt-version bump token spike | **Medium** | Bump only touched formats (carousel.v3, story-single.v2, feed-single.v2); advisory-only concision avoids re-reject loops; accept one-time first-run cost. |
| CASE C Apify image-URL dependency unconfirmed | **Medium** | Text-descriptor MVP only; `IClientAestheticDescriptorReader` is the stable seam; image-URL port is an additive follow-up gated on confirming actor capability (explicit open dependency). |
| Policy default for existing clients | **Medium** | `NULL`/`Unset` → `LegacyTierStrategy` (today's behavior); no backfill needed; rollback = drop column restores current selection. |
| Strategy DI drift (CASE D extensibility unverified) | **Low** | Resolver is a keyed map over registered `IBackgroundSourcingStrategy`; an OCP test injects a fake CASE-D strategy and asserts A/B/C code is untouched. |

## 13. Rollback
- Policy column is additive with a `NULL` legacy default — dropping it restores current selection; disabling strategy DI falls back to `LegacyTierStrategy` everywhere.
- The structured-role change is the one non-trivial contract change: reverting requires reverting `BrandManualSnapshot.ColorPalette` and ALL §6 consumers together (a coordinated rollback unit) plus a jsonb down-migration that drops `role`/`rank`. Called out explicitly so the revert is not attempted piecemeal.
- Concision is prompt-only: reverting the version bumps restores prior copy behavior with no schema impact.

## 14. Spec impact noticed (for `sdd-spec` / verify)
- **background-sourcing-policy (new capability):** policy on `Client` + ABM surface; `IBackgroundSourcingStrategy`/resolver with A/B/C + the legacy adapter; CASE A deterministic set rotation; CASE C text-descriptor seam; CASE D open/closed requirement.
- **content-generation (modified):** carousel coherence via one shared plan over per-slide selection; reconcile both call sites; structured `ColorEntry.Role` scale propagated through `BrandManualSnapshot` (`WeightedColor`) into the AI designer as prevalence/weighting; advisory concision + prompt-version bumps (carousel.v3, story-single.v2, feed-single.v2).
- **profile-analysis (modified, read-only):** read seam exposing `ProfileAiAnalysis.DetectedBackgroundImages` as CASE C aesthetic guidance — no analysis behavior change.

## 15. Next step
`sdd-tasks` — slice these components into work units in seam order: (1) color-role coordinated contract change (§6 items 1-9, one unit) → (2) policy on `Client` + migration → (3) strategy port + A/B/C + resolver + legacy adapter → (4) processor/composer coherence refactor (C1-C6) → (5) CASE C descriptor reader + designer guidance → (6) advisory concision + prompt bumps → (7) API + web ABM. Tasks owns the checklist; this design owns the HOW.
