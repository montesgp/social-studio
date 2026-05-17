# Git Safety (STRICT)

## Purpose
Prevent agents from performing automatic Git write operations. The user is the only authority responsible for persisting changes to the repository.

## Scope
All AI agents, sub-agents, and automated workflows operating in repositories that load this ecosystem.

## Load Conditions
Always loaded.

## Do Not Load When
Never skipped.

## Core rule (NON-NEGOTIABLE)
Agents MUST NOT execute Git write operations unless the user explicitly requests it. This applies regardless of task type, agent type, or execution mode.

## Forbidden by default
The following commands are strictly forbidden unless explicitly requested by the user:
- `git add`
- `git commit`
- `git push`
- `git pull`
- `git merge`
- `git rebase`
- `git checkout`
- `git switch`
- `git reset` (especially `--hard`)
- `git tag`
- branch creation
- branch deletion
- force push operations
- `rm -rf` on any path containing source files

## Allowed (read-only)
- `git status`
- `git diff`
- `git log`
- `git branch --show-current`
- `git show`
- `git diff --name-only`

These must not modify repository state.

## Mandatory behavior
When a task modifies files:
1. Apply code changes only.
2. Stop after file modifications are complete.
3. DO NOT stage changes.
4. DO NOT create commits.
5. DO NOT push changes.

## Commit workflow (when the user authorizes)
When the user explicitly authorizes a commit:
1. Before staging anything, list every file that will be included with a one-line description of what changed in each.
2. Propose a commit message and show the full staged diff summary before asking final permission.
3. Only proceed to `git commit` after explicit confirmation.
4. Never use `--no-verify`, `--force`, `--force-with-lease`, or any flag that bypasses hooks or overwrites remote history without explicit instruction.
5. Never amend a published commit without explicit instruction.
6. If a pre-commit hook fails, report the failure and ask how to proceed — never bypass it.
7. Keep commit messages in imperative present tense, focused on "why" not "what".
8. One logical change per commit; if multiple unrelated changes are staged, ask whether to split them.

## Completion rule
A task is considered complete when files are created/modified and the code compiles (if applicable) and repository rules are satisfied. A task is NOT dependent on committing changes.

## Explicit override
Git operations are ONLY allowed if the user explicitly requests them. Valid examples: "commit these changes", "create a commit", "push this branch", "create a new branch". Without explicit instruction, Git operations MUST NOT be performed.

## Safety priority
This rule has higher priority than speed of execution, automation convenience, agent autonomy, and workflow shortcuts.

## Failure condition
If an agent performs a Git write operation without explicit user request, the behavior is INVALID and the instruction contract has been violated.

## Final rule
Agents modify files. Users control Git.
