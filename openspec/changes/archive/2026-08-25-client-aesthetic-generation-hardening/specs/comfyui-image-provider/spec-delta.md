# Spec Delta: ComfyUI Image Provider — BackgroundOnly Parity, Anatomy Negative-Prompt Terms

<!-- Change: client-aesthetic-generation-hardening -->
<!-- Applies to: openspec/specs/comfyui-image-provider/spec.md (additive delta) -->

## Purpose

Close two quality gaps in `ComfyUiImageModelClient`: `request.BackgroundOnly` is set on every
creative-slide request but silently dropped by the adapter (the OpenAI adapter honours it), and the
default negative prompt has no anatomy-defect suppression terms, contributing to deformed/duplicated
faces and hands.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAGH-CUI-01 | Scope item 4 — `BackgroundOnly` parity with the OpenAI adapter |
| CAGH-CUI-02 | Scope item 5 — anatomy-defect terms in the default negative prompt |
| CAGH-CUI-03 | Approach — shared directive text, not duplicated per adapter |

---

## ADDED Requirements

### Requirement: CAGH-CUI-01 — Adapter Honours request.BackgroundOnly, Injecting the Directive Into the Submitted Prompt

`ComfyUiImageModelClient` MUST read `ImageGenerationRequest.BackgroundOnly`. When `true`, the
adapter MUST inject the background-only directive text into the prompt it submits to the ComfyUI
server, matching `OpenAiImageModelClient`'s directive semantics. When `false` or unset, the adapter's
submitted prompt MUST be unaffected by this requirement (no directive text injected, no other prompt
content changed as a result of this flag).

#### Scenario: BackgroundOnly=true injects the directive into the submitted prompt (CAGH-CUI-01-a)

- GIVEN an `ImageGenerationRequest` with `BackgroundOnly = true`
- WHEN `GenerateAsync` submits the workflow to the ComfyUI server
- THEN the submitted prompt text contains the background-only directive text

#### Scenario: BackgroundOnly=false does not change adapter prompt behaviour (CAGH-CUI-01-b, regression)

- GIVEN an `ImageGenerationRequest` with `BackgroundOnly = false` (or unset)
- WHEN `GenerateAsync` submits the workflow
- THEN the submitted prompt does NOT contain the background-only directive text
- AND the submitted prompt is otherwise identical to pre-change behaviour for the same request

#### Scenario: Directive text is defined once, shared with the OpenAI adapter (CAGH-CUI-01-c)

- GIVEN the completed implementation
- WHEN the background-only directive text is inspected in both adapters
- THEN both adapters reference the same single defined directive text (no duplicated/drifted string)

---

### Requirement: CAGH-CUI-02 — Default Negative Prompt Includes Anatomy-Defect Terms Alongside Existing Text-Suppression Terms

`ComfyUiImageModelClient.DefaultNegativePrompt` MUST include anatomy-defect suppression terms
(at minimum: extra limbs, extra fingers, bad hands, malformed hands, duplicate face, cloned face,
mutated) in addition to every text-suppression term already present. No pre-existing
text-suppression term MAY be removed. `DefaultNegativePrompt` is the sole negative-prompt source
used by every ComfyUI generation request today (no per-org override path exists in the codebase);
this requirement covers only that single constant.

#### Scenario: Anatomy terms present in the default negative prompt (CAGH-CUI-02-a)

- GIVEN the default negative prompt used for a ComfyUI generation request
- WHEN its content is inspected
- THEN it contains anatomy-defect terms (extra limbs, extra fingers, bad/malformed hands, duplicate
  or cloned face, mutated)

#### Scenario: Existing text-suppression terms are retained (CAGH-CUI-02-b, regression)

- GIVEN the default negative prompt before this change contained a set of text-suppression terms
- WHEN the post-change default negative prompt is inspected
- THEN every pre-change text-suppression term is still present

---

## MODIFIED Requirements

None. `CAGH-CUI-01` and `CAGH-CUI-02` are additive: they close gaps in existing adapter behaviour
(`BackgroundOnly` was never read; the negative prompt already existed and gains terms) without
changing any previously specified `comfyui-image-provider` requirement (`CUI-01`..`CUI-06`).

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- `CAGH-CUI-01`'s directive text is extracted from `OpenAiImageModelClient.BackgroundOnlyDirective`
  into one shared location referenced by both adapters (Infrastructure layer only).
- `CAGH-CUI-02` is a `DefaultNegativePrompt` constant edit in `ComfyUiImageModelClient.cs`.
- Both requirements are testable via the existing fake-HTTP-handler pattern (`CUI-06`); no live
  ComfyUI server is required to assert prompt content.
