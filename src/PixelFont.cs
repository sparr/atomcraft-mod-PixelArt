using Godot;

namespace PixelArt;

/// <summary>
/// The bitmap fonts a <see cref="Canvas"/> draws text from, and the atlas each is rendered
/// through.
///
/// <para><b>This is the copy other mods should use.</b> It began as the TestHarness's
/// <c>PixelFont</c> and was copied into a second mod for the usual reason -- a shipped mod
/// must never reference <c>Atomcraft.TestHarness.dll</c>, or it fails to load for every player
/// without the harness -- and the two copies then started to differ. Living here instead, in a
/// mod that ships to players, it can be referenced rather than copied again.</para>
///
/// Overlay text has to stay legible at one simulation pixel per glyph. A pixel is 8 world
/// units across, so at the game's own maximum zoom of 1.5 it is 12 screen pixels wide: any font
/// with real letterforms is illegible at that size, and every scalable font the engine
/// offers blurs, because a glyph outline rasterized at 12px lands on fractional pixel
/// boundaries. A bitmap font drawn at an integer scale, from integer screen positions,
/// through a nearest-neighbor filter, is exact instead: each font pixel is a whole number of
/// screen pixels, every time.
///
/// Three sizes, because one cannot serve the whole zoom range. Zoomed out, only the smallest
/// fits inside a pixel at all; zoomed in, the smallest is a speck in the middle of one. They
/// are separate fonts rather than one font at three scales, because a 3x5 glyph magnified
/// three times is still a 3x5 glyph: it gains size and no detail. Drawing 7x11 properly buys
/// round bowls, real diagonals, and a stroke that stays one pixel wide everywhere.
///
/// <para><b>Three heights, not one.</b> A glyph has a <i>box</i>, which is what a line of text
/// occupies and what a fit is judged against; a <i>descender depth</i> below it, which only g, j,
/// p, q, y and a few tails ever use; and the two together are the <i>drawn</i> picture, which is
/// the shape of an atlas cell. Descenders are drawn into the vertical spacing rather than inside
/// the box, so caps get the whole box and a line of text is the same height either way. The
/// spacing is what pays for them.</para>
///
/// | Size | Box | Descender | Drawn | Spacing (h, v) | Grid |
/// | --- | --- | --- | --- | --- | --- |
/// | <see cref="Small"/> | 3x5 | 1 | 3x6 | 1, 1 | 4x6 |
/// | <see cref="Medium"/> | 5x7 | 1 | 5x8 | 1, 2 | 6x9 |
/// | <see cref="Large"/> | 7x11 | 2 | 7x13 | 1, 3 | 8x14 |
///
/// Every size has real descenders now. Small spends its whole row of vertical spacing on one, so
/// a tail there touches the caps on the line below; that is the deliberate trade at three columns,
/// where the alternative is a q that reads as a g. The other two keep a clear row.
///
/// Digits and capitals are still what the smallest size is best at, and a little punctuation is
/// approximate at 3x5. If a label has to be read exactly, say it in digits and capitals, or use a
/// larger size.
/// </summary>
public sealed partial class PixelFont
{
    /// <summary>3x5 glyphs with a 1-row descender, on a 4x6 grid. The only size that fits inside a pixel when zoomed out.</summary>
    public static readonly PixelFont Small;

    /// <summary>5x7 glyphs with a 1-row descender, on a 6x9 grid.</summary>
    public static readonly PixelFont Medium;

    /// <summary>7x11 glyphs with a 2-row descender, on an 8x14 grid. One-pixel strokes throughout.</summary>
    public static readonly PixelFont Large;

    /// <summary>Every font, largest first. What <see cref="LargestFitting"/> walks.</summary>
    public static readonly IReadOnlyList<PixelFont> BySizeDescending;

