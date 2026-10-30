# Canva Connector — Operator Setup Guide

This guide covers everything an operator and an end user need to get the Canva integration working end-to-end: the one-time org-level connector setup (B2) and the per-user "Export to Canva" feature (B3).

> See also: the integrations spec delta at [`openspec/changes/single-analyzer-canva-export/specs/integrations/spec-delta.md`](../../openspec/changes/single-analyzer-canva-export/specs/integrations/spec-delta.md).

---

## At a glance

| Topic | Value |
|---|---|
| Auth model | OAuth 2.0 authorization-code + PKCE (per user); org-level BYOK credential (client_id / client_secret) |
| User-facing feature | Download PPTX (editable) + Export to Canva (opens editable design) |
| Required Canva plan | Any paid plan suffices for Design Import (non-Enterprise is fine) |
| Enterprise-only features | Autofill / Brand Templates — deferred, not active |
| API surface used | Canva Connect API (Design Import) only |

---

## Part 1 — Org-level connector setup (operator, one time)

### Quick path

1. Go to the [Canva Developer Portal](https://www.canva.com/developers/) and sign in.
2. Navigate to **Your apps** and create a new app (or select an existing one).
3. Under **Authentication**, copy the **Client ID** and **Client secret**.
4. In Social Studio go to **Settings → Integrations → Canva**.
5. Select connection mode **Bring your own key**, paste **Client ID** and **Client secret**.
6. Optionally adjust the **Scopes** field (see below). Leave blank to use the default set.
7. Click **Save** and then **Test connection**.

### Where to get the credentials

| Field | Where to find it |
|---|---|
| Client ID | Canva Developer Portal → Your apps → `<your app>` → Authentication tab |
| Client secret | Same page — click **Reveal** to show the secret |

Both values are generated when you create a Connect app. If you have not created one yet, follow the [Canva Connect API quickstart](https://www.canva.com/developers/docs/connect/getting-started/).

### Default scope string

The **Scopes** field ships the following default value:

```
asset:read asset:write brandtemplate:meta:read design:meta:read design:content:read design:content:write profile:read
```

These are the minimum scopes needed for B2 and B3. Leave the field blank or unchanged to use this default. You may extend the list — additional scopes are space-separated.

| Scope | Purpose |
|---|---|
| `asset:read` | Read assets from the org's library |
| `asset:write` | Upload or update assets |
| `brandtemplate:meta:read` | Read brand template metadata (reserved, not active) |
| `design:meta:read` | Read design metadata |
| `design:content:read` | Read design content |
| `design:content:write` | Create or update design content (Design Import) |
| `profile:read` | Read the authenticated user's profile |

---

## Part 2 — Per-user "Connect to Canva" flow (end user, once per account)

The Export to Canva feature requires each user to authorize the platform to act on their behalf in Canva. This is a standard OAuth 2.0 authorization-code flow with PKCE — no password is shared with Social Studio.

### How the flow works

```
User clicks "Export to Canva"
  → not connected → UI redirects to /api/integrations/canva/authorize?orgIntegrationId=...
  → platform redirects user to canva.com (authorization page)
  → user approves → canva.com redirects to /api/integrations/canva/callback?code=...&state=...
  → platform exchanges code+verifier for access token + refresh token
  → tokens stored per user (scoped to that user + that org integration)
  → user redirected to /integrations/canva/connected — connection is live
```

### Endpoints (API reference)

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/integrations/canva/authorize` | Start OAuth PKCE flow — 302 redirects to Canva |
| `GET` | `/api/integrations/canva/callback` | OAuth callback — exchanges code, stores tokens, 302 redirects to UI |

Both endpoints require the user to be authenticated. The `orgIntegrationId` query parameter identifies the org's Canva integration row.

### Token storage and refresh

- Tokens are stored **per user + per org integration**, not org-wide.
- The access token is short-lived; the platform refreshes it transparently before export using the stored refresh token.
- Canva uses **single-use rotating refresh tokens**: every refresh call produces a new refresh token, which is stored atomically. The old token is never reused.
- Concurrent refresh attempts for the same user are serialized via optimistic concurrency (`xmin` on the token row).

### Revoking a connection

A user can disconnect their Canva account at any time. This deletes only that user's tokens and does not affect other users or the org's BYOK credential.

---

## Part 3 — Download PPTX

Any generated format can be downloaded as an editable `.pptx` file — no Canva account required.

### Endpoint

```
GET /api/social/single-analyzer/{analysisId}/formats/{formatKey}/download-pptx?jobId={jobId}
```

Response: `application/vnd.openxmlformats-officedocument.presentationml.presentation`

### What is editable in the PPTX

| Layer type | In PPTX |
|---|---|
| `data-slot` text elements (headline, subtitle, CTA, etc.) | Editable text boxes — fully selectable and editable in PowerPoint or Keynote |
| `data-layer="background"` decorative element | Flattened raster image layer behind text boxes |

Slide dimensions match the source format canvas (portrait for storys, square for carousels — not the PowerPoint default widescreen size).

### Error responses

| HTTP | Meaning |
|---|---|
| 200 | PPTX bytes — download the file |
| 409 Conflict | The format predates B3 markup persistence — markup is not available for offline export |
| 404 Not Found | The format or job was not found |

---

## Part 4 — Export to Canva

### Endpoint

```
POST /api/social/single-analyzer/{analysisId}/formats/{formatKey}/export-to-canva
```

Request body:

```json
{ "jobId": "<uuid>", "orgIntegrationId": "<uuid>" }
```

Success response:

```json
{
  "designId": "DABxxxxxxxx",
  "editUrl": "https://www.canva.com/design/DABxxxxxxxx/edit",
  "expiresAt": "2026-07-06T12:00:00Z"
}
```

The edit URL is valid for approximately 30 days from the time of import.

### Flow summary

1. Platform checks that the user has a valid (or refreshable) Canva token.
2. Reads the persisted designer markup for the requested format.
3. Builds the PPTX offline from the stored markup (no re-render, no AI call).
4. Uploads the PPTX to the Canva Design Import API.
5. Polls the import job until complete (bounded poll: up to 60 s).
6. Returns the `editUrl` to open the imported design in Canva's editor.

### Error responses

| HTTP | Error code | Meaning |
|---|---|---|
| 200 | — | Success — `editUrl` returned |
| 409 Conflict | `canva.not_connected` | User has not connected their Canva account — redirect to the authorize endpoint |
| 409 Conflict | `canva.reconnect_required` | Refresh token was revoked on Canva's side — user must reconnect |
| 409 Conflict | `content-gen.not_exportable` | Markup not persisted for this format (predates B3) |
| 502 Bad Gateway | `canva.import.timeout` | Canva import job did not complete within the poll budget |
| 502 Bad Gateway | `canva.import.api_failure` | Canva API returned a non-success response |

---

## What is editable in Canva after import

The Canva design produced by Design Import inherits the structure of the PPTX:

- Text slots (`data-slot` elements from the original HTML design) become **editable text boxes** in Canva. Users can change the copy, font, size, and color.
- The decorative background (`data-layer="background"`) is imported as a **flattened image layer**. It is visible and movable, but not broken into individual shape elements.

---

## Autofill and Brand Templates — deferred

**Autofill** and **Brand Templates** are Canva Enterprise-only features. They are NOT active in the current release:

- No Autofill API call is made under any circumstances.
- No Brand Template ID is referenced in any Canva API request.
- The codebase contains a feature-detect stub (`ICanvaAutofillAdapter`) that marks this path as explicitly deferred. It is not wired in the DI container.

When Enterprise support is required, the stub provides the extension point without changes to the user-facing flow.

---

## Prerequisites checklist

- [ ] Org Canva connector configured with a valid `client_id` and `client_secret` (Part 1)
- [ ] Connection test passes in **Settings → Integrations → Canva**
- [ ] End user has completed the "Connect to Canva" OAuth flow (Part 2)
- [ ] Format was generated after B3 was deployed (markup persistence required for export and PPTX download)

---

## Migration note for pre-B2 rows

If an organization previously had an `access_token` value stored under the Canva connector, that value is inert. The old single-field schema has been replaced by the Connect API shape. The connector will show `NotConfigured` status until you enter a valid `client_id` and `client_secret`. The legacy `access_token` row is not deleted automatically — it can be safely ignored or removed manually if needed.
