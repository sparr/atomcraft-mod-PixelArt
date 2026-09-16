using System.Reflection;
using HarmonyLib;

namespace PixelArt;

/// <summary>
/// Entry point, as named by <c>mod.json</c>.
///
/// <para><b>Initialize runs before the game has initialized anything.</b> The mod loader loads
/// every mod during <c>SceneTree._initialize()</c> and <c>Game._Ready</c> does not run until the
/// following frame, so there is no window, no viewport, no camera and no world here. The only
/// correct thing to do is install patches and return; everything this mod does is driven from
/// them afterwards, once a frame, while a world is on screen.</para>
///
/// <para>Reading the settings file is the one exception worth taking, because it depends on
/// nothing but Godot's user directory.</para>
///
/// <para><b>A consumer's <c>Initialize</c> may ask for a canvas here.</b> <see cref="Canvas.For"/>
/// touches no game state -- it creates the Godot node lazily, on the first frame it has something
/// to draw -- so a mod may take its canvas and register a painter from its own
/// <c>Initialize</c>, whichever order the loader happens to run the two mods in.</para>
/// </summary>
public static class ModEntry
{
    /// <summary>
    /// The mod id. One constant, used by <see cref="Log"/>, by <see cref="Settings.Path"/>, as
    /// the Harmony instance id, and matching <c>mod.json</c>'s <c>"id"</c> and the <c>ModId</c>
    /// property in the csproj.
    ///
    /// <para>It is also what a consuming mod names in its own <c>mod.json</c> dependencies, as
    /// <c>"PixelArt/Main"</c>.</para>
    /// </summary>
    public const string ModId = "PixelArt";

    /// <summary>
    /// The running mod's version, matching <c>mod.json</c> and the csproj.
    ///
    /// <para>Deliberately not a <c>const</c>. A const is inlined into whatever compiles against
    /// it, so a mod built against one Pixel Art and run against another would read the version it
    /// was <i>built</i> with and have no way to notice. That is the exact confusion
    /// <see cref="PixelArtApi.RequireVersion"/> exists to prevent, and a const would be this mod
    /// handing it out.</para>
    /// </summary>
    public static readonly string Version = "0.2.0";

    private static Harmony? _harmony;

    public static void Initialize()
    {
        Settings.Load();

        // Before PatchAll rather than after: this is the check that turns a game update into a
        // sentence, and PatchAll is what would otherwise throw first and less helpfully.
        if (!GameBindings.CanDraw)
        {
            Log.Error($"the game no longer has {GameBindings.Missing}, so there is no per-frame " +
                      "hook to draw from and nothing will be drawn by any mod that uses this " +
                      "one. This usually means the game updated; the mod needs one too.");
            return;
        }

        _harmony = new Harmony(ModId);
        // The executing assembly explicitly, rather than the argument-less PatchAll that walks
        // the calling assembly. Same result here, and unambiguous when a mod grows a second one.
        _harmony.PatchAll(Assembly.GetExecutingAssembly());

        // Not fatal: each patch beyond the render hook costs one thing if it is missing -- a
        // fault that outlives its world, or a leaked RID reported at exit -- and half a mod
        // working beats none of it. Said out loud so the cost is attributable.
        if (!GameBindings.Complete)
            Log.Warn($"the game no longer has {GameBindings.Missing}; drawing still works, but " +
                     "fault latches and canvas cleanup are not wired to the game's lifecycle.");

        Log.Info($"initialized, {_harmony.GetPatchedMethods().Count()} method(s) patched, " +
                 $"{Settings.Describe()}");
    }
}
