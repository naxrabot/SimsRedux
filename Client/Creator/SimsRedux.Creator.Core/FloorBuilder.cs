namespace SimsRedux.Creator;

/// <summary>
/// Builds Sims 1 custom floors (.flr).
///
/// SimsRedux: what a .flr contains (inspected from frosti_chevronsimplecherry.flr, and how FreeSO's
/// WorldFloorProvider loads custom floors):
///   SPR2 #513 "HR00G00B00"  near zoom,   one frame 127x64
///   SPR2 #257 "HR00G00B00"  medium zoom, one frame 63x32
///   SPR2 #1   "HR00G00B00"  far zoom,    one frame 31x16
///   PALT #1                 256 colours shared by all three sprites (chunk flags 0)
///   FBMP #1   "Source"      optional 64x64 source bitmap (ignored by FreeSO; we write a 24-bit BMP)
///   STR# #0                 [0] name, [1] price as decimal text (int.Parse'd by FreeSO), [2] description
/// SPR2 frames: version 1000, default palette 1, frame flags 1 (colour only, no z/alpha), palette id
/// field 0xA3A3 (unused filler in v1000), transparent index 0, position (0,0).
///
/// Pixel mapping (verified against the template's own FBMP: exact match at near zoom): the frame is a
/// diamond for an N x N top-down tile (N = 64 near, 32 medium, 16 far; frame = (2N-1) x N). For frame
/// pixel (x, y): a = x - (N - 0.5), b = 2y; texel u = floor((a + b) / 2), v = floor((b - a) / 2).
/// So the tile's (0,0) corner is the diamond's top corner, +u runs down-right and +v down-left.
/// Pixels inside the diamond: rows y &lt;= N/2 cover |x - (N-1)| &lt;= 2y; lower rows cover
/// |x - (N-1)| &lt;= 2(N-1-y) + 1 (clipped to the frame). Everything else is transparent; FreeSO fills
/// those corners at load time by tiling the diamond (SPR2.FloorCopy). Medium/far use the tile box-
/// filtered down to 32/16 pixels and the same nearest-texel mapping (closest match to the Maxis art).
/// </summary>
public static class FloorBuilder
{
    // Chunk ids / labels copied from the frosti template.
    public const ushort FarSpriteId = 1, MediumSpriteId = 257, NearSpriteId = 513;
    public const ushort PaletteId = 1;
    public const ushort StringsId = 0;
    public const string SpriteLabel = "HR00G00B00";
    public const ushort FramePaletteFiller = 0xA3A3;
    /// <summary>Tile size at near zoom (the source texture is reduced to this before mapping).</summary>
    public const int TileSize = 64;

    /// <summary>Options for floor building.</summary>
    public sealed class Options
    {
        /// <summary>Use Floyd-Steinberg dithering when reducing to 256 colours.</summary>
        public bool Dither { get; set; }
        /// <summary>Include an FBMP "Source" chunk with the 64x64 tile (lets tools reopen the floor).</summary>
        public bool IncludeSourceBitmap { get; set; } = true;
        /// <summary>When the texture isn't square, crop the centre square (true) or stretch it (false).</summary>
        public bool CropToSquare { get; set; } = true;
    }

    public static byte[] Build(Image32 texture, string name, int price, string description)
        => BuildIff(texture, name, price, description, new Options()).ToBytes();

    public static byte[] Build(Image32 texture, string name, int price, string description, Options options)
        => BuildIff(texture, name, price, description, options).ToBytes();

