# Spec Delta: Integrations — Canva Per-User OAuth2 + Design Import Export

<!-- Change: single-analyzer-canva-export (Fase B / B3 — #5 Export to Canva) -->
<!-- Applies to: openspec/specs/integrations/spec.md (additive delta) -->

## Purpose

Specify the observable requirements introduced by B3 in the integrations capability:

1. **Per-user Canva OAuth2 (auth-code + PKCE)** — an org user can connect their personal Canva account
   via the authorization-code flow with PKCE. Tokens are stored per user, not per organization.
2. **Per-user token storage and refresh** — connected-user tokens (short-lived access token +
   single-use rotating refresh token) are stored per `(user, organization-integration)` and refreshed
   transparently. Concurrent refresh attempts for the same user are serialized.
3. **Canva Design Import export** — a user connected to Canva can export a generated format as an
   editable design in their Canva account. The platform uploads the format PPTX via the Canva Design
   Import API and returns a time-limited edit URL.

The B2 org-level BYOK credential schema (client_id / client_secret / scopes) is a prerequisite and
is out of scope for this delta. Canva Autofill and Brand Templates (Enterprise-only) are explicitly
deferred.

---

## PRD Traceability

| Requirement ID | PRD Anchor |
|---|---|
| CANVA-USER-01 | PRD `docs/PRD.md` — review item #5 "Export to Canva" |
| CANVA-USER-02 | PRD `docs/PRD.md` — review item #5 "Export to Canva" |
| CANVA-USER-03 | PRD `docs/PRD.md` — review item #5 "Export to Canva" |
| CANVA-EXPORT-01 | PRD `docs/PRD.md` — review item #5 "Export to Canva" |
| CANVA-EXPORT-02 | PRD `docs/PRD.md` — review item #5 "Export to Canva" |
| CANVA-EXPORT-03 | PRD `docs/PRD.md` — review item #5 "Export to Canva" |

---

## ADDED Requirements

---

### Requirement: CANVA-USER-01 — An Org User Can Connect Their Personal Canva Account via OAuth2 + PKCE

An authenticated org user MUST be able to initiate a Canva OAuth2 authorization-code flow with PKCE
from the single-analyzer UI. The platform MUST redirect the user to Canva's authorization endpoint
(using the org's configured client_id and the scopes from the org integration credential) and handle
the callback to exchange the authorization code for an access token and a refresh token.

After a successful callback, the user's Canva tokens MUST be stored in the platform so subsequent
export calls can use them without re-prompting the user.

Per-user tokens are INDEPENDENT of the org-level BYOK credential (client_id / client_secret). The
org credential is the application identity; the user token is the delegated access on behalf of a
specific person.

#### Scenario: Full authorization code + PKCE flow stores user tokens (CANVA-USER-01-a)

- GIVEN an org user who has not yet connected their Canva account
- AND the organization has a valid Canva BYOK credential (client_id, client_secret) configured
- WHEN the user initiates the "Connect to Canva" action and completes authorization on canva.com
- THEN the platform exchanges the authorization code for an access token and a refresh token
- AND the tokens are stored as per-user credentials keyed to that user and that org integration
- AND the user's Canva connection status is shown as connected

#### Scenario: OAuth callback with an invalid or expired state is rejected (CANVA-USER-01-b)

- GIVEN an OAuth callback that arrives with a state parameter not matching the one issued at authorization-start
- WHEN the platform processes the callback
- THEN the callback is rejected with a handled error
- AND no tokens are stored for any user

#### Scenario: User connects in org A; org B user is unaffected (CANVA-USER-01-c)

- GIVEN two organizations, each with their own Canva BYOK credential
- WHEN User A from Org A completes the Canva OAuth flow
- THEN only User A's token is stored, scoped to Org A's integration
- AND no token is created or modified for Org B

---

### Requirement: CANVA-USER-02 — Per-User Canva Tokens Are Stored and Refreshed Independently

Each org user who connects their Canva account MUST have their own access token and refresh token
stored. The platform MUST transparently refresh the access token using the stored refresh token when
the access token is expired or near expiry.

Canva uses single-use rotating refresh tokens: when a refresh token is exchanged, the response
includes a new refresh token. The platform MUST persist the new refresh token atomically with the
new access token so the old refresh token is never used again.

#### Scenario: Access token is refreshed before expiry (CANVA-USER-02-a)

- GIVEN a connected user whose stored access token has expired
- WHEN an export action is triggered on behalf of that user
- THEN the platform uses the stored refresh token to obtain a fresh access token and a new refresh token
- AND the new token pair is stored, replacing the previous pair
- AND the export proceeds with the fresh access token

#### Scenario: Refresh token is rotated atomically (CANVA-USER-02-b)

- GIVEN a connected user with a valid refresh token stored
- WHEN a token refresh completes
- THEN the old refresh token is replaced by the new one in a single atomic write
- AND the old refresh token is no longer stored

#### Scenario: Concurrent refresh attempts for the same user are serialized (CANVA-USER-02-c)

- GIVEN a connected user who triggers two simultaneous export actions that both detect an expired access token
- WHEN both actions attempt to refresh the token at the same time
- THEN exactly one refresh exchange is performed with Canva
- AND the second action uses the token pair obtained by the first refresh (or waits for it)
- AND no token corruption results from the concurrency

---

### Requirement: CANVA-USER-03 — A User Can View and Revoke Their Canva Connection

An org user MUST be able to see whether their personal Canva account is connected from the platform
UI (specifically the single-analyzer connect affordance). A connected user MUST be able to disconnect
(revoke their stored Canva tokens) without affecting other users in the same organization or the org's
BYOK credential.

#### Scenario: Connected user sees connected status (CANVA-USER-03-a)

- GIVEN a user who has completed the Canva OAuth flow
- WHEN the single-analyzer UI displays the Canva export section
- THEN a "connected" indicator is shown (e.g. account name or "Connected" label)
- AND the "Connect to Canva" prompt is not shown

#### Scenario: Disconnected user sees connect prompt (CANVA-USER-03-b)

- GIVEN a user who has never connected Canva, or who has been disconnected
- WHEN the single-analyzer UI displays the Canva export section
- THEN the "Connect to Canva" prompt is shown
- AND no export-to-Canva action is available until connection is established

#### Scenario: Revoking a user connection does not affect the org credential (CANVA-USER-03-c)

- GIVEN a connected user in an organization whose Canva BYOK credential is active
- WHEN the user revokes their Canva connection
- THEN only that user's tokens are deleted
- AND the org's BYOK credential (client_id, client_secret) is unchanged
- AND other users in the same org who are connected remain connected

---

### Requirement: CANVA-EXPORT-01 — A Connected User Can Export a Generated Format to Canva as an Editable Design

A user who has connected their Canva account MUST be able to export any generated format from the
single-analyzer Package tab to their Canva account. The platform MUST produce the PPTX from the
stored designer markup (per content-generation delta, PPTX-02) and upload it to Canva via the
Design Import API. On success, the platform MUST return an edit URL that opens the imported design
in Canva's editor.

The Canva Design Import API (not Autofill, not Brand Templates) is the ONLY Canva upload mechanism
used. Autofill and Brand Templates are not used and are not expected to be available.

#### Scenario: Happy path — connected user receives an edit URL (CANVA-EXPORT-01-a)

- GIVEN a user who has a valid, non-expired Canva connection
- AND a format result with persisted designer markup
- WHEN the user triggers "Export to Canva" for that format
- THEN the system uploads the format as a PPTX to the Canva Design Import API
- AND the response contains an edit URL (e.g. `https://www.canva.com/design/...`)
- AND the edit URL is valid for approximately 30 days

#### Scenario: Edit URL opens an editable design (CANVA-EXPORT-01-b)

- GIVEN a successful export-to-Canva response with an edit URL
- WHEN the user follows the edit URL
- THEN the Canva editor opens with a design that contains editable content
  (text, layout, background) derived from the uploaded PPTX

#### Scenario: Export uses Design Import only, not Autofill (CANVA-EXPORT-01-c)

- GIVEN any export-to-Canva request, regardless of the org's Canva plan tier
- WHEN the platform uploads to Canva
- THEN only the Canva Design Import API endpoint is called
- AND no request is made to the Autofill API or Brand Templates API

---

### Requirement: CANVA-EXPORT-02 — Export Is Blocked Until the User Is Connected

A user who has not yet connected their personal Canva account MUST NOT be able to trigger an
export-to-Canva action. The platform MUST detect the absence of a valid user token before attempting
any upload and MUST surface a connect prompt instead.

#### Scenario: Unconnected user is prompted to connect before export (CANVA-EXPORT-02-a)

- GIVEN a user who has NOT connected their Canva account
- WHEN the user attempts to trigger "Export to Canva" for a format
- THEN the system does not attempt an upload
- AND the UI displays a "Connect to Canva" prompt (or redirects to the OAuth flow)
- AND no error is shown that references a missing token or internal failure

#### Scenario: Export proceeds without re-prompting once connected (CANVA-EXPORT-02-b)

- GIVEN a user who has just completed the Canva OAuth flow in the same session
- WHEN the user triggers "Export to Canva"
- THEN the export proceeds without showing another connect prompt
- AND the user is not redirected to Canva's authorization page again

---

### Requirement: CANVA-EXPORT-03 — Canva API Failures Surface a Handled Error

When the Canva Design Import API returns an error (rate-limit, invalid token, service unavailable,
or any other non-success response), the platform MUST surface a clear, user-facing error message.
The platform MUST NOT return an unhandled server error or crash. A failed export MUST NOT persist
any partial state in the Canva account.

#### Scenario: Canva API unavailable returns a handled error (CANVA-EXPORT-03-a)

- GIVEN a connected user attempting an export
- WHEN the Canva Design Import API returns a 5xx or connection-timeout response
- THEN the system returns a handled error response to the client
- AND the error message indicates that the export failed and the user can try again
- AND no unhandled exception propagates to the user

#### Scenario: Expired access token is refreshed before retrying export (CANVA-EXPORT-03-b)

- GIVEN a connected user whose access token has expired at the moment of export
- WHEN the platform detects the expired token
- THEN the platform refreshes the token (per CANVA-USER-02) and retries the export with the new token
- AND the user sees a successful export result, not a token-expiry error

#### Scenario: Invalid token that cannot be refreshed surfaces a reconnect prompt (CANVA-EXPORT-03-c)

- GIVEN a connected user whose refresh token is invalid or has been revoked on Canva's side
- WHEN the platform attempts a token refresh and Canva returns an error
- THEN the stored tokens are cleared for that user
- AND the user is shown a "Please reconnect your Canva account" message
- AND the "Connect to Canva" prompt is displayed

---

### Requirement: CANVA-EXPORT-04 — Autofill and Brand Templates Are Feature-Detected as Deferred

The platform MUST implement the Canva export path in a way that allows a future Enterprise tier to
activate Autofill or Brand Templates without changing the user-facing flow. At the time this change
is applied, Autofill and Brand Templates MUST NOT be invoked under any circumstances. A feature-
detection placeholder (e.g. a configuration flag, a port method, or a null implementation stub) MUST
be present to mark these paths as explicitly deferred, not simply absent.

#### Scenario: Autofill path is not reached during any current export (CANVA-EXPORT-04-a)

- GIVEN any export-to-Canva request on any plan tier at time of this change
- WHEN the export completes (success or failure)
- THEN no call was made to the Canva Autofill API
- AND no Brand Template ID was referenced in any Canva API request

#### Scenario: Feature-detect extension point exists for Enterprise path (CANVA-EXPORT-04-b)

- GIVEN the compiled integrations code at the time this change is applied
- WHEN the Canva export path is inspected
- THEN a clearly-named extension point, flag, or stub marking "Autofill — deferred, Enterprise only"
  exists in the codebase
- AND it is not reachable from any active code path

---

## MODIFIED Requirements

None. Existing integration requirements are unchanged; this delta is purely additive.
The B2 delta (CANVA-01 through CANVA-04, org-level BYOK credential schema) is a prerequisite
and is not modified here.

---

## REMOVED Requirements

None.

---

## Implementation Reconciliation (WU-8)

Completed in change `single-analyzer-canva-export` (Fase B / B3). All requirements in this delta are
implemented and verified. Work units responsible:

| Requirement | Work Unit | Status |
|---|---|---|
| CANVA-USER-01 (OAuth PKCE connect flow) | WU-5 | Implemented. `GET /api/integrations/canva/authorize` + `GET /api/integrations/canva/callback`. PKCE verifier generated per request; state round-tripped via cache (TTL 10 min); mismatch rejected. |
| CANVA-USER-02 (token refresh rotation + serialization) | WU-4 + WU-5 | Implemented. `CanvaOAuthService.EnsureFreshAsync` refreshes on expiry, persists new pair atomically. Concurrent refresh serialized via `xmin` optimistic concurrency on `platform.canva_user_tokens`. |
| CANVA-USER-03 (view + revoke connection) | WU-5 + WU-7 | Implemented. UI shows connected/disconnected state. `RevokeAsync` deletes only the requesting user's token row. |
| CANVA-EXPORT-01 (Design Import → edit URL) | WU-6 + WU-7 | Implemented. `CanvaDesignImportClient` uploads PPTX, polls job (bounded: 12 × 5 s = 60 s), returns `CanvaImportResult { DesignId, EditUrl, ExpiresAt }`. Endpoint: `POST /api/social/single-analyzer/{analysisId}/formats/{formatKey}/export-to-canva`. Edit URL valid ~30 days. |
| CANVA-EXPORT-02 (blocked until connected) | WU-7 | Implemented. Handler detects absent token → `canva.not_connected` (409) → UI redirects to authorize endpoint. No upload attempted. |
| CANVA-EXPORT-03 (handled API failures) | WU-6 + WU-7 | Implemented. Timeout → 502 `canva.import.timeout`; API failure → 502 `canva.import.api_failure`; revoked token → tokens cleared + 409 `canva.reconnect_required`. No unhandled exception reaches the client. |
| CANVA-EXPORT-04 (Autofill/Brand Templates deferred) | WU-7 | Implemented. `ICanvaAutofillAdapter` stub exists in Application (not wired in DI). `ICanvaDesignImportClient` tests assert no request to `/autofill` or `/brand-template` endpoints. |

### CanvaImportResult shape

The shipped `CanvaImportResult` record carries three fields: `DesignId` (string), `EditUrl` (string),
and `ExpiresAt` (DateTimeOffset). The `DesignId` field was added in WU-7 to support future
Autofill/Brand Template integration. The spec scenarios reference `editUrl` only — both fields are
present in the response.

### Status code note (CANVA-EXPORT-02 / not_connected)

Scenarios CANVA-EXPORT-02-a and related scenarios describe the system "not attempting an upload" and
showing a connect prompt. The implementation returns **HTTP 409 Conflict** with error code
`canva.not_connected`. HTTP 422 is not in the project's error-type infrastructure — `ErrorType.Conflict`
maps to 409 via `ResultExtensions.StatusCodeFor`. This is consistent with the PPTX-02-d treatment
and the observable behavior matches the intent of all affected scenarios.

### EnsureFreshAsync signature (internal, no spec impact)

`ICanvaOAuthService.EnsureFreshAsync` receives `(userId, organizationIntegrationId, organizationId, ct)`.
The `organizationId` parameter was added in WU-7 so the service can resolve the org BYOK credential
(client_id / client_secret) when refreshing. This is an internal concern; no spec scenario references
it directly.
