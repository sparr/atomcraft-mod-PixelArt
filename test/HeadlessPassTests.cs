using System.Collections;
using Atomcraft;
using Atomcraft.TestHarness;
using Godot;
using Session = Atomcraft.TestHarness.Session;

namespace PixelArt.Test;

/// <summary>
/// A consumer's per-pixel logic can be exercised without a display.
///
/// <para><b>Why this matters more than one test.</b> The shared pass used to return at its
/// headless check before any pass ran, so nothing a consumer drew was observable without a
/// window -- not the drawing, which genuinely needs one, but not the <i>logic</i> either: a
/// counter a pass incremented never moved. The convention in this family asks every suite for
/// exactly one test that would fail if the mod were not really installed, and such a test has to
/// watch something happen. Without this it had to be <c>RequiresDisplay</c>, leaving the everyday
/// headless loop asserting only that a pass was <i>registered</i> -- true, weaker, and nobody
/// watched it fire.</para>
///
/// <para>So the two tests below are the ones that would fail if Pixel Art were not installed, and
/// they run headless.</para>
/// </summary>
public static class HeadlessPassTests
{
    private const string Owner = "pixelart.test.headless";

    /// <summary>
    /// Opted in, a pass runs on a headless frame and its draw calls go nowhere.
    ///
    /// <para>This is the test the whole switch exists for, so it asserts both halves: the pass
    /// ran, and drawing from it was harmless.</para>
    /// </summary>
    [GameTest]
    public static IEnumerator APassRunsHeadlessWhenAskedTo()
    {
        if (PixelArtApi.HasDisplay)
            Harness.Inapplicable(
                "this run has a display, so it cannot show that a pass runs without one; " +
                "the headful suite covers the same pass through the frame instead");

        var restore = PixelArtApi.RunPassesWithoutDisplay;
        PixelArtApi.RunPassesWithoutDisplay = true;

        var canvas = Canvas.For(Owner);
        var ran = 0;
        var painted = 0;

        canvas.SetPass("counts", c =>
        {
            ran++;
            // Drawing from a headless pass is allowed and discarded. If it were not -- if a Draw
            // reached the RenderingServer, or built a font atlas -- this is where that would
            // surface, as an exception that faults the canvas.
            c.DrawFill(new Vector2I(0, 0), Colors.Magenta);
            c.DrawLabel(new Vector2I(0, 0), "X", Colors.White, TextSize.Small);
        });
        canvas.SetPainter("paints", _ => painted++);

        try
        {
            yield return Session.Enter("flat");
            yield return Wait.Frames(3);

            if (ran == 0)
                throw new AssertionException(
                    "the pass never ran on a headless frame with RunPassesWithoutDisplay set. " +
                    $"A consumer cannot observe its own per-pixel logic. {PixelArtApi.DescribeState()}");
            if (painted == 0)
                throw new AssertionException(
                    "the painter was never handed a pixel headless, so per-pixel logic is still " +
                    "unreachable without a window");
            if (canvas.Faulted)
                throw new AssertionException(
                    $"drawing from a headless pass faulted the canvas rather than being " +
                    $"discarded: {canvas.Fault}");
            if (PixelArtApi.Faulted)
                throw new AssertionException($"the shared pass faulted: {PixelArtApi.Fault}");

            yield return Session.Leave();
        }
        finally
        {
            Canvas.Forget(Owner);
            PixelArtApi.RunPassesWithoutDisplay = restore;
        }
    }

