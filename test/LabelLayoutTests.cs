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
