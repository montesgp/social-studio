# Content Generation — Package Download Behavior

What a community manager (CM) sees when downloading a generated content package, and how the system names files and folders inside the ZIP.

> Related diagram: [`single-analyzer-flow`](../architecture/diagrams/single-analyzer-flow.md).
> Spec delta: [`openspec/changes/single-analyzer-fase-a/specs/content-generation/spec-delta.md`](../../openspec/changes/single-analyzer-fase-a/specs/content-generation/spec-delta.md).

---

## Quick path

1. Run a single-content analysis and select a client.
2. Generate the desired formats from the convert panel.
   - **Script** → `reel-script`
   - **Story** (Single) → `story-single` · **Story Carousel** → `story-carousel`
   - **Feed Post** (Single) → `feed-single` · **Feed Carousel** → `feed-carousel`
3. Click **package.zip** to assemble and download all generated formats at once.
4. Open the ZIP — find each client's assets in a folder named after the client's display name.

---

## Format keys and file naming inside the ZIP

| Format key | Display name | File / folder inside ZIP |
|---|---|---|
| `reel-script` | Script | `{client}/script/script.md` |
| `story-single` | Story | `{client}/story-single/story-single.pptx` |
| `story-carousel` | Story · Carousel | `{client}/story-carousel/slide_1.pptx`, `slide_2.pptx`, … |
| `feed-single` | Feed Single | `{client}/feed-single/feed-single.pptx` |
| `feed-carousel` | Feed · Carousel | `{client}/feed-carousel/slide_1.pptx`, `slide_2.pptx`, … |
| `caption` / `hook` / etc. | Auxiliaries | `{client}/{key}/{key}.txt` |

Visual formats (story-single, story-carousel, feed-single, feed-carousel) are delivered as editable PPTX files.
Legacy keys `"story"` and `"feed-post"` are read aliases for existing rows/assets. The legacy key `"carousel"` is no longer accepted for new generation requests; existing stored packages with `format_key = 'carousel'` continue to assemble correctly.

**Reel script format.** The `reel-script.md` file contains newline-separated scene blocks. Each scene starts with a bold Markdown timestamp (`**0:00**`), followed by voiceover and on-screen text. Emojis in AI-generated text are preserved verbatim. A hook line opens the script and a CTA line closes it.

Example structure:
```
Hook line

**0:00**: Voiceover text
On-screen text here

**0:30**: Next scene voiceover

CTA: Call to action text
```

---

## Client folder naming inside the ZIP

Each client's generated assets are grouped under a folder named from the client's **display name**, not a GUID or a slug. The name is sanitized for cross-OS filesystem compatibility:

| Rule | Effect | Example |
|---|---|---|
| Remove `< > : " / \ | ? *` and control chars | Illegal chars stripped | `Client:Name` → `ClientName` |
| Trim leading dots and spaces | Prevents Unix hidden-directory | `.config` → `config` |
| Trim trailing dots and spaces | Required by Windows | `Café & Co.` → `Café & Co` |
| Windows device names neutralized | Appends `_` when the full name matches `CON`, `NUL`, etc. | `NUL` → `NUL_` |
| Case, accents, spaces, `&`, emoji preserved | No slug or lowercase conversion | `Café & Co` stays `Café & Co` |

If a client cannot be resolved (e.g. deleted client), the GUID is used as a fallback folder name.

---

## Details

| Topic | Decision |
|---|---|
| **Reel-script extension** | `.md` (Markdown), not `.txt`. The frontend `FORMAT_FILE_NAMES` map reflects this; existing `.txt`-based code was removed in Fase A. |
| **Folder source (frozen ZIP)** | `GeneratedPackageBuilder.NormalizeFolderName(client.Name)` — called at generation time with the name from `BrandSnapshot`. |
| **Folder source (assemble on-demand)** | `AssembleGeneratedPackageQueryHandler` resolves the display name via `ISocialClientRepository.GetByIdAsync` scoped to the organization. The same `NormalizeFolderName` sanitizer is used. |
| **Canva export** | Canva export is a Fase B placeholder. Both download paths produce Canva-compatible formats. |

---

## Checklist

- [ ] Reel-script download appears as `reel-script.md` and opens in any Markdown viewer.
- [ ] ZIP folder is named after the client's display name (e.g. `Acme Corp/`) not a GUID or kebab-case slug.
- [ ] Accented names (e.g. `Café & Co`) appear correctly in the ZIP folder name.
- [ ] Regenerating a format and re-downloading reflects the latest content.

---

## Next step

For the generation pipeline architecture and the Download / Export-to-Canva decision node, see the [`single-analyzer-flow` diagram](../architecture/diagrams/single-analyzer-flow.md).
