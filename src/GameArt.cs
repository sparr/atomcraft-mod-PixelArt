using Godot;

namespace PixelArt;

/// <summary>
/// A picture to draw on a world pixel: a texture the game already ships, and the part of it to
/// use.
/// </summary>
public readonly struct IconArt
{
    internal IconArt(Texture2D texture, Rect2 region)
    {
        Texture = texture;
        Region = region;
    }

    /// <summary>The texture, which belongs to the game. Do not free it.</summary>
    public Texture2D Texture { get; }

    /// <summary>The part of <see cref="Texture"/> that is the shape, in its own pixels.</summary>
    public Rect2 Region { get; }

    /// <summary>The shape's size in its own pixels.</summary>
    public Vector2 Size => Region.Size;
}

/// <summary>
/// Shapes borrowed from the game's own art, for drawing on a world pixel.
///
/// <para>Atomcraft already draws a square, a drop and a cloud wherever it has to say "solid",
/// "liquid" or "gas" -- the material guide's swatches -- and it has a little white person for
/// the spaceship inventory. Borrowing those rather than inventing shapes means a player who has
/// seen the guide already knows what a mark on a pixel means, and it costs nothing to ship: the
/// textures are in the game's own PCK. Found and verified by the AltAnnotations mod; this is the
/// shared copy so the next mod need not find them again.</para>
///
/// <para><b>Why only four, out of the 249 icons the game ships.</b> A shape drawn here is tinted
/// by a modulate, which multiplies: it works on a white silhouette with an alpha channel and
/// turns a full-colour icon into mud. These four are pure white masks, which was checked rather
/// than assumed -- the person's 26 lit screen pixels are every one of them (255,255,255,255).
/// Most of the game's icons are painted art and are not usable this way, so enumerating them all
/// would be a list of mostly-wrong answers. <see cref="Load"/> takes any path for a mod that has
/// checked one for itself, and <see cref="IsWhiteMask"/> is how to check.</para>
///
/// <para><b>Why the person is a region and the phases are not.</b> There is no player sprite of
/// its own. The avatar is composited at run time from layered 12x12 sheets -- a torso, limbs,
/// extremities, a head, a visor and the player's chosen hair -- so "the avatar" is not a file,
/// and what it looks like depends on cosmetics. The pictogram the game uses to <i>mean</i> a
/// player is the left six columns of <c>icon_player_and_spaceship</c>, with a spaceship beside
/// it, so that is what is drawn.</para>
///
/// <para><b>Loaded lazily and never fatally.</b> A missing texture costs the shape and nothing
/// else, so a game update that renames one leaves a mod working with one fewer flourish and says
/// so once. Lazy because resources are the engine's and a mod's <c>Initialize</c> runs before
/// <c>Game._Ready</c>; by the time a frame is drawn they have long been available.</para>
/// </summary>
public static class GameArt
{
    /// <summary>A filled square: the guide's swatch for a solid.</summary>
    public static IconArt? Solid => Get("res://Art/material_swatch_solid.png");

    /// <summary>A falling drop: the guide's swatch for a liquid.</summary>
    public static IconArt? Liquid => Get("res://Art/material_swatch_liquid.png");

    /// <summary>A cloud: the guide's swatch for a gas.</summary>
    public static IconArt? Gas => Get("res://Art/material_swatch_gas.png");

    /// <summary>
    /// A little person. Six columns by nine rows out of a fifteen-square icon -- head, body,
    /// outstretched arms, legs -- the remainder of that file being the spaceship.
    /// </summary>
    public static IconArt? Player =>
        Get("res://Art/icon_player_and_spaceship.png", new Rect2(0, 3, 6, 9));

    /// <summary>The shape for a material's state, or null for one this does not cover.</summary>
    public static IconArt? ForState(MaterialState state) => state switch
    {
        MaterialState.Solid => Solid,
        MaterialState.Liquid => Liquid,
        MaterialState.Gas => Gas,
        _ => null,
    };

    private static readonly Dictionary<string, IconArt?> Loaded = new();
    private static bool _warned;

    /// <summary>
    /// Any texture the game ships, by <c>res://</c> path, optionally cropped to a region.
    ///
    /// <para>Cached, including a null, so a missing texture is looked up once rather than on every
    /// pixel of every frame. Returns null rather than throwing for anything that is not there or
    /// will not load: a shape is a flourish, and a mod should keep working without it.</para>
    ///
    /// <para>Check a candidate with <see cref="IsWhiteMask"/> before shipping it. A full-colour
    /// icon drawn through a tint comes out muddy rather than wrong-looking enough to notice.</para>
    /// </summary>
    public static IconArt? Load(string path, Rect2? region = null)
    {
        var key = region is { } r ? $"{path}#{r}" : path;
        if (Loaded.TryGetValue(key, out var cached))
            return cached;

        IconArt? art = null;
        try
        {
            if (ResourceLoader.Exists(path) && GD.Load<Texture2D>(path) is { } texture)
            {
                var use = region ?? new Rect2(0, 0, texture.GetWidth(), texture.GetHeight());

                // A region naming pixels the texture does not have would draw garbage, and the art
                // being resized is exactly the kind of update nothing else notices.
                if (use.End.X <= texture.GetWidth() && use.End.Y <= texture.GetHeight()
                    && use.Size.X > 0 && use.Size.Y > 0)
                    art = new IconArt(texture, use);
                else
                    Log.Warn($"{path} is {texture.GetWidth()}x{texture.GetHeight()}, which does " +
                             $"not contain the region {use}; that shape is dropped");
            }
            else if (!_warned)
            {
                _warned = true;
                Log.Warn($"the game no longer has {path}, so some marks will be drawn without " +
                         "their shape. Nothing else is affected.");
            }
        }
        catch (Exception e)
        {
            Log.Warn($"could not load {path}: {e.Message}");
        }

        Loaded[key] = art;
        return art;
    }

    /// <summary>
    /// Whether every lit screen pixel of a shape is pure white, which is what makes it tintable.
    ///
    /// <para>Reads the texture back, so it is not something to call every frame -- it is for a
    /// test, or for a one-off check when picking a new shape out of the game's art.</para>
    /// </summary>
    public static bool IsWhiteMask(IconArt? art)
    {
        if (art is not { } shape)
            return false;

        var image = shape.Texture.GetImage();
        if (image == null)
            return false;

        for (var y = (int)shape.Region.Position.Y; y < (int)shape.Region.End.Y; y++)
        for (var x = (int)shape.Region.Position.X; x < (int)shape.Region.End.X; x++)
        {
            var c = image.GetPixel(x, y);
            if (c.A <= 0f)
                continue;
            if (c.R < 0.99f || c.G < 0.99f || c.B < 0.99f)
                return false;
        }
        return true;
    }

    private static IconArt? Get(string path, Rect2? region = null) => Load(path, region);

    /// <summary>Drops the cache, so a test can re-measure rather than inherit an earlier answer.</summary>
    public static void Forget()
    {
        Loaded.Clear();
        _warned = false;
    }
}
