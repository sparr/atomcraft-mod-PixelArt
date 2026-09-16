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
/// three times is still a 3x5 glyph: it gains size and no detail. Drawing 9x13 properly buys
/// round bowls, real diagonals, and descenders that sit below the baseline instead of being
/// folded back into the body.
///
/// | Size | Glyph | Spacing | Grid | Descenders |
/// | --- | --- | --- | --- | --- |
/// | <see cref="Small"/> | 3x5 | 1 | 4x6 | no |
/// | <see cref="Medium"/> | 5x7 | 2 | 7x9 | no |
/// | <see cref="Large"/> | 9x13 | 3 | 12x16 | yes |
///
/// Digits and capitals are what the two smaller sizes are good at. Neither has room below
/// the baseline, so their lowercase g, j, p, q and y are folded up into the body; Large has
/// a real descender zone and draws them properly. A little punctuation is approximate at 3x5.
/// If a label has to be read exactly, say it in digits and capitals, or use a larger size.
/// </summary>
public sealed class PixelFont
{
    /// <summary>3x5 glyphs on a 4x6 grid. The only size that fits inside a pixel when zoomed out.</summary>
    public static readonly PixelFont Small;

    /// <summary>5x7 glyphs on a 7x9 grid.</summary>
    public static readonly PixelFont Medium;

    /// <summary>9x13 glyphs on a 12x16 grid, with a real descender zone below the baseline.</summary>
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
        Small = new PixelFont("Small", 3, 5, 1, hasDescenders: false, Glyphs3x5);
        Medium = new PixelFont("Medium", 5, 7, 2, hasDescenders: false, Glyphs5x7);
        Large = new PixelFont("Large", 9, 13, 3, hasDescenders: true, Glyphs9x13);
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

    private PixelFont(string name, int glyphWidth, int glyphHeight, int spacing,
                      bool hasDescenders, string[] glyphs)
    {
        Name = name;
        GlyphWidth = glyphWidth;
        GlyphHeight = glyphHeight;
        Spacing = spacing;
        HasDescenders = hasDescenders;
        _glyphs = glyphs;
    }

    /// <summary>The name this size is known by, for messages.</summary>
    public string Name { get; }

    /// <summary>Lit area of one glyph, in font pixels.</summary>
    public int GlyphWidth { get; }
    public int GlyphHeight { get; }

    /// <summary>Blank columns between characters, and blank rows between lines.</summary>
    public int Spacing { get; }

    /// <summary>Pen movement per character: the glyph plus its spacing.</summary>
    public int Advance => GlyphWidth + Spacing;

    /// <summary>Row-to-row distance for a multi-line label.</summary>
    public int LineHeight => GlyphHeight + Spacing;

    /// <summary>
    /// Whether lowercase g, j, p, q and y drop below the baseline. False for the two smaller
    /// sizes, which have no room and fold them into the body instead.
    /// </summary>
    public bool HasDescenders { get; }

    /// <summary>Width of <see cref="Atlas"/> in pixels: one <see cref="Advance"/>-wide slot per character.</summary>
    public int AtlasWidth => SlotCount * Advance;

