using Atomcraft;
using Godot;

namespace PixelArt;

/// <summary>
/// The one per-frame pass, shared by every canvas: clear each command list, draw the retained
/// marks, run the whole-frame passes, then walk the visible pixels once and hand each one to
/// every painter that is due.
///
/// <para><b>Once, not once per mod.</b> The pixel walk is the expensive part -- around 57,000
/// pixels at the zoomed-out default -- and it is identical for every consumer, so doing it here
/// means a second mod with a painter costs a delegate call per pixel rather than a second walk.
/// That is most of the reason this is a shared mod rather than a class each mod copies.</para>
/// </summary>
internal static class Renderer
{
    /// <summary>
    /// Whether this process has a display at all. Cached: asking the engine allocates a string,
    /// and it is asked every frame.
    /// </summary>
    private static bool? _headless;

    internal static bool Headless
    {
        get
        {
            _headless ??= DisplayServer.GetName() == "headless";
            return _headless.Value;
        }
    }

    private static bool _warnedHeadless;

    /// <summary>Set when the pass itself threw, as opposed to one canvas's pass. See <see cref="PixelArtApi.Faulted"/>.</summary>
    internal static bool Faulted { get; private set; }

    /// <summary>What went wrong, kept so a test can assert on it rather than grep the log.</summary>
    internal static Exception? Fault { get; private set; }

    /// <summary>How many pixels the painters were walked over on the last frame any of them ran.</summary>
    internal static int PixelsPaintedLastFrame { get; private set; }

    internal static void ClearFault()
    {
        Faulted = false;
        Fault = null;
    }

    /// <summary>
    /// Reused rather than reallocated: this runs every frame, and a render path that allocates
    /// per frame is a render path that stutters on collection.
    /// </summary>
    private static readonly List<(Canvas Canvas, object Pass)> Due = new();

    /// <summary>
    /// This frame's canvases, copied before any of them is drawn.
    ///
    /// <para><b>A snapshot, not the live list, and this is load-bearing.</b> Drawing a canvas runs
    /// a consumer's pass, and a pass may perfectly reasonably ask for a canvas -- a mod that
    /// creates one lazily, on the frame it first has something to say, is doing nothing wrong.
    /// <see cref="Canvas.For"/> then appends to the registry and re-sorts it, and iterating the
    /// live list over that throws "collection was modified" out of the shared pass, which
    /// switches drawing off for every mod at once for the rest of the session.</para>
    ///
    /// <para>Found by a demo mod that registers its canvases from inside its own pass, and only
    /// when its canvases had not already been created by something earlier: the failure is
    /// order-dependent, which is why it is worth a snapshot rather than a rule nobody would
    /// know to follow. A canvas registered mid-frame simply starts drawing on the next one.</para>
    /// </summary>
    private static readonly List<Canvas> ThisFrame = new();

    /// <summary>
    /// Rebuilds every canvas for this frame. Called from a postfix on the game's own per-frame
    /// gameplay update, so the camera everything is projected through is the one the world was
    /// just drawn with.
    /// </summary>
    internal static void Redraw()
    {
        if (Faulted)
            return;

        PixelsPaintedLastFrame = 0;

        if (Headless)
        {
            if (!_warnedHeadless && Canvas.All.Count > 0)
            {
                _warnedHeadless = true;
                Log.Info("idle: there is no display to draw on. Under the test harness, mark a " +
                         "test [GameTest(RequiresDisplay = true)] and run with --headful.");
            }
            return;
        }

        if (Canvas.All.Count == 0)
            return;

        // Off, or between worlds, or in a menu: the canvases are emptied rather than left
        // standing, so marks do not freeze on screen over the main menu.
        if (!PixelArtApi.Enabled || !Game.InSession || UI.CurrentPageId != PageId.Gameplay)
        {
            foreach (var canvas in Canvas.All)
                canvas.ClearCommands();
            return;
        }

        // Everything below projects through the camera, and the painter walk asks the game which
        // pixels it is rendering, which reads the avatar's tile. Both are missing for a few frames
        // around a world load, and skipping those frames is better than letting the backstop tear
        // every canvas down over them.
        if (!ViewGeometry.Ready || !GodotObject.IsInstanceValid(Avatars.LocalAvatar))
            return;

        var altHeld = Canvas.AltHeld;

        Due.Clear();
        ThisFrame.Clear();
        ThisFrame.AddRange(Canvas.All);

        foreach (var canvas in ThisFrame)
        {
            canvas.PixelsPaintedLastFrame = 0;
            // Asked before the canvas is built, so a mod that takes a canvas in its Initialize and
            // has nothing to say this frame costs nothing and leaves no empty layer in the tree.
            // A canvas that had marks a moment ago is still emptied: ClearCommands is a no-op on
            // one that was never built.
            if (!canvas.HasWork)
            {
                canvas.ClearCommands();
                continue;
            }
            if (!canvas.EnsureCanvas())
                continue;
            canvas.ClearCommands();
            canvas.DrawMarksAndPasses(altHeld);
            canvas.CollectPainters(altHeld, Due);
        }

        if (Due.Count > 0)
            RunPainters();
    }

    private static void RunPainters()
    {
        var field = Simulation.CurrentState?.Field;
        var visible = ViewGeometry.VisibleTiles();
        if (field == null || visible is not { } pixels || pixels.width <= 0 || pixels.height <= 0)
            return;

        var size = ViewGeometry.PixelScreenSize;

        // Projected once for the corner and stepped from there, rather than through
        // ViewGeometry.ScreenRectOf per pixel: that asks the engine for the camera and the
        // viewport on every call, and this loop runs once per visible pixel per frame.
        var corner = ViewGeometry.WorldToScreen(
            new Vector2(pixels.min.X * ViewGeometry.TileSize, pixels.min.Y * ViewGeometry.TileSize));

        for (var y = pixels.min.Y; y < pixels.max.Y; y++)
        for (var x = pixels.min.X; x < pixels.max.X; x++)
        {
            var tile = new Vector2I(x, y);
            var screen = new Rect2(corner.X + (x - pixels.min.X) * size,
                                   corner.Y + (y - pixels.min.Y) * size,
                                   size, size);
            var material = field.Get(x, y);

            foreach (var (canvas, pass) in Due)
            {
                if (!canvas.Paint(pass, tile, material, screen))
                    // The painter has been removed and its canvas faulted. Abandoning the frame
                    // rather than carrying on is deliberate: the ones already drawn keep their
                    // partial frame for a sixtieth of a second, and the next frame is clean.
                    return;
                canvas.PixelsPaintedLastFrame++;
            }
            PixelsPaintedLastFrame++;
        }
    }

    /// <summary>
    /// Runs the pass with the frame's failure handling around it.
    ///
    /// <para>Separate from <see cref="Redraw"/> so a test can drive the pass and see what it
    /// threw, while the game only ever gets the caught version. A canvas's own pass failing is
    /// handled where it happens; this is the backstop for everything else, and it switches the
    /// whole mod off rather than logging once per frame forever.</para>
    /// </summary>
    internal static void RedrawGuarded()
    {
        try
        {
            Redraw();
        }
        catch (Exception e)
        {
            Faulted = true;
            Fault = e;
            foreach (var canvas in Canvas.All)
                canvas.ClearCommands();
            Log.Error("the drawing pass threw and has been switched off for this session. The " +
                      "game is unaffected; nothing further will be drawn by any canvas.");
            Log.Error(e.ToString());
        }
    }
}
