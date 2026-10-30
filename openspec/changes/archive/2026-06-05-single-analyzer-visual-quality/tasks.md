# Tasks: single-analyzer-visual-quality — Opus-as-Designer + 3-Tier Background (Fase B / B1)

<!-- Change: single-analyzer-visual-quality -->
<!-- Spec: openspec/changes/single-analyzer-visual-quality/specs/content-generation/spec-delta.md (engram #568) -->
<!-- Design: openspec/changes/single-analyzer-visual-quality/design.md (engram #571) -->
<!-- Slices: B1a (Domain/Application) and B1b (Infrastructure) — chained PRs -->

---

## Review Workload Forecast

| Metric | Value |
|---|---|
| **Estimated changed lines — B1a** | ~480–560 (domain types + application port/validator + pipeline wiring + 24 tests) |
| **Estimated changed lines — B1b** | ~380–450 (Chromium renderer + browser pool + OpusVisualDesigner + DI + 5 tests + ops) |
| **Estimated combined** | ~860–1010 |
| **400-line budget risk** | **High** — each slice individually borderline; combined far over budget |
| **Chained PRs recommended** | **Yes** |
| **Suggested split** | PR B1a (Domain/App, no browser) → PR B1b (Infra, depends on B1a frozen) |
| **Delivery strategy** | ask-on-risk (explicit chain per design) |
| **Chain strategy** | feature-branch-chain: PR B1a targets `dev`; PR B1b targets PR-B1a branch |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

> B1a and B1b are architecturally serial (B1b must NOT change B1a types). Feature-branch-chain is the
> correct strategy: B1a merges to `dev`; B1b is authored against B1a's branch so the B1b diff shows
> ONLY the infrastructure additions — the reviewer never sees B1a changes again.

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| WU-B1a | Domain/App contract, no browser, stub designer | PR 1 → `dev` | Fully testable with dotnet test; freezes all contracts for B1b |
| WU-B1b | Chromium renderer + real Opus designer + ops | PR 2 → PR-B1a branch | Depends on WU-B1a frozen; ops flag for browser install |

---

## Slice B1a — Domain / Application (engine-agnostic, stub designer)

**Status: COMPLETE** (all 33 tasks + 9 fix tasks done)

**TDD order: failing xUnit test → implementation → green → next pair.**
**Sequential within the slice. All tests run with `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj`.**

---

## Slice B1b — Infrastructure (depends on B1a frozen contract)

**Status: COMPLETE** (all 10 phase 7-10 tasks done)

**PREREQUISITE: B1a PR merged (or B1a branch stable). B1b must NOT modify any B1a type.**

---

## Test Results

- Total: 1094
- Passed: 1092
- Failed: 0
- Skipped: 2 (DB-inspection, expected)
- B1a tests: 33 + 9 fix = 42 tests (all GREEN)
- B1b tests: 20 tests (5 ChromiumBrowserPool + 4 HtmlChromiumContentRenderer + 5 B1bDiWiring + 6 OpusVisualDesigner) (all GREEN)
- Gates: 16 PASS / 2 SKIPPED (coverage + frontend — both SHOULD) / 0 MUST FAIL

---

## Spec Coverage

| Spec ID | Status | Notes |
|---------|--------|-------|
| BG-01 | Complete | 3 scenarios covered (legacy JSON, full round-trip, snapshot mirror) |
| BG-02 | Complete | 4 scenarios covered (first forced default, second rejected, different formats, null FormatKey) |
| BG-03 | Complete | 4 scenarios covered (Tier-1 forced, Tier-2 references, Tier-3 creative, different format no-match) |
| VD-01 | Complete | 2 scenarios covered (port in Application, PromptVersionKey non-empty) |
| VD-02 | Complete | 3 scenarios covered (off-palette rejected, non-manual font rejected, on-palette passes) |
| VD-03 | Complete | 2 scenarios covered (text within bounds, text overflow rejected) |
| VD-04 | Complete | 3 scenarios covered (invalid markup rejected, valid markup + key preserved, stub designer no LLM) |

**21/21 spec scenarios covered by passing tests.**

---

## Artifacts

- Proposal: `openspec/changes/single-analyzer-visual-quality/proposal.md` (also in archive)
- Design: `openspec/changes/single-analyzer-visual-quality/design.md` (also in archive)
- Tasks: `openspec/changes/single-analyzer-visual-quality/tasks.md` (also in archive)
- Spec delta: `openspec/changes/single-analyzer-visual-quality/specs/content-generation/spec-delta.md` (merged into canonical, also archived)
- Verify report: `openspec/changes/single-analyzer-visual-quality/verify-report.md` (also in archive)
