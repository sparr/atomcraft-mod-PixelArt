using Atomcraft.TestHarness;
using Godot;

namespace PixelArt.Test;

/// <summary>
/// The bitmap font: its glyph tables, its metrics, and the size it picks for a world pixel.
///
/// <para>All of it is arithmetic over strings, with no texture and no display involved, so it
/// runs in the ordinary headless suite. That is the point: the failure mode of a typo in a
/// hand-written glyph table is a silently misaligned atlas, where every character after the bad
/// one draws a sliver of its neighbour, and nobody would notice until they looked closely at a
/// screenshot.</para>
///
/// <para>Unqualified names here resolve to this mod's -- <c>PixelFont</c>, <c>TextSize</c> and
/// <c>Canvas</c> all have namesakes in the harness, and C# prefers the enclosing namespace to a
/// using directive. Where both are meant in one file, say which.</para>
/// </summary>
public static class FontTests
{
    /// <summary>
    /// Every glyph in every size is the shape the atlas builder expects.
    ///
    /// <para>A miscounted row or column would shift every character after it in the atlas without
    /// any error at all.</para>
    /// </summary>
    [GameTest]
    public static void EveryGlyphIsWellFormed() => PixelFont.ValidateAll();

    /// <summary>
    /// The sizes are distinct fonts rather than one font scaled, which is the whole reason for
    /// having three. Checked on the metrics, since a 3x5 glyph magnified is still 3x5.
    /// </summary>
    [GameTest]
    public static void TheThreeSizesAreDistinctFonts()
    {
        Expect(PixelFont.Small, 3, 5, 1, descenders: false);
        Expect(PixelFont.Medium, 5, 7, 2, descenders: false);
        Expect(PixelFont.Large, 9, 13, 3, descenders: true);

        static void Expect(PixelFont font, int w, int h, int spacing, bool descenders)
        {
            if (font.GlyphWidth != w || font.GlyphHeight != h || font.Spacing != spacing)
                throw new AssertionException(
                    $"{font.Name} is {font.GlyphWidth}x{font.GlyphHeight} with {font.Spacing} " +
                    $"spacing, expected {w}x{h} with {spacing}");
            if (font.HasDescenders != descenders)
                throw new AssertionException(
                    $"{font.Name} reports HasDescenders={font.HasDescenders}, expected {descenders}");
        }
    }

    /// <summary>
    /// The advertised geometry: a 3x5 glyph on a 4x6 grid, with the spacing between characters
    /// and not after the last one, so a centered label really is centered.
    /// </summary>
    [GameTest]
    public static void LabelsMeasureToTheAdvertisedGrid()
    {
        // Small: 3x5 glyphs, 1 spacing. A lone glyph measures its lit area; a second costs a full
        // advance; a second line costs a line height. The other sizes are the same arithmetic
        // with their own numbers, which is the whole contract of a bitmap font.
        Expect(new Vector2I(3, 5), Canvas.MeasureLabel("7"), "one Small character");
        Expect(new Vector2I(7, 5), Canvas.MeasureLabel("42"), "two Small characters");
        Expect(new Vector2I(3, 11), Canvas.MeasureLabel("4\n2"), "two Small lines");

        // Medium: 5x7 glyphs, 2 spacing.
        Expect(new Vector2I(5, 7), Canvas.MeasureLabel("7", TextSize.Medium), "one Medium character");
        Expect(new Vector2I(12, 7), Canvas.MeasureLabel("42", TextSize.Medium), "two Medium characters");
        Expect(new Vector2I(5, 16), Canvas.MeasureLabel("4\n2", TextSize.Medium), "two Medium lines");

        // Large: 9x13 glyphs, 3 spacing.
        Expect(new Vector2I(9, 13), Canvas.MeasureLabel("7", TextSize.Large), "one Large character");
        Expect(new Vector2I(21, 13), Canvas.MeasureLabel("42", TextSize.Large), "two Large characters");
        Expect(new Vector2I(9, 29), Canvas.MeasureLabel("4\n2", TextSize.Large), "two Large lines");

        // Scale multiplies whichever size was named.
        Expect(new Vector2I(42, 26), Canvas.MeasureLabel("42", TextSize.Large, scale: 2),
               "two Large characters at 2x");

        static void Expect(Vector2I want, Vector2I got, string what)
        {
            if (want != got)
                throw new AssertionException($"{what} should measure {want}, measured {got}");
        }
    }

