# Apify Provider Instructions

## Scope
External content data extraction provider.

## Rules
- Respect platform policies and legal constraints.
- Normalize external data into internal content research models.

## Actor catalog vs. actor routing
Two distinct concerns that must not be merged:

- **Catalog (discovery).** `IActorCatalogClient` lists the actors the connected Apify account can
  run. `SyncOrganizationActorsCommand` persists them per organization as
  `platform.organization_actors`, and the connector UI uses them to populate the actor picker.
  Endpoints: `GET /api/integrations/organization/{providerKey}/actors` and
  `POST /api/integrations/organization/{providerKey}/actors/sync`.
- **Routing (execution).** The `actor_mappings_json` credential remains the single routing store:
  it decides which network and variant an actor serves and whether it is primary or fallback. The
  catalog sync never reads or writes it, and the resolvers keep resolving from it unchanged.

## Actor listing
- Use `GET /v2/acts`. It returns every actor the account **created or used**.
- Never send the `my` query parameter. It restricts the result to owned actors and would drop the
  public-store actors the organization already routes to.
- Page with `limit`/`offset`; the API caps a page at 1000 records, so keep paging until a short page
  is returned.
- Ownership is derived by comparing each actor's `username` against the authenticated account's own
  username. If that lookup fails, the sync still succeeds with nothing marked as owned.

## Actor identifiers
- No actor id may ever be a literal in shipped source. Actor resolution is exclusively
  configuration-driven, and `NoActorLiteralGuardTests` enforces this on every Apify call site,
  including the catalog client.
- An actor that is absent from the catalog must stay routable: the picker always keeps manual entry
  available, and the configured value is preserved as a selectable option.
