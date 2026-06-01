# Git Commit Workflow

## Purpose
Make commits and pushes explicit, reviewable, and never silent.

## Scope
Any repository operation that mutates Git state.

## Load Conditions
Always loaded for every profile.

## Do Not Load When
Never skipped.

## Inputs
- The current staged and unstaged change set.

## Procedure
1. Before staging anything, list every file that will be included with a one-line description of what changed in each.
2. Propose a commit message and show the staged diff summary before committing.
3. Commit to a feature/work branch and announce the commit — do not commit silently. No per-commit user authorization is required on work branches. If currently on `dev`/`main`, create a work branch first.
4. Push to the feature/work branch and announce the push, then open the pull request. Never push to, or merge into, `dev`/`main` — integration happens only via a pull request that the user approves and merges.
5. Never use `--no-verify`, `--force`, or `--force-with-lease` without explicit user instruction.
6. Never amend a published commit without explicit user instruction.
7. If a pre-commit hook fails, report the failure and ask how to proceed; never bypass the hook.
8. Keep commit messages in imperative present tense, focused on the "why" rather than the "what".
9. One logical change per commit; if multiple unrelated changes are staged, ask whether to split them.

## Output Expectations
- Commits, pushes, and PR openings are announced, not silent; the user approves and merges the pull request to `dev`/`main`.
- No history is rewritten silently.
- Commits map to one logical change each.
