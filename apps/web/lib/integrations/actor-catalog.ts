/**
 * Turns the synced Apify actor catalog into the option list for the connector's actor picker.
 *
 * The catalog only FEEDS the picker: routing still lives in the connector's actor-mapping rows, so
 * an actor that is not in the catalog must stay routable. That is why a configured value is always
 * preserved as a selectable option.
 *
 * Framework-free on purpose — the component layer stays thin and this logic is unit tested.
 */

export interface ActorCatalogEntry {
  actorId: string;
  name: string;
  username: string;
  slug: string;
  displayName: string;
  isOwned: boolean;
}

export interface ActorSelectOption {
  value: string;
  label: string;
}

/**
 * The value stored in the routing map is ALWAYS the canonical actor id.
 *
 * It must not be the "username/name" slug. Every resolver builds its call as
 * `v2/acts/{Uri.EscapeDataString(actorId)}/run-sync-get-dataset-items`, so a slash inside the value
 * is percent-encoded to %2F and the request 404s at run time — silently, only once the actor is
 * actually executed. Apify's addressable alternative to the id uses a TILDE (`username~actor-name`),
 * not a slash, so the slug as displayed is not a valid substitute either. The slug stays in the
 * label, where it helps a human recognise the actor and costs nothing.
 */
function optionValue(entry: ActorCatalogEntry) {
  return entry.actorId.trim() || entry.slug.trim();
}

function optionLabel(entry: ActorCatalogEntry) {
  const value = optionValue(entry);
  const slug = entry.slug.trim();
  const display = entry.displayName.trim() || slug || value;
  const parts = [display, slug && slug !== display ? slug : "", value]
    .filter((part) => part.length > 0)
    .filter((part, index, all) => all.indexOf(part) === index);
  const base = parts.join(" — ");

  return entry.isOwned ? `${base} (propio)` : base;
}

/**
 * @param actors  The synced catalog for the organization.
 * @param currentValue  The actor id already configured on the row, if any.
 */
/**
 * Human label for an actor id that is already stored on a route.
 *
 * Routes persist the canonical id, which is unreadable on its own ("Apify · DdSkpbFy5T8FyCsor").
 * When the synced catalog knows the actor, show its name; otherwise return the id unchanged, so an
 * actor that was never synced still renders something truthful instead of an empty slot.
 */
export function describeActor(actors: ActorCatalogEntry[], actorId: string): string {
  const trimmed = actorId.trim();
  if (!trimmed) {
    return "";
  }

  const match = actors.find(
    (entry) => entry.actorId.trim() === trimmed || entry.slug.trim() === trimmed
  );

  if (!match) {
    return trimmed;
  }

  return match.displayName.trim() || match.slug.trim() || trimmed;
}

export function buildActorSelectOptions(
  actors: ActorCatalogEntry[],
  currentValue: string
): ActorSelectOption[] {
  const options = [...actors]
    .sort((a, b) => {
      if (a.isOwned !== b.isOwned) {
        return a.isOwned ? -1 : 1;
      }

      return optionValue(a).localeCompare(optionValue(b));
    })
    .map((entry) => ({ value: optionValue(entry), label: optionLabel(entry) }));

  const trimmed = currentValue.trim();
  if (!trimmed) {
    return options;
  }

  // The operator may have configured the canonical actor id while the catalog offers the slug (or
  // the other way round). Re-key that option to what is stored rather than showing the actor twice.
  const matched = actors.find(
    (entry) => entry.actorId.trim() === trimmed || entry.slug.trim() === trimmed
  );

  if (matched) {
    const matchedValue = optionValue(matched);
    return options.map((option) =>
      option.value === matchedValue ? { ...option, value: trimmed } : option
    );
  }

  return [{ value: trimmed, label: `${trimmed} (no está en el catálogo)` }, ...options];
}
