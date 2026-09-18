using Atomcraft.TestHarness;
using Godot;

namespace PixelArt.Test;

/// <summary>
/// Fitting a label to a pixel: what it shows, how it breaks it, and what it does when there is
/// not room for any of it.
///
/// <para>All headless. <see cref="LabelLayout"/> touches no game type on purpose -- it is
/// arithmetic over strings and a box -- so the everyday suite can hold it to its contract rather
/// than leaving it to whoever next looks at a screenshot of a bank of machines.</para>
/// </summary>
public static class LabelLayoutTests
{
    /// <summary>A pixel at the game's own maximum zoom: 12 screen pixels square.</summary>
    private static readonly Vector2 Tiny = new(12, 12);

    /// <summary>A pixel at 8x the game's maximum, which is where a zoom mod gets to.</summary>
    private static readonly Vector2 Roomy = new(96, 96);

    /// <summary>
    /// With room, the whole name is shown; without, it is cut, and a cut always says so.
    ///
    /// <para>The ellipsis is the load-bearing part. Material names share long prefixes, so dozens
    /// of them cut short land on some <i>other</i> material's real name -- "Carbon" is a material
    /// and so is Carbon Dioxide -- and an unmarked cut reads as a confident answer naming the
    /// wrong thing rather than as a cut.</para>
    /// </summary>
    [GameTest]
    public static void ALabelIsCutToFitAndSaysSo()
    {
        var roomy = LabelLayout.Choose("", "Water", Roomy, scale: 1);
        if (roomy.Text != "Water")
            throw new AssertionException(
                $"a 96x96 pixel has room for \"Water\" whole; it laid out {roomy}");

        // A box where cutting actually buys something: 48x48 leaves 45 screen pixels of room, and
        // the smallest font fits ten characters in that but not twelve.
        var cut = LabelLayout.Choose("", "Praseodymium Chloride", new Vector2(48, 48), scale: 1);
        if (cut.Overflows)
            throw new AssertionException($"a 48x48 pixel should hold some of that name; got {cut}");
        if (cut.Text.Contains("Chloride"))
            throw new AssertionException(
                $"a 48x48 pixel cannot hold the whole of that name; it laid out {cut}");
        if (!cut.Text.Contains(PixelFont.Ellipsis))
            throw new AssertionException(
                $"the cut label {cut} does not carry an ellipsis, so it reads as a whole name that " +
                "happens to be short. Material names share prefixes; an unmarked cut names the " +
                "wrong material.");

        // And where no cut can help, it overflows rather than coming back empty: at the game's own
        // maximum zoom a pixel is 12 screen pixels across, and two characters are 11 wide.
        var tiny = LabelLayout.Choose("", "Praseodymium", Tiny, scale: 1);
        if (tiny.Text.Length == 0)
            throw new AssertionException(
                "a 12x12 pixel laid out nothing at all; an empty pixel looks exactly like a pixel " +
                "the mod had no opinion about, which is why this overflows instead");
        if (!tiny.Overflows)
            throw new AssertionException($"{tiny} claims to fit a 12x12 pixel");
    }

    /// <summary>
    /// A wrapped label breaks at spaces and never inside a word.
    ///
    /// <para>"Praseo" over "dymium" would be two fragments that each read as a word, and a reader
    /// who glanced at one line would carry away something the pixel does not say.</para>
    /// </summary>
    [GameTest]
    public static void WrappingHappensAtSpacesOnly()
    {
        var wrapped = LabelLayout.Choose("", "Carbon Dioxide", Roomy, scale: 1);

        foreach (var line in wrapped.Text.Split('\n'))
        {
            var bare = line.TrimEnd(PixelFont.Ellipsis);
            if (bare.Length == 0)
                continue;
            if (!"Carbon Dioxide".Split(' ').Contains(bare) && bare != "Carbon Dioxide")
                throw new AssertionException(
                    $"the line '{bare}' of {wrapped} is not a whole word of \"Carbon Dioxide\"; " +
                    "wrapping broke inside a word");
        }
    }

