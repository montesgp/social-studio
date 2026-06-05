# Reuse Before Create

## Purpose
Force a search for existing primitives before creating a new one.

## Scope
Any change that proposes a new helper, mapper, validator, utility, service, adapter, dialog, button variant, filter panel, form shell, table/list shell, modal, loader, status badge, pagination control, or notification primitive.

## Load Conditions
Always loaded for every profile.

## Do Not Load When
Never skipped.

## Inputs
- The shape and intent of the new abstraction.

## Procedure
1. Identify the role of the new abstraction (transform, validate, render, orchestrate, etc.).
2. Search the repository for existing primitives that play the same role under any name.
3. If a candidate exists, evaluate whether it can be extended before deciding to create a parallel one:
   - **Same behavior / state machine, different presentation or configuration** (e.g., different step labels, count, color variant): add a prop to the existing primitive. Do not create a parallel component for a configuration variant.
   - **Different interaction contract or accessibility role** (e.g., navigable wizard vs. passive progress indicator): a new registered primitive may be justified — document why in the PR description.
4. If a candidate is partially broken: fix the shared primitive first, then consume it.
5. If creating a new primitive is justified, name it after the role it plays and document why no existing primitive served the need.
6. Remove orphaned ad-hoc alternatives when consolidating.

## Output Expectations
- Pull requests that re-introduce a deleted parallel pattern are sent back for refactor.
- Each shared primitive has one home; no second equivalent exists.
- Review comments explicitly cite the existing primitive when duplication is detected.
