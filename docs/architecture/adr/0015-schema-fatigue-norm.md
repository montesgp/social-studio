# ADR 0015 - Schema-Fatigue Norm for AestheticExtraction and ClientAestheticProfile

## Status
Accepted

## Context

`AestheticExtraction` and `ClientAestheticProfile` have accumulated twelve additive
trailing-nullable field slots across roughly six changes in one quarter:
`content-aesthetic-generation`, `client-aesthetic-conceptual-detection`,
`aesthetic-freshness-versioning`, `faithful-flat-slide-rendering`,
`client-aesthetic-fidelity-fixes`, and `client-aesthetic-generation-hardening`. Each addition was
individually justified and additive-nullable, so no single change looked risky in isolation. But
every field added to the extraction contract is another field in the *same single vision-model
JSON response* the extractor must fill correctly in one pass, and per-field extraction accuracy
degrades as that response grows — a cost paid by *existing, already-shipped* fields, silently,
with no change ever having to account for it.

`client-aesthetic-visual-sophistication` (settled decision 7) proposed making this cost explicit
rather than assumed away, at the point `DecorationMotifs` would become the **thirteenth** field
slot on both records.

## Decision

**No further additive field may be added directly to `AestheticExtraction` or
`ClientAestheticProfile` without either:**

1. **A consolidation pass** — grouping two or more already-accumulated signals into one nested
   container record, reducing the net top-level field count the change would otherwise increase,
   or
2. **An ADR recorded before the field is written** — documenting why the new field cannot be
   nested inside an existing container and accepting the extraction-accuracy cost explicitly,
   rather than the cost being paid silently by every other field in the same response.

A change proposing a bare new top-level field on either record, citing neither, is **non-compliant**
and must be sent back at review.

`DecorationMotifs` itself is the norm's first compliant example: it is one field slot (not two),
because `DecorativeOrnamentDetection` — a third, related signal (a recurring decorative ornament's
source path and bounding box) — is nested *inside* `DecorationMotifs` rather than added as a
fourteenth top-level field. `AccentShape`, an initially-proposed fourth member, was cut outright
rather than absorbed, on the reasoning that a weak signal should not spend a field-count budget
that a real one might need later.

## Consequences

- Every future change touching either record's shape must state, in its proposal or design
  document, which of the two compliance paths it takes. This is now a documentation-review
  checkpoint, not just an architecture-gate check (the gate enforces layering, not field count).
- The norm does not cap the *total* number of signals the pipeline can eventually carry — it caps
  how they arrive at the top level, pushing related signals toward nested containers instead of a
  flat, ever-growing field list.
- This norm is recorded here (the ADR route) rather than consolidating an existing field today,
  because at the time of adoption no existing top-level field pairing was ripe for consolidation;
  the norm binds *future* additions.
- The `profile-analysis` spec states this norm normatively (`CAVS-PA-05`), so the constraint is
  visible both at the architecture-decision layer (here) and at the requirement layer a reviewer
  checks a new change against.
- `.claude/CLAUDE.md` is deliberately **not** used to carry this norm — it is not read by the
  `scripts/gates/Run-Gates.ps1` gate suite and would duplicate this ADR without being enforceable.

## Related

- Proposal / Spec / Design: `openspec/changes/client-aesthetic-visual-sophistication/`
- `openspec/specs/profile-analysis/spec.md` — `CAVS-PA-05` states the norm normatively.
- Prior field-growth history: `content-aesthetic-generation`, `client-aesthetic-conceptual-detection`,
  `aesthetic-freshness-versioning`, `faithful-flat-slide-rendering`, `client-aesthetic-fidelity-fixes`,
  `client-aesthetic-generation-hardening` — the six changes whose additive fields motivated this norm.
