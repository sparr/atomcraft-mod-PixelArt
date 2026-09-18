using Atomcraft;
using Godot;

namespace PixelArt;

/// <summary>
/// A surface one mod draws on, above the world and above the HUD.
///
/// <para>Ask for one by name and keep it:</para>
/// <code>
/// private static readonly Canvas Marks = Canvas.For("Pressure");
/// </code>
///
/// <para>Everything is drawn in render-target space and rebuilt from scratch every frame from
/// pixel coordinates. Two consequences are worth knowing up front. Marks track their pixels: pan
/// or zoom and they stay on the pixels they name. And marks are not lit, shadowed or fogged the
/// way the world is, so a fill is the colour you asked for even over an unexplored or
/// pitch-dark pixel, which is the point when what you are showing is why a pixel is not what
/// somebody expected.</para>
///
/// <para>The alternative -- writing into the texture the game builds the world from -- was
/// rejected: it would put marks under the lighting and fog they most need to survive, and it
/// would bind this to three private fields of the game's renderer.</para>
///
/// <para><b>Two lifetimes, and the names say which.</b> The methods that take a tile add a
/// <i>retained</i> mark, redrawn every frame until <see cref="Clear"/>. The <c>Draw*</c>
/// methods take a screen rectangle, draw for this frame only, and are what a pass or a painter
/// calls:</para>
/// <code>
/// Marks.Outline(tile, Colors.Red, thickness: 2f);          // stays until Clear
/// Marks.Label(tile, "leak", Colors.Red, TextSize.Small, LabelPlacement.Above);
///
/// Marks.SetPainter("excess", p =&gt;                          // every visible pixel, every frame
/// {
///     var excess = Pressure.ExcessAt(p.Tile);
///     if (excess &gt; 0)
///         p.Label(excess.ToString(), Colors.White);
/// }, DrawWhen.AltHeld);
///
/// Marks.SetPass("cursor", c =&gt;                             // once a frame, walk what you like
/// {
///     if (ViewGeometry.MouseTile() is {} tile)
///         c.DrawOutline(ViewGeometry.ScreenRectOf(tile), Colors.Yellow);
/// });
/// </code>
///
/// <para>A painter costs a delegate call per visible pixel per frame: around 57,000 of them at
/// the zoomed-out default on a 1920x1080 window, around 900 at 8x zoom. That is affordable for
/// cheap work, and it is why <see cref="DrawWhen.AltHeld"/> exists and why a pass that only
/// wants the pixels near the cursor should walk them itself rather than reject 56,000.</para>
///
/// <para>Each canvas is its own Godot <c>CanvasLayer</c>, so one mod's <see cref="Clear"/>
/// cannot take another mod's marks with it, and <see cref="Layer"/> decides who draws on
/// top.</para>
/// </summary>
public sealed class Canvas
{
    /// <summary>
    /// As high as Godot's canvas layers go, and where the TestHarness puts its own overlay: a
    /// debug mark that the thing being debugged can cover is not much of a debug mark.
    /// </summary>
    public const int TopLayer = 128;

    /// <summary>
    /// Where a canvas sits unless it says otherwise: above the world and the HUD, one step
    /// below <see cref="TopLayer"/>. Sitting just under it means that when someone is running a
    /// mod and the harness together -- which is exactly what a <c>play.sh --debug</c> does -- a
    /// harness mark draws on top rather than fighting for the same plane.
    /// </summary>
    public const int DefaultLayer = 126;

    /// <summary>
    /// Past this many retained marks on one canvas, say so once. Marks are retained on purpose,
    /// so adding one per frame is not an error, but it is nearly always someone reaching for a
    /// mark where they wanted a painter, and the symptom otherwise is a session that slows down
    /// for no visible reason.
    /// </summary>
    private const int MarkWarningThreshold = 10_000;

    /// <summary>
    /// How many clips in one frame is enough to say something. Each one costs a server canvas item
    /// kept for the life of the canvas, and the case this was built for uses exactly one: a
    /// consumer reaching dozens is almost certainly clipping inside a per-cell loop, where every
    /// draw call already sizes itself to the box it is handed and a clip buys nothing.
    /// </summary>
    private const int ClipWarningThreshold = 64;

    private static readonly List<Canvas> Canvases = new();

    /// <summary>
    /// The canvas registered under <paramref name="owner"/>, created on the first call.
    ///
    /// <para>Call it as often as you like: the same name always gives the same canvas, so a
    /// caller need not hold one in a static if it would rather not. The name is the mod's, it
    /// is what appears on the Godot node and in this mod's log lines, and asking for an
    /// existing name with a different layer is refused rather than silently moving somebody
    /// else's drawing.</para>
    /// </summary>
    public static Canvas For(string owner, int layer = DefaultLayer)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("a canvas needs an owner name, so its marks can be told " +
                                        "from another mod's", nameof(owner));

        foreach (var existing in Canvases)
        {
            if (existing.Owner != owner)
                continue;
            if (existing.Layer != layer)
                throw new ArgumentException(
                    $"canvas '{owner}' already exists on layer {existing.Layer}, and something " +
                    $"is now asking for it on layer {layer}. Two mods sharing one owner name " +
                    "would clear each other's marks; pick a name of your own.");
            return existing;
        }

