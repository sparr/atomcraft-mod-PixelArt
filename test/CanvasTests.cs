using Atomcraft.TestHarness;
using Godot;

namespace PixelArt.Test;

/// <summary>
/// The canvas registry and the bookkeeping around it: who owns what, what a
/// <see cref="Canvas.Clear"/> reaches, and what happens with no display.
///
/// <para>None of this needs a screen. Whether a mark reaches the framebuffer is
/// <see cref="ScreenTests"/>; this is the half that can fail on a headless run and should.</para>
///
/// <para><b>Isolation between mods is the claim worth testing here.</b> The harness's overlay was
/// one global surface with one <c>Clear</c>, which is fine when one mod owns it and wrong as soon
/// as two do: whichever cleared last would take the other's marks with it, and the symptom would
/// be a mod that intermittently draws nothing for reasons in somebody else's code.</para>
/// </summary>
public static class CanvasTests
{
    /// <summary>Canvases these tests make, dropped afterwards so a run does not accumulate layers.</summary>
    private const string A = "pixelart.test.a";
    private const string B = "pixelart.test.b";

    private static void ForgetTestCanvases()
    {
        Canvas.Forget(A);
        Canvas.Forget(B);
    }

    /// <summary>
    /// One name is one canvas, so a mod need not hold a reference in a static if it would rather
    /// ask for its canvas where it draws.
    /// </summary>
    [GameTest]
    public static void OneNameIsOneCanvas()
    {
        try
        {
            if (!ReferenceEquals(Canvas.For(A), Canvas.For(A)))
                throw new AssertionException(
                    "asking twice for the same owner gave two canvases, so a mod that does not " +
                    "hold on to its own would silently draw on a second layer");

            if (Canvas.All.Count(c => c.Owner == A) != 1)
                throw new AssertionException($"'{A}' is registered more than once");
        }
        finally
        {
            ForgetTestCanvases();
        }
    }

    /// <summary>
    /// Asking for somebody else's canvas on a different layer is refused rather than moving
    /// their drawing.
    ///
    /// <para>A collision here is two mods that picked the same owner name, and the failure it
    /// would otherwise produce -- each clearing the other's marks -- is the kind that gets
    /// reported against the wrong mod.</para>
    /// </summary>
    [GameTest]
    public static void AskingForSomebodyElsesCanvasOnAnotherLayerIsRefused()
    {
        try
        {
            var first = Canvas.For(A, layer: 120);
            try
            {
                Canvas.For(A, layer: 121);
            }
            catch (ArgumentException)
            {
                if (first.Layer != 120)
                    throw new AssertionException(
                        $"the refusal still moved the canvas, which is now on layer {first.Layer}");
                return;
            }
            throw new AssertionException(
                "a second layer for an existing owner was accepted; one of the two mods would " +
                "then be drawing somewhere it did not ask for");
        }
        finally
        {
            ForgetTestCanvases();
        }
    }

    /// <summary>
    /// A mod's <see cref="Canvas.Clear"/> reaches its own marks and nobody else's, and
    /// <see cref="Canvas.Reset"/> additionally drops its passes.
    /// </summary>
    [GameTest]
    public static void ClearingOneCanvasLeavesTheOtherAlone()
    {
        try
        {
            var a = Canvas.For(A);
            var b = Canvas.For(B);

            a.Fill(new Vector2I(1, 1), Colors.Red);
            a.Outline(new Vector2I(2, 2), Colors.Red);
            b.Label(new Vector2I(3, 3), "b", Colors.Lime);
            b.SetPass("b.pass", _ => { });

            if (a.MarkCount != 2 || b.MarkCount != 1)
                throw new AssertionException(
                    $"marks landed on the wrong canvas: a={a.MarkCount} (expected 2), " +
                    $"b={b.MarkCount} (expected 1)");

            a.Clear();
            if (a.MarkCount != 0)
                throw new AssertionException($"Clear left {a.MarkCount} marks standing");
            if (b.MarkCount != 1)
                throw new AssertionException(
                    "clearing one canvas took another mod's marks with it, which is the whole " +
                    "reason each mod gets a canvas of its own");

            if (b.PassCount != 1)
                throw new AssertionException($"b has {b.PassCount} passes, expected 1");
            b.Clear();
            if (b.PassCount != 1)
                throw new AssertionException(
                    "Clear dropped a pass. Clear is for marks; a pass is registered deliberately " +
                    "and outlives it, or a mod would have to re-register every frame.");
            b.Reset();
            if (b.PassCount != 0 || b.MarkCount != 0)
                throw new AssertionException(
                    $"Reset left {b.MarkCount} marks and {b.PassCount} passes");
        }
        finally
        {
            ForgetTestCanvases();
        }
    }

    /// <summary>
    /// A pass or painter registered under a name already in use replaces it, rather than adding a
    /// second one that draws the same thing twice.
    /// </summary>
    [GameTest]
    public static void RegisteringTheSameNameTwiceReplaces()
    {
        try
        {
            var a = Canvas.For(A);
            a.SetPainter("one", _ => { });
            a.SetPainter("one", _ => { });
            a.SetPass("one", _ => { });
            if (a.PassCount != 1)
                throw new AssertionException(
                    $"three registrations of the name 'one' left {a.PassCount} of them; a mod " +
                    "re-registering on a settings change would accumulate passes");

            a.Remove("one");
            if (a.PassCount != 0)
                throw new AssertionException("Remove did not remove it");
            a.Remove("never registered");    // not an error
        }
        finally
        {
            ForgetTestCanvases();
        }
    }

