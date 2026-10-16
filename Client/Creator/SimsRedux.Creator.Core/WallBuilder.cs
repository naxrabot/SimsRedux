namespace SimsRedux.Creator;

/// <summary>
/// Builds Sims 1 custom wallpapers (.wll).
///
/// SimsRedux: what we found in FreeSO (no sample .wll was available locally, so this is derived from code):
///
/// * tso.content/WorldWallProvider.cs (TS1 path): a custom wall is an IFF with STR# #0
///   ([0] name, [1] price text - int.Parse'd, [2] description) and three SPR# (old-style sprite) chunks:
///   #1 = far, #1793 = medium, #2049 = near. Thumbnail = medium SPR# frame 2.
/// * tso.world/Components/WallComponent.cs GetWallSprite(pattern, style, rotation, ...): the pattern
///   sprite frame index is the wall "rotation": 0 = top-left wall segment, 1 = top-right segment,
///   2 = horizontal diagonal, 3 = vertical diagonal. Each frame is drawn into a fixed destination
///   rectangle whose size equals the texture size (SrcRect = whole texture), so frames must be exactly:
///       near:   64x271, 64x271, 128x240, 16x232
///       medium: 32x136, 32x136,  64x120,  8x116
///       far:    16x68,  16x68,   32x60,   4x58
/// * The wall's silhouette is NOT taken from our sprite: the shader (2DWorldBatch.fx psZWall) takes
///   colour from our pattern and alpha from a mask = the wall *style* sprite (walls.iff, "walls up/down").
///   So the pattern frames must be fully filled rectangles; whatever lies outside the wall shape is
///   simply masked away. We therefore fill every pixel.
/// * Geometry of the wall face inside the frames (from the destination rectangles and the junction
///   rectangle - tile top corner sits 240px below the frame top at near zoom): the wall is 240px tall
///   (near). Frame 0 (top-left) rises left-to-right: column x spans y = 32 - x/2 .. 271 - x/2.
///   Frame 1 (top-right) falls left-to-right: column x spans y = x/2 .. 239 + x/2. Frame 2 (horizontal
///   diagonal) is a plain 128x240 rectangle (FreeSO's 3D mode even uses near frame 2 directly as the
///   wall texture, WallComponentRC.cs). Frame 3 is the vertical diagonal seen nearly edge-on (16px).
///   Medium/far are exactly half/quarter scale.
/// * SPR# chunks reference a PALT by id in their header (SPR.cs: PaletteID). We write one shared PALT #1.
///
/// Mapping we use: the input texture is laid out as an "unrolled" wall face one tile wide and one wall
/// tall (64 x 240 at near zoom), tiling it vertically (see <see cref="Options.VerticalRepeats"/>).
/// Frames 0/1 shear that face along the wall slope (texture rows follow the 2:1 isometric slope),
/// frame 2 stretches it over the 128px diagonal (one repeat per diagonal, like FreeSO's 3D mode) and
/// frame 3 squeezes it into 16px. Uncertain bits are constants below so they're easy to adjust.
/// </summary>
public static class WallBuilder
{
    public const ushort FarSpriteId = 1, MediumSpriteId = 1793, NearSpriteId = 2049;
    public const ushort PaletteId = 1;
    public const ushort StringsId = 0;

    /// <summary>Frame sizes per zoom, index = rotation (0 TL, 1 TR, 2 horizontal diag, 3 vertical diag).</summary>
    public static readonly (int W, int H)[] NearFrames = { (64, 271), (64, 271), (128, 240), (16, 232) };
    public static readonly (int W, int H)[] MediumFrames = { (32, 136), (32, 136), (64, 120), (8, 116) };
    public static readonly (int W, int H)[] FarFrames = { (16, 68), (16, 68), (32, 60), (4, 58) };

    /// <summary>Wall height in pixels at near zoom (scaled by 1/2 and 1/4 for medium/far).</summary>
    public const int NearWallHeight = 240;
    /// <summary>Width of one wall tile at near zoom, measured horizontally on screen.</summary>
    public const int NearTileWidth = 64;
    /// <summary>
    /// Wall height expressed in tile lengths (FreeSO 3D uses 2.95 units per level, 1 unit per tile).
    /// Used to decide how often a square texture repeats vertically.
    /// </summary>
    public const double WallHeightInTiles = 2.95;

    public sealed class Options
    {
        /// <summary>
        /// How many times the texture repeats from floor to ceiling. 0 = automatic: a texture is
        /// assumed to span one tile horizontally and keep its aspect ratio, i.e.
        /// round(2.95 * width / height), at least 1 (tall Sims 3 wallpaper textures end up stretched once).
        /// </summary>
        public int VerticalRepeats { get; set; }
        public bool Dither { get; set; }
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
        int repeats = options.VerticalRepeats > 0
            ? options.VerticalRepeats
            : Math.Max(1, (int)Math.Round(WallHeightInTiles * texture.Width / texture.Height));
        var opaque = FloorBuilder.Flatten(texture);