    /// <summary>
    /// A wrapped label is a block rather than a staircase: the longest line is made as short as it
    /// can be for the number of lines used.
    /// </summary>
    [GameTest]
    public static void WrappedLinesAreBalanced()
    {
        // Three words of very different lengths. Greedy packing would put "A" and "BB" together
        // and leave the long one alone; balancing does not.
        var fitted = LabelLayout.Choose("", "Aa Bbbbbbbb Cc", new Vector2(64, 96), scale: 1);
        var lines = fitted.Text.Split('\n');
        if (lines.Length < 2)
            Harness.Inapplicable($"that box did not need wrapping; it laid out {fitted}");

        var longest = lines.Max(l => l.Length);
        var shortest = lines.Min(l => l.Length);
        if (longest - shortest > 8)
            throw new AssertionException(
                $"the lines of {fitted} run from {shortest} to {longest} characters, which is a " +
                "staircase rather than a block");
    }

    /// <summary>
    /// A one-word name can be broken across lines, which is the only way a square cell can show it
    /// at the size the cell can actually hold.
    ///
    /// <para><b>Measured, not asserted in the abstract.</b> "Water" has no space, so under
    /// <see cref="Breaking.Words"/> it has exactly one arrangement -- itself, on one line -- and is
    /// limited by being five glyphs across however much height the cell has going spare. On a
    /// 32-screen-pixel cell that is a glyph height of 5 against the 11 two lines fit.</para>
    /// </summary>
    [GameTest]
    public static void AOneWordNameCanBeBrokenToFillTheCell()
    {
        var box = new Vector2(32, 32);
        var words = LabelLayout.Choose("", "Water", box, scale: 1);
        var anywhere = LabelLayout.Choose("", "Water", box, scale: 1,
                                          breaking: Breaking.Anywhere);

        if (anywhere.Text.Replace("\n", "") != "Water")
            throw new AssertionException($"the whole name should still be shown; got {anywhere}");
        if (anywhere.Lines < 2)
            throw new AssertionException(
                $"a one-word name was not broken at all: {anywhere}. Words gave {words}");

        var wordsSize = words.Font.GlyphHeight * words.Scale;
        var anySize = anywhere.Font.GlyphHeight * anywhere.Scale;
        if (anySize <= wordsSize)
            throw new AssertionException(
                $"breaking bought nothing: {anywhere} at {anySize} against {words} at {wordsSize}");
    }

    /// <summary>
    /// Where a cell is too small for a one-line name, breaking shows the whole name rather than one
    /// letter and an ellipsis.
    /// </summary>
    [GameTest]
    public static void BreakingShowsAWholeNameWhereCuttingShowedALetter()
    {
        var box = new Vector2(16, 16);
        var cut = LabelLayout.Choose("", "Water", box, scale: 1);
        var whole = LabelLayout.Choose("", "Water", box, scale: 1, breaking: Breaking.Anywhere);

        if (!cut.Text.Contains(PixelFont.Ellipsis))
            Harness.Inapplicable(
                $"a 16px cell now holds that name without cutting ({cut}), so there is nothing for " +
                "breaking to rescue; pick a smaller cell");

        if (whole.Text.Replace("\n", "") != "Water")
            throw new AssertionException(
                $"breaking still did not show the whole name: {whole} against {cut}");
    }

