# Spec Delta: Profile Analysis — Three-Role Typography Extraction, Per-Slide Photo-Transparency Signal

<!-- Change: client-aesthetic-fidelity-fixes -->
<!-- Applies to: openspec/specs/profile-analysis/spec.md (additive delta) -->

## Purpose

Specify the **additive** extraction/persistence changes this change introduces:

1. **Three-role typography extraction** — the multimodal aesthetic extractor gains heading,
   body, AND caption/overline typography extraction (family class, weight, italic, size
   ratio). Previously the extractor emitted no typography at all; `AestheticExtraction` /
   `AestheticProfilePersistence` / `ClientAestheticProfile.Typography` already carry a
   role-keyed contract, so this change extends the extractor's output, not the persisted
   shape.
2. **Per-slide photo-transparency signal** — a new additive field parallel and index-aligned
   to `BackgroundPattern`: boolean + qualitative descriptor per slide (never a numeric
   alpha, per settled decision 2), threaded through the extractor prompt, DTO, `Map()`,
   `AestheticExtraction`, `AestheticProfilePersistence`, and `ClientAestheticProfile`.

All new fields are additive-nullable. A client with no signal (typography or transparency)
produces extraction/persistence output that is unaffected downstream (see `content-generation`
delta for the cold-start render-time guarantee).

---

## PRD Traceability

| Requirement ID | Proposal Anchor |
|---|---|
| CAFF-PA-01 | Settled decision 5 — three typography roles; Scope item 4 |
| CAFF-PA-02 | Scope item 4 — persistence round-trip, no DB migration |
| CAFF-PA-03 | Settled decision 2 & 4 — boolean + descriptor, per-slide; Scope item 5 |
| CAFF-PA-04 | Risks — transparency array length reconciliation vs. `BackgroundPattern` |

---

## ADDED Requirements

### Requirement: CAFF-PA-01 — Extractor Extracts Three Typography Roles

`MultimodalAestheticStyleExtractor` MUST extract typography for the heading, body, AND
caption/overline roles (family class, weight, italic, size ratio) whenever distinguishable
in the client's analyzed feed. Each role is independently additive-nullable: detecting
caption/overline MUST NOT require heading/body to also be detected, and vice versa. When a
role is not determinable, its value MUST be null and extraction MUST still succeed (no error).

#### Scenario: All three roles detected

- GIVEN a client feed with visually distinguishable heading, body, and caption/overline type
- WHEN extraction runs
- THEN the extractor DTO carries non-null family/weight/italic/size-ratio for all three roles

#### Scenario: Only heading and body detected — caption/overline absent (regression: two-role clients unaffected)

- GIVEN a client feed where caption/overline type is not distinguishable but heading and body are
- WHEN extraction runs
- THEN heading and body are populated exactly as before this change
- AND caption/overline is null
- AND no exception is thrown

#### Scenario: No typography role detected — cold start preserved

- GIVEN a client feed with no distinguishable typography
- WHEN extraction runs
- THEN heading, body, and caption/overline are all null
- AND downstream font resolution behaves exactly as it did before this change (see content-generation CAFF-CG-06/07)

---

### Requirement: CAFF-PA-02 — Typography Roles Persist Through AestheticExtraction and ClientAestheticProfile Round-Trip

`AestheticExtraction`, `AestheticProfilePersistence`, and `ClientAestheticProfile.Typography`
MUST carry all three roles additively, with no EF Core migration. A stored profile blob
persisted before this change (zero or two roles) MUST deserialize with any missing role as
null, with no exception.

#### Scenario: Round-trip preserves three roles

- GIVEN a `ClientAestheticProfile.Typography` with populated heading, body, and caption/overline
- WHEN persisted and read back
- THEN all three roles match the persisted values with no data loss

#### Scenario: Pre-change blob (no caption key) reads caption as null

- GIVEN a stored profile blob persisted before this change containing only heading/body (or no typography key at all)
- WHEN deserialized into `ClientAestheticProfile`
- THEN caption/overline is null
- AND no exception is thrown and heading/body values are intact

---

### Requirement: CAFF-PA-03 — Per-Slide Photo-Transparency Signal Extracted as Boolean + Qualitative Descriptor

The extractor MUST extract, per slide, a photo-transparency signal expressed as a boolean
plus a qualitative descriptor string, index-aligned and parallel to `BackgroundPattern`.
The signal MUST NEVER be expressed as a numeric opacity/alpha ratio (settled decision 2).
When the signal is not determinable for a slide, that slide's entry MUST be null.

#### Scenario: Client with a consistent per-slide blended-photo habit

- GIVEN a client feed where slides at index 1 and 3 show a faded/blended photo background
- WHEN extraction runs
- THEN the transparency array has `true` + a qualitative descriptor at indices 1 and 3
- AND indices without the treatment are `false` or null
- AND the array is index-aligned with `BackgroundPattern`

#### Scenario: Client with no transparency treatment — cold start unaffected

- GIVEN a client feed with no blended/faded background habit on any slide
- WHEN extraction runs
- THEN every index in the transparency array is `false` or null
- AND no exception is thrown

#### Scenario: Signal is never a numeric alpha value

- GIVEN any extraction run that detects a transparency treatment
- WHEN the extractor DTO is inspected
- THEN the transparency field type is boolean + string descriptor only
- AND no numeric opacity/alpha field is present in the contract

---

### Requirement: CAFF-PA-04 — Per-Slide Transparency Array Is Reconciled Against BackgroundPattern Length, Never Throws

When the extracted per-slide transparency array's length differs from `BackgroundPattern`'s
length, the reconciliation step (mirroring `BackgroundPatternReconciler`'s tolerance) MUST
index-align the two arrays and pad any missing index with null. It MUST NOT throw and MUST
NOT truncate or alter `BackgroundPattern` itself.

#### Scenario: Transparency array shorter than BackgroundPattern

- GIVEN `BackgroundPattern` has 5 entries and the transparency array has 3
- WHEN the profile is built
- THEN indices 0–2 use the extracted values and indices 3–4 are null
- AND no exception is thrown
- AND `BackgroundPattern` is unchanged

#### Scenario: Transparency array longer than BackgroundPattern

- GIVEN `BackgroundPattern` has 3 entries and the transparency array has 5
- WHEN the profile is built
- THEN only indices 0–2 are retained (aligned to `BackgroundPattern`'s length)
- AND no exception is thrown

#### Scenario: Equal length — straightforward index alignment

- GIVEN `BackgroundPattern` and the transparency array both have 4 entries
- WHEN the profile is built
- THEN each transparency entry aligns 1:1 with its `BackgroundPattern` index
- AND no reconciliation/padding is needed

---

## MODIFIED Requirements

None. All requirements above are additive; existing extraction, persistence, and API
behavior for typography-absent and transparency-absent clients is unchanged.

## REMOVED Requirements

None.