    public override string ToString() => $"{Name} ({GlyphWidth}x{GlyphHeight} on {Advance}x{LineHeight})";

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
            AtlasWidth, GlyphHeight, useMipmaps: false, Image.Format.Rgba8, BuildAtlasData()));

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
        return new Rect2(index * Advance, 0, GlyphWidth, GlyphHeight);
    }

    /// <summary>
    /// The size of a label in font pixels, honoring embedded newlines. Trailing spacing is not
    /// counted, so a one-character label in <see cref="Small"/> measures 3x5 rather than 4x6:
    /// the spacing exists to separate characters from each other, and counting it would push
    /// every centered label half a pixel off.
    /// </summary>
    public Vector2I Measure(string text)
    {
        var lines = text.Split('\n');
        var widest = 0;
        foreach (var line in lines)
            widest = Math.Max(widest, line.Length);
        if (widest == 0)
            return Vector2I.Zero;
        return new Vector2I(widest * Advance - Spacing, lines.Length * LineHeight - Spacing);
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
    /// only then is it made as large as the box allows. A 24x14 box holds Large at 1x and Medium
    /// at 2x, and the 13-pixel-tall Large is the better answer even though the Medium would be a
    /// pixel taller: a 5x7 glyph doubled is still a 5x7 glyph, while 9x13 has round bowls, real
    /// diagonals and descenders. That is the whole reason for having three of them.</para>
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
    public int LitPixels(char c)
    {
        var slot = SlotOf(c);
        if (slot < 0)
            return 0;

        var data = BuildAtlasData();
        var width = AtlasWidth;
        var lit = 0;
        for (var y = 0; y < GlyphHeight; y++)
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
        var data = new byte[width * GlyphHeight * 4];

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
        var rows = _glyphs[glyph].Split(' ');
        if (rows.Length != GlyphHeight)
            throw new InvalidOperationException(
                $"PixelFont {Name} glyph {glyph} ('{(char)(First + glyph)}') has {rows.Length} " +
                $"rows, expected {GlyphHeight}");

        for (var y = 0; y < GlyphHeight; y++)
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

    // Each table holds one entry per printable ASCII character, from space to tilde, as rows
    // of pixels separated by spaces. '#' is lit. Laid out this way so a glyph can be read and
    // corrected in place; the atlas is built from it at first use.

    private static readonly string[] Glyphs3x5 =
    {
        "... ... ... ... ...",   // (space)
        ".#. .#. .#. ... .#.",   // !
        "#.# #.# ... ... ...",   // "
        "#.# ### #.# ### #.#",   // #
        ".## ##. .#. ..# ##.",   // $
        "#.# ..# .#. #.. #.#",   // %
        ".#. #.# .#. #.# .##",   // &
        ".#. .#. ... ... ...",   // '
        "..# .#. .#. .#. ..#",   // (
        "#.. .#. .#. .#. #..",   // )
        "... #.# .#. #.# ...",   // *
        "... .#. ### .#. ...",   // +
        "... ... ... .#. #..",   // ,
        "... ... ### ... ...",   // -
        "... ... ... ... .#.",   // .
        "..# ..# .#. #.. #..",   // /
        "### #.# #.# #.# ###",   // 0
        ".#. ##. .#. .#. ###",   // 1
        "### ..# ### #.. ###",   // 2
        "### ..# ### ..# ###",   // 3
        "#.# #.# ### ..# ..#",   // 4
        "### #.. ### ..# ###",   // 5
        "### #.. ### #.# ###",   // 6
        "### ..# ..# ..# ..#",   // 7
        "### #.# ### #.# ###",   // 8
        "### #.# ### ..# ###",   // 9
        "... .#. ... .#. ...",   // :
        "... .#. ... .#. #..",   // ;
        "..# .#. #.. .#. ..#",   // <
        "... ### ... ### ...",   // =
        "#.. .#. ..# .#. #..",   // >
        "##. ..# .#. ... .#.",   // ?
        ".#. #.# ### #.. .##",   // @
        ".#. #.# ### #.# #.#",   // A
        "##. #.# ##. #.# ##.",   // B
        ".## #.. #.. #.. .##",   // C
        "##. #.# #.# #.# ##.",   // D
        "### #.. ##. #.. ###",   // E
        "### #.. ##. #.. #..",   // F
        ".## #.. #.# #.# .##",   // G
        "#.# #.# ### #.# #.#",   // H
        "### .#. .#. .#. ###",   // I
        "..# ..# ..# #.# .#.",   // J
        "#.# #.# ##. #.# #.#",   // K
        "#.. #.. #.. #.. ###",   // L
        "#.# ### ### #.# #.#",   // M
        "#.# ##. ### .## #.#",   // N
        ".#. #.# #.# #.# .#.",   // O
        "##. #.# ##. #.. #..",   // P
        ".#. #.# #.# ### .##",   // Q
        "##. #.# ##. #.# #.#",   // R
        ".## #.. .#. ..# ##.",   // S
        "### .#. .#. .#. .#.",   // T
        "#.# #.# #.# #.# ###",   // U
        "#.# #.# #.# #.# .#.",   // V
        "#.# #.# ### ### #.#",   // W
        "#.# #.# .#. #.# #.#",   // X
        "#.# #.# .#. .#. .#.",   // Y
        "### ..# .#. #.. ###",   // Z
        ".## .#. .#. .#. .##",   // [
        "#.. #.. .#. ..# ..#",   // backslash
        "##. .#. .#. .#. ##.",   // ]
        ".#. #.# ... ... ...",   // ^
        "... ... ... ... ###",   // _
        "#.. .#. ... ... ...",   // `
        "... ##. .## #.# .##",   // a
        "#.. #.. ##. #.# ##.",   // b
        "... .## #.. #.. .##",   // c
        "..# ..# .## #.# .##",   // d
        "... .#. #.# ##. .##",   // e
        ".## .#. ### .#. .#.",   // f
        "... .## #.# .## ##.",   // g
        "#.. #.. ##. #.# #.#",   // h
        ".#. ... .#. .#. .#.",   // i
        "..# ... ..# #.# .#.",   // j
        "#.. #.# ##. ##. #.#",   // k
        "##. .#. .#. .#. .##",   // l
        "... ### ### #.# #.#",   // m
        "... ##. #.# #.# #.#",   // n
        "... .#. #.# #.# .#.",   // o
        "... ##. #.# ##. #..",   // p
        "... .## #.# .## ..#",   // q
        "... .## #.. #.. #..",   // r
        "... .## .#. ..# ##.",   // s
        ".#. ### .#. .#. .##",   // t
        "... #.# #.# #.# .##",   // u
        "... #.# #.# #.# .#.",   // v
        "... #.# #.# ### ###",   // w
        "... #.# .#. .#. #.#",   // x
        "... #.# #.# .## ##.",   // y
        "... ### .#. #.. ###",   // z
        "..# .#. ##. .#. ..#",   // {
        ".#. .#. .#. .#. .#.",   // |
        "#.. .#. .## .#. #..",   // }
        "... ..# ### #.. ...",   // ~
        "... ... ... ... #.#",   // ellipsis (two dots: three do not fit in three columns)
    };

    private static readonly string[] Glyphs5x7 =
    {
        "..... ..... ..... ..... ..... ..... .....",   // (space)
        "..#.. ..#.. ..#.. ..#.. ..#.. ..... ..#..",   // !
        ".#.#. .#.#. ..... ..... ..... ..... .....",   // "
        ".#.#. .#.#. ##### .#.#. ##### .#.#. .#.#.",   // #
        "..#.. .#### #.#.. .###. ..#.# ####. ..#..",   // $
        "##... ##..# ...#. ..#.. .#... #..## ...##",   // %
        ".##.. #..#. #.#.. .#... #.#.# #..#. .##.#",   // &
        "..#.. ..#.. ..... ..... ..... ..... .....",   // '
        "...#. ..#.. .#... .#... .#... ..#.. ...#.",   // (
        ".#... ..#.. ...#. ...#. ...#. ..#.. .#...",   // )
        "..... #.#.# .###. ##### .###. #.#.# .....",   // *
        "..... ..#.. ..#.. ##### ..#.. ..#.. .....",   // +
        "..... ..... ..... ..... ..##. ..#.. .#...",   // ,
        "..... ..... ..... ##### ..... ..... .....",   // -
        "..... ..... ..... ..... ..... .##.. .##..",   // .
        "....# ....# ...#. ..#.. .#... #.... #....",   // /
        ".###. #...# #..## #.#.# ##..# #...# .###.",   // 0
        "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.",   // 1
        ".###. #...# ....# ...#. ..#.. .#... #####",   // 2
        "##### ...#. ..#.. ...#. ....# #...# .###.",   // 3
        "...#. ..##. .#.#. #..#. ##### ...#. ...#.",   // 4
        "##### #.... ####. ....# ....# #...# .###.",   // 5
        "..##. .#... #.... ####. #...# #...# .###.",   // 6
        "##### ....# ...#. ..#.. .#... .#... .#...",   // 7
        ".###. #...# #...# .###. #...# #...# .###.",   // 8
        ".###. #...# #...# .#### ....# ...#. .##..",   // 9
        "..... .##.. .##.. ..... .##.. .##.. .....",   // :
        "..... .##.. .##.. ..... .##.. ..#.. .#...",   // ;
        "...#. ..#.. .#... #.... .#... ..#.. ...#.",   // <
        "..... ..... ##### ..... ##### ..... .....",   // =
        ".#... ..#.. ...#. ....# ...#. ..#.. .#...",   // >
        ".###. #...# ....# ...#. ..#.. ..... ..#..",   // ?
        ".###. #...# #.### #.#.# #.### #.... .###.",   // @
        "..#.. .#.#. #...# #...# ##### #...# #...#",   // A
        "####. #...# #...# ####. #...# #...# ####.",   // B
        ".###. #...# #.... #.... #.... #...# .###.",   // C
        "###.. #..#. #...# #...# #...# #..#. ###..",   // D
        "##### #.... #.... ####. #.... #.... #####",   // E
        "##### #.... #.... ####. #.... #.... #....",   // F
        ".###. #...# #.... #..## #...# #...# .###.",   // G
        "#...# #...# #...# ##### #...# #...# #...#",   // H
        ".###. ..#.. ..#.. ..#.. ..#.. ..#.. .###.",   // I
        "..### ...#. ...#. ...#. ...#. #..#. .##..",   // J
        "#...# #..#. #.#.. ##... #.#.. #..#. #...#",   // K
        "#.... #.... #.... #.... #.... #.... #####",   // L
        "#...# ##.## #.#.# #.#.# #...# #...# #...#",   // M
        "#...# ##..# #.#.# #..## #...# #...# #...#",   // N
        ".###. #...# #...# #...# #...# #...# .###.",   // O
        "####. #...# #...# ####. #.... #.... #....",   // P
        ".###. #...# #...# #...# #.#.# #..#. .##.#",   // Q
        "####. #...# #...# ####. #.#.. #..#. #...#",   // R
        ".#### #.... #.... .###. ....# ....# ####.",   // S
        "##### ..#.. ..#.. ..#.. ..#.. ..#.. ..#..",   // T
        "#...# #...# #...# #...# #...# #...# .###.",   // U
        "#...# #...# #...# #...# #...# .#.#. ..#..",   // V
        "#...# #...# #...# #.#.# #.#.# ##.## #...#",   // W
        "#...# #...# .#.#. ..#.. .#.#. #...# #...#",   // X
        "#...# #...# .#.#. ..#.. ..#.. ..#.. ..#..",   // Y
        "##### ....# ...#. ..#.. .#... #.... #####",   // Z
        ".###. .#... .#... .#... .#... .#... .###.",   // [
        "#.... #.... .#... ..#.. ...#. ....# ....#",   // backslash
        ".###. ...#. ...#. ...#. ...#. ...#. .###.",   // ]
        "..#.. .#.#. #...# ..... ..... ..... .....",   // ^
        "..... ..... ..... ..... ..... ..... #####",   // _
        ".#... ..#.. ..... ..... ..... ..... .....",   // `
        "..... ..... .###. ....# .#### #...# .####",   // a
        "#.... #.... ####. #...# #...# #...# ####.",   // b
        "..... ..... .###. #.... #.... #.... .###.",   // c
        "....# ....# .#### #...# #...# #...# .####",   // d
        "..... ..... .###. #...# ##### #.... .###.",   // e
        "..##. .#... .#... ####. .#... .#... .#...",   // f
        "..... .#### #...# #...# .#### ....# .###.",   // g
        "#.... #.... ####. #...# #...# #...# #...#",   // h
        "..#.. ..... .##.. ..#.. ..#.. ..#.. .###.",   // i
        "...#. ..... ..##. ...#. ...#. #..#. .##..",   // j
        "#.... #.... #..#. #.#.. ##... #.#.. #..#.",   // k
        ".##.. ..#.. ..#.. ..#.. ..#.. ..#.. .###.",   // l
        "..... ..... ##.#. #.#.# #.#.# #.#.# #...#",   // m
        "..... ..... ####. #...# #...# #...# #...#",   // n
        "..... ..... .###. #...# #...# #...# .###.",   // o
        "..... ####. #...# #...# ####. #.... #....",   // p
        "..... .#### #...# #...# .#### ....# ....#",   // q
        "..... ..... #.##. ##..# #.... #.... #....",   // r
        "..... ..... .#### #.... .###. ....# ####.",   // s
        ".#... .#... ####. .#... .#... .#..# ..##.",   // t
        "..... ..... #...# #...# #...# #..## .##.#",   // u
        "..... ..... #...# #...# #...# .#.#. ..#..",   // v
        "..... ..... #...# #.#.# #.#.# #.#.# .#.#.",   // w
        "..... ..... #...# .#.#. ..#.. .#.#. #...#",   // x
        "..... #...# #...# #...# .#### ....# .###.",   // y
        "..... ..... ##### ...#. ..#.. .#... #####",   // z
        "...## ..#.. ..#.. .#... ..#.. ..#.. ...##",   // {
        "..#.. ..#.. ..#.. ..#.. ..#.. ..#.. ..#..",   // |
        "##... ..#.. ..#.. ...#. ..#.. ..#.. ##...",   // }
        "..... ..... .#... #.#.# ...#. ..... .....",   // ~
        "..... ..... ..... ..... ..... #.#.# #.#.#",   // ellipsis
    };

    /// <summary>Rows 0-9 are the cap and ascender zone with the baseline at row 9; rows 10-12 are the descender zone.</summary>
    private static readonly string[] Glyphs9x13 =
    {
        "......... ......... ......... ......... ......... ......... ......... ......... ......... ......... ......... ......... .........",   // (space)
        "...###... ...###... ...###... ...###... ...###... ...###... ...###... ......... ...###... ...###... ......... ......... .........",   // !
        "..##.##.. ..##.##.. ..##.##.. ......... ......... ......... ......... ......... ......... ......... ......... ......... .........",   // "
        "......... ..##.##.. ..##.##.. ######### ..##.##.. ..##.##.. ######### ..##.##.. ..##.##.. ......... ......... ......... .........",   // #
        "....#.... ..#####.. .##.#.##. .##.#.... ..#####.. ....#..## ##..#..## .##.#.##. ..#####.. ....#.... ......... ......... .........",   // $
        "###....## #.#...##. ###..##.. ....##... ...##.... ..##..... .##..###. ##...#.#. .....###. ......... ......... ......... .........",   // %
        "..####... .##..##.. .##..##.. ..####... .####.... ##..##..# ##...##.# ##....### .##...### ..####.## ......... ......... .........",   // &
        "...###... ...###... ...##.... ......... ......... ......... ......... ......... ......... ......... ......... ......... .........",   // '
        ".....##.. ....##... ...##.... ..##..... ..##..... ..##..... ..##..... ...##.... ....##... .....##.. ......... ......... .........",   // (
        "..##..... ...##.... ....##... .....##.. .....##.. .....##.. .....##.. ....##... ...##.... ..##..... ......... ......... .........",   // )
        "......... ...###... ##.###.## .#######. ...###... .#######. ##.###.## ...###... ......... ......... ......... ......... .........",   // *
        "......... ......... ......... ...###... ...###... ######### ...###... ...###... ......... ......... ......... ......... .........",   // +
        "......... ......... ......... ......... ......... ......... ......... ......... ...###... ...###... ...##.... ..##..... .........",   // ,
        "......... ......... ......... ......... ......... ......... .#######. .#######. ......... ......... ......... ......... .........",   // -
        "......... ......... ......... ......... ......... ......... ......... ......... ...###... ...###... ......... ......... .........",   // .
        ".......## ......##. .....##.. .....##.. ....##... ...##.... ..##..... ..##..... .##...... ##....... ......... ......... .........",   // /
        "..#####.. .##...##. ##.....## ##....### ##...#### ##..##.## ####...## ###....## .##...##. ..#####.. ......... ......... .........",   // 0
        "...###... ..####... .#####... ....##... ....##... ....##... ....##... ....##... ....##... .#######. ......... ......... .........",   // 1
        "..#####.. .##...##. ##.....## .......## ......##. .....##.. ....##... ...##.... ..##..... .######## ......... ......... .........",   // 2
        ".#######. ......##. .....##.. ....##... ...####.. .......## .......## ##.....## .##...##. ..#####.. ......... ......... .........",   // 3
        ".....##.. ....###.. ...####.. ..##.##.. .##..##.. ##...##.. ######### .....##.. .....##.. .....##.. ......... ......... .........",   // 4
        ".#######. .##...... .##...... .######.. .##...##. .......## .......## ##.....## .##...##. ..#####.. ......... ......... .........",   // 5
        "...####.. ..##..##. .##...... ##....... ##.####.. ###...##. ##.....## ##.....## .##...##. ..#####.. ......... ......... .........",   // 6
        "######### ##.....## ......##. .....##.. ....##... ....##... ...##.... ...##.... ...##.... ...##.... ......... ......... .........",   // 7
        "..#####.. .##...##. ##.....## .##...##. ..#####.. .##...##. ##.....## ##.....## .##...##. ..#####.. ......... ......... .........",   // 8
        "..#####.. .##...##. ##.....## ##.....## .##..###. ..####.## .......## ......##. .##..##.. ..####... ......... ......... .........",   // 9
        "......... ......... ......... ...###... ...###... ......... ......... ...###... ...###... ......... ......... ......... .........",   // :
        "......... ......... ......... ...###... ...###... ......... ......... ...###... ...###... ...##.... ..##..... ......... .........",   // ;
        "......... ......##. .....##.. ....##... ...##.... ..##..... ...##.... ....##... .....##.. ......##. ......... ......... .........",   // <
        "......... ......... ......... ......... .#######. .#######. ......... .#######. .#######. ......... ......... ......... .........",   // =
        "......... .##...... ..##..... ...##.... ....##... .....##.. ....##... ...##.... ..##..... .##...... ......... ......... .........",   // >
        "..#####.. .##...##. ##.....## .......## ......##. .....##.. ....##... ......... ...###... ...###... ......... ......... .........",   // ?
        "..#####.. .##...##. ##.....## ##..###.# ##.##.#.# ##.##.#.# ##..####. ##....... .##...##. ..#####.. ......... ......... .........",   // @
        "...###... ...###... ..##.##.. ..##.##.. .##...##. .#######. .##...##. ##.....## ##.....## ##.....## ......... ......... .........",   // A
        "#######.. ##....##. ##.....## ##....##. #######.. ##....##. ##.....## ##.....## ##....##. #######.. ......... ......... .........",   // B
        "..#####.. .##...##. ##.....## ##....... ##....... ##....... ##....... ##.....## .##...##. ..#####.. ......... ......... .........",   // C
        "######... ##...##.. ##....##. ##.....## ##.....## ##.....## ##.....## ##....##. ##...##.. ######... ......... ......... .........",   // D
        "######### ##....... ##....... ##....... #######.. ##....... ##....... ##....... ##....... ######### ......... ......... .........",   // E
        "######### ##....... ##....... ##....... #######.. ##....... ##....... ##....... ##....... ##....... ......... ......... .........",   // F
        "..#####.. .##...##. ##.....## ##....... ##....... ##..##### ##.....## ##.....## .##...##. ..######. ......... ......... .........",   // G
        "##.....## ##.....## ##.....## ##.....## ######### ##.....## ##.....## ##.....## ##.....## ##.....## ......... ......... .........",   // H
        ".#######. ...###... ...###... ...###... ...###... ...###... ...###... ...###... ...###... .#######. ......... ......... .........",   // I
        "....##### ......##. ......##. ......##. ......##. ......##. ##....##. ##....##. .##..##.. ..####... ......... ......... .........",   // J
        "##.....## ##....##. ##...##.. ##..##... #####.... ##..##... ##...##.. ##....##. ##.....## ##.....## ......... ......... .........",   // K
        "##....... ##....... ##....... ##....... ##....... ##....... ##....... ##....... ##....... ######### ......... ......... .........",   // L
        "##.....## ###...### ####.#### ##.###.## ##..#..## ##.....## ##.....## ##.....## ##.....## ##.....## ......... ......... .........",   // M
        "##.....## ###....## ####...## ##.##..## ##..##.## ##...#### ##....### ##.....## ##.....## ##.....## ......... ......... .........",   // N
        "..#####.. .##...##. ##.....## ##.....## ##.....## ##.....## ##.....## ##.....## .##...##. ..#####.. ......... ......... .........",   // O
        "#######.. ##....##. ##.....## ##....##. #######.. ##....... ##....... ##....... ##....... ##....... ......... ......... .........",   // P
        "..#####.. .##...##. ##.....## ##.....## ##.....## ##.....## ##..##.## ##...#### .##...##. ..####.## ......... ......... .........",   // Q
        "#######.. ##....##. ##.....## ##....##. #######.. ##..##... ##...##.. ##....##. ##.....## ##.....## ......... ......... .........",   // R
        "..#####.. .##...##. ##....... .##...... ..#####.. ......##. .......## ##.....## .##...##. ..#####.. ......... ......... .........",   // S
        "######### ...###... ...###... ...###... ...###... ...###... ...###... ...###... ...###... ...###... ......... ......... .........",   // T
        "##.....## ##.....## ##.....## ##.....## ##.....## ##.....## ##.....## ##.....## .##...##. ..#####.. ......... ......... .........",   // U
        "##.....## ##.....## ##.....## ##.....## .##...##. .##...##. ..##.##.. ..##.##.. ...###... ...###... ......... ......... .........",   // V
        "##.....## ##.....## ##.....## ##.....## ##.....## ##..#..## ##.###.## ####.#### ###...### .##...##. ......... ......... .........",   // W
        "##.....## ##.....## .##...##. ..##.##.. ...###... ...###... ..##.##.. .##...##. ##.....## ##.....## ......... ......... .........",   // X
        "##.....## ##.....## .##...##. ..##.##.. ...###... ...###... ...###... ...###... ...###... ...###... ......... ......... .........",   // Y
        "######### ......##. .....##.. ....##... ...##.... ..##..... .##...... ##....... ##....... ######### ......... ......... .........",   // Z
        "..#####.. ..##..... ..##..... ..##..... ..##..... ..##..... ..##..... ..##..... ..##..... ..#####.. ......... ......... .........",   // [
        "##....... .##...... ..##..... ..##..... ...##.... ....##... .....##.. .....##.. ......##. .......## ......... ......... .........",   // backslash
        "..#####.. .....##.. .....##.. .....##.. .....##.. .....##.. .....##.. .....##.. .....##.. ..#####.. ......... ......... .........",   // ]
        "...###... ..##.##.. .##...##. ##.....## ......... ......... ......... ......... ......... ......... ......... ......... .........",   // ^
        "......... ......... ......... ......... ......... ......... ......... ......... ......... ......... ######### ######### .........",   // _
        "..##..... ...##.... ....##... ......... ......... ......... ......... ......... ......... ......... ......... ......... .........",   // `
        "......... ......... ......... ......... ..######. .##....## ..####### .##....## .##....## ..####### ......... ......... .........",   // a
        "##....... ##....... ##....... ##....... ##.####.. ###...##. ##.....## ##.....## ###...##. ##.####.. ......... ......... .........",   // b
        "......... ......... ......... ......... ..#####.. .##...##. ##....... ##....... .##...##. ..#####.. ......... ......... .........",   // c
        ".......## .......## .......## .......## ..####.## .##...### ##.....## ##.....## .##...### ..####.## ......... ......... .........",   // d
        "......... ......... ......... ......... ..#####.. .##...##. ##.....## ######### ##....... ..#####.. ......... ......... .........",   // e
        "....####. ...##.... ...##.... .#######. ...##.... ...##.... ...##.... ...##.... ...##.... ...##.... ......... ......... .........",   // f
        "......... ......... ......... ......... ..####### .##....## ##.....## .##....## ..####### .......## ##.....## .##...##. ..#####..",   // g
        "##....... ##....... ##....... ##....... ##.####.. ###...##. ##.....## ##.....## ##.....## ##.....## ......... ......... .........",   // h
        "...###... ...###... ......... ......... ..####... ...###... ...###... ...###... ...###... .#######. ......... ......... .........",   // i
        ".....###. .....###. ......... ......... ....####. .....###. .....###. .....###. .....###. .....###. ##...###. .##.###.. ..####...",   // j
        "##....... ##....... ##....... ##....... ##...##.. ##..##... ##.##.... #####.... ##..##... ##...###. ......... ......... .........",   // k
        "..###.... ..###.... ...##.... ...##.... ...##.... ...##.... ...##.... ...##.... ...##..## ....####. ......... ......... .........",   // l
        "......... ......... ......... ......... ########. ##.##.##. ##.##.##. ##.##.##. ##.##.##. ##.##.##. ......... ......... .........",   // m
        "......... ......... ......... ......... ##.####.. ###...##. ##.....## ##.....## ##.....## ##.....## ......... ......... .........",   // n
        "......... ......... ......... ......... ..#####.. .##...##. ##.....## ##.....## .##...##. ..#####.. ......... ......... .........",   // o
        "......... ......... ......... ......... ##.####.. ###...##. ##.....## ###...##. ##.####.. ##....... ##....... ##....... ##.......",   // p
        "......... ......... ......... ......... ..####.## .##...### ##.....## .##...### ..####.## .......## .......## .......## .......##",   // q
        "......... ......... ......... ......... ##.####.. ###..##.. ##....... ##....... ##....... ##....... ......... ......... .........",   // r
        "......... ......... ......... ......... ..######. .##....## ..#####.. .......## ##.....## .######.. ......... ......... .........",   // s
        "......... ...##.... ...##.... .#######. ...##.... ...##.... ...##.... ...##.... ...##..## ....####. ......... ......... .........",   // t
        "......... ......... ......... ......... ##.....## ##.....## ##.....## ##.....## .##...### ..####.## ......... ......... .........",   // u
        "......... ......... ......... ......... ##.....## ##.....## .##...##. ..##.##.. ...###... ...###... ......... ......... .........",   // v
        "......... ......... ......... ......... ##.....## ##.....## ##..#..## ##.###.## ####.#### .##...##. ......... ......... .........",   // w
        "......... ......... ......... ......... ##.....## .##...##. ..#####.. ..#####.. .##...##. ##.....## ......... ......... .........",   // x
        "......... ......... ......... ......... ##.....## ##.....## ##.....## .##...### ..####### .......## ......##. .##..##.. ..####...",   // y
        "......... ......... ......... ......... ######### .....##.. ....##... ...##.... ..##..... ######### ......... ......... .........",   // z
        "....####. ...##.... ...##.... ...##.... ..##..... .##...... ..##..... ...##.... ...##.... ....####. ......... ......... .........",   // {
        "...###... ...###... ...###... ...###... ...###... ...###... ...###... ...###... ...###... ...###... ......... ......... .........",   // |
        ".####.... .....##.. .....##.. .....##.. ......##. .......## ......##. .....##.. .....##.. .####.... ......... ......... .........",   // }
        "......... ......... ......... ......... ......... .####..## ##..####. ......... ......... ......... ......... ......... .........",   // ~
        "......... ......... ......... ......... ......... ......... ......... ......... .##.##.## .##.##.## ......... ......... .........",   // ellipsis
    };
}