    /// <summary>
    /// A broken word is priced, not free: a bigger candidate that breaks a word loses to a smaller
    /// one that does not, unless it is enough bigger.
    ///
    /// <para>The two cases either side of the line, at the default cost of 0.4. "Water" on a
    /// 40-pixel cell gains 1.571x in glyph size by breaking, which is worth a break; on a 24-pixel
    /// cell it gains exactly 1.4x, which is not. That the second is a boundary case is deliberate --
    /// it is where a rule that had stopped being applied at all would show first.</para>
    ///
    /// <para><b>What this does not test.</b> The price compounds per break, and that is not
    /// asserted here because it currently cannot be: across 1771 layouts -- 23 material names by 77
    /// cell sizes -- compounding and a flat price choose identically. The candidate set is why. When
    /// two candidates measure the same size the one with fewer breaks wins under any positive
    /// price, and when they differ there is almost always a one-break candidate between them that
    /// dominates both. Compounding is kept because it is the defensible policy, not because
    /// anything here depends on it; see <see cref="LabelLayout.WordSplitCost"/>.</para>
    /// </summary>
    [GameTest]
    public static void BreakingAWordIsPricedRatherThanFree()
    {
        var worthIt = LabelLayout.Choose("", "Water", new Vector2(40, 40), scale: 1,
                                         breaking: Breaking.Anywhere);
        if (worthIt.WordBreaks != 1)
            throw new AssertionException(
                $"a 1.571x size gain is worth one break and was not taken: {worthIt}");

        var notWorthIt = LabelLayout.Choose("", "Water", new Vector2(24, 24), scale: 1,
                                            breaking: Breaking.Anywhere);
        if (notWorthIt.WordBreaks != 0)
            throw new AssertionException(
                $"a 1.4x size gain is not worth a break at a cost of 0.4, but one was taken: " +
                $"{notWorthIt}");

        // The premise: a bigger broken candidate really is on offer there, so the assertion above
        // is the price rejecting it rather than nothing having proposed it.
        var free = LabelLayout.Choose("", "Water", new Vector2(24, 24), scale: 1,
                                      breaking: Breaking.Anywhere, wordSplitCost: 0f);
        if (free.WordBreaks == 0)
            throw new AssertionException(
                $"with the cost at 0 a broken candidate should win on size alone; got {free}. " +
                "Without one, the check above proves nothing.");
        if (free.Font.GlyphHeight * free.Scale <= notWorthIt.Font.GlyphHeight * notWorthIt.Scale)
            throw new AssertionException(
                $"the rejected candidate {free} is not actually bigger than {notWorthIt}, so the " +
                "price was not what rejected it");
    }

    /// <summary>
    /// Wrapping drops the spaces at the ends of lines, and leaves a single line's spacing alone.
    /// </summary>
    [GameTest]
    public static void WrappedLinesCarryNoEdgeSpaces()
    {
        foreach (var body in new[] { "Sulfuric Acid", "Carbon Dioxide", "Praseodymium Chloride" })
        foreach (var cell in new[] { 16, 24, 32, 48, 64 })
        {
            var fitted = LabelLayout.Choose("", body, new Vector2(cell, cell), scale: 1,
                                            breaking: Breaking.Anywhere);
            var lines = fitted.Text.Split('\n');
            if (lines.Length < 2)
                continue;

            foreach (var line in lines)
                if (line.StartsWith(' ') || line.EndsWith(' '))
                    throw new AssertionException(
                        $"the line '{line}' of {fitted} has a space at an end: a leading one is an " +
                        "indent nobody asked for and a trailing one is a column spent on nothing, " +
                        "and either can be the column that costs the label a font size");
        }
    }

    /// <summary>
    /// The optical offset lifts text with no descender and leaves text with one alone.
    /// </summary>
    [GameTest]
    public static void OpticalCenteringShiftsOnlyWhatSitsHigh()
    {
        foreach (var font in PixelFont.BySizeDescending)
        {
            var plain = LabelLayout.OpticalCenterOffset("TOP", font, 1);
            var tailed = LabelLayout.OpticalCenterOffset("Typ", font, 1);

            if (plain <= 0f)
                throw new AssertionException(
                    $"{font.Name} does not shift 'TOP', which has {font.DescenderDepth} empty " +
                    "row(s) under it in its measured box");
            if (tailed != 0f)
                throw new AssertionException(
                    $"{font.Name} shifted 'Typ' by {tailed}, but its descender rows are ink");

            // Only the last line decides, since earlier lines' tails fall inside the line spacing
            // that is already counted.
            if (LabelLayout.OpticalCenterOffset("Typ\nTOP", font, 1) != plain)
                throw new AssertionException(
                    $"{font.Name} let a descender on an earlier line change the offset");

            if (LabelLayout.OpticalCenterOffset("TOP", font, 2) != plain * 2f)
                throw new AssertionException($"{font.Name} did not scale the offset");
        }
    }

