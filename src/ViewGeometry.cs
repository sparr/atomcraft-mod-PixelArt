using Atomcraft;
using Godot;

namespace PixelArt;

/// <summary>
/// Where a world pixel is on screen right now, and which pixels are on screen at all.
///
/// <para>The inverse of the game's own <c>Utils.ScreenPositionToWorldPosition</c>, so it agrees
/// with the mapping the game uses to decide which pixel the mouse is over. Everything a mod
/// needs to put a mark on a pixel is here, and nothing else: no camera control, no avatar, no
/// fog. Moving the view is a different mod's business (see Zoooom for the zoom range, and the
/// TestHarness's <c>View</c> for pinning the camera in a test).</para>
///
/// <para><b>Two things are called a pixel, and this mod keeps them apart.</b> The game simulates
/// a world of discrete <b>pixels</b>, one material each, and that is what "pixel" means
/// everywhere in this API: <see cref="PixelScreenSize"/> is how big one of them is on screen, and
/// a painter is handed one at a time. A pixel of the screen is always called a <b>screen
/// pixel</b>. A pixel's position is a <b>tile</b> coordinate, which is the game's own word for it
/// -- <c>Utils.TileposToGlobal</c>, <c>GlobalToTileposI</c> -- so <see cref="TileAt"/> and
/// <see cref="VisibleTiles"/> are named for the coordinate and everything else for the thing at
/// it.</para>
///
/// <para><b>These are render-target pixels, not window pixels.</b> The project sets
/// <c>display/window/stretch/mode = viewport</c> over a fixed 1600x900 target, so the game does
/// not draw at the window's resolution at all: it draws at 1600x900 and the engine rescales the
/// finished frame to the window. Every coordinate here is a pixel of that target, because that
/// is the space the game itself computes in, and anything drawn from a <c>CanvasLayer</c> is
/// inside the frame and crosses the same rescale. See <see cref="WindowScale"/> and
/// <see cref="PixelPerfect"/> for what that costs and what fixes it.</para>
///
/// <para><b>Nothing here throws.</b> Every member answers with a zero, a sentinel or a null
/// when there is no camera, no viewport or no world. A mod renders on a frame whether or not a
/// world is loaded, and Godot logs an exception out of a per-frame path on every occurrence
/// with no backpressure at all: one throwing hook made a 1.3 million line <c>godot.log</c> in
/// ninety seconds. Ask <see cref="Ready"/> when the difference matters.</para>
/// </summary>
public static class ViewGeometry
{
    /// <summary>Tiles are 8 world units on a side; see <c>Utils.TileposToGlobal</c>.</summary>
    public const float TileSize = 8f;

    /// <summary>
    /// Whether there is a camera, a viewport and a simulation field to project against, which
    /// is what every other member here needs. False for a few frames around a world load as
    /// well as outside a session.
    /// </summary>
    public static bool Ready =>
        Client.FollowCam != null &&
        Game.CanvasLayer != null &&
        Simulation.CurrentState?.Field != null;

    /// <summary>Where the camera is zoomed right now, or 0 with no camera. A world pixel covers <c>8 * Zoom</c> screen pixels.</summary>
    public static float Zoom => Client.FollowCam is { } cam ? cam.Zoom.X : 0f;

    /// <summary>How many screen pixels one simulation pixel covers right now, or 0 with no camera.</summary>
    public static float PixelScreenSize => TileSize * Zoom;

    /// <summary>The size of the frame the game draws into, or <see cref="Vector2.Zero"/> before there is one.</summary>
    public static Vector2 ViewportSize =>
        Game.CanvasLayer?.GetViewport().GetVisibleRect().Size ?? Vector2.Zero;

    /// <summary>
    /// A world position in render-target pixels, or <see cref="Vector2.Zero"/> when there is
    /// nothing to project through. The building block the rest of this class is built from.
    /// </summary>
    public static Vector2 WorldToScreen(Vector2 world)
    {
        var cam = Client.FollowCam;
        if (cam == null)
            return Vector2.Zero;
        var viewport = ViewportSize;
        if (viewport == Vector2.Zero)
            return Vector2.Zero;
        return (world - cam.GlobalPosition) * cam.Zoom + viewport * 0.5f;
    }

    /// <summary>
    /// Where a pixel's <i>center</i> is on screen, for anything that wants to draw at a pixel
    /// rather than over it.
    ///
    /// <para>A pixel off screen still gets an answer, outside the viewport rect. Ask
    /// <see cref="IsVisible"/> if that matters.</para>
    /// </summary>
    public static Vector2 ScreenOf(Vector2I tile) =>
        WorldToScreen(new Vector2(tile.X * TileSize + TileSize / 2f,
                                  tile.Y * TileSize + TileSize / 2f));

    /// <summary>The rectangle a pixel covers on screen.</summary>
    public static Rect2 ScreenRectOf(Vector2I tile)
    {
        var topLeft = WorldToScreen(new Vector2(tile.X * TileSize, tile.Y * TileSize));
        var pixel = PixelScreenSize;
        return new Rect2(topLeft, new Vector2(pixel, pixel));
    }

