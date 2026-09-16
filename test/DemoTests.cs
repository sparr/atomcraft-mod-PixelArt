using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;
using PixelArt.Demo;
using Session = Atomcraft.TestHarness.Session;

namespace PixelArt.Test;

/// <summary>
/// The demo mod shows a player something, and shows it where they are looking.
///
/// <para><b>Why the demo is tested at all.</b> It is the only thing in this project a player ever
/// sees, and it is the kind of code that rots quietly: a row added to the catalogue that pushes
/// the last one off the bottom of the reservation, or an anchor that drifts off screen after a
/// change to how the view is measured, produces no error anywhere and nothing fails. Both of
/// those are one assertion each.</para>
///
/// <para>The harness empties every canvas between tests, deliberately, so a mod's own drawing
/// cannot end up in a screenshot of somebody else's test. That is why each test here calls
/// <c>Showcase.Install</c> first: the demo registers itself at load, and by the time any test
/// runs that registration is gone.</para>
/// </summary>
public static class DemoTests
{
    /// <summary>
    /// The catalogue fits the room reserved for it, and draws on both of its canvases.
    ///
    /// <para>Headless, because a mark is recorded whether or not there is a display: the layout is
    /// arithmetic over pixel offsets, so the everyday suite can catch a row that outgrew its
    /// reservation rather than leaving it to whoever next looks at a screenshot.</para>
    /// </summary>
    [GameTest]
    public static void TheCatalogueFitsTheRoomReservedForIt()
    {
        Showcase.Install();
        var rows = Showcase.PlaceAt(new Vector2I(1000, 1000));

        if (rows.Used > Showcase.Height)
            throw new AssertionException(
                $"the catalogue lays out {rows.Used} pixels tall but Showcase.Height reserves " +
                $"{Showcase.Height}, which is what the placement centres on the view. Raise the " +
                "constant or shorten a row; as it stands the bottom rows hang below where the " +
                "demo thinks it ends, and the placement is off centre by half the difference.");

        if (rows.Alt < rows.Live)
            throw new AssertionException(
                $"the live row is at {rows.Live} and the Alt row at {rows.Alt}; the live section " +
                "draws into both and expects them in that order");

        var marks = Canvas.For(Showcase.Marks);
        var under = Canvas.For(Showcase.Under, 120);
        if (marks.MarkCount == 0)
            throw new AssertionException("the catalogue drew no retained marks at all");
        if (under.MarkCount == 0)
            throw new AssertionException(
                "nothing was drawn on the lower canvas, so the layering row shows one rectangle " +
                "rather than two and demonstrates nothing");
    }

