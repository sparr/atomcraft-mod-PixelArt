namespace PixelArt;

/// <summary>
/// What this mod is doing right now, and the one switch that stops it. Everything here is safe
/// to read with no game loaded; the answers are zero or false until there is one.
///
/// <para>The drawing itself is on <see cref="Canvas"/>, which is what a consumer uses. This is
/// the state around it: whether anything is drawing, whether something went wrong, and how to
/// put it all back between tests.</para>
/// </summary>
public static class PixelArtApi
{
    /// <summary>The running mod's version. See <see cref="RequireVersion"/>.</summary>
    public static string Version => ModEntry.Version;

    /// <summary>
    /// Refuses to continue unless the installed Pixel Art matches the version a mod was built
    /// against. Call it from your mod's <c>Initialize</c>, before taking a canvas.
    ///
    /// <code>
    /// public static void Initialize()
    /// {
    ///     PixelArtApi.RequireVersion("0.1");
    ///     ...
    /// }
    /// </code>
    ///
    /// <para><b>Why this is needed at all.</b> The mod loader's dependency mechanism carries no
    /// version constraint: a mod declares that it needs <c>PixelArt/Main</c>, not <i>which</i>
    /// PixelArt. With an API that is deliberately unstable through 0.x, a mismatch otherwise
    /// surfaces much later as a <c>MissingMethodException</c> from somewhere unrelated -- on a
    /// frame, inside a pass, where it faults a canvas and reads as a bug in the consumer.</para>
    ///
    /// <para>Through 0.x the check is on major and minor: <c>"0.1"</c> accepts any 0.1.x and
    /// rejects 0.2.0, because a minor bump is where breaking changes live before 1.0.</para>
    ///
    /// <para>It throws, and the caller should let it: a mod whose <c>Initialize</c> throws is
    /// reported by the loader as failed, which is a red entry naming the mod and the reason. The
    /// alternative -- carrying on against an API that may not be there -- is a mod that half
    /// works.</para>
    /// </summary>
    public static void RequireVersion(string expected)
    {
        if (Matches(expected, Version))
            return;

        var message =
            $"this mod was built against Pixel Art {expected}, but {Version} is installed. " +
            "The 0.x API changes between minor versions; update the mod or install the matching " +
            "PixelArt release.";
        Log.Error(message);
        throw new InvalidOperationException(message);
    }

    private static bool Matches(string expected, string actual)
    {
        var want = expected.Split('.');
        var have = actual.Split('.');
        if (want.Length < 2 || have.Length < 2)
            return expected == actual;
        return want[0] == have[0] && want[1] == have[1];
    }

    private static bool _enabled = true;

    /// <summary>
    /// Whether anything is drawn at all. Setting it false empties every canvas on the next frame
    /// and leaves them registered, so a mod built on this one is quiet rather than broken, and
    /// switching it back on restores exactly what was registered.
    /// </summary>
    public static bool Enabled
    {
        get => _enabled && Settings.Enabled;
        set
        {
            // Against the effective value, not the backing field: with the settings file saying
            // off, the field alone would report no change and quietly refuse to switch it on.
            if (Enabled == value)
                return;
            _enabled = value;
            Settings.Enabled = value;
            Log.Info(value ? "enabled" : "disabled");
        }
    }

    /// <summary>
    /// Whether the shared per-frame pass has been switched off by an unhandled failure. False in
    /// the ordinary case; true means <c>godot.log</c> has one report of why.
    ///
    /// <para>A failure inside one mod's pass does not land here -- it faults that
    /// <see cref="Canvas"/> alone, so one mod's bug cannot take another mod's overlay down. This
    /// is for a failure in the shared machinery.</para>
    /// </summary>
    public static bool Faulted => Renderer.Faulted;

    /// <summary>What the shared pass threw, kept so a test can assert on it rather than grep the log.</summary>
    public static Exception? Fault => Renderer.Fault;

    /// <summary>Every canvas with a fault of its own, by owner.</summary>
    public static IEnumerable<string> FaultedCanvases =>
        Canvas.All.Where(c => c.Faulted).Select(c => c.Owner);

    /// <summary>How many canvases have been asked for. One per consuming mod, as a rule.</summary>
    public static int CanvasCount => Canvas.All.Count;

