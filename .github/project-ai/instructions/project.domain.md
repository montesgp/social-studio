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
