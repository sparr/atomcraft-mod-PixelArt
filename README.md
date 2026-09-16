# Pixel Art

An Atomcraft mod that lets other mods draw on the world: where a simulation pixel is on screen, and outlines, highlights, direction marks and bitmap text drawn on it, above the world and above the HUD.

**On its own it does nothing visible.** It is a library, and a player installs it because some other mod asks for it. If you are reading this because a mod told you to install Pixel Art, read [Install](#install) and stop; the rest is for people writing one.

**To see what it can draw, install `PixelArt.Demo.zip` beside it.** It puts one example of everything in the world where you spawn, and `./play.sh --demo` launches exactly that. See [The demo mod](#the-demo-mod).

It was extracted from the [Atomcraft TestHarness](https://github.com/sparr/atomcraft-mod-TestHarness), whose `Overlay`, `PixelFont` and the world-to-screen half of its `View` this is. Two mods had already copied that code, which is the usual reason a thing becomes a mod of its own.

## What it gives a mod

| | |
| --- | --- |
| `ViewGeometry` | where a pixel is on screen, which pixels are on screen, which pixel the mouse is over, how far the frame is rescaled on its way to the window |
| `Canvas` | a drawing surface of the mod's own: fills, outlines, direction nubs and text, either retained until cleared or drawn for one frame |
| `PixelFont` | three hand-drawn bitmap fonts, 3x5, 5x7 and 9x13, exact at any whole-number scale |
| `GameArt` | a square, a drop, a cloud and a little person, borrowed from the game's own art and tintable |
| `LabelLayout` | fits a name to a pixel: wraps it, cuts it, marks the cut, picks the font and the scale |
| one per-frame pass | shared by every mod, so a second consumer costs a delegate call per pixel rather than a second walk over the screen |

**Two things are called a pixel, and this document keeps them apart.** The game simulates a world of discrete **pixels**, one material each, and that is what "pixel" means here unless it says otherwise: `ViewGeometry.PixelScreenSize` is how big one is on screen, and a painter is handed one at a time. A pixel of the screen is always called a **screen pixel**. A world pixel's position is a **tile** coordinate, which is the game's own word (`Utils.TileposToGlobal`, `GlobalToTileposI`), so `TileAt` and `VisibleTiles` are named for the coordinate and everything else for the thing at it.

Three things are worth knowing before building on it.

**Marks track pixels, not places on screen.** Everything is stated in pixel coordinates and re-projected every frame, so a mark stays on the pixel it names while the player pans and zooms.

**Marks are not lit, shadowed or fogged.** They are drawn over the finished world rather than into the texture the world is built from, so a fill is the colour you asked for over a pitch-dark cave as much as over open ground. That is usually the point: what an overlay is for is most often explaining why a pixel is not what somebody expected.

**Text is a bitmap font, on purpose.** A pixel is 8 world units, which is 12 screen pixels at the shipped game's closest zoom, and every scalable font blurs at that size because a glyph outline rasterized at 12px lands on fractional pixel boundaries. A bitmap font drawn at a whole-number scale from a whole-number position through a nearest-neighbour filter is exact instead. There are three sizes rather than one scaled three ways, because a 3x5 glyph magnified is still a 3x5 glyph: 9x13 buys round bowls, real diagonals and true descenders.

## Install

1. Install [GodotMonoModLoader](https://github.com/sacroimper/GodotMonoModLoader) and patch the game with it.
2. Drop `PixelArt.zip` into `Mods/` in the game's user data directory, unextracted, beside whichever mod asked for it.

Load order does not matter. A mod may take its canvas and register its drawing from its own `Initialize`, whichever order the loader happens to run the two mods in.

## Settings

`user://PixelArt.json`, written with the defaults and a comment per key the first time the mod runs. Read once, at startup.

| Key | Default | |
| --- | --- | --- |
| `enabled` | `true` | `false` stops every overlay drawn through this mod, without uninstalling anything. Useful for a clean screenshot. |
| `textScale` | `1` | Whole-number multiplier on every label any mod draws, 1 to 8. |

Two keys, and deliberately so: a knob here overrides what another mod's author chose, so anything that mod could reasonably decide for itself belongs in that mod's own settings file. What is here is what a player wants to say once rather than once per mod.

`textScale` is a whole number because that is the whole point of a bitmap font: at 2 every font pixel is a 2x2 block of screen pixels and the glyph stays exact, while at 1.5 it would be resampled into the soft mess a scalable font would have given in the first place. It applies to measuring as well as drawing, so a label that picks its own size steps down to a smaller font rather than overflowing its pixel.

## Using it from a mod

Declare the dependency in `mod.json`, so the loader loads this first and reports a clear error if a player does not have it:

```json
"modules": [
  {
    "moduleId": "YourMod/Main",
    "dll": "YourMod.dll",
    "initClass": "YourMod.ModEntry",
    "dependencies": ["PixelArt/Main"]
  }
]
```

Reference `PixelArt.dll` out of a pinned release zip, never a sibling checkout, exactly as these projects reference the harness. Never ship a copy of it; the loader loads it from its own zip.

```xml
<Reference Include="PixelArt">
  <HintPath>$(PixelArtDir)/PixelArt.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Then take a canvas and draw on it. Retained marks last until `Clear`:

```csharp
using PixelArt;

private static readonly Canvas Marks = Canvas.For("YourMod");

Marks.Outline(tile, Colors.Red, thickness: 2f);
Marks.Label(tile, "leak", Colors.Red, TextSize.Small, LabelPlacement.Above);
Marks.Arrow(tile, Aim.Down, Colors.Red);
Marks.Clear();
```

`Fill`, `Outline`, `Arrow`, `Icon` and `Label` each take a `RectInt` as well as a single pixel — retained and `Draw*` alike — and that is one mark rather than one per pixel: a filled region costs a single draw call, an outlined one gets a single border around the whole block rather than a box per pixel, and an arrow or a label is sized to the block rather than to one pixel of it. At the zoom a world starts at a world pixel is about six screen pixels across -- smaller than any glyph -- so reach for the block overloads whenever the thing you are marking is bigger than one pixel.

A **painter** runs for every pixel on screen, every frame, and draws for that frame only. It is the right shape for state that changes as the simulation runs:

```csharp
Marks.SetPainter("excess", p =>
{
    var excess = Pressure.ExcessAt(p.Tile);
    if (excess > 0)
        p.Label(excess.ToString(), Colors.White);
}, DrawWhen.AltHeld);
```

A **pass** runs once per frame and walks whatever it likes. Reach for this when the mod already knows which pixels it cares about, since being handed all 57,000 on screen to reject most of them is the expensive way round:

```csharp
Marks.SetPass("cursor", c =>
{
    if (ViewGeometry.MouseTile() is { } tile)
        c.DrawOutline(ViewGeometry.ScreenRectOf(tile), Colors.Yellow);
});
```

`test/DebugOverlay.cs` is the worked example: a pass that annotates the pixels around the cursor, using nothing another mod cannot use. `./play.sh --debug` runs it.

Four things about the API that are easy to get wrong:

- **The bare names are retained, the `Draw*` names are for this frame.** `Fill(tile, ...)` adds a mark that is redrawn every frame until `Clear`; `DrawFill(rect, ...)` or `DrawFill(tile, ...)` draws once, now, and is what a pass or a painter calls. Adding a mark every frame is the mistake this naming exists to make visible, and past ten thousand of them the log says so.
- **Gate a pass or painter with a predicate, not just `DrawWhen`.** `SetPass(name, pass, () => Settings.Enabled && Canvas.AltHeld)` is asked once a frame, before the pass runs — so a painter that is not due costs one delegate call rather than 57,000. `DrawWhen.AltHeld` is the shorthand for `() => Canvas.AltHeld` and nothing more. **Prefer the predicate whenever the condition is partly your own**: AltHeld reads the real keyboard, so a mod whose setting says "only while Alt is held" cannot add "and only when my setting is on" — and no test can reach the drawing at all, because a test cannot hold a key down. A predicate that throws is treated as a pass that throws: removed, canvas faulted, one line in the log.
- **`ForEachPixel(block, body)` when you want some pixels, not all of them.** A painter is handed every pixel on screen; a pass is handed none. A cursor-sized overlay wants the 169 around the mouse, and this is that walk — corner projected once, rows stepped from it, clipped to what is on screen and inside the world, returning how many it visited. `ForEachPixel(centre, radius, body)` is the common case.

  ```csharp
  canvas.SetPass("cursor", c =>
  {
      if (ViewGeometry.MouseTile() is { } tile)
          c.ForEachPixel(tile, 6, p => p.Outline(Colors.Yellow));
  });
  ```
- **`TextSize.Auto` picks the largest size that fits the pixel**, at the scale in force, and falls back to the smallest rather than drawing nothing: a label spilling past its pixel can still be read, and an empty pixel looks exactly like a pixel your code decided to skip.
- **Outline a label rather than plating it, when it sits on something.** `plate:` draws a box behind the text; `outline:` draws the text four times in a surround colour, offset a screen pixel each way, and once more on top. Over the game's machines -- which are all fully saturated primaries -- a plate works and looks like what it is: at the sizes a label reaches when zoomed in, a black rectangle covering the very pixel being described. An outline leaves the machine visible between the letters. It costs five textured rects per glyph, which is nothing at any sane number of labels.
- **`TextSize.Fit` picks the scale too, so the text tracks the pixel as the view zooms.** Auto keeps the scale you asked for, so zooming out leaves the text the same size while the pixels shrink underneath it until neighbouring labels overlap into a grey smear. Fit shrinks with them and then, once even the smallest font no longer fits, draws nothing. Use Fit for a label that belongs to *a pixel* and would be noise once it cannot fit inside one; use Auto or a named size for a label that belongs to *the reader* -- a heading, a readout, anything they are meant to find rather than stumble on. Fit prefers detail over size, so a 9x13 at 1x beats a 5x7 doubled, and the player's `textScale` does not apply to it: what fits inside a pixel is the pixel's business. `Canvas.FitFor(tile, text)` answers in advance, and a scale of 0 means it would not be drawn.
- **Nothing in `ViewGeometry` throws.** It answers with a zero, a sentinel or a null when there is no camera or no world, because a render path runs on frames where neither exists, and Godot logs an exception out of such a path on every occurrence with no backpressure at all: one throwing hook made a 1.3 million line `godot.log` in ninety seconds. Ask `ViewGeometry.Ready` when the difference matters.

### Fitting a name to a pixel

`TextSize.Fit` sizes text you hand it. `LabelLayout` decides what the text should be:

```csharp
var fitted = LabelLayout.Choose(mark: "=", body: material.Name, tile);
if (!fitted.Overflows)
    canvas.DrawLabel(tile, fitted, Colors.White, outline: Colors.Black);
```

Given a mark, a body and a pixel, it tries the whole body wrapped, then the whole body on one line, then progressively shorter cuts, and takes the first that fits — preferring bigger glyphs, then narrower ones. Four things it does that are easy to get wrong:

- **It wraps only at spaces.** A hard break inside a word gives two fragments that each read as a word — "Praseo" over "dymium" — and a reader who glances at one line carries away something the pixel does not say.
- **It balances the lines**, so a wrapped label is a block rather than a staircase. Found by trying every width and taking the narrowest that still fits the line budget; the inputs are a handful of words, so the answer is exactly optimal rather than approximately so.
- **Every cut ends in an ellipsis, always.** Material names share long prefixes, so dozens of them cut short land on some *other* material's real name — "Carbon" is a material and so is Carbon Dioxide. An unmarked cut does not read as a cut; it reads as a confident answer naming the wrong thing.
- **It stacks a mark above the body when that buys a bigger font.** A square pixel has as much height as width and one line of text uses almost none of the height, so `=` over `Water` is usually a whole font size better than `=Water`.

**The fit is judged before the player's `textScale`**, and the multiplier applied to the result — which may push it past the pixel. Judging afterwards would answer "draw my labels bigger" by drawing *less text*, which is the opposite of what was asked.

When not even one character fits — the ordinary case at the game's own maximum zoom, where a pixel is 12 screen pixels across and two characters are 11 wide — it returns the fullest candidate with `Overflows` set rather than nothing, because an empty pixel looks exactly like a pixel your mod had no opinion about. **A caller drawing many labels at once should check `Overflows` and skip:** one overflowing label is readable, a hundred on adjacent pixels is a grey smear. That decision is yours because both answers are right for somebody.

A fitted label is drawn with `DrawLabel` only — there is no retained overload. It is an answer about how big the pixel was on one frame, and a retained mark is redrawn at every zoom after that: right once, then quietly wrong.

### Shapes from the game's own art

`GameArt` hands you four shapes the game already draws, and `Canvas.Icon` puts one on a pixel:

```csharp
Marks.Icon(tile, GameArt.ForState(material.State), Colors.White);
Marks.Icon(block, GameArt.Player, Colors.Magenta);
```

They are the material guide's own swatches -- a filled square for a solid, a drop for a liquid, a cloud for a gas -- and the little person from the spaceship inventory. A player who has seen the guide already knows what they mean, and they cost nothing to ship. Found and verified by [AltAnnotations](https://github.com/sparr/atomcraft-mod-AltAnnotations); this is the shared copy so the next mod need not find them again.

Only four, out of the 249 icons the game ships, and that is the point rather than an omission: a shape is tinted by a modulate, which **multiplies**, so it works on a white silhouette with an alpha channel and turns painted art into mud. These four are pure white masks, which the suite re-checks every run rather than taking on trust -- a texture that gets repainted keeps loading and keeps drawing, and nothing but a person looking at it would notice. `GameArt.Load(path, region)` takes any other texture for a mod that has checked one for itself, and `GameArt.IsWhiteMask` is how to check.

An icon is drawn at a whole scale with its proportions kept, so a six-by-nine person stays a person rather than being stretched square, and below one screen pixel per source pixel it is not drawn at all: a fractional downscale of a six-pixel-wide figure loses limbs rather than shrinking.

### One mod's failure is not another's

Each consumer's canvas is its own Godot `CanvasLayer`, so one mod's `Clear` cannot take another's marks with it, and `Layer` decides who draws on top. A pass or painter that throws is removed and **its** canvas is faulted; every other canvas keeps drawing, and `godot.log` gets one report naming the canvas rather than a report per frame forever. `Simulation.Init` and `Simulation.Reset` clear the latch, so a fault costs a session rather than a process.

Pick an owner name of your own -- your mod id is the obvious one. Two mods sharing a name would clear each other's marks, so asking for an existing name on a different layer is refused rather than silently moving somebody else's drawing.

`Canvas.For` is safe to call from anywhere, including from inside a pass on the frame your mod first has something to say: the shared pass draws from a snapshot of the registry, and a canvas registered mid-frame starts drawing on the next one. That is not a detail worth knowing so much as one you should not have to -- it is here because the demo mod does exactly this, and the version of the library that iterated the live registry instead switched drawing off for every installed mod the first time it happened.

### Exactness, and the one thing this mod cannot fix

Everything here is drawn at whole-number positions and whole-number scales, so what leaves this mod is exact. Whether it reaches the window that way is a different question: the game draws into a fixed **1600x900 render target** and the engine rescales that finished frame to the window, so at the shipped default of 1280x720 every frame -- the game's own art included -- is resampled by 0.8 on its way to the screen. That is invisible in world art, which has no one-pixel features to lose, and glaring in a bitmap font, which arrives with rows and columns doubled or dropped and reads as a broken font rather than as a scaled frame.

`ViewGeometry.PixelPerfect` says whether the window is the target's size or a whole multiple of it, and `ViewGeometry.WindowScale` gives the ratio. Nothing in this mod can change either: the rescale happens to the finished frame, after everything inside it has been drawn. The remedy is a window the frame does not need rescaling to fill, which is [ActualResolution](https://github.com/sparr/atomcraft-mod-ActualResolution)'s business.

## The demo mod

`demo/` builds `PixelArt.Demo.zip`, a peer mod that depends on this one and on nothing else -- no harness, so it is something a player can install and look at. Enter a world and it draws a catalogue of one of everything, in the middle of what you can see, nudged left of the spaceship. It then stays on the pixels it was put on: it moves only when the **avatar** walks more than 120 pixels past its edge, never because the view changed. Zooming in shrinks the view around a camera that has not gone anywhere, and an earlier version that re-placed it whenever its middle left the screen jumped it across the world while the player was only trying to look closer.

It is worth reading as well as looking at. It is the worked example of a consumer, and it is deliberately written the way one should be:

- **It patches nothing and references no Harmony.** A consumer of this library does not have to.
- **Its drawing has no try/catch.** A pass that throws is removed by the library and its canvas faulted, with one line in `godot.log` naming it; writing that guard in every consumer is exactly what this mod exists to stop. The one place the demo *does* guard is the one place it touches the game rather than a canvas (below), where the library's answer -- switch this canvas off for the session -- would cost the whole demonstration rather than the two lines that stopped working.
- **It closes the game's two introductions.** A new world opens with the welcome panel over the middle of the screen and a tutorial goal list down the right, both exactly where the catalogue goes. The demo closes the first the way its own button does (`Gameplay.SetCurrentWindowId(WindowId.None)`) and clears the second the way the game's own "show tutorials" setting does (`TutorialGoal.SetGoal(null)`). Neither writes to your profile or your settings, and neither touches any other window -- open the hub or your inventory and the demo leaves it alone. Set `Showcase.DismissIntroWindows = false` to keep them. This is the only thing in the demo that is not drawing, and it lives here rather than in the library because Pixel Art draws *over* the HUD and has no business touching it.
- **It splits retained from per-frame.** `Catalogue.cs` is added once when the catalogue is placed and redrawn from pixel coordinates by the library forever; `LiveSection.cs` is rebuilt every frame because every number in it has changed. That split is the thing most worth copying.
- **It uses a pass for what it can and a painter for what it cannot.** The cursor readout walks the pixels it cares about; the Alt-held material highlight genuinely needs every pixel on screen, which is what a painter is for and why it is gated.

Two tests in `test/DemoTests.cs` keep it honest: that the catalogue fits the room reserved for it (headless), and that the whole of it lands inside the pixels the game is drawing when a player enters a world (headful, and it leaves the screenshot in the run's artifacts).

## Building and testing

```sh
cp harness.conf.example harness.conf   # and point it at a TestHarness release
./build.sh [--release]                 # Debug by default; --release is what ships
./run-tests.sh                         # this project's tests
./run-tests.sh --headful               # and the ones that need something on screen
./run-tests.sh --retirement            # is the game still the shape this works around?
./run-tests.sh --no-build              # test what is installed; how a release is verified
./play.sh --demo                       # play it as a player would: the library and the demo
./play.sh --debug                      # the harness's debug overlay instead
```

Cut every release with `--release`. A Debug assembly carries `DebuggableAttribute` with `DisableOptimizations`, which turns the JIT off for it entirely, and this mod's inner loop runs once per visible pixel per frame.

`./run-tests.sh` runs against a patched **copy** of the game in a throwaway prefix, never your own install, and a test root private to this project. See `harness.conf.example`.

## Layout

```
src/            the shipped mod
  Canvas.cs         a surface one mod draws on: marks, passes, painters, primitives
  ViewGeometry.cs   where a pixel is on screen, and which pixels are
  GameArt.cs        shapes borrowed from the game's own art, and how to borrow more
  LabelLayout.cs    fitting a name to a pixel: wrapping, cutting, and which font says it
  PixelFont.cs      three bitmap fonts and the atlas each is drawn through
  Renderer.cs       the one per-frame pass, shared by every canvas
  RenderPatch.cs    where it hangs off the game
  DrawTypes.cs      the enums, and the VisiblePixel a painter is handed
  PixelArtApi.cs    what it is doing right now, and the switch that stops it
demo/           the demo mod: one of everything, drawn in the world. A peer mod depending
                  only on this one, and the worked example of a consumer
  Showcase.cs       the canvases, where the catalogue is placed, and when it is rebuilt
  Catalogue.cs      the retained half: one example of every mark, with a caption each
  LiveSection.cs    the per-frame half: readouts, the animation, the cursor, the painter
test/           the test mod, a peer mod that depends on this one, the demo and the harness
conformance/    properties of the game every overlay mod depends on, naming no mod
minimal/        the mechanism restated in one file, outside the SDK's glob
```

### The tests

`ScreenTests` are the ones that matter and the ones that cost: they enter a world, hold the camera, draw, read the render target back and look at the pixels. The rest is arithmetic -- glyph tables, label metrics, the canvas registry -- and runs headless, which is where a game update should be caught.

### The conformance suite

`conformance/` names no mod. It asks whether the game still lets *anybody* draw over the world: whether a canvas layer above the game reaches the frame, and whether the game's own screen-to-world mapping is still the inverse of the camera's projection. Both are assumptions every overlay mod makes, none of them documented anywhere, and if either stopped holding, the authors of each such mod would find out separately as "my marks vanished". A failure there is bad news, which is what separates it from the retirement suite.

### The retirement suite

`test/RetirementTests.cs` asserts the game is **still** the shape this mod works around, so a failure there is good news and means something can be deleted. Each test's doc comment says what. It is excluded from the everyday run, because mixing "is the mod correct" with "is the game still broken" makes a red suite unreadable.

### The debug overlay

`test/DebugOverlay.cs` lives in the test mod rather than the shipped one, because it is a demonstration for a person rather than a feature, and because a shipped assembly that referenced `Atomcraft.TestHarness.dll` would fail to load for every player without the harness. `./play.sh --debug` is the one place `play.sh` deliberately loads the harness, and it accepts the lost autosaves that come with it.

## Compatibility

Written against Atomcraft build **25333425** (`Atomcraft.dll` md5 `24bae9b4d904deb16b573d3279a5d3e1`) and TestHarness 0.4.0.

Nothing here touches the simulation: this mod reads the field to ask what is in a pixel and writes nothing at all, so it cannot change what the world does or desync a multiplayer session, and two players need not both have it.

If a game update moves what the mod reaches for, it says so at startup and does nothing further rather than failing obscurely:

```
[PixelArt] ERROR: the game no longer has Gameplay.Process, so there is no per-frame hook to draw from
and nothing will be drawn by any mod that uses this one.
```

Compatible with anything that does not also draw on a canvas layer at the very top. Several mods drawing *through* this one is the case it is built for.

- **[ActualResolution](https://github.com/sparr/atomcraft-mod-ActualResolution)** -- recommended alongside it. It resizes the render target to the window, which is the only way what this draws reaches the screen unresampled.
- **[Zoooom](https://github.com/sparr/atomcraft-mod-Zoooom)** -- zoom in further and a world pixel is a large target on screen, which is where a label inside a single one becomes legible.
- **[IntegerZoom](https://github.com/sparr/atomcraft-mod-IntegerZoom)** -- snapping the zoom to a whole number of screen pixels per world pixel keeps a pixel's edges on screen-pixel boundaries, so the rounding this mod does per mark has nothing left to round.
- **The TestHarness** -- its own `Overlay` still exists and draws on layer 128. A canvas taken here sits at 126 by default, under it, so a harness debug mark draws on top rather than fighting for the same plane.
- **Anything that changes the camera** is fine: everything is projected through `Client.FollowCam` every frame, so a mod that moves, zooms or rounds the camera is followed rather than fought.

## What it patches

| Method | Why |
| --- | --- |
| `Gameplay.Process` (postfix) | rebuild every canvas, right after the game has rebuilt the world they sit on. This is where the game positions the world sprite from the camera, so a postfix on it projects through exactly the camera the frame was drawn with |
| `Simulation.Init` (postfix) | clear the fault latches, so a fault costs a session rather than a process |
| `Simulation.Reset` (postfix) | the same, and the one reliable "this world is over" signal |
| `Game.OnApplicationQuit` (prefix) | hand back the `RenderingServer` canvas items and the font atlases, which are server resources nothing else frees, so the engine does not report them as leaks at exit |

Nothing is reached by reflection and nothing private is touched: every one of those is public and named with `nameof`, so a rename is a build error rather than a silence. `src/GameBindings.cs` re-checks them at startup anyway, for the player running a different build from the one this was compiled against.

The simulation is never touched. This mod draws, and does not read or write a single pixel except to ask what colour to make a mark.

## Migrating the three copies this was extracted from

Nothing has been changed in any of them yet, on purpose: a mod in this tree pins a dependency to a **release**, never to a sibling checkout, and this has no release to pin. Once it does, here is what each can drop.

**TestHarness** -- `src/Overlay.cs`, `src/PixelFont.cs` and the world-to-screen section of `src/View.cs` are this mod. The harness can keep `Overlay` as a thin shim over a canvas of its own (`Canvas.For("TestHarness", Canvas.TopLayer)`) so its own tests and every mod's test code keep compiling, and `View` keeps the parts a test needs and this mod deliberately does not have: `LookAt`, `RevealFog`, `MaxZoomFactor`, `DismissModLoaderReport`. Its `OverlayTests` split the same way -- the font and geometry half moves here, the zoom-limit half stays. One thing to watch: the harness's copy raises `AssertionException` from bad font arguments and this one raises `ArgumentException`, since a shipped mod cannot reference the harness.

**AltAnnotations** -- `src/PixelFont.cs`, `src/AnnotationCanvas.cs` and `src/ViewGeometry.cs` go, and its per-frame `AnnotationPainter.Redraw` becomes a `SetPass` on a canvas. Its own render hook, fault latch and headless check all go with them. The `≠` glyph it added is already here: it is `PixelFont.NotEqual`, composed at atlas time from the font's own `=` and `/` rather than hand-drawn, which is why it is right at all three sizes.

**Pressure** -- `test/DebugOverlay.cs` labels every compressed pixel with its density while Alt is held, and it sits in the test mod only because it draws through the harness. Through this mod it can move to the shipped side and become a thing a player can switch on, rather than a thing reachable only from `play.sh --debug`. Its `HoverHintPatch.ShowInternals` flag exists to let the test mod drive it from the outside without the shipped assembly naming a harness type, and that flag can go with it.

## License

MIT. See [LICENSE](LICENSE), which ships inside the zip, because a zip is what a player receives.
