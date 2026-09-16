using Atomcraft;
using Atomcraft.TestHarness;
using Godot;

namespace PixelArt.Test;

/// <summary>
/// What this mod looks like when it is working: hold Alt in a session and the pixels around the
/// cursor are outlined, named and pointed at.
///
/// <para><b>It is also the worked example of a consumer.</b> Everything below is what another
/// mod's code would look like -- take a canvas, register a pass, draw on pixels -- and it uses
/// nothing this mod does not offer anyone else. A mod copying it should copy the shape and not
/// the content.</para>
///
/// <para>It lives in the test mod rather than the shipped one because it is a demonstration for a
/// person rather than a feature, and reaching it means <c>./play.sh --debug</c>, which is the one
/// place <c>play.sh</c> deliberately loads the harness. Registered from <c>Initialize</c>, which
/// is allowed: <see cref="Canvas.For"/> touches no game state and the Godot node is made lazily,
/// on the first frame there is something to draw.</para>
///
/// <para>A run of the test suite takes it away again on the first test, because
/// <c>PixelArtApi.ResetState</c> empties every canvas -- that is deliberate, so a debug painter
/// cannot draw across a test's screenshot.</para>
/// </summary>
internal static class DebugOverlay
{
    /// <summary>Pixels either side of the cursor. 4 is a 9x9 block: one machine and its plumbing.</summary>
    private const int Radius = 4;

    private static readonly Color Ink = new(0.55f, 0.90f, 1.00f);
    private static readonly Color Plate = new(0f, 0f, 0f, 0.62f);

    internal static void Register()
    {
        // A whole-frame pass rather than a per-pixel painter, because this wants 81 pixels and a
        // painter would be handed 57,000 to reject. The choice a consumer makes between the two is
        // exactly this one.
        // Gated on a predicate rather than on DrawWhen.AltHeld, even though the condition is
        // exactly "Alt is held". A predicate is something a test can drive; the keyboard is not.
        Canvas.For("PixelArt.debug", Canvas.TopLayer - 1)
              .SetPass("cursor", Draw, () => ShowAlways || Canvas.AltHeld);
    }

    /// <summary>
    /// Forced on, so a test can drive this without holding a key. The whole reason the pass is
    /// gated on a predicate rather than on <c>DrawWhen.AltHeld</c>.
    /// </summary>
    internal static bool ShowAlways;

    /// <summary>How many pixels the last pass looked at, for a test to assert on.</summary>
    internal static int ScannedLastFrame;

    private static void Draw(Canvas canvas)
    {
        var field = Simulation.CurrentState?.Field;
        if (field == null || ViewGeometry.MouseTile() is not { } mouse)
            return;

        // The whole walk, including the corner projection, the row stepping and the clipping to
        // what is on screen. This used to be twenty lines here; ForEachPixel exists because every
        // cursor-sized overlay was writing them again.
        ScannedLastFrame = canvas.ForEachPixel(mouse, Radius, p =>
        {
            if (p.MaterialTypeId < 0)
                return;                         // air, and there is nothing to say about it
            p.Outline(new Color(Ink, 0.5f));
        });

        // The pixel under the cursor gets the detail: its coordinates and what is in it, with the
        // label below the pixel so the cursor is not sitting on top of the text.
        var here = ViewGeometry.ScreenRectOf(mouse);
        canvas.DrawOutline(here, Ink, 2f);
        canvas.DrawArrow(here, Aim.Up, Ink);

        var name = field.Get(mouse.X, mouse.Y) is var id && id >= 0
            ? id.ToMaterialName()
            : "air";
        canvas.DrawLabel(here, $"{mouse.X},{mouse.Y}\n{name}", Ink,
                         TextSize.Small, LabelPlacement.Below, plate: Plate);
    }
}
