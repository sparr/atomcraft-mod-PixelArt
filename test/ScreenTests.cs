using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

// The game has a Session type of its own; the harness's is the one meant here.
using Session = Atomcraft.TestHarness.Session;

namespace PixelArt.Test;

/// <summary>
/// What the arithmetic cannot show: that a mark reaches the frame, on the pixel it names, at the
/// size it was drawn at.
///
/// <para>These need a display and abstain without one, so the ordinary headless suite stays green
/// and <c>./run-tests.sh --headful</c> is where they count. They read the render target, before
/// the engine scales that to the window, so what is measured is the frame the game drew; whether
/// that survives to the window unresampled is a display concern -- see
/// <see cref="ViewGeometry.PixelPerfect"/> and the ActualResolution mod.</para>
///
/// <para>Unqualified <c>Canvas</c>, <c>ViewGeometry</c> and <c>TextSize</c> are this mod's; C#
/// prefers the enclosing namespace to a using directive, so the harness's namesakes do not get in
/// the way. <c>View</c> is the harness's, and is used only to point the camera somewhere known.</para>
/// </summary>
public static class ScreenTests
{
    /// <summary>The canvas these tests draw on, dropped after each so a run does not accumulate layers.</summary>
    private const string Owner = "pixelart.test.screen";

    /// <summary>
    /// The pixel-to-screen mapping agrees with the game's own, or a mark lands next to the pixel it
    /// names.
    ///
    /// <para>Checked by round-tripping through the game's inverse
    /// (<c>Utils.ScreenPositionToWorldPosition</c>), so a change to either side fails this, and
    /// across the view rather than at one point, because an error in the camera term vanishes at
    /// the center of the screen -- exactly where a single-point test would put it.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator ScreenCoordinatesRoundTripThroughTheGamesOwnMapping()
    {
        yield return Session.Enter("flat");
        var center = Anchor();
        yield return View.LookAt(center);

        if (!ViewGeometry.Ready)
            throw new AssertionException(
                "ViewGeometry is not ready inside a session with the view held; it wants a " +
                "camera, a viewport and a simulation field");

        foreach (var offset in new[]
                 {
                     new Vector2I(0, 0), new Vector2I(5, 3), new Vector2I(-5, -3),
                     new Vector2I(20, -12), new Vector2I(-20, 12),
                 })
        {
            var tile = center + offset;
            var back = ViewGeometry.TileAt(ViewGeometry.ScreenOf(tile));
            if (back != tile)
                throw new AssertionException(
                    $"the center of {tile} is at {ViewGeometry.ScreenOf(tile)}, which the game " +
                    $"reads back as {back}. PixelArt and the game disagree about where a pixel is.");

            var rect = ViewGeometry.ScreenRectOf(tile);
            if (Math.Abs(rect.Size.X - ViewGeometry.PixelScreenSize) > 0.001f)
                throw new AssertionException(
                    $"a pixel measures {rect.Size.X} across but PixelScreenSize says " +
                    $"{ViewGeometry.PixelScreenSize}");
        }

        // A block of pixels is one rectangle, not a rectangle per pixel.
        var block = ViewGeometry.ScreenRectOf(new RectInt(center.X, center.Y, 3, 2));
        var expected = new Vector2(3, 2) * ViewGeometry.PixelScreenSize;
        if ((block.Size - expected).Length() > 0.01f)
            throw new AssertionException(
                $"a 3x2 block of pixels measured {block.Size}, expected {expected}");

        if (!ViewGeometry.IsVisible(center))
            throw new AssertionException("the tile the view is held on is not reported visible");

        yield return Session.Leave();
    }

