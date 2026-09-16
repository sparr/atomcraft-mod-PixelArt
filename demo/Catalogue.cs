using Godot;

namespace PixelArt.Demo;

/// <summary>
/// The colours the demo draws in: pale, so they read as an overlay rather than as part of the
/// pixel underneath, and distinct from the game's machines, which are fully saturated primaries.
/// </summary>
internal static class Palette
{
    internal static readonly Color Ink = new(0.55f, 0.90f, 1.00f);
    internal static readonly Color Warm = new(1.00f, 0.82f, 0.35f);
    internal static readonly Color Cool = new(0.62f, 1.00f, 0.58f);
    internal static readonly Color Loud = new(1.00f, 0.55f, 0.85f);
    internal static readonly Color Plate = new(0f, 0f, 0f, 0.62f);

    /// <summary>
    /// Behind an outlined label or a bordered arrow. Opaque, unlike the plate: a border is a
    /// pixel wide and a translucent one would take the colour of whatever it happened to fall on.
    /// </summary>
    internal static readonly Color Edge = new(0f, 0f, 0f, 1f);
    internal static readonly Color Caption = new(1f, 1f, 1f);
}

/// <summary>
/// A running cursor down the catalogue, in pixels relative to its top left corner.
///
/// <para>Rows are reserved rather than numbered so that adding one in the middle does not mean
/// renumbering the rest, and so the one number a reader has to trust -- <see cref="Showcase.Height"/>
/// -- can be checked against <see cref="Used"/> by a test rather than by eye.</para>
/// </summary>
internal sealed class Layout
{
    private readonly Vector2I _origin;
    private int _y;

    internal Layout(Vector2I origin) => _origin = origin;

    /// <summary>Where the caption of a row is centred, in pixels from the left edge.</summary>
    internal const int CaptionX = 16;

    /// <summary>Where a row's examples start, clear of the widest caption.</summary>
    internal const int ExampleX = 34;

    /// <summary>Takes the next <paramref name="rows"/> pixels and returns the top of them.</summary>
    internal int Reserve(int rows)
    {
        var top = _y;
        _y += rows;
        return top;
    }

    /// <summary>How tall the catalogue came out.</summary>
    internal int Used => _y;

    internal Vector2I At(int dx, int dy) => new(_origin.X + dx, _origin.Y + dy);

    internal RectInt Block(int dx, int dy, int width, int height) =>
        new(_origin.X + dx, _origin.Y + dy, width, height);
}

/// <summary>
/// The half of the demo that does not change from frame to frame: one example of every mark a
/// <see cref="Canvas"/> retains, with a caption beside each.
///
/// <para>Built once, when the catalogue is placed, and then redrawn from pixel coordinates every
/// frame by the library. That is the point of a retained mark and the thing to notice while
/// looking at it: pan and zoom, and every one of these stays on the pixels it was put on, while
/// the text stays the size it was asked for.</para>
///
/// <para>Captions are upper case because the two smaller fonts are drawn for digits and capitals;
/// their lowercase has no room below the baseline. At the zoom a world starts at, a pixel is about
/// six screen pixels, so the captions here are drawn at scale 2 to stay readable, and everything
/// only gets clearer as you zoom in -- text is measured in screen pixels and pixels are not.</para>
/// </summary>
internal static class Catalogue
{
    internal static Rows Build(Canvas marks, Canvas under, Vector2I anchor)
    {
        marks.Clear();
        under.Clear();

        var l = new Layout(anchor);

        Title(marks, l);
        Fills(marks, l);
        Outlines(marks, l);
        Arrows(marks, l);
        TextSizes(marks, l);
        TextExtras(marks, l);
        Icons(marks, l);
        var text = TextOnAPixel(marks, l);
        Placements(marks, l);
        Layers(marks, under, l);

        // The last two rows are captions only: what goes in them is drawn every frame by
        // LiveSection, which is handed these offsets rather than repeating them.
        var live = LiveHeading(marks, l);
        var alt = AltHeading(marks, l);

        return new Rows(live, alt, text, l.Used);
    }

    private const int TitleRows = 10;
    private const int FillRows = 9;
    private const int OutlineRows = 9;
    private const int ArrowRows = 9;
    private const int TextSizeRows = 11;
    private const int TextExtraRows = 10;
    private const int IconRows = 9;
    private const int TextOnAPixelRows = 11;
    private const int PlacementRows = 11;
    private const int LayerRows = 9;
    private const int LiveRows = 26;
    private const int AltRows = 6;

