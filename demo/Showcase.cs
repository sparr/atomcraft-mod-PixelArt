using Atomcraft;
using Godot;

namespace PixelArt.Demo;

/// <summary>
/// The demonstration itself: three canvases, where they are drawn, and when that is rebuilt.
///
/// <para><b>Where it appears.</b> The catalogue is placed in the middle of whatever the player
/// can see the first frame a world is on screen, nudged left so the spaceship stays clear, so
/// entering a world puts it on screen without anyone having to go looking. After that it stays on
/// the pixels it was placed on -- it is part of the world, not part of the HUD, and panning away
/// from it is meant to feel like walking away from a signpost.</para>
///
/// <para><b>It moves only when you walk away from it</b>, by more than <see cref="FollowMargin"/>
/// pixels past its edge, and it is the <i>avatar</i> that is measured rather than the view. An
/// earlier version placed it again whenever the middle of it left the screen, which meant zooming
/// in moved it: the view shrinks around a camera that has not gone anywhere, the middle falls
/// outside it, and the catalogue jumped to the new centre while the player was only trying to
/// look more closely at it. Zoom changes what you can see, not where you are, so it is no longer
/// consulted at all.</para>
///
/// <para><b>Three canvases, on purpose.</b> One mod does not need three; this one has them so the
/// layering is visible (<see cref="Under"/> draws beneath <see cref="Marks"/>) and so the live
/// half can be rebuilt every frame while the catalogue half is retained and rebuilt only when it
/// moves. That split is the thing most worth copying out of this mod.</para>
///
/// <para>Public, because the project's test mod drives it: a demo nobody checks is a demo that
/// quietly stops matching the library.</para>
/// </summary>
public static class Showcase
{
    /// <summary>The retained catalogue: every mark that does not change from frame to frame.</summary>
    public const string Marks = "PixelArt.Demo";

    /// <summary>Beneath <see cref="Marks"/>, so that "two canvases, and this one is underneath" is a thing you can see.</summary>
    public const string Under = "PixelArt.Demo.under";

    /// <summary>The per-frame half: readouts, the animation, the cursor, and the Alt painter.</summary>
    public const string Live = "PixelArt.Demo.live";

    private const int UnderLayer = 120;
    private const int LiveLayer = 127;

    /// <summary>
    /// How much room the catalogue takes, in pixels. It fits the default view with room to spare:
    /// at the zoom a world starts at, a pixel is about 6 screen pixels, so this is roughly 480 by
    /// 700 of the frame's 1600 by 900, and the default view is 265 by 149 pixels.
    /// </summary>
    public const int Width = 80;

    /// <inheritdoc cref="Width"/>
    public const int Height = 132;

    /// <summary>
    /// How far left of centre the catalogue sits, so that the spaceship and the avatar -- which
    /// are what the camera is pointed at -- are not underneath it.
    /// </summary>
    private const int LeftOfCentre = 22;

    /// <summary>
    /// How far past the edge of the catalogue the avatar has to get before it is placed again, in
    /// world pixels.
    ///
    /// <para>Generous on purpose. A signpost that follows you the moment you step off it is worse
    /// than one you have to walk back to, and staying on the pixels it was put on is the whole
    /// point of a retained mark. 120 is about half a default screen's width beyond the edge, which
    /// is further than anyone drifts while reading it.</para>
    /// </summary>
    private const int FollowMargin = 120;

    /// <summary>Where the catalogue's top left corner is, or null before a world has been on screen.</summary>
    public static Vector2I? Anchor { get; private set; }

    /// <summary>The block of pixels the catalogue occupies. Empty before it has been placed.</summary>
    public static RectInt Area =>
        Anchor is { } a ? new RectInt(a.X, a.Y, Width, Height) : new RectInt(0, 0, 0, 0);

    /// <summary>Whether the callbacks are registered. False after the harness has swept the canvases.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Frames the live pass has run, which is also what the animation is driven from.</summary>
    public static int Frames { get; private set; }

    /// <summary>
    /// Whether the demo closes the game's own introduction windows on its way in: the welcome
    /// panel a new world opens with, and the tutorial goal list down the right-hand side.
    ///
    /// <para>On by default, because both of them sit exactly where the catalogue does and a demo
    /// nobody can see is not a demo. This is the one thing in this mod that is not drawing, and
    /// the only reason it belongs here rather than in the library: PixelArt draws over the HUD
    /// and does not touch it.</para>
    ///
    /// <para><b>Nothing is made permanent.</b> The window is closed the way its own button closes
    /// it, and the tutorial goal is cleared the way the game's own "show tutorials" setting clears
    /// it -- neither writes to your profile or your settings. Uninstall the demo, or set this
    /// false, and the next new world introduces itself normally.</para>
    /// </summary>
    public static bool DismissIntroWindows { get; set; } = true;

