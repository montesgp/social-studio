"use client";

import { KeyboardEvent, useEffect, useState } from "react";
import { Plus, Trash2, X } from "lucide-react";
import { getContentPlan, upsertContentPlan } from "@/lib/api/content-plans";
import type { ContentPlanCalendarSlot, Provenance, UpsertContentPlanPayload } from "@/lib/api/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";

interface ClientContentPlanSectionProps {
  clientId: string;
  token: string;
}

const MONTHS = [
  { value: 1, label: "January" },
  { value: 2, label: "February" },
  { value: 3, label: "March" },
  { value: 4, label: "April" },
  { value: 5, label: "May" },
  { value: 6, label: "June" },
  { value: 7, label: "July" },
  { value: 8, label: "August" },
  { value: 9, label: "September" },
  { value: 10, label: "October" },
  { value: 11, label: "November" },
  { value: 12, label: "December" },
];

const MAX_KPIS = 20;

function getYearOptions(): number[] {
  const current = new Date().getFullYear();
  return [current - 2, current - 1, current, current + 1, current + 2];
}

function KpiTagInput({
  tags,
  onChange,
}: {
  tags: string[];
  onChange: (tags: string[]) => void;
}) {
  const [input, setInput] = useState("");
  const atLimit = tags.length >= MAX_KPIS;

  function addTag(raw: string) {
    const value = raw.trim().replace(/,+$/, "").trim();
    if (!value || tags.includes(value) || atLimit) return;
    onChange([...tags, value]);
    setInput("");
  }

  function handleKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Enter" || e.key === ",") {
      e.preventDefault();
      addTag(input);
    } else if (e.key === "Backspace" && input === "" && tags.length > 0) {
      onChange(tags.slice(0, -1));
    }
  }

  function handleBlur() {
    if (input.trim()) addTag(input);
  }

  return (
    <div className="flex flex-wrap gap-1.5 rounded-md border border-input bg-background px-3 py-2 focus-within:ring-2 focus-within:ring-ring">
      {tags.map((tag) => (
        <span
          key={tag}
          className="inline-flex items-center gap-1 rounded bg-muted px-2 py-0.5 text-xs font-medium"
        >
          {tag}
          <button
            type="button"
            onClick={() => onChange(tags.filter((t) => t !== tag))}
            className="text-muted-foreground hover:text-foreground"
            aria-label={`Remove ${tag}`}
          >
            <X className="h-3 w-3" />
          </button>
        </span>
      ))}
      <input
        type="text"
        value={input}
        onChange={(e) => setInput(e.target.value)}
        onKeyDown={handleKeyDown}
        onBlur={handleBlur}
        disabled={atLimit}
        placeholder={atLimit ? `Max ${MAX_KPIS} reached` : "Add KPI and press Enter"}
        className="min-w-20 flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
      />
    </div>
  );
}

function ProvenanceBadge({ provenance }: { provenance: Provenance }) {
  if (!provenance || provenance.status === "Empty") return null;
  return (
    <span className="inline-flex items-center gap-1 rounded border border-border bg-muted/50 px-1.5 py-0.5 text-[10px] text-muted-foreground">
      <span>{provenance.source}</span>
      <span>·</span>
      <span>{provenance.status}</span>
    </span>
  );
}

interface CalendarSlotRow {
  localId: string;
  day: string;
  contentFormat: string;
}

let localIdCounter = 0;
function nextLocalId() {
  return `local-${++localIdCounter}`;
}

function slotFromApi(slot: ContentPlanCalendarSlot): CalendarSlotRow {
  return {
    localId: nextLocalId(),
    day: String(slot.day),
    contentFormat: slot.contentFormat,
  };
}