    // ------------------------------------------------------------------------- the rows

    private static void Title(Canvas c, Layout l)
    {
        var dy = l.Reserve(TitleRows);
        c.Label(l.At(Showcase.Width / 2, dy + 2), "PIXELART", Palette.Caption,
                TextSize.Large, LabelPlacement.Center, scale: 2, plate: Palette.Plate);
        // Everything under the title is drawn by LiveSection, in one label, rather than here.
        // Part of it states how many screen pixels a world pixel covers, which changes every time
        // the player touches the zoom: as a retained mark it claimed the same figure forever,
        // which is the exact mistake a retained mark makes easy. The rest of the line went with it
        // because two labels centred on one row draw on top of each other, and a single two-line
        // label cannot collide with itself.
    }

    /// <summary>Flat colour over a pixel, over a block of pixels, and over a block without hiding it.</summary>
    private static void Fills(Canvas c, Layout l)
    {
        var dy = l.Reserve(FillRows);
        Caption(c, l, dy, "FILL\nPIXEL  RECT  ALPHA");

        c.Fill(l.At(Layout.ExampleX, dy + 1), Palette.Ink);
        // One mark for the whole block rather than eighteen, which is the reason Fill takes a
        // rectangle at all: a large area costs one draw call rather than thousands.
        c.Fill(l.Block(Layout.ExampleX + 5, dy, 6, 3), Palette.Warm);
        // Alpha tints rather than replaces, so the material underneath is still there to read --
        // usually what you want when the mark is an annotation rather than a redaction.
        c.Fill(l.Block(Layout.ExampleX + 15, dy, 6, 3), new Color(Palette.Cool, 0.4f));
    }

    /// <summary>A border drawn inside the pixel's own edges, at one thickness and at three.</summary>
    private static void Outlines(Canvas c, Layout l)
    {
        var dy = l.Reserve(OutlineRows);
        Caption(c, l, dy, "OUTLINE\nTHIN  THICK  RECT");

        c.Outline(l.At(Layout.ExampleX, dy + 1), Palette.Ink);
        c.Outline(l.At(Layout.ExampleX + 5, dy + 1), Palette.Ink, thickness: 3f);
        // One border around the whole block, not one around each pixel in it.
        c.Outline(l.Block(Layout.ExampleX + 12, dy, 8, 4), Palette.Warm, thickness: 2f);
    }

    /// <summary>Which way a pixel points: a triangle on one of its four edges.</summary>
    private static void Arrows(Canvas c, Layout l)
    {
        var dy = l.Reserve(ArrowRows);
        Caption(c, l, dy, "ARROW\nPLAIN, THEN BORDERED");

        var aims = new[] { Aim.Up, Aim.Down, Aim.Left, Aim.Right };
        for (var i = 0; i < aims.Length; i++)
        {
            // On a block of pixels rather than on one, because a nub is a third of what it points
            // out of: on a single pixel at the zoom a world starts at it is at its two-pixel floor
            // and invisible from across the screen. Same call either way.
            var block = l.Block(Layout.ExampleX + i * 6, dy, 3, 3);
            // Outlined as well, so that "the nub sits wholly inside what it points out of" is
            // visible: two neighbours pointing at each other give two marks, not one smear.
            c.Outline(block, new Color(Palette.Ink, 0.4f));
            // The last two get a border, which is what makes an arrow read over a machine colour
            // rather than dissolving into it.
            c.Arrow(block, aims[i], Palette.Ink, i >= 2 ? Palette.Edge : null);
        }
    }

    /// <summary>The three fonts, at the size each is drawn at.</summary>
    private static void TextSizes(Canvas c, Layout l)
    {
        var dy = l.Reserve(TextSizeRows);
        Caption(c, l, dy, "TEXT SIZE\nTHREE FONTS, NOT ONE");

        // Named rather than left to Auto, because the point here is the difference between them.
        // They are separate fonts, not one scaled three ways: 9x13 buys round bowls, real
        // diagonals and descenders that a magnified 3x5 glyph cannot have.
        c.Label(l.At(Layout.ExampleX + 2, dy + 3), "3X5", Palette.Ink, TextSize.Small, scale: 2);
        c.Label(l.At(Layout.ExampleX + 12, dy + 3), "5X7", Palette.Ink, TextSize.Medium, scale: 2);
        c.Label(l.At(Layout.ExampleX + 28, dy + 3), "9X13", Palette.Ink, TextSize.Large, scale: 2);
    }