    /// <summary>
    /// Built here rather than in the field initializers above. Static field initializers run
    /// in declaration order, and the glyph tables these read are declared at the bottom of the
    /// file where they can be edited without scrolling past them; constructing the fonts up
    /// there would hand each one a null table. A static constructor runs after every field
    /// initializer, so the order on the page stops mattering.
    /// </summary>
    static PixelFont()
    {
        Small = new PixelFont("Small", 3, 5, descenderDepth: 1, horizontalSpacing: 1, verticalSpacing: 1, Glyphs3x5);
        Medium = new PixelFont("Medium", 5, 7, descenderDepth: 1, horizontalSpacing: 1, verticalSpacing: 2, Glyphs5x7);
        Large = new PixelFont("Large", 7, 11, descenderDepth: 2, horizontalSpacing: 1, verticalSpacing: 3, Glyphs7x11);
        BySizeDescending = new[] { Large, Medium, Small };
    }

    private const char First = ' ';
    private const char Last = '~';

    /// <summary>
    /// Characters the tables carry beyond printable ASCII, in the order their slots follow
    /// <see cref="Last"/> in the atlas.
    ///
    /// <para>Only one so far, and it earns its place: a nonmatch filter and a match filter are
    /// opposites, and writing them <c>=</c> and <c>!=</c> spends a whole extra character of a
    /// pixel that is twelve screen pixels wide to say what one glyph says better.</para>
    /// </summary>
    private static readonly char[] Drawn = { Ellipsis };

    /// <summary>U+2260 NOT EQUAL TO. Composed at atlas time; see <see cref="Composed"/>.</summary>
    public const char NotEqual = '\u2260';

    /// <summary>
    /// U+2026 HORIZONTAL ELLIPSIS: the mark on a label that had to be cut short.
    ///
    /// <para>Drawn by hand rather than composed, because there is nothing in printable ASCII to
    /// compose it from -- three full stops in a row are three advances wide, which is most of a
    /// pixel's worth of label spent saying "there was more". At 3x5 it is two dots, since three
    /// do not fit in three columns, and two dots still read as "cut" where a period does
    /// not.</para>
    ///
    /// <para>It earns its place because of what the alternative costs. Material names share long
    /// prefixes, so dozens of them cut short land on some <i>other</i> material's real name:
    /// "Carbon" is a material and so is Carbon Dioxide. An unmarked cut does not read as a cut,
    /// it reads as a confident answer naming the wrong thing.</para>
    /// </summary>
    public const char Ellipsis = '\u2026';

    /// <summary>
    /// Glyphs built by overlaying two ASCII ones rather than drawn by hand.
    ///
    /// <para>A hand-drawn <c>≠</c> would be a fourth row of glyph data to keep in step with
    /// three tables that are already hand-maintained, and the failure mode of getting one
    /// wrong is a character that reads as a different character. Overlaying the font's own
    /// <c>=</c> and <c>/</c> is what the glyph means, is right at every size by construction,
    /// and follows any later correction to either source glyph.</para>
    /// </summary>
    private static readonly (char Char, char Over, char Under)[] Composed =
    {
        (NotEqual, '=', '/'),
    };

    /// <summary>How many slots the atlas has: printable ASCII plus the extras.</summary>
    /// <summary>Printable ASCII, which is what the glyph tables themselves hold.</summary>
    private const int AsciiSlots = Last - First + 1;

    private int SlotCount => AsciiSlots + Drawn.Length + Composed.Length;

    /// <summary>
    /// Which atlas slot a character occupies, or -1 for one this font cannot draw.
    /// </summary>
    private static int SlotOf(char c)
    {
        if (c >= First && c <= Last)
            return c - First;

        // Drawn extras come straight after ASCII, because the tables carry a row for each; the
        // composed ones follow, and have no row of their own.
        var drawn = Array.IndexOf(Drawn, c);
        if (drawn >= 0)
            return AsciiSlots + drawn;

        for (var i = 0; i < Composed.Length; i++)
            if (Composed[i].Char == c)
                return AsciiSlots + Drawn.Length + i;

        return -1;
    }

    private readonly string[] _glyphs;
    private ImageTexture? _atlas;

