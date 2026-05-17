# Manifest Lifecycle

## Purpose
Manage the full lifecycle of manifest files: create new manifests, modify existing LOAD lists, deprecate retired manifests.

## Placement Decision
- Global reusable manifest → `.github/incoders-ai/manifests/<name>.manifest.md`.
- Project manifest → `.github/project-ai/project.manifest.md` (only one per project).

## Create
Trigger: a new project type, stack combination, or provider bundle is recurring across projects.

Required fields:
- Manifest title.
- `LOAD:` list with explicit, existing paths (no globs to non-existent folders).
- Optional: project type, supported apps, supported providers.

Project manifest additionally declares:
- Project type.
- Active global manifests.
- Active provider manifests.
- Active product providers.
- Project documents, instructions, skills and agents.

## Modify
Trigger: adding/removing a stack, provider, profile, skill or agent from a manifest's LOAD list.

Rules:
- Every entry in `LOAD:` MUST point to an existing file or directory. Broken paths are a gate-level defect.
- Removing a LOAD entry MUST be paired with a check that no other code depends on it.
- Project manifest changes MUST be reflected in `AGENTS.md` if they affect the required read order.
- Bump `.github/incoders-ai/VERSION.md` (minor for additive, major for removed/renamed entries).
- Record the change in `CHANGELOG.md`.

## Deprecate
Trigger: the manifest is replaced by a more specific one, merged, or no longer applies.

Rules:
- Replace the body with a one-line pointer to the successor manifest.
- Update any consumer that referenced the old manifest.
- Keep the file for one minor cycle before deletion.
- Note the deprecation in `CHANGELOG.md`.
