# ADR 0008 - Image Conditioning Capability, DescribeAsync, and Telemetry Correlation

## Status
Accepted

## Context

ADR 0007 (Image Generation Gateway Port) established `IImageGenerationGateway` as the
Application-layer port for AI image generation and `IImageModelClient` as the adapter contract.
By the time the `client-aligned-carousel-generation` change began, three gaps remained:

1. **No capability-detection mechanism.** The gateway had no way to ask a resolved adapter
   whether it supports image-conditioned generation (passing reference images alongside a
   prompt). The absence was a blocking gap for OQ-4: the background service had no signal
   to decide between conditioning and descriptor-prompt fallback, so it silently skipped
   conditioning for all providers.

2. **Byte-hauling in the Application layer.** The reserved `ImageGenerationRequest.Init`
   slot carried `byte[]?` — the only way to pass a reference image. Resolving bytes from
   storage requires `IGeneratedAssetStorage`, an Infrastructure concern. If the Application
   layer built `Init` with live bytes, Infrastructure types would leak into the call site
   (OQ-4 / architecture-gate violation).

3. **Silent image-gen degradation.** `ImageGenerationGateway` always passed `AnalysisId:
   null` to `IntegrationExecutionPolicy`, which had a guard that skipped persistence when
   `AnalysisId` was null. Every per-slide image-gen attempt — success or failure — produced
   no row in `social.integration_execution_attempts`. Silent fallbacks to the CSS designer
   background were invisible to operators (OQ-6).

These three gaps are addressed together because they share a single design principle: the
Application layer should declare *what* it wants (capability flag, Url handle, correlation
token) and let Infrastructure resolve the *how* (adapter bytes, byte-hauling, attempt rows).

## Decision

### 1. `IImageGenerationGateway.DescribeAsync` and `ImageModelCapabilities`

A new method is added to the existing `IImageGenerationGateway` Application port:

```csharp
Task<Result<ImageModelCapabilities>> DescribeAsync(Guid organizationId, CancellationToken ct = default);
```

`DescribeAsync` resolves the IMAGE route and credential for the org, instantiates the
corresponding `IImageModelClient`, and returns its `Capabilities` property WITHOUT making
a provider network call. The capability record is:

```csharp
public sealed record ImageModelCapabilities(bool SupportsImageConditioning, int MaxConditioningImages);
```

`SupportsImageConditioning` and `MaxConditioningImages` are **advertised declaratively on
the adapter** (a read-only property `IImageModelClient.Capabilities`). No database flag,
no workflow-catalog column, and no per-provider if/else in Application code. Adding a new
provider that supports conditioning requires only implementing `IImageModelClient` with the
correct `Capabilities` value; the routing layer and the background service are unchanged.

The background service (`ClientAestheticImageBackgroundService`) calls `DescribeAsync` once
per job — not once per slide — to avoid per-slide gateway round-trips.

**Rationale:** Advertising capability on the adapter keeps the decision logic in
Infrastructure (where provider knowledge belongs) and the consumption logic in Application
(where it can stay provider-agnostic). A DB flag would require migration on every new
provider; a catalog column would couple the workflow catalog schema to provider SDK
capabilities.

### 2. `ImageGenerationRequest.ReferenceImages` — Url handles, not bytes

A new additive trailing field is added to `ImageGenerationRequest`:

```csharp
IReadOnlyList<ImageInitReference>? ReferenceImages = null
```

`ImageInitReference` carries `byte[]? Bytes`, `string? Url`, and `string? Mime`. The
Application layer populates only `Url` (a storage path from `ClientAestheticProfile.ReferenceImages`)
and leaves `Bytes = null`. The supporting `IImageModelClient` adapter resolves bytes from
`IGeneratedAssetStorage` before forwarding them to the provider. For the OpenAI adapter,
resolved bytes are forwarded to the `/v1/images/edits` multipart endpoint.

The original `ImageInitReference? Init` field (ADR 0007 §3) is retained for back-compat
with any caller still setting it. `ReferenceImages` is the preferred slot going forward
because it supports multiple reference images.

**Rationale:** The Application layer should be free of byte-hauling. Passing Url handles
means the call site in `ClientAestheticImageBackgroundService` never touches `IGeneratedAssetStorage`
— it holds only a storage path string. Bytes are resolved once, inside the adapter, where
the provider SDK that needs them lives. This preserves the hexagonal boundary: no
`IGeneratedAssetStorage` or provider-SDK types appear in `ImageGenerationRequest` or any
Application call site.

### 3. Cap `MaxConditioningImages = 3`

Each supporting adapter advertises `MaxConditioningImages`. The background service enforces:

```
refs = min(profile.ReferenceImages.Count, capabilities.MaxConditioningImages, 3)
```

The hard cap of 3 is applied on the Application side; the adapter's declared maximum is a
further constraint. The value 3 was chosen to reuse the client's curated reference set
(typically 2–4 archived creatives) without passing the full archive, bounding vision-token
cost per slide.

**Rationale:** Without a hard cap, a client with a large archive could inadvertently send
dozens of images per slide, inflating token usage and latency non-linearly. Three images
provide sufficient conditioning signal for background style transfer while remaining
predictable in cost.

### 4. Telemetry correlation — `CorrelationId` / `CorrelationKind`, nullable `AnalysisId`, additive migration

Four coordinated changes fix the silent-degradation gap:

