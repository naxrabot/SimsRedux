using System.Text;
using SimsRedux.Creator;

namespace SimsRedux.Creator.Tests;

/// <summary>
/// SimsRedux: plain console test runner for the creator core library. Prints PASS/FAIL per check and
/// exits non-zero if anything failed. Usage: SimsRedux.Creator.Tests [previewOutputFolder]
/// </summary>
public static class Program
{
    // SimsRedux: the repo root - the folder above this test project that holds Client/ (works here and on CI)
    static readonly string Repo = FindRepo();
    static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Client", "Simitone"))) d = d.Parent;
        return d?.FullName ?? "/home/claude/simsredux";
    }
    const string FreeSo = "/home/claude/simsredux-freeso";
    static readonly string FrostiFloor = $"{Repo}/Client/Simitone/Simitone.Client/Content/redux/mods/Downloads/frosti_chevron/frosti_chevronsimplecherry.flr";
    static readonly string PosterTemplate = $"{Repo}/Client/Simitone/Simitone.Client/Content/redux/mods/Overrides/PaintingPosterSuperstar.iff";
    static readonly string AnimatedTemplate = $"{Repo}/Client/Simitone/Simitone.Client/Content/redux/mods/Overrides/PaintingsAnimated.iff";

    static int _pass, _fail;
    static string OutDir = "/tmp/claude-0/-home-claude/ea31331f-9456-5e95-aba9-2082ad890cca/scratchpad/creator/out";

    public static int Main(string[] args)
    {
        if (args.Length > 0) OutDir = args[0];
        Directory.CreateDirectory(OutDir);
        Console.WriteLine($"SimsRedux Creator core tests (previews -> {OutDir})");

        Run("PNG encode/decode round trip", TestPng);
        Run("BMP encode/decode round trip", TestBmp);
        Run("Image resize quality", TestResize);
        Run("DDS DXT1/DXT3/DXT5/uncompressed decode", TestDds);
        Run("RefPack compress/decompress (all opcodes) + corrupt input", TestRefPack);
        Run("DBPF 2.0 synthetic package read-back", TestDbpf);
        Run("Sims3Library.FindPackages", TestFindPackages);
        Run("IFF round trip (frosti floor) is byte-identical", TestIffRoundTrip);
        Run("STR# read/write", TestStr);
        Run("SPR2 decode -> encode -> decode (floor + poster)", TestSpr2RoundTrip);
        Run("SPR# encode -> decode", TestSprRoundTrip);
        Run("Palette quantization error is small", TestQuantize);
        Run("Floor diamond mapping matches template", TestFloorMapping);
        Run("Floor build from template FBMP ~= template", TestFloorBuild);
        Run("Wall build: SPR# frames have expected sizes", TestWallBuild);
        Run("Picture build from test image (poster template)", TestPictureBuild);
        Run("Picture build (animated paintings template) doesn't crash", TestPictureAnimated);
        Run("CreatorPack manifest + creator.json", TestCreatorPack);
        Run("Previews render for every kind", TestPreviews);
        Run("Texture sorter keeps real textures and drops helpers/masks/effects", TestSorter);
        Run("Colouring in grey textures", TestColours);
        Run("Item source info (new fields + older items)", TestItemSource);
        Run("Editing items: rename, change kind, name clash", TestPackEdit);
        Run("Previews render at every zoom", TestPreviewZooms);

        Console.WriteLine();
        Console.WriteLine($"{_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }

    static void TestSorter()
    {
        // names
        foreach (var junk in new[] { "stoneTile01Lrg_s_0x1234ABCD", "Floor_Tile_Puzzle_m_0x00AA", "stoneTile01Lrg_4_0x99", "Wall_BathroomTile_N",
                                     "ScreenGrabFilter_Tiles", "causticsTiled_0x3828c", "kaleidoscopeTiled_0x1", "effects_tiles", "woodFloor_spec" })
            Check(TextureSorter.ByName(junk) != TextureKind.Usable, "name dropped: " + junk);
        foreach (var good in new[] { "tileMarbleLrg_til_0x55", "Floor_Tile_Bathroom", "diner_tile_0x5f3bb27b", "woodPlankOak01", "sidewalkTile_concrete" })
            Check(TextureSorter.ByName(good) == TextureKind.Usable, "name kept: " + good);

        // pictures
        Check(TextureSorter.ByLook(MakeBrickTexture(128, 128)) == TextureKind.Usable, "brick texture kept");
        var mask = new Image32(64, 64);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            { bool red = ((x / 8) + (y / 8)) % 2 == 0; mask.SetPixel(x, y, (byte)(red ? 255 : 0), (byte)(red ? 0 : 255), 0, 255); }
        Check(TextureSorter.ByLook(mask) == TextureKind.Mask, "red/green mask dropped");
        var normal = new Image32(64, 64);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) normal.SetPixel(x, y, (byte)(120 + (x % 9)), (byte)(125 + (y % 7)), 250, 255);
        Check(TextureSorter.ByLook(normal) == TextureKind.NormalMap, "normal map dropped");
        var flat = new Image32(64, 64); flat.Fill(40, 44, 60, 255);
        Check(TextureSorter.ByLook(flat) == TextureKind.Blank, "flat colour dropped");
        var clear = new Image32(64, 64); clear.Fill(255, 0, 0, 0);
        Check(TextureSorter.ByLook(clear) == TextureKind.Blank, "see-through dropped");
        var grid = new Image32(64, 64);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { bool line = x % 16 < 2 || y % 16 < 2; byte v = (byte)(line ? 0 : 255); grid.SetPixel(x, y, v, v, v, 255); }
        Check(TextureSorter.ByLook(grid) == TextureKind.Mask, "black-and-white grout grid dropped");
        // a real-looking wood: brown with grain
        var wood = new Image32(64, 64);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { int v = (int)(20 * Math.Sin(x * 0.7 + Math.Sin(y * 0.2) * 3)); wood.SetPixel(x, y, (byte)(140 + v), (byte)(90 + v / 2), (byte)(50 + v / 3), 255); }
        Check(TextureSorter.ByLook(wood) == TextureKind.Usable, "wood kept");
        // a grey stone is still usable (greys aren't dropped)
        var stone = new Image32(64, 64);
        var rnd = new Random(3);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { byte v = (byte)(100 + rnd.Next(60)); stone.SetPixel(x, y, v, v, (byte)(v + 4), 255); }
        Check(TextureSorter.ByLook(stone) == TextureKind.Greyscale, "grey stone sorted as grey (colourable)");
        // a blurred red/green mask (like the DXT-compressed Sims 3 ones) is still a mask
        var blur = new Image32(64, 64);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { double t = 0.5 + 0.5 * Math.Sin(x * 0.5); blur.SetPixel(x, y, (byte)(40 + 200 * t), (byte)(40 + 200 * (1 - t)), (byte)(20 + rnd.Next(20)), 255); }
        Check(TextureSorter.ByLook(blur) == TextureKind.Mask || TextureSorter.ByLook(blur) == TextureKind.Usable, "blurred mask doesn't crash");
        var blockMask = new Image32(64, 64);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { bool red = ((x / 6) + (y / 6)) % 2 == 0; blockMask.SetPixel(x, y, (byte)(red ? 230 - rnd.Next(30) : 30 + rnd.Next(30)), (byte)(red ? 20 + rnd.Next(30) : 220 - rnd.Next(30)), (byte)rnd.Next(25), 255); }
        Check(TextureSorter.ByLook(blockMask) == TextureKind.Mask, "noisy red/green mask dropped");
        foreach (var obj in new[] { "poolGnome_d_0x9b", "snowmanBallSpec_0x1", "treeWillow_winter_c_0x2", "tableCoffeeMission1", "cratesWooden_d_0xc", "halfWallOrnateAsian" })
            Check(TextureSorter.ByName(obj) != TextureKind.Usable, "object texture dropped: " + obj);
    }

    static void TestPreviews()
    {
        var tex = MakeBrickTexture(128, 128);
        var floor = Previews.Render("floor", FloorBuilder.Build(tex, "P Floor", 5, ""));
        Check(floor.Width > 100 && floor.Height > 50, "floor preview size " + floor.Width + "x" + floor.Height);
        floor.SavePng(Out("preview_floor.png"));
        var wall = Previews.Render("wall", WallBuilder.Build(tex, "P Wall", 5, ""));
        Check(wall.Width > 50 && wall.Height > 100, "wall preview size " + wall.Width + "x" + wall.Height);
        wall.SavePng(Out("preview_wall.png"));
        var template = File.ReadAllBytes(PosterTemplate);
        var pic = Previews.Render("picture", PictureBuilder.Build(tex, "P Picture", 5, "", template));
        Check(pic.Width > 20 && pic.Height > 20, "picture preview size " + pic.Width + "x" + pic.Height);
        pic.SavePng(Out("preview_picture.png"));
    }

    static void TestColours()
    {
        var grey = new Image32(16, 16);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { byte v = (byte)(x < 8 ? 100 : 156); grey.SetPixel(x, y, v, v, v, 255); }
        grey.SetPixel(0, 0, 50, 50, 50, 0); // see-through pixel
        var c = Colours.Colourise(grey, 200, 100, 50);
        double sr = 0, sg = 0, sb = 0; int n = 0;
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { if (x == 0 && y == 0) continue; c.GetPixel(x, y, out var r, out var g, out var b, out _); sr += r; sg += g; sb += b; n++; }
        Check(Math.Abs(sr / n - 200) < 6 && Math.Abs(sg / n - 100) < 4 && Math.Abs(sb / n - 50) < 3, $"average becomes the picked colour ({sr / n:F0},{sg / n:F0},{sb / n:F0})");
        c.GetPixel(2, 5, out var dr, out _, out _, out _); c.GetPixel(12, 5, out var lr, out _, out _, out _);
        Check(dr < lr, "darker parts stay darker");
        c.GetPixel(0, 0, out _, out _, out _, out var a0);
        Check(a0 == 0, "see-through stays see-through");
        Check(Colours.TryParseHex("#A0B1c2", out var hr, out var hg, out var hb) && hr == 0xA0 && hg == 0xB1 && hb == 0xC2, "hex colour reads");
        Check(!Colours.TryParseHex("#12345", out _, out _, out _) && !Colours.TryParseHex("zzzzzz", out _, out _, out _), "bad hex refused");
        Check(Colours.ToHex(1, 2, 255) == "#0102FF", "hex colour writes");
        Check(Colours.SetPresets.Count >= 12 && Colours.SetPresets.Select(p => p.Name).Distinct().Count() == Colours.SetPresets.Count, "set colours have unique names");
    }

    static void TestItemSource()
    {
        var a = ItemSource.ParseLegacy("FullBuild0.package 00000000ABCD1234 colour #aa8866");
        Check(a.Package == "FullBuild0.package" && a.Instance == 0xABCD1234UL && a.Tint == "#AA8866", "older text: package, id and colour");
        var b = ItemSource.ParseLegacy("FullBuild0.package#0x00B2D882:0x00000000:0x1234");
        Check(b.Package == "FullBuild0.package" && b.Instance == 0x1234UL && b.Tint == null, "oldest text: type:group:instance");
        var c = ItemSource.ParseLegacy(@"C:\Pictures\my poster.png");
        Check(c.IsFile && c.File == @"C:\Pictures\my poster.png" && !c.IsPackage, "picture file path");
        var d = ItemSource.ParseLegacy("");
        Check(!d.IsFile && !d.IsPackage, "empty source");
        var item = new CreatorItem { Source = "x" };
        item.WriteSource(new ItemSource { Package = @"E:\Sims3\GameData\Shared\Packages\FullBuild0.package", Instance = 0x55, Tint = "#112233" });
        var back = item.ReadSource();
        Check(back.Package!.EndsWith("FullBuild0.package") && back.Instance == 0x55UL && back.Tint == "#112233", "new fields round trip");
        Check(item.Source == "FullBuild0.package 0000000000000055 colour #112233", "plain source text still written: " + item.Source);
    }

    static void TestPackEdit()
    {
        var folder = Out("pack_edit");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        var pack = CreatorPack.Open(folder);
        var tex = MakeBrickTexture(64, 64);
        var src = new ItemSource { Package = "/x/FullBuild0.package", Instance = 7 };
        var f1 = pack.Add("floor", "Red Brick", 10, "", FloorBuilder.Build(tex, "Red Brick", 10, ""), src);
        var f2 = pack.Add("floor", "Blue Brick", 10, "", FloorBuilder.Build(tex, "Blue Brick", 10, ""), src);
        Check(f1.SourceInstance == "0000000000000007" && f1.ReadSource().Instance == 7UL, "Add stores the source");
        // same name: file kept
        pack.Update(f1, "floor", "Red Brick", 12, "now 12", FloorBuilder.Build(tex, "Red Brick", 12, ""), null);
        Check(f1.File == "Downloads/Floors/redux_red_brick.flr" && f1.Price == 12 && f1.ReadSource().Instance == 7UL, "edit keeps file and source");
        // rename: new file, old one gone
        pack.Update(f1, "floor", "Dark Red Brick", 12, "", FloorBuilder.Build(tex, "Dark Red Brick", 12, ""), new ItemSource { Package = "/x/FullBuild0.package", Instance = 7, Tint = "#550000" });
        Check(f1.File == "Downloads/Floors/redux_dark_red_brick.flr" && File.Exists(Path.Combine(folder, f1.File)) && !File.Exists(Path.Combine(folder, "Downloads/Floors/redux_red_brick.flr")), "rename moves the file");
        Check(f1.Tint == "#550000", "rename stores the new colour");
        // name clash refused
        bool threw = false;
        try { pack.Update(f1, "floor", "blue brick", 12, "", new byte[] { 1 }, null); } catch (InvalidOperationException) { threw = true; }
        Check(threw && f1.Name == "Dark Red Brick" && File.ReadAllBytes(Path.Combine(folder, f1.File)).Length > 1, "can't rename onto another item");
        // same name as another kind is fine; changing kind moves folder
        pack.Update(f2, "wall", "Blue Brick", 14, "", WallBuilder.Build(tex, "Blue Brick", 14, ""), null);
        Check(f2.Kind == "wall" && f2.File == "Downloads/Walls/redux_blue_brick.wll" && !File.Exists(Path.Combine(folder, "Downloads/Floors/redux_blue_brick.flr")), "changing kind moves it to Walls");
        pack.Save();
        var re = CreatorPack.Open(folder);
        Check(re.Items.Count == 2 && re.Items.Any(i => i.Name == "Dark Red Brick" && i.Tint == "#550000" && i.SourcePackage == "/x/FullBuild0.package"), "edits saved to creator.json");
        Check(CreatorPack.ReadManifest(Path.Combine(folder, "manifest.txt")).SequenceEqual(new[] { "Downloads/Floors/redux_dark_red_brick.flr", "Downloads/Walls/redux_blue_brick.wll" }), "manifest follows the renames");
    }

    static void TestPreviewZooms()
    {
        var tex = MakeBrickTexture(128, 128);
        var built = new Dictionary<string, byte[]>
        {
            ["floor"] = FloorBuilder.Build(tex, "Z Floor", 5, ""),
            ["wall"] = WallBuilder.Build(tex, "Z Wall", 5, ""),
            ["picture"] = PictureBuilder.Build(tex, "Z Picture", 5, "", File.ReadAllBytes(PosterTemplate)),
        };
        foreach (var kv in built)
        {
            var sizes = new List<int>();
            for (int z = 0; z < 3; z++)
            {
                var img = Previews.Render(kv.Key, kv.Value, z);
                sizes.Add(img.Width * img.Height);
                img.SavePng(Out($"zoom_{kv.Key}_{Previews.ZoomNames[z].ToLowerInvariant()}.png"));
            }
            Check(sizes[0] > sizes[1] && sizes[1] > sizes[2], $"{kv.Key}: near > medium > far ({string.Join(", ", sizes)})");
            Check(Previews.Render(kv.Key, kv.Value).Rgba.SequenceEqual(Previews.Render(kv.Key, kv.Value, 0).Rgba), $"{kv.Key}: default preview is the near zoom");
        }
    }

    // ------------------------------------------------------------------ harness

    static void Run(string name, Action test)
    {
        Console.WriteLine($"== {name}");
        try { test(); }
        catch (Exception e) { Fail($"{name}: threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
    }

    static void Check(bool ok, string what)
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS {what}"); }
        else Fail(what);
    }

    static void Fail(string what) { _fail++; Console.WriteLine($"  FAIL {what}"); }

    static void Throws<T>(Action a, string what) where T : Exception
    {
        try { a(); Fail($"{what} (no exception)"); }
        catch (T) { Check(true, what); }
        catch (Exception e) { Fail($"{what} (threw {e.GetType().Name}: {e.Message})"); }
    }

    static string Out(string name) => Path.Combine(OutDir, name);

    /// <summary>Upscales with nearest neighbour so small sprites are easy to look at.</summary>
    static Image32 Zoom(Image32 img, int k)
    {
        var o = new Image32(img.Width * k, img.Height * k);
        for (int y = 0; y < o.Height; y++)
            for (int x = 0; x < o.Width; x++)
            {
                img.GetPixel(x / k, y / k, out var r, out var g, out var b, out var a);
                o.SetPixel(x, y, r, g, b, a);
            }
        return o;
    }

    /// <summary>Composites an image over a checkerboard (to see transparency in previews).</summary>
    static Image32 OnChecker(Image32 img)
    {
        var o = new Image32(img.Width, img.Height);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                img.GetPixel(x, y, out var r, out var g, out var b, out var a);
                byte bg = ((x / 8 + y / 8) & 1) == 0 ? (byte)200 : (byte)150;
                o.SetPixel(x, y, (byte)((r * a + bg * (255 - a)) / 255), (byte)((g * a + bg * (255 - a)) / 255), (byte)((b * a + bg * (255 - a)) / 255), 255);
            }
        return o;
    }

    /// <summary>Procedural test texture: gradient, stripes and an asymmetric "R"-ish glyph.</summary>
    static Image32 TestPicture(int w, int h)
    {
        var img = new Image32(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = (double)x / w, v = (double)y / h;
                byte r = (byte)(40 + 180 * u), g = (byte)(60 + 120 * v), b = (byte)(200 - 150 * u * v);
                // diagonal stripes
                if (((x + y) / (w / 16 + 1)) % 2 == 0) { r = (byte)(r * 0.8); g = (byte)(g * 0.8); }
                // a big "F" in the upper-left (asymmetric so mirroring would be obvious)
                bool f = (u > 0.15 && u < 0.27 && v > 0.15 && v < 0.85) || (u > 0.15 && u < 0.7 && v > 0.15 && v < 0.27) || (u > 0.15 && u < 0.55 && v > 0.45 && v < 0.55);
                if (f) { r = 250; g = 240; b = 30; }
                // red dot in the top-right corner
                if (Math.Pow(u - 0.8, 2) + Math.Pow(v - 0.2, 2) < 0.006) { r = 230; g = 20; b = 20; }
                img.SetPixel(x, y, r, g, b, 255);
            }
        return img;
    }

    // ------------------------------------------------------------------ image formats

    static void TestPng()
    {
        var img = TestPicture(37, 23);
        img.SetPixel(3, 4, 1, 2, 3, 0);
        img.SetPixel(5, 6, 10, 20, 30, 128);
        var png = img.ToPng();
        var back = Image32.LoadPng(png);
        Check(back.Width == 37 && back.Height == 23, "size preserved");
        Check(back.Rgba.SequenceEqual(img.Rgba), "pixels identical (RGBA)");
        Throws<InvalidDataException>(() => Image32.LoadPng(new byte[] { 1, 2, 3 }), "garbage throws InvalidDataException");
        img.SavePng(Out("test_picture.png"));
    }

    static void TestBmp()
    {
        var img = TestPicture(13, 9);
        var bmp = Bmp.Encode24(img);
        var back = Bmp.Decode(bmp);
        Check(back.Width == 13 && back.Height == 9 && Image32.MeanRgbError(img, back) == 0, "24-bit BMP round trip exact");
        Check(Image32.Load(bmp).Width == 13, "Image32.Load sniffs BMP");
    }

    static void TestResize()
    {
        // A 2x2 checker of black/white averaged down to 1x1 must be mid-grey.
        var c = new Image32(2, 2);
        c.SetPixel(0, 0, 0, 0, 0, 255); c.SetPixel(1, 0, 255, 255, 255, 255); c.SetPixel(0, 1, 255, 255, 255, 255); c.SetPixel(1, 1, 0, 0, 0, 255);
        var one = c.Resize(1, 1);
        one.GetPixel(0, 0, out var r, out _, out _, out var a);
        Check(Math.Abs(r - 128) <= 1 && a == 255, $"box filter averages (got {r})");
        var big = TestPicture(512, 512);
        var small = big.Resize(64, 64);
        var ref64 = TestPicture(64, 64);
        double err = Image32.MeanRgbError(small, ref64);
        Check(err < 30, $"512->64 downscale resembles direct 64px render (mean err {err:F1})");
        var up = small.Resize(128, 96);
        Check(up.Width == 128 && up.Height == 96, "upscale size");
    }

    // ------------------------------------------------------------------ DDS

    static byte[] MakeDds(int w, int h, string? fourcc, int bits = 0, uint rm = 0, uint gm = 0, uint bm = 0, uint am = 0, uint pfFlags = 0, byte[]? payload = null)
    {
        var d = new byte[128 + (payload?.Length ?? 0)];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(d, 0);
        BitConverter.GetBytes(124).CopyTo(d, 4);
        BitConverter.GetBytes(0x1007).CopyTo(d, 8);
        BitConverter.GetBytes(h).CopyTo(d, 12);
        BitConverter.GetBytes(w).CopyTo(d, 16);
        BitConverter.GetBytes(1).CopyTo(d, 28);
        BitConverter.GetBytes(32).CopyTo(d, 76);
        if (fourcc != null) { BitConverter.GetBytes(0x4u).CopyTo(d, 80); Encoding.ASCII.GetBytes(fourcc).CopyTo(d, 84); }
        else
        {
            BitConverter.GetBytes(pfFlags).CopyTo(d, 80);
            BitConverter.GetBytes(bits).CopyTo(d, 88);
            BitConverter.GetBytes(rm).CopyTo(d, 92); BitConverter.GetBytes(gm).CopyTo(d, 96); BitConverter.GetBytes(bm).CopyTo(d, 100); BitConverter.GetBytes(am).CopyTo(d, 104);
        }
        BitConverter.GetBytes(0x1000).CopyTo(d, 108);
        payload?.CopyTo(d, 128);
        return d;
    }

    static ushort To565(int r, int g, int b) => (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

    /// <summary>8x4 DXT1: left block pure red/blue split by index, right block 3-colour mode with transparency.</summary>
    static byte[] Dxt1Payload()
    {
        var p = new List<byte>();
        // Block A: c0 = red, c1 = blue (c0 > c1 -> 4 colour); rows: idx 0,1,2,3 per column
        ushort red = To565(255, 0, 0), blue = To565(0, 0, 255);
        p.AddRange(BitConverter.GetBytes(red)); p.AddRange(BitConverter.GetBytes(blue));
        uint idx = 0;
        for (int i = 0; i < 16; i++) idx |= (uint)(i % 4) << (2 * i); // column x uses index x
        p.AddRange(BitConverter.GetBytes(idx));
        // Block B: c0 = green < c1 = white -> 3 colour + transparent; all pixels index 3 except first (index 0)
        ushort green = To565(0, 255, 0), white = To565(255, 255, 255);
        p.AddRange(BitConverter.GetBytes(green)); p.AddRange(BitConverter.GetBytes(white));
        uint idx2 = 0xFFFFFFFC; // pixel 0 -> 0, others -> 3
        p.AddRange(BitConverter.GetBytes(idx2));
        return p.ToArray();
    }

    static byte[] Dxt5Payload()
    {
        var p = new List<byte>();
        // alpha: a0 = 255, a1 = 0, 8-alpha mode; pixel i uses index i % 8
        p.Add(255); p.Add(0);
        ulong bits = 0;
        for (int i = 0; i < 16; i++) bits |= (ulong)(i % 8) << (3 * i);
        for (int i = 0; i < 6; i++) p.Add((byte)(bits >> (8 * i)));
        ushort c0 = To565(255, 255, 0), c1 = To565(0, 0, 0);
        p.AddRange(BitConverter.GetBytes(c0)); p.AddRange(BitConverter.GetBytes(c1));
        p.AddRange(BitConverter.GetBytes(0u)); // all colour index 0 -> yellow
        return p.ToArray();
    }

    static void TestDds()
    {
        var dxt1 = MakeDds(8, 4, "DXT1", payload: Dxt1Payload());
        Check(Dds.TryReadInfo(dxt1, out var info) && info.Width == 8 && info.Height == 4 && info.Format == DdsFormat.DXT1, "DXT1 header info");
        var img = Dds.Decode(dxt1);
        img.GetPixel(0, 0, out var r, out var g, out var b, out var a);
        Check(r == 255 && g == 0 && b == 0 && a == 255, $"DXT1 colour 0 = red (got {r},{g},{b},{a})");
        img.GetPixel(1, 2, out r, out g, out b, out a);
        Check(r == 0 && g == 0 && b == 255, $"DXT1 colour 1 = blue (got {r},{g},{b})");
        img.GetPixel(2, 1, out r, out g, out b, out a);
        Check(r == 170 && b == 85 && g == 0, $"DXT1 colour 2 = 2/3 red + 1/3 blue (got {r},{g},{b})");
        img.GetPixel(4, 0, out r, out g, out b, out a);
        Check(r == 0 && g == 255 && b == 0 && a == 255, $"DXT1 3-colour block pixel 0 green (got {r},{g},{b},{a})");
        img.GetPixel(5, 1, out r, out g, out b, out a);
        Check(a == 0, "DXT1 index 3 in 3-colour mode is transparent");

        var dxt5 = MakeDds(4, 4, "DXT5", payload: Dxt5Payload());
        var i5 = Dds.Decode(dxt5);
        i5.GetPixel(0, 0, out r, out g, out b, out a);
        Check(r == 255 && g == 255 && b == 0 && a == 255, $"DXT5 pixel 0 opaque yellow (got {r},{g},{b},{a})");
        i5.GetPixel(1, 0, out _, out _, out _, out a);
        Check(a == 0, $"DXT5 alpha index 1 = a1 = 0 (got {a})");
        i5.GetPixel(2, 0, out _, out _, out _, out a);
        Check(a == (6 * 255) / 7, $"DXT5 alpha index 2 = 6/7*255 (got {a})");

        // DXT3: explicit 4-bit alpha
        var p3 = new byte[16];
        for (int i = 0; i < 8; i++) p3[i] = 0xF0; // even pixels alpha 0, odd pixels alpha 15
        BitConverter.GetBytes(To565(0, 255, 255)).CopyTo(p3, 8);
        var i3 = Dds.Decode(MakeDds(4, 4, "DXT3", payload: p3));
        i3.GetPixel(0, 0, out _, out g, out b, out a);
        Check(a == 0 && g == 255 && b == 255, "DXT3 even pixel alpha 0, cyan");
        i3.GetPixel(1, 0, out _, out _, out _, out a);
        Check(a == 255, "DXT3 odd pixel alpha 255");

        // A8R8G8B8
        var argb = new byte[2 * 1 * 4];
        argb[0] = 0x10; argb[1] = 0x20; argb[2] = 0x30; argb[3] = 0x40; // B G R A
        argb[4] = 0xFF; argb[5] = 0x00; argb[6] = 0x80; argb[7] = 0xFF;
        var ia = Dds.Decode(MakeDds(2, 1, null, 32, 0xFF0000, 0xFF00, 0xFF, 0xFF000000, 0x41, argb));
        ia.GetPixel(0, 0, out r, out g, out b, out a);
        Check(r == 0x30 && g == 0x20 && b == 0x10 && a == 0x40, "A8R8G8B8 pixel");
        // X8R8G8B8 (no alpha flag)
        var ix = Dds.Decode(MakeDds(2, 1, null, 32, 0xFF0000, 0xFF00, 0xFF, 0, 0x40, argb));
        ix.GetPixel(0, 0, out _, out _, out _, out a);
        Check(a == 255, "X8R8G8B8 is opaque");
        // R8G8B8
        var i24 = Dds.Decode(MakeDds(1, 1, null, 24, 0xFF0000, 0xFF00, 0xFF, 0, 0x40, new byte[] { 1, 2, 3 }));
        i24.GetPixel(0, 0, out r, out g, out b, out _);
        Check(r == 3 && g == 2 && b == 1, "R8G8B8 pixel");
        // L8 and A8L8
        var il = Dds.Decode(MakeDds(2, 1, null, 8, 0xFF, 0, 0, 0, 0x20000, new byte[] { 7, 200 }));
        il.GetPixel(1, 0, out r, out g, out b, out a);
        Check(r == 200 && g == 200 && b == 200 && a == 255, "L8 pixel");
        var ial = Dds.Decode(MakeDds(1, 1, null, 16, 0xFF, 0, 0, 0xFF00, 0x20001, new byte[] { 90, 33 }));
        ial.GetPixel(0, 0, out r, out _, out _, out a);
        Check(r == 90 && a == 33, "A8L8 pixel");
        Check(Dds.TryReadInfo(MakeDds(2, 1, null, 8, 0xFF, 0, 0, 0, 0x20000), out var li) && li.Format == DdsFormat.L8, "L8 info");
        Throws<InvalidDataException>(() => Dds.Decode(MakeDds(64, 64, "DXT1", payload: new byte[8])), "truncated DXT data throws");
        Zoom(img, 16).SavePng(Out("dds_dxt1_test.png"));
    }

    // ------------------------------------------------------------------ RefPack / DBPF

    static void TestRefPack()
    {
        var rnd = new Random(1234);
        var inputs = new List<byte[]>
        {
            Array.Empty<byte>(),
            new byte[] { 1 },
            new byte[] { 1, 2, 3, 4, 5, 6, 7 },
            Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("SimsRedux creator! ", 500))),
        };
        var noise = new byte[70000]; rnd.NextBytes(noise); inputs.Add(noise);
        // mixed: repeats at short, medium and long distances with long lengths (exercises all copy forms)
        var mixed = new List<byte>();
        var chunk = new byte[3000]; rnd.NextBytes(chunk);
        // short matches at short distances (2-byte opcode): random bytes separated by a recurring 6-byte word
        for (int i = 0; i < 300; i++) { var sep = new byte[rnd.Next(1, 30)]; rnd.NextBytes(sep); mixed.AddRange(sep); mixed.AddRange(Encoding.ASCII.GetBytes("sims!")); }
        for (int i = 0; i < 40; i++) { mixed.AddRange(chunk.Take(rnd.Next(5, 3000))); var junk = new byte[rnd.Next(1, 40000)]; rnd.NextBytes(junk); mixed.AddRange(junk.Take(rnd.Next(0, 200))); mixed.AddRange(Enumerable.Repeat((byte)i, rnd.Next(3, 1500))); }
        inputs.Add(mixed.ToArray());
        int k = 0;
        foreach (var inp in inputs)
        {
            var c = RefPack.Compress(inp);
            var d = RefPack.Decompress(c);
            Check(d.SequenceEqual(inp), $"round trip #{k} ({inp.Length} -> {c.Length} bytes)");
            var lit = RefPack.Compress(inp, literalsOnly: true);
            Check(RefPack.Decompress(lit).SequenceEqual(inp), $"literal-only round trip #{k}");
            if (inp.Length > 200)
            {
                var part = RefPack.Decompress(c, 128);
                Check(part.Length >= 128 && part.AsSpan(0, 128).SequenceEqual(inp.AsSpan(0, 128)), $"partial decompress #{k}");
            }
            k++;
        }
        // opcode coverage
        var comp = RefPack.Compress(inputs[^1]);
        var ops = new HashSet<string>();
        int p = 5;
        while (p < comp.Length)
        {
            int b0 = comp[p];
            if (b0 <= 0x7F) { ops.Add("2-byte"); p += 2 + (b0 & 3); }
            else if (b0 <= 0xBF) { ops.Add("3-byte"); p += 3 + ((comp[p + 1] >> 6) & 3); }
            else if (b0 <= 0xDF) { ops.Add("4-byte"); p += 4 + (b0 & 3); }
            else if (b0 <= 0xFB) { ops.Add("literal"); p += 1 + ((b0 & 0x1F) << 2) + 4; }
            else { ops.Add("stop"); break; }
        }
        Check(ops.SetEquals(new[] { "2-byte", "3-byte", "4-byte", "literal", "stop" }), "compressor emitted every opcode form: " + string.Join(",", ops));
        // 4-byte size header (flag 0x80)
        var big = new byte[] { 0x90, 0xFB, 0, 0, 0, 3, 0xFF };
        big = big.Concat(new byte[] { 9, 8, 7 }).ToArray();
        Check(RefPack.Decompress(big).SequenceEqual(new byte[] { 9, 8, 7 }), "flag 0x80 => 4-byte size");
        Throws<InvalidDataException>(() => RefPack.Decompress(new byte[] { 0x10, 0xFB, 0, 0, 50, 0x00, 0x00 }), "back-reference before start throws");
        Throws<InvalidDataException>(() => RefPack.Decompress(new byte[] { 0x10, 0xFB, 0, 1, 0, 0xE0, 1, 2 }), "truncated stream throws");
        Throws<InvalidDataException>(() => RefPack.Decompress(new byte[] { 0x10, 0x00, 0, 0, 1 }), "missing magic throws");
    }

    static void TestDbpf()
    {
        // Two textures: a 4x4 DXT5 stored uncompressed, an 8x4 DXT1 stored RefPack-compressed,
        // a 16x16 A8R8G8B8 stored zlib-compressed, plus a _KEY name map and an unrelated resource.
        var dds5 = MakeDds(4, 4, "DXT5", payload: Dxt5Payload());
        var dds1 = MakeDds(8, 4, "DXT1", payload: Dxt1Payload());
        var argb = new byte[16 * 16 * 4];
        for (int i = 0; i < 256; i++) { argb[i * 4] = (byte)i; argb[i * 4 + 1] = 0x22; argb[i * 4 + 2] = 0x33; argb[i * 4 + 3] = 0xFF; }
        var dds32 = MakeDds(16, 16, null, 32, 0xFF0000, 0xFF00, 0xFF, 0xFF000000, 0x41, argb);
        var key = new MemoryStream();
        var kw = new BinaryWriter(key);
        kw.Write(7u); kw.Write(2u);
        kw.Write(0x1111222233334444UL); kw.Write(9u); kw.Write(Encoding.ASCII.GetBytes("woodfloor"));
        kw.Write(0x5555666677778888UL); kw.Write(10u); kw.Write(Encoding.ASCII.GetBytes("wallpaper1"));
        var res = new List<Sims3Package.NewResource>
        {
            new() { Type = Sims3Package.TextureType, Group = 0, Instance = 0x1111222233334444UL, Data = dds5, Compression = 0 },
            new() { Type = Sims3Package.TextureType, Group = 0, Instance = 0x5555666677778888UL, Data = dds1, Compression = 0xFFFF },
            new() { Type = Sims3Package.TextureType, Group = 0, Instance = 0x0000000000000042UL, Data = dds32, Compression = 0x5A42 },
            new() { Type = Sims3Package.NameMapType, Group = 0, Instance = 0x1, Data = key.ToArray(), Compression = 0xFFFF },
            new() { Type = 0x12345678, Group = 1, Instance = 0x2, Data = new byte[] { 1, 2, 3 }, Compression = 0 },
        };
        foreach (uint flags in new uint[] { 0, 2 })
        {
            var bytes = Sims3Package.Write(res, flags);
            var path = Out($"synthetic_{flags}.package");
            File.WriteAllBytes(path, bytes);
            using var pkg = Sims3Package.Open(path);
            Check(pkg.Resources.Count == 5, $"[flags {flags}] 5 index entries");
            var r1 = pkg.Resources[1];
            Check(r1.Compressed == 0xFFFF && r1.MemSize == dds1.Length && r1.Instance == 0x5555666677778888UL, $"[flags {flags}] entry fields (compression, memsize, 64-bit instance)");
            Check(pkg.Read(r1).SequenceEqual(dds1), $"[flags {flags}] RefPack resource decompresses to the original");
            Check(pkg.Read(pkg.Resources[0]).SequenceEqual(dds5), $"[flags {flags}] uncompressed resource");
            Check(pkg.Read(pkg.Resources[2]).SequenceEqual(dds32), $"[flags {flags}] zlib resource");
            var tex = pkg.ListTextures().ToList();
            Check(tex.Count == 3, $"[flags {flags}] 3 textures listed");
            var t1 = tex.First(t => t.Resource.Instance == 0x5555666677778888UL);
            Check(t1.Width == 8 && t1.Height == 4 && t1.Format == DdsFormat.DXT1 && t1.Name == "wallpaper1", $"[flags {flags}] DXT1 texture info + name ({t1})");
            var t0 = tex.First(t => t.Resource.Instance == 0x1111222233334444UL);
            Check(t0.Name == "woodfloor" && t0.Format == DdsFormat.DXT5, $"[flags {flags}] DXT5 texture info + name ({t0})");
            var t2 = tex.First(t => t.Resource.Instance == 0x42);
            Check(t2.Name == null && t2.Width == 16 && t2.Format == DdsFormat.A8R8G8B8, $"[flags {flags}] unnamed ARGB texture ({t2})");
            var img = pkg.LoadTexture(t1);
            img.GetPixel(0, 0, out var r, out var g, out var b, out _);
            Check(r == 255 && g == 0 && b == 0, $"[flags {flags}] LoadTexture decodes");
            var img2 = pkg.LoadTexture(t2);
            img2.GetPixel(5, 0, out _, out _, out b, out _);
            Check(b == 5, $"[flags {flags}] zlib ARGB texture pixel");
        }
        // Bad files fail with a clear exception.
        var junk = Out("junk.package");
        File.WriteAllBytes(junk, Encoding.ASCII.GetBytes("this is not a package at all, no sir, not at all........................................................."));
        Throws<InvalidDataException>(() => Sims3Package.Open(junk).Dispose(), "non-DBPF file throws InvalidDataException");
        var bad = Sims3Package.Write(res);
        BitConverter.GetBytes(int.MaxValue / 2).CopyTo(bad, 0x2C); // index size beyond file
        Throws<InvalidDataException>(() => Sims3Package.FromBytes(bad), "index outside file throws");
        var bad2 = Sims3Package.Write(res);
        BitConverter.GetBytes(1).CopyTo(bad2, 4); // DBPF 1.x (Sims 2)
        Throws<InvalidDataException>(() => Sims3Package.FromBytes(bad2), "DBPF 1.x rejected");
        // Truncated RefPack payload: ListTextures skips it, Read throws.
        var bad3 = Sims3Package.Write(res);
        using (var pk = Sims3Package.FromBytes(bad3))
        {
            var r = pk.Resources[1];
            // corrupt: shorten the stored size in a copy of the index so the stream is cut off
            Check(pk.ListTextures().Count() == 3, "ListTextures works on intact package");
        }
    }

    static void TestFindPackages()
    {
        var root = Out("fake_sims3");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var bg = Path.Combine(root, "The Sims 3", "GameData", "Shared", "Packages");
        var ep = Path.Combine(root, "The Sims 3 World Adventures", "GameData", "Shared", "Packages");
        var other = Path.Combine(root, "The Sims 3", "Mods", "Packages");
        Directory.CreateDirectory(bg); Directory.CreateDirectory(ep); Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(bg, "FullBuild0.package"), "x");
        File.WriteAllText(Path.Combine(ep, "FullBuild_p01.package"), "x");
        File.WriteAllText(Path.Combine(other, "somemod.package"), "x");
        var found = Sims3Library.FindPackages(root);
        Check(found.Count == 2 && found.Any(f => f.EndsWith("FullBuild0.package")) && found.Any(f => f.EndsWith("FullBuild_p01.package")), $"finds base game + EP GameData packages only ({found.Count})");
        Check(Sims3Library.FindPackages(Path.Combine(root, "does-not-exist")).Count == 0, "missing folder -> empty list");
        Check(Sims3Library.FindPackages(other).Count == 1, "folder without GameData -> all packages below it");
    }

    // ------------------------------------------------------------------ IFF and chunks

    static void TestIffRoundTrip()
    {
        var bytes = File.ReadAllBytes(FrostiFloor);
        var iff = IffFile.Read(bytes);
        Check(iff.Chunks.Count == 7, $"7 chunks read ({string.Join(", ", iff.Chunks)})");
        var again = iff.ToBytes();
        Check(again.SequenceEqual(bytes), $"byte-identical round trip ({again.Length} vs {bytes.Length} bytes)");
        iff.PreserveRsmp = false;
        var noRsmp = iff.ToBytes();
        var re = IffFile.Read(noRsmp);
        Check(re.Chunks.Count == 6 && !re.Chunks.Any(c => c.Type == "rsmp") && noRsmp[60] == 0 && noRsmp[63] == 0, "dropping rsmp writes offset 0");
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(FrostiFloor)!, "*.flr"))
        {
            var b = File.ReadAllBytes(f);
            if (!IffFile.Read(b).ToBytes().SequenceEqual(b)) { Fail($"round trip {Path.GetFileName(f)}"); return; }
        }
        Check(true, "all 12 frosti floors round-trip");
        var poster = File.ReadAllBytes(PosterTemplate);
        Check(IffFile.Read(poster).ToBytes().SequenceEqual(poster), "poster template round-trips");
        Throws<InvalidDataException>(() => IffFile.Read(new byte[100]), "non-IFF throws");
        var trunc = bytes.Take(500).ToArray();
        Throws<InvalidDataException>(() => IffFile.Read(trunc), "truncated IFF throws");
    }

    static void TestStr()
    {
        var iff = IffFile.Load(FrostiFloor);
        var strChunk = iff.Get("STR#", 0)!;
        var s = StrList.Read(strChunk.Data);
        Check(s.GetString(0) == "Chevron Simple Cherry" && s.GetString(1) == "3" && s.GetString(2)!.StartsWith("Made by Frosti"), "reads floor name/price/description");
        Check(s.Write().SequenceEqual(strChunk.Data), "re-writes byte-identically (incl. 0xA3 trailer)");
        var poster = IffFile.Load(PosterTemplate);
        var ctss = StrList.Read(poster.Get("CTSS", 2000)!.Data);
        Check(ctss.GetString(0) == "Betty Yeti Attacks! Movie Poster", "reads CTSS name");
        Check(ctss.GetString(0, 3)!.StartsWith("Affiche"), "per-language strings (French)");
        // Maxis files end with uninitialised padding where FreeSO/we write 0xA3 bytes; everything before it must match.
        var orig = poster.Get("CTSS", 2000)!.Data; var mine = ctss.Write();
        int trailer = ctss.Items.Count;
        Check(mine.Length == orig.Length && mine.AsSpan(0, mine.Length - trailer).SequenceEqual(orig.AsSpan(0, orig.Length - trailer)), "CTSS rewrites identically (apart from the padding trailer)");
    }

    static bool FramesEqualVisible(Spr2Frame a, Spr2Frame b, out string why)
    {
        why = "";
        if (a.Width != b.Width || a.Height != b.Height || a.Flags != b.Flags || a.X != b.X || a.Y != b.Y || a.TransparentIndex != b.TransparentIndex) { why = "header"; return false; }
        for (int i = 0; i < a.Width * a.Height; i++)
        {
            if (a.Alpha[i] != b.Alpha[i]) { why = $"alpha at {i}"; return false; }
            if (a.Alpha[i] == 0) continue;
            if (a.Indices[i] != b.Indices[i]) { why = $"index at {i}"; return false; }
            if (a.HasZ && a.Z[i] != b.Z[i]) { why = $"z at {i}"; return false; }
        }
        return true;
    }

    static void TestSpr2RoundTrip()
    {
        var iff = IffFile.Load(FrostiFloor);
        var pal = Palt.Read(iff.Get("PALT", 1)!.Data).Palette;
        foreach (ushort id in new ushort[] { 513, 257, 1 })
        {
            var spr = Spr2.Read(iff.Get("SPR2", id)!.Data);
            var re = Spr2.Read(spr.Write());
            string why = "";
            bool ok = re.Frames.Count == spr.Frames.Count && FramesEqualVisible(spr.Frames[0], re.Frames[0], out why);
            Check(ok, $"floor SPR2 #{id} {spr.Frames[0].Width}x{spr.Frames[0].Height} re-encodes to the same pixels {why}");
            // Only the (uninitialised) padding bytes of odd-length runs may differ from the Maxis-era encoder.
            var origB = iff.Get("SPR2", id)!.Data; var mineB = spr.Write();
            int diff = 0; for (int i = 0; i < Math.Min(origB.Length, mineB.Length); i++) if (origB[i] != mineB[i]) diff++;
            Check(origB.Length == mineB.Length && diff < 70, $"floor SPR2 #{id} re-encodes to the same length/commands ({diff} padding bytes differ)");
            OnChecker(Zoom(spr.Frames[0].ToImage(pal), 4)).SavePng(Out($"template_floor_{id}.png"));
        }
        var poster = IffFile.Load(PosterTemplate);
        foreach (var c in poster.All("SPR2"))
        {
            var spr = Spr2.Read(c.Data);
            var re = Spr2.Read(spr.Write());
            bool all = re.Frames.Count == spr.Frames.Count;
            string why = "";
            for (int i = 0; all && i < spr.Frames.Count; i++) all = FramesEqualVisible(spr.Frames[i], re.Frames[i], out why);
            Check(all, $"poster SPR2 #{c.Id} ({spr.Frames.Count} frames, flags {spr.Frames[0].Flags}) round-trips {why}");
        }
    }

    static void TestSprRoundTrip()
    {
        var spr = new Spr { PaletteId = 3 };
        var rnd = new Random(5);
        foreach (var (w, h) in new[] { (64, 271), (128, 240), (4, 58), (7, 3) })
        {
            var f = new SprFrame(w, h);
            for (int i = 0; i < w * h; i++)
            {
                int x = i % w, y = i / w;
                f.Opaque[i] = (byte)((x + y) % 11 == 0 || y == 1 ? 0 : 1);
                f.Indices[i] = (byte)(x < w / 2 ? 17 : rnd.Next(256)); // left half runs, right half noise
            }
            if (h > 4) for (int x = 0; x < w; x++) f.Opaque[2 * w + x] = 0; // a fully transparent row
            spr.Frames.Add(f);
        }
        var re = Spr.Read(spr.Write());
        bool ok = re.Frames.Count == spr.Frames.Count && re.PaletteId == 3;
        for (int i = 0; ok && i < spr.Frames.Count; i++)
        {
            var a = spr.Frames[i]; var b = re.Frames[i];
            ok = a.Width == b.Width && a.Height == b.Height && a.Opaque.SequenceEqual(b.Opaque);
            for (int k = 0; ok && k < a.Opaque.Length; k++) if (a.Opaque[k] != 0 && a.Indices[k] != b.Indices[k]) ok = false;
        }
        Check(ok, "SPR# frames round-trip (fills, literals, skips, transparent rows)");
    }

    static void TestQuantize()
    {
        var img = TestPicture(200, 150);
        var pal = Palette.Quantize(img, 256);
        Check(pal.Count <= 256 && pal.Count > 100, $"palette has {pal.Count} colours");
        var idx = pal.Map(img);
        var back = new Image32(img.Width, img.Height);
        for (int i = 0; i < idx.Length; i++) { var (r, g, b) = pal[idx[i]]; back.Rgba[i * 4] = r; back.Rgba[i * 4 + 1] = g; back.Rgba[i * 4 + 2] = b; back.Rgba[i * 4 + 3] = 255; }
        double err = Image32.MeanRgbError(img, back);
        Check(err < 3.0, $"mean per-channel error {err:F2} < 3");
        var pal16 = Palette.Quantize(img, 16, reserveTransparent: true);
        Check(pal16.TransparentIndex == 0 && pal16.Count <= 16 && pal16.Map(img).All(i => i != 0), "reserved transparent index never used for opaque pixels");
        var few = new Image32(4, 1);
        few.SetPixel(0, 0, 1, 2, 3, 255); few.SetPixel(1, 0, 200, 100, 50, 255); few.SetPixel(2, 0, 1, 2, 3, 255); few.SetPixel(3, 0, 9, 9, 9, 0);
        var pf = Palette.Quantize(few);
        Check(pf.Count == 2, "image with 2 opaque colours -> 2 palette entries");
    }

    // ------------------------------------------------------------------ builders

    static void TestFloorMapping()
    {
        var iff = IffFile.Load(FrostiFloor);
        foreach (var (id, n) in new[] { ((ushort)513, 64), ((ushort)257, 32), ((ushort)1, 16) })
        {
            var f = Spr2.Read(iff.Get("SPR2", id)!.Data).Frames[0];
            bool same = f.Width == 2 * n - 1 && f.Height == n;
            for (int y = 0; same && y < f.Height; y++)
                for (int x = 0; x < f.Width; x++)
                    if ((f.Alpha[y * f.Width + x] != 0) != FloorBuilder.InDiamond(n, x, y)) { same = false; break; }
            Check(same, $"diamond coverage of {f.Width}x{f.Height} frame matches InDiamond({n})");
        }
    }

    static Image32 TemplateFbmp() => Bmp.Decode(IffFile.Load(FrostiFloor).Get("FBMP", 1)!.Data);

    static void TestFloorBuild()
    {
        var tmpl = IffFile.Load(FrostiFloor);
        var src = TemplateFbmp();
        Check(src.Width == 64 && src.Height == 64, "template FBMP is 64x64");
        var bytes = FloorBuilder.Build(src, "Test Floor", 7, "A floor built from the frosti source bitmap");
        File.WriteAllBytes(Out("test_floor.flr"), bytes);
        var iff = IffFile.Read(bytes);
        var tPal = Palt.Read(tmpl.Get("PALT", 1)!.Data).Palette;
        var nPal = Palt.Read(iff.Get("PALT", 1)!.Data).Palette;
        Check(iff.Chunks.Select(c => $"{c.Type}{c.Id}").SequenceEqual(new[] { "SPR2513", "SPR2257", "SPR21", "PALT1", "FBMP1", "STR#0" }), "chunk layout matches template");
        Check(iff.All("SPR2").All(c => c.Label == "HR00G00B00" && c.Flags == 0x10) && iff.Get("PALT", 1)!.Flags == 0, "labels/flags match template");
        var strs = StrList.Read(iff.Get("STR#", 0)!.Data);
        Check(strs.GetString(0) == "Test Floor" && strs.GetString(1) == "7" && strs.GetString(2)!.StartsWith("A floor"), "STR# name/price/description");
        Check(int.TryParse(strs.GetString(1), out _), "price parses as int (FreeSO int.Parse)");
        double[] limits = { 4, 14, 14 };
        int k = 0;
        foreach (ushort id in new ushort[] { 513, 257, 1 })
        {
            var tf = Spr2.Read(tmpl.Get("SPR2", id)!.Data).Frames[0];
            var nf = Spr2.Read(iff.Get("SPR2", id)!.Data).Frames[0];
            Check(nf.Width == tf.Width && nf.Height == tf.Height && nf.Flags == tf.Flags && nf.PaletteId == tf.PaletteId && nf.TransparentIndex == tf.TransparentIndex && nf.X == tf.X && nf.Y == tf.Y,
                $"SPR2 #{id} frame header matches template ({nf.Width}x{nf.Height} flags {nf.Flags} pal 0x{nf.PaletteId:X4})");
            bool cover = true;
            for (int i = 0; i < tf.Alpha.Length; i++) if ((tf.Alpha[i] == 0) != (nf.Alpha[i] == 0)) cover = false;
            Check(cover, $"SPR2 #{id} covers exactly the template's pixels");
            var ti = tf.ToImage(tPal); var ni = nf.ToImage(nPal);
            double err = Image32.MeanRgbError(ti, ni);
            Check(err < limits[k], $"SPR2 #{id} mean colour error vs template {err:F2} < {limits[k]}");
            OnChecker(Zoom(ni, 4)).SavePng(Out($"floor_built_{id}.png"));
            k++;
        }
        // A tiled preview (how it looks in game after FreeSO's FloorCopy fill) for a Sims-3-like texture.
        var tex = MakeWoodTexture(512);
        tex.SavePng(Out("floor_source_wood.png"));
        var wood = IffFile.Read(FloorBuilder.Build(tex, "Wood", 10, "test"));
        var wn = Spr2.Read(wood.Get("SPR2", 513)!.Data).Frames[0].ToImage(Palt.Read(wood.Get("PALT", 1)!.Data).Palette);
        TiledFloorPreview(wn).SavePng(Out("floor_wood_tiled_near.png"));
        TiledFloorPreview(Spr2.Read(tmpl.Get("SPR2", 513)!.Data).Frames[0].ToImage(tPal)).SavePng(Out("floor_template_tiled_near.png"));
    }

    static Image32 MakeWoodTexture(int n)
    {
        var img = new Image32(n, n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int plank = x / (n / 4);
                double grain = Math.Sin((y + plank * 37) * 0.09 + Math.Sin(x * 0.05) * 2) * 0.5 + 0.5;
                bool seam = x % (n / 4) < 3 || ((y + plank * n / 3) % n) < 3;
                double baseR = 150 + plank * 12, baseG = 95 + plank * 6, baseB = 50;
                double f = seam ? 0.45 : 0.8 + 0.2 * grain;
                img.SetPixel(x, y, Image32.ClampByte(baseR * f), Image32.ClampByte(baseG * f), Image32.ClampByte(baseB * f), 255);
            }
        return img;
    }

    /// <summary>Lays diamonds out in the isometric grid (3x3 tiles) like the game does.</summary>
    static Image32 TiledFloorPreview(Image32 diamond)
    {
        int n = diamond.Height; // 64
        var canvas = new Image32(n * 2 * 3 + 2, n * 3 + 2);
        canvas.Fill(40, 40, 40, 255);
        for (int ty = 0; ty < 3; ty++)
            for (int tx = 0; tx < 3; tx++)
            {
                int ox = (tx - ty) * n + n * 2, oy = (tx + ty) * n / 2;
                for (int y = 0; y < diamond.Height; y++)
                    for (int x = 0; x < diamond.Width; x++)
                    {
                        diamond.GetPixel(x, y, out var r, out var g, out var b, out var a);
                        if (a == 0) continue;
                        int cx = ox + x, cy = oy + y;
                        if (cx >= 0 && cy >= 0 && cx < canvas.Width && cy < canvas.Height) canvas.SetPixel(cx, cy, r, g, b, 255);
                    }
            }
        return Zoom(canvas, 2);
    }

    static Image32 MakeBrickTexture(int w, int h)
    {
        var img = new Image32(w, h);
        int bh = h / 8, bw = w / 2;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int row = y / bh;
                int xx = (x + (row % 2) * bw / 2) % w;
                bool mortar = y % bh < 2 || xx % bw < 2;
                double n = ((x * 7919 + y * 104729) % 23) / 23.0;
                if (mortar) img.SetPixel(x, y, 200, 195, 185, 255);
                else img.SetPixel(x, y, Image32.ClampByte(150 + 30 * n + row * 3), Image32.ClampByte(60 + 15 * n), Image32.ClampByte(45 + 10 * n), 255);
            }
        // a coloured band at the bottom (to check the floor anchoring of repeats)
        for (int y = h - h / 16; y < h; y++) for (int x = 0; x < w; x++) img.SetPixel(x, y, 60, 90, 160, 255);
        return img;
    }

    static void TestWallBuild()
    {
        var tex = MakeBrickTexture(256, 256);
        tex.SavePng(Out("wall_source_brick.png"));
        var bytes = WallBuilder.Build(tex, "Test Brick", 12, "Bricks for testing");
        File.WriteAllBytes(Out("test_wall.wll"), bytes);
        var iff = IffFile.Read(bytes);
        var strs = StrList.Read(iff.Get("STR#", 0)!.Data);
        Check(strs.GetString(0) == "Test Brick" && strs.GetString(1) == "12" && strs.GetString(2) == "Bricks for testing", "STR# #0 name/price/description");
        var paltChunk = iff.Get("PALT", WallBuilder.PaletteId);
        Check(paltChunk != null, "PALT #1 present");
        var pal = Palt.Read(paltChunk!.Data).Palette;
        var expect = new (ushort id, (int W, int H)[] sizes, string zoom)[]
        {
            (WallBuilder.FarSpriteId, WallBuilder.FarFrames, "far"),
            (WallBuilder.MediumSpriteId, WallBuilder.MediumFrames, "medium"),
            (WallBuilder.NearSpriteId, WallBuilder.NearFrames, "near"),
        };
        foreach (var (id, sizes, zoom) in expect)
        {
            var c = iff.Get("SPR#", id);
            Check(c != null, $"SPR# #{id} ({zoom}) present");
            if (c == null) continue;
            var spr = Spr.Read(c.Data);
            Check(spr.PaletteId == WallBuilder.PaletteId && spr.Frames.Count == 4, $"SPR# #{id}: 4 frames, palette {spr.PaletteId}");
            for (int i = 0; i < Math.Min(4, spr.Frames.Count); i++)
            {
                var f = spr.Frames[i];
                Check(f.Width == sizes[i].W && f.Height == sizes[i].H, $"SPR# #{id} frame {i} is {f.Width}x{f.Height} (expected {sizes[i].W}x{sizes[i].H})");
                Check(f.Opaque.All(o => o != 0), $"SPR# #{id} frame {i} fully filled");
                Zoom(f.ToImage(pal), zoom == "near" ? 2 : zoom == "medium" ? 4 : 8).SavePng(Out($"wall_{zoom}_frame{i}.png"));
            }
        }
        var near = Spr.Read(iff.Get("SPR#", WallBuilder.NearSpriteId)!.Data);
        OnChecker(Zoom(WallBuilder.PreviewCorner(near, pal), 2)).SavePng(Out("wall_near_corner_preview.png"));
        // Thumbnail FreeSO uses: medium frame 2.
        var thumb = Spr.Read(iff.Get("SPR#", WallBuilder.MediumSpriteId)!.Data).Frames[2];
        Check(thumb.Width == 64 && thumb.Height == 120, "catalog thumbnail (medium frame 2) is 64x120");
        // Tall Sims 3 style texture: one stretched repeat.
        var tall = WallBuilder.BuildIff(MakeBrickTexture(128, 512), "Tall", 1, "");
        Check(Spr.Read(tall.Get("SPR#", WallBuilder.NearSpriteId)!.Data).Frames.Count == 4, "tall texture builds");
    }

    static void TestPictureBuild()
    {
        var template = File.ReadAllBytes(PosterTemplate);
        var tIff = IffFile.Read(template);
        var targets = PictureBuilder.FindTargets(tIff);
        Console.WriteLine("  targets: " + string.Join("; ", targets));
        Check(targets.Count == 6 && targets.Count(t => t.Flip) == 3 && targets.All(t => t.SpriteId == 200), "finds 3 front frames (+3 flipped uses) in wall SPR2 #200");
        var pic = TestPicture(300, 400);
        pic.SavePng(Out("picture_source.png"));
        var bytes = PictureBuilder.Build(pic, "Test Poster", 123, "A poster made by the tests", template);
        File.WriteAllBytes(Out("test_picture.iff"), bytes);
        var iff = IffFile.Read(bytes);
        var objds = iff.All("OBJD").ToList();
        Check(objds.Count == 1, $"one OBJD left (others pruned): {objds.Count}");
        var o = new Objd(objds[0].Data);
        var tO = new Objd(tIff.All("OBJD").First().Data);
        Check(o.Guid != tO.Guid && tIff.All("OBJD").All(x => new Objd(x.Data).Guid != o.Guid), $"fresh GUID 0x{o.Guid:X8} (template 0x{tO.Guid:X8})");
        Check(o.Guid == PictureBuilder.GuidFor("Test Poster"), "GUID is deterministic from the name");
        Check(o.Price == 123 && objds[0].Label == "Test Poster", "price + OBJD label");
        Check(objds[0].Data.Length == tIff.All("OBJD").First().Data.Length, "OBJD size unchanged");
        // only GUID/price changed in OBJD
        int diffs = 0; var td = tIff.All("OBJD").First().Data; var nd = objds[0].Data;
        for (int i = 0; i < td.Length; i++) if (td[i] != nd[i]) diffs++;
        Check(diffs <= 6, $"OBJD: only GUID/price bytes differ ({diffs} bytes)");
        var ctss = StrList.Read(iff.Get("CTSS", o.CatalogStringsId)!.Data);
        Check(ctss.GetString(0) == "Test Poster" && ctss.GetString(1) == "A poster made by the tests" && ctss.GetString(0, 3) == "Test Poster", "CTSS name/description (all languages)");
        Check(!iff.All("CTSS").Any(c => c.Id != o.CatalogStringsId), "other objects' CTSS removed");
        var bmp = iff.Get("BMP_", o.CatalogStringsId);
        Check(bmp != null && Bmp.TryReadSize(bmp.Data, out var bw, out var bh) && bw == 74 && bh == 37, "catalog thumbnail regenerated at template size");
        if (bmp != null) Zoom(Bmp.Decode(bmp.Data), 4).SavePng(Out("picture_catalog_thumb.png"));

        var sprC = iff.Get("SPR2", 200)!;
        var spr = Spr2.Read(sprC.Data);
        var pal = Palt.Read(iff.Get("PALT", (ushort)spr.DefaultPaletteId)!.Data).Palette;
        Check(spr.Frames.Count == 9, $"SPR2 #200 has 6 + 3 mirrored frames ({spr.Frames.Count})");
        var tSpr = Spr2.Read(tIff.Get("SPR2", 200)!.Data);
        bool alphaSame = true;
        for (int i = 0; i < 6; i++) if (!tSpr.Frames[i].Alpha.SequenceEqual(spr.Frames[i].Alpha)) alphaSame = false;
        Check(alphaSame, "alpha channel of every original frame preserved");
        bool zSame = true;
        for (int i = 0; i < 6; i++)
            for (int k = 0; k < tSpr.Frames[i].Z.Length; k++)
                if (tSpr.Frames[i].Alpha[k] > 0 && tSpr.Frames[i].Z[k] != spr.Frames[i].Z[k]) zSame = false;
        Check(zSame, "z-buffer preserved");
        // back views look the same as before (re-quantized only)
        var tPal = Palt.Read(tIff.Get("PALT", (ushort)tSpr.DefaultPaletteId)!.Data).Palette;
        double backErr = Image32.MeanRgbError(tSpr.Frames[0].ToImage(tPal), spr.Frames[0].ToImage(pal));
        Check(backErr < 4, $"back view unchanged apart from palette ({backErr:F2})");
        // front view actually changed
        double frontDiff = Image32.MeanRgbError(tSpr.Frames[1].ToImage(tPal), spr.Frames[1].ToImage(pal));
        Check(frontDiff > 10, $"front view repainted (diff {frontDiff:F1})");
        // mirrored copy, once flipped, equals the normal front (picture reads correctly in both directions)
        var front = spr.Frames[1].ToImage(pal);
        var wallDg = Dgrp.Read(iff.Get("DGRP", (ushort)(o.BaseGraphicId + 1))!.Data);
        int mirIdx = (int)wallDg.Images.First(i => i.Direction == 0x10 && i.Zoom == 3).Sprites[0].FrameIndex;
        var mirrored = spr.Frames[mirIdx].ToImage(pal);
        Check(mirIdx >= 6 && spr.Frames[mirIdx].Width == 59 && spr.Frames[mirIdx].X == spr.Frames[1].X && spr.Frames[mirIdx].Y == spr.Frames[1].Y, $"near mirrored copy (frame {mirIdx}) keeps frame size/position");
        // check the red dot (top-right of the picture) lands top-right in the front view and, after the
        // game's flip, top-right in the mirrored view as well
        var geo = PictureBuilder.Geometry.Fit(spr.Frames[1]);
        var (dx, dy) = geo.At(0.75, 0.25);
        (double r, double g, double b) Avg(Image32 im, double cx, double cy)
        {
            double r = 0, g = 0, b = 0; int n = 0;
            for (int yy = -2; yy <= 2; yy++) for (int xx = -2; xx <= 2; xx++)
                { im.GetPixel(Math.Clamp((int)cx + xx, 0, im.Width - 1), Math.Clamp((int)cy + yy, 0, im.Height - 1), out var rr, out var gg, out var bb, out _); r += rr; g += gg; b += bb; n++; }
            return (r / n, g / n, b / n);
        }
        var cFront = Avg(front, dx, dy);
        Check(cFront.r > 150 && cFront.g < 120, $"red dot top-right in front view ({cFront.r:F0},{cFront.g:F0},{cFront.b:F0})");
        // displayed mirrored view = flip(stored copy); the stored copy has the picture mirrored, so the dot is top-LEFT in storage
        var (mx, my) = geo.At(0.25, 0.25);
        var cMir = Avg(mirrored, mx, my);
        Check(cMir.r > 150 && cMir.g < 120, $"mirrored copy has the dot top-left so it shows top-right after the flip ({cMir.r:F0},{cMir.g:F0},{cMir.b:F0})");
        // DGRP: flipped front directions point at the new frames
        var wallDgrp = Dgrp.Read(iff.Get("DGRP", (ushort)(o.BaseGraphicId + 1))!.Data);
        var flippedFront = wallDgrp.Images.Where(i => i.IsFront).SelectMany(i => i.Sprites.Where(s => s.Flip)).ToList();
        Check(flippedFront.Count == 3 && flippedFront.All(s => s.FrameIndex >= 6), "flipped front DGRP entries use mirrored frames");
        var normalFront = wallDgrp.Images.Where(i => i.IsFront).SelectMany(i => i.Sprites.Where(s => !s.Flip)).ToList();
        Check(normalFront.All(s => s.FrameIndex == 1 || s.FrameIndex == 3 || s.FrameIndex == 5), "unflipped front entries unchanged");
        Check(iff.All("PALT").Any(p => p.Id == spr.DefaultPaletteId) && iff.Get("PALT", 0) == null, "new PALT added, old unused PALT removed");
        Check(iff.All("SPR2").Select(c => (int)c.Id).OrderBy(x => x).SequenceEqual(new[] { 100, 200 }), "other objects' sprites pruned (left: " + string.Join(",", iff.All("SPR2").Select(c => c.Id)) + ")");
        Check(iff.All("DGRP").Select(c => (int)c.Id).OrderBy(x => x).SequenceEqual(new[] { 100, 101 }), "only the object's own DGRPs remain");
        Check(bytes.Length < template.Length, $"smaller than template ({bytes.Length} < {template.Length})");
        // Every remaining SPR2 decodes and references an existing PALT.
        bool allOk = iff.All("SPR2").All(c => { var s = Spr2.Read(c.Data); return iff.Get("PALT", (ushort)s.DefaultPaletteId) != null; });
        Check(allOk, "all SPR2 decode and reference an existing PALT");
        // Every DGRP sprite reference of the kept object resolves.
        bool refsOk = true;
        for (int gi = o.BaseGraphicId; gi < o.BaseGraphicId + o.NumGraphics; gi++)
        {
            var g = Dgrp.Read(iff.Get("DGRP", (ushort)gi)!.Data);
            foreach (var img in g.Images) foreach (var s in img.Sprites)
                { var sc = iff.Get("SPR2", (ushort)s.SpriteId); if (sc == null || s.FrameIndex >= Spr2.Read(sc.Data).Frames.Count) refsOk = false; }
        }
        Check(refsOk, "kept object's DGRP references all resolve");
        // Deterministic output
        Check(PictureBuilder.Build(pic, "Test Poster", 123, "A poster made by the tests", template).SequenceEqual(bytes), "building twice gives identical bytes");
        Check(new Objd(IffFile.Read(PictureBuilder.Build(pic, "Other Poster", 5, "", template)).All("OBJD").First().Data).Guid != o.Guid, "different name -> different GUID");

        // previews
        for (int i = 0; i < spr.Frames.Count; i++)
        {
            var f = spr.Frames[i];
            var im = f.ToImage(pal);
            OnChecker(Zoom(im, 4)).SavePng(Out($"picture_frame{i}.png"));
        }
        // in-game look of the 4 directions at near zoom (front views; flipped ones flipped)
        var nearFront = spr.Frames[1].ToImage(pal);
        var nearMirDisplayed = FlipX(spr.Frames[mirIdx].ToImage(pal));
        var both = new Image32(nearFront.Width * 2 + 10, nearFront.Height);
        Blit(both, nearFront, 0, 0); Blit(both, nearMirDisplayed, nearFront.Width + 10, 0);
        OnChecker(Zoom(both, 4)).SavePng(Out("picture_near_both_walls.png"));
        var tBoth = new Image32(nearFront.Width * 2 + 10, nearFront.Height);
        Blit(tBoth, tSpr.Frames[1].ToImage(tPal), 0, 0); Blit(tBoth, FlipX(tSpr.Frames[1].ToImage(tPal)), nearFront.Width + 10, 0);
        OnChecker(Zoom(tBoth, 4)).SavePng(Out("picture_template_near_both_walls.png"));

        // Contain mode and no-mirror-fix option
        var contain = IffFile.Read(PictureBuilder.Build(pic, "Contain Poster", 50, "", template, new PictureBuilder.Options { Fit = PictureBuilder.FitMode.Contain, FixMirroredViews = false }));
        var cs = Spr2.Read(contain.Get("SPR2", 200)!.Data);
        Check(cs.Frames.Count == 6, "FixMirroredViews=false keeps 6 frames");
        OnChecker(Zoom(cs.Frames[1].ToImage(Palt.Read(contain.Get("PALT", (ushort)cs.DefaultPaletteId)!.Data).Palette), 4)).SavePng(Out("picture_contain_frame1.png"));
        // A landscape photo-like picture into another object of the template
        var land = MakeWoodTexture(400).Resize(400, 250);
        var other = IffFile.Read(PictureBuilder.Build(land, "Landscape", 80, "", template, new PictureBuilder.Options { ObjectId = 16843 }));
        Check(other.All("OBJD").Single().Id == 16843, "ObjectId option picks Painting3");
    }

    static void TestPictureAnimated()
    {
        if (!File.Exists(AnimatedTemplate)) { Check(true, "(animated template not present, skipped)"); return; }
        var template = File.ReadAllBytes(AnimatedTemplate);
        var targets = PictureBuilder.FindTargets(IffFile.Read(template));
        Console.WriteLine($"  animated targets: {targets.Count} ({string.Join("; ", targets.Take(6))}...)");
        try
        {
            var bytes = PictureBuilder.Build(TestPicture(200, 260), "Animated Test", 99, "x", template);
            var iff = IffFile.Read(bytes);
            Check(iff.All("OBJD").Count() == 1, "animated template clone has one OBJD");
            File.WriteAllBytes(Out("test_picture_animated.iff"), bytes);
        }
        catch (InvalidDataException e)
        {
            Check(true, $"animated template rejected cleanly: {e.Message}");
        }
    }

    static Image32 FlipX(Image32 img)
    {
        var o = new Image32(img.Width, img.Height);
        for (int y = 0; y < img.Height; y++) for (int x = 0; x < img.Width; x++)
            { img.GetPixel(x, y, out var r, out var g, out var b, out var a); o.SetPixel(img.Width - 1 - x, y, r, g, b, a); }
        return o;
    }

    static void Blit(Image32 dst, Image32 src, int ox, int oy)
    {
        for (int y = 0; y < src.Height; y++) for (int x = 0; x < src.Width; x++)
            { src.GetPixel(x, y, out var r, out var g, out var b, out var a); if (ox + x < dst.Width && oy + y < dst.Height) dst.SetPixel(ox + x, oy + y, r, g, b, a); }
    }

    static void TestCreatorPack()
    {
        var folder = Out("pack");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        var pack = CreatorPack.Open(folder);
        var floor = FloorBuilder.Build(TemplateFbmp(), "Cherry Copy", 3, "copy");
        pack.Add("floor", "Cherry Copy", 3, "copy", "FullBuild0.package#0x00B2D882:0x00000000:0x1234", floor);
        pack.Add("wall", "Brick Wall!", 5, "bricks", "test", WallBuilder.Build(MakeBrickTexture(64, 64), "Brick Wall!", 5, "bricks"));
        pack.Add("picture", "Poster One", 30, "", "test.png", PictureBuilder.Build(TestPicture(64, 64), "Poster One", 30, "", File.ReadAllBytes(PosterTemplate)));
        pack.Add("floor", "Cherry Copy", 4, "copy again", "x", floor); // re-export replaces
        pack.Save();
        var manifest = CreatorPack.ReadManifest(Path.Combine(folder, "manifest.txt"));
        Check(manifest.SequenceEqual(new[] { "Downloads/Floors/redux_cherry_copy.flr", "Downloads/Objects/redux_poster_one.iff", "Downloads/Walls/redux_brick_wall.wll" }), "manifest lists files: " + string.Join(", ", manifest));
        Check(File.ReadAllLines(Path.Combine(folder, "manifest.txt"))[0].StartsWith("#"), "manifest has a comment header");
        Check(manifest.All(m => File.Exists(Path.Combine(folder, m))), "all manifest files exist");
        var re = CreatorPack.Open(folder);
        Check(re.Items.Count == 3 && re.Items.First(i => i.Kind == "floor").Price == 4 && re.Items.First(i => i.Kind == "floor").Source == "x" && re.Items.First(i => i.Kind == "floor").File == "Downloads/Floors/redux_cherry_copy.flr", "creator.json reloads (re-export replaced the item)");
        var json = File.ReadAllText(Path.Combine(folder, "creator.json"));
        Check(json.Contains("\"kind\"") && json.Contains("\"source\"") && json.Contains("\"file\""), "creator.json has kind/name/price/file/source");
        re.Remove(re.Items.First(i => i.Kind == "wall"));
        re.Save();
        Check(CreatorPack.ReadManifest(Path.Combine(folder, "manifest.txt")).Count == 2 && !File.Exists(Path.Combine(folder, "Downloads/Walls/redux_brick_wall.wll")), "remove deletes file + manifest line");
    }
}
