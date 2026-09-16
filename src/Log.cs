using Godot;

namespace PixelArt;

/// <summary>
/// Everything this mod says, funnelled through one prefix.
///
/// <c>user://logs/godot.log</c> is the only channel a mod has once the game is running, and it
/// carries every subsystem's output, so a consistent tag is what makes this mod's lines findable
/// in it. <c>play.sh --verify</c> greps for exactly this tag.
///
/// <para>Shipped code owns its own <c>Log</c> like this rather than borrowing the harness's: a
/// mod that referenced <c>Atomcraft.TestHarness.dll</c> from its shipped assembly would fail to
/// load for every player who had not installed the harness. That applies with particular force
/// here, since every mod that draws through this one inherits its references.</para>
///
/// <para>A line written on behalf of a consumer names that consumer's canvas, not just this mod,
/// or a player looking at a report has no way to tell whose drawing broke.</para>
/// </summary>
internal static class Log
{
    public const string Tag = ModEntry.ModId;

    public static void Info(string message) => GD.Print($"[{Tag}] {message}");
    public static void Warn(string message) => GD.PrintErr($"[{Tag}] WARNING: {message}");
    public static void Error(string message) => GD.PrintErr($"[{Tag}] ERROR: {message}");
}
