"use client";

// AiNarrativePanel — PA-11, PA-16, PA-18 AI analysis narrative display.
// Renders summary, audience profile, content pillars, top content types,
// posting days, voice/tone, strengths, weaknesses, opportunities, KPIs,
// monthly objective, content strategy suggestion, and detected brand assets.
// Any null/empty field is omitted (PA-18). Low-confidence warnings shown (PA-11).

import { Badge } from "@/components/ui/badge";
import type { ProfileAiAnalysisDto } from "@/lib/api/types/profile-analyzer";

// ── Types ─────────────────────────────────────────────────────────────────────

interface AiNarrativePanelProps {
  aiAnalysis: ProfileAiAnalysisDto | null;
}

// ── Sub-components ────────────────────────────────────────────────────────────

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <h4 className="text-xs font-semibold text-muted-foreground/80 uppercase tracking-wide mb-1.5">
      {children}
    </h4>
  );
}

function TagList({ items }: { items: string[] }) {
  if (items.length === 0) return null;
  return (
    <div className="flex flex-wrap gap-1.5">
      {items.map((item, i) => (
        <Badge key={i} variant="outline" className="text-xs font-normal">
          {item}
        </Badge>
      ))}
    </div>
  );
}

function BulletList({ items }: { items: string[] }) {
  if (items.length === 0) return null;
  return (
    <ul className="space-y-0.5 text-sm text-foreground/80">
      {items.map((item, i) => (
        <li key={i} className="flex items-start gap-2">
          <span className="mt-0.5 shrink-0 text-muted-foreground/50">•</span>
          <span>{item}</span>
        </li>
      ))}
    </ul>
  );
}

function ConfidenceBadge({ confidence }: { confidence: number }) {
  const pct = Math.round(confidence * 100);
  const variant =
    pct >= 80 ? "success" : pct >= 50 ? "warning" : "destructive";
  return (
    <Badge variant={variant} className="text-xs">
      Confidence: {pct}%
    </Badge>
  );
}

// ── Component ─────────────────────────────────────────────────────────────────

export function AiNarrativePanel({ aiAnalysis }: AiNarrativePanelProps) {
  if (!aiAnalysis) {
    return (
      <div className="flex items-center justify-center h-20 rounded-md border border-border/30 border-dashed">
        <p className="text-xs text-muted-foreground/60">
          AI analysis unavailable
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Header row with confidence */}
      <div className="flex items-start justify-between gap-3">
        <h3 className="text-sm font-semibold">AI analysis</h3>
        <ConfidenceBadge confidence={aiAnalysis.confidence} />
      </div>

      {/* Low-confidence or sparse-data warnings (PA-11) */}
      {aiAnalysis.warnings && aiAnalysis.warnings.length > 0 && (
        <div className="rounded-md border border-amber-500/30 bg-amber-500/5 p-3 space-y-1 print:bg-amber-50 print:border-amber-600/50">
          <p className="text-xs font-medium text-amber-700 dark:text-amber-300 print:!text-amber-900">
            Analysis warnings
          </p>
          {aiAnalysis.warnings.map((w, i) => (
            <p key={i} className="text-xs text-amber-800 dark:text-amber-200 print:!text-amber-900">
              {w}
            </p>
          ))}
        </div>
      )}

      {/* Summary */}
      {aiAnalysis.summary && (
        <div>
          <SectionTitle>Summary</SectionTitle>
          <p className="text-sm text-foreground/80 leading-relaxed">{aiAnalysis.summary}</p>
        </div>
      )}

      {/* Audience profile */}
      {aiAnalysis.audienceProfile && (
        <div>
          <SectionTitle>Audience profile</SectionTitle>
          <p className="text-sm text-foreground/80">{aiAnalysis.audienceProfile}</p>
        </div>
      )}

      {/* Voice / tone */}
      {aiAnalysis.voiceTone && (
        <div>
          <SectionTitle>Voice and tone</SectionTitle>
          <p className="text-sm text-foreground/80">{aiAnalysis.voiceTone}</p>
        </div>
      )}

      {/* Content pillars */}
      {aiAnalysis.contentPillars.length > 0 && (
        <div>
          <SectionTitle>Content pillars</SectionTitle>
          <TagList items={aiAnalysis.contentPillars} />
        </div>
      )}

      {/* Top content types */}
      {aiAnalysis.topContentTypes.length > 0 && (
        <div>
          <SectionTitle>Top content types</SectionTitle>
          <TagList items={aiAnalysis.topContentTypes} />
        </div>
      )}

      {/* Posting days */}
      {aiAnalysis.postingDays.length > 0 && (
        <div>
          <SectionTitle>Recommended posting days</SectionTitle>
          <TagList items={aiAnalysis.postingDays} />
        </div>
      )}

      {/* Strengths / weaknesses / opportunities */}
      <div className="grid gap-3 sm:grid-cols-3">
        {aiAnalysis.strengths.length > 0 && (
          <div>
            <SectionTitle>Strengths</SectionTitle>
            <BulletList items={aiAnalysis.strengths} />
          </div>
        )}
        {aiAnalysis.weaknesses.length > 0 && (
          <div>
            <SectionTitle>Weaknesses</SectionTitle>
            <BulletList items={aiAnalysis.weaknesses} />
          </div>
        )}
        {aiAnalysis.opportunities.length > 0 && (
          <div>
            <SectionTitle>Opportunities</SectionTitle>
            <BulletList items={aiAnalysis.opportunities} />
          </div>
        )}
      </div>

      {/* Recommended KPIs */}
      {aiAnalysis.recommendedKpis.length > 0 && (
        <div>
          <SectionTitle>Recommended KPIs</SectionTitle>
          <TagList items={aiAnalysis.recommendedKpis} />
        </div>
      )}

      {/* Monthly objective suggestion */}
      {aiAnalysis.monthlyObjectiveSuggestion && (
        <div>
          <SectionTitle>Suggested monthly objective</SectionTitle>
          <p className="text-sm text-foreground/80">{aiAnalysis.monthlyObjectiveSuggestion}</p>
        </div>
      )}

      {/* Content strategy suggestion */}
      {aiAnalysis.contentStrategySuggestion && (
        <div>
          <SectionTitle>Suggested content strategy</SectionTitle>
          <p className="text-sm text-foreground/80">{aiAnalysis.contentStrategySuggestion}</p>
        </div>
      )}

      {/* Detected brand assets */}
      {aiAnalysis.detectedBrandColors.length > 0 && (
        <div>
          <SectionTitle>Detected brand colors</SectionTitle>
          <div className="flex flex-wrap gap-2">
            {aiAnalysis.detectedBrandColors.map((color, i) => (
              <div key={i} className="flex items-center gap-1.5">
                <span
                  className="inline-block h-4 w-4 rounded-full border border-border/40 shrink-0"
                  style={{ backgroundColor: color.hex }}
                  title={color.hex}
                />
                <span className="text-xs text-foreground/70">{color.name}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {aiAnalysis.detectedFonts.length > 0 && (
        <div>
          <SectionTitle>Detected fonts</SectionTitle>
          <TagList items={aiAnalysis.detectedFonts} />
        </div>
      )}
    </div>
  );
}
