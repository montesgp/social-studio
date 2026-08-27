# OCR / Baked-In-Text Detection — Feasibility Report

Status: **investigation only** — no production code, port, or adapter is introduced by this report or
by WU-7. This document exists to inform a *future*, separately-scoped decision.

## Trigger

During this session's manual verification, a ComfyUI-generated background image shipped with
readable, non-editable text baked into the raster (the model rendered words directly into the pixels,
outside the app's own text-slot overlay). `DegenerateImageGuardrailValidator`
(`Incoders.Hive.Infrastructure/Social/ContentGeneration/DegenerateImageGuardrailValidator.cs`) did not
catch it — by its own doc comment (lines 20-24) it is explicitly scoped to near-black/flat-color
rejection only and "does NOT yet implement the foreground/background-only inspection … (text/logo
detection needs OCR/ML) — that remains a future concern." This report is the requested investigation
into closing that gap.

## Candidate Approaches

### 1. Local OCR (Tesseract / `tesseract.js`)
- Runs in-process or as a local binary; no network call, no per-image cost, no data leaves the host.
- `Tesseract.NET`/`Tesseract` NuGet wraps the native `tesseract` engine (C++); requires shipping/
  installing language traineddata files and a native binary alongside the .NET service — a new
  deployment dependency (container image change, not just a NuGet reference).
- Designed for structured document/scan OCR (high-contrast text on a clean background). Accuracy on
  *stylized, low-contrast text baked into a photographic/artistic background* — the actual failure
  mode here — is materially worse than on scanned documents; this is a known weak spot for Tesseract
  specifically, not just an assumption.
- No cloud dependency, no external credential, no additional egress — fits this codebase's current
  posture (Infrastructure has zero outbound OCR/vision calls today).

### 2. Cloud OCR / vision API (e.g. a hosted OCR endpoint)
- Materially better accuracy on stylized/embedded text than local Tesseract, at the cost of a new
  external dependency: network round-trip, API key/credential to provision and rotate, per-call
  billing, and a new failure mode (the call itself can time out or fail, which then needs its own
  fail-open/fail-closed policy).
- Adds a hard network hop to an already network-bound pipeline (ComfyUI generation is itself a
  long-running external call — see Latency below), compounding the "how many external calls before an
  image ships" surface area.

### 3. Vision-model call (ask the multimodal model directly, e.g. "does this image contain readable
   text? yes/no + bounding regions")
- This codebase already has multimodal AI client abstractions in place
  (`Incoders.Hive.Application/Abstractions/Ai`, `ClaudePromptCliModelClient` in
  `Incoders.Hive.Infrastructure/Ai/Providers`), so from an integration-shape perspective this is the
  closest fit to existing patterns — it would plug into the same kind of client abstraction already
  used for prompt generation, rather than requiring a brand-new SDK/binary/OCR-specific dependency.
- Trades classic OCR's "extract exact text" for a coarser but often more robust judgment ("is there
  legible baked-in text in this frame, yes or no") — arguably a better fit for a guardrail whose job is
  reject/accept, not transcription.
- Still a network round-trip against a provider, with the same latency-stacking and cost-per-call
  concerns as option 2, plus prompt-engineering risk (false positive/negative rate depends on prompt
  quality and is not directly measurable without a labeled dataset — see Accuracy below).

## Existing Dependencies (verified this session)

Searched `Incoders.Hive.Infrastructure.csproj`, `Incoders.Hive.Application.csproj`, and
`Incoders.Hive.ApiService.csproj` for any OCR/text-detection/vision package reference: **none found**.
The only image-processing dependency currently in the tree is `SkiaSharp`, used exclusively for the
degenerate-image (near-black / flat-color) pixel sampling described above — it has no OCR capability.
Zero groundwork exists today; any candidate above is a net-new dependency, not an extension of
something already wired in.

## Measured Accuracy on Real Generated Slides

**Not measured.** This report does not include a true/false positive/negative count over ≥10 real
generated slides, because doing so would require standing up at least a throwaway OCR pass (local or
cloud) against this session's/this project's generated-image corpus and hand-labeling the results —
that is itself scoped implementation/experimentation work, not documentation, and is explicitly out of
scope for WU-7 (D2: no code, no port, no adapter). Recording "not measured, and why" here rather than
fabricating numbers is the honest disposition; a future scoped spike would need to produce this data
before any candidate could be selected with confidence.

## Latency Impact

