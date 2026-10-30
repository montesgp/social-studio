# Quality Gates - Phase 1

## What this is
Phase 1 of an AI-governed engineering quality system for Incoders repositories.
It turns the documentation layer under `.github/incoders-ai/**` into automatic,
runnable enforcement, without dragging in heavy enterprise tooling.

It is intentionally pragmatic: small, scriptable, locally runnable, CI-runnable,
and portable to a future central `incoders-ai-governance` repository.

## Layout

| Concern | Location |
|---|---|
| Gate definitions (rules, severity) | `.github/incoders-ai/gates/*.md` |
| Project-local configuration | `project.gates.yml` (repo root) |
| Local + CI orchestrator | `scripts/gates/Run-Gates.ps1` |
| Individual checks | `scripts/gates/checks/Check-*.ps1` |
| Shared helpers | `scripts/gates/lib/*.ps1` |
| Reusable CI workflow | `.github/workflows/_reusable-quality-gates.yml` |
| Project caller workflow | `.github/workflows/quality-gates.yml` |
| PR template | `.github/PULL_REQUEST_TEMPLATE.md` |
| Git hooks (tracked) | `scripts/gates/hooks/{pre-commit,pre-push}` |
| Hook installer | `scripts/gates/Install-Hooks.ps1` |

## Severity model
Defined in `.github/incoders-ai/gates/gates.policy.md`:
- **MUST** - blocks merge, fails CI and `Run-Gates.ps1` (exit 1).
- **SHOULD** - warning. Does not fail by default. Can be promoted via `fail-on-warn: true` in CI.
- **MAY** - informational only.

Projects MAY tighten severity in `project.gates.yml`. Downgrading a MUST requires an ADR.

## Phase 1 gates

| Gate | Default | What it does |
|---|---|---|
| `build` | MUST | `dotnet build` solution + `npm run build` per app. |
| `tests` | MUST | `dotnet test` on declared test projects, optional npm test scripts. |
| `coverage` | SHOULD (warn-only) | Aggregates coverlet cobertura output, reports % vs `minimum` and `target`. |
| `architecture` | MUST | Verifies hexagonal layering on `<ProjectReference>` graph + flags forbidden namespace `using` imports per layer. |
| `dependencies` | MUST | Cycle detection on the project graph + solution drift + IVT reciprocity. |
| `secrets` | MUST | Regex scan for AWS/JWT/PEM/Supabase/GitHub/OpenAI/Anthropic keys; sensitive-file gitignore hygiene. |
| `security` | MUST | `appsettings*.json` placeholder check + insecure default patterns + http-vs-https hint. |
| `vulnerabilities` | SHOULD | `dotnet list package --vulnerable` + `npm audit --audit-level=high`. |

## Run locally

PowerShell (Windows native or `pwsh` cross-platform):

```powershell
# Run every gate
pwsh -File scripts/gates/Run-Gates.ps1

# Only fast static gates (no build/test/network)
pwsh -File scripts/gates/Run-Gates.ps1 -Only @('architecture','dependencies','secrets','security')

# Skip slow ones during inner-loop iteration
pwsh -File scripts/gates/Run-Gates.ps1 -Skip @('build','tests','vulnerabilities')

# Machine-readable output
pwsh -File scripts/gates/Run-Gates.ps1 -Json -NoSummary
```

Exit codes:
- `0` - every MUST gate passed.
- `1` - at least one MUST gate failed.
- `2` - configuration error.

PowerShell 5.1 (built-in `powershell.exe` on Windows) also works:

```powershell
& powershell -NoProfile -File ./scripts/gates/Run-Gates.ps1
```

### Per-line opt-out
Lines containing the marker `gates:secret-allow` are skipped by the secrets scanner.
Use sparingly, only for documented test fixtures or sample placeholders.

```csharp
const string FakeKey = "deliberately-fake-token-for-demo-12345"; // gates:secret-allow
```

## Git hooks (when developing locally)

Hooks are tracked under `scripts/gates/hooks/` and installed into `.git/hooks/` per clone (git does not track `.git/`). Install once per fresh clone:

```powershell
# Install pre-commit only (fast gates, ~3-5s per commit)
pwsh -File scripts/gates/Install-Hooks.ps1

# Install pre-commit + pre-push (full suite on push, ~30s warm)
# Recommended when pushing directly to main without PR review.
pwsh -File scripts/gates/Install-Hooks.ps1 -PrePush

# Remove hooks
pwsh -File scripts/gates/Install-Hooks.ps1 -Uninstall
```

What each hook does:

- **`pre-commit`**: runs `architecture`, `dependencies`, `secrets`, `security`. Sub-5s. Blocks the commit on any MUST failure.
- **`pre-push`**: runs the FULL suite (build, tests, vulnerabilities, etc.). 30s warm, up to 3 min cold. Blocks the push on any MUST failure.

Bypass (humans only, never AI agents):

```bash
git commit --no-verify
git push --no-verify
```

The installer is safe to re-run; it overwrites only its own hooks (recognized by a marker comment) and refuses to clobber unrelated hooks unless `-Force` is passed.

## CI

- `.github/workflows/quality-gates.yml` runs on every PR targeting `dev` or `main`, and on demand via `workflow_dispatch`.
- It calls `_reusable-quality-gates.yml`, which sets up dotnet + node + pwsh and invokes the same script developers run locally.
- Coverage artifacts are uploaded under the `coverage` artifact name for 14 days.

### Path filtering

Doc-only changes do NOT trigger CI. The `paths-ignore` block in `quality-gates.yml` excludes:

