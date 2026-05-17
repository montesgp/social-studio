# ADR 0001 - Use Modular Agent Ecosystem

## Status
Accepted

## Context
Projects need reusable AI engineering rules without loading irrelevant technology-specific content.

## Decision
Use `.github/incoders-ai` as reusable global ecosystem and `.github/project-ai` as project-specific context.
Active project manifests control selective loading.

## Consequences
- Global ecosystem can evolve independently.
- Project context remains local.
- Agents avoid Angular/React/.NET/provider leakage across unrelated tasks.
