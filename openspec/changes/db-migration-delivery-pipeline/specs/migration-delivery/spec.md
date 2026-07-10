# Migration Delivery Specification

## Purpose

Pipeline that proves migrations apply cleanly on an ephemeral database (PR-time) and then delivers them to the real cloud DB on merge, with a mandatory pre-apply backup.

## Requirements

### Requirement: Idempotent Script Artifact

CI MUST generate an idempotent migration SQL script (`dotnet ef migrations script --idempotent`) for every PR touching migrations and publish it as a reviewable build artifact.

#### Scenario: PR includes new migrations

- GIVEN a PR adds one or more EF migrations
- WHEN the verify job runs
- THEN CI publishes the generated idempotent SQL script as a downloadable artifact

### Requirement: Ephemeral Verification

CI MUST apply the generated script to a disposable Postgres service container and MUST perform an `Up → Down → Up` round-trip before the PR can be considered verified.

#### Scenario: Script applies cleanly

- GIVEN the idempotent script from the current PR
- WHEN CI applies it to a fresh ephemeral Postgres container
- THEN the apply succeeds with no errors
- AND the subsequent `Down` then `Up` round-trip also succeeds

#### Scenario: Script fails to apply

- GIVEN the idempotent script from the current PR
- WHEN CI applies it to a fresh ephemeral Postgres container and the apply errors
- THEN the verify job FAILS
- AND the PR cannot pass required checks

### Requirement: Pre-Apply Backup Gate

On merge to `dev`, the system MUST take a backup/snapshot of the cloud database BEFORE applying any migration, and MUST NOT proceed to apply if the backup step fails.

#### Scenario: Backup succeeds then apply runs

- GIVEN a merge to `dev` triggers the apply job
- WHEN the pre-apply backup step completes successfully
- THEN the apply step runs the freshly regenerated idempotent script against the cloud DB

#### Scenario: Backup fails and apply is blocked

- GIVEN a merge to `dev` triggers the apply job
- WHEN the pre-apply backup step fails or errors
- THEN the apply step MUST NOT run
- AND the job reports failure

### Requirement: Automatic Apply on Merge

Applying to the cloud DB MUST happen automatically on merge to `dev`, without a manual approval gate, relying on ephemeral verification as the safety net.

#### Scenario: Merge triggers apply without manual intervention

- GIVEN a PR with a passing ephemeral-verify check is merged to `dev`
- WHEN the merge completes
- THEN the apply job starts automatically and requires no human approval step

### Requirement: Environment-Scoped Secret, Forward-Compatible

The apply job MUST read its DB connection secret from a single GitHub Environment (`dev` today) and MUST use an identical apply invocation regardless of environment, so a new tier can be added later with only a new Environment + secret.

#### Scenario: Adding a future tier requires no workflow change

- GIVEN the team later provisions a `staging` GitHub Environment with its own DB secret
- WHEN the apply workflow is pointed at that environment
- THEN the same apply job logic runs unchanged — only the environment name and secret value differ