    /// <summary>
    /// Auto picks the largest size that fits, and falls back rather than drawing nothing.
    ///
    /// <para>Pure arithmetic over the three sizes' metrics: what a pixel of a given size can hold
    /// does not depend on there being a screen.</para>
    /// </summary>
    [GameTest]
    public static void AutoPicksTheLargestSizeThatFits()
    {
        // "42" needs 21x13 in Large, 12x7 in Medium, 7x5 in Small.
        Expect(new Vector2(96, 96), PixelFont.Large, "a box with room for anything");
        Expect(new Vector2(21, 13), PixelFont.Large, "a box that fits Large exactly");
        Expect(new Vector2(20, 13), PixelFont.Medium, "a box one screen pixel too narrow for Large");
        Expect(new Vector2(21, 12), PixelFont.Medium, "a box one screen pixel too short for Large");
        Expect(new Vector2(12, 7), PixelFont.Medium, "a box that fits Medium exactly");
        Expect(new Vector2(11, 7), PixelFont.Small, "a box one screen pixel too narrow for Medium");
        Expect(new Vector2(7, 5), PixelFont.Small, "a box that fits Small exactly");

        // Nothing fits, and the smallest is drawn anyway: a label spilling past its pixel can still
        // be read, and an empty pixel is indistinguishable from one nobody labelled.
        Expect(new Vector2(2, 2), PixelFont.Small, "a box too small for any size");

        // Scale is part of what has to fit, so raising it steps down through the sizes rather than
        // overflowing.
        if (PixelFont.LargestFitting("42", new Vector2(21, 13), scale: 2) != PixelFont.Small)
            throw new AssertionException(
                "at 2x, \"42\" needs 42x26 in Large and 24x14 in Medium, so a 21x13 box should " +
                $"fall to Small; got {PixelFont.LargestFitting("42", new Vector2(21, 13), 2)}");

        static void Expect(Vector2 box, PixelFont want, string what)
        {
            var got = PixelFont.LargestFitting("42", box);
            if (got != want)
                throw new AssertionException($"{what} ({box}) chose {got}, expected {want}");
        }
    }

    /// <summary>
    /// Every glyph is one connected shape, so no character has a piece floating loose from the
    /// rest of it.
    ///
    /// <para><b>Written because one did.</b> The 9x13 lowercase 'u' had its bowl and its right
    /// stem separated by two unlit columns: it validated, it measured correctly, it drew, and it
    /// looked like a broken character. Nothing else here could see that, because every other test
    /// is about the table's shape rather than the letterform's.</para>
    ///
    /// <para>Connectivity is diagonal as well as orthogonal, since a bitmap font joins strokes
    /// corner to corner all the time. The allowlist is the characters that are genuinely made of
    /// separate parts -- a dot over an i, the two halves of a colon -- and it is spelled out
    /// rather than inferred, so adding a new glyph in two pieces is a decision somebody records
    /// here rather than a silent exception.</para>
    /// </summary>
    [GameTest]
    public static void EveryGlyphIsOnePiece()
    {
        // Characters drawn in more than one piece on purpose.
        var manyPieces = new HashSet<char> { '!', '"', '\'', ':', ';', '?', 'i', 'j', '=', '%', '#' };

        foreach (var font in PixelFont.BySizeDescending)
        foreach (var c in Printable())
        {
            if (manyPieces.Contains(c) || c == ' ')
                continue;

            var pieces = Pieces(font, c);
            if (pieces > 1)
                throw new AssertionException(
                    $"the {font.Name} '{c}' is drawn in {pieces} disconnected pieces, so part of " +
                    "it floats loose from the rest. Either the glyph table has a gap in it, or " +
                    "the character belongs in this test's allowlist of ones drawn in parts.");
        }

        static IEnumerable<char> Printable()
        {
            for (var c = ' '; c <= '~'; c++)
                yield return c;
        }
    }

    /// <summary>
    /// How many separate 8-connected shapes a glyph is made of, read out of the font's own atlas
    /// so the test sees exactly what is drawn rather than a copy of the table.
    /// </summary>
    private static int Pieces(PixelFont font, char c)
    {
        var region = font.Region(c);
        var image = font.Atlas.GetImage();
        var x0 = (int)region.Position.X;
        var y0 = (int)region.Position.Y;
        var w = (int)region.Size.X;
        var h = (int)region.Size.Y;

        var lit = new bool[w, h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            lit[x, y] = image.GetPixel(x0 + x, y0 + y).A > 0.5f;

        var seen = new bool[w, h];
        var pieces = 0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (!lit[x, y] || seen[x, y])
                continue;
            pieces++;
            var stack = new Stack<(int X, int Y)>();
            stack.Push((x, y));
            while (stack.Count > 0)
            {
                var (px, py) = stack.Pop();
                if (px < 0 || py < 0 || px >= w || py >= h || seen[px, py] || !lit[px, py])
                    continue;
                seen[px, py] = true;
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    stack.Push((px + dx, py + dy));
            }
        }
        return pieces;
    }