    /// <summary>How many times each has been dismissed, for the log and for a test.</summary>
    public static int WelcomesDismissed { get; private set; }

    /// <inheritdoc cref="WelcomesDismissed"/>
    public static int TutorialsDismissed { get; private set; }

    private static bool _dismissFaulted;

    /// <summary>How many times the catalogue has been placed. Once per world, unless you walk away from it.</summary>
    public static int Placements { get; private set; }

    private static World? _world;
    private static Rows _rows;

    /// <summary>The layout the catalogue was last built with, for anything that wants to measure it.</summary>
    public static Rows LastRows => _rows;

    /// <summary>
    /// Registers the live pass and the Alt painter. Idempotent, and safe to call with no game
    /// loaded.
    ///
    /// <para>Also what puts the mod back after a test run: the harness empties every canvas
    /// between tests, deliberately, so that a mod's own drawing cannot end up in a screenshot of
    /// somebody else's test.</para>
    /// </summary>
    public static void Install()
    {
        _dismissFaulted = false;
        var live = Canvas.For(Live, LiveLayer);
        live.SetPass("demo", Frame);
        live.SetPainter("match", LiveSection.PaintMatchingMaterial, DrawWhen.AltHeld);
        Installed = true;
    }

    /// <summary>Removes everything this mod draws, leaving the canvases registered and empty.</summary>
    public static void Uninstall()
    {
        Canvas.For(Live, LiveLayer).Reset();
        Canvas.For(Marks).Clear();
        Canvas.For(Under, UnderLayer).Clear();
        Anchor = null;
        Installed = false;
    }

    /// <summary>
    /// Puts the catalogue back where it would be for a player entering a world now. The next
    /// frame places it again.
    /// </summary>
    public static void Forget()
    {
        Anchor = null;
        _world = null;
    }

    /// <summary>
    /// Draws the catalogue at a chosen corner and returns the layout it came to.
    ///
    /// <para>What <see cref="Place"/> calls, exposed because a mark is recorded whether or not
    /// there is a display: the layout can be laid out and measured by a headless test, which is
    /// where a row that outgrew its reservation should be caught rather than in a screenshot.</para>
    /// </summary>
    public static Rows PlaceAt(Vector2I anchor)
    {
        Anchor = anchor;
        Placements++;
        _rows = Catalogue.Build(Canvas.For(Marks), Canvas.For(Under, UnderLayer), anchor);
        return _rows;
    }

    /// <summary>
    /// The per-frame pass, run by PixelArt while a world is on screen.
    ///
    /// <para>No try/catch, on purpose. A pass that throws is removed by the library and its canvas
    /// is faulted, with one line in <c>godot.log</c> naming it, precisely so that every consumer
    /// does not have to write this guard for itself -- and so that a bug here cannot take another
    /// mod's overlay down with it.</para>
    /// </summary>
    private static void Frame(Canvas live)
    {
        Frames++;
        DismissIntro();
        Place();
        if (Anchor is { } anchor)
            LiveSection.Draw(live, anchor, _rows);
    }

    /// <summary>
    /// Closes the two things the game puts in front of a new world: the welcome panel, and the
    /// tutorial goal list on the right.
    ///
    /// <para>Both through the game's own public methods, and both the way the game itself does
    /// it. <c>Gameplay.SetCurrentWindowId(WindowId.None)</c> is what the panel's own close button
    /// runs, so the window gets its <c>OnClose</c> and the simulation is unpaused properly rather
    /// than having a <c>Visible</c> flag flipped behind its back. <c>TutorialGoal.SetGoal(null)</c>
    /// is what <c>SaveData_Device.ApplySettings</c> runs when a player turns tutorials off; the
    /// private <c>EndTutorial</c> the cancel button is wired to would do the same and also write
    /// <c>TutorialComplete</c> into the profile, which is not this mod's business.</para>
    ///
    /// <para>Guarded, and the one place in this mod that is. The rule everywhere else is that a
    /// pass needs no try/catch because the library removes one that throws -- but what the library
    /// does is switch that canvas off for the session, which is the right answer for drawing that
    /// failed and the wrong one here: a game update that moved this would cost the whole
    /// demonstration rather than the two lines that stopped working. So this reports once and
    /// gives up, and the drawing carries on without it.</para>
    /// </summary>
    private static void DismissIntro()
    {
        if (!DismissIntroWindows || _dismissFaulted)
            return;

        try
        {
            // Only this one window. Closing whatever happens to be open every frame would stop
            // the player opening their inventory, which is a demo mod deciding it owns the UI.
            if (Gameplay.CurrentWindowId == WindowId.WelcomeWindow)
            {
                Gameplay.SetCurrentWindowId(WindowId.None);
                WelcomesDismissed++;
                Log.Info("closed the welcome window; it sits where the catalogue goes");
            }

            // Cleared whenever the game sets one, not just the first: the goal list comes back
            // each time a step completes, and while this mod is installed the intent is a clear
            // screen. Reading the goal first keeps this to one call per goal rather than one per
            // frame.
            if (TutorialGoal.CurrentGoal != null)
            {
                TutorialGoal.SetGoal(null);
                TutorialsDismissed++;
                Log.Info("cleared the tutorial goal list; nothing was written to your profile");
            }
        }
        catch (Exception e)
        {
            _dismissFaulted = true;
            Log.Warn("could not dismiss the game's introduction windows, and will stop trying. " +
                     $"The catalogue is still drawn, underneath them: {e.Message}");
        }
    }

