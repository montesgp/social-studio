import { test } from "node:test";
import assert from "node:assert/strict";

import { formatCountryLabel, formatMarketLabel } from "./market-label";
import type { MarketOption } from "@/lib/api/niche-discovery";

// The market selector was rendering `displayNameKey` — an i18n key — straight into the option text.
// These tests pin the behavior that fixes it. Market codes here are real ISO codes because the
// label is DERIVED from them, but no assertion spells out a country or language name: ND-15 forbids
// those literals in this folder, and asserting them would also just restate the ICU tables.

function market(code: string, primaryLanguage: string): MarketOption {
  return { code, primaryLanguage, displayNameKey: `social.markets.${code.toLowerCase().replace("-", "_")}` };
}

test("renders a readable label instead of echoing the i18n key", () => {
  const label = formatMarketLabel(market("pt-BR", "pt"));

  assert.ok(!label.includes("social."), `label still looks like an i18n key: ${label}`);
  assert.ok(!label.includes("_"), `label still looks like an i18n key: ${label}`);
  assert.ok(label.length > 0);
});

test("distinct markets render distinct labels so the selector is usable", () => {
  const first = formatMarketLabel(market("pt-BR", "pt"));
  const second = formatMarketLabel(market("en-US", "en"));

  assert.notEqual(first, second);
});

test("same region, different language renders distinct labels", () => {
  const first = formatMarketLabel(market("en-CA", "en"));
  const second = formatMarketLabel(market("fr-CA", "fr"));

  assert.notEqual(first, second);
});

test("falls back to the market code when the region cannot be resolved", () => {
  assert.equal(formatMarketLabel(market("xx-QQ", "xx")), "xx-QQ");
});

// Discovery is partitioned by LANGUAGE, not geography, so the catalog seeds `{lang}-ZZ` — ZZ being
// the ISO 3166 "unspecified region". The selector must show the language alone there: naming a
// country would promise a partition the engine does not implement.
test("renders the language alone for the unspecified region", () => {
  const label = formatMarketLabel(market("id-ZZ", "id"));

  assert.ok(!label.includes("·"), `expected no region segment, got: ${label}`);
  assert.ok(!/unknown/i.test(label), `expected no placeholder region name, got: ${label}`);
  assert.ok(label.length > 0);
});

test("unspecified-region markets stay distinguishable by language", () => {
  assert.notEqual(formatMarketLabel(market("id-ZZ", "id")), formatMarketLabel(market("pl-ZZ", "pl")));
});

test("falls back to the market code when neither region nor language resolve", () => {
  assert.equal(formatMarketLabel(market("xx-ZZ", "xx")), "xx-ZZ");
});

test("falls back to the market code when it carries no region part", () => {
  assert.equal(formatMarketLabel({ code: "mkt-one".split("-")[0], primaryLanguage: "en", displayNameKey: "k" }), "mkt");
});

test("renders the region alone when the language cannot be resolved", () => {
  const withLanguage = formatMarketLabel(market("pt-BR", "pt"));
  const withoutLanguage = formatMarketLabel(market("pt-BR", "xx"));

  assert.ok(withLanguage.includes(withoutLanguage));
  assert.ok(withLanguage.length > withoutLanguage.length);
});

// Evidence rows carry the provider-reported channel country. Discovery partitions by language, so
// this is the only place the operator learns WHERE a niche actually lives.

test("renders a readable country name from the provider-reported code", () => {
  const label = formatCountryLabel("ID");

  assert.ok(label);
  assert.notEqual(label, "ID");
  assert.ok(!/^[A-Z]{2}$/.test(label!), `expected a name, got a bare code: ${label}`);
});

test("returns null when the country is unreported, so the UI can omit it", () => {
  assert.equal(formatCountryLabel(null), null);
  assert.equal(formatCountryLabel(""), null);
  assert.equal(formatCountryLabel("   "), null);
});

test("falls back to the raw code rather than inventing a country name", () => {
  assert.equal(formatCountryLabel("QQ"), "QQ");
});

test("distinct country codes render distinct names", () => {
  assert.notEqual(formatCountryLabel("ID"), formatCountryLabel("PL"));
});
