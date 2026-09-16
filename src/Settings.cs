using System.Globalization;
using Godot;
using FileAccess = Godot.FileAccess;

namespace PixelArt;

/// <summary>
/// What the player can change, and the one file they change it in.
///
/// <para>The file is <c>user://PixelArt.json</c>, beside the game's own
/// <c>DeviceSettings.json</c>, and it is written with the defaults the first time the mod runs
/// so the options are discoverable without reading the README. It is read once, at
/// <c>Initialize</c>; nothing here is reread while the game runs.</para>
///
/// <para><b>These settings cross every mod that draws through this one</b>, which is what makes
/// them worth having at all: a player who finds the text too small has one place to say so
/// rather than one per mod, and a player taking a screenshot has one switch that takes every
/// overlay off it. It is also why there are only two. A knob here overrides what another mod's
/// author chose, so anything that mod could reasonably decide for itself belongs in that mod's
/// own settings file.</para>
///
/// <para>Parsed with Godot's own <see cref="Json"/> rather than Newtonsoft. Newtonsoft is loaded
/// and would do, but a mod that reaches for the game's copy of a library takes on that version;
/// two scalars do not need it.</para>
/// </summary>
public static class Settings
{
    /// <summary>Where the settings file lives, in Godot's user data directory.</summary>
    public const string Path = "user://" + ModEntry.ModId + ".json";

    /// <summary>
    /// Master switch. Off, no canvas draws anything and the per-frame pass returns immediately,
    /// so every mod built on this one goes quiet without any of them being uninstalled. Also
    /// what a test toggles; see <see cref="PixelArtApi.Enabled"/>.
    /// </summary>
    public static bool Enabled = true;

    /// <summary>
    /// Multiplies every label's scale, for a player who wants the text bigger than the mod
    /// drawing it asked for.
    ///
    /// <para>A whole number, because that is the whole point of a bitmap font: at 2 every font
    /// pixel is a 2x2 block of screen pixels and the glyph stays exact, while at 1.5 it would be
    /// resampled into the soft mess a scalable font would have given in the first place.</para>
    ///
    /// <para>It is applied to measuring as well as drawing, so a label that picks its size
    /// automatically steps down to a smaller font rather than overflowing its pixel.</para>
    /// </summary>
    public static int TextScale = DefaultTextScale;

    /// <summary>
    /// The defaults, named once so <see cref="Reset"/> and the first-run file cannot drift apart
    /// from the declarations above.
    /// </summary>
    public const int DefaultTextScale = 1;

    /// <summary>The largest text scale that makes sense; past this a label is bigger than the screen.</summary>
    public const int MaxTextScale = 8;

    /// <summary>Whether <see cref="Load"/> has run, so it does not run twice.</summary>
    private static bool _loaded;

    /// <summary>
    /// Reads the settings file, writing it with the defaults first if it is not there.
    ///
    /// Never throws: a settings file that cannot be read or does not parse leaves the defaults
    /// in place and says so in the log. Refusing to start over a stray comma would be a worse
    /// outcome than ignoring it, and here it would take every mod that draws down with it.
    /// </summary>
    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;

        try
        {
            if (!FileAccess.FileExists(Path))
            {
                Save();
                return;
            }

            using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                Log.Warn($"could not open {Path} ({FileAccess.GetOpenError()}); using defaults");
                return;
            }

            var parsed = Json.ParseString(file.GetAsText());
            if (parsed.VariantType != Variant.Type.Dictionary)
            {
                Log.Warn($"{Path} is not a JSON object; using defaults");
                return;
            }

            var settings = parsed.AsGodotDictionary();
            Enabled = Bool(settings, "enabled", Enabled);
            TextScale = Int(settings, "textScale", TextScale);
            Validate();

            Log.Info($"settings: {Describe()}");
        }
        catch (Exception e)
        {
            Log.Warn($"could not read {Path}; using defaults: {e.Message}");
        }
    }

    /// <summary>
    /// Brings what was read into a range a label can be drawn at, and says so when it had to.
    ///
    /// <para>Public so a test can exercise it on a value it sets itself, rather than by writing
    /// a settings file and reloading.</para>
    /// </summary>
    public static void Validate()
    {
        var scale = Math.Clamp(TextScale, 1, MaxTextScale);
        if (scale != TextScale)
            Log.Warn($"textScale is a whole multiplier between 1 and {MaxTextScale}; " +
                     $"{TextScale} became {scale}");
        TextScale = scale;
    }

    /// <summary>
    /// The current values on one line. Used in the startup log, and handed to the harness's
    /// <c>StateRegistry</c> so a test failure report says what the knobs were set to.
    /// </summary>
    public static string Describe() => $"enabled={Enabled} textScale={TextScale}";

    /// <summary>
    /// Restores every setting to its default, without touching the file. Used between tests, via
    /// the <c>StateSpec</c> the test mod registers.
    ///
    /// Written out longhand rather than by re-reading the file, because a test must not depend on
    /// what happens to be on the developer's disk. A field added above and not added here leaks
    /// one test's configuration into every test after it.
    /// </summary>
    public static void Reset()
    {
        Enabled = true;
        TextScale = DefaultTextScale;
    }

    /// <summary>Writes the current values, which on a first run are the defaults.</summary>
    private static void Save()
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Log.Warn($"could not write {Path} ({FileAccess.GetOpenError()})");
            return;
        }

        // Hand-written rather than serialized, because the comments are the point: this file is
        // the only documentation a player who never finds the README will see. It is also the
        // only place a player learns that this mod is the reason several others can draw at all.
        file.StoreString(
            "{\n" +
            "    \"_\": \"Settings for the PixelArt mod, which other mods draw their overlays through. Delete this file to restore defaults.\",\n" +
            "    \"_enabled\": \"false stops every overlay drawn through this mod, without uninstalling anything. Useful for a clean screenshot.\",\n" +
            $"    \"enabled\": {(Enabled ? "true" : "false")},\n" +
            $"    \"_textScale\": \"Whole-number multiplier on every label any mod draws, 1 to {MaxTextScale}. Whole numbers only: a bitmap font is exact at 2 and blurred at 1.5.\",\n" +
            $"    \"textScale\": {Text(TextScale)}\n" +
            "}\n");
        Log.Info($"wrote default settings to {Path}");
    }

    /// <summary>
    /// A number as JSON, in the invariant culture. The game runs under whatever locale the player
    /// has, and a locale-formatted number would write a settings file this mod cannot read back.
    /// </summary>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool Bool(Godot.Collections.Dictionary settings, string key, bool fallback) =>
        settings.TryGetValue(key, out var value) ? value.AsBool() : fallback;

    private static int Int(Godot.Collections.Dictionary settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) ? (int)value.AsInt32() : fallback;
}