    /// <summary>
    /// A caller can ask for more lines than the default, which is the only way to name a long
    /// material whole.
    ///
    /// <para><b>Why the default is not enough.</b> Material names run past forty characters, and
    /// three lines of them do not fit a pixel at a readable size, so the layout cuts and the label
    /// ends in an ellipsis. Naming the thing is sometimes worth more than keeping the label
    /// compact, and that is a judgement about one caller's labels rather than about labels in
    /// general -- so <see cref="LabelLayout.MaxLines"/> is a default and this is the override.</para>
    /// </summary>
    [GameTest]
    public static void ACallerCanAskForMoreLinesThanTheDefault()
    {
        // Five words, so there is something to wrap; long enough that three lines cannot hold it
        // in a pixel this size. A real name of this shape is what prompted the parameter.
        const string longName = "Ammonium Iron Sulfate Dodecahydrate Crystal";
        var box = new Vector2(72, 96);

        var byDefault = LabelLayout.Choose("", longName, box, scale: 1);
        var roomier = LabelLayout.Choose("", longName, box, scale: 1, maxLines: 6);

        var defaultLines = byDefault.Text.Split('\n').Length;
        if (defaultLines > LabelLayout.MaxLines)
            throw new AssertionException(
                $"the default laid out {defaultLines} lines, past the {LabelLayout.MaxLines} it " +
                $"documents: {byDefault}");

        // The premise. If the default already showed the whole name, this test proves nothing
        // about the override, and it should say so rather than passing quietly.
        if (!byDefault.Text.Contains(PixelFont.Ellipsis))
            Harness.Inapplicable(
                $"a {box} box already holds that name in {LabelLayout.MaxLines} lines ({byDefault}), " +
                "so there is nothing for more lines to buy; pick a smaller box or a longer name");

        if (roomier.Text.Split('\n').Length <= LabelLayout.MaxLines)
            throw new AssertionException(
                $"maxLines: 6 laid out {roomier.Text.Split('\n').Length} line(s), so the limit was " +
                $"not raised: {roomier}");
        if (roomier.Text.Contains(PixelFont.Ellipsis))
            throw new AssertionException(
                $"the whole name still did not fit in six lines: {roomier}");
        if (roomier.Text.Replace("\n", " ") != longName)
            throw new AssertionException(
                $"six lines showed \"{roomier.Text.Replace("\n", " ")}\" rather than the whole name");
    }

    /// <summary>
    /// A limit below one is clamped rather than producing a label with no lines in it, and the
    /// wrapping rules do not change just because the budget did.
    /// </summary>
    [GameTest]
    public static void AnImpossibleLineLimitIsClamped()
    {
        var single = LabelLayout.Choose("", "Carbon Dioxide", new Vector2(96, 96), scale: 1, maxLines: 0);

        if (single.Text.Length == 0)
            throw new AssertionException("maxLines: 0 laid out nothing at all");
        if (single.Text.Contains('\n'))
            throw new AssertionException(
                $"maxLines: 0 should clamp to one line; it laid out {single}");

        // Raising the budget must not start breaking words, which is the rule a bigger budget
        // would be most tempting to relax.
        var many = LabelLayout.Choose("", "Carbon Dioxide", new Vector2(40, 96), scale: 1, maxLines: 8);
        foreach (var line in many.Text.Split('\n'))
        {
            var bare = line.TrimEnd(PixelFont.Ellipsis);
            if (bare.Length == 0)
                continue;
            if (!"Carbon Dioxide".Split(' ').Contains(bare) && bare != "Carbon Dioxide")
                throw new AssertionException(
                    $"with eight lines available the line '{bare}' of {many} is not a whole word; " +
                    "a bigger line budget must not start breaking words");
        }
    }

    /// <summary>
    /// Stacking a mark above the body buys a bigger font when the body is what needs the width.
    ///
    /// <para>This is the whole reason the arrangement exists: a square pixel has as much height as
    /// width, and one line of text uses almost none of the height.</para>
    /// </summary>
    [GameTest]
    public static void AMarkIsStackedWhenThatBuysSomething()
    {
        var stacked = LabelLayout.Choose("=", "Water", new Vector2(48, 48), scale: 1);
        var flat = LabelLayout.Choose("", "=Water", new Vector2(48, 48), scale: 1);

        if (stacked.Font.GlyphHeight * stacked.Scale < flat.Font.GlyphHeight * flat.Scale)
            throw new AssertionException(
                $"stacking gave {stacked} and one line gave {flat}; the stacked arrangement is " +
                "supposed to be at least as large, since that is what it is for");

        if (!stacked.Text.StartsWith("="))
            throw new AssertionException($"the mark was dropped: {stacked}");
    }

