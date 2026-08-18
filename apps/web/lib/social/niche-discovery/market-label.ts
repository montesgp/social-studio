/**
 * Renders a market catalog entry as a human-readable option label (ND-14, ND-15).
 *
 * The backend hands the frontend a `displayNameKey` — an i18n key, not a label — and this app has
 * no i18n dictionary mounted yet, so the selector was rendering the raw key. The label is DERIVED
 * from the entry's own ISO codes via `Intl.DisplayNames` rather than looked up in a hardcoded map,
 * for two reasons:
 *
 *  - ND-14: seeding a market must stay a data-only change. A hardcoded map would make every new
 *    catalog row a frontend edit.
 *  - ND-15: no identifier or literal in this folder may name a country, so the map could not be
 *    written here even if we wanted it.
 *
 * When the dictionary lands, replace this with a real lookup on `displayNameKey` — the key is
 * already carried through the API for exactly that.
 */

import type { MarketOption } from "@/lib/api/niche-discovery";

/** UI copy in this app is English today; the derived names follow it. */
const DISPLAY_LOCALE = "en";

/**
 * `Intl.DisplayNames` echoes the input back when it cannot resolve a code, which is how we detect
 * an unresolvable market without maintaining our own list of valid ones.
 */
function resolve(type: "region" | "language", code: string): string | null {
  try {
    const resolved = new Intl.DisplayNames([DISPLAY_LOCALE], { type }).of(code);
    return resolved && resolved !== code ? resolved : null;
  } catch {
    return null;
  }
}

/**
 * ISO 3166 "unspecified region". Discovery is partitioned by LANGUAGE, not geography, so the catalog
 * seeds `{lang}-ZZ` rather than inventing a representative country per language. Naming a country in
 * the selector would promise a partition the engine does not implement — two rows differing only in
 * region produce byte-identical runs.
 */
const UNSPECIFIED_REGION = "ZZ";

/**
 * Renders the provider-reported country of an evidence row. Because discovery partitions by LANGUAGE,
 * this is the only place the operator learns where a niche actually lives — so an unreported country
 * returns `null` for the UI to omit, and an unrecognised code falls back to itself. Never invent a
 * country: a wrong one here sends the operator to the wrong market.
 */
export function formatCountryLabel(country: string | null | undefined): string | null {
  const code = country?.trim();

  if (!code) {
    return null;
  }

  return resolve("region", code) ?? code;
}

export function formatMarketLabel(market: MarketOption): string {
  const region = market.code.includes("-") ? market.code.split("-")[1] : null;
  const languageName = market.primaryLanguage ? resolve("language", market.primaryLanguage) : null;

  if (region === UNSPECIFIED_REGION) {
    return languageName ?? market.code;
  }

  const regionName = region ? resolve("region", region) : null;

  // An unresolvable region leaves nothing more legible than the code itself — echoing the raw i18n
  // key instead would be strictly worse, which is the bug this module exists to fix.
  if (!regionName) {
    return market.code;
  }

  return languageName ? `${regionName} · ${languageName}` : regionName;
}