        // Unrolled wall face at each zoom: one tile wide, one wall tall.
        var faces = new Image32[3];
        int[] scale = { 1, 2, 4 }; // near, medium, far divisors
        for (int z = 0; z < 3; z++)
        {
            int fw = NearTileWidth / scale[z], fh = NearWallHeight / scale[z];
            int tileH = Math.Max(1, (int)Math.Round((double)fh / repeats));
            var tile = opaque.Resize(fw, tileH);
            var face = new Image32(fw, fh);
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                {
                    // repeat vertically, anchored at the floor so the skirting/bottom edge lines up
                    int sy = Image32.Mod(tileH - 1 - Image32.Mod(fh - 1 - y, tileH), tileH);
                    tile.GetPixel(x, sy, out var r, out var g, out var b, out _);
                    face.SetPixel(x, y, r, g, b, 255);
                }
            faces[z] = face;
        }

        var frames = new List<Image32>[3];
        var sizes = new[] { NearFrames, MediumFrames, FarFrames };
        for (int z = 0; z < 3; z++)
        {
            frames[z] = new List<Image32>();
            for (int rot = 0; rot < 4; rot++) frames[z].Add(RenderFrame(faces[z], rot, sizes[z][rot].W, sizes[z][rot].H, scale[z]));
        }

        var palette = Palette.Quantize(frames.SelectMany(f => f), 256);

        var iff = new IffFile();
        iff.Chunks.Add(new IffChunk("STR#", StringsId, FloorBuilder.CatalogStrings(name, price, description, "Wall").Write(), "", 0x10));
        iff.Chunks.Add(new IffChunk("PALT", PaletteId, Palt.FromPalette(palette).Write(), "", 0x00));
        ushort[] ids = { NearSpriteId, MediumSpriteId, FarSpriteId };
        string[] labels = { "Near", "Medium", "Far" };
        for (int z = 0; z < 3; z++)
        {
            var spr = new Spr { PaletteId = PaletteId };
            foreach (var img in frames[z])
            {
                var f = new SprFrame(img.Width, img.Height);
                f.SetFromImage(img, palette, options.Dither);
                spr.Frames.Add(f);
            }
            iff.Chunks.Add(new IffChunk("SPR#", ids[z], spr.Write(), labels[z], 0x10));
        }
        return iff;
    }

    /// <summary>
    /// Renders one wall frame from an unrolled face image (face width = one tile, face height = wall height).
    /// Every pixel of the frame is filled (the game masks the silhouette with the wall style sprite).
    /// </summary>
    public static Image32 RenderFrame(Image32 face, int rotation, int w, int h, int scaleDivisor)
    {
        var img = new Image32(w, h);
        double wallH = (double)NearWallHeight / scaleDivisor;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double fx, fy; // coordinates in the face image (pixel-centre based)
                double cx = x + 0.5, cy = y + 0.5;
                switch (rotation)
                {
                    case 0: // top-left wall: top edge from (0, h - wallH) up to (w, h - wallH - w/2)
                        fx = cx / w * face.Width;
                        fy = (cy - (h - wallH - cx / 2.0)) / wallH * face.Height;
                        break;
                    case 1: // top-right wall: top edge from (0, 0) down to (w, w/2)
                        fx = cx / w * face.Width;
                        fy = (cy - cx / 2.0) / wallH * face.Height;
                        break;
                    case 2: // horizontal diagonal: plain rectangle, one repeat across
                        fx = cx / w * face.Width;
                        fy = (cy - (h - wallH)) / wallH * face.Height;
                        break;
                    default: // vertical diagonal, nearly edge-on: squeeze the face width into the strip
                        fx = cx / w * face.Width;
                        fy = (cy - (h - wallH)) / wallH * face.Height;
                        break;
                }
                face.SampleWrap(fx, fy, out var r, out var g, out var b, out _);
                img.SetPixel(x, y, Image32.ClampByte(r), Image32.ClampByte(g), Image32.ClampByte(b), 255);
            }
        return img;
    }

    /// <summary>
    /// Debug helper: composes a preview of frames 0 and 1 side by side, masked to the wall face
    /// parallelograms described above (roughly what the game shows for a straight wall corner).
    /// </summary>
    public static Image32 PreviewCorner(Spr near, Palette palette)
    {
        var f0 = near.Frames[0].ToImage(palette);
        var f1 = near.Frames[1].ToImage(palette);
        var img = new Image32(f0.Width + f1.Width, f0.Height);
        double wallH = NearWallHeight;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                bool left = x < f0.Width;
                int lx = left ? x : x - f0.Width;
                double top = left ? (img.Height - wallH - (lx + 0.5) / 2.0) : (lx + 0.5) / 2.0;
                if (y + 0.5 < top || y + 0.5 > top + wallH) continue;
                var src = left ? f0 : f1;
                src.GetPixel(lx, y, out var r, out var g, out var b, out var a);
                img.SetPixel(x, y, r, g, b, a);
            }
        return img;
    }
}
