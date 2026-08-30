import { test } from "node:test";
import assert from "node:assert/strict";
import { buildActorSelectOptions, describeActor, type ActorCatalogEntry } from "./actor-catalog.ts";

const own: ActorCatalogEntry = {
  actorId: "aBcDeFgHiJkLmNoP1",
  name: "my-scraper",
  username: "acme",
  slug: "acme/my-scraper",
  displayName: "My Scraper",
  isOwned: true
};

const store: ActorCatalogEntry = {
  actorId: "zZ9YyXxWwVvUuTtS2",
  name: "tiktok-scraper",
  username: "vendor",
  slug: "vendor/tiktok-scraper",
  displayName: "TikTok Scraper",
  isOwned: false
};

test("buildActorSelectOptions: stores the canonical actor id, never the slug", () => {
  const options = buildActorSelectOptions([own], "");

  // A slug would be percent-encoded into %2F by the resolvers' URL building and 404 at run time.
  assert.deepEqual(
    options.map((option) => option.value),
    ["aBcDeFgHiJkLmNoP1"]
  );
  assert.ok(!options.some((option) => option.value.includes("/")));
});

test("buildActorSelectOptions: labels show the display name and the addressable slug", () => {
  const options = buildActorSelectOptions([store], "");

  assert.equal(options[0].label, "TikTok Scraper — vendor/tiktok-scraper — zZ9YyXxWwVvUuTtS2");
});

test("buildActorSelectOptions: marks the organization's own actors", () => {
  const options = buildActorSelectOptions([own], "");

  assert.equal(options[0].label, "My Scraper — acme/my-scraper — aBcDeFgHiJkLmNoP1 (propio)");
});

test("buildActorSelectOptions: keeps store actors the org only uses", () => {
  const options = buildActorSelectOptions([own, store], "");

  assert.equal(options.length, 2);
  assert.ok(options.some((option) => option.value === "zZ9YyXxWwVvUuTtS2"));
});

test("buildActorSelectOptions: sorts own actors first, then alphabetically", () => {
  const otherOwn: ActorCatalogEntry = { ...own, actorId: "aAaAaAaAaAaAaAaA0", slug: "acme/aaa", displayName: "Aaa" };
  const options = buildActorSelectOptions([store, own, otherOwn], "");

  // Own actors first; ties broken by the stored value, which is the canonical id.
  assert.deepEqual(
    options.map((option) => option.value),
    ["aAaAaAaAaAaAaAaA0", "aBcDeFgHiJkLmNoP1", "zZ9YyXxWwVvUuTtS2"]
  );
});

test("buildActorSelectOptions: a configured actor missing from the catalog stays selectable", () => {
  const options = buildActorSelectOptions([own], "clockworks/free-tiktok-scraper");

  assert.equal(options[0].value, "clockworks/free-tiktok-scraper");
  assert.equal(options[0].label, "clockworks/free-tiktok-scraper (no está en el catálogo)");
});

test("buildActorSelectOptions: a configured actor present in the catalog is not duplicated", () => {
  const options = buildActorSelectOptions([own, store], "zZ9YyXxWwVvUuTtS2");

  assert.equal(options.filter((option) => option.value === "zZ9YyXxWwVvUuTtS2").length, 1);
  assert.equal(options.length, 2);
});

test("buildActorSelectOptions: a legacy slug-shaped configured value is re-keyed to the catalog entry", () => {
  // Rows configured before the id fix may still hold a slug; it must resolve to the same single
  // option rather than appearing twice.
  const options = buildActorSelectOptions([own], "acme/my-scraper");

  assert.equal(options.length, 1);
  assert.equal(options[0].value, "acme/my-scraper");
  assert.equal(options[0].label, "My Scraper — acme/my-scraper — aBcDeFgHiJkLmNoP1 (propio)");
});

test("buildActorSelectOptions: entries without a slug fall back to the canonical actor id", () => {
  const slugless: ActorCatalogEntry = { ...own, slug: "", username: "", name: "" };
  const options = buildActorSelectOptions([slugless], "");

  assert.equal(options[0].value, "aBcDeFgHiJkLmNoP1");
});

test("buildActorSelectOptions: an empty catalog with no configured value yields no options", () => {
  assert.deepEqual(buildActorSelectOptions([], ""), []);
});

test("buildActorSelectOptions: whitespace-only configured values are ignored", () => {
  assert.deepEqual(buildActorSelectOptions([], "   "), []);
});

test("describeActor: resolves a stored actor id to its catalog display name", () => {
  assert.equal(describeActor([own, store], "aBcDeFgHiJkLmNoP1"), "My Scraper");
});

test("describeActor: resolves a legacy slug-shaped stored value too", () => {
  assert.equal(describeActor([own], "acme/my-scraper"), "My Scraper");
});

test("describeActor: falls back to the slug when the entry has no display name", () => {
  const nameless: ActorCatalogEntry = { ...store, displayName: "" };

  assert.equal(describeActor([nameless], "zZ9YyXxWwVvUuTtS2"), "vendor/tiktok-scraper");
});

test("describeActor: returns the id unchanged when the catalog does not know it", () => {
  assert.equal(describeActor([own], "DdSkpbFy5T8FyCsor"), "DdSkpbFy5T8FyCsor");
});

test("describeActor: an empty actor id yields an empty label", () => {
  assert.equal(describeActor([own], "   "), "");
});
