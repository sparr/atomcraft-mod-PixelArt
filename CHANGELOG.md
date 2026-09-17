# Changelog

Broad strokes only. Each release's notes say more, and the commits say most.

## 0.3.0

New bitmap fonts. **Breaking for consumers**, so move your `RequireVersion` pin to `"0.3"`.

- The largest face is **7x11**, replacing 9x13. Seven columns centre a one-pixel stem exactly, so `I`, `T`, `l`, `i`, `1` and `|` no longer read as bold beside everything else, and a line of text costs about half the ink and a fifth less width.
- **Every size has real descenders.** They are drawn below the glyph box, into the vertical spacing, rather than folded up into the body, so `g j p q y` are proper letters at 3x5 and 5x7 for the first time and caps keep the whole box.
- `PixelFont.Spacing` splits into `HorizontalSpacing` and `VerticalSpacing`. A glyph now has three heights: `GlyphHeight` (the box, what a fit is judged against), `DescenderDepth`, and `DrawnHeight` (the atlas cell). A mod drawing its own command list against `Atlas` must size its destination rect by `DrawnHeight`.
- `Measure` counts the descender on every label, whatever the text says, so a fit cannot be claimed for a label whose tail would fall outside the pixel and a live label's baseline does not move when a descender appears.

## 0.2.0

- **Predicate gating.** `SetPass` and `SetPainter` take a `Func<bool>`, asked once per frame rather than once per pixel. `DrawWhen.AltHeld` read the real keyboard and nothing else, so a consumer could not combine it with its own setting and no test could reach a gated pass at all.
- **`ForEachPixel`** over a block or a centre and radius, for an overlay that wants 169 pixels rather than the ~57,000 a painter is handed.
- **Passes can run without a display**, opt-in through `PixelArtApi.RunPassesWithoutDisplay`, so a consumer's per-pixel logic is reachable from a headless test. Left alone, a headless frame still costs nothing.
- `DrawFill` and `DrawOutline` gained the `RectInt` overload the other draw calls already had.

## 0.1.0

First release. A drawing layer other Atomcraft mods build their overlays on: where a world pixel is on screen, and outlines, highlights, direction arrows, borrowed game art and bitmap text drawn on it, above the world and above the HUD.

Extracted from the TestHarness's `Overlay`, `PixelFont` and the world-to-screen half of its `View`, then merged with the copies AltAnnotations had already made of the same code. Each consumer gets its own canvas, so one mod's `Clear` cannot reach another's marks and a pass that throws faults only its own canvas, while the visible-pixel walk happens once per frame for all of them.