    private PixelFont(string name, int glyphWidth, int glyphHeight, int descenderDepth,
                      int horizontalSpacing, int verticalSpacing, string[] glyphs)
    {
        // A descender is drawn into the vertical spacing, so a deeper one than there is spacing
        // would land inside the next line's box rather than beside it. Checked rather than
        // trusted: the failure mode is a tail overlapping a cap two rows into a paragraph, which
        // reads as a glyph bug at the character it lands on rather than as a metric.
        if (descenderDepth > verticalSpacing)
            throw new InvalidOperationException(
                $"PixelFont {name} has a {descenderDepth}-row descender under {verticalSpacing} " +
                "row(s) of vertical spacing, so a tail would be drawn over the line below it");

        Name = name;
        GlyphWidth = glyphWidth;
        GlyphHeight = glyphHeight;
        DescenderDepth = descenderDepth;
        HorizontalSpacing = horizontalSpacing;
        VerticalSpacing = verticalSpacing;
        _glyphs = glyphs;
    }

    /// <summary>The name this size is known by, for messages.</summary>
    public string Name { get; }

    /// <summary>Width of one glyph, in font pixels.</summary>
    public int GlyphWidth { get; }

    /// <summary>
    /// Height of the glyph box: cap height, with the baseline on its last row. This is what a line
    /// of text occupies and what a fit is judged against, and it does <b>not</b> include the
    /// descender -- see <see cref="DrawnHeight"/> for the picture an atlas cell holds.
    /// </summary>
    public int GlyphHeight { get; }

    /// <summary>
    /// Rows drawn below the box, for g, j, p, q, y, the tail of Q, and a few marks.
    ///
    /// <para>Never larger than <see cref="VerticalSpacing"/>, so a tail is drawn beside the next
    /// line rather than over it. At <see cref="Small"/> the two are equal, so a tail does touch the
    /// caps below it: three columns leave no other way to tell a q from a g.</para>
    /// </summary>
    public int DescenderDepth { get; }

    /// <summary>
    /// Height of the drawn picture, and of an atlas cell: the box plus the descender. This is the
    /// row count every glyph in the table must have, and the height a glyph is blitted at.
    /// </summary>
    public int DrawnHeight => GlyphHeight + DescenderDepth;

    /// <summary>Blank columns between characters.</summary>
    public int HorizontalSpacing { get; }

    /// <summary>
    /// Blank rows between lines, which is also the room a descender is drawn into. The two are the
    /// same rows on purpose: reserving separate space for a tail most glyphs do not have would make
    /// every line of text taller to no benefit.
    /// </summary>
    public int VerticalSpacing { get; }

    /// <summary>Pen movement per character: the glyph plus its horizontal spacing.</summary>
    public int Advance => GlyphWidth + HorizontalSpacing;

    /// <summary>Row-to-row distance for a multi-line label: the box plus its vertical spacing.</summary>
    public int LineHeight => GlyphHeight + VerticalSpacing;

    /// <summary>
    /// Whether lowercase g, j, p, q and y drop below the baseline. True at every size now; kept as
    /// a question a caller can ask rather than a fact it has to know per size.
    /// </summary>
    public bool HasDescenders => DescenderDepth > 0;

    /// <summary>Width of <see cref="Atlas"/> in pixels: one <see cref="Advance"/>-wide slot per character.</summary>
    public int AtlasWidth => SlotCount * Advance;

    public override string ToString() =>
        $"{Name} ({GlyphWidth}x{GlyphHeight}+{DescenderDepth} on {Advance}x{LineHeight})";

