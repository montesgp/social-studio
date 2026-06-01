# Git Safety

## Purpose
Agents perform routine Git operations (branch, stage, commit, push to work branches) and open pull requests, so implementation flows without friction. The user retains **exclusive authority over the final integration**: approving and merging pull requests into the protected branches `dev` and `main`. That decision is the user's, made after reviewing the entire PR (and approval may be delegated back to an agent only on explicit user request).

## Scope
All AI agents, sub-agents, and automated workflows operating in repositories that load this ecosystem.

## Load Conditions
Always loaded.

## Do Not Load When
Never skipped.

## Core rule
Agents MAY create branches, stage, commit, push to non-protected work branches, and open pull requests — without asking for per-action authorization. Agents MUST NOT merge a pull request, nor merge/push directly into `dev` or `main`. Integration into shared branches is the user's authority, exercised after reviewing the PR.

## Allowed for agents (no per-action authorization needed)
- Branch creation (`git branch`, `git switch -c`, `git checkout -b`)
- Staging (`git add`)
- `git commit`
- `git push` to a feature/work branch (including `-u`/`--set-upstream`)
- Opening a pull request (`gh pr create` or equivalent)
- Read-only inspection: `git status`, `git diff`, `git log`, `git branch`, `git show`, `git diff --name-only`

## User-exclusive (agents MUST NOT perform without an explicit user action/request)
- Merging a pull request
- Merging into `dev` or `main`
- Pushing directly to `dev` or `main`
- Deleting shared or protected branches

Approving a PR is the user's call (we review together); it may be delegated to an agent only on explicit user request. The final merge to `dev` or `main` is decided by the user after reviewing the entire PR.

## Always forbidden (regardless of authority)
- `--no-verify`, `-c core.hooksPath=/dev/null`, or any flag that skips/bypasses hooks
- `--force` / `--force-with-lease` / force push, unless the user explicitly instructs it on a non-protected branch
- History rewrite on shared branches (`git rebase`, `git reset --hard`) without explicit user instruction
- Amending a published commit without explicit instruction
- `rm -rf` on any path containing source files
- Bypassing a failing pre-commit/pre-push hook — report the failure and ask; never bypass

## Branch model
- Implementation happens on feature/work branches (including SDD chained-PR branches and integration/tracker branches).
- Protected branches: `dev` and `main`. They are reached ONLY via a user-merged pull request.
- If the working tree is currently on `dev` or `main`, create a work branch BEFORE committing — never commit straight onto a protected branch.

## Commit hygiene (apply to every agent commit)
1. One logical change per commit; if unrelated changes are staged, split them.
2. Conventional commits, imperative present tense, focused on "why" not "what".
3. No AI attribution / `Co-Authored-By` lines.
4. If a hook fails, report and ask how to proceed — never bypass it.
5. Before staging, it remains good practice to summarize which files change and why.

## Completion rule
A task may include committing/pushing to a work branch and opening a pull request. It is complete when the changes are committed/pushed and the PR is opened, the code compiles (if applicable), and repository rules are satisfied. Final integration awaits the user's PR review and merge.

## Safety priority
This rule has higher priority than speed of execution, automation convenience, agent autonomy, and workflow shortcuts.

## Failure condition
If an agent merges a pull request, or merges/pushes into `dev` or `main`, without an explicit user action or request, the behavior is INVALID and the instruction contract has been violated.

## Final rule
Agents branch, commit, push, and open pull requests. Users approve and merge to `dev`/`main`.
