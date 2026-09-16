using Atomcraft;
using Godot;
using HarmonyLib;

namespace PixelArt;

/// <summary>
/// Rebuilds every canvas once per frame, right after the game has rebuilt the world they sit on
/// top of.
///
/// <para><c>Gameplay.Process</c> is where the game positions the world sprite and computes its
/// shader offsets from the camera, so a postfix on it projects through exactly the camera the
/// frame was drawn with. Driving this from <c>SceneTree.ProcessFrame</c> instead would be a
/// frame out of step whenever the camera is moving, and a mark a few screen pixels off the pixel it
/// names is worse than no mark.</para>
///
/// <para>Worth knowing when this seems not to be installed: <b>a Harmony postfix does not run
/// when the original method throws.</b> <c>GetPatchedMethods</c> lists the method, the method
/// demonstrably executes, and the postfix silently never fires. Check <c>godot.log</c> for an
/// exception inside <c>Gameplay.Process</c> before suspecting Harmony.</para>
///
/// <para>Deliberately not gated on a test being in progress: this is as much for watching a mod
/// while playing it as for a test that screenshots it.</para>
/// </summary>
[HarmonyPatch(typeof(Gameplay), nameof(Gameplay.Process))]
internal static class GameplayRenderPatch
{
    [HarmonyPostfix]
    internal static void AfterProcess() => Renderer.RedrawGuarded();
}

/// <summary>
/// Clears the fault latches when a world starts or ends, so a fault is per-session rather than
/// permanent and a player who hits one can recover by starting a new world.
///
/// <para><c>Simulation.Reset</c> is called from <c>Game.StopSession</c> and is the one reliable
/// "this world is over" signal; the mod loader's universe-load hook is not, because it only
/// fires when a universe file is actually read, so a new world would inherit the last one's
/// state.</para>
///
/// <para>This mod does no per-tick work, so these are the <i>only</i> two things it asks of the
/// simulation, and they are not a substitute for the harness's state registry: a region test
/// calls neither, so a fault raised in one test would otherwise survive into every test after
/// it. See <see cref="PixelArtApi.ResetState"/>.</para>
/// </summary>
[HarmonyPatch]
internal static class SimulationLifecyclePatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Simulation), nameof(Simulation.Init))]
    internal static void AfterInit() => ClearFaults();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Simulation), nameof(Simulation.Reset))]
    internal static void AfterReset() => ClearFaults();

    private static void ClearFaults()
    {
        Renderer.ClearFault();
        foreach (var canvas in Canvas.All)
            canvas.ClearFault();
    }
}

/// <summary>
/// Hands the server resources back when the game shuts down.
///
/// <para>A <c>RenderingServer</c> canvas item is not a node: nothing frees it when the tree goes
/// away, and a font atlas held by a static outlives the renderer that made it. Both are reported
/// as leaks at exit, which is noise in a log somebody is reading to find a real fault. Each
/// canvas frees its own item when its layer leaves the tree; this covers the case where the
/// tree goes away without that happening first. <c>Game.OnApplicationQuit</c> is the game's own
/// shutdown step, called from its <c>_ExitTree</c>.</para>
/// </summary>
[HarmonyPatch(typeof(Game), nameof(Game.OnApplicationQuit))]
internal static class QuitPatch
{
    [HarmonyPrefix]
    internal static void BeforeQuit()
    {
        try
        {
            Canvas.ReleaseAll();
        }
        catch (Exception e)
        {
            // On the way out, and a failure here costs a warning in the engine's leak report
            // rather than anything a player can see.
            Log.Warn($"could not release the canvases on quit: {e.Message}");
        }
    }
}