    /// <summary>
    /// The glyph atlas: one row of <see cref="Advance"/>-wide slots in character order, white
    /// where lit and fully transparent elsewhere, so the draw colour comes from the modulate
    /// and one texture serves every colour. Built once, on the first label drawn in this size.
    ///
    /// <para>Public so a mod drawing its own command list can use the font without going
    /// through <see cref="Canvas"/>: pair <c>Atlas.GetRid()</c> with <see cref="Region"/> in a
    /// <c>RenderingServer.CanvasItemAddTextureRectRegion</c> call, and set the item's texture
    /// filter to <c>Nearest</c> or the blocks come out soft. Its lifetime belongs to this mod,
    /// which drops every atlas when the renderer tears down; do not dispose it.</para>
    /// </summary>
    public ImageTexture Atlas =>
        _atlas ??= ImageTexture.CreateFromImage(Image.CreateFromData(
            AtlasWidth, DrawnHeight, useMipmaps: false, Image.Format.Rgba8, BuildAtlasData()));

    /// <summary>
    /// Drops the atlas texture, so it is not still held by a static when the renderer tears
    /// down and reported as a leak. The next label drawn builds it again.
    /// </summary>
    internal void Release()
    {
        _atlas?.Dispose();
        _atlas = null;
    }

    /// <summary>Releases every size's atlas.</summary>
    internal static void ReleaseAll()
    {
        foreach (var font in BySizeDescending)
            font.Release();
    }

    /// <summary>
    /// Where <paramref name="c"/> lives in <see cref="Atlas"/>. Anything outside printable
    /// ASCII draws as '?', which is more useful on screen than a blank or a crash.
    /// </summary>
    public Rect2 Region(char c)
    {
        var index = SlotOf(c);
        if (index < 0)
            index = '?' - First;
        return new Rect2(index * Advance, 0, GlyphWidth, DrawnHeight);
    }

    /// <summary>
    /// The size of a label in font pixels, honoring embedded newlines.
    ///
    /// <para>Trailing horizontal spacing is not counted, so a one-character label in
    /// <see cref="Small"/> measures 3 wide rather than 4: the spacing exists to separate characters
    /// from each other, and counting it would push every centered label half a pixel off.</para>
    ///
    /// <para><b>The descender is always counted, whatever the text says.</b> Two reasons, and the
    /// second is the one that would bite. A measurement is what <see cref="FitToBox"/> and
    /// <see cref="LabelLayout"/> judge a fit against, so leaving the descender out of it would let
    /// "Deep" be called a fit and then draw its tail outside the pixel. And a live label whose text
    /// changes every frame would jump a row up and down as a descender came and went, because a
    /// centered label is placed from its own measurement. Counting it always costs a caps-only
    /// label one or two font pixels of conservatism and buys a baseline that does not move.</para>
    /// </summary>
    public Vector2I Measure(string text)
    {
        var lines = text.Split('\n');
        var widest = 0;
        foreach (var line in lines)
            widest = Math.Max(widest, line.Length);
        if (widest == 0)
            return Vector2I.Zero;
        return new Vector2I(
            widest * Advance - HorizontalSpacing,
            (lines.Length - 1) * LineHeight + DrawnHeight);
    }

    /// <summary>
    /// The largest size whose rendering of <paramref name="text"/> at <paramref name="scale"/>
    /// fits inside <paramref name="box"/>, or <see cref="Small"/> when none of them do.
    ///
    /// Falling back to the smallest rather than refusing is deliberate: a label that does not
    /// quite fit its pixel is still the thing you wanted to read, and a pixel with nothing in it
    /// looks exactly like a pixel your code decided to skip.
    /// </summary>
    public static PixelFont LargestFitting(string text, Vector2 box, int scale = 1)
    {
        foreach (var font in BySizeDescending)
        {
            var size = (Vector2)font.Measure(text) * scale;
            if (size.X <= box.X && size.Y <= box.Y)
                return font;
        }
        return Small;
    }