    /// <summary>
    /// There is always an answer, and when nothing fits it says so rather than pretending.
    ///
    /// <para>A caller that would rather draw nothing than overflow checks
    /// <see cref="FittedText.Overflows"/>; one that would rather show something illegible than
    /// nothing ignores it. Both are reasonable, which is why this returns the label and the fact
    /// rather than choosing for them.</para>
    /// </summary>
    [GameTest]
    public static void NothingFittingIsStillAnAnswer()
    {
        var hopeless = LabelLayout.Choose("", "Praseodymium Chloride", new Vector2(4, 4), scale: 1);

        if (hopeless.Text.Length == 0)
            throw new AssertionException(
                "a 4x4 box laid out nothing at all; an empty pixel looks exactly like a pixel the " +
                "mod had no opinion about");
        if (!hopeless.Overflows)
            throw new AssertionException(
                $"{hopeless} claims to fit a 4x4 box, which is smaller than any glyph");
        if (hopeless.Scale < 1)
            throw new AssertionException($"{hopeless} has a scale below 1, which cannot be drawn");
    }

    /// <summary>
    /// The player's magnification makes labels bigger rather than shorter.
    ///
    /// <para>The subtle half of the contract. Judging the fit <i>after</i> the multiplier would
    /// mean a player who asked for bigger text got less text instead, which is the opposite of
    /// what they asked for -- so the fit is judged at scale 1 and the multiplier applied to the
    /// result, which may push it past the pixel.</para>
    /// </summary>
    [GameTest]
    public static void ThePlayersScaleMakesLabelsBiggerNotShorter()
    {
        var plain = LabelLayout.Choose("", "Water", Roomy, scale: 1);
        var doubled = LabelLayout.Choose("", "Water", Roomy, scale: 2);

        if (doubled.Text != plain.Text)
            throw new AssertionException(
                $"at scale 1 the label is {plain} and at scale 2 it is {doubled}; asking for " +
                "bigger text returned different text");
        if (doubled.Scale != plain.Scale * 2)
            throw new AssertionException(
                $"scale 2 should double the magnification of {plain}; it gave {doubled}");
    }

    /// <summary>
    /// A label that fits is really inside the room the layout says it has, which is what makes
    /// "fits" mean anything.
    /// </summary>
    [GameTest]
    public static void AFittingLabelIsActuallyInside()
    {
        foreach (var box in new[] { Tiny, new Vector2(24, 24), new Vector2(48, 48), Roomy })
        foreach (var body in new[] { "Water", "Carbon Dioxide", "Praseodymium Chloride", "Ice" })
        {
            var fitted = LabelLayout.Choose("=", body, box, scale: 1);
            if (fitted.Overflows)
                continue;

            var room = LabelLayout.RoomIn(box);
            if (fitted.Size.X > room.X || fitted.Size.Y > room.Y)
                throw new AssertionException(
                    $"{fitted} claims to fit a {box} pixel, but it measures {fitted.Size} against " +
                    $"{room} of room");
        }
    }

    /// <summary>Every size draws the cut mark, since every size can be the one a cut lands in.</summary>
    [GameTest]
    public static void EverySizeDrawsTheEllipsis()
    {
        foreach (var font in PixelFont.BySizeDescending)
        {
            var lit = font.LitPixels(PixelFont.Ellipsis);
            if (lit == 0)
                throw new AssertionException(
                    $"the {font.Name} font draws nothing for the ellipsis, so a cut label in that " +
                    "size ends in a blank and reads as a whole word");

            if (font.Region(PixelFont.Ellipsis).Position.X == font.Region('?').Position.X)
                throw new AssertionException(
                    $"the {font.Name} ellipsis has no slot of its own and falls back to '?'");
        }
    }
}