This session measured **~9m35s** for one successful ComfyUI background-image generation — the slowest
stage in the content-generation pipeline by a wide margin (template render, guardrail checks, and
storage upload are all sub-second by comparison). Any additional per-image OCR/vision pass is pure
addition on top of that number, since it must run *after* the image exists (it inspects generated
pixels, it cannot run earlier):

- Local Tesseract: roughly hundreds of milliseconds to a few seconds per image on typical CPU, depending
  on resolution and preprocessing — small in absolute terms, but not zero, and it runs synchronously in
  the same request path that already waits 9+ minutes on ComfyUI.
- Cloud OCR / vision-model call: adds one more network round-trip (typically low seconds, but subject to
  provider latency variance and possible retries) on top of the 9m35s ComfyUI wait — worse relative
  latency contribution than local OCR, but likely still small compared to the dominant ComfyUI stage.

In relative terms, either option adds a small percentage on top of an already-dominant 9m35s stage. In
absolute terms, this pipeline is already slow enough that a multi-second guardrail is unlikely to be the
user-visible bottleneck — but see the false-positive tradeoff below, which matters more than raw
latency.

## False-Positive / False-Negative Tradeoff

- A **false positive** (guardrail wrongly flags a clean image as containing baked-in text) discards a
  good image and forces a retry of the single most expensive step in the pipeline (~9m35s). At any
  non-trivial false-positive rate, this guardrail could dominate total pipeline cost/latency more than
  the defect it is meant to catch.
- A **false negative** (guardrail misses real baked-in text) ships exactly the defect this investigation
  was triggered by — a broken slide reaching the studio undetected, same as today's status quo minus the
  guardrail's existence.
- Because option 1 (local Tesseract) is known-weaker on stylized/embedded text specifically, and no
  accuracy data exists yet for options 2/3 against this project's actual generated-image distribution,
  neither the false-positive rate nor the false-negative rate can be bounded today. Any guardrail wired
  in without that data risks either silently discarding good ~9m35s generations (false positives) or
  giving false confidence while still shipping the defect (false negatives).

## Behaviour When the Dependency Is Absent

None of the three candidates exists in this codebase today (see Existing Dependencies above), so the
current, actual behaviour is: the pipeline ships whatever `DegenerateImageGuardrailValidator` accepts,
with no text/logo inspection at all — the defect that triggered this investigation reaches the studio
undetected. Any future guardrail built on one of these candidates would need an explicit fail-open or
fail-closed policy for the case where the OCR/vision dependency itself is unavailable at runtime (native
binary missing, traineddata missing, network/provider call failing): following this codebase's existing
precedent in `DegenerateImageGuardrailValidator` (fail-open — "never over-reject… if we cannot
decode/inspect the bytes, accept them", lines 60-67), the consistent choice would be fail-open on
inspection failure, i.e. an inspection failure ships the image rather than blocking the pipeline. That
is a decision for the future scoped implementation, not this report.

## Cost per 1,000 Images

- Local Tesseract: effectively $0 marginal cost (CPU time only), but a fixed one-time cost of adding a
  native binary + traineddata to the deployment image and maintaining that dependency.
- Cloud OCR / vision-model call: **not quotable without picking a specific provider and pricing tier**,
  which this investigation intentionally does not do (D2 scope). Order-of-magnitude for OCR APIs is
  typically low dollars per 1,000 calls; a vision-model call is typically priced per-token/per-image and
  varies more widely by provider and prompt size. Either way this is a new recurring line-item cost that
  does not exist in the pipeline today.

## Recommendation

**Needs further scoping — do not implement in this change.**

Rationale: the trigger defect is real and confirmed, but none of the three candidates has measured
accuracy data against this project's actual generated-image distribution, and the pipeline's dominant
cost (~9m35s per ComfyUI image) makes the false-positive risk of an unproven guardrail more dangerous
than the status quo of an occasional undetected defect. The threshold for revisiting this as a scoped
implementation: a follow-up spike that (a) runs at least one candidate against ≥10 real generated slides
recorded with actual true/false positive/negative counts, and (b) shows a false-positive rate low enough
that the expected retry cost (false-positive rate × ~9m35s) is acceptable relative to the defect's
observed real-world frequency. Until that data exists, no candidate above can be selected with
confidence, and shipping one without it risks trading a rare visible defect for a more frequent, more
expensive silent regeneration cost.

If a future change proceeds, the closest-fit integration shape is option 3 (vision-model call through
the existing `Incoders.Hive.Application/Abstractions/Ai` client abstraction), since it reuses an
established pattern in this codebase rather than introducing a new OCR-specific binary or SDK — but that
preference is provisional and should be revisited once accuracy data exists.