    /// <summary>Everything else a label can do: a plate, two lines, a glyph past ASCII.</summary>
    private static void TextExtras(Canvas c, Layout l)
    {
        var dy = l.Reserve(TextExtraRows);
        Caption(c, l, dy, "TEXT\nPLATE LINES GLYPH EDGE");

        // A plate is worth it over the game's machines, which are strongly coloured: the
        // alternative, picking a text colour per material, throws away what the colour was saying.
        c.Label(l.At(Layout.ExampleX + 2, dy + 2), "PLATE", Palette.Warm,
                TextSize.Small, LabelPlacement.Center, scale: 2, plate: Palette.Plate);
        c.Label(l.At(Layout.ExampleX + 14, dy + 2), "TWO\nLINES", Palette.Ink,
                TextSize.Small, LabelPlacement.Center, scale: 2);
        // Composed at atlas time from the font's own '=' and '/', so it is right at all three
        // sizes and follows any later correction to either.
        // Plated as well, and not because the glyph needs explaining: it lands wherever the
        // catalogue lands, which out here is over striped terrain, and a plate is what makes any
        // label legible over a background nobody chose.
        c.Label(l.At(Layout.ExampleX + 26, dy + 2), $"A{PixelFont.NotEqual}B", Palette.Cool,
                TextSize.Small, LabelPlacement.Center, scale: 2, plate: Palette.Plate);
        // Outlined rather than plated: every glyph drawn four times in the surround and once on
        // top, which leaves whatever is underneath visible between the letters. A plate at this
        // size is a black box over the thing being described.
        c.Label(l.At(Layout.ExampleX + 38, dy + 2), "EDGE", Palette.Caption,
                TextSize.Small, LabelPlacement.Center, scale: 2, outline: Palette.Edge);
    }

    /// <summary>
    /// The game's own shapes, drawn on pixels and tinted.
    ///
    /// <para>Not this mod's art: a square, a drop and a cloud are the material guide's own
    /// swatches, and the little person is the one the spaceship inventory uses. A player who has
    /// seen the guide already knows what they mean, and they cost nothing to ship. All four are
    /// white silhouettes, which is what lets a tint colour them.</para>
    /// </summary>
    private static void Icons(Canvas c, Layout l)
    {
        var dy = l.Reserve(IconRows);
        Caption(c, l, dy, "GAME ART\nSOLID LIQUID GAS YOU");

        var shapes = new (IconArt? Art, Color Tint)[]
        {
            (GameArt.Solid, Palette.Warm),
            (GameArt.Liquid, Palette.Ink),
            (GameArt.Gas, Palette.Cool),
            (GameArt.Player, Palette.Loud),
        };

        for (var i = 0; i < shapes.Length; i++)
        {
            // On a block, for the same reason the text is: a shape is drawn at a whole scale or
            // not at all, and one pixel is six screen pixels across out here.
            var block = l.Block(Layout.ExampleX + i * 8, dy, 6, 5);
            c.Outline(block, new Color(shapes[i].Tint, 0.35f));
            c.Icon(block, shapes[i].Art, shapes[i].Tint);
        }
    }

