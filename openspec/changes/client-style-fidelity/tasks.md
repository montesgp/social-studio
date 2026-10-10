# Tasks: Client Style Fidelity

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Delivery strategy | Direct to `dev` (owner instruction), one commit per work unit |
| Chained PRs recommended | No |

## Work Units

| Unit | Goal | Focused test command |
|------|------|----------------------|
| WU-1 | Font catalog, provider faces, weight clamp, resolution | `dotnet test --filter "FullyQualifiedName~Fonts\|FullyQualifiedName~ClientTypographyResolution"` |
| WU-2 | Chromium font injection + Studio font endpoints + Studio stylesheet | `dotnet test --filter HtmlChromiumContentRenderer` |
| WU-3 | Composition fixes: template by layout, no hardcoded decoration, tracking, width | `dotnet test --filter "FullyQualifiedName~ContentGeneration"` |
| WU-4 | Observed colour pairings, pixel grounding, frequency allocation | `dotnet test --filter "FullyQualifiedName~ColorPairing\|FullyQualifiedName~ResolvePalette"` |
| WU-5 | Extractor v9 prompt/DTO/mapping + persistence | `dotnet test --filter "FullyQualifiedName~AestheticStyleExtractor\|FullyQualifiedName~AestheticProfilePersistence"` |
| WU-6 | Visual font match | `dotnet test --filter FontCandidateVisualMatcher` |
| WU-7 | Keyword highlight (generator + composer + builders) | `dotnet test --filter "FullyQualifiedName~Highlight"` |
| WU-8 | Regenerate Demo Roasters and compare against the client's posts | Manual |

## Checklist

- [ ] 1.1 Embed catalog fonts + OFL licences; update `THIRD-PARTY-NOTICES.md`
- [ ] 1.2 `FontCatalog.Bundled`, `ClampWeight`, extended `SystemSafeList`
- [ ] 1.3 `IFontProvider.ResolveFaces` + `EmbeddedFontProvider`
- [ ] 1.4 `FontFamilyResolution` token/contains/descriptor resolution
- [ ] 2.1 Renderer injects referenced catalog families
- [ ] 2.2 Studio `fonts.css` + font file endpoints; canvas loads the stylesheet
- [ ] 3.1 Carousel template chosen by layout
- [ ] 3.2 No accent pill / bar / empty chip with an aesthetic context
- [ ] 3.3 Clamped weights, neutral tracking, measured heading width
- [ ] 4.1 `ColorPairing.SlideCount/Kind`; `ColorPairingEvidence.Filter`; Skia sampler
- [ ] 4.2 Frequency allocation in `ResolvePalette`
- [ ] 5.1 Extractor v9 prompt + DTO + mapping; persistence round-trip
- [ ] 6.1 Visual font match
- [ ] 7.1 `titleHighlight` generation + highlight rendering
- [ ] 8.1 Gates green; regenerate Demo Roasters; archive change
