# Archive Report: Client Style Fidelity

**Applied:** 2026-10-10, direct to `dev` (owner instruction).

## Outcome

Verified on Demo Roasters (client `3bb7231d`) with the v9 extractor:

- Typography: the visual font comparison chose **League Gothic** for the heading (the model had
  named Anton), Montserrat for body and Courier Prime for captions; renders embed the real faces.
- Colours: observed combinations only — white text with a lime highlight on `#0D0D0D` (10 flat
  slides) and over photos (4). The derived lime-background pairing is gone.
- Headlines: 2-3 words per line (block 55 % of width), key phrase in the client's lime.
- No accent pill, full-width bar or empty brand-mark chip.

## Defects found and fixed while verifying

- Regenerating a format kept the first run's `StartedAt`, so the watchdog failed every regeneration
  made more than an hour after the original run (`watchdog-timeout`).
- An abandoned non-terminal profile analysis blocked every later aesthetic refresh for the client.
- The flat path did not reflow, so a taller measured headline overlapped the subtitle (CSF-CG-07).

## Not verified end-to-end

Photo slides with a freshly generated background: on the local RX 580 (DirectML) each ComfyUI image
takes ~600 s, the default `timeout_ms`, so backgrounds were discarded and slides degraded to flat.
The photo-path markup is covered by unit tests.

## Follow-ups

- `DecorationMotifs` is never threaded into `ClientAestheticDesignContext` (divider/archetype unused
  in production). Keep it unwired until divider detection is reliable — v9 still reports `Line` for
  Demo Roasters, which does not use one.
- The raw aesthetic-extraction response is not persisted; diagnosing extraction quality needs logs.
