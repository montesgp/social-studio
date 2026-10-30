# Project Domain

Model workflow events explicitly. Treat AI outputs, prompts and evaluations as first-class domain artifacts.

## Content Format Taxonomy (`Domain/Social/ContentFormats/`)

The content format taxonomy uses three orthogonal axis types — `Surface`, `Layout`, and `Medium` — plus an immutable `FormatCatalog` that is the single source of truth for all supported formats.

### Axis types

- `Surface` — Script | Story | Feed (content intent)
- `Layout` — Single | Carousel (Script has no layout; Layout is null for Script rows)
- `Medium` — Image | Video | Text (Medium.Video is representable; no video format ships in v1)
- `FormatDefinition` — a catalog row value object with FormatKey, Surface, Layout?, Medium, Channel, ContentMedium, IsMultiAsset, DisplayLabel, StorageFolder, IsAuxiliary, IsVisual

### v1 catalog entries

| FormatKey | Surface | Layout | Medium | IsMultiAsset |
|---|---|---|---|---|
| reel-script | Script | — | Text | false |
| story | Story | Single | Image | false |
| story-carousel | Story | Carousel | Image | true |
| feed-post | Feed | Single | Image | false |
| feed-carousel | Feed | Carousel | Image | true |

Plus auxiliaries (content-idea, cta-variation, caption, hook) — Text medium, excluded from the UI picker.

### Catalog API

- `FormatCatalog.TryGet(key)` — canonical-only lookup; returns null for "carousel" and other legacy/invalid keys.
- `FormatCatalog.ResolveForAssembly(key)` — read/assemble path only; resolves canonical keys AND the legacy "carousel" alias.
- `FormatCatalog.KnownKeys()` — selectable/whitelist set; does NOT include the legacy "carousel" alias.
- `FormatCatalog.BySurface(surface)` — deliverable entries for a surface (excludes auxiliaries).
- `FormatCatalog.ValidLayoutsFor(surface)` — Script → [], Story/Feed → [Single, Carousel].

### Rules for AI agents

- Do NOT add new format keys to FormatCatalog without a matching generator + composer + test.
- Do NOT add the legacy "carousel" key to KnownKeys or TryGet; it is read-only via ResolveForAssembly.
- Do NOT create literals like `new[] { "story", "feed-post", "carousel" }` — derive from FormatCatalog.All().
- Adding a future video format requires ONE FormatDefinition row + wiring. Axis types and derive/normalize layer require no changes.

## Font Contract (`FontCatalog`)

`SocialStudio.Application/Features/Social/ContentGeneration/Visual/Fonts/FontCatalog.cs` is the
single source of truth for which font families Content Studio may offer, validate, and render.
Three consumers must never disagree, and all three read this one static class:

1. **Guardrail allow-list** (`VisualGuardrailValidator.ValidateTypography`) — rejects any named
   font that is neither the client's brand typography nor a `FontCatalog` entry.
2. **Picker UI** — the frontend font picker never hardcodes a font list; it consumes
   `GET /api/social/content-studio/render-capabilities` (`GetRenderCapabilitiesQuery`), which calls
   `FontCatalog.For(brand.Typography)`.
3. **Embedded render fonts** (`IFontProvider` / `EmbeddedFontProvider`) — every entry in
   `FontCatalog.SystemSafeList` MUST be resolvable by the embedded font provider.

`FontCatalog.SystemSafeList` intentionally contains only OFL/Bitstream-licensed families
(Liberation Sans/Serif/Mono, DejaVu Sans/Serif) — never Arial/Helvetica, which are
Monotype-licensed and cannot be legally embedded. See `THIRD-PARTY-NOTICES.md` for the full
license inventory.

### Rules for AI agents

- Do NOT add a font to the picker, the guardrail allow-list, or the embedded renderer
  independently. Add it to `FontCatalog` and let all three consumers pick it up.
- Do NOT introduce a second hardcoded font list anywhere in `apps/web` — the picker must always
  source its options from the render-capabilities endpoint.
- A font added to `FontCatalog.SystemSafeList` MUST have its `.ttf` asset embedded under
  `SocialStudio.Infrastructure/Rendering/Fonts/Assets/` and its license recorded in
  `THIRD-PARTY-NOTICES.md`.

## Caption invariant (`ContentDraft.Caption`)

`ContentDraft` exposes a dedicated `Caption` field, separate from `Text` (which continues to carry
reel scripts and other non-caption content). `Caption` (`SocialStudio.Domain/Social/ContentStudio/Caption.cs`)
enforces Instagram's publishing ceilings as a **domain-level invariant**, not a publishing-adapter
concern:

- maximum 2,200 characters
- maximum 30 hashtags

Caption is never null after generation: if the AI-generated visual markup omits a caption,
`CaptionFallback` produces a deterministic, non-empty fallback and the job logs that the fallback
was used — caption absence never fails a generation job. The caption UI (`content-studio-panel.tsx`)
is enabled for every placement, including stories (no `targetPlacement !== "story"` gate).

### Rules for AI agents

- Do NOT write caption-shaped content into `ContentDraft.Text`. Use `Caption.Create` and the
  dedicated field.
- Do NOT bypass `Caption.Create` when constructing or editing a caption — the 2,200-character and
  30-hashtag ceilings must be enforced at construction time, in every code path (grep for all
  `ContentDraft`/`ContentItem` construction sites when touching this invariant, not just the one a
  task names — a prior gap here shipped caption wiring on only one of two materialization paths).
