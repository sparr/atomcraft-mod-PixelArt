using Atomcraft.TestHarness;
using Godot;

namespace PixelArt.Test;

/// <summary>
/// The reset this mod registers with the harness puts the mod back, not just its settings.
///
/// <para>Its own file because it is about the test machinery rather than about the drawing, and
/// because the thing it guards is easy to widen by accident: the day somebody makes
/// <c>Settings.Reset</c> cover more, this test should say so rather than keep passing.</para>
/// </summary>
public static class StateResetTests
{
    /// <summary>
    /// The state registration puts the mod back on, not just its settings.
    ///
    /// <para>The registered reset runs between tests, and the state a test is most likely to leave
    /// behind is the state it did not change on purpose: the off switch flipped by a test that
    /// threw before its <c>finally</c>. That is not reachable from <c>Settings.Reset</c> --
    /// <see cref="PixelArtApi.Enabled"/> is the conjunction of a setting and a field, so restoring
    /// the setting leaves a false field false -- and a mod left switched off does not fail the
    /// tests after it. It passes the ones that assert the game looks as it does unmodded, which is
    /// worse.</para>
    ///
    /// <para>The first assertion is a premise check: if <c>Settings.Reset</c> ever does restore the
    /// mod on its own, this test is measuring nothing and says so rather than passing quietly.</para>
    /// </summary>
    [GameTest]
    public static void ResettingTheStateRestoresTheModAndNotOnlyItsSettings()
    {
        PixelArtApi.Enabled = false;

        Settings.Reset();
        if (PixelArtApi.Enabled)
            throw new AssertionException(
                "Settings.Reset restored the mod by itself, so this test no longer covers the gap " +
                "it was written for. Check what ResetState is still needed for.");

        PixelArtApi.ResetState();
        if (!PixelArtApi.Enabled)
            throw new AssertionException(
                "ResetState left the mod switched off. Every test after this one would run with " +
                "nothing drawn, and the ones asserting an unmarked frame would pass.");
        if (PixelArtApi.Faulted)
            throw new AssertionException("ResetState left a fault latched");
        if (Settings.TextScale != Settings.DefaultTextScale)
            throw new AssertionException(
                $"ResetState left textScale at {Settings.TextScale}, not the default " +
                $"{Settings.DefaultTextScale}");
    }

    /// <summary>
    /// The reset empties every canvas, whoever owns it, including a mod's own debug painter.
    ///
    /// <para>Stated separately because it fails for a different reason and with a different
    /// symptom: a mark left standing from an earlier test appears in the next test's screenshot,
    /// which makes that screenshot evidence of something other than the test that took it. That is
    /// why the sweep is total rather than per-mod, and it is also why a pass a mod registers from
    /// its <c>Initialize</c> survives an ordinary play session and does not survive the first test
    /// of a run.</para>
    /// </summary>
    [GameTest]
    public static void ResettingTheStateEmptiesEveryCanvas()
    {
        const string mine = "pixelart.test.reset.a";
        const string theirs = "pixelart.test.reset.b";

        try
        {
            var a = Canvas.For(mine);
            var b = Canvas.For(theirs);
            a.Fill(new Vector2I(1, 1), Colors.Red);
            a.SetPainter("left behind", _ => { });
            b.Label(new Vector2I(2, 2), "x", Colors.Lime);

            PixelArtApi.ResetState();

            if (a.MarkCount != 0 || a.PassCount != 0 || b.MarkCount != 0)
                throw new AssertionException(
                    $"the reset left marks or passes standing: a has {a.MarkCount} marks and " +
                    $"{a.PassCount} passes, b has {b.MarkCount} marks. A mark that outlives its " +
                    "test lands in the next test's screenshot.");
        }
        finally
        {
            Canvas.Forget(mine);
            Canvas.Forget(theirs);
        }
    }
}
