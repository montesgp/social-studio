# Niche / Sector Catalog

Operator guide for the platform-managed catalog of sectors and niches, and for attaching niches to
a client.

## What the catalog is for

A niche is a sector a client operates in — "Gastronomy", "Medical & Health", "Real Estate". Every
niche carries a **curated list of search hashtags**, and those hashtags are the discovery key: they
are what content discovery searches on when it goes looking for candidate content for clients in
that niche.

The hashtags are **stored, not derived**. Deriving a hashtag from a display name produces tags that
nobody actually uses on the target network ("Medical & Health" becomes a hashtag with no posts
behind it), and every client on that niche inherits the same dead key. Curating them in one place
means a bad hashtag is fixed **once, for everyone**.

Before the catalog, each client carried a free-text "Sector / Niche" note. Free text cannot be
searched on, cannot be shared between clients, and cannot be corrected centrally. The catalog
replaces it for everything operational.

## Who manages it

**Platform admins only.** Creating, renaming, re-iconing, curating hashtags, deactivating and
reactivating a niche are all PlatformAdmin operations.

Everyone else can **read** the catalog — that is how the client form offers niches to pick from —
but non-admins never see deactivated entries, even if they ask for them.

## Where to find it

Admin navigation → **System** → **Niches** (`/admin/niches`), next to Social Networks. It works the
same way as the Social Networks catalog:

| Column | Notes |
| --- | --- |
| Icon | Optional. A niche with no icon renders the generic fallback icon. |
| Code | Immutable after creation — client associations point at it. Lowercase letters, digits and hyphens, 2–64 chars. |
| Display Name | What operators read. Up to 120 characters. |
| Curated hashtags | The discovery key. Up to 20, lowercase alphanumeric or underscore, no `#`, no spaces. |
| Created | When the entry was added, in your configured date format. |
| Status | Active or Inactive. |

**Listing behaviour worth knowing:** the list is bounded on the server. With no search term it
returns the **most recently created entries first**, 20 per page. With a search term it filters by
display name or code and orders alphabetically. As the catalog grows, older entries are found by
searching, not by paging to the end.

**Deactivating** a niche is a soft delete. Existing client associations are not erased, but a
deactivated niche can no longer be assigned to a client. Reactivate to make it selectable again.

### Editing hashtags

Open **Edit** on a niche. In the curated hashtags field, type a tag and press <kbd>Enter</kbd> or
comma to add it; paste a comma- or space-separated list to add several at once. A leading `#` is
stripped and the tag is lowercased for you. Backspace on an empty box removes the last tag.

The starter catalog ships with:

| Code | Display name | Hashtags |
| --- | --- | --- |
| `software-technology` | Software & Technology | `software`, `technology` |
| `medical-health` | Medical & Health | `medical`, `health` |
| `gastronomy` | Gastronomy | `gastronomy`, `food` |
| `fitness-wellness` | Fitness & Wellness | `fitness`, `wellness` |
| `real-estate` | Real Estate | `realestate`, `property` |

Seeding only ever adds missing entries. Your renames, hashtag curation and deactivations survive
restarts.

## How a client gets niches

Open a client (Clients → edit) → **Basic Info** → **Niches**. The field is a searchable multi-select:

1. Focus the field. Before you type anything it shows the newest entries in the catalog.
2. Type to search the catalog by name or code.
3. Pick an entry — it becomes a removable pill. A client may carry **several** niches.
4. Save the client. The selection is persisted as a whole (it replaces whatever was there before).

The field is keyboard-usable end to end: arrow keys move through the results, <kbd>Enter</kbd>
picks, <kbd>Esc</kbd> closes the list, and <kbd>Backspace</kbd> on an empty box removes the last
pill.

### Inline invention is not allowed

If a search returns nothing, the field says so and stops there. **There is no "create this one"
button, by design.** A niche typed into a client form would be invisible to every other client, its
hashtags would be nobody's responsibility, and the catalog would drift back into free text.

When a client needs a niche that does not exist yet: ask a platform admin to add it to the catalog
(with its curated hashtags), then pick it on the client.

## What the hashtags actually drive

The curated hashtags are the input to **niche search** in content discovery — the tags the network is
asked for, and the key the candidate bank is stored under. Three cases, and nothing else:

| The client is… | What niche search does |
| --- | --- |
| Assigned to one or more niches with curated hashtags | Searches the **union** of those hashtags, de-duplicated, in assignment order. A client that is *Software & Technology* **and** *Medical & Health* searches `software`, `technology`, `medical`, `health` in **one** call |
| Assigned to **no** niche | Falls back to deriving hashtags from the legacy free-text sector, exactly as before the catalog. Nothing about that client changes |
| Assigned to niches that **all** have an empty curated list | Runs **nothing** and reports the niche stage as **Unconfigured**. It deliberately does *not* fall back to the free text |

That last row is the one worth remembering: an empty curated list is an **unfinished catalog entry**,
not an empty search. Falling back would quietly reinstate the guessed hashtags the catalog exists to
replace, so the platform sends you here to finish the entry instead.

**The union is capped at 5 hashtags.** Each extra hashtag is extra provider work and, on usage-billed
actors, extra money, so a client assigned to many niches cannot fan one run out into an expensive
scrape. When the cap bites, the first 5 in assignment order are searched and the log names both the
kept and the dropped tags. Remove niches from the client to change which ones travel.

**Two clients on the same niche share one paid scrape.** Because the search hashtags come from the
catalog rather than from each client's wording, clients of the same organization on the same niche
produce the same bank key by construction: the second one is served from the reserve the first one
paid for. Editing a niche's hashtags changes that key — it is a new search, and the first run under
the new tags will be billed.

## The legacy free-text value

Clients created before the catalog may still carry the old free-text `sectorOrNiche` note. It is
**left completely untouched**:

- It is still stored and still returned by the API.
- The client form shows it **read-only**, labelled as legacy context, below the niche selection.
- It is **not** auto-migrated into catalog entries, and nothing deletes it.
- It no longer drives discovery — the curated hashtags on the selected niches do.

Treat it as a historical note: useful for deciding *which* catalog niches a client should get, not
as a live setting. Once a client has proper niches selected, the legacy text is simply context.
