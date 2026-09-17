using Godot;

namespace PixelArt;

/// <summary>
/// A label that has been fitted to a pixel: what text to draw, in which font, at what
/// magnification.
///
/// <para>Named for what it is rather than called <c>Label</c>, because Godot has a
/// <c>Godot.Label</c> node and a mod with both namespaces imported would have to say which it
/// meant every time.</para>
///
/// <para><b>Per frame, not retained.</b> The answer depends on how big the pixel is on screen,
/// which changes with the zoom, so a fitted label is drawn with <c>Canvas.DrawLabel</c> on the
/// frame it was computed for. There is deliberately no retained overload: one would be right
/// until the player touched the zoom and wrong afterwards, silently.</para>
/// </summary>
public readonly struct FittedText
{
    internal FittedText(string text, PixelFont font, int scale, bool overflows)
    {
        Text = text;
        Font = font;
        Scale = scale;
        Overflows = overflows;
    }

    /// <summary>The text as it should be drawn, newlines and all. May be shorter than what was asked for.</summary>
    public string Text { get; }

    /// <summary>Which of the three sizes it is drawn in.</summary>
    public PixelFont Font { get; }

    /// <summary>How many screen pixels each font pixel becomes. Always at least 1.</summary>
    public int Scale { get; }

    /// <summary>
    /// Whether this spills past the pixel it was laid out for. True only when nothing fit, which
    /// at the game's own maximum zoom is the ordinary case.
    /// </summary>
    public bool Overflows { get; }

    /// <summary>Whether there is anything to draw.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Text);

    /// <summary>How much screen space the drawn label takes.</summary>
    public Vector2 Size => (Vector2)Font.Measure(Text) * Scale;

    /// <summary>How many lines it was broken into.</summary>
    public int Lines => Text.Count(c => c == '\n') + 1;

    public override string ToString() =>
        $"'{Text.Replace("\n", "\\n")}' {Font.Name} x{Scale}{(Overflows ? " overflowing" : "")}";
}

/// <summary>
/// How a label is fitted to a pixel: which of the three bitmap sizes, how many whole screen
/// pixels each font pixel becomes, whether to stack a leading mark above the rest, whether to
/// wrap across lines, and how much of it to show at all.
///
/// <para><b>Everything is decided against the pixel's size on the frame it is drawn</b>, which is
/// the whole point. A world pixel is 8 world units, so it is 12 screen pixels across at the
/// game's own maximum zoom and up to 96 with a zoom mod installed. At 12 there is room for about
/// three characters of the smallest font; at 96 there is room for a two-line label in the largest
/// font at three times size. A layout fixed at either end is wrong at the other.</para>
///
/// <para><b>What this is for, and when to use the simpler thing instead.</b>
/// <see cref="TextSize.Fit"/> answers "given exactly this text, how large can I draw it, and
/// should I draw it at all". This answers "given this much to say, what should I actually draw" --
/// it may wrap it, cut it short, or stack a symbol above a name. Reach for Fit when the text is
/// short and fixed, and for this when it is a name that might be "Praseodymium Chloride".</para>
///
/// <para><b>Why stacking a mark is worth a mechanism.</b> A filter's label is a symbol and a name
/// -- <c>=</c> and <c>Water</c>. On one line that is six characters competing for the pixel's
/// width, and width is the scarce dimension: a square pixel has as much height as width, and a
/// line of text uses almost none of it. Putting the mark on its own line spends the height that
/// was going to waste and gives the name the pixel's full width, which is usually worth a whole
/// font size.</para>
///
/// <para>Touches no game type, so the whole of it is assertable headlessly. Adapted from the
/// AltAnnotations mod (MIT, same author), which is where it was worked out.</para>
/// </summary>
public static class LabelLayout
{
    /// <summary>
    /// The largest whole magnification a label is grown to on its own.
    ///
    /// <para>A one-character label -- a gate symbol, an oscillator period -- would otherwise grow
    /// until it filled the pixel edge to edge, covering the material underneath and colliding with
    /// a direction arrow on the same pixel. Four is enough to fill a 96-screen-pixel cell
    /// comfortably and leaves the pixel visible around the text.</para>
    /// </summary>
    public const int MaxAutoScale = 4;

