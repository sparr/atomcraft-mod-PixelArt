using System.Globalization;
using Atomcraft;
using Godot;

namespace PixelArt.Demo;

/// <summary>
/// The half of the demo that is rebuilt every frame: a readout of what the library can tell you
/// about the view, an animation, a box measured to fit its own label, the cursor, and the Alt
/// painter.
///
/// <para>Everything here is drawn with the <c>Draw*</c> methods, which last for the frame they
/// are called on. That is the difference worth taking away from this mod: the catalogue above is
/// added once and redrawn by the library forever, and this is added again every frame because
/// every number in it has changed.</para>
/// </summary>
internal static class LiveSection
{
    /// <summary>How many pixels across the marker walks, and how many frames it spends on each.</summary>
    private const int MarchCells = 12;
    private const int FramesPerCell = 6;

    /// <summary>What the Alt painter is looking for: the material under the cursor, or -1 for none.</summary>
    private static short _target = -1;

    /// <summary>Pixels the painter tinted, counted as it goes and reported on the next frame.</summary>
    private static int _matching;
    private static int _matched;

    /// <summary>The material under the cursor, by name, for the Alt row's caption.</summary>
    private static string _targetName = "";

    internal static void Draw(Canvas c, Vector2I anchor, Rows rows)
    {
        // The library only runs a pass while a world is on screen, so this is belt and braces --
        // and the one line of guard a consumer needs, in place of a try/catch.
        if (!ViewGeometry.Ready)
            return;

        // Painters run after passes within a frame, so what the painter counted is from last
        // frame. Reported as such rather than pretending otherwise.
        _matched = _matching;
        _matching = 0;

        ReadTarget();
        Subtitle(c, anchor);
        March(c, anchor, rows.Live);
        MeasuredBox(c, anchor, rows.Live);
        TextRow(c, anchor, rows.Text);
        Readout(c, anchor, rows.Live);
        AltRow(c, anchor, rows.Alt);
        Cursor(c);
    }

    /// <summary>
    /// The line under the title: how big a world pixel is on screen right now.
    ///
    /// <para>Drawn every frame rather than added once with the rest of the title, because it is
    /// the number that changes every time the player touches the zoom. Stated as a retained mark
    /// it would claim the same figure forever, which is what it used to do.</para>
    /// </summary>
    private static void Subtitle(Canvas c, Vector2I anchor)
    {
        var size = ViewGeometry.PixelScreenSize;
        var room = size >= 24f ? "PLENTY OF ROOM" : "ZOOM IN FOR MORE";

        // One label of two lines, not two labels. Both are centred on the same column, so as two
        // marks they would be drawn one on top of the other unless someone remembered to keep
        // their rows apart -- which is exactly what went wrong when this was split in two.
        c.DrawLabel(ViewGeometry.ScreenRectOf(new Vector2I(anchor.X + Showcase.Width / 2, anchor.Y + 7)),
                    $"ONE OF EVERYTHING IT DRAWS\nA WORLD PIXEL IS {N(size)} SCREEN PIXELS -- {room}",
                    Palette.Ink, TextSize.Small, LabelPlacement.Center, scale: 2);
    }

    /// <summary>
    /// A marker walking a row of pixels, which is the cheapest possible proof that this half is
    /// redrawn rather than retained: stop the game and it stops, and nothing about it is stored.
    /// </summary>
    private static void March(Canvas c, Vector2I anchor, int row)
    {
        var at = Showcase.Frames / FramesPerCell % MarchCells;
        for (var i = 0; i < MarchCells; i++)
        {
            var tile = new Vector2I(anchor.X + Layout.ExampleX + i, anchor.Y + row + 1);
            if (i == at)
            {
                c.DrawFill(tile, Palette.Warm);
                c.DrawOutline(tile, Palette.Caption);
            }
            else
            {
                c.DrawOutline(tile, new Color(Palette.Warm, 0.3f));
            }
        }
    }

    /// <summary>
    /// A box drawn exactly around a label, which is what <see cref="Canvas.MeasureLabel"/> is for:
    /// a caller that wants to place or frame text itself rather than hand the job to a placement.
    ///
    /// <para>Measured in screen pixels, because that is what text is: the box is built from a
    /// <see cref="Rect2"/> rather than from pixels, and it stays snug as the view zooms while the
    /// pixels underneath it grow.</para>
    /// </summary>
    private static void MeasuredBox(Canvas c, Vector2I anchor, int row)
    {
        const string text = "MEASURED";
        const int scale = 2;

        var tile = new Vector2I(anchor.X + Layout.ExampleX + 8, anchor.Y + row + 4);
        var size = (Vector2)Canvas.MeasureLabel(text, TextSize.Small, scale);
        var centre = ViewGeometry.ScreenOf(tile);
        var pad = new Vector2(3, 3);

        c.DrawOutline(new Rect2(centre - size / 2f - pad, size + pad * 2f), Palette.Cool);
        c.DrawLabel(ViewGeometry.ScreenRectOf(tile), text, Palette.Cool, TextSize.Small,
                    LabelPlacement.Center, scale);
    }

