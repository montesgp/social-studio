# Secrets Gate

## Purpose
Detect committed (or about-to-be-committed) secrets, tokens, private keys, or credentials before they reach the remote.

## Scope
Applies to all tracked text files except paths listed in `project.gates.yml -> secrets.ignore_paths[]`.

## Load Conditions
Every CI run; every local pre-task validation; SHOULD also be wired as a pre-commit hook (Phase 2).

## Do Not Load When
Never. This gate is always-on.

## Rules

### Pattern Detection (default level: **MUST**)
The scanner MUST detect at minimum:

- AWS Access Key ID (`AKIA[0-9A-Z]{16}`).
- AWS Secret Access Key (`(?i)aws(.{0,20})?(secret|access)(.{0,20})?[=:]\s*['"]?[A-Za-z0-9/+=]{40}['"]?`).
- Generic high-entropy `*_KEY=`, `*_SECRET=`, `*_TOKEN=` assignments with values >= 16 chars and not placeholders (`xxx`, `your-…`, `<…>`, `change-me`).
- PEM blocks (`-----BEGIN (RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----`).
- JWT-shaped strings (`eyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}`).
- Supabase service role keys (`sbp_[a-z0-9]{40,}` and `sb-[a-z0-9-]{20,}` with role hints in nearby text).
- GitHub PATs (`ghp_[A-Za-z0-9]{36,}`, `github_pat_[A-Za-z0-9_]{82,}`).
- OpenAI keys (`sk-[A-Za-z0-9]{32,}`).
- Anthropic keys (`sk-ant-[A-Za-z0-9_\-]{32,}`).

Additional patterns MAY be declared in `project.gates.yml -> secrets.patterns_extra[]`.

### File Hygiene (default level: **MUST**)
- `.mcp.json`, `.env`, `.env.local`, `appsettings.Local.json`, `secrets.json` MUST remain in `.gitignore`.
- Any of these files in the working tree MUST NOT be staged.

### False Positives (default level: **SHOULD**)
- Sample/example files (`*.example*`, `*.template*`) SHOULD use placeholder values that the regex set tolerates (`xxx`, `your-…`, `<…>`).

## Output Expectations
- `[ MUST ] secrets.scan: pass|fail — <findings> findings (<severity-breakdown>)`.
- Each finding MUST be reported as `<file>:<line>: <pattern-name>: <redacted-snippet>`.
- The full secret value MUST NEVER be echoed; only first 4 + last 4 chars or pattern name.
- Non-zero exit code on any finding.

## Future Enhancements (Phase 2)
- Replace internal regex scanner with `gitleaks` (already industry standard).
- Pre-commit hook bootstrap.
- Truffleshog or similar for entropy-based detection.
- Secret rotation policy + automated revocation hooks.
