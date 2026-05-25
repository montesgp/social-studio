# Incoders Hive Platform · Hive Social Module

Incoders Hive is the platform. **Hive Social** (also referenced as Incoders Social) is the first product module being bootstrapped in this repository.

This first usable version includes:
- .NET Aspire-orchestrated backend/API
- PostgreSQL/Supabase-ready EF Core persistence
- Organization (tenant-backed) vertical slice
- Next.js dashboard in `apps/web`

---

## Monorepo Structure

```txt
apps/
  web/                               # Next.js dashboard (App Router)
Incoders.Hive.ApiService/            # ASP.NET Core API
Incoders.Hive.AppHost/               # Aspire local orchestration
Incoders.Hive.Application/           # Use cases / ports
Incoders.Hive.Domain/                # Entities / domain rules
Incoders.Hive.Infrastructure/        # EF Core + adapters
Incoders.Hive.Tests/                 # Unit tests
docs/
openspec/
```

---

## Security & Configuration

- No real secrets should be committed.
- `.mcp.json` is ignored. Use `.mcp.example.json` as template.
- API JWT secret and DB connection must be provided locally (env vars, user secrets, or local ignored files).

### Required local values

- `ConnectionStrings__Postgres`
- `Authentication__Jwt__Secret` (>= 32 chars)

Example connection string (placeholder):

```
Host=YOUR_SUPABASE_HOST;Port=5432;Database=postgres;Username=postgres;Password=YOUR_SUPABASE_DB_PASSWORD;SSL Mode=Require;Trust Server Certificate=true
```

---

## Backend: Migrations & Run

From repository root:

```powershell
# Restore/build API
dotnet build Incoders.Hive.ApiService/Incoders.Hive.ApiService.csproj

# Apply migrations (requires ConnectionStrings__Postgres)
dotnet dotnet-ef database update `
  --project Incoders.Hive.Infrastructure/Incoders.Hive.Infrastructure.csproj `
  --startup-project Incoders.Hive.ApiService/Incoders.Hive.ApiService.csproj `
  --context Incoders.Hive.Infrastructure.Identity.AppIdentityDbContext
```

Run API only:

```powershell
dotnet run --project Incoders.Hive.ApiService/Incoders.Hive.ApiService.csproj
```

Default API URL is defined by `launchSettings.json` (currently `http://localhost:5336`).

Fast API-only development profile:

```powershell
dotnet run --project Incoders.Hive.ApiService/Incoders.Hive.ApiService.csproj --launch-profile http-fast
```

The `http-fast` profile skips automatic migrations, startup seed data, and local file logging.
Use the normal `http`/`https` profiles when you need startup data refreshed.

### Windows Smart App Control / Code Integrity

If local binaries are blocked (for example `0x800711C7`), sign local build outputs:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/dev/Sign-LocalDevBinaries.ps1
```

To auto-sign after each API build:

```powershell
dotnet build Incoders.Hive.ApiService/Incoders.Hive.ApiService.csproj -p:LocalDevSignBinaries=true
```

On Windows Debug builds this is enabled by default. To opt out for one build:

```powershell
dotnet build Incoders.Hive.ApiService/Incoders.Hive.ApiService.csproj -p:LocalDevSignBinaries=false
```

---

## Frontend: Run

```powershell
cd apps/web
copy .env.example .env.local
npm install
npm run dev
```

Default frontend URL: `http://localhost:3000`

Set `NEXT_PUBLIC_API_BASE_URL` in `apps/web/.env.local` to your API URL.

Development uses `next dev` without Turbopack. Production builds write to `.next-build`
so `npm run build` / quality gates do not corrupt the active dev server cache in `.next`.

Login route:
- `http://localhost:3000/login`

---

## Aspire Local Orchestration

The AppHost currently orchestrates:
- `apiservice`

Run:

```powershell
dotnet run --project Incoders.Hive.AppHost/Incoders.Hive.AppHost.csproj
```

Fast AppHost profile:

```powershell
dotnet run --project Incoders.Hive.AppHost/Incoders.Hive.AppHost.csproj --launch-profile http-fast
```

### Visual Studio F5

This repo includes a shared multi-project launch profile file:
- `Incoders.Hive.slnLaunch`

Use the **Aspire Full Stack (AppHost)** profile so pressing **F5** starts AppHost, which then orchestrates:
- API service
- `apps/web` Next.js frontend

---

## Current API Slice (Organizations)

`Organization` is implemented as the public API contract over the existing tenant aggregate (no duplicate business concept).

Endpoints:
- `GET /api/organizations`
- `GET /api/organizations/{id}`
- `POST /api/organizations`
- `PUT /api/organizations/{id}`
- `POST /api/organizations/{id}/deactivate` (archive/deactivate; no hard delete)

Legacy alias routes under `/api/tenants` are kept for compatibility.
