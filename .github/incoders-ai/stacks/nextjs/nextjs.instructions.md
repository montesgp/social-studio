# Next.js Instructions

## Scope
Next.js applications using App Router, React, TypeScript, Tailwind and shadcn/ui.

## Architecture Rules
- Use the App Router model: `app/`, `layout.tsx`, `page.tsx`, route groups and nested layouts.
- Keep routing concerns inside `app/`.
- Keep business/product code outside `app/` when it grows: prefer `features/*`.
- Use route groups to separate areas such as `(auth)`, `(workspace)`, `(admin)`, `(marketing)`.
- Do not put large business components directly inside `page.tsx`.
- `page.tsx` should compose feature components, not contain complex logic.
- Prefer server components by default.
- Use client components only when interactivity, browser APIs, state, effects, or event handlers are required.

## Server / Client Boundaries
- Do not add `"use client"` unless strictly necessary.
- Never import server-only modules into client components.
- Keep database, secrets, backend SDK admin clients, filesystem, and privileged logic server-side only.
- Client components may call frontend services/hooks, but must not access secrets.
- Server actions or route handlers must validate input and authentication.
- Be explicit when crossing boundaries between server and client.

## Folder Structure
Preferred structure:

```txt
app/
  (auth)/
  (workspace)/
  (admin)/
components/
  ui/
  layout/
  shared/
features/
  auth/
  organizations/
  clients/
  content-workflow/
  ideas/
  publications/
lib/
  api/
  supabase/
  utils.ts
hooks/
types/