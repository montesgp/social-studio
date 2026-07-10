# Local Dev Database Specification

## Purpose

A disposable, local Docker Postgres instance so developers stop auto-migrating the single shared cloud DB during local `dotnet run`/F5 sessions.

## Requirements

### Requirement: Disposable Local Postgres

The repo MUST provide a `postgres:16` Docker Compose service that developers can start locally to serve as the local dev database.

#### Scenario: Developer starts local DB

- GIVEN a developer runs the provided compose command
- WHEN the container starts
- THEN a Postgres 16 instance is reachable at the configured local connection string

### Requirement: Local Auto-Migrate and Seed

When the app targets the local Docker Postgres, it MUST auto-apply migrations and run existing `SeedData` bootstrap on startup.

#### Scenario: First run against empty local DB

- GIVEN a developer points `ConnectionStrings:Postgres` at the local Docker Postgres
- WHEN they run the app locally
- THEN the app applies all pending migrations
- AND `SeedData` bootstraps the sysadmin user, roles, and catalogs
