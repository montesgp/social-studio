# Spec Delta: Integrations — Per-Client Account Credentials

<!-- Change: content-bank-planner -->
<!-- Applies to: openspec/specs/integrations/spec.md (additive delta) -->

## Purpose

Layer a **per-client account credential** level on top of the existing org-level `OrganizationIntegration` model, so a `Publication` destination can be resolved against both (a) the org's API app access and (b) the specific client account's own token/external account id for a network. This is additive; it does not change org-level integration behavior, connection modes, or statuses.

## ADDED Requirements

### Requirement: INT-BANK-01 — Per-Client Account Credential Exists Alongside Org-Level Integration

The system MUST persist, per client and per social network, a credential record (extending `ClientOwnAccount` or a new dedicated entity) holding the client account's publish token and external account id for that network. This record MUST be distinct from and layered on top of the org-level `OrganizationIntegration` record for the same provider — resolving a publication destination MUST require both to be present and valid.

#### Scenario: Per-client credential resolves alongside org-level integration

- GIVEN an org with a connected Instagram `OrganizationIntegration` and a client account with a stored Instagram credential
- WHEN a destination is resolved for that client account and Instagram
- THEN both the org-level integration and the per-client credential are returned together

#### Scenario: Org-level integration connected but client has no credential

- GIVEN an org with a connected Instagram `OrganizationIntegration` but a client account with no stored Instagram credential
- WHEN a destination is resolved for that client account and Instagram
- THEN resolution fails with a structured error identifying the missing per-client credential
- AND the org-level integration status is unaffected by this failure

### Requirement: INT-BANK-02 — Per-Client Credentials Are Additive to Existing Integration Behavior

Introducing per-client account credentials MUST NOT change the existing `OrganizationIntegration` catalog, connection modes (`BringYourOwnKey`, `ManagedByIncoders`, `SystemDefault`), statuses, or the Settings > Integrations checklist behavior for Single Analyzer or Generator setup.

#### Scenario: Existing org-level integration checklist is unaffected

- GIVEN an org's existing Single Analyzer and Generator setup checklists
- WHEN per-client account credentials are introduced for Instagram
- THEN the existing checklists continue to evaluate the same org-level connected-provider conditions as before

## MODIFIED Requirements

None. This delta adds a new credential layer without altering any existing `integrations` requirement.

## REMOVED Requirements

None.
