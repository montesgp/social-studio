# Frontend Architecture (Hive Social Bootstrap)

## Scope
This iteration introduces the first frontend at `apps/web` for the Hive Social module.

## Stack
- Next.js (App Router)
- React + TypeScript
- Tailwind CSS
- shadcn/ui-style primitives
- Radix UI (Dialog)
- Lucide icons
- Framer Motion (subtle transitions)

## Layout
- Sidebar + topbar app shell
- Dashboard overview
- Organizations cockpit (connected end-to-end)
  - create organization
  - list/search/paginate organizations
  - inspect organization details
  - rename/update organization
  - archive/deactivate organization
  - reactivate archived organization
- Placeholder pages:
  - Users/Members
  - Clients
  - Content Workflow
  - AI Ideas
  - Publications

## API Integration
- Typed client under `apps/web/lib/api/*`
- ProblemDetails-oriented error parsing
- `NEXT_PUBLIC_API_BASE_URL` controls backend target and is required (no localhost fallback hardcodes).
- Authentication flows (`/api/auth/login`, `/api/auth/forgot-password`, `/api/auth/reset-password`) run through backend APIs only.
- Frontend stores backend token pair locally (`incoders.hive.auth.session`) and mirrors access/refresh tokens in cookies for server-side route guards.
- Organizations cockpit supports an optional manual bearer-token override in localStorage under `incoders.hive.apiToken` for bootstrap verification.

## Modularity
The folder structure is intentionally ready for incremental module growth without coupling future modules into this first slice.
