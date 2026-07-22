import { test } from "node:test";
import assert from "node:assert/strict";
import { formatUserDate, formatUserDateTime } from "./date.ts";

function pad(value: number) {
  return String(value).padStart(2, "0");
}

test("formatUserDate: renders dd/MM/yyyy pattern", () => {
  const date = new Date(2026, 6, 20, 12, 0, 0);
  const expected = `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()}`;
  assert.equal(formatUserDate(date.toISOString(), "dd/MM/yyyy"), expected);
});

test("formatUserDate: renders MM/dd/yyyy pattern", () => {
  const date = new Date(2026, 6, 20, 12, 0, 0);
  const expected = `${pad(date.getMonth() + 1)}/${pad(date.getDate())}/${date.getFullYear()}`;
  assert.equal(formatUserDate(date.toISOString(), "MM/dd/yyyy"), expected);
});

test("formatUserDate: renders yyyy-MM-dd pattern", () => {
  const date = new Date(2026, 6, 20, 12, 0, 0);
  const expected = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  assert.equal(formatUserDate(date.toISOString(), "yyyy-MM-dd"), expected);
});

test("formatUserDate: pads single-digit day and month", () => {
  const date = new Date(2026, 0, 5, 0, 0, 0);
  const expected = `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()}`;
  assert.equal(formatUserDate(date.toISOString(), "dd/MM/yyyy"), expected);
});

test("formatUserDate: returns empty string for invalid input", () => {
  assert.equal(formatUserDate("not-a-date", "dd/MM/yyyy"), "");
});

test("formatUserDate: returns empty string for empty input", () => {
  assert.equal(formatUserDate("", "dd/MM/yyyy"), "");
});

test("formatUserDate: returns empty string for an unsupported pattern", () => {
  const date = new Date(2026, 6, 20, 12, 0, 0);
  // @ts-expect-error deliberately passing an unsupported pattern
  assert.equal(formatUserDate(date.toISOString(), "DD.MM.YYYY"), "");
});

test("formatUserDateTime: appends 24h HH:mm after the date pattern", () => {
  const date = new Date(2026, 6, 20, 14, 5, 0);
  const expected = `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
  assert.equal(formatUserDateTime(date.toISOString(), "dd/MM/yyyy"), expected);
});

test("formatUserDateTime: pads single-digit hours and minutes", () => {
  const date = new Date(2026, 6, 20, 4, 5, 0);
  const expected = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
  assert.equal(formatUserDateTime(date.toISOString(), "yyyy-MM-dd"), expected);
});

test("formatUserDateTime: returns empty string for invalid input", () => {
  assert.equal(formatUserDateTime("not-a-date", "dd/MM/yyyy"), "");
});
