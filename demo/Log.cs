using Godot;

namespace PixelArt.Demo;

/// <summary>
/// Everything this mod says, funnelled through one prefix. <c>play.sh --demo</c> greps for it.
///
/// <para>Its own, rather than the library's: <c>PixelArt.Log</c> is internal to that assembly on
/// purpose, so that a line in <c>godot.log</c> tagged <c>[PixelArt]</c> is always the library
/// speaking and never one of its consumers.</para>
/// </summary>
internal static class Log
{
    public const string Tag = ModEntry.ModId;

    public static void Info(string message) => GD.Print($"[{Tag}] {message}");
    public static void Warn(string message) => GD.PrintErr($"[{Tag}] WARNING: {message}");
    public static void Error(string message) => GD.PrintErr($"[{Tag}] ERROR: {message}");
}