    /// <summary>
    /// How many pixels the painters were walked over on the last frame any of them ran. Zero when
    /// no painter is registered, or when every one of them is waiting on Alt, which is the point
    /// of the gate.
    /// </summary>
    public static int PixelsPaintedLastFrame => Renderer.PixelsPaintedLastFrame;

    /// <summary>Whether this process has a display to draw on.</summary>
    public static bool HasDisplay => !Renderer.Headless;

    /// <summary>
    /// Run every pass and painter on a headless frame too, discarding the draw calls. Off by
    /// default.
    ///
    /// <para><b>What this is for.</b> Without it, nothing a consumer draws is observable without
    /// a display: the shared pass returns before any pass runs, so a mod's own per-pixel logic
    /// never executes and a counter it increments never moves. That costs more than one test. The
    /// convention in this family asks every suite for exactly one test that would fail if the mod
    /// were not really installed, and that test has to watch something actually happen -- so
    /// without this it must be <c>RequiresDisplay</c>, and the everyday headless loop is left
    /// asserting only that a pass is <i>registered</i>, which nobody watched fire.</para>
    ///
    /// <para>Set it from a test mod's <c>Initialize</c>. The per-pixel logic is the part worth
    /// exercising cheaply; the <c>RenderingServer</c> calls are the part that genuinely needs a
    /// window, and those are discarded here -- every <c>Draw</c> is a no-op on a canvas with no
    /// item, so nothing is allocated and no font atlas is built.</para>
    ///
    /// <para><b>Deliberately not cleared by <see cref="ResetState"/>.</b> Everything else there is
    /// state a test might leave behind by failing; this is a decision a test mod makes once, at
    /// load, about the whole run. Resetting it between tests would switch it off before the first
    /// test that needed it, which is its only purpose. It is reported by
    /// <see cref="DescribeState"/> instead, so a confusing headless failure says whether passes
    /// were running.</para>
    ///
    /// <para>A real headless server pays nothing: left false, the frame returns exactly where it
    /// did before.</para>
    /// </summary>
    public static bool RunPassesWithoutDisplay { get; set; }

    /// <summary>What is registered and what state it is in, as one line for the log.</summary>
    public static string Describe() =>
        $"{CanvasCount} canvas(es): " +
        (CanvasCount == 0
            ? "none registered"
            : string.Join(", ", Canvas.All.Select(c =>
                $"{c.Owner}@{c.Layer} marks={c.MarkCount} passes={c.PassCount}" +
                (c.Faulted ? " FAULTED" : ""))));

    /// <summary>
    /// Returns the mod to the state it starts a session in: settings at their defaults, every
    /// canvas emptied, no fault latched anywhere.
    ///
    /// <para><b>Why <c>Settings.Reset</c> alone will not do.</b> Settings are the state a test
    /// changes on purpose; this is the state a test leaves behind by failing.
    /// <see cref="Enabled"/> is the conjunction of a setting and a field, so a test that leaves
    /// the field false -- because it threw before its <c>finally</c>, or because a
    /// <c>finally</c> in an iterator was never resumed -- keeps the mod off however the setting
    /// is restored. And a fault latch is set once and cleared only on a simulation lifecycle
    /// hook, which a region test never calls, so one throw would disable drawing for every test
    /// after this one. Neither failure is loud: a mod left switched off does not fail every test
    /// after it, it fails the ones asserting <i>a mod's</i> drawing and passes the ones asserting
    /// the <i>game's</i> appearance.</para>
    ///
    /// <para>The sweep of the canvases is deliberately total, marks and passes alike, including a
    /// consumer's: see <see cref="Canvas.ResetAll"/>.</para>
    /// </summary>
    public static void ResetState()
    {
        _enabled = true;
        Settings.Reset();
        Renderer.ClearFault();
        Canvas.ResetAll();
    }

    /// <summary>
    /// Settings and runtime state in one line, for a failure message. Names the fault latches
    /// explicitly, because "the drawing is switched off" is the explanation a confusing failure
    /// most often has, and it belongs in the message rather than in <c>godot.log</c>.
    /// </summary>
    public static string DescribeState() =>
        $"{Settings.Describe()} enabled={Enabled} faulted={Faulted} " +
        $"display={HasDisplay} headlessPasses={RunPassesWithoutDisplay} " +
        $"painted={PixelsPaintedLastFrame} {Describe()}";
}