    /// <summary>
    /// How many lines a label is wrapped across by default.
    ///
    /// <para>Three is where a label stops being a label and starts being a paragraph on a pixel.
    /// It is also as many as a square pixel has room for at a readable size: at 96 screen pixels,
    /// three lines of the 5x7 font at twice size is 54 of the 96.</para>
    ///
    /// <para>A default rather than a ceiling, because it is a judgement about labels in general
    /// and some callers know better about theirs. Material names run past forty characters, and
    /// naming one of those whole is worth more than the tidiness three lines buys -- so
    /// <see cref="Choose(string, string, Vector2, int?, int, int)"/> takes a limit of its
    /// own.</para>
    /// </summary>
    public const int MaxLines = 3;

    /// <summary>
    /// Clearance kept inside the pixel, as a fraction of it, so a label is never flush against the
    /// edge and stays distinguishable from an arrow on the same pixel.
    /// </summary>
    private const float MarginFraction = 0.06f;

    /// <summary>Clearance never grows past this, so a large pixel is not mostly margin.</summary>
    private const float MaxMargin = 6f;

    /// <summary>...nor shrinks below this, so a tiny pixel still keeps its label off the edge.</summary>
    private const float MinMargin = 1f;

    /// <summary>
    /// How much of a pixel a label may actually occupy, once the clearance is taken off.
    ///
    /// <para>Public because it is the only way to ask, from outside, whether a given rendering
    /// would have fit -- which is what a test needs to check that the layout never spends a line
    /// on a wrap or a stack that bought nothing.</para>
    /// </summary>
    public static Vector2 RoomIn(Vector2 box)
    {
        var margin = Mathf.Clamp(box.X * MarginFraction, MinMargin, MaxMargin);
        return new Vector2(box.X - 2f * margin, box.Y - 2f * margin);
    }

    /// <summary>Lays out a mark and a body on the pixel at <paramref name="tile"/>, as it is on screen now.</summary>
    public static FittedText Choose(string mark, string body, Vector2I tile,
                                    int? scale = null, int maxBody = int.MaxValue,
                                    int maxLines = MaxLines) =>
        Choose(mark, body, ViewGeometry.ScreenRectOf(tile).Size, scale, maxBody, maxLines);

