// PixelArt, restated in one file: what it actually takes to draw a box on a simulation pixel.
//
// Not equivalent to the mod, and deliberately so. Missing: the bitmap fonts and everything about
// text, the canvas registry that keeps two mods' marks apart, retained marks, the per-pixel painter
// walk, the fault latches, the settings, and every guard for the frames where there is no camera
// or no world. What is here is the mechanism the rest is built around, and it is about forty
// lines.
//
// Drop it in a mod, call Minimal.Install() from Initialize(), and pixel (100, 100) is outlined in
// red for as long as the world is on screen.
//
// Outside src/ on purpose: the SDK's **/*.cs glob would otherwise compile it into the mod, and two
// [HarmonyPatch] classes on Gameplay.Process would draw everything twice.

using Atomcraft;
using Godot;
using HarmonyLib;

namespace PixelArtMinimal;

public static class Minimal
{
    public static void Install() => new Harmony("PixelArtMinimal").PatchAll();
}

[HarmonyPatch(typeof(Gameplay), nameof(Gameplay.Process))]
internal static class Draw
{
    private const float TileSize = 8f;          // world units per simulation pixel
    private static readonly Vector2I Target = new(100, 100);

    private static CanvasLayer? _layer;
    private static Rid _item;

    // Gameplay.Process is where the game positions the world sprite from the camera, so a postfix
    // on it projects through exactly the camera the frame was drawn with.
    private static void Postfix()
    {
        var camera = Client.FollowCam;
        var root = Game.Instance?.GetTree()?.Root;
        if (camera == null || root == null || DisplayServer.GetName() == "headless")
            return;

        if (_layer == null || !GodotObject.IsInstanceValid(_layer))
        {
            // A CanvasLayer plus a RenderingServer item, rather than a Control with _Draw: a mod
            // is compiled without Godot's source generators, so a CanvasItem subclass of ours
            // would never have its _Draw called.
            _layer = new CanvasLayer { Name = "PixelArtMinimal", Layer = 128 };
            root.AddChild(_layer);
            _item = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetParent(_item, _layer.GetCanvas());
        }

        RenderingServer.CanvasItemClear(_item);

        // The whole of the geometry: a pixel's corner in world units, relative to the camera,
        // scaled by the zoom, from the middle of the frame.
        var viewport = Game.CanvasLayer.GetViewport().GetVisibleRect().Size;
        var world = new Vector2(Target.X * TileSize, Target.Y * TileSize);
        var at = (world - camera.GlobalPosition) * camera.Zoom + viewport * 0.5f;
        var size = TileSize * camera.Zoom.X;

        // Four rects rather than a stroked outline, which would straddle the pixel's edge and put
        // half of itself on the neighbour.
        var (x, y, t) = (Mathf.Round(at.X), Mathf.Round(at.Y), 1f);
        RenderingServer.CanvasItemAddRect(_item, new Rect2(x, y, size, t), Colors.Red);
        RenderingServer.CanvasItemAddRect(_item, new Rect2(x, y + size - t, size, t), Colors.Red);
        RenderingServer.CanvasItemAddRect(_item, new Rect2(x, y, t, size), Colors.Red);
        RenderingServer.CanvasItemAddRect(_item, new Rect2(x + size - t, y, t, size), Colors.Red);
    }
}
