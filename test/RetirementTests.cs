using Atomcraft;
using Atomcraft.TestHarness;

namespace PixelArt.Test;

/// <summary>
/// Is the game still the shape this mod works around?
///
/// <para><b>A failure here is good news</b>, and it is a different question from whether this mod
/// is correct, which is why these do not run in the everyday loop: <c>./run-tests.sh
/// --retirement</c> asks this one. Each test's doc comment says what to delete when it fails,
/// because a year from now the person reading the red result will not be the person who wrote the
/// workaround.</para>
///
/// <para>Both of these are about the same thing: this mod draws on frames the game does not
/// expect anybody to be asking questions on. A per-frame hook runs while a world is loading and
/// while the player is in a menu, and the game's own helpers are written for the one place the
/// game calls them from.</para>
/// </summary>
public static class RetirementTests
{
    /// <summary>
    /// The game's own "which tile is the mouse over" helper still dereferences <c>Game.World</c>
    /// with no guard, so it cannot be called on a frame where there is no world.
    ///
    /// <para><b>When this fails:</b> <c>ViewGeometry.MouseTile</c> can stop being a hand-rolled
    /// copy of the game's two lines and become a call to <c>Utils.GetTileMousePosition</c>. Keep
    /// the nullable return either way: a pass still has to be able to tell "no world" from
    /// "pixel (0,0)".</para>
    ///
    /// <para><b>Why the world is nulled rather than the test run without one.</b> The harness
    /// loads a world header for its region tests, so <c>Game.World</c> is set for the whole run
    /// and the state this is about -- the frames around a world load, which a render hook runs on
    /// -- never occurs on its own. Nulling the field reproduces it exactly. It is safe because
    /// this test is not frame-driven: no frame elapses between the two lines below, so no game
    /// code ever sees the null, and the <c>finally</c> puts it back whatever happens.</para>
    /// </summary>
    [GameTest]
    public static void TheGamesMouseTileHelperStillThrowsWithNoWorld()
    {
        var world = Game.World;
        try
        {
            Game.World = null!;

            // The mod's own answer first, since it is the thing the workaround exists for: no
            // world means no answer, rather than an exception out of a per-frame path.
            if (ViewGeometry.MouseTile() != null)
                throw new AssertionException(
                    "ViewGeometry.MouseTile answered with no world loaded, so its guard is not " +
                    "the guard this test is about");

            // The catch is wrapped tightly around the game's own call on purpose: catching
            // around the line above as well would turn a NullReferenceException out of the mod's
            // guard into a pass, which is the one outcome this must never report.
            try
            {
                Utils.GetTileMousePosition();
            }
            catch (NullReferenceException)
            {
                return;
            }
        }
        finally
        {
            Game.World = world;
        }

        throw new AssertionException(
            "Utils.GetTileMousePosition no longer throws with no world loaded. If it now answers " +
            "safely, ViewGeometry.MouseTile can call it instead of reimplementing it. See this " +
            "test's doc comment.");
    }

    /// <summary>
    /// The game still draws at a fixed render-target size and rescales the finished frame to the
    /// window, so a one-pixel feature reaches the window intact only when the window is a whole
    /// multiple of the target.
    ///
    /// <para><b>When this fails:</b> the game has started drawing at the window's own resolution,
    /// and <see cref="ViewGeometry.WindowScale"/> and <see cref="ViewGeometry.PixelPerfect"/> are
    /// answering a question nobody needs to ask any more. Delete both, and the paragraph in the
    /// README that explains why a bitmap font can arrive lopsided through no fault of the font.
    /// </para>
    ///
    /// <para>Asserted on the viewport rather than on a screenshot, so it runs headless: the claim
    /// is about the size the game composes at, not about what a particular window does with
    /// it.</para>
    /// </summary>
    [GameTest]
    public static void TheFrameIsStillComposedAtAFixedSize()
    {
        const int width = 1600, height = 900;

        var viewport = Game.CanvasLayer?.GetViewport()?.GetVisibleRect().Size;
        if (viewport == null)
            Harness.Inapplicable("no viewport yet, so there is nothing to measure");

        if ((int)viewport!.Value.X == width && (int)viewport.Value.Y == height)
            return;

        throw new AssertionException(
            $"the game composes at {viewport.Value}, not the {width}x{height} render target this " +
            "mod's exactness is stated against. If the frame is now drawn at the window's own " +
            "resolution, WindowScale and PixelPerfect have nothing left to warn about. Note that " +
            "a mod which resizes the render target (ActualResolution does) produces this same " +
            "result, in which case this suite is being run in company and the failure is that " +
            "mod working, not the game changing.");
    }
}
