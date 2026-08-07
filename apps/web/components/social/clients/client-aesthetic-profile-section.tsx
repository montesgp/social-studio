"use client";

import { useCallback, useEffect, useState } from "react";
import {
  getClientAestheticProfile,
  updateClientAestheticProfileBackgroundPattern,
} from "@/lib/api/client-aesthetic-profile";
import type { ClientAestheticProfileAdmin, SlideBackgroundKind } from "@/lib/api/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { useDateFormatter } from "@/lib/format/use-date-formatter";

interface ClientAestheticProfileSectionProps {
  clientId: string;
  token: string;
}

const SLIDE_KIND_OPTIONS: SlideBackgroundKind[] = ["Photo", "FlatColor", "Decorator"];

function EmptyValue() {
  return <span className="text-xs italic text-muted-foreground">missing</span>;
}

/**
 * client-analysis-data admin panel: shows everything captured from the client's last profile
 * analysis and lets an admin correct the per-slide BackgroundPattern by hand — the field that
 * gates whether generation invokes the ComfyUI AI-background path for a slide.
 *
 * All other fields (palette, style descriptors, motifs, reference images, etc.) are read-only
 * for now; a future change can extend the same edit flow to them.
 */
export function ClientAestheticProfileSection({ clientId, token }: ClientAestheticProfileSectionProps) {
  const { formatDateTime: formatDate } = useDateFormatter();
  const [loading, setLoading] = useState(true);
  const [data, setData] = useState<ClientAestheticProfileAdmin | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [editedPattern, setEditedPattern] = useState<SlideBackgroundKind[]>([]);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saveSuccess, setSaveSuccess] = useState(false);

  const load = useCallback(async () => {
    if (!clientId || !token) return;
    setLoading(true);
    setLoadError(null);
    try {
      const result = await getClientAestheticProfile(clientId, token);
      setData(result);
      setEditedPattern(result.profile?.backgroundPattern ?? []);
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to load aesthetic profile.";
      setLoadError(message);
    } finally {
      setLoading(false);
    }
  }, [clientId, token]);

  useEffect(() => {
    void load();
  }, [load]);

  function setSlideKind(index: number, kind: SlideBackgroundKind) {
    setEditedPattern((prev) => prev.map((v, i) => (i === index ? kind : v)));
    setSaveSuccess(false);
  }

  function addSlide() {
    setEditedPattern((prev) => [...prev, "FlatColor"]);
    setSaveSuccess(false);
  }

  function removeSlide(index: number) {
    setEditedPattern((prev) => prev.filter((_, i) => i !== index));
    setSaveSuccess(false);
  }

  async function handleSaveBackgroundPattern() {
    if (editedPattern.length === 0) {
      setSaveError("At least one slide is required.");
      return;
    }
    setSaving(true);
    setSaveError(null);
    setSaveSuccess(false);
    try {
      await updateClientAestheticProfileBackgroundPattern(
        clientId,
        { backgroundPattern: editedPattern },
        token,
      );
      setSaveSuccess(true);
      void load();
    } catch (err) {
      const message = (err as { message?: string })?.message ?? "Failed to save background pattern.";
      setSaveError(message);
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return <p className="text-xs text-muted-foreground">Loading aesthetic profile…</p>;
  }

  if (loadError) {
    return <p className="text-sm text-destructive">{loadError}</p>;
  }

  if (!data?.hasAnalysis) {
    return (
      <p className="text-sm text-muted-foreground">
        No profile analysis has been captured for this client yet.
      </p>
    );
  }

  const profile = data.profile;
  const canEdit = data.status === "Analyzed";

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-3 rounded-md border border-border bg-muted/30 p-3">
        <Badge variant={data.status === "Analyzed" ? "success" : data.status === "Failed" ? "destructive" : "info"}>
          {data.status ?? "Unknown"}
        </Badge>
        <span className="text-xs text-muted-foreground">
          Source: {data.profileUrlOrHandle ?? <EmptyValue />}
        </span>
        <span className="text-xs text-muted-foreground">
          Completed: {formatDate(data.completedAtUtc)}
        </span>
      </div>

      {!profile ? (
        <p className="text-sm text-muted-foreground">
          The latest analysis has not produced a captured aesthetic profile yet (status:{" "}
          {data.status ?? "unknown"}).
        </p>
      ) : (
        <div className="space-y-6">
          {/* ── Background Pattern (EDITABLE) ─────────────────────────────── */}
          <div className="space-y-2">
            <p className="text-sm font-semibold text-foreground">
              Background Pattern <span className="text-muted-foreground text-xs">(per slide, editable)</span>
            </p>
            <p className="text-xs text-muted-foreground">
              Controls which slides get an AI-generated background. Fix misclassified slides here.
            </p>
            {editedPattern.length === 0 ? (
              <EmptyValue />
            ) : (
              <div className="flex flex-wrap gap-2">
                {editedPattern.map((kind, index) => (
                  <div key={index} className="flex items-center gap-1 rounded-md border border-border p-1.5">
                    <span className="text-xs text-muted-foreground">#{index + 1}</span>
                    <select
                      aria-label={`Slide ${index + 1} background kind`}
                      value={kind}
                      onChange={(e) => setSlideKind(index, e.target.value as SlideBackgroundKind)}
                      className="rounded border border-input bg-background px-1.5 py-1 text-xs"
                    >
                      {SLIDE_KIND_OPTIONS.map((opt) => (
                        <option key={opt} value={opt}>
                          {opt}
                        </option>
                      ))}
                    </select>
                    <button
                      type="button"
                      onClick={() => removeSlide(index)}
                      className="text-xs text-muted-foreground hover:text-destructive"
                      aria-label={`Remove slide ${index + 1}`}
                    >
                      ×
                    </button>
                  </div>
                ))}
              </div>
            )}
            <div className="flex items-center gap-2">
              <Button type="button" variant="outline" size="sm" onClick={addSlide}>
                Add slide
              </Button>
              <Button
                type="button"
                size="sm"
                disabled={!canEdit || saving}
                onClick={() => void handleSaveBackgroundPattern()}
              >
                {saving ? "Saving…" : "Save Background Pattern"}
              </Button>
            </div>
            {!canEdit ? (
              <p className="text-xs text-muted-foreground">
                Editing is only available once the analysis status is &quot;Analyzed&quot;.
              </p>
            ) : null}
            {saveError ? <p className="text-xs text-destructive">{saveError}</p> : null}
            {saveSuccess ? <p className="text-xs text-emerald-600">Background pattern saved.</p> : null}
          </div>

          {/* ── Read-only captured data ────────────────────────────────────── */}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1">
              <p className="text-xs font-semibold text-muted-foreground">Voice Tone</p>
              <p className="text-sm">{profile.voiceTone ?? <EmptyValue />}</p>
            </div>
            <div className="space-y-1">
              <p className="text-xs font-semibold text-muted-foreground">Caption Voice</p>
              <p className="text-sm">{profile.captionVoice ?? <EmptyValue />}</p>
            </div>
            <div className="space-y-1">
              <p className="text-xs font-semibold text-muted-foreground">Average Slides / Carousel</p>
              <p className="text-sm">{profile.averageSlidesPerCarousel ?? <EmptyValue />}</p>
            </div>
            <div className="space-y-1">
              <p className="text-xs font-semibold text-muted-foreground">Typical Slide Count</p>
              <p className="text-sm">{profile.typicalSlideCount ?? <EmptyValue />}</p>
            </div>
          </div>

          <div className="space-y-1">
            <p className="text-xs font-semibold text-muted-foreground">Palette</p>
            {profile.palette && profile.palette.length > 0 ? (
              <div className="flex flex-wrap gap-2">
                {profile.palette.map((entry, index) => (
                  <div key={index} className="flex items-center gap-1.5 rounded-md border border-border px-2 py-1">
                    <span
                      className="h-4 w-4 rounded-full border border-border"
                      style={{ backgroundColor: entry.hex }}
                    />
                    <span className="text-xs">{entry.hex}</span>
                    {entry.role ? <span className="text-xs text-muted-foreground">({entry.role})</span> : null}
                  </div>
                ))}
              </div>
            ) : (
              <EmptyValue />
            )}
          </div>

          <div className="space-y-1">
            <p className="text-xs font-semibold text-muted-foreground">Color Pairings</p>
            {profile.colorPairings && profile.colorPairings.length > 0 ? (
              <div className="flex flex-wrap gap-2">
                {profile.colorPairings.map((pair, index) => (
                  <div key={index} className="flex items-center gap-1 rounded-md border border-border px-2 py-1 text-xs">
                    <span className="h-3 w-3 rounded-full border border-border" style={{ backgroundColor: pair.backgroundHex }} />
                    <span>on</span>
                    <span className="h-3 w-3 rounded-full border border-border" style={{ backgroundColor: pair.textHex }} />
                  </div>
                ))}
              </div>
            ) : (
              <EmptyValue />
            )}
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1">
              <p className="text-xs font-semibold text-muted-foreground">Style Descriptors</p>
              {profile.styleDescriptors && profile.styleDescriptors.length > 0 ? (
                <div className="flex flex-wrap gap-1">
                  {profile.styleDescriptors.map((d) => (
                    <Badge key={d} variant="outline">{d}</Badge>
                  ))}
                </div>
              ) : (
                <EmptyValue />
              )}
            </div>
            <div className="space-y-1">
              <p className="text-xs font-semibold text-muted-foreground">Motifs</p>
              {profile.motifs && profile.motifs.length > 0 ? (
                <div className="flex flex-wrap gap-1">
                  {profile.motifs.map((m) => (
                    <Badge key={m} variant="outline">{m}</Badge>
                  ))}
                </div>
              ) : (
                <EmptyValue />
              )}
            </div>
          </div>

          <div className="space-y-1">
            <p className="text-xs font-semibold text-muted-foreground">Reference Images</p>
            <p className="text-sm">
              {profile.referenceImages && profile.referenceImages.length > 0
                ? `${profile.referenceImages.length} captured`
                : <EmptyValue />}
            </p>
          </div>

          <div className="space-y-1">
            <p className="text-xs font-semibold text-muted-foreground">Brand Mark</p>
            {profile.brandMark ? (
              <p className="text-sm">
                {profile.brandMark.present
                  ? `Present${profile.brandMark.positionHint ? ` (${profile.brandMark.positionHint})` : ""}`
                  : "Not detected"}
              </p>
            ) : (
              <EmptyValue />
            )}
          </div>

          <div className="space-y-1">
            <p className="text-xs font-semibold text-muted-foreground">Conceptual Method</p>
            {profile.conceptualMethod ? (
              <p className="text-sm">
                {profile.conceptualMethod.method} — {profile.conceptualMethod.rationale}
              </p>
            ) : (
              <EmptyValue />
            )}
          </div>
        </div>
      )}
    </div>
  );
}