    /// <summary>
    /// Lays out a mark and a body in a box of screen pixels.
    ///
    /// <para>Candidates are tried from the most informative down, and the <b>first one that
    /// fits</b> wins: the whole body wrapped, then the whole body on one line, then progressively
    /// shorter cuts of it. Within one amount of body, the arrangement that yields the largest
    /// glyphs wins, and between two of equal size the narrower one does. So a pixel with room
    /// shows the whole name and one without shows as much of it as it can, and the reader never
    /// has to know which happened -- a cut always ends in <see cref="PixelFont.Ellipsis"/>.</para>
    ///
    /// <para><b>There is always an answer, and it is never empty.</b> When not even one character
    /// fits -- which at the game's own maximum zoom is the ordinary case, since a pixel is then 12
    /// screen pixels across and two characters of the smallest font are 11 wide before margins --
    /// the fullest candidate is returned with <see cref="FittedText.Overflows"/> set, and it
    /// spills past the pixel. That is deliberate: a label that does not quite fit is still the
    /// thing you wanted to read, and an empty pixel looks exactly like a pixel the mod had no
    /// opinion about.</para>
    ///
    /// <para><b>So a caller drawing many labels at once should check <c>Overflows</c> and skip.</b>
    /// One overflowing label is readable; a hundred of them on adjacent pixels is a grey smear.
    /// That decision is the caller's because both answers are right for somebody, which is also
    /// the difference from <see cref="TextSize.Fit"/> -- that one decides for you, and draws
    /// nothing.</para>
    /// </summary>
    /// <param name="mark">The leading symbol, or empty. Never cut, and never wrapped.</param>
    /// <param name="body">The rest of the label. Cut and wrapped as the pixel demands.</param>
    /// <param name="box">The pixel's size in screen pixels.</param>
    /// <param name="scale">
    /// The player's own magnification, applied on top of the chosen one. Defaults to
    /// <see cref="Settings.TextScale"/>.
    /// </param>
    /// <param name="maxBody">The most characters of <paramref name="body"/> ever shown.</param>
    /// <param name="maxLines">
    /// The most lines <paramref name="body"/> is wrapped across, defaulting to
    /// <see cref="MaxLines"/>. Raise it when naming the thing whole matters more than keeping the
    /// label compact: material names run past forty characters, and three lines cannot hold one.
    /// Clamped to at least 1, so a caller cannot ask for a label with no lines in it.
    /// </param>
    public static FittedText Choose(string mark, string body, Vector2 box,
                                    int? scale = null, int maxBody = int.MaxValue,
                                    int maxLines = MaxLines)
    {
        var userScale = Mathf.Max(1, scale ?? Settings.TextScale);
        maxLines = Mathf.Max(1, maxLines);
        mark ??= "";
        body ??= "";

        if (mark.Length == 0 && body.Length == 0)
            return new FittedText("", PixelFont.Small, userScale, overflows: false);

        var room = RoomIn(box);
        FittedText? fallback = null;

        // The last rung of the ladder -- drop the body and show the mark alone -- only exists when
        // there is a mark. Without one it would offer "draw nothing" as a candidate that trivially
        // fits, and every pixel too small for a single character would come back empty instead of
        // falling through to the overflow below.
        foreach (var shown in BodyLengths(body, maxBody, includeEmpty: mark.Length > 0))
        {
            FittedText? best = null;

            foreach (var text in Arrangements(mark, shown, maxLines))
            {
                var measured = Fit(text, room, userScale);

                // Remembered from the very first candidate tried: the whole label, on one line. If
                // nothing ever fits, that is the best thing to overflow with -- one line spilling
                // sideways reads better than two, and it shows the most.
                fallback ??= measured;

                if (measured.Overflows)
                    continue;
                if (best == null || Beats(measured, best.Value))
                    best = measured;
            }

            if (best != null)
                return best.Value;
        }

        return fallback ?? new FittedText(mark + body, PixelFont.Small, userScale, overflows: true);
    }

    /// <summary>
    /// Whether one arrangement is better than another, at equal amounts of body shown.
    ///
    /// <para><b>Bigger glyphs first, then narrower.</b> Size is the obvious half. Width is the half
    /// that is easy to miss and matters as much, because a mod that labels pixels usually labels
    /// whole banks of them on adjacent pixels: two labels that each fit their own pixel can still
    /// run into each other, and the pixel's height is the space going spare. On a 96-screen-pixel
    /// cell <c>=Water</c> fits on one line in the largest font and so does <c>=</c> over
    /// <c>Water</c> -- same size, and the stacked one is 57 screen pixels wide against 69.</para>
    ///
    /// <para>Strictly better on both counts, so an arrangement that ties on size and width keeps
    /// whichever came first, and <see cref="Arrangements"/> yields the plainest first.</para>
    /// </summary>
    private static bool Beats(in FittedText candidate, in FittedText best)
    {
        var size = candidate.Font.GlyphHeight * candidate.Scale;
        var bestSize = best.Font.GlyphHeight * best.Scale;
        if (size != bestSize)
            return size > bestSize;
        return candidate.Size.X < best.Size.X;
    }

    /// <summary>
    /// The ways a mark and a body can be arranged, plainest first, which is what wins when
    /// <see cref="Beats"/> finds nothing to choose between two of them.
    /// </summary>
    private static IEnumerable<string> Arrangements(string mark, string body, int maxLines)
    {
        if (body.Length == 0)
        {
            yield return mark;
            yield break;
        }

        if (mark.Length == 0)
        {
            foreach (var wrapped in Wrappings(body, maxLines))
                yield return wrapped;
            yield break;
        }

        yield return mark + body;

        foreach (var wrapped in Wrappings(body, maxLines))
            yield return mark + "\n" + wrapped;
    }

    /// <summary>
    /// The ways a body can be broken across lines, fewest lines first.
    ///
    /// <para>Only at spaces. A hard break inside a word would produce two fragments that each read
    /// as a word -- "Praseo" over "dymium" -- and a reader who glanced at one line would carry away
    /// something the pixel does not say. A long single word is cut instead, which at least ends in
    /// a mark that says it was cut.</para>
    /// </summary>
    private static IEnumerable<string> Wrappings(string body, int maxLines)
    {
        yield return body;

        var words = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
            yield break;

        for (var lines = 2; lines <= Math.Min(maxLines, words.Length); lines++)
            yield return Balance(words, lines);
    }

