"use client";

import { KeyboardEvent, useEffect, useState } from "react";
import { X } from "lucide-react";
import { getClientStrategy, upsertClientStrategy } from "@/lib/api/client-strategy";
import type { UpsertClientStrategyPayload } from "@/lib/api/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

interface ClientStrategySectionProps {
  clientId: string;
  token: string;
}

const MAX_PILLARS = 10;
const MAX_KEYWORDS = 20;

function TagInput({
  tags,
  onChange,
  max,
  placeholder,
  id,
}: {
  tags: string[];
  onChange: (tags: string[]) => void;
  max: number;
  placeholder: string;
  id: string;
}) {
  const [input, setInput] = useState("");
  const atLimit = tags.length >= max;

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
        id={id}
        type="text"
        value={input}
        onChange={(e) => setInput(e.target.value)}
        onKeyDown={handleKeyDown}
        onBlur={handleBlur}
        disabled={atLimit}
        placeholder={atLimit ? `Max ${max} reached` : placeholder}
        className="min-w-20 flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
      />
    </div>
  );
}

export function ClientStrategySection({ clientId, token }: ClientStrategySectionProps) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  const [contentPillars, setContentPillars] = useState<string[]>([]);
  const [toneKeywords, setToneKeywords] = useState<string[]>([]);
  const [targetAudience, setTargetAudience] = useState("");
  const [postingFrequency, setPostingFrequency] = useState("");
  const [notes, setNotes] = useState("");

  useEffect(() => {
    if (!clientId || !token) return;
    setLoading(true);
    getClientStrategy(clientId, token)
      .then((strategy) => {
        if (strategy) {
          setContentPillars(strategy.contentPillars ?? []);
          setToneKeywords(strategy.toneKeywords ?? []);
          setTargetAudience(strategy.targetAudience ?? "");
          setPostingFrequency(strategy.postingFrequency ?? "");
          setNotes(strategy.notes ?? "");
        }
      })
      .catch(() => {
        setError("Failed to load strategy.");
      })
      .finally(() => setLoading(false));
  }, [clientId, token]);

  async function handleSave() {
    setSaving(true);
    setError(null);
    setSuccess(false);
    try {
      const payload: UpsertClientStrategyPayload = {
        contentPillars,
        toneKeywords,
        targetAudience: targetAudience.trim() || null,
        postingFrequency: postingFrequency.trim() || null,
        notes: notes.trim() || null,
      };
      await upsertClientStrategy(clientId, payload, token);
      setSuccess(true);
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to save strategy.";
      setError(message);
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return <p className="text-xs text-muted-foreground">Loading strategy…</p>;
  }

  return (
    <div className="space-y-4">
      <div className="space-y-1.5">
        <label htmlFor="pillars-input" className="text-sm font-medium">
          Content Pillars{" "}
          <span className="text-muted-foreground text-xs">(max {MAX_PILLARS}, Enter or comma to add)</span>
        </label>
        <TagInput
          id="pillars-input"
          tags={contentPillars}
          onChange={setContentPillars}
          max={MAX_PILLARS}
          placeholder="e.g. Education"
        />
      </div>

      <div className="space-y-1.5">
        <label htmlFor="keywords-input" className="text-sm font-medium">
          Tone Keywords{" "}
          <span className="text-muted-foreground text-xs">(max {MAX_KEYWORDS})</span>
        </label>
        <TagInput
          id="keywords-input"
          tags={toneKeywords}
          onChange={setToneKeywords}
          max={MAX_KEYWORDS}
          placeholder="e.g. Professional"
        />
      </div>

      <div className="space-y-1.5">
        <label htmlFor="target-audience" className="text-sm font-medium">
          Target Audience <span className="text-muted-foreground text-xs">(optional)</span>
        </label>
        <textarea
          id="target-audience"
          value={targetAudience}
          onChange={(e) => setTargetAudience(e.target.value)}
          maxLength={1000}
          rows={3}
          placeholder="Describe the target audience…"
          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        />
      </div>

      <div className="space-y-1.5">
        <label htmlFor="posting-frequency" className="text-sm font-medium">
          Posting Frequency <span className="text-muted-foreground text-xs">(optional, max 200)</span>
        </label>
        <Input
          id="posting-frequency"
          value={postingFrequency}
          onChange={(e) => setPostingFrequency(e.target.value)}
          maxLength={200}
          placeholder="e.g. 3x per week"
        />
      </div>

      <div className="space-y-1.5">
        <label htmlFor="strategy-notes" className="text-sm font-medium">
          Notes <span className="text-muted-foreground text-xs">(optional, max 2000)</span>
        </label>
        <textarea
          id="strategy-notes"
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          maxLength={2000}
          rows={4}
          placeholder="Additional strategy notes…"
          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        />
      </div>

      {error ? <p className="text-sm text-destructive">{error}</p> : null}
      {success ? <p className="text-sm text-emerald-600">Strategy saved.</p> : null}

      <div className="flex justify-end">
        <Button type="button" onClick={() => void handleSave()} disabled={saving}>
          {saving ? "Saving…" : "Save Strategy"}
        </Button>
      </div>
    </div>
  );
}
