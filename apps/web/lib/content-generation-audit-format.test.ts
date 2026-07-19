import { test } from "node:test";
import assert from "node:assert/strict";
import {
  classifySlideOutcome,
  formatBytes,
  formatDurationSeconds,
  formatElapsedMs,
  getJobStatusTone,
  hadRetry,
  isFailedStatus,
  isInProgressStatus,
  isPartialStatus
} from "./content-generation-audit-format.ts";

test("formatElapsedMs renders sub-second durations in milliseconds", () => {
  assert.equal(formatElapsedMs(0), "0ms");
  assert.equal(formatElapsedMs(999), "999ms");
});

test("formatElapsedMs renders second-plus durations via formatDurationSeconds", () => {
  assert.equal(formatElapsedMs(1500), "2s");
  // Normal image-generation attempt (~390000ms) reads as minutes, not a wall of digits.
  assert.equal(formatElapsedMs(390000), "6m 30s");
  // A ~600000ms (10 minute) step reads as a clear outlier, not an unreadable millisecond count.
  assert.equal(formatElapsedMs(600000), "10m 0s");
});

test("formatElapsedMs guards against invalid input", () => {
  assert.equal(formatElapsedMs(-5), "—");
  assert.equal(formatElapsedMs(Number.NaN), "—");
});

test("formatDurationSeconds renders hours/minutes/seconds tiers", () => {
  assert.equal(formatDurationSeconds(45), "45s");
  assert.equal(formatDurationSeconds(125), "2m 5s");
  assert.equal(formatDurationSeconds(3725), "1h 2m");
});

test("formatDurationSeconds handles missing durations (job never finished)", () => {
  assert.equal(formatDurationSeconds(null), "—");
  assert.equal(formatDurationSeconds(undefined), "—");
});

test("status classification is mutually exclusive per bucket", () => {
  assert.equal(isFailedStatus("Failed"), true);
  assert.equal(isPartialStatus("Failed"), false);
  assert.equal(isPartialStatus("Partial"), true);
  assert.equal(isPartialStatus("PartialSuccess"), true);
  assert.equal(isInProgressStatus("Running"), true);
  assert.equal(isInProgressStatus("Completed"), false);
});

test("getJobStatusTone maps each bucket to a distinct badge tone", () => {
  assert.equal(getJobStatusTone("Failed"), "destructive");
  assert.equal(getJobStatusTone("Partial"), "warning");
  assert.equal(getJobStatusTone("Running"), "info");
  assert.equal(getJobStatusTone("Completed"), "success");
  assert.equal(getJobStatusTone("SomeUnknownStatus"), "muted");
});

test("hadRetry flags a silent retry via retryCount or attemptNumber", () => {
  assert.equal(hadRetry({ attemptNumber: 1, retryCount: 0 }), false);
  assert.equal(hadRetry({ attemptNumber: 1, retryCount: 1 }), true);
  assert.equal(hadRetry({ attemptNumber: 2, retryCount: 0 }), true);
});

test("classifySlideOutcome buckets degrade-to-css outcomes distinctly from success/failure", () => {
  assert.equal(classifySlideOutcome(null), "unknown");
  assert.equal(classifySlideOutcome("Succeeded"), "succeeded");
  assert.equal(classifySlideOutcome("DegradedToCss"), "degraded");
  assert.equal(classifySlideOutcome("Failed"), "failed");
  assert.equal(classifySlideOutcome("SomethingElse"), "unknown");
});

test("formatBytes renders B/KB/MB tiers", () => {
  assert.equal(formatBytes(500), "500 B");
  assert.equal(formatBytes(2048), "2.0 KB");
  assert.equal(formatBytes(5 * 1024 * 1024), "5.0 MB");
  assert.equal(formatBytes(-1), "—");
});