    /// <summary>
    /// The largest font and the largest whole-number scale whose rendering of
    /// <paramref name="text"/> fits inside <paramref name="box"/>, or a scale of 0 when even the
    /// smallest font at scale 1 does not fit.
    ///
    /// <para>The difference from <see cref="LargestFitting"/> is what it is allowed to change:
    /// that one picks a font at a scale you chose, and this one picks the scale too. That is what
    /// makes a label track the pixel it belongs to as the view zooms -- it grows with the pixel
    /// on the way in, and shrinks on the way out instead of overlapping its neighbours.</para>
    ///
    /// <para><b>Detail first, then size.</b> The most detailed font that fits at all wins, and
    /// only then is it made as large as the box allows. A box that holds Large at 1x and Medium at
    /// 2x takes the Large even though the doubled Medium would be taller: a 5x7 glyph doubled is
    /// still a 5x7 glyph, while 7x11 has round bowls, real diagonals and deeper descenders. That is
    /// the whole reason for having three of them.</para>
    ///
    /// <para>Scale 0 means "do not draw this". A caller that would rather have something
    /// illegible than nothing should use <see cref="LargestFitting"/>, which never refuses.</para>
    /// </summary>
    public static (PixelFont Font, int Scale) FitToBox(string text, Vector2 box, int maxScale = 8)
    {
        // Largest font first, and the first one that fits at all is the answer: detail beats size.
        foreach (var font in BySizeDescending)
        {
            var size = font.Measure(text);
            if (size.X <= 0 || size.Y <= 0)
                continue;

            // The largest whole scale this font fits the box at. Whole, because a bitmap glyph is
            // exact at 2 and resampled mush at 1.5.
            var scale = Math.Min((int)(box.X / size.X), (int)(box.Y / size.Y));
            scale = Math.Min(scale, Math.Max(1, maxScale));
            if (scale >= 1)
                return (font, scale);
        }

        return (Small, 0);
    }

    /// <summary>
    /// Checks every glyph in this size is the shape the atlas builder expects, and throws
    /// naming the offender if one is not.
    ///
    /// The glyph tables are hand-written data, and the failure mode of a typo in one is a
    /// silently misaligned atlas: every character after the bad one draws a sliver of its
    /// neighbour. This is pure arithmetic over strings, with no texture and no display
    /// involved, so the ordinary headless suite can catch that rather than leaving it to
    /// whoever next looks closely at a screenshot.
    /// </summary>
    public void Validate() => BuildAtlasData();

    /// <summary>
    /// How many pixels of a character are lit, counted out of the built atlas data rather than off
    /// the table, so a composed glyph is counted the same way a drawn one is and any slot
    /// arithmetic is counted too.
    ///
    /// <para>No texture and no renderer involved, which is what makes it usable from a headless
    /// test. Not cheap -- it builds the atlas data to answer -- so it is for tests, not frames.</para>
    /// </summary>
    public int LitPixels(char c) => LitRows(c, 0, DrawnHeight);

    /// <summary>
    /// Whether <paramref name="c"/> puts any ink below the baseline, in this size.
    ///
    /// <para>Answered from the glyph table rather than from a list of characters, so it stays right
    /// when a face is redrawn, and it differs by size: the comma descends at every size, the
    /// underscore and Q only in <see cref="Large"/>, and a face that moved a tail back inside its
    /// box would be reported as not descending without anyone having to remember this.</para>
    ///
    /// <para>Cheap after the first call: one pass over the table per size, cached. Unlike
    /// <see cref="LitPixels"/> this is meant to be usable from a frame.</para>
    /// </summary>
    public bool Descends(char c)
    {
        var slot = SlotOf(c);
        if (slot < 0 || DescenderDepth == 0)
            return false;

        _descends ??= BuildDescenderTable();
        return slot < _descends.Length && _descends[slot];
    }

    /// <summary>Whether any character of <paramref name="text"/> descends.</summary>
    public bool AnyDescends(string text)
    {
        foreach (var c in text)
            if (c != '\n' && Descends(c))
                return true;
        return false;
    }

    private bool[]? _descends;

    private bool[] BuildDescenderTable()
    {
        var data = BuildAtlasData();
        var width = AtlasWidth;
        var table = new bool[SlotCount];

        for (var slot = 0; slot < table.Length; slot++)
        for (var y = GlyphHeight; y < DrawnHeight && !table[slot]; y++)
        for (var x = 0; x < GlyphWidth; x++)
            if (data[((y * width) + slot * Advance + x) * 4 + 3] != 0)
            {
                table[slot] = true;
                break;
            }

        return table;
    }

