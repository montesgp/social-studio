# Archive Report: content-image-generation

**Change ID**: `content-image-generation`
**Archived**: 2026-08-25
**Status**: COMPLETE

---

## Executive Summary

AI image generation shipped complete: provider-agnostic, workflow-configured image gateway and model client (bytes-only), two-step flow (prompt-then-render), CSS-designer fallback, Supabase-backed image storage. ClientAesthetic carousel creative slides now composite real generated backgrounds instead of flat CSS. Ten work units implemented, tested, and verified. Specs merged; change folder archived.

---

## Final State Authority Hierarchy

This archive report reflects final state (per launch prompt):

1. **Apply-progress explicit facts**:
   - All 10 work units verified: IImageGenerationGateway, IImagePromptWriter, IClientAestheticImageBackgroundService, CreativeSlideIndexResolver, ImagePromptWriter, ClientAestheticImageBackgroundService called from ContentGenerationJobProcessor.cs (§660-730) with CSS-degrade fallback confirmed live
   - OpenAiImageModelClient confirmed; IImageGenerationClient/NullImageGenerationClient retired/removed
   - state.yaml shows apply-complete; gates PASS

2. **Persisted tasks** (`tasks.md`):
   - All 10 work units marked complete

---

## What Shipped

### Domain Layer (Value Objects)
- **CreativeSlideIndexResolver**: logic for selecting which carousel slides are creative (default: first+last; nullable, extractor-ready)
- **ImageGenerationRequest record**: encapsulates (prompt, dimensions, org-scoped provider routing)
- **CreativeSlideIndices**: nullable IReadOnlyList<int> on ClientAestheticProfile (read-side projection, no EF column for v1)

### Application Layer (Ports)
- **IImageGenerationGateway**: org-routed, usage-ledger instrumented, mirrors AiCompletionGateway
- **IImagePromptWriter**: TEXT route on IAiCompletionGateway (namespaced image-prompt/v1)
- **IImageModelClient port**: bytes-only (no JSON return); contract: BinaryContent(bytes, mediaType)
- **IClientAestheticImageBackgroundService**: per-client creative-slide generation (cap=2, Task.WhenAll, reuse by prompt-hash, per-slide fallback)

### Infrastructure Layer (Persistence & Adapters)
- **OpenAiImageModelClient**: Infrastructure adapter; dall-e-3 (gpt-image-1) seeded as FIRST MediaGeneration provider; any provider allowed
- **ImageGenerationGateway**: IWorkflowConfigurationResolver + IIntegrationResolver + IExternalProviderClientFactory dispatch
- **ImageGenerationGatewayInstrumentationDecorator**: usage-ledger writes ProviderCallContext (mirrors text-AI)
- **Manual DI decoration** (no Scrutor): DependencyInjection.cs style registration
- **ClientAestheticImageBackgroundService**: 
  - Inline sync-parallel (Task.WhenAll for ≤2 creative slides)
  - Content-address by (org, client, formatResultId, slideIndex, promptHash)
  - Reuse stored background by default; regenerate only on prompt-hash change or force-flag
- **WorkflowConfigurationCatalog**: new generate-image step (MediaGeneration category, code-only static list, no migration)
- **No EF changes for v1**: CreativeSlideIndices defaults first+last (no column needed yet)
- **No new migration**: catalog is code-only; storage reuses generated-asset pattern

### Compositing Layer
- **BackgroundSelection.ForcedDefault(url, "ai-generated-background")**: injected per-slide; reuses existing compositing (BuildTemplateBackedMarkup, CompositionSpecEnvelope)
- **Text remains editable**: studio rebuilds text overlay on raster
- **Flat slides unchanged**: CSS-designer path for non-creative slides

### Resilience & Fallback
- **Image-gen never on critical path**: prompt-writer failure or image-gen failure logs + falls back to CSS background
- **Per-slide resilience**: one slide failure does NOT abort carousel; failed slide uses CSS fallback
- **Silent degradation**: no error visible to operator; job completes

### Retirement
- **IImageGenerationClient + NullImageGenerationClient**: removed (legacy interface lacking OrganizationId; superseded by IImageGenerationGateway)

---

## Spec Merges

### Specs Created

**`openspec/specs/image-generation/spec.md`** (NEW)
- Full image-generation capability spec (4 spec files in delta)
- Requirements: IMG-01 through IMG-08 (gateway, ledger, two-step, resilience, provider-agnostic, architecture gate, retirement)

### Specs Extended

**`openspec/specs/content-generation/spec.md`**
- Appended delta: AI image backgrounds for creative slides, CSS fallback, compositing reuse
- Added 4 requirements (creative-slide sourcing, per-slide resilience, storage reuse, text editability)

**`openspec/specs/background-sourcing-policy/spec.md`**
- Appended delta: Image-gen interaction with CASE B/C sourcing policies (independent channel)
- Added 3 requirements (image-gen as separate channel, CASE B/C palette weighting orthogonal, resilience interaction)

**`openspec/specs/integrations/spec.md`**
- Appended delta: MediaGeneration workflow step, provider routing, catalog config
- Added 4 requirements (step discovery, org-level configuration, provider selection, model routing)

---

## Verification Summary

### Implementation Facts

