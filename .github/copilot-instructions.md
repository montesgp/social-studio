# GitHub Copilot Instructions

Follow the repository rules in `AGENTS.md` first.

## Terminal approvals

For local VS Code Copilot agent sessions, this repository auto-approves terminal commands whose first token is `dotnet` through `.vscode/settings.json`.

Treat `dotnet ...` commands as pre-approved for build, test, restore, format, EF tooling, and SDK inspection workflows.

This is about terminal command approval, not Git policy. Per `AGENTS.md` / `git-safety.rules.md`, agents may branch, commit, push, and open pull requests; approving and merging PRs into `dev`/`main` remain the user's authority. Force pushes, history rewrites on shared branches, and destructive filesystem operations still require explicit user approval.

## Copilot CLI parity

When using GitHub Copilot CLI instead of VS Code agent mode, start the session with:

```bash
copilot --allow-tool='shell(dotnet:*)'
```

This mirrors the workspace `dotnet` command bypass because Copilot CLI tool permissions are passed as session flags, not repository settings.
