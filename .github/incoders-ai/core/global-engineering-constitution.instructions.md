# Global Engineering Constitution

## Purpose
Non-negotiable principles that apply to every project loading the Incoders AI ecosystem. Project rules MAY tighten these; project rules MUST NOT weaken them.

## Scope
All repositories that load any Incoders AI manifest.

## Load Conditions
Always loaded.

## Do Not Load When
Never skipped.

## Non-negotiable principles

- **Reuse before create.** Search for an existing primitive before adding a new one. Extend, do not duplicate.
- **Stable contracts before convenience.** Designs that serve multiple clients outlast designs tailored to today's UI.
- **Modular DI/composition before centralized startup bloat.** Composition lives in capability-named modules, not in one wall of registrations.
- **OpenAPI / contract completeness before endpoint completion.** A working endpoint without discoverable contract is not finished.
- **External provider isolation before direct coupling.** Every provider sits behind an anti-corruption layer.
- **UTC everywhere for canonical date/time handling.** Local time appears at the presentation boundary only.
- **Localization-ready outputs before shipping.** Stable error codes are separate from localized text.
- **Persistence abstraction before vendor lock-in.** Domain and Application know nothing about the storage provider.
- **Explicit over implicit.** Prefer explicit behavior over magic; explicit contracts over implicit conventions.
- **Composition over inheritance.** Unless inheritance is genuinely the clearest model.
- **No speculative abstractions.** Abstractions earn their place by serving real, observed reuse.
- **Boring, readable, scalable architecture.** Optimize for the team that will maintain this in six months, not for cleverness.
- **Accessibility, responsiveness, and testability are product requirements**, not optional polish.
- **No hardcoded environment-specific values.**
- **When modifying existing code, preserve behavior** unless explicitly asked to change it.

## Delivery expectations

- Ship vertical slices that can evolve safely.
- Keep domain language aligned with the project's actual business vocabulary.
- Separate presentational concerns from orchestration and data access concerns.
- Document reusable patterns once and reuse them everywhere.
- Treat prompts, workflows, and AI evaluations as versioned product artifacts.

## Output Expectations
- Pull-request reviews can cite these principles as the reason to accept or reject a design.
- New code reads as if a single team wrote the whole repository.
