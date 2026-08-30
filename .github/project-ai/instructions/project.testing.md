# Project Testing

Use deterministic unit/integration tests plus TestSprite for AI-assisted E2E validation of the core workflow.

## Entity Lifecycle Coverage

When an agent implements or changes an end-to-end entity slice, deterministic tests must prove the full supported lifecycle for that entity. Cover create, get by id, list one/many, filter/search, update, deactivate/archive, reactivate, delete only when the product actually supports delete, tenant/organization boundaries, authorization rules, validation failures, and business invariants.

Do not invent operations for the sake of a generic CRUD checklist. Test the real contract: if the product uses deactivate/archive instead of hard delete, assert that behavior clearly.