    /// <summary>
    /// Left alone, a headless frame still costs nothing: no pass runs at all.
    ///
    /// <para>The other half of the contract, and the one a dedicated server cares about. Without
    /// it the switch would be a default rather than an opt-in.</para>
    /// </summary>
    [GameTest]
    public static IEnumerator AHeadlessFrameRunsNothingByDefault()
    {
        if (PixelArtApi.HasDisplay)
            Harness.Inapplicable("this run has a display, so there is no headless frame to idle");

        var restore = PixelArtApi.RunPassesWithoutDisplay;
        PixelArtApi.RunPassesWithoutDisplay = false;

        var canvas = Canvas.For(Owner);
        var ran = 0;
        canvas.SetPass("counts", _ => ran++);

        try
        {
            yield return Session.Enter("flat");
            yield return Wait.Frames(3);

            if (ran != 0)
                throw new AssertionException(
                    $"a pass ran {ran} time(s) on a headless frame without being asked to. A " +
                    "dedicated server would pay for every consumer's per-pixel work and draw " +
                    "none of it.");

            yield return Session.Leave();
        }
        finally
        {
            Canvas.Forget(Owner);
            PixelArtApi.RunPassesWithoutDisplay = restore;
        }
    }

    /// <summary>
    /// The switch survives the reset the harness runs between tests, unlike everything else in
    /// <see cref="PixelArtApi.ResetState"/>.
    ///
    /// <para>Stated as a test because it is a deliberate exception to the convention and reads
    /// like an oversight otherwise. Everything else that reset touches is state a test might
    /// leave behind by failing; this is a decision a test mod makes once at load, and clearing it
    /// between tests would switch it off before the first test that needed it.</para>
    /// </summary>
    [GameTest]
    public static void TheHeadlessSwitchOutlivesAStateReset()
    {
        var restore = PixelArtApi.RunPassesWithoutDisplay;
        try
        {
            PixelArtApi.RunPassesWithoutDisplay = true;
            PixelArtApi.ResetState();

            if (!PixelArtApi.RunPassesWithoutDisplay)
                throw new AssertionException(
                    "ResetState cleared RunPassesWithoutDisplay. A test mod sets it once at load, " +
                    "so clearing it between tests switches it off before the first test that " +
                    "needs it -- which is every test that needs it.");

            if (!PixelArtApi.DescribeState().Contains("headlessPasses=True"))
                throw new AssertionException(
                    "the state report does not say whether headless passes are running, which is " +
                    "the first thing to check when a headless test sees nothing happen: " +
                    PixelArtApi.DescribeState());
        }
        finally
        {
            PixelArtApi.RunPassesWithoutDisplay = restore;
        }
    }

    /// <summary>
    /// A walk can stop early, which is what a scan of the whole screen wants and a block of 169
    /// pixels does not care about.
    /// </summary>
    [GameTest]
    public static IEnumerator AWalkCanStopEarly()
    {
        if (PixelArtApi.HasDisplay)
            Harness.Inapplicable("covered headless; this needs no window either way");

        var restore = PixelArtApi.RunPassesWithoutDisplay;
        PixelArtApi.RunPassesWithoutDisplay = true;
        var canvas = Canvas.For(Owner);

        try
        {
            yield return Session.Enter("flat");
            yield return Wait.Frames(2);

            var onScreen = ViewGeometry.VisibleTiles();
            if (onScreen is not { width: >= 8, height: >= 8 })
                Harness.Inapplicable("no usable block of visible pixels to walk");

            var visible = onScreen!.Value;
            var whole = new RectInt(visible.X, visible.Y, 8, 8);

            var seen = 0;
            var visited = canvas.ForEachPixelWhile(whole, _ => { seen++; return seen < 3; });

            if (visited != 3 || seen != 3)
                throw new AssertionException(
                    $"stopping after the third pixel visited {visited} and called the body {seen} " +
                    "time(s); a scan that cannot stop reads the whole screen to find its first match");

            var all = canvas.ForEachPixelWhile(whole, _ => true);
            if (all != 64)
                throw new AssertionException(
                    $"never stopping walked {all} of an 8x8 block, expected 64");

            yield return Session.Leave();
        }
        finally
        {
            Canvas.Forget(Owner);
            PixelArtApi.RunPassesWithoutDisplay = restore;
        }
    }
}
