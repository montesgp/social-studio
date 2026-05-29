"use client";

import { KeyboardEvent, useEffect, useState } from "react";
import { X } from "lucide-react";
import { getClientMonthlyObjective, upsertClientMonthlyObjective } from "@/lib/api/client-monthly-objectives";
import type { UpsertClientMonthlyObjectivePayload } from "@/lib/api/types";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";

interface ClientMonthlyObjectivesSectionProps {
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

export function ClientMonthlyObjectivesSection({ clientId, token }: ClientMonthlyObjectivesSectionProps) {
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);

  const [objectives, setObjectives] = useState("");
  const [kpis, setKpis] = useState<string[]>([]);

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  useEffect(() => {
    if (!clientId || !token) return;
    setLoading(true);
    setError(null);
    setSuccess(false);
    getClientMonthlyObjective(clientId, year, month, token)
      .then((obj) => {
        if (obj) {
          setObjectives(obj.objectives ?? "");
          setKpis(obj.kpis ?? []);
        } else {
          setObjectives("");
          setKpis([]);
        }
      })
      .catch(() => {
        setError("Failed to load objective.");
      })
      .finally(() => setLoading(false));
  }, [clientId, token, year, month]);

  async function handleSave() {
    if (!objectives.trim()) {
      setError("Objectives text is required.");
      return;
    }
    setSaving(true);
    setError(null);
    setSuccess(false);
    try {
      const payload: UpsertClientMonthlyObjectivePayload = {
        objectives: objectives.trim(),
        kpis: kpis.length > 0 ? kpis : undefined,
      };
      await upsertClientMonthlyObjective(clientId, year, month, payload, token);
      setSuccess(true);
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to save objective.";
      setError(message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-[minmax(12rem,1fr)_minmax(8rem,0.7fr)]">
        <div className="space-y-2">
          <label htmlFor="obj-month" className="block text-sm font-medium text-foreground">
            Month
          </label>
          <Select
            id="obj-month"
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
          <label htmlFor="obj-year" className="block text-sm font-medium text-foreground">
            Year
          </label>
          <Select
            id="obj-year"
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
          <div className="space-y-1.5">
            <label htmlFor="objectives-text" className="text-sm font-medium">
              Objectives <span className="text-destructive">*</span>{" "}
              <span className="text-muted-foreground text-xs">(max 3000)</span>
            </label>
            <textarea
              id="objectives-text"
              value={objectives}
              onChange={(e) => setObjectives(e.target.value)}
              maxLength={3000}
              rows={5}
              placeholder="Describe the objectives for this month…"
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            />
          </div>

          <div className="space-y-1.5">
            <label className="text-sm font-medium">
              KPIs{" "}
              <span className="text-muted-foreground text-xs">(max {MAX_KPIS}, Enter or comma to add)</span>
            </label>
            <KpiTagInput tags={kpis} onChange={setKpis} />
          </div>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
          {success ? <p className="text-sm text-emerald-600">Objective saved.</p> : null}

          <div className="flex justify-end">
            <Button type="button" onClick={() => void handleSave()} disabled={saving}>
              {saving ? "Saving…" : "Save Objective"}
            </Button>
          </div>
        </>
      )}
    </div>
  );
}