    /// <summary>
    /// Zooming does not move the catalogue.
    ///
    /// <para><b>A regression test.</b> It used to be placed again whenever the middle of it left
    /// the view, and zooming in shrinks the view around a camera that has not moved: three notches
    /// of zoom and the catalogue jumped to the new centre, while the player was only trying to
    /// look more closely at it. What moves it now is the avatar walking away from it, which is the
    /// thing "follow the player" was meant to mean.</para>
    ///
    /// <para>The zoom is driven through the harness, which sets the camera and its easing target
    /// together, so the frames in between cannot be mistaken for a player walking.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator ZoomingDoesNotMoveTheCatalogue()
    {
        Showcase.Install();
        Showcase.Forget();

        yield return Session.Enter("flat");
        yield return Wait.Frames(4);

        if (Showcase.Anchor is not { } placed)
            throw new AssertionException("the demo never placed itself, so there is nothing to move");
        var placements = Showcase.Placements;

        var restore = View.MaxZoomFactor;
        try
        {
            View.MaxZoomFactor = 8;
            foreach (var zoom in new[] { View.GameMaxZoom * 4f, View.GameMaxZoom * 8f, View.GameMaxZoom })
            {
                yield return View.SetZoom(zoom);
                yield return Wait.Frames(3);

                if (Showcase.Anchor != placed)
                    throw new AssertionException(
                        $"the catalogue moved from {placed} to {Showcase.Anchor} when the zoom " +
                        $"went to {zoom}. Zoom changes what you can see, not where you are, and " +
                        "the placement must not consult the view.");
            }

            if (Showcase.Placements != placements)
                throw new AssertionException(
                    $"the catalogue was placed {Showcase.Placements - placements} more time(s) " +
                    "while only the zoom changed");
        }
        finally
        {
            View.MaxZoomFactor = restore;
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// The two things the game puts in front of a new world -- the welcome panel and the tutorial
    /// goal list -- are closed by the demo, and nothing else is.
    ///
    /// <para>Driven rather than waited for. Whether a given world opens with the welcome panel
    /// depends on the profile and on whether that character has any upgrade yet, so a test that
    /// entered a world and hoped to see one would be vacuous exactly when it mattered. Opening it
    /// here, through the game's own call, makes the premise certain.</para>
    ///
    /// <para>The third assertion is the one that keeps this honest: an ordinary window opened
    /// afterwards is left alone. "Close whatever is open" would pass the first two and would stop
    /// a player opening their inventory.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheDemoClosesTheIntroductionWindowsAndNothingElse()
    {
        Showcase.Install();

        yield return Session.Enter("flat");
        yield return Wait.Frames(3);

        // The welcome panel, opened the way Client.OnLocalPlayerJoined opens it on a new world.
        Gameplay.SetCurrentWindowId(WindowId.WelcomeWindow);
        if (Gameplay.CurrentWindowId != WindowId.WelcomeWindow)
            Harness.Inapplicable("the game would not open the welcome window, so there is " +
                                 "nothing here to dismiss");

        yield return Wait.Frames(3);

        if (Gameplay.CurrentWindowId == WindowId.WelcomeWindow)
            throw new AssertionException(
                "the welcome window is still open after three frames of the demo's pass. It " +
                "covers most of the screen, and the catalogue is drawn underneath it. " +
                $"dismissIntroWindows={Showcase.DismissIntroWindows}, " +
                $"frames={Showcase.Frames}, {PixelArtApi.DescribeState()}");

        // The tutorial goal list, started the way the game starts it for a new character.
        Client.StartTutorial();
        if (TutorialGoal.CurrentGoal == null)
            Harness.Inapplicable("the game would not start a tutorial, so there is no goal list " +
                                 "to dismiss");

        yield return Wait.Frames(3);

        if (TutorialGoal.CurrentGoal != null)
            throw new AssertionException(
                "the tutorial goal list is still set after three frames of the demo's pass; it " +
                "draws down the right-hand side over the live readout");

        // And an ordinary window is not touched. The hub is what a player opens to craft, and a
        // demo that closed it every frame would be unusable rather than merely unhelpful.
        Gameplay.SetCurrentWindowId(WindowId.HubWindow);
        yield return Wait.Frames(3);

        if (Gameplay.CurrentWindowId != WindowId.HubWindow)
            throw new AssertionException(
                "the demo closed an ordinary window it was not asked to. Only the welcome window " +
                "is its business; closing whatever happens to be open stops the player using the " +
                "game at all.");

        Gameplay.SetCurrentWindowId(WindowId.None);
        yield return Session.Leave();
    }

    /// <summary>
    /// A player entering a world sees the demo without going to look for it: the whole catalogue
    /// is inside the pixels the game is drawing, and it got there on its own.
    ///
    /// <para>This is the mod's entire promise, and the one thing about it that cannot be checked
    /// by arithmetic: where "the middle of the view" is depends on the camera, the zoom, the
    /// window and the size of the rendered window of pixels, none of which exist headless.</para>
    ///
    /// <para>It leaves a screenshot in the run's artifacts, which for a mod whose whole output is
    /// visual is the only way to answer "yes, but what does it look like".</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator TheDemoPlacesItselfInsideTheDefaultView()
    {
        Showcase.Install();
        Showcase.Forget();

        yield return Session.Enter("flat");
        // A few frames for the pass to run, place the catalogue, and have it rendered.
        yield return Wait.Frames(5);

        if (Showcase.Anchor is not { } anchor)
            throw new AssertionException(
                $"the demo never placed itself: {Showcase.Frames} frame(s) of its pass have run. " +
                $"{PixelArtApi.DescribeState()}");

        var visible = ViewGeometry.VisibleTiles()
                      ?? throw new AssertionException("the game cannot say which pixels are visible");
        var area = Showcase.Area;

        // max is exclusive, so the far corner that has to be on screen is one pixel inside it.
        var corners = new[]
        {
            area.min,
            new Vector2I(area.max.X - 1, area.min.Y),
            new Vector2I(area.min.X, area.max.Y - 1),
            area.max - Vector2I.One,
        };

        foreach (var corner in corners)
            if (!visible.Contains(corner))
                throw new AssertionException(
                    $"the catalogue runs from {area.min} to {area.max}, and the corner {corner} " +
                    $"is outside the {visible.width}x{visible.height} block of pixels on screen " +
                    $"({visible.min} to {visible.max}). A player entering a world would see only " +
                    "part of it. Either the demo is too big for the default view or it is being " +
                    "placed against the wrong rectangle.");

        if (Canvas.For(Showcase.Live, 127).PassCount < 2)
            throw new AssertionException(
                "the live pass or the Alt painter is not registered, so the bottom half of the " +
                "demo is captions with nothing under them");

        // The demo closes the welcome window itself, which the test above covers. This is for
        // anything else a fixture world might have left open: what is being screenshotted should
        // be the demo, not a dialog over it.
        Session.CloseAllWindows();
        yield return Wait.Frames(2);

        var image = Game.CanvasLayer.GetViewport().GetTexture()?.GetImage();
        if (image == null || image.GetWidth() == 0)
            Harness.Inapplicable("the viewport cannot be read back on this renderer");
        Artifacts.WriteBytes("demo.png", image!.SavePngToBuffer());

        // Every assertion above is about bookkeeping -- an anchor, a mark count, a registration --
        // and every one of them passed on a run where the frame came out completely empty,
        // because the shared pass had faulted on the first frame. So the last word is the frame
        // itself: the demo's own ink, counted in the pixels the game actually rendered.
        if (PixelArtApi.Faulted)
            throw new AssertionException(
                $"the shared drawing pass faulted, so nothing was drawn: {PixelArtApi.Fault}");

        var ink = new Color(0.55f, 0.90f, 1.00f);      // Palette.Ink, the demo's own blue
        var found = 0;
        for (var y = 0; y < image.GetHeight() && found < 50; y += 2)
        for (var x = 0; x < image.GetWidth() && found < 50; x += 2)
        {
            var c = image.GetPixel(x, y);
            if (Math.Abs(c.R - ink.R) + Math.Abs(c.G - ink.G) + Math.Abs(c.B - ink.B) < 0.08f)
                found++;
        }

        if (found < 50)
            throw new AssertionException(
                $"only {found} pixels of the demo's own colour reached the rendered frame, over a " +
                "catalogue the game says is entirely on screen. Something between the marks and " +
                $"the framebuffer is not working. {PixelArtApi.DescribeState()}");

        yield return Session.Leave();
    }
}
