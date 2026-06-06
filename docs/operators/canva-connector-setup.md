# Canva Connector — Operator Setup Guide

This guide tells an operator exactly what to configure so the Canva connector reaches `Connected` status in **Settings → Integrations**. The connector uses the **Canva Connect API** BYOK (Bring Your Own Key) flow — you supply your own Connect app credentials.

> See also: the integrations spec delta at [`openspec/specs/integrations/spec.md`](../../openspec/specs/integrations/spec.md#canva-connect-api-byok-configuration-canva-delta-01).

---

## Quick path

1. Go to the [Canva Developer Portal](https://www.canva.com/developers/) and sign in.
2. Navigate to **Your apps** and create a new app (or select an existing one).
3. Under **Authentication**, copy the **Client ID** and **Client secret**.
4. Go to **Settings → Integrations → Canva** in Incoders Hive.
5. Select connection mode **Bring your own key**, paste **Client ID** and **Client secret**.
6. Optionally adjust the **Scopes** field (see below). Leave blank to use the default set.
7. Click **Save** and then **Test connection**.

---

## Where to get the credentials

| Field | Where to find it |
|---|---|
| Client ID | Canva Developer Portal → Your apps → `<your app>` → Authentication tab |
| Client secret | Same page — click **Reveal** to show the secret |

Both values are generated when you create a Connect app. If you have not created one yet, follow the [Canva Connect API quickstart](https://www.canva.com/developers/docs/connect/getting-started/).

---

## Default scope string

The **Scopes** field ships the following default value:

```
asset:read asset:write brandtemplate:meta:read design:meta:read design:content:read design:content:write profile:read
```

These are the minimum scopes needed for the features planned in B2 and B3. Leave the field blank or unchanged to use this default. You may extend the list — additional scopes are space-separated.

| Scope | Purpose |
|---|---|
| `asset:read` | Read assets from the org's library |
| `asset:write` | Upload or update assets |
| `brandtemplate:meta:read` | Read brand template metadata |
| `design:meta:read` | Read design metadata |
| `design:content:read` | Read design content |
| `design:content:write` | Create or update design content |
| `profile:read` | Read the authenticated user's profile |

---

## B2 vs B3 scope

**B2 (this change)** stores the app-level `client_id` and `client_secret` only. The connector will reach `Connected` status once both are entered and the connection test passes.

**B3 (deferred)** will add the OAuth authorization-code / per-user token flow (PKCE, code exchange, refresh token rotation). Until B3 is shipped, no Canva API calls are made at runtime — the credentials are stored and ready for when the export feature is implemented.

---

## Migration note for existing rows

If an organization previously had an `access_token` value stored under the Canva connector, that value is now inert. The old single-field schema has been replaced by the Connect API shape. The connector will show `NotConfigured` status until you enter a valid `client_id` and `client_secret`. The legacy `access_token` row is not deleted automatically — it can be safely ignored or removed manually if needed.
