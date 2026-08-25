# ComfyUI Generation-Time Findings Report

<!-- Change: client-aesthetic-fidelity-fixes -->
<!-- WU-0 — investigative deliverable, documentation only, no code change -->

## Status

Investigative — final. This report satisfies proposal Scope item 6 and the "ComfyUI findings
report" success criterion. **No code fix is recommended.**

## Trigger

Two `provider_timeout` failures were observed on the Lucia Fernandez reference job
(`client_id 0186cd86-…`, analysis `3f0e272f-…`, job `894ccda7-…`) during ComfyUI image
generation, each waiting the full configured timeout before failing.

## Findings

- **`ComfyUiSettings.DefaultTimeout = 600_000 ms`** (10 minutes exactly) — matches the two
  observed `provider_timeout` failures precisely. This is the configured HTTP-flow budget
  (`ComfyUiImageModelClient.cs` L142). It is a deliberate, explicit setting, **not a bug**.
- **`ComfyUiSettings.DefaultSteps = 20`, `DefaultCfg = 7.0`** — both are standard/moderate
  values for SD1.5/SDXL-class checkpoints. There is no evidence of a hardcoded excessive
  step count driving the slowdown.
- **`MaxGenerationPixels = 1_310_720` with `LatentDimensionMultiple = 64`**
  (`ClientAestheticImageBackgroundService.cs` L82-90) — documented as measured empirically
  on "ComfyUI 0.3.48, `--directml --lowvram --cpu-vae`": 1024x1024 succeeds, while 912x1144
  and 1080x1352 both OOM despite similar or fewer total pixels (divisibility-by-64 matters
  more than raw pixel count on this backend). This corroborates the resolution the
  successful slide-4 generation ran at (~1024x1280, within the 1,310,720-px cap).
- **`IntegrationExecutionPolicy.cs`** — exponential backoff (2s base, capped 30s) between
  retries is not the source of the ~20-minute total waste on the reference job; that came
  from two full `MaxAttempts` each timing out at the full 600s budget.
- A single ~1.3-Mpx image at 20 steps took **~9m35s** on the observed backend
  configuration: `--directml --lowvram --cpu-vae`.

## Assessment

**~9m35s for a single ~1.3-Mpx image at 20 steps on `--directml --lowvram --cpu-vae` is
consistent with known DirectML/CPU-VAE performance characteristics.** DirectML lacks
CUDA-kernel-level optimization, and forcing VAE decode onto the CPU is a well-documented
ComfyUI slowdown, especially when combined with low-VRAM mode. No code-level
misconfiguration was found — steps, CFG scale, and resolution caps are all reasonable and
already tuned to this backend's empirically observed OOM boundaries. The generation-time
cost is attributable to the backend hardware/execution-mode configuration
(`--directml --lowvram --cpu-vae`), which is outside this codebase's control.

## Recommendation

**No code fix is recommended.** No timeout, step count, CFG value, resolution cap, or
retry-policy change is proposed as part of this change. Any future GPU/backend
infrastructure change (e.g., moving off DirectML/CPU-VAE to a CUDA-capable backend) is an
infrastructure decision outside this change's scope and is explicitly out of scope per the
proposal's "Out of Scope" table (`Any ComfyUI code, settings, timeout, or hardware/
execution-mode change`).

## Source

This report formalizes exploration.md's Item 5 findings
(`openspec/changes/client-aesthetic-fidelity-fixes/exploration.md`, "Item 5 — ComfyUI
generation-time report (investigative only)") as the standalone deliverable required by
proposal Scope item 6.

*Archived 2026-08-24.*
