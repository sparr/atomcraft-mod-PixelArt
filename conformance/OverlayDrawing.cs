using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;
using Session = Atomcraft.TestHarness.Session;

namespace PixelArtConformance;

/// <summary>
/// Does the game still let a mod draw over the world at all?
///
/// <para><b>This suite names no mod.</b> It states properties the game either has or does not, and
/// can be installed alongside any mod that draws an overlay, alongside a rival, or alongside none.
/// Nothing here references PixelArt, or names it, or depends on its assembly.</para>
///
/// <para><b>Why these properties.</b> Every overlay mod, whatever it draws, rests on the same two
/// assumptions. First, that a <c>CanvasLayer</c> added above the game's own reaches the rendered
/// frame, drawn after the world and over the top of it -- the alternative, writing into the
/// texture the game builds the world from, puts marks under the lighting and the fog, which is
/// where they are least readable. Second, that where a pixel is on screen can be computed from the
/// camera, and that the answer is the same one the game gets when it decides which pixel the mouse
/// is over. Neither is documented anywhere; both are how the game happens to be put together.</para>
///
/// <para>Properties a mod <i>depends on</i> rather than ones it fixes, so a failure here is bad
/// news -- which is what separates this suite from a retirement suite. What it would mean is that
/// overlay mods stopped being possible in the shape they are all written in, and the failure would
/// otherwise reach each of their authors as "my marks vanished" with no shared explanation.</para>
/// </summary>
public static class OverlayDrawing
{
    /// <summary>Tiles are 8 world units on a side; see <c>Utils.TileposToGlobal</c>.</summary>
    private const float TileSize = 8f;

    /// <summary>
    /// A canvas layer above the game's own reaches the rendered frame, over the world.
    ///
    /// <para>Built from nothing but Godot and the scene tree: a <c>CanvasLayer</c> at the top of
    /// the range, a <c>RenderingServer</c> canvas item under it, one rectangle in the middle of
    /// the screen, and then the render target is read back and the pixel is checked. The colour is
    /// one the game never draws, and the rectangle is large, so nothing about the world underneath
    /// can produce either a pass or a failure.</para>
    ///
    /// <para><b>Why the server API rather than a <c>Control</c> with <c>_Draw</c>.</b> A mod is
    /// compiled without Godot's source generators, so a <c>CanvasItem</c> subclass declared in a
    /// mod never has its <c>_Draw</c> called at all. That is a property of how mods are built
    /// rather than of the game, so it is not what this asserts -- but it is why every overlay mod
    /// draws this way, and why this suite tests this path.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator ACanvasLayerAboveTheGameReachesTheFrame()
    {
        yield return Session.Enter("flat");

        var root = Game.Instance?.GetTree()?.Root;
        if (root == null)
            Harness.Inapplicable("no scene tree to add a layer to");

        // 128 is as high as Godot's canvas layers go. Deliberately the very top: the claim is that
        // a mod can draw over everything the game draws, the HUD included.
        var layer = new CanvasLayer { Name = "ConformanceProbe", Layer = 128 };
        root!.AddChild(layer);
        var item = RenderingServer.CanvasItemCreate();

        var ink = new Color(0f, 1f, 1f);          // cyan: nothing in the world is this
        try
        {
            RenderingServer.CanvasItemSetParent(item, layer.GetCanvas());

            var viewport = Game.CanvasLayer.GetViewport().GetVisibleRect().Size;
            var box = new Rect2(viewport * 0.5f - new Vector2(40, 40), new Vector2(80, 80));
            RenderingServer.CanvasItemAddRect(item, box, ink);

            yield return Wait.Frames(3);

            var image = Game.CanvasLayer.GetViewport().GetTexture()?.GetImage();
            if (image == null || image.GetWidth() == 0)
                Harness.Inapplicable("the viewport cannot be read back on this renderer");

            var x = Mathf.Clamp((int)box.GetCenter().X, 0, image!.GetWidth() - 1);
            var y = Mathf.Clamp((int)box.GetCenter().Y, 0, image.GetHeight() - 1);
            var got = image.GetPixel(x, y);

            if (Math.Abs(got.R - ink.R) + Math.Abs(got.G - ink.G) + Math.Abs(got.B - ink.B) > 0.1f)
                throw new AssertionException(
                    $"a rectangle drawn on a canvas layer above the game shows as {got} at " +
                    $"({x},{y}), expected {ink}. A mod can no longer draw over the world from a " +
                    "canvas layer, which is how every overlay mod is built.");
        }
        finally
        {
            if (item.IsValid)
                RenderingServer.FreeRid(item);
            if (GodotObject.IsInstanceValid(layer))
                layer.QueueFree();
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// Where a pixel is on screen can still be computed from the camera, and the game agrees.
    ///
    /// <para>The forward direction is the projection every overlay mod does for itself:
    /// <c>(world - camera) * zoom + viewport/2</c>. The backward direction is the game's own
    /// <c>Utils.ScreenPositionToWorldPosition</c>, which is what it uses to decide which pixel the
    /// mouse is over. If those two ever stop being inverses, every mark every overlay mod draws
    /// lands next to the pixel it names, and each author finds it separately.</para>
    ///
    /// <para>Checked across the view rather than at one point, because an error in the camera term
    /// vanishes at the center of the screen, which is exactly where a single-point test would put
    /// it.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheGamesScreenMappingIsTheCamerasProjection()
    {
        yield return Session.Enter("flat");

        if (Game.World?.Spaceship == null)
            Harness.Inapplicable("no spaceship to anchor a world position on");
        var center = Game.World.Spaceship.GlobalPosition.GlobalToTileposI() + new Vector2I(200, 0);

        // The harness's View is used only to put the camera somewhere known and hold it there;
        // nothing below asks it where anything is.
        yield return View.LookAt(center);

        var camera = Client.FollowCam
                     ?? throw new AssertionException("no camera; is a session active?");
        var viewport = Game.CanvasLayer.GetViewport().GetVisibleRect().Size;

        foreach (var offset in new[]
                 {
                     new Vector2I(0, 0), new Vector2I(7, 4), new Vector2I(-7, -4),
                     new Vector2I(24, -15), new Vector2I(-24, 15),
                 })
        {
            var tile = center + offset;

            // The projection, done by hand: world center of the pixel, relative to the camera,
            // scaled by the zoom, from the middle of the frame.
            var world = new Vector2(tile.X * TileSize + TileSize / 2f,
                                    tile.Y * TileSize + TileSize / 2f);
            var screen = (world - camera.GlobalPosition) * camera.Zoom + viewport * 0.5f;

            var back = screen.ScreenPositionToWorldPosition().GlobalToTileposI();
            if (back != tile)
                throw new AssertionException(
                    $"projecting pixel {tile} through the camera puts it at {screen}, which the " +
                    $"game reads back as pixel {back}. The game's screen-to-world mapping is no " +
                    "longer the inverse of the camera's projection, so every overlay mod's marks " +
                    "are drawn in the wrong place.");
        }

        yield return Session.Leave();
    }
}
