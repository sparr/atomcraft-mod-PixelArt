using Atomcraft.TestHarness;

namespace PixelArt.Test;

/// <summary>
/// Generic well-formedness, from the harness.
///
/// <para><b>Every mod should have this, and it is two lines.</b> It catches the mistakes that
/// produce no error at load and no crash, just a mod that quietly does less than it says: a
/// manifest naming an <c>initClass</c> that is not there, a data path matching nothing in the
/// zip, a missing translation.</para>
///
/// <para>It reads the mod's own zip rather than the live registries, so it also works on a mod
/// that fails to load, which is when it is worth the most. For this mod in particular a manifest
/// that does not load is every consumer's overlay gone at once, with the loader report naming
/// only this one.</para>
/// </summary>
public static class ValidationTests
{
    [GameTest]
    public static void TheModIsWellFormed() => Validation.Check("PixelArt");

    /// <summary>
    /// The test mod too. Its manifest can rot the same way -- a renamed <c>initClass</c>, a
    /// dependency on a module id that no longer exists -- and nothing else would notice.
    /// </summary>
    [GameTest]
    public static void TheTestModIsWellFormed() => Validation.Check("PixelArt.Test");
}