    /// <summary>
    /// The two per-frame answers in the text row: fitted on the left, laid out in the middle.
    ///
    /// <para>Both are decisions about this frame's zoom, which is why they are drawn here rather
    /// than added once with the boxes around them. Zoom out and the fitted one gives up while the
    /// laid-out one wraps, then cuts, then marks the cut.</para>
    /// </summary>
    private static void TextRow(Canvas c, Vector2I anchor, int row)
    {
        const string name = "PRASEODYMIUM CHLORIDE";

        // Fit: sized to the block, and not drawn at all once it will not fit inside it.
        var fit = new RectInt(anchor.X + Layout.ExampleX, anchor.Y + row, 9, 6);
        c.DrawLabel(fit, name, Palette.Cool, TextSize.Fit, outline: Palette.Edge);

        // Laid out: wrapped, cut and marked as the block demands. Drawn even when it overflows,
        // since there is only one of it and the row is about showing what it does.
        var laid = new RectInt(anchor.X + Layout.ExampleX + 11, anchor.Y + row, 9, 6);
        var fitted = LabelLayout.Choose("", name, ViewGeometry.ScreenRectOf(laid).Size);
        c.DrawLabel(laid, fitted, fitted.Overflows ? Palette.Loud : Palette.Warm,
                    outline: Palette.Edge);
    }

    /// <summary>
    /// Everything the library can answer about the view right now. Most of a consumer's use of
    /// <see cref="ViewGeometry"/> is one of these lines.
    /// </summary>
    private static void Readout(Canvas c, Vector2I anchor, int row)
    {
        var visible = ViewGeometry.VisibleTiles();
        var mouse = ViewGeometry.MouseTile();
        var centre = ViewGeometry.TileAt(ViewGeometry.ViewportSize / 2f);
        var scale = ViewGeometry.WindowScale;

        // The pixel the readout itself sits on, which is also the pixel Auto is resolved against:
        // what fits is a question about a pixel, so it cannot be answered without one.
        var tile = new Vector2I(anchor.X + Layout.ExampleX + 20, anchor.Y + row + 18);
        var auto = Canvas.FontFor(tile, "42");

        var text = string.Join('\n', new[]
        {
            $"PIXEL {N(ViewGeometry.PixelScreenSize)} PX  ZOOM {Z(ViewGeometry.Zoom)}",
            visible is { } v ? $"VIEW {v.width} X {v.height} PIXELS" : "VIEW UNKNOWN",
            $"CENTER {centre.X} {centre.Y}",
            mouse is { } m ? $"MOUSE {m.X} {m.Y}" : "MOUSE OFF WORLD",
            $"PAINTED {PixelArtApi.PixelsPaintedLastFrame} PIXELS",
            $"CANVASES {PixelArtApi.CanvasCount}  TEXTSCALE {Settings.TextScale}",
            $"WINDOW {N(scale.X)}X {(ViewGeometry.PixelPerfect ? "EXACT" : "RESAMPLED")}",
            $"AUTO PICKS {auto.Name.ToUpperInvariant()} HERE",
            $"FRAME {Showcase.Frames}",
        });

        c.DrawLabel(ViewGeometry.ScreenRectOf(tile), text, Palette.Caption,
                    TextSize.Small, LabelPlacement.Center, scale: 2, plate: Palette.Plate);
    }

    /// <summary>What the painter is doing, or what to do to make it do anything.</summary>
    private static void AltRow(Canvas c, Vector2I anchor, int row)
    {
        var tile = new Vector2I(anchor.X + Layout.ExampleX + 16, anchor.Y + row + 2);

        var text = !Canvas.AltHeld
            ? "HOLD ALT TO TINT EVERY\nPIXEL LIKE THE ONE YOU POINT AT"
            : _target < 0
                ? "POINT AT SOMETHING SOLID\nAIR IS NOT WORTH TINTING"
                : $"MATCHING {_targetName.ToUpperInvariant()}\n{_matched} PIXELS ON SCREEN";

        c.DrawLabel(ViewGeometry.ScreenRectOf(tile), text,
                    Canvas.AltHeld ? Palette.Loud : Palette.Caption,
                    TextSize.Small, LabelPlacement.Center, scale: 2, plate: Palette.Plate);
    }

