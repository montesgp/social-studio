# Spec Delta: Content Studio — Client Style Fidelity

<!-- Change: client-style-fidelity -->
<!-- Applies to: openspec/specs/content-studio/spec.md -->

## ADDED Requirements

### Requirement: CSF-CS-01 — Studio Loads the Bundled Font Catalog

The API MUST serve the bundled catalog as `GET /api/content-studio/fonts.css` (`@font-face` rules)
and `GET /api/content-studio/fonts/{dir}/{file}` (font files), without authentication, since the
fonts are OFL-licensed. The Studio canvas MUST load the stylesheet once into `document.head`, so
edited text uses the same faces as the rendered slide.
