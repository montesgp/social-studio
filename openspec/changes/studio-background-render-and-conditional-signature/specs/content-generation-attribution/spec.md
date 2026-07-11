# Delta: Conditional @handle Signature Gating

<!-- Change: studio-background-render-and-conditional-signature (Issue #70) -->
<!-- Applies to: openspec/specs/content-generation/spec.md (CAC-CG-03) -->

## Purpose

`CarouselVisualComposer` currently populates the `@handle` slot whenever
`BrandSnapshot.Handle` is non-null. Because a handle is also required for aesthetic
profile analysis, every analyzed client gets signed automatically — including clients
who do not actually sign their real slides. This delta gates the `@handle` slot on an
explicit "client signs" condition, applied consistently in `StoryVisualComposer` and
`CarouselVisualComposer` via `AttributionPolicy`.

## MODIFIED Requirements

### Requirement: CAC-CG-03 — `CarouselVisualComposer` Gains `@handle`/Signature Slot, Gated on "Client Signs"

`CarouselVisualComposer` MUST include a dedicated composition slot for the client's
`@handle` and signature at parity with `StoryVisualComposer` and `FeedPostVisualComposer`.
The slot MUST be populated only when `AttributionPolicy.ResolveSignature` determines the
client signs their slides — EITHER (1) profile analysis detected a "client signs" signal,
OR (2) an explicit client opt-in field/preference is set. A non-null `BrandSnapshot.Handle`
remains NECESSARY (there is nothing to sign with otherwise) but is NO LONGER SUFFICIENT on
its own. When neither signal is present, OR when `Handle` is null/empty, the slot MUST be
empty or omitted with no error. The default is opt-in: absent an explicit opt-in field or a
detected signal, the client is NOT signed, even if a `Handle` is registered.

(Previously: the slot was rendered whenever `BrandSnapshot.Handle` was non-null, with no
"client signs" gate — every client with a registered own-account handle was signed
unconditionally.)

#### Scenario: Handle present but neither signal set — slot omitted (behavior change)

- GIVEN a `ClientAesthetic` carousel job where `BrandSnapshot.Handle = "@clientbrand"`
- AND the client has NOT been detected by profile analysis as signing their slides
- AND the client has NOT explicitly opted in to signing
- WHEN `CarouselVisualComposer` composes a slide
- THEN the `@handle` slot is empty or omitted
- AND no exception is raised and the slide renders normally

#### Scenario: Client explicitly opted in — slot rendered

- GIVEN a `ClientAesthetic` carousel job where `BrandSnapshot.Handle = "@clientbrand"`
- AND the client has an explicit opt-in preference set to sign their slides
- WHEN `CarouselVisualComposer` composes a slide
- THEN the `@handle` slot carries `"@clientbrand"` in the composition envelope

#### Scenario: Profile analysis detected client signs — slot rendered

- GIVEN a `ClientAesthetic` carousel job where `BrandSnapshot.Handle = "@clientbrand"`
- AND profile analysis detected a "client signs their slides" signal for that client
- AND the client has NOT set an explicit opt-in preference
- WHEN `CarouselVisualComposer` composes a slide
- THEN the `@handle` slot carries `"@clientbrand"` in the composition envelope

#### Scenario: Handle absent — slot empty regardless of signals (unchanged)

- GIVEN `BrandSnapshot.Handle = null`
- WHEN `CarouselVisualComposer` composes a slide, even if a "client signs" signal is present
- THEN the `@handle` slot is empty or omitted
- AND no exception is raised and the slide renders normally

#### Scenario: StoryVisualComposer applies the same gate as CarouselVisualComposer

- GIVEN a `ClientAesthetic` story job and a carousel job for the same client, both with
  `BrandSnapshot.Handle` set and the same "client signs" signal state
- WHEN `StoryVisualComposer` and `CarouselVisualComposer` each compose their slides
- THEN both composers reach the same signing decision (both sign or both omit)
- AND both resolve the signature text via `AttributionPolicy.ResolveSignature`

## ADDED Requirements

### Requirement: CAC-CG-05 — Existing Handled Clients Default to Opted-Out (Migration)

For clients that were previously signed solely because `BrandSnapshot.Handle` was
non-null, and that have neither a detected "client signs" signal nor an explicit
opt-in preference recorded at the time this gate activates, the system MUST default
them to NOT signed (opt-out). This is a deliberate, documented behavior change, not a
silent flip: clients that actually sign their real slides MUST be identifiable via
profile analysis and/or opted in explicitly to resume signing.

#### Scenario: Pre-existing handled client with no recorded signal stops being signed

- GIVEN a client that has `BrandSnapshot.Handle` set and was signed under the prior
  unconditional rule
- AND no "client signs" signal has been detected and no opt-in preference is recorded
- WHEN a generation job composes a slide for that client after this change activates
- THEN the `@handle` slot is empty or omitted
- AND the client can resume being signed once profile analysis detects the signal or an
  explicit opt-in is recorded
