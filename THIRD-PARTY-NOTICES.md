# Third-Party Notices

This project embeds or depends on the following third-party components. Each
entry lists the component, its license, and where the full license text is
stored in this repository.

## Embedded fonts (server-side rendering)

The renderer embeds these font files so that carousel/story/reel visuals
render deterministically regardless of the host machine's installed fonts
(`SocialStudio.Infrastructure/Rendering/Fonts/Assets/`). All are licensed
under terms that permit embedding and redistribution.

| Font family | File(s) | License | Full text |
|---|---|---|---|
| Noto Color Emoji | `NotoColorEmoji.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Licenses/NotoColorEmoji.OFL.txt` |
| Liberation Sans | `LiberationSans-Regular.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Licenses/Liberation.OFL.txt` |
| Liberation Serif | `LiberationSerif-Regular.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Licenses/Liberation.OFL.txt` |
| Liberation Mono | `LiberationMono-Regular.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Licenses/Liberation.OFL.txt` |
| DejaVu Sans | `DejaVuSans-Regular.ttf` | Bitstream Vera + public domain (DejaVu) terms | `SocialStudio.Infrastructure/Rendering/Fonts/Licenses/DejaVu.LICENSE.txt` |
| DejaVu Serif | `DejaVuSerif-Regular.ttf` | Bitstream Vera + public domain (DejaVu) terms | `SocialStudio.Infrastructure/Rendering/Fonts/Licenses/DejaVu.LICENSE.txt` |

### Client typography catalog

Curated families used to reproduce each client's observed typography
(`BundledFontCatalog`, OpenSpec change `client-style-fidelity`). Sourced from
the `ofl/` tree of github.com/google/fonts; each family's licence text ships
next to its files under `Rendering/Fonts/Assets/Catalog/<family>/OFL.txt`.

| Font family | File(s) | License | Full text |
|---|---|---|---|
| League Gothic | `LeagueGothic[wdth].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/leaguegothic/OFL.txt` |
| Antonio | `Antonio[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/antonio/OFL.txt` |
| Anton | `Anton-Regular.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/anton/OFL.txt` |
| Oswald | `Oswald[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/oswald/OFL.txt` |
| Bebas Neue | `BebasNeue-Regular.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/bebasneue/OFL.txt` |
| Fjalla One | `FjallaOne-Regular.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/fjallaone/OFL.txt` |
| Barlow Condensed | `BarlowCondensed-Regular.ttf`, `BarlowCondensed-Medium.ttf`, `BarlowCondensed-SemiBold.ttf`, `BarlowCondensed-Bold.ttf`, `BarlowCondensed-ExtraBold.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/barlowcondensed/OFL.txt` |
| Roboto Condensed | `RobotoCondensed[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/robotocondensed/OFL.txt` |
| Sofia Sans Extra Condensed | `SofiaSansExtraCondensed[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/sofiasansextracondensed/OFL.txt` |
| Archivo Narrow | `ArchivoNarrow[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/archivonarrow/OFL.txt` |
| Teko | `Teko[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/teko/OFL.txt` |
| Poppins | `Poppins-Light.ttf`, `Poppins-Regular.ttf`, `Poppins-Medium.ttf`, `Poppins-SemiBold.ttf`, `Poppins-Bold.ttf`, `Poppins-ExtraBold.ttf`, `Poppins-Black.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/poppins/OFL.txt` |
| Montserrat | `Montserrat[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/montserrat/OFL.txt` |
| Inter | `Inter[opsz,wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/inter/OFL.txt` |
| DM Sans | `DMSans[opsz,wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/dmsans/OFL.txt` |
| Work Sans | `WorkSans[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/worksans/OFL.txt` |
| Raleway | `Raleway[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/raleway/OFL.txt` |
| Open Sans | `OpenSans[wdth,wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/opensans/OFL.txt` |
| Roboto | `Roboto[wdth,wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/roboto/OFL.txt` |
| Space Grotesk | `SpaceGrotesk[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/spacegrotesk/OFL.txt` |
| Archivo Black | `ArchivoBlack-Regular.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/archivoblack/OFL.txt` |
| Nunito | `Nunito[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/nunito/OFL.txt` |
| Playfair Display | `PlayfairDisplay[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/playfairdisplay/OFL.txt` |
| Lora | `Lora[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/lora/OFL.txt` |
| DM Serif Display | `DMSerifDisplay-Regular.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/dmserifdisplay/OFL.txt` |
| EB Garamond | `EBGaramond[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/ebgaramond/OFL.txt` |
| Space Mono | `SpaceMono-Regular.ttf`, `SpaceMono-Bold.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/spacemono/OFL.txt` |
| Courier Prime | `CourierPrime-Regular.ttf`, `CourierPrime-Bold.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/courierprime/OFL.txt` |
| JetBrains Mono | `JetBrainsMono[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/jetbrainsmono/OFL.txt` |
| IBM Plex Mono | `IBMPlexMono-Light.ttf`, `IBMPlexMono-Regular.ttf`, `IBMPlexMono-Medium.ttf`, `IBMPlexMono-SemiBold.ttf`, `IBMPlexMono-Bold.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/ibmplexmono/OFL.txt` |
| Caveat | `Caveat[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/caveat/OFL.txt` |
| Dancing Script | `DancingScript[wght].ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/dancingscript/OFL.txt` |
| Pacifico | `Pacifico-Regular.ttf` | SIL Open Font License 1.1 | `SocialStudio.Infrastructure/Rendering/Fonts/Assets/Catalog/pacifico/OFL.txt` |

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