    public static IffFile BuildIff(Image32 texture, string name, int price, string description, Options? options = null)
    {
        options ??= new Options();
        if (texture == null) throw new ArgumentNullException(nameof(texture));
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), "Price can't be negative.");
        var src = texture;
        if (src.Width != src.Height && options.CropToSquare) src = src.CropSquare();
        // Floors are opaque: flatten any alpha onto black so transparent texels don't vanish.
        var tile64 = Flatten(src.Resize(TileSize, TileSize));
        var tile32 = tile64.Resize(32, 32);
        var tile16 = tile64.Resize(16, 16);

        var palette = Palette.Quantize(new[] { tile64, tile32, tile16 }, 256);

        var iff = new IffFile();
        iff.Chunks.Add(new IffChunk("SPR2", NearSpriteId, MakeSprite(tile64, palette, options.Dither).Write(), SpriteLabel, 0x10));
        iff.Chunks.Add(new IffChunk("SPR2", MediumSpriteId, MakeSprite(tile32, palette, options.Dither).Write(), SpriteLabel, 0x10));
        iff.Chunks.Add(new IffChunk("SPR2", FarSpriteId, MakeSprite(tile16, palette, options.Dither).Write(), SpriteLabel, 0x10));
        iff.Chunks.Add(new IffChunk("PALT", PaletteId, Palt.FromPalette(palette).Write(), "", 0x00));
        if (options.IncludeSourceBitmap)
            iff.Chunks.Add(new IffChunk("FBMP", 1, Bmp.Encode24(tile64), "Source", 0x10));
        iff.Chunks.Add(new IffChunk("STR#", StringsId, CatalogStrings(name, price, description).Write(), "", 0x10));
        return iff;
    }

    /// <summary>The STR# #0 used by floors and walls: name, price, description (with the template's comments).</summary>
    public static StrList CatalogStrings(string name, int price, string description, string kind = "Floor")
        => StrList.Create(new[]
        {
            (name ?? "", $"{kind} Name"),
            (price.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{kind} Price"),
            (description ?? "", $"{kind} Description"),
        });

    /// <summary>Renders the diamond for an N x N tile (frame (2N-1) x N) as an RGBA image (outside = transparent).</summary>
    public static Image32 RenderDiamond(Image32 tile)
    {
        int n = tile.Width;
        if (tile.Height != n) throw new ArgumentException("Tile must be square.");
        int w = 2 * n - 1, h = n;
        var img = new Image32(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!InDiamond(n, x, y)) continue;
                TexelFor(n, x, y, out int u, out int v);
                tile.GetPixel(u, v, out var r, out var g, out var b, out _);
                img.SetPixel(x, y, r, g, b, 255);
            }
        return img;
    }

    /// <summary>True if frame pixel (x,y) of an N-sized floor sprite is inside the diamond.</summary>
    public static bool InDiamond(int n, int x, int y)
    {
        int w = 2 * n - 1;
        if (x < 0 || y < 0 || x >= w || y >= n) return false;
        int dx = Math.Abs(x - (n - 1));
        return y <= n / 2 ? dx <= 2 * y : dx <= 2 * (n - 1 - y) + 1;
    }

    /// <summary>Texel (u,v) of an N x N tile shown at frame pixel (x,y).</summary>
    public static void TexelFor(int n, int x, int y, out int u, out int v)
    {
        double a = x - (n - 0.5), b = 2.0 * y;
        u = Image32.Mod((int)Math.Floor((a + b) / 2), n);
        v = Image32.Mod((int)Math.Floor((b - a) / 2), n);
    }

    private static Spr2 MakeSprite(Image32 tile, Palette palette, bool dither)
    {
        var diamond = RenderDiamond(tile);
        var frame = new Spr2Frame(diamond.Width, diamond.Height, Spr2Frame.FlagColor)
        {
            PaletteId = FramePaletteFiller,
            TransparentIndex = 0,
        };
        frame.SetFromImage(diamond, palette, dither);
        var spr = new Spr2 { Version = 1000, DefaultPaletteId = PaletteId };
        spr.Frames.Add(frame);
        return spr;
    }

    internal static Image32 Flatten(Image32 img)
    {
        var o = img.Clone();
        for (int i = 0; i < o.Rgba.Length; i += 4)
        {
            int a = o.Rgba[i + 3];
            o.Rgba[i] = (byte)(o.Rgba[i] * a / 255); o.Rgba[i + 1] = (byte)(o.Rgba[i + 1] * a / 255); o.Rgba[i + 2] = (byte)(o.Rgba[i + 2] * a / 255);
            o.Rgba[i + 3] = 255;
        }
        return o;
    }
}
