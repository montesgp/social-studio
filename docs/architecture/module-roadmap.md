# Module Roadmap

## Platform Context
- **Incoders Hive** = platform
- **Hive Social / Incoders Social** = first module

## Persistence Schema Strategy
- `identity` schema: authentication/authorization.
- `platform` schema: cross-module platform data such as organizations, memberships, system settings and provider/model catalogs.
- `social` schema: Hive Social / Incoders Social MVP data.
- Future product modules should add module-level schemas rather than mixing module data into `public`.

## Current Iteration (Completed Bootstrap Scope)
- API + persistence foundation on PostgreSQL/Supabase-ready EF Core
- Organization vertical slice (tenant-backed)
- First Next.js dashboard shell + organizations UI

## Next Recommended Iterations
1. Members/roles management slice
2. Clients bounded context
3. Content research workflow state tracking
4. AI idea generation/evaluation contracts
5. ~~Publication pipeline tracking~~ — first slice shipped by `content-bank-planner` (Content Bank + Publication axis + Instagram publish-now; see `docs/PRD.md` §12, `docs/SDD.md` §13, ADR 0005). Scheduler/dispatcher + other networks remain deferred.

## Explicitly Deferred in this Iteration
- Campaigns module
- Leads module
- Analytics module
- Automations module
- Agents module