    /// <summary>
    /// A painter is handed the pixels that are on screen, with what is in them, and nothing else.
    ///
    /// <para>Proven against a pixel this test places itself: the painter has to see that exact
    /// tile carrying that exact material, and the screen rectangle it is given has to be where the
    /// mapping says the pixel is.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator APainterSeesEveryVisibleCell()
    {
        yield return Session.Enter("flat");
        var tile = Anchor();
        yield return View.LookAt(tile);

        Session.SetPixel(tile.X, tile.Y, "Granite");
        var granite = Materials.GetBaseMaterialId("Granite");

        var canvas = Canvas.For(Owner);
        var seen = 0;
        short sawMaterial = -1;
        var sawRect = new Rect2();
        canvas.SetPainter("probe", p =>
        {
            if (p.Tile != tile)
                return;
            seen++;
            sawMaterial = p.MaterialTypeId;
            sawRect = p.Screen;
            p.Outline(Colors.Lime, 2f);
        });

        try
        {
            yield return Wait.Frames(3);

            if (canvas.Faulted)
                throw new AssertionException($"the canvas faulted: {canvas.Fault}");
            if (seen == 0)
                throw new AssertionException(
                    $"the painter was never handed {tile}, though the view is held on it. It " +
                    $"painted {PixelArtApi.PixelsPaintedLastFrame} pixels, over " +
                    $"{ViewGeometry.VisibleTiles()?.min} to {ViewGeometry.VisibleTiles()?.max}.");
            if (sawMaterial != granite)
                throw new AssertionException(
                    $"the painter saw material {sawMaterial} at {tile}, expected Granite " +
                    $"({granite}). The painter is reading a different id space than SimField stores.");
            if (sawRect.Position.DistanceTo(ViewGeometry.ScreenRectOf(tile).Position) > 1f)
                throw new AssertionException(
                    $"the painter placed {tile} at {sawRect.Position} but ScreenRectOf says " +
                    $"{ViewGeometry.ScreenRectOf(tile).Position}");
            if (canvas.PixelsPaintedLastFrame < 1)
                throw new AssertionException("no pixels were reported painted");
        }
        finally
        {
            Canvas.Forget(Owner);
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// <c>ForEachPixel</c> walks exactly the block asked for, clipped to what is on screen, and
    /// hands over the same thing a painter gets.
    ///
    /// <para>The point of it is that a cursor-sized overlay wants 169 pixels and a painter offers
    /// 57,000. Before this existed every such mod reprojected the corner and stepped the rows for
    /// itself -- and could not build a <c>VisiblePixel</c> anyway, since the constructor is
    /// internal.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator ForEachPixelWalksTheBlockItWasGiven()
    {
        yield return Session.Enter("flat");
        var tile = Anchor();
        yield return View.LookAt(tile);

        Session.SetPixel(tile.X, tile.Y, "Granite");
        var granite = Materials.GetBaseMaterialId("Granite");

        var canvas = Canvas.For(Owner);
        try
        {
            const int radius = 6;
            var seen = 0;
            short sawMaterial = -1;
            var sawRect = new Rect2();
            var sawCentre = false;

            var visited = canvas.ForEachPixel(tile, radius, p =>
            {
                seen++;
                if (p.Tile != tile)
                    return;
                sawCentre = true;
                sawMaterial = p.MaterialTypeId;
                sawRect = p.Screen;
            });

            var side = 2 * radius + 1;
            if (visited != side * side)
                throw new AssertionException(
                    $"a radius of {radius} around a tile in open view is {side}x{side} pixels; " +
                    $"it walked {visited}");
            if (visited != seen)
                throw new AssertionException(
                    $"it reported {visited} but called the body {seen} time(s)");
            if (!sawCentre)
                throw new AssertionException($"the block never included its own centre {tile}");
            if (sawMaterial != granite)
                throw new AssertionException(
                    $"it saw material {sawMaterial} at {tile}, expected Granite ({granite})");
            if (sawRect.Position.DistanceTo(ViewGeometry.ScreenRectOf(tile).Position) > 1f)
                throw new AssertionException(
                    $"it placed {tile} at {sawRect.Position} but ScreenRectOf says " +
                    $"{ViewGeometry.ScreenRectOf(tile).Position}");

            // Clipped rather than refused: a block off the edge of the view yields the part that
            // is on it, and a block entirely off yields nothing.
            var far = new RectInt(tile.X + 100_000, tile.Y, 4, 4);
            if (canvas.ForEachPixel(far, _ => throw new Exception("walked an off-screen pixel")) != 0)
                throw new AssertionException("a block far off screen was walked anyway");
        }
        finally
        {
            Canvas.Forget(Owner);
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// A painter that throws takes its own canvas down and leaves every other canvas drawing.
    ///
    /// <para>This is the difference between a shared drawing layer and a shared point of failure,
    /// and it is worth a headful test because the removal happens inside the frame rather than
    /// where the painter was registered. Godot logs an unhandled per-frame exception on every
    /// occurrence with no backpressure at all -- one bad hook made a 1.3 million line log in
    /// ninety seconds -- so "removed after one throw" is the contract.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator AThrowingPainterTakesOnlyItsOwnCanvasDown()
    {
        const string bad = "pixelart.test.bad";
        const string good = "pixelart.test.good";

        yield return Session.Enter("flat");
        var tile = Anchor();
        yield return View.LookAt(tile);

        var loud = Canvas.For(bad);
        var quiet = Canvas.For(good);
        var quietRan = 0;

        loud.SetPainter("throws", _ => throw new InvalidOperationException("deliberate"));
        quiet.SetPainter("counts", _ => quietRan++);

        try
        {
            yield return Wait.Frames(3);

            if (!loud.Faulted)
                throw new AssertionException(
                    "the canvas whose painter threw is not faulted, so it will be called again " +
                    "next frame and every frame after that");
            if (loud.PassCount != 0)
                throw new AssertionException(
                    $"the throwing painter is still registered ({loud.PassCount} pass(es))");
            if (quiet.Faulted)
                throw new AssertionException(
                    $"an unrelated mod's canvas was faulted by somebody else's exception: " +
                    $"{quiet.Fault}");
            if (quietRan == 0)
                throw new AssertionException(
                    "the surviving canvas's painter never ran, so one mod's bad painter stops " +
                    "every other mod's drawing");
            if (PixelArtApi.Faulted)
                throw new AssertionException(
                    $"one painter's exception switched the whole shared pass off: {PixelArtApi.Fault}");
        }
        finally
        {
            Canvas.Forget(bad);
            Canvas.Forget(good);
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// A pass may register a canvas while the frame is being drawn, which is what a mod that
    /// creates one lazily does on the frame it first has something to say.
    ///
    /// <para><b>A regression test.</b> The shared pass used to iterate the live canvas registry,
    /// so the first <c>Canvas.For</c> from inside a pass threw "collection was modified" out of
    /// the pass itself and switched drawing off for <i>every</i> mod for the rest of the session.
    /// It was order-dependent -- harmless whenever something earlier had already created that
    /// canvas -- which is exactly the kind of failure a rule in the documentation does not
    /// prevent.</para>
    ///
    /// <para>Found by looking at a screenshot of the demo mod, which does this, and noticing the
    /// frame was empty while every assertion about it passed.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator APassMayRegisterACanvasWhileTheFrameIsDrawn()
    {
        const string host = "pixelart.test.host";
        const string late = "pixelart.test.late";

        yield return Session.Enter("flat");
        var tile = Anchor();
        yield return View.LookAt(tile);

        var canvas = Canvas.For(host);
        var runs = 0;
        canvas.SetPass("registers", _ =>
        {
            runs++;
            // Registering a canvas mid-frame, and drawing on it, which is all a lazily-created
            // canvas amounts to.
            Canvas.For(late, 119).Fill(tile, Colors.Magenta);
        });

        try
        {
            yield return Wait.Frames(3);

            if (PixelArtApi.Faulted)
                throw new AssertionException(
                    "the shared pass faulted when a pass registered a canvas, so nothing at all " +
                    $"is drawn for any mod now: {PixelArtApi.Fault}");
            if (canvas.Faulted)
                throw new AssertionException($"the registering canvas faulted: {canvas.Fault}");
            if (runs < 2)
                throw new AssertionException(
                    $"the pass ran {runs} time(s); it should run every frame, so it was removed");
            if (Canvas.All.All(c => c.Owner != late))
                throw new AssertionException("the canvas registered from the pass is not registered");
        }
        finally
        {
            Canvas.Forget(host);
            Canvas.Forget(late);
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// A mark actually reaches the framebuffer, on the pixel it names.
    ///
    /// <para>Everything else checks the arithmetic leading up to the draw; this reads the rendered
    /// frame back and looks at the pixel. Deliberately fills over a pixel whose own colour is
    /// nothing like the fill, and samples the middle of the pixel rather than its edge, so neither
    /// the material underneath nor a rounding error at a boundary can produce a pass.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator AFillReachesTheRenderedFrame()
    {
        yield return Session.Enter("flat");
        var tile = Anchor();
        yield return View.LookAt(tile);

        var canvas = Canvas.For(Owner);
        var restore = View.MaxZoomFactor;
        try
        {
            // Zoomed in, so the pixel is a large target on screen and a pixel or two of camera
            // drift cannot move the sample off it.
            View.MaxZoomFactor = 8;
            yield return View.SetZoom(View.GameMaxZoom * 8f);

            var fill = new Color(1f, 0f, 1f);           // magenta: nothing in the world is this
            canvas.Fill(tile, fill);
            yield return Wait.Frames(3);

            var image = Game.CanvasLayer.GetViewport().GetTexture()?.GetImage();
            if (image == null || image.GetWidth() == 0)
                Harness.Inapplicable("the viewport cannot be read back on this renderer");

            var at = ViewGeometry.ScreenOf(tile);
            var x = Mathf.Clamp((int)at.X, 0, image!.GetWidth() - 1);
            var y = Mathf.Clamp((int)at.Y, 0, image.GetHeight() - 1);
            var got = image.GetPixel(x, y);

            if (Distance(got, fill) > 0.1f)
                throw new AssertionException(
                    $"the rendered frame shows {got} at the center of filled pixel {tile} " +
                    $"(screen {x},{y}), expected {fill}. The canvas is not reaching the screen, " +
                    $"or it is drawing somewhere other than where ScreenOf says. " +
                    $"{PixelArtApi.DescribeState()}");
        }
        finally
        {
            View.MaxZoomFactor = restore;
            Canvas.Forget(Owner);
        }

        yield return Session.Leave();

        static float Distance(Color a, Color b) =>
            Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    }

    /// <summary>
    /// Glyphs come out the right way up, the right way round, and as blocks of exactly the size
    /// they were drawn at.
    ///
    /// <para>Two claims in one test because they need the same expensive setup and because
    /// neither catches the other's failure. A glyph drawn upside down still measures correctly,
    /// still fills the same rectangle and still looks like text at a glance, so the orientation is
    /// checked against a letter asymmetric in both axes. A glyph stretched by a fraction still has
    /// the right pixel in the middle of every block, so the exactness is checked by measuring the
    /// extent of a bar that should be one font pixel wide.</para>
    ///
    /// <para>The frame is saved as an artifact either way, so a headful run leaves behind a
    /// picture of the overlay that someone can simply look at.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator GlyphsDrawTheRightWayUpAndAsExactBlocks()
    {
        // Blocks bigger than one pixel, so sampling their middles has something to be tolerant
        // with; at scale 1 a "block" is a single pixel and the tolerance is gone.
        const int labelScale = 3;
        const int barScale = 4;

        yield return Session.Enter("flat");
        var tile = Anchor();
        yield return View.LookAt(tile);

        var canvas = Canvas.For(Owner);
        var restore = View.MaxZoomFactor;
        var restoreScale = Settings.TextScale;
        try
        {
            // The player's own multiplier would change every expected size below; this test is
            // about the drawing, not about the setting, which CanvasTests covers.
            Settings.TextScale = 1;
            View.MaxZoomFactor = 8;
            yield return View.SetZoom(View.GameMaxZoom * 8f);

            // Black under white, so "lit" and "unlit" are unambiguous whatever the world is doing
            // underneath, and both are painted by this mod in the intended order.
            canvas.Fill(tile, Colors.Black);
            canvas.Label(tile, "L", Colors.White, TextSize.Large, scale: labelScale);
            yield return Wait.Frames(3);

            var image = Game.CanvasLayer.GetViewport().GetTexture()?.GetImage();
            if (image == null || image.GetWidth() == 0)
                Harness.Inapplicable("the viewport cannot be read back on this renderer");

            Artifacts.WriteBytes("glyph.png", image!.SavePngToBuffer());

            var measured = Canvas.MeasureLabel("L", TextSize.Large, labelScale);
            var origin = ViewGeometry.ScreenOf(tile) - (Vector2)measured / 2f;

            // Large 'L' is a ONE-pixel stem down the left with the foot on the baseline, row 10 of
            // the 11-row box: "#......" ten times over, then "#######". Rows 11 and 12 are the
            // descender zone, drawn into the vertical spacing, which an 'L' never reaches.
            Lit(0, 0, "the top of the stem");
            Dark(1, 0, "the column beside the stem, which is lit only if the stroke is two wide");
            Dark(4, 0, "the top right, which is only lit if the glyph is upside down");
            Lit(0, 10, "the foot of the stem");
            Lit(6, 10, "the far end of the foot");
            Dark(4, 4, "the open middle right");
            Dark(4, 11, "the descender zone, which an 'L' does not reach into");

            void Lit(int fx, int fy, string what)
            {
                var c = Sample(fx, fy);
                if (c.R + c.G + c.B < 2.5f)
                    throw new AssertionException(
                        $"{what} of a Large 'L' should be white, the frame shows {c}");
            }

            void Dark(int fx, int fy, string what)
            {
                var c = Sample(fx, fy);
                if (c.R + c.G + c.B > 0.5f)
                    throw new AssertionException(
                        $"{what} of a Large 'L' should be unlit black, the frame shows {c}");
            }

            // The middle of the font pixel's block, so being a pixel out anywhere upstream still
            // reads the block that was meant.
            Color Sample(int fx, int fy)
            {
                var x = (int)Math.Round(origin.X) + fx * labelScale + labelScale / 2;
                var y = (int)Math.Round(origin.Y) + fy * labelScale + labelScale / 2;
                return image.GetPixel(Mathf.Clamp(x, 0, image.GetWidth() - 1),
                                      Mathf.Clamp(y, 0, image.GetHeight() - 1));
            }

            // -------- and now the extent, which the tolerant sampling above cannot see

            var ink = new Color(1f, 0f, 1f);
            canvas.Clear();
            // Opaque and larger than the glyph, so every pixel searched is either backdrop or ink
            // and the world underneath cannot be mistaken for either.
            var patch = new RectInt(tile.X - 4, tile.Y - 4, 9, 9);
            canvas.Fill(patch, Colors.Black);
            // Small on purpose: its '|' is exactly one lit column five rows tall, which makes the
            // expected block trivially stateable. The draw path is shared by all three sizes, so
            // proving it exact for one proves it for all.
            canvas.Label(tile, "|", ink, TextSize.Small, scale: barScale);
            yield return Wait.Frames(3);

            image = Game.CanvasLayer.GetViewport().GetTexture()?.GetImage();
            if (image == null || image.GetWidth() == 0)
                Harness.Inapplicable("the viewport cannot be read back on this renderer");

            var area = ViewGeometry.ScreenRectOf(patch);
            var x0 = Math.Max(0, Mathf.FloorToInt(area.Position.X));
            var y0 = Math.Max(0, Mathf.FloorToInt(area.Position.Y));
            var x1 = Math.Min(image!.GetWidth(), Mathf.CeilToInt(area.End.X));
            var y1 = Math.Min(image.GetHeight(), Mathf.CeilToInt(area.End.Y));

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, lit = 0;
            for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var c = image.GetPixel(x, y);
                if (c.R <= 0.5f || c.G >= 0.5f || c.B <= 0.5f)
                    continue;
                lit++;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }

            if (lit == 0)
                throw new AssertionException(
                    $"no glyph reached the frame inside the patch at ({x0},{y0})-({x1},{y1}); " +
                    $"zoom={ViewGeometry.Zoom}, pixel={ViewGeometry.PixelScreenSize}, " +
                    $"windowScale={ViewGeometry.WindowScale}");

            var width = maxX - minX + 1;
            var height = maxY - minY + 1;
            var wantHeight = PixelFont.Small.GlyphHeight * barScale;
            if (width != barScale || height != wantHeight || lit != width * height)
                throw new AssertionException(
                    $"a Small '|' at {barScale}x arrived as a {width}x{height} shape covering " +
                    $"{lit} pixels; expected a solid {barScale}x{wantHeight} block. A bar wider " +
                    "or taller than that is a doubled column or row, which means the frame was " +
                    $"resampled between the draw and the read. windowScale={ViewGeometry.WindowScale}, " +
                    $"pixelPerfect={ViewGeometry.PixelPerfect}");
        }
        finally
        {
            Settings.TextScale = restoreScale;
            View.MaxZoomFactor = restore;
            Canvas.Forget(Owner);
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// A tile in open sky well away from the spaceship: far enough that the fog lift and the view
    /// hold are doing real work, and empty enough that holding the avatar there does not fight the
    /// game's unstick logic.
    /// </summary>
    private static Vector2I Anchor()
    {
        if (Game.World?.Spaceship == null)
            Harness.Inapplicable("no spaceship to anchor a world position on");
        return Game.World.Spaceship.GlobalPosition.GlobalToTileposI() + new Vector2I(200, 0);
    }
}
