using System.Reflection;
using Atomcraft;
using Atomcraft.TestHarness;
using HarmonyLib;

namespace PixelArt.Test;

/// <summary>
/// The mod is actually wired into the game.
///
/// <para><b>These fail first and loudest when a Harmony patch has gone missing</b>, which makes
/// every other failure in the suite easier to read: a mod whose hooks never installed fails its
/// behavior tests for reasons that have nothing to do with its behavior, and this names the real
/// cause. Here it matters twice over, because every other mod's overlay fails the same way.</para>
///
/// <para>Worth knowing while reading a failure here: a postfix does not run when the original
/// method throws. <c>GetPatchedMethods</c> lists the method, the method demonstrably executes,
/// and the postfix silently never fires. If a hook seems not to be installed, check
/// <c>godot.log</c> for an exception inside the target before suspecting Harmony.</para>
/// </summary>
public static class RegistrationTests
{
    /// <summary>The Harmony id the mod registers under, which is its mod id.</summary>
    private const string Owner = "PixelArt";

    /// <summary>
    /// The per-frame hook everything else rests on is installed. Without it nothing is ever
    /// drawn, by this mod or by any mod built on it, and nothing anywhere reports an error.
    /// </summary>
    [GameTest]
    public static void TheRenderHookIsInstalled()
    {
        var target = AccessTools.Method(typeof(Gameplay), nameof(Gameplay.Process))
                     ?? throw new AssertionException(
                         "the game no longer has Gameplay.Process; the mod needs an update");

        if (!Harmony.GetAllPatchedMethods().Contains(target))
            throw new AssertionException(
                "Gameplay.Process is not patched, so no canvas will ever be drawn. " +
                "ModEntry.Initialize may have returned early because GameBindings reported " +
                "something missing.");
    }

    /// <summary>
    /// The patch is this mod's and not somebody else's, which is what the Harmony instance id is
    /// for. A second mod postfixing the same method is fine and expected; this asserts ours is
    /// among them.
    /// </summary>
    [GameTest]
    public static void TheRenderHookIsOurs()
    {
        var target = AccessTools.Method(typeof(Gameplay), nameof(Gameplay.Process))
                     ?? throw new AssertionException("the game no longer has Gameplay.Process");

        var owners = Harmony.GetPatchInfo(target)?.Owners
                     ?? (IReadOnlyList<string>)Array.Empty<string>();

        if (!owners.Contains(Owner))
            throw new AssertionException(
                $"Gameplay.Process is patched by [{string.Join(", ", owners)}], none of which is " +
                $"'{Owner}'. Another mod is doing this, or the id changed.");
    }

    /// <summary>
    /// The simulation lifecycle hooks that clear a fault are installed, so a mod that threw once
    /// is not switched off for the rest of the process.
    /// </summary>
    [GameTest]
    public static void TheLifecycleHooksAreInstalled()
    {
        var patched = Harmony.GetAllPatchedMethods().ToList();

        foreach (var name in new[] { nameof(Simulation.Init), nameof(Simulation.Reset) })
        {
            var target = AccessTools.Method(typeof(Simulation), name);
            if (target == null)
                throw new AssertionException(
                    $"the game no longer has Simulation.{name}; the mod needs an update");
            if (!patched.Contains(target))
                throw new AssertionException(
                    $"Simulation.{name} is not patched, so a canvas that faulted once would stay " +
                    "switched off for the rest of the process rather than for the rest of the world.");
        }
    }

    /// <summary>
    /// The startup binding check agrees with reality. If this fails, <c>GameBindings</c> is
    /// looking for something the mod does not actually depend on, or missing something it does.
    /// </summary>
    [GameTest]
    public static void TheBindingCheckIsHonest()
    {
        var bindings = typeof(PixelArtApi).Assembly.GetType("PixelArt.GameBindings")
                       ?? throw new AssertionException("PixelArt.GameBindings no longer exists");

        var complete = bindings
            .GetProperty("Complete", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as bool?;

        if (complete != true)
            throw new AssertionException(
                "GameBindings reports something missing, and the drawing depends on it: " +
                bindings.GetProperty("Missing", BindingFlags.NonPublic | BindingFlags.Static)
                        ?.GetValue(null));
    }
}
