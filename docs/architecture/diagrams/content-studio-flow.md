# Content Studio — Flow & Responsibility Diagram

> Change: `content-studio` (Phase 1). ADR: [`docs/architecture/adr/0004-content-studio-review-model.md`](../adr/0004-content-studio-review-model.md).
> Companion design: `openspec/changes/content-studio/design.md`.

Content Studio is the **post-generation editorial surface**: it derives an editable, reviewable working copy from **immutable** generation output, runs an approval lifecycle, and hands approved content off to publishing. It is **source-agnostic** (any generator), **isolated** (no `SingleAnalyzer` types, no `ContentGeneration` internals), and it **never publishes**.

## 1. End-to-end flow

```mermaid
flowchart LR
  subgraph Producers["Generators (producers)"]
    SA["Single Analyzer<br/>GeneratorKind = SingleAnalyzer"]
    MG["Magic Generator<br/>(future) GeneratorKind = MagicGenerator"]
  end

  subgraph Gen["content-generation — IMMUTABLE (generate-once)"]
    GP["GeneratedPackage<br/>+ durable per-format results<br/>(flyer/carousel PPTX, reel-script MD, caption, hook...)"]
  end

  SA --> GP
  MG -. future .-> GP

  subgraph Studio["content-studio — editorial working surface (this change)"]
    MAT["ContentItemMaterializer<br/>read-only · idempotent · lazy on open"]
    CS["ContentSet (GenerationRun)<br/>ReviewSummary roll-up"]
    CI["ContentItem*<br/>ContentDraft (editable)<br/>ReviewState machine"]
  end

  GP == "read contract (CG-READ-01)<br/>soft ContentOrigin (Guids only)" ==> MAT
  MAT --> CS
  CS -- "soft member refs" --> CI

  subgraph Pub["publishing — (future, stub today)"]
    PL["Publication pipeline<br/>draft → provider → status"]
  end

  CI == "Approved → ReadyForPublication<br/>event ContentApprovedForPublication<br/>port IPublicationHandoff<br/>PublicationHandoffPayload (SchemaVersion=1)" ==> PL

  CI -. "PlanContext — Phase 4 (soft)" .-> CP["ContentPlan.CalendarSlot"]

  classDef immutable fill:#f4f4f5,stroke:#a1a1aa,color:#27272a;
  classDef studio fill:#eff6ff,stroke:#3b82f6,color:#1e3a8a;
  classDef future fill:#fff7ed,stroke:#f59e0b,color:#7c2d12;
  class GP immutable;
  class MAT,CS,CI studio;
  class MG,PL,CP future;
```

**Boundaries enforced**
- **Immutability**: the materializer only *reads* `GeneratedPackage`/format results; Content Studio never writes them (CS-10, CG-READ-02).
- **Context isolation**: `Domain/Social/ContentStudio/` references *no* `SingleAnalyzer` type and *no* `ContentGeneration` internal type — only soft `Guid`s via `ContentOrigin` (mirrors ProfileAnalysis PA-REG-01).
- **Responsibility ends at handoff**: Content Studio emits the payload and marks `HandedOff`; it performs no scheduling/publishing (PUB-HANDOFF-02).

## 2. ReviewState lifecycle

```mermaid
stateDiagram-v2
  [*] --> Draft: Materialize
  Draft --> InReview
  Draft --> Edited: edit text/visual
  Draft --> Discarded
  InReview --> Edited
  InReview --> Approved
  InReview --> Rejected: reason required
  InReview --> Discarded
  Edited --> InReview
  Edited --> Approved
  Edited --> Rejected
  Edited --> Discarded
  Approved --> ReadyForPublication: stage
  Approved --> Edited: re-open
  Approved --> Rejected
  Rejected --> InReview
  Rejected --> Edited
  Rejected --> Discarded
  Discarded --> InReview: recover
  ReadyForPublication --> HandedOff: handoff emitted
  ReadyForPublication --> Approved: un-stage
  HandedOff --> [*]
```

Transitions are enforced by a static `ReviewStateTransitions` table (same pattern as `ProfileAnalysisStatusTransitions`). Any move not in the table returns a structured `ContentStudioErrors.IllegalReviewTransition` (→ HTTP 409 `content-studio.illegal_transition`), never a silent no-op. `HandedOff` is terminal.

## 3. Individual ↔ bulk is cardinality, not a different model

```mermaid
flowchart TB
  R1["Single Analyzer run"] --> S1["ContentSet (small)<br/>~1 item per format"]
  R2["Magic Generator run (future)"] --> S2["ContentSet (large)<br/>or Campaign of sets"]
  S1 --> OPS["Per-item commands<br/>edit · approve · reject · discard · duplicate · stage"]
  S2 --> BULK["Bulk = application fan-out<br/>over a resolved selection<br/>(partial-success, async at large N)"]
  BULK --> OPS
  classDef studio fill:#eff6ff,stroke:#3b82f6,color:#1e3a8a;
  class S1,S2,OPS,BULK studio;
```

A Single Analyzer run is a small `ContentSet`; Magic Generator is a large set (or a `Campaign`). Bulk operations are an application-layer fan-out over the **same** `ContentItem` aggregate and the **same** per-item commands — there is no separate "bulk" model, which is what lets generation scale from one piece to many without a redesign.