    /// <summary>
    /// Packs words into at most <paramref name="lines"/> lines with the longest line as short as
    /// possible, which is what makes a wrapped label a block rather than a staircase.
    ///
    /// <para>Found by trying every width and taking the smallest that still fits the words into the
    /// line budget. The inputs are a handful of words, so the cost does not matter and the result
    /// is exactly optimal rather than approximately so.</para>
    /// </summary>
    private static string Balance(string[] words, int lines)
    {
        var longest = words.Max(w => w.Length);
        var total = words.Sum(w => w.Length) + words.Length - 1;

        for (var width = longest; width <= total; width++)
            if (Pack(words, width) is { } packed && packed.Count <= lines)
                return string.Join("\n", packed);

        return string.Join("\n", words);
    }

    /// <summary>Greedily fills lines no wider than <paramref name="width"/>.</summary>
    private static List<string> Pack(string[] words, int width)
    {
        var packed = new List<string>();
        var line = "";
        foreach (var word in words)
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (candidate.Length <= width)
            {
                line = candidate;
                continue;
            }
            if (line.Length > 0)
                packed.Add(line);
            line = word;
        }
        if (line.Length > 0)
            packed.Add(line);
        return packed;
    }

    /// <summary>
    /// The body at decreasing lengths: whole, then cut shorter and shorter, then nothing.
    ///
    /// <para>Every cut ends in <see cref="PixelFont.Ellipsis"/>, always. See that field for why an
    /// unmarked cut is worse than useless when the things being named share prefixes.</para>
    ///
    /// <para>The ladder steps down by two characters rather than one, because a one-character
    /// difference almost never changes which font fits and every step costs a pass over the
    /// arrangements.</para>
    /// </summary>
    internal static IEnumerable<string> BodyLengths(string body, int maxBody,
                                                    bool includeEmpty = true)
    {
        if (body.Length == 0)
        {
            yield return "";
            yield break;
        }

        var start = Math.Min(body.Length, Math.Max(1, maxBody));
        if (start == body.Length)
            yield return body;
        else
            yield return Cut(body, start);

        for (var length = start - 2; length >= 1; length -= 2)
            yield return Cut(body, length);

        // The mark on its own. A bare "=" still says "this is a filter, and it is set to
        // something", which is more than the unannotated pixel says.
        if (includeEmpty)
            yield return "";
    }

    /// <summary>The first <paramref name="length"/> characters, plus the cut mark.</summary>
    private static string Cut(string body, int length)
    {
        if (length >= body.Length)
            return body;
        // Trailing space trimmed before the mark, so "Carbon Dioxide" cut at 7 reads "Carbon…"
        // rather than "Carbon …".
        return body[..length].TrimEnd() + PixelFont.Ellipsis;
    }

    /// <summary>
    /// The largest font and whole magnification that draw <paramref name="text"/> inside
    /// <paramref name="room"/>, or the smallest font marked as overflowing.
    ///
    /// <para><b>Whether it fits is judged before the player's own magnification.</b> That is
    /// deliberate: <c>textScale</c> means "draw my labels bigger", and counting it here would make
    /// a player who asked for bigger labels get <i>shorter</i> ones instead. So the fit decides
    /// what to say and which font says it, and the player's multiplier is applied to the result --
    /// which may push it past the pixel, because that is what asking for bigger text means.</para>
    /// </summary>
    private static FittedText Fit(string text, Vector2 room, int userScale)
    {
        // Nothing to draw trivially fits, and says so: the alternative is reporting an empty
        // label as overflowing, which would make "show the mark alone" look impossible.
        if (text.Length == 0)
            return new FittedText(text, PixelFont.Small, userScale, overflows: false);

        var (font, scale) = PixelFont.FitToBox(text, room, MaxAutoScale);
        return scale < 1
            ? new FittedText(text, PixelFont.Small, userScale, overflows: true)
            : new FittedText(text, font, scale * userScale, overflows: false);
    }
}