    /// <summary>
    /// The same long name three ways, on three identical blocks: fitted, laid out, and fixed.
    ///
    /// <para>One row rather than two, because the three only mean anything against each other.
    /// Zoom in and out and watch what each does.</para>
    ///
    /// <list type="bullet">
    /// <item><b>FIT</b> sizes the text to the block and draws nothing once it will not fit, so
    /// out here it is empty and fills in as you zoom. That is the point of it: a hundred of these
    /// on adjacent pixels never becomes a smear.</item>
    /// <item><b>LAID OUT</b> decides what the text should be as well as how big -- it wraps at
    /// spaces, cuts where it must, and marks a cut with an ellipsis -- so it has something to say
    /// at almost any zoom.</item>
    /// <item><b>FIXED</b> is a named size and a fixed scale. It always draws, and out here it
    /// spills across its neighbours, which is exactly what the other two exist to avoid.</item>
    /// </list>
    ///
    /// <para>The first two are drawn by the live half, since both are answers about this frame's
    /// zoom; only the boxes and the caption are retained here.</para>
    /// </summary>
    private static int TextOnAPixel(Canvas c, Layout l)
    {
        var dy = l.Reserve(TextOnAPixelRows);
        Caption(c, l, dy, "TEXT ON A PIXEL\nFIT  LAID OUT  FIXED");

        c.Outline(l.Block(Layout.ExampleX, dy, 9, 6), new Color(Palette.Cool, 0.5f));
        c.Outline(l.Block(Layout.ExampleX + 11, dy, 9, 6), new Color(Palette.Warm, 0.5f));

        var fixedBox = l.Block(Layout.ExampleX + 22, dy, 9, 6);
        c.Outline(fixedBox, new Color(Palette.Ink, 0.5f));
        c.Label(fixedBox, "FIXED", Palette.Ink, TextSize.Small, LabelPlacement.Center, scale: 2);
        return dy;
    }

    /// <summary>Where a label sits relative to the pixel it belongs to.</summary>
    private static void Placements(Canvas c, Layout l)
    {
        var dy = l.Reserve(PlacementRows);
        Caption(c, l, dy, "PLACEMENT\nAROUND ONE PIXEL");

        var places = new (LabelPlacement Where, string Text)[]
        {
            (LabelPlacement.Center, "CTR"),
            (LabelPlacement.TopLeft, "TL"),
            (LabelPlacement.Above, "ABOVE"),
            (LabelPlacement.Below, "BELOW"),
        };

        for (var i = 0; i < places.Length; i++)
        {
            // Every one against an identical pixel, filled and outlined, so what moves between
            // them is only the placement.
            var tile = l.At(Layout.ExampleX + 3 + i * 11, dy + 5);
            c.Fill(tile, new Color(Palette.Warm, 0.35f));
            c.Outline(tile, Palette.Warm);
            c.Label(tile, places[i].Text, Palette.Caption, TextSize.Small, places[i].Where, scale: 2);
        }
    }

    /// <summary>Two canvases, and which of them is on top.</summary>
    private static void Layers(Canvas top, Canvas bottom, Layout l)
    {
        var dy = l.Reserve(LayerRows);
        Caption(top, l, dy, "TWO CANVASES\nLAYER DECIDES");

        // Same pixels, two mods. The one on the higher layer wins the overlap, and neither one's
        // Clear can reach the other's marks -- which is what stops one mod's overlay from
        // vanishing because of something in somebody else's code.
        bottom.Fill(l.Block(Layout.ExampleX, dy, 12, 4), Palette.Loud);
        bottom.Label(l.At(Layout.ExampleX + 3, dy + 2), "UNDER", Palette.Caption,
                     TextSize.Small, LabelPlacement.Center, scale: 2);

        top.Fill(l.Block(Layout.ExampleX + 8, dy + 1, 12, 4), Palette.Cool);
        top.Label(l.At(Layout.ExampleX + 16, dy + 3), "OVER", new Color(0f, 0f, 0f),
                  TextSize.Small, LabelPlacement.Center, scale: 2);
    }

    private static int LiveHeading(Canvas c, Layout l)
    {
        var dy = l.Reserve(LiveRows);
        Caption(c, l, dy + 14, "LIVE PASS\nREDRAWN EVERY FRAME");
        return dy;
    }

    private static int AltHeading(Canvas c, Layout l)
    {
        var dy = l.Reserve(AltRows);
        Caption(c, l, dy + 1, "PAINTER\nASKS EVERY PIXEL IN VIEW");
        return dy;
    }

    /// <summary>
    /// A row's caption: two lines, centred in the left column, clear of the examples.
    ///
    /// <para>Centred rather than aligned to an edge because a label is placed against a
    /// <i>pixel</i>, and a pixel is a world coordinate: there is no left margin to align to that
    /// would stay put as the view zooms.</para>
    /// </summary>
    private static void Caption(Canvas c, Layout l, int dy, string text) =>
        c.Label(l.At(Layout.CaptionX, dy + 1), text, Palette.Caption,
                TextSize.Small, LabelPlacement.Center, scale: 2, plate: Palette.Plate);
}
