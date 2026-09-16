using Atomcraft;
using HarmonyLib;

namespace PixelArt;

/// <summary>
/// Everything this mod needs the game to still have, checked once at startup.
///
/// <para><b>Why this exists.</b> A mod's whole failure mode after a game update is silence:
/// Harmony finds nothing to patch, nothing throws, the game runs, and the mod simply does not do
/// its job. Here that silence is inherited by every mod drawing through this one, each of which
/// would be reported as broken instead. One check at startup turns that into a sentence naming
/// what moved.</para>
///
/// <para><b>What the compiler already covers.</b> Every method below is public and named with
/// <c>nameof</c>, so a rename is a build error against the new assembly -- the loudest failure
/// available. They are resolved here as well because a player can be running a different game
/// build from the one this was compiled against, and that case deserves a sentence rather than an
/// exception out of <c>PatchAll</c>.</para>
///
/// <para>Nothing here is reached by string, and that is worth keeping. The harness's overlay
/// needed <c>FollowCam</c>'s private easing target because it moved the camera; this mod only
/// reads where the camera is, through public members, so a game update can cost it a patch target
/// but not a silently-null reflective bind.</para>
/// </summary>
internal static class GameBindings
{
    /// <summary>
    /// The methods this mod patches or projects through. Without the first there is no frame to
    /// draw on at all; the rest decide which pixels are on screen.
    /// </summary>
    private static readonly (string Name, bool Found)[] Members =
    {
        ("Gameplay.Process",
            AccessTools.Method(typeof(Gameplay), nameof(Gameplay.Process)) != null),
        ("Gameplay.GetWindowRectAndOriginForRenderingWorld",
            AccessTools.Method(typeof(Gameplay),
                nameof(Gameplay.GetWindowRectAndOriginForRenderingWorld)) != null),
        ("Simulation.Init",
            AccessTools.Method(typeof(Simulation), nameof(Simulation.Init)) != null),
        ("Simulation.Reset",
            AccessTools.Method(typeof(Simulation), nameof(Simulation.Reset)) != null),
        ("Game.OnApplicationQuit",
            AccessTools.Method(typeof(Game), nameof(Game.OnApplicationQuit)) != null),
    };

    /// <summary>Whether there is anything to patch. False means the mod should not try.</summary>
    internal static bool Complete => Members.All(m => m.Found);

    /// <summary>
    /// Whether the one hook everything depends on is present. The others cost a feature each; a
    /// missing <c>Gameplay.Process</c> costs the whole mod, and the difference is worth saying out
    /// loud rather than refusing to load over a missing quit hook.
    /// </summary>
    internal static bool CanDraw => Members[0].Found;

    /// <summary>Names what is missing, for one log line at startup.</summary>
    internal static string Missing =>
        string.Join(", ", Members.Where(m => !m.Found).Select(m => m.Name));
}
