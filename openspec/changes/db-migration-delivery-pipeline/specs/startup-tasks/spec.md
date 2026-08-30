# Delta for Startup Tasks

## MODIFIED Requirements

### Requirement: Startup Migration Application

`StartupTasks:ApplyMigrations` MUST default to `true` only when the app runs in local development against the local dev database. In every other environment, it MUST default to `false`, making the migration-delivery pipeline (not app startup) the sole authority for applying migrations.
(Previously: `ApplyMigrations` defaulted to `IsDevelopment()` with no distinction between local and any other "development"-flagged environment, and there was no pipeline apply authority — app startup was the only place migrations were ever applied.)

#### Scenario: Local dev run auto-migrates the local DB

- GIVEN the app runs locally with `ConnectionStrings:Postgres` pointing at the local Docker Postgres
- WHEN the app starts
- THEN it applies any pending migrations automatically before serving traffic

#### Scenario: Non-dev environment does not auto-migrate

- GIVEN the app runs in any environment other than local dev (including any environment targeting the shared cloud DB)
- WHEN the app starts
- THEN it MUST NOT apply migrations automatically
- AND schema changes only reach that DB via the migration-delivery apply job

#### Scenario: Explicit override still respected

- GIVEN an operator explicitly sets `StartupTasks:ApplyMigrations=true` in a non-dev environment for a documented exceptional case
- WHEN the app starts
- THEN it applies migrations as configured, overriding the environment-based default
