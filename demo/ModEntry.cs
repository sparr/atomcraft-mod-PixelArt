namespace PixelArt.Demo;

/// <summary>
/// Entry point, as named by <c>mod.json</c>.
///
/// <para><b>This is what a consumer of PixelArt looks like.</b> There is no Harmony instance, no
/// patch, and no game type touched here: the whole mod is a canvas and two callbacks registered
/// on it, and everything else is driven by the library's own per-frame pass.</para>
///
/// <para>Registering from <c>Initialize</c> is allowed even though the game has initialized
/// nothing yet -- no window, no camera, no world. <see cref="Canvas.For"/> touches no game state
/// and the Godot node behind a canvas is made lazily, on the first frame there is something to
/// draw, so the order the loader happens to run this mod and PixelArt in does not matter.</para>
/// </summary>
public static class ModEntry
{
    public const string ModId = "PixelArt.Demo";

    /// <summary>
    /// The Pixel Art this mod is written against. Its 0.x API changes between minor versions and
    /// the loader's dependencies carry no version constraint, so without this check a mismatch
    /// surfaces later as a <c>MissingMethodException</c> from inside a frame.
    /// </summary>
    public const string PixelArtVersion = "0.3";

    public static void Initialize()
    {
        PixelArtApi.RequireVersion(PixelArtVersion);
        Showcase.Install();
        Log.Info("loaded; the catalogue is drawn where you spawn, and follows you if you " +
                 "wander off it. Hold Alt for the painter.");
    }
}
