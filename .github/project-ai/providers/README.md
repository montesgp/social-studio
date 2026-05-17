# Project Providers

Project-specific external providers live here. Cross-project, reusable providers (AI models, generic infrastructure like Supabase/Postgres/Azure, generic testing tools) belong in `.github/incoders-ai/providers/`.

## What lives here

- `data/` — content research providers (Apify, Data365). Specific to the social-content workflow of this product.
- `design/` — design tooling providers (Canva). Specific to publication asset composition.
- `social/` — social publishing providers (Blotato). Specific to the publication pipeline.

## Placement rule

A provider belongs here when:
- It is tied to this product's domain (social content operations, publication pipeline) and would not be used by an unrelated Incoders project.
- Removing it from a generic Incoders ecosystem would not hurt other projects.

A provider belongs in `.github/incoders-ai/providers/` when:
- It represents a generic capability category (AI model routing, managed DB, auth, observability, testing) reusable across unrelated projects.
- Its abstractions and ports are valuable beyond this product's workflow.

When in doubt, follow `meta/lifecycle/provider.lifecycle.md`.
