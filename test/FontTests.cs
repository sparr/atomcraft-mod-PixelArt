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
        Expect(PixelFont.Small, 3, 5, descender: 1, h: 1, v: 1);
        Expect(PixelFont.Medium, 5, 7, descender: 1, h: 1, v: 2);
        Expect(PixelFont.Large, 7, 11, descender: 2, h: 1, v: 3);

        static void Expect(PixelFont font, int w, int boxHeight, int descender, int h, int v)
        {
            if (font.GlyphWidth != w || font.GlyphHeight != boxHeight ||
                font.DescenderDepth != descender ||
                font.HorizontalSpacing != h || font.VerticalSpacing != v)
                throw new AssertionException(
                    $"{font.Name} is {font.GlyphWidth}x{font.GlyphHeight}+{font.DescenderDepth} " +
                    $"with {font.HorizontalSpacing}/{font.VerticalSpacing} spacing, expected " +
                    $"{w}x{boxHeight}+{descender} with {h}/{v}");
            if (!font.HasDescenders)
                throw new AssertionException(
                    $"{font.Name} reports no descenders despite a {font.DescenderDepth}-row zone");
        }
    }

    /// <summary>
    /// The advertised geometry: the horizontal spacing falls between characters and not after the
    /// last one, so a centered label really is centered, and the descender is counted on every
    /// measurement whether or not the text has one.
    ///
    /// <para>That second half is the one worth pinning. A measurement is what a fit is judged
    /// against, so a measurement that left the descender out would call "Deep" a fit and then draw
    /// its tail outside the pixel; and a live label would jump a row as a descender came and went,
    /// because a centered label is placed from its own measurement.</para>
    /// </summary>
    [GameTest]
    public static void LabelsMeasureToTheAdvertisedGrid()
    {
        // Small: a 3x5 box plus 1 descender row, 1 horizontal and 1 vertical spacing. A lone glyph
        // measures its box and descender; a second costs a full advance; a second line costs a line
        // height. The other sizes are the same arithmetic with their own numbers, which is the
        // whole contract of a bitmap font.
        Expect(new Vector2I(3, 6), Canvas.MeasureLabel("7"), "one Small character");
        Expect(new Vector2I(7, 6), Canvas.MeasureLabel("42"), "two Small characters");
        Expect(new Vector2I(3, 12), Canvas.MeasureLabel("4\n2"), "two Small lines");

        // Medium: a 5x7 box plus 1 descender row, 1 and 2 spacing.
        Expect(new Vector2I(5, 8), Canvas.MeasureLabel("7", TextSize.Medium), "one Medium character");
        Expect(new Vector2I(11, 8), Canvas.MeasureLabel("42", TextSize.Medium), "two Medium characters");
        Expect(new Vector2I(5, 17), Canvas.MeasureLabel("4\n2", TextSize.Medium), "two Medium lines");

        // Large: a 7x11 box plus 2 descender rows, 1 and 3 spacing.
        Expect(new Vector2I(7, 13), Canvas.MeasureLabel("7", TextSize.Large), "one Large character");
        Expect(new Vector2I(15, 13), Canvas.MeasureLabel("42", TextSize.Large), "two Large characters");
        Expect(new Vector2I(7, 27), Canvas.MeasureLabel("4\n2", TextSize.Large), "two Large lines");

        // A caps-only label and one with a tail measure the same, which is what keeps a live
        // label's baseline still.
        Expect(Canvas.MeasureLabel("Top", TextSize.Large), Canvas.MeasureLabel("Typ", TextSize.Large),
               "a label with a descender against one without");

        // Scale multiplies whichever size was named.
        Expect(new Vector2I(30, 26), Canvas.MeasureLabel("42", TextSize.Large, scale: 2),
               "two Large characters at 2x");

        static void Expect(Vector2I want, Vector2I got, string what)
        {
            if (want != got)
                throw new AssertionException($"{what} should measure {want}, measured {got}");
        }
    }

    /// <summary>
    /// Every size really draws below the baseline, and only the characters that should.
    ///
    /// <para><b>Nothing else would catch losing it.</b> The descender rows live outside the glyph
    /// box, so the atlas cell, the region and the destination rect all have to be sized by
    /// <see cref="PixelFont.DrawnHeight"/> rather than <see cref="PixelFont.GlyphHeight"/>. Size
    /// any one of them by the box instead and a tail is silently cropped or the whole picture is
    /// squashed into the box -- the glyph still validates, still measures, still draws, and every
    /// other test in this file still passes. This is the one that goes red.</para>
    ///
    /// <para>Counted out of the built atlas data rather than off the table, so it is the bytes a
    /// draw would sample, not the strings they came from.</para>
    /// </summary>
    [GameTest]
    public static void EverySizeDrawsBelowTheBaseline()
    {
        foreach (var font in PixelFont.BySizeDescending)
        {
            foreach (var c in "gjpqy")
            {
                if (BelowBaseline(font, c) == 0)
                    throw new AssertionException(
                        $"the {font.Name} '{c}' lights nothing in its {font.DescenderDepth}-row " +
                        "descender zone, so either the glyph folds its tail into the body or the " +
                        "atlas is being built to the box height and cropping it");
            }

            // And the other direction, or a font that simply lit every row would pass the above.
            foreach (var c in "HOxz70")
            {
                if (BelowBaseline(font, c) != 0)
                    throw new AssertionException(
                        $"the {font.Name} '{c}' has ink below its baseline; only g, j, p, q, y and " +
                        "a few tails belong there, and a cap that reaches down will collide with " +
                        "the line beneath it");
            }
        }

        // Lit pixels of a character that fall inside the descender zone, which is every row of the
        // drawn picture from the box height down.
        static int BelowBaseline(PixelFont font, char c) =>
            font.LitPixels(c) - font.LitPixelsInBox(c);
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
        // "42" needs 15x13 in Large, 11x8 in Medium, 7x6 in Small.
        Expect(new Vector2(96, 96), PixelFont.Large, "a box with room for anything");
        Expect(new Vector2(15, 13), PixelFont.Large, "a box that fits Large exactly");
        Expect(new Vector2(14, 13), PixelFont.Medium, "a box one screen pixel too narrow for Large");
        Expect(new Vector2(15, 12), PixelFont.Medium, "a box one screen pixel too short for Large");
        Expect(new Vector2(11, 8), PixelFont.Medium, "a box that fits Medium exactly");
        Expect(new Vector2(10, 8), PixelFont.Small, "a box one screen pixel too narrow for Medium");
        Expect(new Vector2(7, 6), PixelFont.Small, "a box that fits Small exactly");

        // Nothing fits, and the smallest is drawn anyway: a label spilling past its pixel can still
        // be read, and an empty pixel is indistinguishable from one nobody labelled.
        Expect(new Vector2(2, 2), PixelFont.Small, "a box too small for any size");

        // Scale is part of what has to fit, so raising it steps down through the sizes rather than
        // overflowing.
        if (PixelFont.LargestFitting("42", new Vector2(15, 13), scale: 2) != PixelFont.Small)
            throw new AssertionException(
                "at 2x, \"42\" needs 30x26 in Large and 22x16 in Medium, so a 15x13 box should " +
                $"fall to Small; got {PixelFont.LargestFitting("42", new Vector2(15, 13), 2)}");

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
    /// <para><b>Written because one did.</b> A lowercase 'u' once had its bowl and its right
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
        // Characters drawn in more than one piece on purpose at every size: a dot over a stem, a
        // pair of marks, a glyph whose parts never touch.
        var manyPieces = new HashSet<char> { '!', '"', '\'', ':', ';', '?', 'i', 'j', '=', '%', '#' };

        // And characters that only come apart at one size, because that size forced it.
        //
        // <para>At three columns a diagonal cannot both read as a diagonal and stay connected: a
        // stroke that steps one column per row has nowhere to go, so 's' and 'z' step two and
        // their middles touch nothing. The break is what makes them legible rather than a defect
        // in them.</para>
        //
        // Kept separate from the list above rather than merged into it, so an exemption cannot
        // spread to a size that did not need it. A broken 'z' at 7x11 has eleven rows to avoid it
        // in and still fails here, which is the case that would really be a mistake.
        var manyPiecesAtSize = new Dictionary<string, HashSet<char>>
        {
            ["Small"] = new HashSet<char> { 's', 'z' },
        };

        foreach (var font in PixelFont.BySizeDescending)
        foreach (var c in Printable())
        {
            if (manyPieces.Contains(c) || c == ' ')
                continue;
            if (manyPiecesAtSize.TryGetValue(font.Name, out var atThisSize) && atThisSize.Contains(c))
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