        var canvas = new Canvas(owner, layer);
        Canvases.Add(canvas);
        // Layer order is Godot's business once the nodes exist; this keeps All, and so the
        // painter walk, in a stable and readable order.
        Canvases.Sort((a, b) => a.Layer.CompareTo(b.Layer));
        return canvas;
    }

    /// <summary>Every canvas that has been asked for, lowest layer first.</summary>
    public static IReadOnlyList<Canvas> All => Canvases;

    /// <summary>
    /// Drops every mark and every pass on every canvas, whoever registered them, and clears
    /// their faults.
    ///
    /// <para>This is what the harness runs between tests, and the sweep is deliberately total: a
    /// mod's own debug painter drawing across every test's screenshot would make those
    /// screenshots evidence of something other than the test. A pass a mod registers from its
    /// <c>Initialize</c> therefore survives an ordinary play session, where no test ever ends,
    /// and does not survive the first test of a run.</para>
    /// </summary>
    public static void ResetAll()
    {
        foreach (var canvas in Canvases)
            canvas.Reset();
    }

    /// <summary>
    /// Drops a canvas entirely: its marks, its passes, its Godot node and its server resources,
    /// and its registration.
    ///
    /// <para>Rarely what you want -- <see cref="Visible"/> switches a canvas off and keeps
    /// everything registered, which is the reversible version. This is for a test that makes
    /// canvases of its own and should not leave a layer behind for every one, and for a mod that
    /// is genuinely done drawing for the rest of the session.</para>
    ///
    /// <para>Returns whether there was one. A reference someone still holds keeps working and
    /// simply draws nothing, so this cannot fault another mod that was mid-frame.</para>
    /// </summary>
    public static bool Forget(string owner)
    {
        var index = Canvases.FindIndex(c => c.Owner == owner);
        if (index < 0)
            return false;
        var canvas = Canvases[index];
        canvas.Reset();
        canvas.DestroyNode();
        Canvases.RemoveAt(index);
        return true;
    }

    private Canvas(string owner, int layer)
    {
        Owner = owner;
        Layer = layer;
    }

    /// <summary>The mod this canvas belongs to.</summary>
    public string Owner { get; }

    /// <summary>Which Godot canvas layer it draws on. Higher draws on top.</summary>
    public int Layer { get; }

    /// <summary>
    /// Whether this canvas draws at all. False leaves it registered and empty, which is how a
    /// mod offers its own off switch without tearing anything down.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Set when a pass or a painter on this canvas threw, which stops this canvas for the rest
    /// of the session. <c>godot.log</c> has one report of why; other canvases are unaffected.
    /// </summary>
    public bool Faulted { get; private set; }

    /// <summary>What went wrong, kept so a test can assert on it rather than grep the log.</summary>
    public Exception? Fault { get; private set; }

    /// <summary>How many pixels this canvas's painters were run over on the last frame they ran.</summary>
    public int PixelsPaintedLastFrame { get; internal set; }

    /// <summary>How many retained marks are standing.</summary>
    public int MarkCount => _marks.Count;

    /// <summary>How many per-frame passes and painters are registered.</summary>
    public int PassCount => _passes.Count;

    /// <summary>Clears a latched fault, so the canvas draws again. See <see cref="Faulted"/>.</summary>
    public void ClearFault()
    {
        Faulted = false;
        Fault = null;
    }

    // ------------------------------------------------------------ retained marks

    private abstract class Mark
    {
        public abstract void Draw(Canvas canvas);
    }

    private sealed class FillMark : Mark
    {
        public RectInt Tiles; public Color Color;
        public override void Draw(Canvas canvas) => canvas.DrawFill(ViewGeometry.ScreenRectOf(Tiles), Color);
    }

    private sealed class OutlineMark : Mark
    {
        public RectInt Tiles; public Color Color; public float Thickness;
        public override void Draw(Canvas canvas) =>
            canvas.DrawOutline(ViewGeometry.ScreenRectOf(Tiles), Color, Thickness);
    }

    private sealed class ArrowMark : Mark
    {
        public RectInt Tiles; public Aim Aim; public Color Color; public Color? Surround;
        public override void Draw(Canvas canvas) =>
            canvas.DrawArrow(ViewGeometry.ScreenRectOf(Tiles), Aim, Color, Surround);
    }

    private sealed class IconMark : Mark
    {
        public RectInt Tiles; public IconArt? Art; public Color Tint;
        public override void Draw(Canvas canvas) =>
            canvas.DrawIcon(ViewGeometry.ScreenRectOf(Tiles), Art, Tint);
    }

    private sealed class LabelMark : Mark
    {
        public RectInt Tiles; public string Text = ""; public Color Color;
        public Color? Plate; public Color? Outline;
        public TextSize Size; public LabelPlacement Placement; public int Scale;
        public override void Draw(Canvas canvas) =>
            canvas.DrawLabel(ViewGeometry.ScreenRectOf(Tiles), Text, Color, Size, Placement, Scale,
                             Plate, Outline);
    }

    private readonly List<Mark> _marks = new();
    private bool _warnedManyMarks;

    private void Add(Mark mark)
    {
        _marks.Add(mark);
        if (_marks.Count < MarkWarningThreshold || _warnedManyMarks)
            return;
        _warnedManyMarks = true;
        Log.Warn($"canvas '{Owner}' is holding {_marks.Count} marks, and every one is redrawn " +
                 "every frame. Marks are retained until Clear; if you are adding them every " +
                 "frame, SetPainter and SetPass are the per-frame versions.");
    }

    /// <summary>
    /// Paints a pixel a flat colour, over whatever the game drew there.
    ///
    /// <para>The mark is remembered against the pixel, not against a place on screen, and
    /// redrawn every frame until <see cref="Clear"/>. Colours with alpha tint rather than
    /// replace, which is usually what you want when the material underneath is still worth
    /// seeing.</para>
    /// </summary>
    public void Fill(Vector2I tile, Color color) => Fill(new RectInt(tile.X, tile.Y, 1, 1), color);

    /// <summary>Paints a rectangle of pixels. One mark, so a large area costs one draw rather than thousands.</summary>
    public void Fill(RectInt tiles, Color color) => Add(new FillMark { Tiles = tiles, Color = color });

    /// <summary>
    /// Draws a coloured border around a pixel.
    ///
    /// <para>The border sits just inside the pixel's own edges, so outlining two neighbours
    /// leaves two distinct boxes rather than one shared smear, and an outline never covers a
    /// pixel it does not name. Thickness is in screen pixels, so it stays equally visible as the
    /// zoom changes rather than vanishing when you zoom out.</para>
    /// </summary>
    public void Outline(Vector2I tile, Color color, float thickness = 1f) =>
        Outline(new RectInt(tile.X, tile.Y, 1, 1), color, thickness);

    /// <summary>Draws one border around a whole rectangle of pixels, not around each pixel in it.</summary>
    public void Outline(RectInt tiles, Color color, float thickness = 1f) =>
        Add(new OutlineMark { Tiles = tiles, Color = color, Thickness = thickness });

    /// <summary>
    /// Points a triangle out of one edge of a pixel: which way a machine faces, which way
    /// something flows.
    ///
    /// <para>The triangle is a third of the pixel and at least two screen pixels, so it survives
    /// being zoomed out, where a strictly proportional mark would vanish. At the zoom a world
    /// starts at, a pixel is six screen pixels and the nub is at its floor of two; point at a block of
    /// pixels instead when it has to read from across the screen.</para>
    /// </summary>
    public void Arrow(Vector2I tile, Aim aim, Color color, Color? surround = null) =>
        Arrow(new RectInt(tile.X, tile.Y, 1, 1), aim, color, surround);

    /// <summary>
    /// Points one triangle out of one edge of a whole block of pixels, sized to the block rather
    /// than to a pixel.
    /// </summary>
    public void Arrow(RectInt tiles, Aim aim, Color color, Color? surround = null) =>
        Add(new ArrowMark { Tiles = tiles, Aim = aim, Color = color, Surround = surround });

    /// <summary>
    /// Writes text on a pixel, in a bitmap font drawn at a whole-number scale from a
    /// whole-number screen position, so every font pixel is an exact block of screen pixels at
    /// any zoom. Embedded newlines start a new line.
    ///
    /// <para>By default the largest of the three sizes that fits the pixel is used, which is
    /// usually what you want: a pixel is <c>8 * zoom</c> screen pixels, so the size that reads
    /// best changes as the view zooms and with how much text there is. Name a
    /// <see cref="TextSize"/> to fix it instead, when a row of labels has to come out at one
    /// size regardless of what each one says.</para>
    ///
    /// <para><paramref name="scale"/> multiplies whichever size is used, and the auto choice is
    /// made against the scaled size, so raising it picks a smaller font rather than overflowing.
    /// The player's own <c>textScale</c> setting multiplies it again.</para>
    ///
    /// <para><paramref name="plate"/> draws a rectangle behind the text first. Worth it over the
    /// game's machines, which are strongly coloured: the alternative, picking a text colour per
    /// material, throws away whatever the colour was saying.</para>
    ///
    /// <para>The size is chosen when the mark is drawn, not when it is added, so a retained
    /// label keeps choosing correctly as the view zooms.</para>
    /// </summary>
    public void Label(Vector2I tile, string text, Color color,
                      TextSize size = TextSize.Auto,
                      LabelPlacement placement = LabelPlacement.Center,
                      int scale = 1,
                      Color? plate = null,
                      Color? outline = null) =>
        Label(new RectInt(tile.X, tile.Y, 1, 1), text, color, size, placement, scale, plate, outline);

    /// <summary>
    /// Writes text against a whole block of pixels rather than one.
    ///
    /// <para>What <see cref="TextSize.Fit"/> usually wants: one pixel is about six screen pixels
    /// across at the zoom a world starts at, which is smaller than any glyph, so a label fitted to
    /// a single pixel is not drawn until the view is well zoomed in. Fitted to the block a thing
    /// actually occupies, it has room to say something.</para>
    /// </summary>
    public void Label(RectInt tiles, string text, Color color,
                      TextSize size = TextSize.Auto,
                      LabelPlacement placement = LabelPlacement.Center,
                      int scale = 1,
                      Color? plate = null,
                      Color? outline = null) =>
        Add(new LabelMark
        {
            Tiles = tiles, Text = text, Color = color, Plate = plate, Outline = outline,
            Size = size, Placement = placement, Scale = RequireScale(scale),
        });

    /// <summary>
    /// Draws one of the game's own shapes on a pixel, tinted: a square, a drop, a cloud, a little
    /// person. See <see cref="GameArt"/> for which and why.
    ///
    /// <para>Sized to fill the pixel less a margin <b>without distorting</b> -- the source's own
    /// proportions are kept and the smaller dimension decides, so a six-by-nine person stays a
    /// person rather than being stretched square -- and at a whole scale, so the nearest filter has
    /// no fraction to resolve. Below one screen pixel per source pixel nothing is drawn: a
    /// fractional downscale of a six-pixel-wide figure loses limbs rather than shrinking.</para>
    ///
    /// <para>Drawn behind whatever else is on the pixel if it is added first, which is usually
    /// what you want: it is there to be recognised at a glance, not to compete with a label
    /// sitting on it.</para>
    /// </summary>
    public void Icon(Vector2I tile, IconArt? art, Color tint) =>
        Icon(new RectInt(tile.X, tile.Y, 1, 1), art, tint);

    /// <summary>Draws one of the game's own shapes across a block of pixels.</summary>
    public void Icon(RectInt tiles, IconArt? art, Color tint) =>
        Add(new IconMark { Tiles = tiles, Art = art, Tint = tint });

    /// <summary>Drops this canvas's retained marks. Passes and painters are left alone.</summary>
    public void Clear() => _marks.Clear();

    /// <summary>
    /// Drops this canvas's marks, passes and painters, and clears its fault. The canvas itself
    /// stays registered, so a caller's reference to it stays good.
    /// </summary>
    public void Reset()
    {
        _marks.Clear();
        _passes.Clear();
        _warnedManyMarks = false;
        ClearFault();
        // Not a redraw: Reset is what the frame's backstop calls when a redraw threw, and
        // redrawing from there is how one bad frame becomes a loop. The command list is emptied
        // directly, and the next frame draws whatever is registered by then.
        ClearCommands();
    }

    // ------------------------------------------------------------------ per frame

    private sealed class Pass
    {
        public string Name = "";

        /// <summary>When to run. Null means every frame, which is the cheap common case.</summary>
        public Func<bool>? When;
        public Action<Canvas>? WholeFrame;
        public Action<VisiblePixel>? PerPixel;
    }

    /// <summary>
    /// The predicate <see cref="DrawWhen.AltHeld"/> is shorthand for. One instance rather than a
    /// lambda per registration, since registering is not where the cost should be.
    /// </summary>
    private static readonly Func<bool> WhileAltHeld = () => AltHeld;

    private static Func<bool>? PredicateFor(DrawWhen when) =>
        when == DrawWhen.AltHeld ? WhileAltHeld : null;

    private readonly List<Pass> _passes = new();

    /// <summary>
    /// Registers a callback to run once per frame, replacing anything already registered under
    /// the same name on this canvas.
    ///
    /// <para>The callback is handed this canvas and draws on it with the <c>Draw*</c> methods,
    /// which last for that frame only. Use it when the mod knows which pixels it cares about --
    /// the ones near the cursor, the ones it has state for -- since walking those directly is
    /// far cheaper than being handed all 57,000 on screen and rejecting most of them.</para>
    /// </summary>
    public void SetPass(string name, Action<Canvas> pass, DrawWhen when = DrawWhen.Always) =>
        Replace(new Pass { Name = name, When = PredicateFor(when), WholeFrame = pass });

    /// <summary>
    /// Registers a once-a-frame callback that runs only when <paramref name="when"/> says so.
    ///
    /// <para>The predicate is asked once per frame, before the pass is called, and the pass is not
    /// called at all when it answers false -- so an expensive pass behind a false predicate costs
    /// one delegate call a frame.</para>
    ///
    /// <para><b>Prefer this to <see cref="DrawWhen.AltHeld"/> whenever the condition is the mod's
    /// own.</b> AltHeld reads the real keyboard and nothing else, so a mod whose own setting says
    /// "only while Alt is held" cannot express "and only when my setting is on", and no test can
    /// reach the drawing at all: a test cannot hold a key down. A predicate is both. AltHeld
    /// remains as the shorthand for <c>() =&gt; Canvas.AltHeld</c>, which is what it now is.</para>
    ///
    /// <para>A predicate that throws is treated exactly as a pass that throws: removed, its canvas
    /// faulted, one line in the log. It runs on the same per-frame path and must not be the thing
    /// that fills a disk.</para>
    /// </summary>
    public void SetPass(string name, Action<Canvas> pass, Func<bool> when) =>
        Replace(new Pass { Name = name, When = when, WholeFrame = pass });

    /// <summary>
    /// Registers a callback to run for every pixel on screen, every frame, replacing anything
    /// already registered under the same name on this canvas.
    ///
    /// <para>The callback is handed each visible pixel in turn and draws on it or does not. Draw
    /// calls made from a painter last for that frame only, which is what makes a painter the
    /// right shape for state that changes as the simulation runs.</para>
    ///
    /// <para>Pass <see cref="DrawWhen.AltHeld"/> to have it run only while the player holds Alt,
    /// joining the game's own modifier for extra detail. On every other frame it is not called
    /// at all, so an expensive painter costs nothing until someone asks for it.</para>
    ///
    /// <para>A painter that throws is removed rather than left to throw again next frame: Godot
    /// logs an unhandled per-frame exception every frame with no backpressure, and one bad hook
    /// is enough to fill a disk. Its canvas is faulted, the exception is logged once, and every
    /// other canvas keeps drawing.</para>
    /// </summary>
    public void SetPainter(string name, Action<VisiblePixel> painter, DrawWhen when = DrawWhen.Always) =>
        Replace(new Pass { Name = name, When = PredicateFor(when), PerPixel = painter });

    /// <summary>
    /// Registers a per-pixel painter that runs only when <paramref name="when"/> says so. See
    /// <see cref="SetPass(string, Action{Canvas}, Func{bool})"/> for why a predicate is usually
    /// the better gate.
    ///
    /// <para>The predicate is asked once per frame, not once per pixel, so a painter that is not
    /// due costs one delegate call rather than 57,000.</para>
    /// </summary>
    public void SetPainter(string name, Action<VisiblePixel> painter, Func<bool> when) =>
        Replace(new Pass { Name = name, When = when, PerPixel = painter });

    private void Replace(Pass pass)
    {
        Remove(pass.Name);
        _passes.Add(pass);
    }

    /// <summary>Removes a pass or painter by name. Removing one that was never registered is not an error.</summary>
    public void Remove(string name) => _passes.RemoveAll(p => p.Name == name);

    /// <summary>
    /// Runs <paramref name="body"/> over a block of pixels, handing it each one the way a painter
    /// is handed one: the tile, what is in it, and where it is on screen.
    ///
    /// <para><b>This is what a cursor-sized pass wants.</b> A painter is handed every pixel on
    /// screen -- about 57,000 at the zoom a world starts at -- and a mod that cares about the 169
    /// around the cursor spends 56,831 delegate calls saying no. A pass gets none and has to walk
    /// them itself, which means reprojecting the corner and stepping the rows, which is this
    /// method. Now it is four lines:</para>
    ///
    /// <code>
    /// canvas.SetPass("cursor", c =&gt;
    /// {
    ///     if (ViewGeometry.MouseTile() is { } tile)
    ///         c.ForEachPixel(ViewGeometry.Around(tile, 6), p =&gt; p.Outline(Colors.Yellow));
    /// });
    /// </code>
    ///
    /// <para>The block is clipped to what is actually on screen and inside the world, so a caller
    /// need not check either: a radius that runs off the edge of the view simply yields fewer
    /// pixels. Returns how many it visited, which is the number worth logging when a pass looks
    /// like it is doing nothing.</para>
    ///
    /// <para>The corner is projected once and the rows are stepped from it, rather than asking
    /// <see cref="ViewGeometry.ScreenRectOf(Vector2I)"/> per pixel -- that call asks the engine for
    /// the camera and the viewport every time, which is the cost this exists to avoid.</para>
    ///
    /// <para>An exception from <paramref name="body"/> is not caught here. Called from a pass, as
    /// intended, the pass's own guard has it: the canvas faults and the pass is removed, exactly as
    /// if the throw had happened directly in the pass.</para>
    /// </summary>
    public int ForEachPixel(RectInt tiles, Action<VisiblePixel> body)
    {
        var field = Simulation.CurrentState?.Field;
        if (field == null || ViewGeometry.VisibleTiles() is not { } onScreen)
            return 0;

        var area = tiles.Intersection(onScreen);
        if (area.width <= 0 || area.height <= 0)
            return 0;

        var size = ViewGeometry.PixelScreenSize;
        var corner = ViewGeometry.WorldToScreen(
            new Vector2(area.min.X * ViewGeometry.TileSize, area.min.Y * ViewGeometry.TileSize));

        var visited = 0;
        for (var y = area.min.Y; y < area.max.Y; y++)
        for (var x = area.min.X; x < area.max.X; x++)
        {
            var screen = new Rect2(corner.X + (x - area.min.X) * size,
                                   corner.Y + (y - area.min.Y) * size,
                                   size, size);
            body(new VisiblePixel(this, new Vector2I(x, y), field.Get(x, y), screen));
            visited++;
        }
        return visited;
    }

    /// <summary>Runs <paramref name="body"/> over the pixels within <paramref name="radius"/> of a tile.</summary>
    public int ForEachPixel(Vector2I centre, int radius, Action<VisiblePixel> body) =>
        ForEachPixel(ViewGeometry.Around(centre, radius), body);

    /// <summary>
    /// The same walk, but <paramref name="body"/> returns whether to keep going: false stops it
    /// where it stands.
    ///
    /// <para>Irrelevant for a block of 169 pixels and worth having for a scan of the whole screen,
    /// where the answer is usually found long before the end. Returns how many pixels were
    /// visited, including the one that stopped it.</para>
    ///
    /// <code>
    /// Vector2I? found = null;
    /// canvas.ForEachPixelWhile(area, p =&gt;
    /// {
    ///     if (p.MaterialTypeId != wanted) return true;   // keep looking
    ///     found = p.Tile; return false;                  // stop here
    /// });
    /// </code>
    ///
    /// <para>A separate name rather than an overload: <c>Action&lt;VisiblePixel&gt;</c> and
    /// <c>Func&lt;VisiblePixel, bool&gt;</c> are ambiguous for some lambda bodies, and a caller
    /// should never have to think about which one a lambda bound to.</para>
    /// </summary>
    public int ForEachPixelWhile(RectInt tiles, Func<VisiblePixel, bool> body)
    {
        var field = Simulation.CurrentState?.Field;
        if (field == null || ViewGeometry.VisibleTiles() is not { } onScreen)
            return 0;

        var area = tiles.Intersection(onScreen);
        if (area.width <= 0 || area.height <= 0)
            return 0;

        var size = ViewGeometry.PixelScreenSize;
        var corner = ViewGeometry.WorldToScreen(
            new Vector2(area.min.X * ViewGeometry.TileSize, area.min.Y * ViewGeometry.TileSize));

        var visited = 0;
        for (var y = area.min.Y; y < area.max.Y; y++)
        for (var x = area.min.X; x < area.max.X; x++)
        {
            var screen = new Rect2(corner.X + (x - area.min.X) * size,
                                   corner.Y + (y - area.min.Y) * size,
                                   size, size);
            visited++;
            if (!body(new VisiblePixel(this, new Vector2I(x, y), field.Get(x, y), screen)))
                return visited;
        }
        return visited;
    }

    /// <summary>The stopping walk, over the pixels within <paramref name="radius"/> of a tile.</summary>
    public int ForEachPixelWhile(Vector2I centre, int radius, Func<VisiblePixel, bool> body) =>
        ForEachPixelWhile(ViewGeometry.Around(centre, radius), body);

    /// <summary>Whether the player is holding Alt right now, read the same way the game reads it.</summary>
    public static bool AltHeld => Input.IsKeyPressed(Key.Alt);

    /// <summary>
    /// Whether a pass is due this frame, with the predicate's own failure handled here rather
    /// than left to escape into the shared frame.
    /// </summary>
    private bool Due(Pass pass)
    {
        if (pass.When == null)
            return true;
        try
        {
            return pass.When();
        }
        catch (Exception e)
        {
            Blame(pass, e, " deciding whether to run");
            return false;
        }
    }

    /// <summary>Whether this canvas has anything at all to put on screen this frame.</summary>
    internal bool HasWork => Visible && !Faulted && (_marks.Count > 0 || _passes.Count > 0);

    /// <summary>
    /// Draws the retained marks and runs the whole-frame passes. The per-pixel painters are run
    /// by <see cref="Renderer"/>, which walks the visible pixels once for every canvas rather
    /// than once each.
    /// </summary>
    internal void DrawMarksAndPasses()
    {
        foreach (var mark in _marks)
            mark.Draw(this);

        // Over a copy: a pass may register or remove another, and one that throws is removed
        // from the live list below.
        foreach (var pass in _passes.ToArray())
        {
            if (pass.WholeFrame == null || !Due(pass))
                continue;
            try
            {
                pass.WholeFrame(this);
            }
            catch (Exception e)
            {
                Blame(pass, e, "");
                return;
            }
        }
    }

    /// <summary>Every per-pixel painter due to run this frame, appended to <paramref name="into"/>.</summary>
    internal void CollectPainters(List<(Canvas Canvas, object Pass)> into)
    {
        foreach (var pass in _passes)
            if (pass.PerPixel != null && Due(pass))
                into.Add((this, pass));
    }

    /// <summary>Runs one collected painter over one pixel, and reports whether it survived.</summary>
    internal bool Paint(object pass, Vector2I tile, short materialTypeId, Rect2 screen)
    {
        var painter = (Pass)pass;
        try
        {
            painter.PerPixel!(new VisiblePixel(this, tile, materialTypeId, screen));
            return true;
        }
        catch (Exception e)
        {
            Blame(painter, e, $" at {tile}");
            return false;
        }
    }

    /// <summary>
    /// Removes the offending pass, faults the canvas, and says so once.
    ///
    /// <para>Removed rather than retried: an unhandled per-frame exception is logged by Godot on
    /// every occurrence with no backpressure, and something that threw on one pixel will throw on
    /// the next one too. Faulting the canvas as well means a mod with several passes does not
    /// limp along drawing half of what it meant to while the log holds the only clue.</para>
    /// </summary>
    private void Blame(Pass pass, Exception e, string where)
    {
        _passes.Remove(pass);
        Faulted = true;
        Fault = e;
        ClearCommands();
        Log.Error($"'{pass.Name}' on canvas '{Owner}' threw{where} and has been removed; that " +
                  $"canvas is switched off for this session. Other mods are unaffected. {e}");
    }

    // -------------------------------------------------------------------- drawing

    private CanvasLayer? _node;
    private Rid _item;

    /// <summary>
    /// Children of <see cref="_item"/> that exist only to be clipped. Commands go to one of these
    /// instead of to <see cref="_item"/> while <see cref="CurrentClip"/> is set.
    ///
    /// <para><b>Separate items rather than a flag, and one per clip rather than one in total.</b>
    /// The server clips an item as a whole: a clip is a property that lives on the item until the
    /// frame is drawn, not a command in its list. So there is no way to scissor some of one item's
    /// commands and not others, and -- the part that is easy to get wrong -- no way to turn a clip
    /// off when a scope ends, because the frame has not been drawn yet. Turning it off at
    /// <see cref="ClearClip"/> is what the first version of this did, and the clip then never
    /// applied at all.</para>
    ///
    /// <para>One item per clip used in a frame follows from that: a second clip reusing the first
    /// item would re-point its rectangle, and the content drawn under the first clip would be cut
    /// to the second one's boundary instead. The pool is reused across frames rather than freed, so
    /// it settles at the high-water mark of any single frame.</para>
    /// </summary>
    private readonly List<Rid> _clipItems = new();

    /// <summary>How many of <see cref="_clipItems"/> have been handed out this frame.</summary>
    private int _clipsUsed;

    /// <summary>The item the current clip's commands go to, valid only while <see cref="_clip"/> is set.</summary>
    private Rid _activeClipItem;

    private bool _warnedManyClips;

    private Rect2? _clip;

    /// <summary>
    /// The rectangle drawing is currently trimmed to, in screen pixels, or null when it is not.
    ///
    /// <para>Public so a consumer can tell whether it is inside a clip without tracking that
    /// itself, and so a test can see the reset happen.</para>
    /// </summary>
    public Rect2? CurrentClip => _clip;

    /// <summary>Where a draw call appends: the clipped child while a clip is set, the item itself otherwise.</summary>
    private Rid Target => _clip.HasValue ? _activeClipItem : _item;

    /// <summary>
    /// Makes sure there is something to draw into, and returns false when there is not: headless,
    /// or before the scene tree is up. Neither is an error worth a log line every frame.
    /// </summary>
    internal bool EnsureCanvas()
    {
        if (Renderer.Headless)
            return false;

        if (_node != null && GodotObject.IsInstanceValid(_node))
            return true;

        var root = Game.Instance?.GetTree()?.Root;
        if (root == null)
            return false;

        // A plain CanvasLayer plus a RenderingServer canvas item, rather than a Control with a
        // _Draw override: a mod is compiled without Godot's source generators, so a CanvasItem
        // subclass of ours would never have its _Draw called. The server API needs no generated
        // bridge and gives exact control over the command list.
        _node = new CanvasLayer { Name = $"PixelArt.{Owner}", Layer = Layer };
        // A canvas item is a server resource, not a node, so nothing frees it when the tree goes
        // away; without this the engine reports a leaked RID at every exit.
        _node.TreeExiting += ReleaseCanvas;
        root.AddChild(_node);

        _item = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(_item, _node.GetCanvas());
        // Nearest, so a glyph scaled by a whole number stays a grid of hard-edged blocks.
        RenderingServer.CanvasItemSetDefaultTextureFilter(
            _item, RenderingServer.CanvasItemTextureFilter.Nearest);

        _clipItems.Clear();
        _clipsUsed = 0;
        _clip = null;
        _activeClipItem = default;
        return true;
    }

    /// <summary>
    /// Empties this frame's command list without tearing the canvas down, and drops any clip with
    /// it.
    ///
    /// <para>The clip is per frame on purpose. A pass that throws between setting one and clearing
    /// it would otherwise leave the canvas trimmed for the rest of the session, and the symptom --
    /// marks silently missing outside a rectangle nobody remembers setting -- is far harder to
    /// place than the exception that caused it.</para>
    /// </summary>
    internal void ClearCommands()
    {
        if (_item.IsValid && _node != null && GodotObject.IsInstanceValid(_node))
        {
            RenderingServer.CanvasItemClear(_item);
            foreach (var clipped in _clipItems)
                if (clipped.IsValid)
                    RenderingServer.CanvasItemClear(clipped);
        }

        // The pool is handed out again from the start, not freed: a canvas that clipped once last
        // frame will clip once this frame, and re-creating the item every frame would churn a
        // server resource for nothing.
        _clipsUsed = 0;
        ClearClip();
    }

    private void ReleaseCanvas()
    {
        // The child first: freeing a parent does not free its children, and a canvas item is a
        // server resource that nothing else owns. Without this the engine reports a leaked RID at
        // exit, which is the same bug the comment on _item warns about.
        // The children first: freeing a parent does not free its children, and a canvas item is a
        // server resource that nothing else owns. Without this the engine reports a leaked RID at
        // exit, which is the same bug the comment on _item warns about.
        foreach (var clipped in _clipItems)
            if (clipped.IsValid)
                RenderingServer.FreeRid(clipped);
        _clipItems.Clear();
        _clipsUsed = 0;
        _activeClipItem = default;

        if (_item.IsValid)
            RenderingServer.FreeRid(_item);
        _item = default;
        _clip = null;
        _node = null;
    }

    /// <summary>Frees the server resources and takes the layer out of the scene tree with them.</summary>
    private void DestroyNode()
    {
        var node = _node;
        ReleaseCanvas();
        if (node != null && GodotObject.IsInstanceValid(node))
        {
            // The handler would call ReleaseCanvas again on a canvas that may have been rebuilt
            // by then, which would free somebody else's item.
            node.TreeExiting -= ReleaseCanvas;
            node.QueueFree();
        }
    }

    /// <summary>Drops every canvas's server resources and the font atlases. Called when the tree goes away.</summary>
    internal static void ReleaseAll()
    {
        foreach (var canvas in Canvases)
            canvas.ReleaseCanvas();
        PixelFont.ReleaseAll();
    }

    /// <summary>
    /// Trims everything drawn from here until the clip is cleared to <paramref name="screen"/>,
    /// and returns a scope that clears it.
    ///
    /// <code>
    /// using (canvas.Clip(panel))
    /// {
    ///     // every Draw call here is cut at the panel's edge
    /// }
    /// </code>
    ///
    /// <para><b>All of them, not just the rectangular ones.</b> A consumer can intersect its own
    /// rectangle before calling <see cref="DrawFill(Rect2, Color)"/>, and can hand-roll an outline
    /// as four fills, and that is where it runs out: a shape, an arrow and a label reach the server
    /// as a texture region, a polygon and a run of glyph quads, none of which a caller can trim
    /// from outside. That asymmetry is why this belongs here.</para>
    ///
    /// <para><b>Glyphs are cut mid-glyph</b>, and shapes mid-shape. That is the point rather than a
    /// limitation: content running off the edge of a panel is what a panel looks like, and the
    /// alternative -- dropping whole elements that cross the boundary -- is what a consumer can
    /// already do for itself, at the cost of reconstructing where each call lands.</para>
    ///
    /// <para><b>Cleared at the start of every frame</b>, whatever the last one did. A pass that
    /// throws between setting a clip and clearing it cannot leave the canvas trimmed forever; the
    /// scope returned here is belt to that braces.</para>
    ///
    /// <para>No nesting. Setting a clip while one is in effect replaces it rather than
    /// intersecting -- a consumer wanting the intersection can pass it, and the one case this was
    /// built for clips once, to a panel, and never to anything inside it.</para>
    ///
    /// <para><b>Clipped content draws above unclipped content</b>, whatever order the calls were
    /// made in. A clip is a property of a canvas item rather than a command, so clipped drawing has
    /// to go to a child item, and a child draws after its parent. Within one clip, and within the
    /// unclipped drawing, call order is preserved as usual; it is only the two groups that cannot
    /// interleave. A consumer that needs something over the top of a clipped panel should use a
    /// second canvas on a higher layer.</para>
    ///
    /// <para>Each clip used in a frame costs a server canvas item, kept and reused for the life of
    /// the canvas. One a frame is the expected shape and what this was built for; dozens will log a
    /// warning, because a clip inside a per-cell loop is almost always unnecessary -- every draw
    /// call already sizes itself to the box it is handed.</para>
    /// </summary>
    /// <param name="screen">The rectangle to trim to, in screen pixels, unsnapped.</param>
    public ClipScope Clip(Rect2 screen)
    {
        SetClip(screen);
        return new ClipScope(this);
    }

    /// <summary>
    /// Trims drawing to <paramref name="screen"/> until <see cref="ClearClip"/> or the end of the
    /// frame. <see cref="Clip"/> is the same thing with the clearing attached; prefer it.
    /// </summary>
    public void SetClip(Rect2 screen)
    {
        if (!EnsureCanvas() || !_item.IsValid)
            return;

        // An empty or backwards rectangle draws nothing rather than throwing: a consumer computing
        // one as the intersection of a panel and a cell will produce plenty of them at the rim, and
        // "nothing is inside it" is the right answer, not an error.
        var rect = screen.Size.X <= 0f || screen.Size.Y <= 0f
            ? new Rect2(screen.Position, Vector2.Zero)
            : screen;

        _activeClipItem = TakeClipItem();
        _clip = rect;
        RenderingServer.CanvasItemSetCustomRect(_activeClipItem, true, rect);
        RenderingServer.CanvasItemSetClip(_activeClipItem, true);
    }

    /// <summary>
    /// Stops trimming, so later draw calls go back to the unclipped item. Harmless when nothing was
    /// clipped.
    ///
    /// <para>This does <b>not</b> switch the clip off on the server, and cannot: the frame has not
    /// been drawn yet, so turning it off here would mean it never applied. It stops routing, and
    /// the item keeps both its commands and its clip until the frame is over.</para>
    /// </summary>
    public void ClearClip()
    {
        _clip = null;
        _activeClipItem = default;
    }

    /// <summary>
    /// The next clip item of the pool, creating one if this frame has used more than any frame
    /// before it.
    /// </summary>
    private Rid TakeClipItem()
    {
        if (_clipsUsed < _clipItems.Count)
            return _clipItems[_clipsUsed++];

        var created = RenderingServer.CanvasItemCreate();
        // Parented to _item so it inherits its transform, and given the same filter, since a
        // clipped label is still a bitmap font. Note that being a child also puts it ABOVE the
        // unclipped commands in draw order whatever order they were issued in -- see Clip.
        RenderingServer.CanvasItemSetParent(created, _item);
        RenderingServer.CanvasItemSetDefaultTextureFilter(
            created, RenderingServer.CanvasItemTextureFilter.Nearest);
        _clipItems.Add(created);
        _clipsUsed++;

        if (_clipItems.Count >= ClipWarningThreshold && !_warnedManyClips)
        {
            _warnedManyClips = true;
            Log.Warn($"canvas '{Owner}' has used {_clipItems.Count} clips in one frame. Each is a " +
                     "server canvas item held for the life of the canvas. Clipping once around a " +
                     "loop costs one; clipping inside it costs one per iteration, and is usually " +
                     "not needed, since every draw call already sizes itself to the box it is given.");
        }

        return created;
    }

    /// <summary>
    /// What <see cref="Clip"/> returns: clears the clip when it goes out of scope.
    ///
    /// <para>A struct so a <c>using</c> in a per-frame pass allocates nothing. The compiler calls
    /// <see cref="Dispose"/> directly on a known struct type rather than boxing it.</para>
    /// </summary>
    public readonly struct ClipScope : IDisposable
    {
        private readonly Canvas? _canvas;

        internal ClipScope(Canvas canvas) => _canvas = canvas;

        public void Dispose() => _canvas?.ClearClip();
    }

    /// <summary>Paints a screen rectangle, for this frame only.</summary>
    public void DrawFill(Rect2 screen, Color color)
    {
        if (!_item.IsValid)
            return;
        RenderingServer.CanvasItemAddRect(Target, Snap(screen), color);
    }

    /// <summary>Paints a pixel, for this frame only.</summary>
    public void DrawFill(Vector2I tile, Color color) => DrawFill(ViewGeometry.ScreenRectOf(tile), color);

    /// <summary>Paints a block of pixels, for this frame only.</summary>
    public void DrawFill(RectInt tiles, Color color) => DrawFill(ViewGeometry.ScreenRectOf(tiles), color);

    /// <summary>Outlines a screen rectangle from the inside, for this frame only.</summary>
    public void DrawOutline(Rect2 screen, Color color, float thickness = 1f)
    {
        if (!_item.IsValid)
            return;

        var r = Snap(screen);

        // Never more than half the box: past that the two side bars would be asked for a negative
        // height. Below one, there is no half to work with -- the box is a sliver one or two screen
        // pixels across, with no room for a border and an interior both -- and an outline of it is
        // a filled sliver. Draw that rather than throwing or drawing nothing.
        //
        // Written as a floor-then-test rather than a Clamp because that is the bug this replaces:
        // Mathf.Clamp(1, 1, 0) throws ArgumentException, and a box under two screen pixels on its
        // shorter side makes the maximum 0 while the minimum stays 1. It is reachable by any
        // consumer outlining a rect whose size it did not choose -- a cell at the edge of a clip,
        // say -- and it faulted the whole canvas of the mod that found it.
        var room = Mathf.Floor(Math.Min(r.Size.X, r.Size.Y) / 2f);
        if (room < 1f)
        {
            RenderingServer.CanvasItemAddRect(Target, r, color);
            return;
        }

        var t = Mathf.Clamp(Mathf.Round(thickness), 1f, room);

        // Four rects rather than a polyline: a stroked line straddles its path, so half of it
        // would land on the neighbouring pixel. These sit wholly inside the rectangle.
        RenderingServer.CanvasItemAddRect(Target, new Rect2(r.Position.X, r.Position.Y, r.Size.X, t), color);
        RenderingServer.CanvasItemAddRect(Target, new Rect2(r.Position.X, r.End.Y - t, r.Size.X, t), color);
        RenderingServer.CanvasItemAddRect(Target, new Rect2(r.Position.X, r.Position.Y + t, t, r.Size.Y - 2 * t), color);
        RenderingServer.CanvasItemAddRect(Target, new Rect2(r.End.X - t, r.Position.Y + t, t, r.Size.Y - 2 * t), color);
    }

    /// <summary>Outlines a pixel, for this frame only.</summary>
    public void DrawOutline(Vector2I tile, Color color, float thickness = 1f) =>
        DrawOutline(ViewGeometry.ScreenRectOf(tile), color, thickness);

    /// <summary>Draws one border around a whole block of pixels, for this frame only.</summary>
    public void DrawOutline(RectInt tiles, Color color, float thickness = 1f) =>
        DrawOutline(ViewGeometry.ScreenRectOf(tiles), color, thickness);

    /// <summary>
    /// A triangle on one edge of a rectangle, pointing out of it, for this frame only.
    ///
    /// <para>It sits wholly inside the rectangle, so two neighbours pointing at each other
    /// produce two distinct marks rather than one smear across the boundary. Its size is a third
    /// of the pixel and at least two screen pixels, so it stays visible zoomed out, where a strictly
    /// proportional mark would vanish.</para>
    /// </summary>
    public void DrawArrow(Rect2 screen, Aim aim, Color color, Color? surround = null)
    {
        if (!_item.IsValid || aim == Aim.None)
            return;

        var r = Snap(screen);
        var reach = Mathf.Max(2f, Mathf.Round(Mathf.Min(r.Size.X, r.Size.Y) / 3f));

        // Below four screen pixels the border would be most of the arrow, so it is dropped rather
        // than drawn.
        if (surround is not { } edge || reach < 4f)
        {
            Triangle(r, aim, apexInset: 0f, reach, color);
            return;
        }

        // The border is a second triangle at the full reach, with the coloured one set one screen
        // pixel in from the edge and two shorter.
        //
        // Simply drawing a larger triangle behind does NOT work: both apexes would sit on the
        // rectangle's edge and coincide there, so the border would show along the base and nowhere
        // else. Insetting the apex is what turns it into a border on all three sides.
        Triangle(r, aim, apexInset: 0f, reach, edge);
        Triangle(r, aim, apexInset: 1f, reach - 2f, color);
    }

    /// <summary>
    /// One triangle, its apex <paramref name="apexInset"/> screen pixels in from an edge of
    /// <paramref name="r"/> and its base a further <paramref name="reach"/> in, as wide again
    /// either side of the centre line.
    ///
    /// <para>Wholly inside the rectangle, so two neighbours pointing at each other produce two
    /// distinct marks rather than one smear across the boundary.</para>
    /// </summary>
    private void Triangle(Rect2 r, Aim aim, float apexInset, float reach, Color color)
    {
        var midX = Mathf.Round(r.Position.X + r.Size.X / 2f);
        var midY = Mathf.Round(r.Position.Y + r.Size.Y / 2f);
        var near = apexInset;
        var far = apexInset + reach;

        var points = aim switch
        {
            Aim.Up => new[]
            {
                new Vector2(midX, r.Position.Y + near),
                new Vector2(midX - reach, r.Position.Y + far),
                new Vector2(midX + reach, r.Position.Y + far),
            },
            Aim.Down => new[]
            {
                new Vector2(midX, r.End.Y - near),
                new Vector2(midX + reach, r.End.Y - far),
                new Vector2(midX - reach, r.End.Y - far),
            },
            Aim.Left => new[]
            {
                new Vector2(r.Position.X + near, midY),
                new Vector2(r.Position.X + far, midY + reach),
                new Vector2(r.Position.X + far, midY - reach),
            },
            _ => new[]
            {
                new Vector2(r.End.X - near, midY),
                new Vector2(r.End.X - far, midY - reach),
                new Vector2(r.End.X - far, midY + reach),
            },
        };

        RenderingServer.CanvasItemAddPolygon(Target, points, new[] { color, color, color });
    }

    /// <summary>
    /// Draws one of the game's own shapes into a screen rectangle, for this frame only. See
    /// <see cref="Icon(Vector2I, IconArt?, Color)"/> for how it is sized.
    /// </summary>
    public void DrawIcon(Rect2 screen, IconArt? art, Color tint)
    {
        if (!_item.IsValid || art is not { } shape)
            return;

        var r = Snap(screen);
        var source = shape.Region.Size;
        if (source.X <= 0f || source.Y <= 0f)
            return;

        var room = Mathf.Min(r.Size.X, r.Size.Y) * IconFill;
        var scale = Mathf.Floor(Mathf.Min(room / source.X, room / source.Y));
        if (scale < 1f)
            return;

        var size = source * scale;
        var dest = new Rect2(
            Mathf.Round(r.GetCenter().X - size.X / 2f),
            Mathf.Round(r.GetCenter().Y - size.Y / 2f),
            size.X, size.Y);

        RenderingServer.CanvasItemAddTextureRectRegion(
            Target, dest, shape.Texture.GetRid(), shape.Region, tint);
    }

    /// <summary>How much of a pixel a shape fills, leaving the rest as margin.</summary>
    private const float IconFill = 0.78f;

    /// <summary>Draws one of the game's own shapes on a pixel, for this frame only.</summary>
    public void DrawIcon(Vector2I tile, IconArt? art, Color tint) =>
        DrawIcon(ViewGeometry.ScreenRectOf(tile), art, tint);

    /// <summary>Draws one of the game's own shapes across a block of pixels, for this frame only.</summary>
    public void DrawIcon(RectInt tiles, IconArt? art, Color tint) =>
        DrawIcon(ViewGeometry.ScreenRectOf(tiles), art, tint);

    /// <summary>Points out of one edge of a pixel, for this frame only.</summary>
    public void DrawArrow(Vector2I tile, Aim aim, Color color, Color? surround = null) =>
        DrawArrow(ViewGeometry.ScreenRectOf(tile), aim, color, surround);

    /// <summary>Points out of one edge of a block of pixels, for this frame only.</summary>
    public void DrawArrow(RectInt tiles, Aim aim, Color color, Color? surround = null) =>
        DrawArrow(ViewGeometry.ScreenRectOf(tiles), aim, color, surround);

    /// <summary>
    /// Writes text against a screen rectangle, for this frame only. See
    /// <see cref="Label(Vector2I, string, Color, TextSize, LabelPlacement, int, Color?)"/> for
    /// what the arguments mean.
    /// </summary>
    public void DrawLabel(Rect2 screen, string text, Color color,
                          TextSize size = TextSize.Auto,
                          LabelPlacement placement = LabelPlacement.Center,
                          int scale = 1,
                          Color? plate = null,
                          Color? outline = null)
    {
        if (!_item.IsValid || string.IsNullOrEmpty(text))
            return;

        var pixel = Snap(screen);
        PixelFont font;
        if (size == TextSize.Fit)
        {
            // Fit decides the scale as well. The fit itself is judged at scale 1, before the
            // player's multiplier: textScale means "draw my labels bigger", so counting it here
            // would answer a request for bigger text by refusing to draw it.
            (font, scale) = PixelFont.FitToBox(text, pixel.Size);
            if (scale < 1)
                return;     // it does not fit, and drawing it anyway is what Fit exists to avoid
            scale *= Math.Max(1, Settings.TextScale);
        }
        else
        {
            scale = EffectiveScale(scale);
            font = Resolve(size, text, pixel.Size, scale);
        }

        DrawText(pixel, text, font, scale, color, placement, plate, outline);
    }

    /// <summary>
    /// Draws a label that <see cref="LabelLayout"/> has already fitted, for this frame only.
    ///
    /// <para>There is no retained version on purpose. A fitted label is an answer about how big
    /// the pixel was on the frame it was computed for, and a retained mark is redrawn at every
    /// zoom after that -- it would be right once and quietly wrong from then on.</para>
    /// </summary>
    public void DrawLabel(Rect2 screen, in FittedText fitted, Color color,
                          LabelPlacement placement = LabelPlacement.Center,
                          Color? plate = null,
                          Color? outline = null)
    {
        if (!_item.IsValid || fitted.IsEmpty)
            return;
        DrawText(Snap(screen), fitted.Text, fitted.Font, fitted.Scale, color, placement, plate, outline);
    }

    /// <summary>Draws a fitted label on a pixel, for this frame only.</summary>
    public void DrawLabel(Vector2I tile, in FittedText fitted, Color color,
                          LabelPlacement placement = LabelPlacement.Center,
                          Color? plate = null,
                          Color? outline = null) =>
        DrawLabel(ViewGeometry.ScreenRectOf(tile), fitted, color, placement, plate, outline);

    /// <summary>Draws a fitted label across a block of pixels, for this frame only.</summary>
    public void DrawLabel(RectInt tiles, in FittedText fitted, Color color,
                          LabelPlacement placement = LabelPlacement.Center,
                          Color? plate = null,
                          Color? outline = null) =>
        DrawLabel(ViewGeometry.ScreenRectOf(tiles), fitted, color, placement, plate, outline);

    /// <summary>The drawing itself, once the text, the font and the magnification are all decided.</summary>
    private void DrawText(Rect2 pixel, string text, PixelFont font, int scale, Color color,
                          LabelPlacement placement, Color? plate, Color? outline)
    {
        var measured = (Vector2)font.Measure(text) * scale;

        // One font pixel of clearance, so a label is never flush against the edge it is placed
        // from and stays distinguishable from an outline on the same pixel.
        var gap = scale;

        var origin = placement switch
        {
            LabelPlacement.TopLeft => new Vector2(pixel.Position.X + gap, pixel.Position.Y + gap),
            LabelPlacement.Above   => new Vector2(pixel.GetCenter().X - measured.X / 2f,
                                                  pixel.Position.Y - measured.Y - gap),
            LabelPlacement.Below   => new Vector2(pixel.GetCenter().X - measured.X / 2f,
                                                  pixel.End.Y + gap),
            _                      => pixel.GetCenter() - measured / 2f,
        };

        // Whole pixels, so the nearest-neighbor filter has no fractional offset to resolve and a
        // glyph's blocks all come out the same size.
        origin = new Vector2(Mathf.Round(origin.X), Mathf.Round(origin.Y));

        if (plate is { } behind)
            RenderingServer.CanvasItemAddRect(
                Target,
                new Rect2(origin.X - gap, origin.Y - gap, measured.X + 2 * gap, measured.Y + 2 * gap),
                behind);

        var lines = text.Split('\n');

        // Each line placed on its own, so a stacked label sits over the middle of its longest
        // line rather than all of them starting where the widest one does. Flush left only where
        // the placement itself is a corner.
        var origins = new Vector2[lines.Length];
        for (var line = 0; line < lines.Length; line++)
        {
            var width = LineWidth(font, lines[line], scale);
            var x = placement == LabelPlacement.TopLeft
                ? origin.X
                : Mathf.Round(origin.X + (measured.X - width) / 2f);
            origins[line] = new Vector2(x, origin.Y + line * font.LineHeight * scale);
        }

        // The surround, if asked for: the WHOLE text drawn four times, offset a pixel each way,
        // and only then the text itself on top.
        //
        // Whole text per pass rather than five passes per glyph, and that is correctness rather
        // than tidiness. Glyphs are spaced by the font's own advance, which at scale 1 leaves a
        // single screen pixel between them -- exactly the offset the surround uses. Interleave
        // the passes and the surround of one character is drawn after the body of the character
        // before it, painting a dark bite out of its right-hand edge. Every surround first, every
        // body second, and no character can eat its neighbour. The cost is identical either way:
        // the command list takes one textured rect per glyph per pass regardless of the order
        // they are added in.
        if (outline is { } edge)
        {
            var step = Mathf.Max(1f, Mathf.Floor(scale / 2f));
            foreach (var offset in new[]
                     {
                         new Vector2(-step, 0f), new Vector2(step, 0f),
                         new Vector2(0f, -step), new Vector2(0f, step),
                     })
                DrawGlyphs(font, lines, origins, offset, scale, edge);
        }

        DrawGlyphs(font, lines, origins, Vector2.Zero, scale, color);
    }

    /// <summary>The lit width of one line, which is the advance per character less the trailing spacing.</summary>
    private static float LineWidth(PixelFont font, string line, int scale) =>
        line.Length == 0 ? 0f : (font.Advance * line.Length - font.HorizontalSpacing) * scale;

    /// <summary>
    /// One pass over the whole text: every glyph of every line, in one colour, shifted by
    /// <paramref name="offset"/>.
    /// </summary>
    private void DrawGlyphs(PixelFont font, string[] lines, Vector2[] origins, Vector2 offset,
                            int scale, Color color)
    {
        var atlas = font.Atlas.GetRid();
        for (var line = 0; line < lines.Length; line++)
        {
            var at = origins[line] + offset;
            for (var i = 0; i < lines[line].Length; i++)
            {
                var c = lines[line][i];
                if (c == ' ')
                    continue;
                // DrawnHeight, not GlyphHeight: the atlas cell carries the descender rows below
                // the box, and a dest rect sized to the box alone would squash the whole picture
                // into it rather than clipping the tail -- a texture rect scales its region to fit.
                // Every glyph is drawn to the same rect either way, so the baseline does not move.
                var dest = new Rect2(
                    at.X + i * font.Advance * scale,
                    at.Y,
                    font.GlyphWidth * scale,
                    font.DrawnHeight * scale);
                RenderingServer.CanvasItemAddTextureRectRegion(Target, dest, atlas, font.Region(c), color);
            }
        }
    }

    /// <summary>Writes text against a block of pixels, for this frame only.</summary>
    public void DrawLabel(RectInt tiles, string text, Color color,
                          TextSize size = TextSize.Auto,
                          LabelPlacement placement = LabelPlacement.Center,
                          int scale = 1,
                          Color? plate = null,
                          Color? outline = null) =>
        DrawLabel(ViewGeometry.ScreenRectOf(tiles), text, color, size, placement, scale, plate, outline);

    /// <summary>Writes text on a pixel, for this frame only.</summary>
    public void DrawLabel(Vector2I tile, string text, Color color,
                          TextSize size = TextSize.Auto,
                          LabelPlacement placement = LabelPlacement.Center,
                          int scale = 1,
                          Color? plate = null,
                          Color? outline = null) =>
        DrawLabel(ViewGeometry.ScreenRectOf(tile), text, color, size, placement, scale, plate, outline);

    // ------------------------------------------------------------------- measuring

    /// <summary>
    /// How much screen space a label would take, so a caller can place one itself rather than
    /// guess. Honors embedded newlines and the player's <c>textScale</c>, and counts only the
    /// glyphs: the spacing between characters is not added after the last one.
    ///
    /// <para><see cref="TextSize.Auto"/> has no answer here, because what fits depends on the
    /// pixel; ask <see cref="FontFor"/> with the pixel you mean, or name a size.</para>
    /// </summary>
    public static Vector2I MeasureLabel(string text, TextSize size = TextSize.Small, int scale = 1) =>
        Font(size, "MeasureLabel").Measure(text) * EffectiveScale(scale);

    /// <summary>
    /// Which font a label would be drawn in on a given pixel: the resolution of
    /// <see cref="TextSize.Auto"/>, exposed so a caller can measure or align against the same
    /// answer the drawing will use.
    /// </summary>
    public static PixelFont FontFor(Vector2I tile, string text, TextSize size = TextSize.Auto,
                                    int scale = 1) =>
        Resolve(size, text, ViewGeometry.ScreenRectOf(tile).Size, EffectiveScale(scale));

    /// <summary>
    /// The font and scale <see cref="TextSize.Fit"/> would draw with on a given pixel, so a caller
    /// can ask before committing to a label. A scale of 0 means it would not be drawn at all,
    /// which is a caller's cue to say something shorter or to say nothing.
    /// </summary>
    public static (PixelFont Font, int Scale) FitFor(Vector2I tile, string text) =>
        PixelFont.FitToBox(text, ViewGeometry.ScreenRectOf(tile).Size);

    private static PixelFont Resolve(TextSize size, string text, Vector2 pixel, int scale) =>
        size switch
        {
            TextSize.Auto => PixelFont.LargestFitting(text, pixel, scale),
            TextSize.Fit => PixelFont.FitToBox(text, pixel).Font,
            _ => Font(size, "Label"),
        };

    private static PixelFont Font(TextSize size, string what) => size switch
    {
        TextSize.Small  => PixelFont.Small,
        TextSize.Medium => PixelFont.Medium,
        TextSize.Large  => PixelFont.Large,
        TextSize.Auto or TextSize.Fit => throw new ArgumentException(
            $"{what} needs a named TextSize; {size} means \"as large as fits this pixel\", which " +
            "has no answer without a pixel. Use Canvas.FontFor or Canvas.FitFor with the pixel " +
            "you mean."),
        _ => throw new ArgumentException($"unknown TextSize {size}"),
    };

    /// <summary>
    /// The caller's scale times the player's <c>textScale</c>, and never below 1. Applied
    /// everywhere a scale is used, drawing and measuring alike, so the two agree and so a player
    /// who turns the text up gets a smaller font chosen rather than an overflowing one.
    /// </summary>
    private static int EffectiveScale(int scale) =>
        Math.Max(1, RequireScale(scale) * Math.Max(1, Settings.TextScale));

    private static int RequireScale(int scale) =>
        scale >= 1 ? scale : throw new ArgumentException($"a text scale must be at least 1; got {scale}");

    /// <summary>
    /// Rounds a rectangle to whole screen pixels. At a fractional zoom a pixel's edges land
    /// between pixels, and an unsnapped fill bleeds a translucent line onto its neighbours,
    /// which reads as a mark on a pixel nobody marked.
    /// </summary>
    private static Rect2 Snap(Rect2 r)
    {
        var min = new Vector2(Mathf.Round(r.Position.X), Mathf.Round(r.Position.Y));
        var max = new Vector2(Mathf.Round(r.End.X), Mathf.Round(r.End.Y));
        var size = max - min;
        return new Rect2(min, new Vector2(Mathf.Max(1f, size.X), Mathf.Max(1f, size.Y)));
    }
}
