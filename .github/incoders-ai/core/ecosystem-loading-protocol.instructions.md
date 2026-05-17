# Ecosystem Loading Protocol

## Purpose
Defines how agents must load context.

## Load Order
1. User task.
2. Root `AGENTS.md`.
3. `.github/project-ai/project.manifest.md`.
4. Declared global manifests from `.github/incoders-ai/manifests`.
5. Declared project instructions, docs, skills and agents.

## Precedence
More specific instructions override generic ones. Project overrides global. Stack/provider overrides profile. Task overrides all non-safety rules.

## Context Discipline
Never load unrelated stack/provider files. If uncertain, inspect files and infer from manifests before loading broad context.
