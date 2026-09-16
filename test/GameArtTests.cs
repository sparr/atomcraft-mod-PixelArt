using Atomcraft.TestHarness;
using Godot;

namespace PixelArt.Test;

/// <summary>
/// The shapes borrowed from the game's own art are still there, and still the kind of art that
/// can be tinted.
///
/// <para><b>Both halves matter and they fail differently.</b> A renamed texture costs the shape
/// and says so in the log, which is survivable. A texture that stops being a white silhouette --
/// repainted, given an outline, recoloured for a new UI -- keeps loading and keeps drawing, and
/// comes out as mud in whatever colour a mod asked for. Nothing would report that but a person
/// looking at it.</para>
///
/// <para>These read textures, so they need the game's resources rather than a display: the file
/// is loaded and its pixels examined, and no frame is involved.</para>
/// </summary>
public static class GameArtTests
{
    /// <summary>The game still ships every shape this mod names.</summary>
    [GameTest]
    public static void TheBorrowedShapesAreStillThere()
    {
        GameArt.Forget();

        foreach (var (name, art) in Shapes())
            if (art == null)
                throw new AssertionException(
                    $"the game no longer has the art behind GameArt.{name}. Nothing breaks -- a " +
                    "missing shape is simply not drawn -- but the path in GameArt needs updating, " +
                    "or the shape needs retiring.");
    }

    /// <summary>
    /// Every borrowed shape is a pure white silhouette, so a draw tint colours it rather than
    /// muddying it.
    ///
    /// <para>This is the assumption the whole idea rests on, and it is the one that can rot
    /// silently. It was true when the shapes were picked; this is what notices when it stops
    /// being.</para>
    /// </summary>
    [GameTest]
    public static void TheBorrowedShapesAreWhiteMasks()
    {
        GameArt.Forget();

        foreach (var (name, art) in Shapes())
        {
            if (art == null)
                Harness.Inapplicable($"GameArt.{name} is missing, which the test above reports");

            if (!GameArt.IsWhiteMask(art))
                throw new AssertionException(
                    $"GameArt.{name} is no longer a pure white silhouette, so tinting it now " +
                    "multiplies one colour by another and produces mud. Either the game repainted " +
                    "it, in which case it should be retired, or it needs converting to a mask " +
                    "from its alpha channel at load time.");
        }
    }

    /// <summary>
    /// A shape has pixels lit, which is what distinguishes "the art is there" from "the art is
    /// there and is a blank square".
    ///
    /// <para>Worth stating separately because <see cref="GameArt.IsWhiteMask"/> passes an entirely
    /// transparent texture: every lit pixel of nothing is white.</para>
    /// </summary>
    [GameTest]
    public static void TheBorrowedShapesHaveSomethingInThem()
    {
        GameArt.Forget();

        foreach (var (name, art) in Shapes())
        {
            if (art == null)
                Harness.Inapplicable($"GameArt.{name} is missing, which the first test reports");

            var shape = art!.Value;
            var image = shape.Texture.GetImage();
            if (image == null)
                Harness.Inapplicable("textures cannot be read back on this renderer");

            var lit = 0;
            for (var y = (int)shape.Region.Position.Y; y < (int)shape.Region.End.Y; y++)
            for (var x = (int)shape.Region.Position.X; x < (int)shape.Region.End.X; x++)
                if (image!.GetPixel(x, y).A > 0f)
                    lit++;

            if (lit == 0)
                throw new AssertionException(
                    $"GameArt.{name} loads, but every pixel of the region it names is " +
                    "transparent. The art was probably moved inside its file rather than renamed.");

            ModLog().Event("game_art", new()
            {
                ["shape"] = name,
                ["size"] = $"{shape.Region.Size.X}x{shape.Region.Size.Y}",
                ["lit"] = lit,
            });
        }
    }

    /// <summary>
    /// An unknown path is a null rather than an exception, and the same path asked for twice is
    /// looked up once.
    ///
    /// <para>The caching is not an optimisation to take on trust: a shape is asked for per pixel
    /// per frame, so a miss that reached <c>ResourceLoader</c> every time would be a file system
    /// call tens of thousands of times a second.</para>
    /// </summary>
    [GameTest]
    public static void AnUnknownShapeIsNullAndIsNotLookedUpTwice()
    {
        GameArt.Forget();

        if (GameArt.Load("res://Art/no_such_texture_at_all.png") != null)
            throw new AssertionException("a path the game does not have returned a shape");

        // Loading the real one twice returns the same texture instance, which is what says the
        // second call did not go back to the loader.
        var first = GameArt.Solid;
        var second = GameArt.Solid;
        if (first is { } a && second is { } b && !ReferenceEquals(a.Texture, b.Texture))
            throw new AssertionException(
                "asking for the same shape twice loaded it twice; the cache is not working, and " +
                "this is asked for once per pixel per frame");
    }

    private static (string Name, IconArt? Art)[] Shapes() => new[]
    {
        ("Solid", GameArt.Solid),
        ("Liquid", GameArt.Liquid),
        ("Gas", GameArt.Gas),
        ("Player", GameArt.Player),
    };

    /// <summary>The test mod's own log, for the measurements above.</summary>
    private static ModLog ModLog() => Atomcraft.TestHarness.Log.For(ModEntry.ModId);
}