    /// <summary>
    /// Places the catalogue if it has never been placed, if the world changed, or if the player
    /// has wandered far enough that its middle is off screen.
    /// </summary>
    private static void Place()
    {
        if (!ReferenceEquals(_world, Game.World))
        {
            _world = Game.World;
            Anchor = null;
        }

        if (Anchor is { } placed && !HasWalkedAway(placed))
            return;

        if (ViewGeometry.VisibleTiles() is not { } visible || visible.width <= 0 || visible.height <= 0)
            return;

        // Nudged left only when there is room for the nudge; on a small window the catalogue is
        // better centred than pushed half off the screen.
        var shift = visible.width >= Width + 2 * LeftOfCentre ? LeftOfCentre : 0;
        var corner = new Vector2I(visible.X + visible.width / 2 - Width / 2 - shift,
                                  visible.Y + visible.height / 2 - Height / 2);
        var rows = PlaceAt(corner);
        Log.Info($"catalogue #{Placements} drawn at {corner}, {Width}x{rows.Used} pixels " +
                 $"of the {Width}x{Height} reserved");
    }

    /// <summary>
    /// Whether the avatar has left the catalogue and its margin behind.
    ///
    /// <para>Measured against the avatar and not against the camera or the view. The camera lags
    /// the avatar and leads it while it moves, and the view changes size with the zoom, so either
    /// would move the catalogue for reasons that are not "the player went somewhere else". With no
    /// avatar to measure -- the frames around a world load -- the answer is no, and the catalogue
    /// stays where it is.</para>
    /// </summary>
    private static bool HasWalkedAway(Vector2I anchor)
    {
        if (!GodotObject.IsInstanceValid(Avatars.LocalAvatar))
            return false;

        var here = Avatars.LocalAvatar.GlobalPosition.GlobalToTileposI();
        return here.X < anchor.X - FollowMargin
            || here.Y < anchor.Y - FollowMargin
            || here.X > anchor.X + Width + FollowMargin
            || here.Y > anchor.Y + Height + FollowMargin;
    }

    /// <summary>How far outside the catalogue the avatar is, in world pixels; 0 while it is inside.</summary>
    public static int AvatarDistanceOutside
    {
        get
        {
            if (Anchor is not { } a || !GodotObject.IsInstanceValid(Avatars.LocalAvatar))
                return 0;
            var here = Avatars.LocalAvatar.GlobalPosition.GlobalToTileposI();
            var dx = Math.Max(0, Math.Max(a.X - here.X, here.X - (a.X + Width)));
            var dy = Math.Max(0, Math.Max(a.Y - here.Y, here.Y - (a.Y + Height)));
            return Math.Max(dx, dy);
        }
    }
}

/// <summary>
/// Where the rows the live half draws into ended up, handed back by <see cref="Catalogue.Build"/>
/// rather than restated as constants in two files, which is how a layout drifts apart.
/// </summary>
public readonly struct Rows
{
    internal Rows(int live, int alt, int text, int used)
    {
        Live = live;
        Alt = alt;
        Text = text;
        Used = used;
    }

    /// <summary>Top of the live row, in pixels below the anchor.</summary>
    public int Live { get; }

    /// <summary>Top of the Alt row, in pixels below the anchor.</summary>
    public int Alt { get; }

    /// <summary>Top of the text-on-a-pixel row, in pixels below the anchor.</summary>
    public int Text { get; }

    /// <summary>
    /// How tall the catalogue actually came out, measured while laying it out rather than
    /// restated as a constant. <see cref="Showcase.Height"/> is what the placement reserves for
    /// it, and the test mod checks that this still fits inside that.
    /// </summary>
    public int Used { get; }
}
