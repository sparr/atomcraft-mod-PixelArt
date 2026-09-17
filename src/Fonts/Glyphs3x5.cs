namespace PixelArt;

// One face of the bitmap font. Its own file because a face is 96 pictures and nothing
// else, and because a picture is only legible if it is allowed to be one.
//
// Every glyph is a raw string literal drawn as it appears: '#' is lit, '.' is not. The
// atlas builder splits on whitespace, so the rows may be laid out however reads best and
// the line ending does not matter. It is built from these at first use and never again.
//
// Each picture is 3 wide and 6 tall: the 3x5 box, then 1 descender row drawn
// below it into the 1 row of vertical spacing. Only g, j, p, q, y,
// the tail of Q, and a few marks ever reach into them.
//
// The order is load bearing: printable ASCII from space to tilde, in order, then one row
// per character in PixelFont.Drawn. PixelFont checks the count at build time, and every
// row is checked for width, so a miscounted table is a named exception rather than an
// atlas in which every character after the mistake draws a sliver of its neighbour.

public sealed partial class PixelFont
{
    /// <summary>
    /// The 3x5 face, on a 4x6 grid.
    ///
    /// The only size that fits inside a world pixel when zoomed out, and the one the other two fall
    /// back to. Digits and capitals are what it is good at, and a little punctuation is approximate:
    /// three columns is not many. Its lowercase g, j, p, q and y now drop a row below the baseline
    /// rather than being folded into the body, which costs the whole of its one row of vertical
    /// spacing -- at this size a tail touches the caps on the line below, and that reads better than
    /// a q that looks like a g. Its ellipsis is two dots, because three do not fit in three columns.
    /// </summary>
    private static readonly string[] Glyphs3x5 =
    {
        // (space)
        """
        ...
        ...
        ...
        ...
        ...
        ...
        """,
        // !
        """
        .#.
        .#.
        .#.
        ...
        .#.
        ...
        """,
        // "
        """
        #.#
        #.#
        ...
        ...
        ...
        ...
        """,
        // #
        """
        #.#
        ###
        #.#
        ###
        #.#
        ...
        """,
        // $
        """
        .##
        ##.
        .#.
        .##
        ##.
        ...
        """,
        // %
        """
        #.#
        ..#
        .#.
        #..
        #.#
        ...
        """,
        // &
        """
        .#.
        #.#
        .#.
        #.#
        .##
        ...
        """,
        // '
        """
        .#.
        .#.
        ...
        ...
        ...
        ...
        """,
        // (
        """
        ..#
        .#.
        .#.
        .#.
        ..#
        ...
        """,
        // )
        """
        #..
        .#.
        .#.
        .#.
        #..
        ...
        """,
        // *
        """
        ...
        #.#
        .#.
        #.#
        ...
        ...
        """,
        // +
        """
        ...
        .#.
        ###
        .#.
        ...
        ...
        """,
        // ,
        """
        ...
        ...
        ...
        ...
        .#.
        #..
        """,
        // -
        """
        ...
        ...
        ###
        ...
        ...
        ...
        """,
        // .
        """
        ...
        ...
        ...
        ...
        .#.
        ...
        """,
        // /
        """
        ..#
        ..#
        .#.
        #..
        #..
        ...
        """,
        // 0
        """
        ###
        #.#
        #.#
        #.#
        ###
        ...
        """,
        // 1
        """
        .#.
        ##.
        .#.
        .#.
        ###
        ...
        """,
        // 2
        """
        ###
        ..#
        ###
        #..
        ###
        ...
        """,
        // 3
        """
        ###
        ..#
        ###
        ..#
        ###
        ...
        """,
        // 4
        """
        #.#
        #.#
        ###
        ..#
        ..#
        ...
        """,
        // 5
        """
        ###
        #..
        ###
        ..#
        ###
        ...
        """,
        // 6
        """
        ###
        #..
        ###
        #.#
        ###
        ...
        """,
        // 7
        """
        ###
        ..#
        ..#
        ..#
        ..#
        ...
        """,
        // 8
        """
        ###
        #.#
        ###
        #.#
        ###
        ...
        """,
        // 9
        """
        ###
        #.#
        ###
        ..#
        ###
        ...
        """,
        // :
        """
        ...
        .#.
        ...
        .#.
        ...
        ...
        """,
        // ;
        """
        ...
        .#.
        ...
        ...
        .#.
        #..
        """,
        // <
        """
        ..#
        .#.
        #..
        .#.
        ..#
        ...
        """,
        // =
        """
        ...
        ###
        ...
        ###
        ...
        ...
        """,
        // >
        """
        #..
        .#.
        ..#
        .#.
        #..
        ...
        """,
        // ?
        """
        ##.
        ..#
        .#.
        ...
        .#.
        ...
        """,
        // @
        """
        .#.
        ###
        ###
        #..
        .##
        ...
        """,
        // A
        """
        .#.
        #.#
        ###
        #.#
        #.#
        ...
        """,
        // B
        """
        ##.
        #.#
        ##.
        #.#
        ##.
        ...
        """,
        // C
        """
        .##
        #..
        #..
        #..
        .##
        ...
        """,
        // D
        """
        ##.
        #.#
        #.#
        #.#
        ##.
        ...
        """,
        // E
        """
        ###
        #..
        ##.
        #..
        ###
        ...
        """,
        // F
        """
        ###
        #..
        ##.
        #..
        #..
        ...
        """,
        // G
        """
        .##
        #..
        #.#
        #.#
        .##
        ...
        """,
        // H
        """
        #.#
        #.#
        ###
        #.#
        #.#
        ...
        """,
        // I
        """
        ###
        .#.
        .#.
        .#.
        ###
        ...
        """,
        // J
        """
        ..#
        ..#
        ..#
        #.#
        .#.
        ...
        """,
        // K
        """
        #.#
        #.#
        ##.
        #.#
        #.#
        ...
        """,
        // L
        """
        #..
        #..
        #..
        #..
        ###
        ...
        """,
        // M
        """
        #.#
        ###
        ###
        #.#
        #.#
        ...
        """,
        // N
        """
        ##.
        #.#
        #.#
        #.#
        #.#
        ...
        """,
        // O
        """
        .#.
        #.#
        #.#
        #.#
        .#.
        ...
        """,
        // P
        """
        ##.
        #.#
        ##.
        #..
        #..
        ...
        """,
        // Q
        """
        .#.
        #.#
        #.#
        ###
        .##
        ...
        """,
        // R
        """
        ##.
        #.#
        ##.
        #.#
        #.#
        ...
        """,
        // S
        """
        .##
        #..
        .#.
        ..#
        ##.
        ...
        """,
        // T
        """
        ###
        .#.
        .#.
        .#.
        .#.
        ...
        """,
        // U
        """
        #.#
        #.#
        #.#
        #.#
        ###
        ...
        """,
        // V
        """
        #.#
        #.#
        #.#
        #.#
        .#.
        ...
        """,
        // W
        """
        #.#
        #.#
        ###
        ###
        #.#
        ...
        """,
        // X
        """
        #.#
        #.#
        .#.
        #.#
        #.#
        ...
        """,
        // Y
        """
        #.#
        #.#
        .#.
        .#.
        .#.
        ...
        """,
        // Z
        """
        ###
        ..#
        .#.
        #..
        ###
        ...
        """,
        // [
        """
        .##
        .#.
        .#.
        .#.
        .##
        ...
        """,
        // \
        """
        #..
        #..
        .#.
        ..#
        ..#
        ...
        """,
        // ]
        """
        ##.
        .#.
        .#.
        .#.
        ##.
        ...
        """,
        // ^
        """
        .#.
        #.#
        ...
        ...
        ...
        ...
        """,
        // _
        """
        ...
        ...
        ...
        ...
        ###
        ...
        """,
        // `
        """
        #..
        .#.
        ...
        ...
        ...
        ...
        """,
        // a
        """
        ...
        ##.
        .##
        #.#
        .##
        ...
        """,
        // b
        """
        #..
        #..
        ##.
        #.#
        ##.
        ...
        """,
        // c
        """
        ...
        .##
        #..
        #..
        .##
        ...
        """,
        // d
        """
        ..#
        ..#
        .##
        #.#
        .##
        ...
        """,
        // e
        """
        ...
        .#.
        #.#
        ##.
        .##
        ...
        """,
        // f
        """
        .##
        .#.
        ###
        .#.
        .#.
        ...
        """,
        // g
        """
        ...
        .##
        #.#
        .##
        ..#
        ##.
        """,
        // h
        """
        #..
        #..
        ##.
        #.#
        #.#
        ...
        """,
        // i
        """
        .#.
        ...
        .#.
        .#.
        .#.
        ...
        """,
        // j
        """
        ..#
        ...
        ..#
        ..#
        ..#
        ##.
        """,
        // k
        """
        #..
        #..
        #.#
        ##.
        #.#
        ...
        """,
        // l
        """
        ##.
        .#.
        .#.
        .#.
        .##
        ...
        """,
        // m
        """
        ...
        ###
        ###
        #.#
        #.#
        ...
        """,
        // n
        """
        ...
        ##.
        #.#
        #.#
        #.#
        ...
        """,
        // o
        """
        ...
        .#.
        #.#
        #.#
        .#.
        ...
        """,
        // p
        """
        ...
        ##.
        #.#
        ##.
        #..
        #..
        """,
        // q
        """
        ...
        .##
        #.#
        .##
        ..#
        ..#
        """,
        // r
        """
        ...
        .##
        #..
        #..
        #..
        ...
        """,
        // s
        """
        ...
        .##
        #..
        ..#
        ##.
        ...
        """,
        // t
        """
        .#.
        ###
        .#.
        .#.
        .##
        ...
        """,
        // u
        """
        ...
        #.#
        #.#
        #.#
        .##
        ...
        """,
        // v
        """
        ...
        #.#
        #.#
        #.#
        .#.
        ...
        """,
        // w
        """
        ...
        #.#
        #.#
        ###
        ###
        ...
        """,
        // x
        """
        ...
        #.#
        .#.
        .#.
        #.#
        ...
        """,
        // y
        """
        ...
        #.#
        #.#
        .##
        ..#
        ##.
        """,
        // z
        """
        ...
        ###
        ..#
        #..
        ###
        ...
        """,
        // {
        """
        ..#
        .#.
        ##.
        .#.
        ..#
        ...
        """,
        // |
        """
        .#.
        .#.
        .#.
        .#.
        .#.
        ...
        """,
        // }
        """
        #..
        .#.
        .##
        .#.
        #..
        ...
        """,
        // ~
        """
        ...
        ..#
        ###
        #..
        ...
        ...
        """,
        // ellipsis
        """
        ...
        ...
        ...
        ...
        #.#
        ...
        """,
    };
}