| Work Unit | Deliverable | Status | Notes |
|-----------|-------------|--------|-------|
| WU-1 | IImageGenerationGateway + IImageModelClient ports | COMPLETE | Bytes-only, org-routed, ledger-instrumented |
| WU-2 | OpenAiImageModelClient (dall-e-3) adapter | COMPLETE | First provider seeded; any provider allowed |
| WU-3 | IImagePromptWriter via TEXT route | COMPLETE | Namespaced image-prompt/v1; reuses IAiCompletionGateway |
| WU-4 | ClientAestheticImageBackgroundService | COMPLETE | Cap=2; Task.WhenAll; reuse-by-prompt-hash; per-slide fallback |
| WU-5 | ImageGenerationGateway + InstrumentationDecorator + manual DI | COMPLETE | Mirrors text-AI pattern; no new transports |
| WU-6 | OpenAiImageModelClient (kept, not duplicate) | COMPLETE | Single implementation per OQ-5 decision |
| WU-7 | WorkflowConfigurationCatalog generate-image step | COMPLETE | MediaGeneration category; code-only; integrations UI auto-renders |
| WU-8 | ContentGenerationJobProcessor wiring | COMPLETE | ForcedDefault overlay pre-render loop; CSS fallback; parity + resilience tests |
| WU-9 | Retire IImageGenerationClient + NullImageGenerationClient | COMPLETE | Removed (legacy, no org scope) |
| WU-10 | ADR + spec sync + gates | COMPLETE | ADR 0007; specs merged |

### Test Results & Verification

| Criterion | Status | Notes |
|-----------|--------|-------|
| Image-gen never on critical path (resilience non-negotiable) | PASS | Failure → CSS fallback; job completes |
| Per-slide resilience (one fail ≠ carousel fail) | PASS | Fallback per slide; skipped from image array |
| Provider routing via IWorkflowConfigurationResolver | PASS | No hardcoded provider; catalog-driven |
| No IMAGE route config = CSS-only (silent degrade) | PASS | Tested; no error |
| Creative-slide set nullable + extractor-ready | PASS | Default first+last; no EF column for v1 |
| Provider types NOT in Domain/Application | PASS | OpenXML, Playwright only in Infrastructure; architecture gate PASS |
| IImageGenerationClient + NullImageGenerationClient gone | PASS | Removed; no dangling references |
| Prompt-writer + image-gen instrumented | PASS | ProviderCallContext decorator written; usage ledger entries recorded |
| Retry on force-regenerate only on prompt-hash change | PASS | Content-addressed storage tested |
| Text remains editable in studio (ForcedDefault not locked) | PASS | Existing BuildTemplateBackedMarkup unchanged; overlay compositing reused |
| MUST gates pass | PASS | Architecture gate; no provider leak |

---

## Design Decisions (OQ Resolutions)

| OQ | Decision | Notes |
|----|----------|-------|
| OQ-1 | Creative-slide default: first+last (nullable) | Extractor-ready; ConfigurableProfile future |
| OQ-2 | Max=2 slides; 1024x1024 PNG (DECISION-NEEDED from owner) | Constant in CreativeSlideIndexResolver; owner-tunable before GA |
| OQ-3 | Reuse by prompt-hash (content-addressed); regenerate on prompt change or force | Silent reuse; regenerate only on semantic change |
| OQ-4 | Inline sync-parallel (Task.WhenAll ≤2 slides) | No new job/worker state; within render-loop context |
| OQ-5 | IImageGenerationGateway + IImageModelClient; retire IImageGenerationClient | Both new ports; legacy removed |
| OQ-6 | TEXT route (generate-content/ai-generation) for image-prompt (no sub-step in v1) | Reuses existing route; namespaced handler |
| OQ-7 | ImageInitReference nullable (Init=null, reserved for image-to-image v2) | Deferred seam |

---

## Archive Contents

- `proposal.md` — provider-agnostic gateway, resilience non-negotiable, Creative-vs-flat routing, open design questions
- `design.md` — OQ resolutions D1-D7; architecture & ports; PPTX mapping (not applicable to this change); pixel-persistence offline; TDD plan
- `tasks.md` — 10 work units, all complete
- `specs/image-generation/spec.md` — full capability spec (8 requirements, 16 scenarios)
- `specs/content-generation/spec-delta.md` — creative-slide sourcing, resilience, CSS fallback
- `specs/background-sourcing-policy/spec-delta.md` — image-gen as independent channel
- `specs/integrations/spec-delta.md` — workflow routing, provider selection
- `archive-report.md` — this file

**Moved from**: `openspec/changes/content-image-generation/`
**Moved to**: `openspec/changes/archive/2026-08-25-content-image-generation/`

---

## Observation IDs (Engram Traceability)

| Artifact | Observation ID |
|----------|---|
| proposal | 778 |
| spec | 779 |
| design | 780 |
| tasks | 781 |

---

## Key Learnings

1. Image generation as a provider-agnostic port (IImageGenerationGateway + IImageModelClient) with org-level workflow routing allows first-provider seeding (OpenAI) while decoupling implementation from abstract contract; any provider follows the same pattern without codebase changes to core logic.

2. Resilience as a non-negotiable invariant (failure → CSS fallback, never fails the job) requires per-slide error handling, not carousel-level rollback; inline sync-parallel Task.WhenAll is cheaper than async-background for ≤2 creative slides and fits the render-loop context.

3. Content-addressing by (org, client, formatResultId, slideIndex, promptHash) enables silent reuse without re-calling the model; regeneration only on semantic prompt change (hash mismatch) or explicit force reduces redundant API calls and costs.

4. CreativeSlideIndices as a nullable, defaulted, extractor-ready field on read-side projection (ClientAestheticProfile) avoids EF column overhead in v1 while remaining forward-compatible for user-configurable rules in later phases.

5. Legacy port retirement (IImageGenerationClient + NullImageGenerationClient) must be coordinated across all call sites; a grep + compile-time breakage check prevents silent dangling references.

---

*Archive report generated 2026-08-25.*
*SDD cycle complete. Change is closed.*
