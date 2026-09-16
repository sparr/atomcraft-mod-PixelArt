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
        Canvas.For("PixelArt.debug", Canvas.TopLayer - 1)
              .SetPass("cursor", Draw, DrawWhen.AltHeld);
    }

    private static void Draw(Canvas canvas)
    {
        var field = Simulation.CurrentState?.Field;
        var visible = ViewGeometry.VisibleTiles();
        var mouse = ViewGeometry.MouseTile();
        if (field == null || visible == null || mouse == null)
            return;

        var area = ViewGeometry.Around(mouse.Value, Radius).Intersection(visible.Value);
        if (area.width <= 0 || area.height <= 0)
            return;

        // The corner is projected once and the loop steps from it. Going through ScreenRectOf per
        // pixel would ask the engine for the camera and the viewport on every one of them.
        var pixel = ViewGeometry.PixelScreenSize;
        var corner = ViewGeometry.WorldToScreen(
            new Vector2(area.min.X * ViewGeometry.TileSize, area.min.Y * ViewGeometry.TileSize));

        for (var y = area.min.Y; y < area.max.Y; y++)
        for (var x = area.min.X; x < area.max.X; x++)
        {
            var rect = new Rect2(corner.X + (x - area.min.X) * pixel,
                                 corner.Y + (y - area.min.Y) * pixel,
                                 pixel, pixel);
            var material = field.Get(x, y);
            if (material < 0)
                continue;                       // air, and there is nothing to say about it
            canvas.DrawOutline(rect, new Color(Ink, 0.5f));
        }

        // The pixel under the cursor gets the detail: its coordinates and what is in it, with the
        // label below the pixel so the cursor is not sitting on top of the text.
        var here = ViewGeometry.ScreenRectOf(mouse.Value);
        canvas.DrawOutline(here, Ink, 2f);
        canvas.DrawArrow(here, Aim.Up, Ink);

        var name = field.Get(mouse.Value.X, mouse.Value.Y) is var id && id >= 0
            ? id.ToMaterialName()
            : "air";
        canvas.DrawLabel(here, $"{mouse.Value.X},{mouse.Value.Y}\n{name}", Ink,
                         TextSize.Small, LabelPlacement.Below, plate: Plate);
    }
}