**4a. Nullable `AnalysisId` on `IntegrationExecutionAttemptRecord`.**
`AnalysisId` is changed from `Guid` (required) to `Guid?` (nullable). The non-null
invariant held only because the analysis-AI path always had an analysis context; the
image-gen path does not.

**4b. Additive correlation fields on ports.**
`IntegrationExecutionAttemptRecord` and `IntegrationExecutionRequest` gain two trailing
optional fields: `Guid? CorrelationId = null, string? CorrelationKind = null`. The
background service supplies `CorrelationId = FormatResultId` and
`CorrelationKind = "content-generation/generate-image"` per slide. `DiagnosticsJson`
carries `{"slideIndex": N, "reason": "..."}` for degraded slides.

**4c. Relaxed persistence guard.**
`IntegrationExecutionPolicy.PersistAttemptAsync` previously skipped persistence when
`AnalysisId is null`. The guard is relaxed to:

```
skip only when AnalysisId is null AND CorrelationId is null
```

This preserves the original analysis-context rows unchanged while enabling image-gen
attempt rows that have no analysis FK.

**4d. Additive, reversible EF Core migration.**
Migration `20260628000000_RelaxAnalysisIdNullableAndAddCorrelationToIntegrationAttempts`
applies four DDL steps:

1. Drop the NOT NULL foreign-key constraint on `analysis_id`.
2. Alter `analysis_id` column to nullable.
3. Add nullable `correlation_id uuid`.
4. Add nullable `correlation_kind varchar(128)`.
5. Re-add the foreign key as `IsRequired(false)` (optional FK).

The `Down()` method reverses all steps. Existing `analysis_id`-scoped rows are unaffected;
their `correlation_id` is null. New image-gen rows carry `correlation_id` and a null
`analysis_id`.

**Rationale:** Decoupling image-gen telemetry from the single-analysis FK surfaces the
currently invisible CSS-fallback degradation without changing job completion semantics.
The job status remains `Completed`; no `error_code` is written. An operator query on
`social.integration_execution_attempts WHERE correlation_kind = 'content-generation/generate-image'
AND status = 'Failed'` shows every degraded slide for any job. The additive-nullable
migration approach was chosen over a separate telemetry table because `integration_execution_attempts`
already models the "attempt of a workflow step" concept; extending it is semantically
consistent and avoids a new table join for cross-path reporting.

### 5. Architecture-gate compliance

All new types added by this decision follow the hexagonal boundary:

| Concern | Layer | Type(s) |
|---------|-------|---------|
| Capability query + result | Application | `IImageGenerationGateway.DescribeAsync`, `ImageModelCapabilities` |
| Reference-image slot | Application | `ImageGenerationRequest.ReferenceImages` (`ImageInitReference` with Url only) |
| Correlation fields | Application | `IntegrationExecutionAttemptRecord`, `IntegrationExecutionRequest` — additive nullable fields |
| Byte resolution, provider API call (`/v1/images/edits`), background-only directive | Infrastructure | `IImageModelClient` adapter ONLY |
| EF entity, migration, repository mapping | Infrastructure | `IntegrationExecutionAttemptData`, migration class, `EfIntegrationExecutionAttemptRepository` |

No provider SDK type, EF Core type, or `IGeneratedAssetStorage` reference appears in any
Application or Domain signature. The constraint is enforced by the existing architecture
gate (`pwsh -File scripts/gates/Run-Gates.ps1`) and pinned by a port contract test
following the `IImageGenerationGatewayContractTests` pattern.

## Consequences

- `ClientAestheticImageBackgroundService` can gate reference-image conditioning on a
  declarative per-adapter property instead of an if/else switch on provider key strings.
  Any future `IImageModelClient` that supports conditioning self-declares it; no Application
  code changes.
- The Application layer call site for image generation remains free of byte-hauling and
  storage SDK imports. Storage resolution is an Infrastructure-only concern.
- Per-slide image-gen degradation is now observable. Operators can distinguish "image-gen
  succeeded" from "CSS fallback used" without reading logs; they query
  `social.integration_execution_attempts` by `correlation_kind`.
- The migration is forward/backward compatible: a rollback removes the correlation columns
  and restores NOT NULL on `analysis_id` while leaving existing analysis-scoped rows intact.
- `DescribeAsync` incurs one gateway route-resolution call per job (not per slide). The
  resolved `ImageModelCapabilities` is cached in the local variable for the job's duration.
- Adding a second supporting adapter (e.g., a Claude or Stability AI adapter that supports
  conditioning) requires only implementing `IImageModelClient.Capabilities` with
  `SupportsImageConditioning = true` and `MaxConditioningImages = N`. No Application,
  migration, or catalog changes are needed.

## Related

- Proposal / Spec / Design: `openspec/changes/client-aligned-carousel-generation/`
- Builds on: ADR 0007 (Image Generation Gateway Port — `IImageGenerationGateway` + `IImageModelClient`)
- Spec requirements realized: CAC-IMG-01 (reference-image conditioning), CAC-INT-01 (per-slide telemetry)
- Architecture gate: `pwsh -File scripts/gates/Run-Gates.ps1`
- Migration: `SocialStudio.Infrastructure/Persistence/Postgres/Migrations/20260628000000_RelaxAnalysisIdNullableAndAddCorrelationToIntegrationAttempts.cs`