    /// <summary>
    /// Fit picks a scale as well as a font, so a label grows with the pixel it is drawn on and
    /// stops being drawn once it cannot fit inside it.
    ///
    /// <para>Arithmetic over the metrics, so it runs headless. What it pins down is the contract
    /// the demo leans on: bigger box, bigger text; box too small, scale 0 and nothing drawn.</para>
    /// </summary>
    [GameTest]
    public static void FitScalesWithTheBoxAndRefusesWhenItCannot()
    {
        // "42" is 21x13 in Large, 12x7 in Medium, 7x5 in Small.
        var (font, scale) = PixelFont.FitToBox("42", new Vector2(42, 26));
        if (font != PixelFont.Large || scale != 2)
            throw new AssertionException(
                $"a 42x26 box holds Large at 2x; Fit chose {font} at {scale}x");

        (font, scale) = PixelFont.FitToBox("42", new Vector2(21, 13));
        if (font != PixelFont.Large || scale != 1)
            throw new AssertionException($"a 21x13 box holds Large at 1x; Fit chose {font} at {scale}x");

        // Detail beats size where they disagree. A 24x14 box holds Large at 1x (21x13) and
        // Medium at 2x (24x14), and the Medium would be a screen pixel taller -- but a doubled
        // 5x7 glyph is still a 5x7 glyph, and Large has the bowls and descenders that is all for.
        (font, scale) = PixelFont.FitToBox("42", new Vector2(24, 14));
        if (font != PixelFont.Large)
            throw new AssertionException(
                $"a 24x14 box holds Large at 1x (21x13) and Medium at 2x (24x14); Fit chose " +
                $"{font} at {scale}x, preferring the magnified smaller font");

        // Too small for anything: scale 0, which the drawing reads as "do not draw".
        (_, scale) = PixelFont.FitToBox("42", new Vector2(6, 4));
        if (scale != 0)
            throw new AssertionException(
                $"a 6x4 box cannot hold \"42\" in any size, but Fit returned scale {scale}. " +
                "Fit has to refuse rather than overflow; that is the whole difference from Auto.");

        // And it grows without bound as the view zooms in, up to the cap.
        (_, scale) = PixelFont.FitToBox("42", new Vector2(1000, 1000), maxScale: 4);
        if (scale != 4)
            throw new AssertionException($"Fit should stop at the cap of 4; it returned {scale}");
    }

    /// <summary>
    /// Every character has a slot of its own in the atlas, and one it cannot draw falls back to
    /// '?' rather than to a blank or a crash.
    ///
    /// <para>The composed characters are the ones worth checking. <c>≠</c> is not drawn by hand:
    /// it is the font's own <c>=</c> and <c>/</c> blitted into one slot, which is right at every
    /// size by construction and follows any later correction to either source glyph. What could
    /// silently go wrong is the bookkeeping -- a slot count that does not include the extras
    /// leaves the last character reading off the end of the atlas -- so this checks the regions
    /// are distinct and inside it.</para>
    /// </summary>
    [GameTest]
    public static void ComposedAndUnknownCharactersHaveSlotsOfTheirOwn()
    {
        foreach (var font in PixelFont.BySizeDescending)
        {
            var digit = font.Region('0');
            var notEqual = font.Region(PixelFont.NotEqual);
            var unknown = font.Region('☃');          // a snowman, which no size draws
            var question = font.Region('?');

            if (notEqual.Position.X == digit.Position.X)
                throw new AssertionException(
                    $"{font.Name} puts '{PixelFont.NotEqual}' and '0' in the same atlas slot");
            if (notEqual.End.X > font.AtlasWidth)
                throw new AssertionException(
                    $"{font.Name} places '{PixelFont.NotEqual}' at {notEqual}, which runs past " +
                    $"the atlas at {font.AtlasWidth} pixels wide. The slot count is not counting " +
                    "the composed characters.");
            if (unknown != question)
                throw new AssertionException(
                    $"{font.Name} draws an unknown character from {unknown} rather than from the " +
                    $"'?' slot at {question}");
        }
    }
}
