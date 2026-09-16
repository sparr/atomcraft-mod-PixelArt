using Atomcraft;
using System.Collections;
using Atomcraft.TestHarness;
using Godot;
using Session = Atomcraft.TestHarness.Session;

namespace PixelArt.Test;

/// <summary>
/// When a pass runs, and who decides.
///
/// <para><b>Why these exist.</b> The only gate this mod offered at first was
/// <c>DrawWhen.AltHeld</c>, which reads the real keyboard and nothing else. A consumer whose own
/// setting said "annotate while Alt is held" could not express "and only when my setting is on",
/// and -- worse -- no test could reach its drawing at all, because a test cannot hold a key down.
/// Five of that mod's tests failed on it and the whole drawing path was unreachable. A predicate
/// covers both cases, and AltHeld is now the shorthand for one of them.</para>
///
/// <para>So these tests are themselves the evidence the gap is closed: every one of them drives a
/// gated pass, which is the thing that used to be impossible.</para>
/// </summary>
public static class GatingTests
{
    private const string Owner = "pixelart.test.gate";

    /// <summary>
    /// A predicate decides whether the pass runs, and is asked once a frame rather than taken on
    /// trust at registration.
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator APredicateGatesAPass()
    {
        yield return Session.Enter("flat");
        yield return View.LookAt(Anchor());

        var canvas = Canvas.For(Owner);
        var open = false;
        var asked = 0;
        var ran = 0;

        canvas.SetPass("gated", _ => ran++, () => { asked++; return open; });

        try
        {
            yield return Wait.Frames(3);

            if (asked == 0)
                throw new AssertionException("the predicate was never asked");
            if (ran != 0)
                throw new AssertionException(
                    $"the pass ran {ran} time(s) while its predicate said no");

            var askedWhileClosed = asked;
            open = true;
            yield return Wait.Frames(3);

            if (ran == 0)
                throw new AssertionException(
                    $"the pass never ran after its predicate said yes; it was asked " +
                    $"{asked - askedWhileClosed} more time(s)");
            if (canvas.Faulted)
                throw new AssertionException($"the canvas faulted: {canvas.Fault}");
        }
        finally
        {
            Canvas.Forget(Owner);
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// A painter's predicate is asked once a frame, not once per pixel.
    ///
    /// <para>That is the difference between a gate that costs a delegate call and one that costs
    /// 57,000 of them, and it is not visible from the outside except by counting.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator APaintersPredicateIsAskedOncePerFrame()
    {
        yield return Session.Enter("flat");
        yield return View.LookAt(Anchor());

        var canvas = Canvas.For(Owner);
        var asked = 0;
        var painted = 0;

        canvas.SetPainter("counted", _ => painted++, () => { asked++; return true; });

        try
        {
            yield return Wait.Frames(3);

            if (painted == 0)
                throw new AssertionException("the painter never ran, so nothing was counted");
            if (asked >= painted)
                throw new AssertionException(
                    $"the predicate was asked {asked} time(s) for {painted} painted pixels, which " +
                    "is per pixel rather than per frame -- the gate costs as much as the work");
            if (asked > 10)
                throw new AssertionException(
                    $"the predicate was asked {asked} times over three frames; once a frame is " +
                    "the contract");
        }
        finally
        {
            Canvas.Forget(Owner);
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// A predicate that throws is treated exactly as a pass that throws, rather than escaping into
    /// the shared frame.
    ///
    /// <para>It runs on the same per-frame path, so it has the same power to fill a disk: Godot
    /// logs an unhandled exception from there on every occurrence with no backpressure.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator AThrowingPredicateFaultsOnlyItsOwnCanvas()
    {
        const string bad = "pixelart.test.gate.bad";
        const string good = "pixelart.test.gate.good";

        yield return Session.Enter("flat");
        yield return View.LookAt(Anchor());

        var loud = Canvas.For(bad);
        var quiet = Canvas.For(good);
        var quietRan = 0;

        loud.SetPass("throws", _ => { }, () => throw new InvalidOperationException("deliberate"));
        quiet.SetPass("counts", _ => quietRan++);

        try
        {
            yield return Wait.Frames(3);

            if (!loud.Faulted)
                throw new AssertionException(
                    "the canvas whose predicate threw is not faulted, so it will be asked again " +
                    "next frame and every frame after that");
            if (loud.PassCount != 0)
                throw new AssertionException("the pass with the throwing predicate is still registered");
            if (quiet.Faulted || quietRan == 0)
                throw new AssertionException(
                    $"an unrelated canvas was taken down with it: faulted={quiet.Faulted}, " +
                    $"ran={quietRan}");
            if (PixelArtApi.Faulted)
                throw new AssertionException(
                    $"one predicate's exception switched the whole shared pass off: {PixelArtApi.Fault}");
        }
        finally
        {
            Canvas.Forget(bad);
            Canvas.Forget(good);
        }

        yield return Session.Leave();
    }

    /// <summary>
    /// <c>DrawWhen.AltHeld</c> still means what it did, and is now simply a predicate.
    ///
    /// <para>Only the not-held direction is asserted: holding a real modifier means synthesising
    /// OS input, which a test cannot do reliably, and the expensive mistake is a pass that runs
    /// when it was told not to. That asymmetry is exactly why a mod should reach for the predicate
    /// overload instead.</para>
    /// </summary>
    [GameTest(RequiresDisplay = true)]
    public static IEnumerator AltHeldIsStillTheShorthandItWas()
    {
        yield return Session.Enter("flat");
        yield return View.LookAt(Anchor());

        if (Canvas.AltHeld)
            Harness.Inapplicable("something is holding Alt, so an idle gate cannot be observed");

        var canvas = Canvas.For(Owner);
        var ran = 0;
        canvas.SetPass("gated", _ => ran++, DrawWhen.AltHeld);

        try
        {
            yield return Wait.Frames(3);
            if (ran != 0)
                throw new AssertionException($"an AltHeld pass ran {ran} time(s) with Alt not held");

            canvas.SetPass("ungated", _ => ran++);
            yield return Wait.Frames(3);
            if (ran == 0)
                throw new AssertionException(
                    "an ungated pass did not run either, so the check above proved nothing");
        }
        finally
        {
            Canvas.Forget(Owner);
        }

        yield return Session.Leave();
    }

    private static Vector2I Anchor()
    {
        if (Game.World?.Spaceship == null)
            Harness.Inapplicable("no spaceship to anchor a world position on");
        return Game.World.Spaceship.GlobalPosition.GlobalToTileposI() + new Vector2I(200, 0);
    }
}
