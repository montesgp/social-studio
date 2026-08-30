# Migration Drift Gate Specification

## Purpose

CI gate that fails a PR when the EF Core model no longer matches the committed migrations, catching schema drift before merge. Secret-free — no DB connection required.

## Requirements

### Requirement: Drift Detection on PR

The system MUST detect when the EF Core model differs from the last committed migration and MUST fail the CI check when drift is found.

#### Scenario: Model changed without a migration

- GIVEN a PR changes an EF entity/configuration but adds no new migration
- WHEN `Check-MigrationDrift.ps1` runs in CI
- THEN the gate check FAILS
- AND the PR cannot pass required checks

#### Scenario: Model change matched by a migration

- GIVEN a PR changes an EF entity/configuration and includes a matching migration
- WHEN `Check-MigrationDrift.ps1` runs in CI
- THEN the gate check PASSES

#### Scenario: No model changes

- GIVEN a PR touches no EF model code
- WHEN `Check-MigrationDrift.ps1` runs in CI
- THEN the gate check PASSES with no drift reported

### Requirement: No DB Access Required

The drift gate MUST run without any database connection string or CI secret.

#### Scenario: Gate runs with zero DB credentials configured

- GIVEN CI has no `ConnectionStrings__Postgres` or DB secret set
- WHEN the drift gate job runs
- THEN it completes and reports pass/fail based only on source-code comparison

### Requirement: Gate Registration

The drift check MUST be registered in `project.gates.yml` and documented in the Gate Index, following the existing `scripts/gates/checks/*.ps1` pattern.

#### Scenario: Gate is discoverable

- GIVEN a contributor reads `.github/incoders-ai/gates/README.md`
- WHEN they look up gate names
- THEN `migration-drift` appears with its level (MUST/SHOULD) and doc link
