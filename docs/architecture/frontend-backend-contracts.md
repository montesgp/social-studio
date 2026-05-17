# Frontend ↔ Backend Contracts (Bootstrap)

## Organizations Contract
Public concept: **Organization**  
Backend implementation: tenant aggregate

### Endpoints
- `GET /api/organizations`
- `GET /api/organizations/{id}`
- `POST /api/organizations`
- `PUT /api/organizations/{id}`
- `POST /api/organizations/{id}/deactivate`
- `POST /api/organizations/{id}/activate`

### Response Shape
```json
{
  "id": "guid",
  "name": "string",
  "ownerUserId": "guid",
  "isActive": true,
  "deactivatedAtUtc": null,
  "createdAtUtc": "2026-05-15T00:00:00Z",
  "updatedAtUtc": "2026-05-15T00:00:00Z"
}
```

### List Shape
Uses paged envelope (`items`, `page`, `pageSize`, `totalCount`, `totalPages`, `hasPreviousPage`, `hasNextPage`).

## Error Contract
- API emits RFC 7807/ProblemDetails-compatible responses.
- Frontend API client parses `title/detail/status` and surfaces a normalized message.

## Auth Contract (current)
- Endpoints require Bearer authentication.
- Frontend authenticates via backend endpoints (`POST /api/auth/login`, `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`).
- Frontend stores backend tokens locally and sends access token as `Authorization: Bearer <token>` on protected calls.
- During bootstrap UI still allows manually supplying a bearer token for local verification flows.