- `docs/**`, `**/*.md`, `LICENSE`
- `.github/incoders-ai/gates/**` and any `*.md` under `.github/incoders-ai/**` or `.github/project-ai/**`
- `.alt/**`

If a PR targeting `dev`/`main` touches at least one non-ignored file, the full workflow runs. If the change is purely documentation, no compute is spent.

## How to port to another repo

1. Copy `.github/incoders-ai/gates/`, `.github/workflows/_reusable-quality-gates.yml`, `.github/workflows/quality-gates.yml`, `.github/PULL_REQUEST_TEMPLATE.md`, `scripts/gates/`, `project.gates.yml`, and this document.
2. Adjust `project.gates.yml` to reflect the target solution / projects / frontend apps.
3. Update `.NET` and `Node` versions in `quality-gates.yml` if needed.
4. Done. The reusable workflow is self-contained.

When the central `incoders-ai-governance` repo exists, step 1 collapses to:
- Replace `_reusable-quality-gates.yml` and `scripts/gates/` with a single `uses:` reference to the central workflow at `incoders/incoders-ai-governance@v1`.
- Keep only `project.gates.yml` and `quality-gates.yml` (caller) in the project repo.

## What is intentionally NOT in Phase 1
- SonarQube / SonarCloud integration.
- CodeQL.
- ArchUnitNET / NetArchTest fitness functions.
- Mutation testing (Stryker).
- Contract / consumer-driven testing.
- OpenAPI governance (lint, breaking-change detection).
- Runtime security headers / CSP enforcement.
- AI-assisted E2E (TestSprite / Playwright).
- SBOM generation / license compliance.
- Pre-commit hook installer.
- Renovate / Dependabot config.

These are good ideas. They belong to Phase 2 / Phase 3 when this baseline is stable and not a maintenance burden.

## Roadmap

### Phase 2 - Hardening (after baseline runs green for 2 weeks)
- Promote `vulnerabilities` from SHOULD -> MUST once the inventory is clean.
- Set `coverage.minimum > 0` and store a baseline; fail when trend drops > 1pp.
- Replace internal regex secret scanner with `gitleaks` (drop-in replacement; richer rules + entropy).
- Add architecture fitness functions:
  - `NetArchTest` or `ArchUnitNET` for backend layering tests living inside `SocialStudio.Tests`.
  - `dependency-cruiser` for the Next.js app.
- Pre-commit hook bootstrap script (calls `Run-Gates.ps1` with `-Only @('secrets','architecture')` for fast iteration).
- Renovate / Dependabot config for backend and frontend.
- `Directory.Build.props` to enforce common .NET settings centrally (LangVersion, AnalysisLevel, EnableNETAnalyzers).
- Per-layer coverage thresholds (Domain ≥ 90%, Application ≥ 80%).

### Phase 3 - Defense-in-depth
- SonarCloud integration (call from reusable workflow).
- CodeQL on PRs.
- Mutation testing (Stryker.NET) on Domain + Application.
- Contract testing (Pact.NET) for cross-service boundaries; use cases first.
- OpenAPI governance: schema lint (Spectral), breaking-change detection (oasdiff) on the API contract.
- AI-assisted E2E with TestSprite or Playwright on the golden user journey.
- Runtime governance: scan deployed environments for security headers, CSP, TLS versions; consume into a SHOULD CI gate via deploy webhook.
- Observability-driven quality: SLO checks against Grafana/Loki dashboards as a post-deploy gate.
- Advanced DevSecOps: SBOM (CycloneDX), license compliance, dependency provenance (SLSA L2/L3).
- Move workflows + scripts to a central `incoders-ai-governance` repo; pin via tag (`@v1`) and consume per repo.

## Risks and known limitations

- **YAML parser is intentionally minimal.** It supports the subset used by `project.gates.yml`. If the schema grows beyond simple maps/lists/scalars, swap to the `powershell-yaml` PSGallery module (one-line change in `Run-Gates.ps1`).
- **Secret scanner has false-positive trade-offs.** Generic `KEY="..."` detection is heuristic; placeholder regex covers common patterns (`__X__`, `xxx`, `change-me`, `unit-test-*`, ...) and a `// gates:secret-allow` marker is provided. Phase 2 (`gitleaks`) reduces false positives further.
- **Architecture gate compares using-directives, not full type usage.** A determined developer could route through aliases. Phase 2 (`NetArchTest`/`ArchUnitNET`) fixes this with proper IL inspection.
- **Coverage gate aggregates per-test-project cobertura output simply.** If multiple test projects emit overlapping reports, percentages are computed across the union of files. Phase 2 may switch to `reportgenerator` for proper merging.
- **Coverage collection is opt-in.** `gates.test.collect_coverage` defaults to `false` because some coverlet.collector + .NET SDK combinations crash tests when `--collect:XPlat Code Coverage` is added. Enable it locally and verify before flipping it on for everyone, then promote `gates.coverage.minimum > 0` in Phase 2.
- **Vulnerability scan requires network.** Offline runs degrade to warnings, never failures. CI runs are authoritative.
- **PowerShell-only scripting.** Runs on Windows (built-in) and Linux/macOS via `pwsh`. Bash-only environments would need a wrapper; not worth the maintenance burden for Phase 1 since GitHub runners ship `pwsh`.
- **Strict mode not enabled in scripts.** PS 5.1 strict mode breaks defensive XML/PSCustomObject access; we trade strictness for robustness. Phase 2 may move scripts to PS 7.4+ only and re-enable.
