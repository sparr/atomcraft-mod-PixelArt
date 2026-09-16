using Atomcraft.TestHarness;

namespace PixelArt.Test;

/// <summary>
/// Entry point for the test mod.
///
/// <para>A peer of <c>PixelArt</c> rather than a module of it, because the mod loader treats a
/// missing dependency as an error: a test module shipped inside the mod's own zip would show a
/// red entry in the loader report for every player who did not also install the harness.</para>
///
/// <para>The harness discovers <c>[GameTest]</c> methods in every loaded assembly by itself, so
/// there is nothing to register for the tests. What is registered here is the mod's own state,
/// so the harness can put it back between tests and report it on a failure.</para>
/// </summary>
public static class ModEntry
{
    public const string ModId = "PixelArt.Test";

    /// <summary>
    /// The harness this mod is written against. Its 0.x API changes between minor versions, and
    /// the loader's dependencies carry no version constraint, so without this check a mismatched
    /// harness surfaces later as a <c>MissingMethodException</c> from somewhere unrelated. Keep
    /// it in step with the zip named in <c>harness.conf</c>.
    /// </summary>
    public const string HarnessVersion = "0.4";

    public static void Initialize()
    {
        Harness.RequireVersion(HarnessVersion);

        // ONE StateSpec for the whole mod, resetting everything it carries between tests: the
        // settings, the runtime switch, the fault latches, and every canvas's marks and passes.
        //
        // Deliberately one rather than one per concern. Two mods in this family registered a spec
        // per concern and each ended up registering exactly its settings, leaving the fault latch
        // and the runtime switch behind. A latch nothing clears is worse than a failing test: a
        // mod left switched off fails the tests asserting the MOD's behavior and passes the ones
        // asserting the GAME's, so a run goes partly red for unrelated-looking reasons and partly
        // green for no reason at all.
        //
        // No checksum: nothing here is simulation state, so none of it belongs in a determinism
        // comparison. Drawing does not touch the field.
        StateRegistry.Register(new StateSpec
        {
            Name = "pixelart",
            OnReset = PixelArtApi.ResetState,
            OnDescribe = PixelArtApi.DescribeState,
        });

        // Play-time diagnostics. Registered here rather than in the shipped mod because it is a
        // demonstration for a person rather than a feature; reachable in a real session only via
        // ./play.sh --debug.
        DebugOverlay.Register();

        Log.Info("loaded");
    }
}