export function ClientContentPlanSection({ clientId, token }: ClientContentPlanSectionProps) {
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);

  const [objectiveText, setObjectiveText] = useState("");
  const [kpis, setKpis] = useState<string[]>([]);
  const [contentStrategyText, setContentStrategyText] = useState("");
  const [calendarSlots, setCalendarSlots] = useState<CalendarSlotRow[]>([]);

  const [objectiveProvenance, setObjectiveProvenance] = useState<Provenance | null>(null);
  const [strategyProvenance, setStrategyProvenance] = useState<Provenance | null>(null);
  const [calendarProvenance, setCalendarProvenance] = useState<Provenance | null>(null);

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  useEffect(() => {
    if (!clientId || !token) return;
    setLoading(true);
    setError(null);
    setSuccess(false);
    getContentPlan(clientId, year, month, token)
      .then((plan) => {
        if (plan) {
          setObjectiveText(plan.objectiveText ?? "");
          setKpis(plan.kpis ?? []);
          setContentStrategyText(plan.contentStrategyText ?? "");
          setCalendarSlots(plan.slots?.map(slotFromApi) ?? []);
          setObjectiveProvenance(plan.objectiveProvenance ?? null);
          setStrategyProvenance(plan.contentStrategyProvenance ?? null);
          setCalendarProvenance(plan.calendarProvenance ?? null);
        } else {
          setObjectiveText("");
          setKpis([]);
          setContentStrategyText("");
          setCalendarSlots([]);
          setObjectiveProvenance(null);
          setStrategyProvenance(null);
          setCalendarProvenance(null);
        }
      })
      .catch(() => {
        setError("Failed to load content plan.");
      })
      .finally(() => setLoading(false));
  }, [clientId, token, year, month]);

  function addSlot() {
    setCalendarSlots((prev) => [...prev, { localId: nextLocalId(), day: "", contentFormat: "" }]);
  }

  function removeSlot(localId: string) {
    setCalendarSlots((prev) => prev.filter((s) => s.localId !== localId));
  }

  function updateSlot(localId: string, field: "day" | "contentFormat", value: string) {
    setCalendarSlots((prev) =>
      prev.map((s) => (s.localId === localId ? { ...s, [field]: value } : s)),
    );
  }

  async function handleSave() {
    if (!objectiveText.trim()) {
      setError("Objective is required.");
      return;
    }

    const invalidSlot = calendarSlots.find((s) => {
      const d = parseInt(s.day, 10);
      return !s.contentFormat.trim() || isNaN(d) || d < 1 || d > 31;
    });
    if (invalidSlot) {
      setError("Each calendar slot must have a day (1–31) and a content format.");
      return;
    }

    setSaving(true);
    setError(null);
    setSuccess(false);
    try {
      const payload: UpsertContentPlanPayload = {
        objectives: objectiveText.trim(),
        kpis: kpis.length > 0 ? kpis : undefined,
        contentStrategyText: contentStrategyText.trim() || null,
        slots: calendarSlots.map((s) => ({
          day: parseInt(s.day, 10),
          contentFormat: s.contentFormat.trim(),
        })),
      };
      const saved = await upsertContentPlan(clientId, year, month, payload, token);
      setCalendarSlots(saved.slots?.map(slotFromApi) ?? []);
      setObjectiveProvenance(saved.objectiveProvenance ?? null);
      setStrategyProvenance(saved.contentStrategyProvenance ?? null);
      setCalendarProvenance(saved.calendarProvenance ?? null);
      setSuccess(true);
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to save content plan.";
      setError(message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-4">
      {/* Period selectors */}
      <div className="grid gap-3 sm:grid-cols-[minmax(12rem,1fr)_minmax(8rem,0.7fr)]">
        <div className="space-y-2">
          <label htmlFor="cp-month" className="block text-sm font-medium text-foreground">
            Month
          </label>
          <Select
            id="cp-month"
            value={month}
            onChange={(e) => setMonth(Number(e.target.value))}
            className="w-full"
          >
            {MONTHS.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </Select>
        </div>
        <div className="space-y-2">
          <label htmlFor="cp-year" className="block text-sm font-medium text-foreground">
            Year
          </label>
          <Select
            id="cp-year"
            value={year}
            onChange={(e) => setYear(Number(e.target.value))}
            className="w-full"
          >
            {getYearOptions().map((y) => (
              <option key={y} value={y}>
                {y}
              </option>
            ))}
          </Select>
        </div>
      </div>

      {loading ? (
        <p className="text-xs text-muted-foreground">Loading…</p>
      ) : (
        <>
          {/* Objective */}
          <div className="space-y-1.5">
            <div className="flex items-center gap-2">
              <label htmlFor="cp-objective" className="text-sm font-medium">
                Objective <span className="text-destructive">*</span>{" "}
                <span className="text-muted-foreground text-xs">(max 3000)</span>
              </label>
              {objectiveProvenance ? <ProvenanceBadge provenance={objectiveProvenance} /> : null}
            </div>
            <textarea
              id="cp-objective"
              value={objectiveText}
              onChange={(e) => setObjectiveText(e.target.value)}
              maxLength={3000}
              rows={5}
              placeholder="Describe the objectives for this period…"
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            />
          </div>

          {/* KPIs */}
          <div className="space-y-1.5">
            <label className="text-sm font-medium">
              KPIs{" "}
              <span className="text-muted-foreground text-xs">(max {MAX_KPIS}, Enter or comma to add)</span>
            </label>
            <KpiTagInput tags={kpis} onChange={setKpis} />
          </div>

          {/* Content Strategy */}
          <div className="space-y-1.5">
            <div className="flex items-center gap-2">
              <label htmlFor="cp-strategy" className="text-sm font-medium">
                Content Strategy{" "}
                <span className="text-muted-foreground text-xs">(optional — AI-fillable or manual)</span>
              </label>
              {strategyProvenance ? <ProvenanceBadge provenance={strategyProvenance} /> : null}
            </div>
            <textarea
              id="cp-strategy"
              value={contentStrategyText}
              onChange={(e) => setContentStrategyText(e.target.value)}
              rows={4}
              placeholder="Describe the content strategy for this period…"
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            />
          </div>

          {/* Content Calendar */}
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="text-sm font-medium">Content Calendar</span>
                {calendarProvenance ? <ProvenanceBadge provenance={calendarProvenance} /> : null}
              </div>
              <Button type="button" variant="ghost" size="sm" onClick={addSlot}>
                <Plus className="mr-1 h-3.5 w-3.5" />
                Add slot
              </Button>
            </div>

            {calendarSlots.length === 0 ? (
              <p className="text-xs text-muted-foreground">No calendar slots yet. Add one above.</p>
            ) : (
              <div className="space-y-2">
                {calendarSlots.map((slot) => (
                  <div key={slot.localId} className="flex items-center gap-2">
                    <div className="w-20 shrink-0 space-y-0.5">
                      <label className="sr-only">Day</label>
                      <Input
                        type="number"
                        min={1}
                        max={31}
                        value={slot.day}
                        onChange={(e) => updateSlot(slot.localId, "day", e.target.value)}
                        placeholder="Day"
                        aria-label="Day (1–31)"
                      />
                    </div>
                    <div className="flex-1 space-y-0.5">
                      <label className="sr-only">Content format</label>
                      <Input
                        value={slot.contentFormat}
                        onChange={(e) => updateSlot(slot.localId, "contentFormat", e.target.value)}
                        placeholder="Content format (e.g. Reel, Carousel)"
                        aria-label="Content format"
                      />
                    </div>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => removeSlot(slot.localId)}
                      aria-label="Remove slot"
                      className="px-2"
                    >
                      <Trash2 className="h-4 w-4 text-muted-foreground" />
                    </Button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
          {success ? <p className="text-sm text-emerald-600">Content plan saved.</p> : null}

          <div className="flex justify-end">
            <Button type="button" onClick={() => void handleSave()} disabled={saving}>
              {saving ? "Saving…" : "Save Content Plan"}
            </Button>
          </div>
        </>
      )}
    </div>
  );
}