    /// <summary>The rectangle a block of pixels covers on screen, as one rectangle rather than many.</summary>
    public static Rect2 ScreenRectOf(RectInt tiles)
    {
        var topLeft = WorldToScreen(new Vector2(tiles.min.X * TileSize, tiles.min.Y * TileSize));
        var pixel = PixelScreenSize;
        return new Rect2(topLeft, new Vector2(tiles.width * pixel, tiles.height * pixel));
    }

    /// <summary>
    /// Which pixel a render-target position falls in: the game's own inverse, so a coordinate
    /// can be round-tripped through <see cref="ScreenOf"/> and come back to the pixel it started
    /// at.
    /// </summary>
    public static Vector2I TileAt(Vector2 screen) =>
        screen.ScreenPositionToWorldPosition().GlobalToTileposI();

    /// <summary>
    /// Which pixel the mouse is over, or null when there is no world to ask about.
    ///
    /// <para>The game's own <c>Utils.GetTileMousePosition</c> dereferences <c>Game.World</c>
    /// with no guard, which is fine where the game calls it -- inside gameplay -- and not fine
    /// on a render hook that also runs while a world is loading.</para>
    /// </summary>
    public static Vector2I? MouseTile()
    {
        if (Game.World == null || Client.FollowCam == null)
            return null;
        return Game.World.GetGlobalMousePosition().GlobalToTileposI();
    }

    /// <summary>Whether a pixel is inside <see cref="VisibleTiles"/> right now. False when that cannot be answered.</summary>
    public static bool IsVisible(Vector2I tile) => VisibleTiles() is { } visible && visible.Contains(tile);

    /// <summary>
    /// Every pixel the player can actually see right now, edge pixels included even when only
    /// part of one is on screen, or null when that cannot be answered.
    ///
    /// <para>Three things bound it, and all three matter. The viewport and the zoom say how
    /// much world fits on screen. The game renders the world from a fixed-size window of pixels
    /// around the avatar, and outside that window there is nothing drawn to mark. And the
    /// simulation field has edges, past which there are no pixels at all. The intersection is
    /// what is on screen and real, which is what a per-pixel pass wants to walk.</para>
    /// </summary>
    public static RectInt? VisibleTiles()
    {
        var cam = Client.FollowCam;
        var field = Simulation.CurrentState?.Field;
        if (cam == null || field == null)
            return null;
        var viewport = ViewportSize;
        if (viewport == Vector2.Zero)
            return null;
        // The rendered window is computed from the avatar's tile, so without one the game
        // cannot say which pixels it is drawing.
        if (!GodotObject.IsInstanceValid(Avatars.LocalAvatar))
            return null;

        var halfWorld = viewport * 0.5f / cam.Zoom;
        var min = (cam.GlobalPosition - halfWorld).GlobalToTileposI();
        var max = (cam.GlobalPosition + halfWorld).GlobalToTileposI();
        var onScreen = new RectInt(min.X, min.Y, max.X - min.X + 1, max.Y - min.Y + 1);

        var rendered = Gameplay.GetWindowRectAndOriginForRenderingWorld().Item1;
        var world = new RectInt(0, 0, field.Width, field.Height);

        return onScreen.Intersection(rendered).Intersection(world);
    }

    /// <summary>The block of pixels within <paramref name="radius"/> of a tile, inclusive. A cursor-sized pass wants this.</summary>
    public static RectInt Around(Vector2I tile, int radius)
    {
        var side = 2 * Math.Max(0, radius) + 1;
        return new RectInt(tile.X - radius, tile.Y - radius, side, side);
    }

    // ------------------------------------------------------- the stretched viewport

    /// <summary>
    /// How many window pixels the engine paints for each render-target pixel, once the whole
    /// finished frame has been scaled to the window.
    ///
    /// <para>The default window is 1280x720 over a 1600x900 target, so this is normally 0.8:
    /// the final blit resamples the entire frame, the game's own art included, and a
    /// one-pixel-wide feature survives it only by luck. That is invisible in the world art,
    /// which has no one-pixel features to lose, and glaring in a bitmap font, which arrives with
    /// rows and columns doubled or dropped and reads as a broken font rather than as a scaled
    /// frame.</para>
    /// </summary>
    public static Vector2 WindowScale
    {
        get
        {
            var viewport = ViewportSize;
            if (viewport.X <= 0f || viewport.Y <= 0f)
                return Vector2.One;
            return (Vector2)DisplayServer.WindowGetSize() / viewport;
        }
    }

    /// <summary>
    /// Whether what is drawn here survives to the window unresampled: true when the window is
    /// the render target's size or a whole multiple of it.
    ///
    /// <para>False does not mean anything drawn is wrong, and nothing in this mod can make it
    /// true: the rescale happens to the finished frame, after everything inside it has been
    /// drawn. The remedy is a window the frame does not need rescaling to fill, which is a
    /// display concern rather than a drawing one -- see the ActualResolution mod, which resizes
    /// the render target to the window instead.</para>
    /// </summary>
    public static bool PixelPerfect
    {
        get
        {
            var s = WindowScale;
            return Mathf.IsEqualApprox(s.X, s.Y)
                && s.X >= 1f
                && Mathf.IsEqualApprox(s.X, Mathf.Round(s.X));
        }
    }
}