    /// <summary>
    /// The pixel under the cursor, named, with the block around it that a cursor-sized pass would
    /// walk. This is the whole of what most overlay mods need from the library.
    /// </summary>
    private static void Cursor(Canvas c)
    {
        if (ViewGeometry.MouseTile() is not { } tile)
            return;

        // Around is what a pass uses instead of being handed every pixel on screen: 25 pixels here
        // rather than the 39,000 a painter would see at this zoom.
        var near = ViewGeometry.Around(tile, 2);
        c.DrawOutline(ViewGeometry.ScreenRectOf(near), new Color(Palette.Loud, 0.45f));

        c.DrawOutline(tile, Palette.Loud, thickness: 2f);
        // Deliberately a fixed size rather than Fit: this one belongs to whoever is reading it, so
        // it should stay legible at any zoom even when it is far bigger than the pixel it names.
        c.DrawLabel(ViewGeometry.ScreenRectOf(tile),
                    $"{tile.X} {tile.Y}\n{_targetName.ToUpperInvariant()}", Palette.Loud,
                    TextSize.Small, LabelPlacement.Below, scale: 2, plate: Palette.Plate);
    }

    /// <summary>Reads the material under the cursor once a frame, for the painter and the captions.</summary>
    private static void ReadTarget()
    {
        _target = -1;
        _targetName = "NOTHING";

        var field = Simulation.CurrentState?.Field;
        if (field == null || ViewGeometry.MouseTile() is not { } tile)
            return;

        var id = field.Get(tile.X, tile.Y);
        // -1 is air and -2 is out of bounds. Tinting every air pixel on screen would be a solid
        // sheet of colour rather than a demonstration of anything.
        if (id < 0)
        {
            _targetName = id == -1 ? "AIR" : "OUT OF BOUNDS";
            return;
        }

        _target = id;
        _targetName = id.ToMaterialName() ?? "UNKNOWN";
    }

    /// <summary>
    /// The Alt painter: every pixel on screen, every frame, tinted if it holds the same material as
    /// the pixel under the cursor.
    ///
    /// <para>This is what a painter is for -- a question asked of every visible pixel, whose answer
    /// changes as the simulation runs -- and why <see cref="DrawWhen.AltHeld"/> exists: at the
    /// zoom a world starts at this is about 39,000 delegate calls a frame, which is affordable
    /// when somebody asked for it and wasteful the rest of the time.</para>
    /// </summary>
    internal static void PaintMatchingMaterial(VisiblePixel p)
    {
        if (_target < 0 || p.MaterialTypeId != _target)
            return;

        p.Fill(new Color(Palette.Loud, 0.45f));
        // Only once a pixel is big enough for an outline to be distinguishable from a fill.
        if (ViewGeometry.PixelScreenSize >= 12f)
            p.Outline(Palette.Loud);

        // And the name on the pixel itself, at whatever size fits inside it. This is the case
        // TextSize.Fit exists for: there may be thousands of these, and a label that kept its size
        // as the view zoomed out would turn the screen into a grey smear of overlapping plates.
        // Fit draws nothing once the pixel is too small to hold the text, so the labels simply
        // thin out as you zoom away and nothing has to decide a cutoff.
        // Laid out rather than merely fitted, which is the difference between the two text
        // mechanisms. Material names are long and share prefixes -- "Carbon" is a material and so
        // is Carbon Dioxide -- so this wraps at spaces, cuts where it must, and always marks a cut
        // with an ellipsis so a short answer cannot be read as a different material's full name.
        var fitted = LabelLayout.Choose("", _targetName.ToUpperInvariant(), p.Tile);

        // Skipped when it does not fit rather than drawn overflowing. One overflowing label is
        // readable; there may be hundreds of these on adjacent pixels, and a hundred of them is a
        // grey smear. LabelLayout hands the decision over precisely because both answers are right
        // for somebody.
        if (!fitted.Overflows)
            p.Canvas.DrawLabel(p.Screen, fitted, Palette.Caption, outline: Palette.Edge);

        _matching++;
    }

    /// <summary>
    /// A number as English, in the invariant culture. The game runs under whatever locale the
    /// player has, and a decimal comma in a readout reads as a thousands separator.
    /// </summary>
    private static string N(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// The zoom, to three decimals.
    ///
    /// <para>One more than everything else here, because the zoom is the one figure whose steps
    /// are eighths: a world pixel is 8 world units, so a whole number of screen pixels per world
    /// pixel is a zoom of n/8. IntegerZoom snaps to exactly those, and at two decimals 0.875 and
    /// 0.88 read as the same number, so its steps could not be told apart.</para>
    /// </summary>
    private static string Z(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);
}
