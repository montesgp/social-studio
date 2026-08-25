# Spec Delta: Integrations — Profile Actor Mapping and Automatic Fallback

<!-- Change: single-analyzer-profile-analysis -->
<!-- Applies to: openspec/specs/integrations/spec.md (additive delta) -->

## Purpose

Document the required `actor_mappings_json` entries for the Instagram profile scrape
variant and specify the automatic backup-actor fallback behavior introduced by this
change. No existing integration model schema is modified; these are additive
documentation and behavior requirements.

---

## PRD Traceability

| Requirement ID | PRD Anchor |
|---|---|
| INT-01 | PRD §9 — "Each organization can configure its own third-party integration credentials without relying only on appsettings" |
| INT-02 | PRD §9 — Apify scraping with clear workflow state and error surfacing |

---

## ADDED Requirements

---

### Requirement: INT-01 — actor_mappings_json Must Contain Profile Variant Entries

The Apify `actor_mappings_json` integration credential MUST contain entries for
`networkKey = "instagram"` and `variantKey = "profile"` specifying both a default
actor identifier and a backup actor identifier. The required entry shape is:

```json
{
  "networkKey": "instagram",
  "variantKey": "profile",
  "defaultActorId": "iMcuOj3FW9xLIbqET",
  "backupActorId":  "dSCLg0C3YEZ83HzYX"
}
```

Where:
- `defaultActorId` corresponds to `get-leads~all-in-one-instagram-scraper` (Apify actor iMcuOj3FW9xLIbqET)
- `backupActorId` corresponds to `apify~instagram-profile-scraper` (Apify actor dSCLg0C3YEZ83HzYX)

The absence of these entries MUST cause the profile analysis to fail fast with code
`Integration.ProfileActorNotConfigured` before any Apify call is made. The existing
single-video entry for `networkKey = "instagram"` (without `variantKey = "profile"`)
MUST remain untouched.

#### Scenario: Profile entries resolve correct actor ids

- GIVEN an actor_mappings_json credential containing the profile variant entries above
- WHEN the profile scrape adapter resolves actors for (networkKey="instagram", variantKey="profile")
- THEN defaultActorId is iMcuOj3FW9xLIbqET and backupActorId is dSCLg0C3YEZ83HzYX

#### Scenario: Absent profile entries fail fast

- GIVEN an actor_mappings_json credential with no entry for variantKey="profile"
- WHEN a profile analysis is requested
- THEN the system returns 422 with code "Integration.ProfileActorNotConfigured"
- AND no Apify HTTP call is made

---

### Requirement: INT-02 — Automatic Backup-Actor Fallback Behavior Documented

The integrations capability documentation MUST describe the automatic backup-actor
fallback behavior: when the default actor for a given `(networkKey, variantKey)` entry
returns an Apify-level error or an empty/non-profile response, the system MUST
automatically retry using `backupActorId` in the same pipeline run without user
intervention. This behavior applies exclusively to the `variantKey = "profile"` path.
The single-video scrape path has no fallback and MUST NOT be affected.

#### Scenario: Fallback documented and traceable in diagnostics

- GIVEN the default actor for (networkKey="instagram", variantKey="profile") fails
- WHEN the automatic fallback executes
- THEN the provider diagnostics record both the default actor failure reason and
  the backup actor id used
- AND the integration documentation describes this as expected automatic behavior,
  not a configuration error

---

## MODIFIED Requirements

None. Existing integration requirements are unchanged; this delta is purely additive.

---

## REMOVED Requirements

None.