    /// <summary>
    /// Without a display everything is inert and says so once, rather than throwing. A mod that
    /// takes a canvas and registers a painter in its <c>Initialize</c> should not have to know
    /// whether this particular run has a screen.
    /// </summary>
    [GameTest]
    public static void DrawingIsInertWithoutADisplay()
    {
        if (DisplayServer.GetName() != "headless")
            Harness.Inapplicable("this run has a display, so there is nothing inert to check");

        try
        {
            var a = Canvas.For(A);
            a.Fill(new Vector2I(1, 1), Colors.Red);
            a.Outline(new Vector2I(1, 1), Colors.Red);
            a.Label(new Vector2I(1, 1), "x", Colors.Red);
            a.Arrow(new Vector2I(1, 1), Aim.Up, Colors.Red);
            a.SetPainter("inert", _ => throw new Exception("a painter ran with no display"));
            a.SetPass("inert.pass", _ => throw new Exception("a pass ran with no display"));

            // The immediate-mode calls too: a pass is not called at all headless, but a mod may
            // reach for one of these from somewhere else entirely.
            a.DrawFill(new Rect2(0, 0, 8, 8), Colors.Red);
            a.DrawLabel(new Rect2(0, 0, 8, 8), "x", Colors.Red);

            if (PixelArtApi.PixelsPaintedLastFrame != 0)
                throw new AssertionException(
                    $"a headless run painted {PixelArtApi.PixelsPaintedLastFrame} pixels");
            if (PixelArtApi.HasDisplay)
                throw new AssertionException("HasDisplay is true on a headless run");
            if (a.Faulted)
                throw new AssertionException($"the canvas faulted with no display: {a.Fault}");
        }
        finally
        {
            ForgetTestCanvases();
        }
    }

    /// <summary>
    /// Every shape can be drawn against a block of pixels as well as a single one, for a frame as
    /// well as retained.
    ///
    /// <para>Asymmetry here is a papercut a consumer hits and works around rather than reports:
    /// <c>DrawArrow</c>, <c>DrawIcon</c> and <c>DrawLabel</c> took a <c>RectInt</c> while
    /// <c>DrawFill</c> and <c>DrawOutline</c> did not, so a mod drawing a block had to reach for
    /// <c>ViewGeometry.ScreenRectOf</c> for two of the five and not the others. Checked by
    /// reflection rather than by calling them, so the test is about the shape of the API and runs
    /// with no display.</para>
    /// </summary>
    [GameTest]
    public static void EveryShapeTakesABlockOfPixels()
    {
        var canvas = typeof(Canvas);
        var rect = typeof(RectInt);

        foreach (var name in new[] { "Fill", "Outline", "Arrow", "Icon", "Label",
                                     "DrawFill", "DrawOutline", "DrawArrow", "DrawIcon", "DrawLabel" })
        {
            var takesBlock = canvas.GetMethods()
                .Where(m => m.Name == name)
                .Any(m => m.GetParameters().FirstOrDefault()?.ParameterType == rect);

            if (!takesBlock)
                throw new AssertionException(
                    $"Canvas.{name} has no RectInt overload, so a mod marking a block of pixels " +
                    "has to project the rectangle itself for this one shape and not the others");
        }
    }

    /// <summary>
    /// The player's text scale multiplies what a mod asked for, in measuring as well as in
    /// drawing.
    ///
    /// <para>The two have to agree or a label that picks its own size overflows the pixel it was
    /// measured against, which is the failure this setting would otherwise introduce.</para>
    /// </summary>
    [GameTest]
    public static void ThePlayersTextScaleMultipliesWhatAModAskedFor()
    {
        var restore = Settings.TextScale;
        try
        {
            Settings.TextScale = 1;
            var plain = Canvas.MeasureLabel("42", TextSize.Small);

            Settings.TextScale = 3;
            var scaled = Canvas.MeasureLabel("42", TextSize.Small);

            if (scaled != plain * 3)
                throw new AssertionException(
                    $"at textScale 3 a label measuring {plain} should measure {plain * 3}, " +
                    $"measured {scaled}");

            Settings.TextScale = 0;
            Settings.Validate();
            if (Settings.TextScale != 1)
                throw new AssertionException(
                    $"a textScale of 0 became {Settings.TextScale}, not 1; a label would be " +
                    "drawn at zero size, which is a label nobody can see");

            Settings.TextScale = Settings.MaxTextScale + 5;
            Settings.Validate();
            if (Settings.TextScale != Settings.MaxTextScale)
                throw new AssertionException(
                    $"a textScale past the maximum became {Settings.TextScale}, not " +
                    $"{Settings.MaxTextScale}");
        }
        finally
        {
            Settings.TextScale = restore;
        }
    }

    /// <summary>
    /// A scale below 1 is refused rather than quietly treated as 1: it is always a bug at the
    /// call site, and a silently corrected one is a label at a size nobody chose.
    /// </summary>
    [GameTest]
    public static void AScaleBelowOneIsRefused()
    {
        try
        {
            Canvas.MeasureLabel("x", TextSize.Small, scale: 0);
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new AssertionException("a scale of 0 was accepted");
    }

    /// <summary>
    /// <see cref="TextSize.Auto"/> has no answer without a pixel, and says so rather than picking
    /// one at random.
    /// </summary>
    [GameTest]
    public static void MeasuringAutoIsRefused()
    {
        try
        {
            Canvas.MeasureLabel("x", TextSize.Auto);
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new AssertionException(
            "measuring at TextSize.Auto was accepted, so a caller got a number for a question " +
            "that has no answer without a pixel");
    }
}