    /// <summary>
    /// How many pixels of a character are lit <b>inside the box</b>, above the baseline. Subtract
    /// it from <see cref="LitPixels"/> to count what a character puts in the descender zone.
    /// </summary>
    public int LitPixelsInBox(char c) => LitRows(c, 0, GlyphHeight);

    private int LitRows(char c, int firstRow, int lastRowExclusive)
    {
        var slot = SlotOf(c);
        if (slot < 0)
            return 0;

        var data = BuildAtlasData();
        var width = AtlasWidth;
        var lit = 0;
        for (var y = firstRow; y < lastRowExclusive; y++)
        for (var x = 0; x < GlyphWidth; x++)
            if (data[((y * width) + slot * Advance + x) * 4 + 3] != 0)
                lit++;
        return lit;
    }

    /// <summary>Validates every size.</summary>
    public static void ValidateAll()
    {
        foreach (var font in BySizeDescending)
            font.Validate();
    }

    private byte[] BuildAtlasData()
    {
        // Checked here rather than trusted, because the failure mode is silent and ugly: a table
        // one row short leaves the last drawn extra reading as whatever sits in the slot after it,
        // and a row too many is a glyph nothing can address.
        if (_glyphs.Length != AsciiSlots + Drawn.Length)
            throw new InvalidOperationException(
                $"PixelFont {Name} has {_glyphs.Length} glyph rows, expected {AsciiSlots} for " +
                $"printable ASCII plus {Drawn.Length} drawn extra(s). A row was added to the " +
                "table without being added to Drawn, or the other way round.");

        var width = AtlasWidth;
        var data = new byte[width * DrawnHeight * 4];

        for (var index = 0; index < _glyphs.Length; index++)
            Blit(data, width, slot: index, glyph: index);

        // The composed glyphs, drawn into their own slots by overlaying two ASCII ones. Done
        // after the loop above so both sources are already validated by the time they are read
        // a second time.
        foreach (var (c, over, under) in Composed)
        {
            var slot = SlotOf(c);
            if (slot < 0)
                throw new InvalidOperationException(
                    $"PixelFont {Name} composes '{c}' but it has no slot; add it to Composed");
            Blit(data, width, slot, SlotOf(over));
            Blit(data, width, slot, SlotOf(under));
        }

        return data;
    }

    /// <summary>
    /// Draws one glyph of the table into one slot of the atlas. Lit pixels are set and unlit
    /// ones left alone, which is what makes two calls into the same slot an overlay rather
    /// than a replacement.
    /// </summary>
    private void Blit(byte[] data, int width, int slot, int glyph)
    {
        // Split on any whitespace rather than on a single separator: it is what lets a glyph be
        // written as a picture across several lines, and it makes the line ending irrelevant, so
        // a checkout that converts the file to CRLF cannot silently widen every row by one.
        var rows = _glyphs[glyph].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length != DrawnHeight)
            throw new InvalidOperationException(
                $"PixelFont {Name} glyph {glyph} ('{(char)(First + glyph)}') has {rows.Length} " +
                $"rows, expected {DrawnHeight} ({GlyphHeight} of box plus {DescenderDepth} of " +
                "descender)");

        for (var y = 0; y < DrawnHeight; y++)
        {
            if (rows[y].Length != GlyphWidth)
                throw new InvalidOperationException(
                    $"PixelFont {Name} glyph {glyph} ('{(char)(First + glyph)}') row {y} is " +
                    $"{rows[y].Length} pixels wide, expected {GlyphWidth}");

            for (var x = 0; x < GlyphWidth; x++)
            {
                if (rows[y][x] != '#')
                    continue;
                var offset = ((y * width) + slot * Advance + x) * 4;
                data[offset] = data[offset + 1] = data[offset + 2] = data[offset + 3] = byte.MaxValue;
            }
        }
    }

}
