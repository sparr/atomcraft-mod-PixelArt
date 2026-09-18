using Godot;

namespace PixelArt;

/// <summary>When a per-frame pass or painter runs.</summary>
public enum DrawWhen
{
    /// <summary>Every frame the world is on screen.</summary>
    Always,

    /// <summary>
    /// Only while the player holds Alt.
    ///
    /// <para>Alt is already the game's own "tell me more" modifier: holding it adds mass,
    /// specific heat, laser absorption and tile coordinates to the hover box, and lights up the
    /// pixels the simulation loader is working on. A pass gated this way joins that gesture
    /// instead of inventing a new one, and costs nothing on the frames nobody asked.</para>
    /// </summary>
    AltHeld,
}

/// <summary>
/// Which bitmap font text is drawn in. See <see cref="PixelFont"/> for the metrics.
/// </summary>
public enum TextSize
{
    /// <summary>
    /// The largest size that fits the pixel, which is the default.
    ///
    /// <para>A pixel is <c>8 * zoom</c> screen pixels, so what fits changes as the view zooms and
    /// with how much text there is. Picking per label keeps a number inside its own pixel
    /// without the caller tracking the zoom. When even the smallest overflows, the smallest is
    /// drawn anyway: a label spilling past its pixel still reads, and an empty pixel does
    /// not.</para>
    /// </summary>
    Auto,

    /// <summary>3x5 glyphs with a 1-row descender, on a 4x6 grid.</summary>
    Small,

    /// <summary>5x7 glyphs with a 1-row descender, on a 6x9 grid.</summary>
    Medium,

    /// <summary>7x11 glyphs with a 2-row descender, on an 8x14 grid. One-pixel strokes throughout.</summary>
    Large,

    /// <summary>
    /// The largest font <b>and</b> the largest whole scale that fit the pixel, so the text tracks
    /// the pixel it is drawn on as the view zooms.
    ///
    /// <para>The difference from <see cref="Auto"/> is what happens at the two ends. Auto keeps
    /// the scale you asked for and only chooses a font, so zooming in leaves the text the same
    /// size in the middle of a growing pixel, and zooming out leaves it the same size while the
    /// pixels shrink underneath it until neighbouring labels overlap into a grey smear. Fit
    /// changes the scale as well: the text grows with the pixel on the way in, and on the way out
    /// it shrinks and then, when even the smallest font no longer fits, is <b>not drawn</b>.</para>
    ///
    /// <para>That last part is the point, and it is the opposite of what Auto does. Use Fit for a
    /// label that belongs to one pixel and would be noise once it cannot fit inside it; use Auto
    /// or a named size for a label that has to be readable whatever the zoom -- a heading, a
    /// readout, anything a reader is meant to find rather than stumble on.</para>
    ///
    /// <para>The player's <c>textScale</c> is not applied to Fit. What fits inside a pixel is
    /// decided by the pixel, and multiplying it afterwards would simply overflow it again.</para>
    /// </summary>
    Fit,
}

/// <summary>Where a label sits relative to the pixel it belongs to.</summary>
public enum LabelPlacement
{
    /// <summary>Centered on the pixel. What you want when the label fits inside it.</summary>
    Center,

    /// <summary>Just inside the pixel's top left corner.</summary>
    TopLeft,

    /// <summary>Centered horizontally, sitting just above the pixel.</summary>
    Above,

    /// <summary>Centered horizontally, sitting just below the pixel.</summary>
    Below,
}

/// <summary>
/// Which edge of a pixel a nub points out of. <see cref="None"/> draws nothing, so a caller can
/// hand over whatever it has without a branch.
/// </summary>
public enum Aim
{
    None,
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// One pixel of the world, on screen, handed to a painter.
///
/// <para>The tile and its material are already in hand because the pass that found the pixel had
/// to read them anyway, so a painter that only cares about one material can reject the rest
/// without touching the field again.</para>
///
/// <para>Everything drawn through it lasts for this frame only, which is what makes a painter
/// the right shape for state that changes as the simulation runs. Use the <see cref="Canvas"/>
/// methods that take a tile for a mark that should stay.</para>
/// </summary>
public readonly struct VisiblePixel
{
    internal VisiblePixel(Canvas canvas, Vector2I tile, short materialTypeId, Rect2 screen)
    {
        Canvas = canvas;
        Tile = tile;
        MaterialTypeId = materialTypeId;
        Screen = screen;
    }

    /// <summary>The canvas being painted, for anything the shorthands below do not cover.</summary>
    public Canvas Canvas { get; }

    /// <summary>The pixel's position in the simulation field.</summary>
    public Vector2I Tile { get; }

    /// <summary>
    /// What is in the pixel, as a base material id (the id space <c>SimField</c> stores and
    /// <c>string.ToMaterialTypeId()</c> returns, not the one <c>Materials.GetMaterialTypeId</c>
    /// does -- those two disagree for 1904 of the 1913 shipped materials). -1 is air.
    /// </summary>
    public short MaterialTypeId { get; }

    /// <summary>The rectangle this pixel covers on screen, in render-target pixels.</summary>
    public Rect2 Screen { get; }

    /// <summary>Paints the whole pixel, for this frame.</summary>
    public void Fill(Color color) => Canvas.DrawFill(Screen, color);

    /// <summary>Outlines the pixel, for this frame.</summary>
    public void Outline(Color color, float thickness = 1f) => Canvas.DrawOutline(Screen, color, thickness);

    /// <summary>Points out of one edge of the pixel, for this frame.</summary>
    public void Arrow(Aim aim, Color color, Color? surround = null) =>
        Canvas.DrawArrow(Screen, aim, color, surround);

    /// <summary>Draws one of the game's own shapes on the pixel, for this frame.</summary>
    public void Icon(IconArt? art, Color tint) => Canvas.DrawIcon(Screen, art, tint);

    /// <summary>Writes on the pixel, for this frame.</summary>
    public void Label(string text, Color color,
                      TextSize size = TextSize.Auto,
                      LabelPlacement placement = LabelPlacement.Center,
                      int scale = 1,
                      Color? plate = null,
                      Color? outline = null) =>
        Canvas.DrawLabel(Screen, text, color, size, placement, scale, plate, outline);
}

/// <summary>
/// Where <see cref="LabelLayout"/> is allowed to break a line.
/// </summary>
public enum Breaking
{
    /// <summary>
    /// Only at spaces. A word is never cut, and a body with no space in it has exactly one
    /// arrangement: itself, on one line.
    /// </summary>
    Words,

    /// <summary>
    /// Inside a word when that buys a bigger font.
    ///
    /// <para><b>What it is for.</b> A cell is square, and a one-word name laid out on one line is
    /// constrained by its own length rather than by the cell: "Water" is five glyphs across
    /// whatever the cell's height is going spare. Measured on a 32-screen-pixel cell, one line of
    /// "Water" fits a glyph height of 5 where "Wat" over "er" fits 11, and on a 16-pixel cell the
    /// one-line rule gives up entirely and shows "W" and an ellipsis where two lines show the whole
    /// name.</para>
    ///
    /// <para>Breaking a word is still worth avoiding -- "Praseo" over "dymium" is two fragments
    /// that each read as a word -- so it is priced rather than free; see the
    /// <c>wordSplitCost</c> parameter of <see cref="LabelLayout.Choose(string, string, Godot.Vector2, int?, int, int, Breaking, float)"/>.</para>
    /// </summary>
    Anywhere,
}
