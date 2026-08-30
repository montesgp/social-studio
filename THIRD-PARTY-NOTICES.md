# Third-Party Notices

This project embeds or depends on the following third-party components. Each
entry lists the component, its license, and where the full license text is
stored in this repository.

## Embedded fonts (server-side rendering)

The renderer embeds these font files so that carousel/story/reel visuals
render deterministically regardless of the host machine's installed fonts
(`Incoders.Hive.Infrastructure/Rendering/Fonts/Assets/`). All are licensed
under terms that permit embedding and redistribution.

| Font family | File(s) | License | Full text |
|---|---|---|---|
| Noto Color Emoji | `NotoColorEmoji.ttf` | SIL Open Font License 1.1 | `Incoders.Hive.Infrastructure/Rendering/Fonts/Licenses/NotoColorEmoji.OFL.txt` |
| Liberation Sans | `LiberationSans-Regular.ttf` | SIL Open Font License 1.1 | `Incoders.Hive.Infrastructure/Rendering/Fonts/Licenses/Liberation.OFL.txt` |
| Liberation Serif | `LiberationSerif-Regular.ttf` | SIL Open Font License 1.1 | `Incoders.Hive.Infrastructure/Rendering/Fonts/Licenses/Liberation.OFL.txt` |
| Liberation Mono | `LiberationMono-Regular.ttf` | SIL Open Font License 1.1 | `Incoders.Hive.Infrastructure/Rendering/Fonts/Licenses/Liberation.OFL.txt` |
| DejaVu Sans | `DejaVuSans-Regular.ttf` | Bitstream Vera + public domain (DejaVu) terms | `Incoders.Hive.Infrastructure/Rendering/Fonts/Licenses/DejaVu.LICENSE.txt` |
| DejaVu Serif | `DejaVuSerif-Regular.ttf` | Bitstream Vera + public domain (DejaVu) terms | `Incoders.Hive.Infrastructure/Rendering/Fonts/Licenses/DejaVu.LICENSE.txt` |

None of these are Arial/Helvetica or other Monotype-licensed families, which
cannot be legally embedded; the system-safe font list (`FontCatalog`) is
deliberately restricted to the families above.

## Frontend runtime dependencies

| Package | Version | License | Purpose |
|---|---|---|---|
| `emoji-picker-react` | 4.19.1 | MIT | Emoji picker UI for caption and slide-text editing in Content Studio (`apps/web`) |

`emoji-picker-react`'s own `LICENSE` file is distributed with the package
under `apps/web/node_modules/emoji-picker-react/LICENSE` and is not
duplicated here; its terms are standard MIT.
