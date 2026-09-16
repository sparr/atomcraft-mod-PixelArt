using Atomcraft.TestHarness;

namespace PixelArt.Test;

/// <summary>
/// This test mod's log.
///
/// <para><b>Why a test mod needs this and the shipped mod does not.</b> A test mod has
/// <c>using Atomcraft.TestHarness;</c> at the top of every file, which brings the harness's own
/// static <c>Log</c> into scope. That one writes under the <i>harness's</i> name, so with
/// several mods in a run every line is unattributable. <c>Log.For</c> binds a log to a name;
/// aliasing the result as <c>Log</c> here means test code just writes <c>Log.Info</c> and gets
/// the right prefix.</para>
///
/// <para>It is not merely cosmetic: <c>ModLog.Event</c> tags its structured records with the mod
/// id, so a record in <c>results.jsonl</c> says who wrote it.</para>
///
/// <para>The shipped mod has the opposite constraint -- it must not reference the harness
/// assembly at all, or it fails to load for every player who has not installed the harness -- and
/// owns a small internal <c>Log</c> of its own; see <c>src/Log.cs</c>.</para>
/// </summary>
internal static class Log
{
    private static readonly ModLog Inner = Atomcraft.TestHarness.Log.For(ModEntry.ModId);

    internal static void Info(string message) => Inner.Info(message);
    internal static void Warn(string message) => Inner.Warn(message);
    internal static void Error(string message) => Inner.Error(message);
    internal static void Event(string kind, Dictionary<string, object?> fields) =>
        Inner.Event(kind, fields);
}
