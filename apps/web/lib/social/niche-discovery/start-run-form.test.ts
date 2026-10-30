import { test } from "node:test";
import assert from "node:assert/strict";

import {
  CONTENT_FORMAT_OPTIONS,
  DEPTH_OPTIONS,
  DEFAULT_WINDOW_DAYS,
  MAX_WINDOW_DAYS,
  MIN_WINDOW_DAYS,
  buildStartRunRequest,
  createDefaultStartRunFormState,
  validateStartRunForm,
  type StartRunFormState,
} from "./start-run-form";

// WU-14 — the analysis window and depth are operator-chosen, explicit owner decisions (not
// defaults of convenience), with a 90-day window default (SocialStudio.Domain DiscoveryWindow:
// MinDays=7, MaxDays=365, DefaultDays=90). This module is the only place that encodes those bounds
// for the frontend.

function validState(): StartRunFormState {
  return {
    sourceMarketCode: "mkt-one",
    targetMarketCode: "mkt-two",
    networkKey: "youtube",
    contentFormat: "video",
    windowDays: 90,
    depth: "Standard",
    seedTermsText: "budget travel, street food",
  };
}

test("createDefaultStartRunFormState defaults the window to 90 days and depth to Standard", () => {
  const state = createDefaultStartRunFormState();
  assert.equal(state.windowDays, DEFAULT_WINDOW_DAYS);
  assert.equal(state.windowDays, 90);
  assert.equal(state.depth, "Standard");
});

test("depth and content format options are exactly the operator-selectable values", () => {
  assert.deepEqual(DEPTH_OPTIONS, ["Quick", "Standard", "Deep"]);
  assert.deepEqual(CONTENT_FORMAT_OPTIONS, ["video", "short"]);
});

test("window day bounds match the backend DiscoveryWindow value object (7..365)", () => {
  assert.equal(MIN_WINDOW_DAYS, 7);
  assert.equal(MAX_WINDOW_DAYS, 365);
});

test("a fully filled, in-range form is valid", () => {
  const result = validateStartRunForm(validState());
  assert.equal(result.valid, true);
  assert.deepEqual(result.errors, []);
});

test("missing source or target market is rejected", () => {
  const missingSource = validateStartRunForm({ ...validState(), sourceMarketCode: "" });
  assert.equal(missingSource.valid, false);
  assert.ok(missingSource.errors.includes("sourceMarketCode"));

  const missingTarget = validateStartRunForm({ ...validState(), targetMarketCode: "" });
  assert.equal(missingTarget.valid, false);
  assert.ok(missingTarget.errors.includes("targetMarketCode"));
});

// Discovery is partitioned by LANGUAGE, and the backend never enforced source != target
// (DiscoveryRun.Create parses both codes and does not compare them). Studying a language against
// itself — where is the gap INSIDE this language — is a legitimate question, so the frontend no
// longer invents a rule the domain does not have.
test("the same market on both sides is allowed", () => {
  const result = validateStartRunForm({ ...validState(), targetMarketCode: "mkt-one" });
  assert.equal(result.valid, true);
  assert.deepEqual(result.errors, []);
});

// Without seed terms the backend sources nothing at all: SourceAsync passes the empty list to
// ChannelSearch/VideoSearch, no query is ever issued, and the run finishes Completed with zero
// evidence. An empty-handed run is never what the operator meant.
// Seed terms are OPTIONAL, and their absence is the point: with none, the backend runs a query-less
// trending sweep and lets the niches emerge from the harvested corpus. Requiring them would turn
// discovery into "confirm the niche I already guessed".
test("no seed terms is valid — that is the discovery case", () => {
  const empty = validateStartRunForm({ ...validState(), seedTermsText: "" });
  assert.equal(empty.valid, true);
  assert.deepEqual(empty.errors, []);

  const blank = validateStartRunForm({ ...validState(), seedTermsText: "  ,  ,\n " });
  assert.equal(blank.valid, true);
});

test("blank-only seed term text yields no terms, so the backend sweeps", () => {
  assert.deepEqual(buildStartRunRequest({ ...validState(), seedTermsText: " , \n " }).seedTerms, []);
});

test("seed terms split on commas and newlines, trimmed and de-duplicated", () => {
  const request = buildStartRunRequest({
    ...validState(),
    seedTermsText: " budget travel ,\n street food\n budget travel ,,",
  });

  assert.deepEqual(request.seedTerms, ["budget travel", "street food"]);
});

test("seed terms are preserved verbatim so non-English markets can be searched", () => {
  // The term goes straight to YouTube as `q`; the pipeline never translates it, so an operator
  // targeting a non-English language must be able to type in that language.
  const request = buildStartRunRequest({ ...validState(), seedTermsText: "wisata murah, kuliner kaki lima" });

  assert.deepEqual(request.seedTerms, ["wisata murah", "kuliner kaki lima"]);
});

test("window days outside 7..365 is rejected", () => {
  const tooLow = validateStartRunForm({ ...validState(), windowDays: 6 });
  assert.equal(tooLow.valid, false);
  assert.ok(tooLow.errors.includes("windowDays"));

  const tooHigh = validateStartRunForm({ ...validState(), windowDays: 366 });
  assert.equal(tooHigh.valid, false);
  assert.ok(tooHigh.errors.includes("windowDays"));

  const boundaryLow = validateStartRunForm({ ...validState(), windowDays: 7 });
  assert.equal(boundaryLow.valid, true);
  const boundaryHigh = validateStartRunForm({ ...validState(), windowDays: 365 });
  assert.equal(boundaryHigh.valid, true);
});

test("buildStartRunRequest maps a valid form 1:1 onto the API request shape", () => {
  const request = buildStartRunRequest(validState());
  assert.deepEqual(request, {
    sourceMarketCode: "mkt-one",
    targetMarketCode: "mkt-two",
    networkKey: "youtube",
    contentFormat: "video",
    windowDays: 90,
    depth: "Standard",
    seedTerms: ["budget travel", "street food"],
  });
});
