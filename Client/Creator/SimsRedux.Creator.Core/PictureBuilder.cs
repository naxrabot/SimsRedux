using System.Text;

namespace SimsRedux.Creator;

/// <summary>
/// Builds Sims 1 wall pictures/posters by cloning an existing wall-picture object IFF and painting a new
/// picture into its sprites.
///
/// SimsRedux: how a Maxis wall picture works (studied on PaintingPosterSuperstar.iff):
/// * The IFF holds several objects (4 OBJDs "Poster - Painting1..4"). Each OBJD has BaseGraphicID and
///   NumGraphics = 2: DGRP base+0 "Ground" (the rolled-up poster) and base+1 "Wall" (hung on a wall).
/// * A DGRP has 12 images = 4 directions x 3 zooms (zoom 1 far, 2 medium, 3 near). Directions are flags:
///   0x04 RightFront, 0x10 LeftFront, 0x01 RightBack, 0x40 LeftBack. The wall DGRP points at one SPR2
///   with 6 frames: [near back, near front, medium back, medium front, far back, far front]. "Front"
///   directions use the front frame (0x04 as is, 0x10 horizontally flipped); "back" directions show the
///   blank back of the poster. Frames are 7-flag sprites (colour + z-buffer + alpha) whose silhouette is
///   the poster as a parallelogram sheared along the wall's 2:1 slope (near front frame is 59x117).
/// * The SPR2 references a PALT through its default palette id; OBJD field 16 is the price, fields
///   12-13 the GUID, field 39 the catalog strings id = CTSS id (string 0 = name, 1 = description,
///   repeated per language) and also the id of the catalog thumbnail BMP_ (+2000/+4000 for the
///   smaller speech-bubble icons).
///
/// What we do:
/// 1. Pick the object (first OBJD unless told otherwise, plus its multi-tile siblings), find its wall
///    DGRP(s) and every (sprite, frame, flip) used by a front-facing direction.
/// 2. For each such frame, fit the poster's parallelogram from the alpha channel (top and bottom edges
///    are straight lines; sides are vertical), detect the outer frame/border thickness from the template
///    (colour run from the edge inward, measured on the near frame) and paint the new picture into the
///    inner region with 4x4 supersampling. Isometric projection is affine, so this mapping is exact for
///    a flat picture on a wall. Alpha and z-buffer are kept from the template, so depth/edges stay right.
/// 3. Maxis reused one front frame flipped for the second wall direction, which would mirror our picture
///    (text would read backwards). With FixMirroredViews we append extra frames holding the mirrored
///    picture and point the flipped DGRP entries at them, so after the flip the picture reads correctly.
/// 4. All frames of the sprite (including the unchanged back views) are re-quantized into one new PALT.
/// 5. The OBJD gets a deterministic GUID derived from the name (re-exports keep the same GUID), the new
///    price and label; the CTSS gets the new name/description in every language; the catalog BMP_
///    thumbnails are regenerated; the template's other objects are removed (otherwise the clone would
///    duplicate them in the catalog with their original GUIDs).
/// </summary>
public static class PictureBuilder
{
    /// <summary>How the picture fills the template's picture area.</summary>
    public enum FitMode { Cover, Contain, Stretch }

    /// <summary>On-screen width of one tile along a wall at near zoom (horizontal pixels).</summary>
    public const double NearTileWidthPx = 64.0;
    /// <summary>Pixels per world unit vertically at near zoom (240px wall / 2.95 units).</summary>
    public const double NearPxPerUnitHeight = 240.0 / 2.95;

    public sealed class Options
    {
        /// <summary>Chunk id of the OBJD to clone (null = the first OBJD in the file).</summary>
        public ushort? ObjectId { get; set; }
        public FitMode Fit { get; set; } = FitMode.Cover;
        public bool FixMirroredViews { get; set; } = true;
        /// <summary>Remove the template's other objects (recommended; they would keep their original GUIDs).</summary>
        public bool PruneOtherObjects { get; set; } = true;
        public bool Dither { get; set; }
        /// <summary>Override the detected border (fraction of the poster width/height per side), or null to auto-detect.</summary>
        public double? BorderFraction { get; set; }
        /// <summary>Background for <see cref="FitMode.Contain"/> letterboxing.</summary>
        public (byte r, byte g, byte b) MatColor { get; set; } = (238, 236, 228);
    }

    /// <summary>Result of analysing a template (exposed for tests and the app's preview).</summary>
    public sealed class Target
    {
        public ushort SpriteId; public int Frame; public bool Flip; public uint Zoom;
        public override string ToString() => $"SPR2 #{SpriteId} frame {Frame}{(Flip ? " (flipped)" : "")} zoom {Zoom}";
    }

    public static byte[] Build(Image32 picture, string name, int price, string description, byte[] templateIff)
        => BuildIff(picture, name, price, description, templateIff, new Options()).ToBytes();

    public static byte[] Build(Image32 picture, string name, int price, string description, byte[] templateIff, Options options)
        => BuildIff(picture, name, price, description, templateIff, options).ToBytes();

    /// <summary>Deterministic object GUID for a creator item (FNV-1a of the normalised name).</summary>
    public static uint GuidFor(string name, int index = 0)
    {
        var key = "SimsRedux.Creator:picture:" + (name ?? "").Trim().ToLowerInvariant() + ":" + index;
        uint h = 2166136261;
        foreach (var b in Encoding.UTF8.GetBytes(key)) { h ^= b; h *= 16777619; }
        return h == 0 ? 1u : h;
    }

    /// <summary>Lists the front-facing wall sprite frames of the chosen object in a template.</summary>
    public static List<Target> FindTargets(IffFile iff, ushort? objectId = null)
    {
        var (_, kept) = ChooseObjects(iff, objectId);
        return FindTargets(iff, kept);
    }

    private static (IffChunk main, List<IffChunk> kept) ChooseObjects(IffFile iff, ushort? objectId)
    {
        var objds = iff.All("OBJD").ToList();
        if (objds.Count == 0) throw new InvalidDataException("Template has no OBJD (not an object file).");
        var main = objectId != null ? objds.FirstOrDefault(o => o.Id == objectId) : objds[0];
        if (main == null) throw new ArgumentException($"Template has no OBJD #{objectId}.");
        var mo = new Objd(main.Data);
        var kept = new List<IffChunk> { main };
        if (mo.MasterId != 0)
            kept.AddRange(objds.Where(o => o != main && new Objd(o.Data).MasterId == mo.MasterId));
        return (main, kept);
    }

    private static IEnumerable<IffChunk> DgrpsFor(IffFile iff, Objd o)
    {
        var inRange = iff.All("DGRP").Where(g => g.Id >= o.BaseGraphicId && g.Id < o.BaseGraphicId + Math.Max((int)o.NumGraphics, 1)).ToList();
        // SimsRedux: "Wall" DGRPs are the hung-on-wall graphics; fall back to every base graphic.
        var walls = inRange.Where(g => g.Label.Contains("wall", StringComparison.OrdinalIgnoreCase)).ToList();
        return walls.Count > 0 ? walls : inRange;
    }

    private static List<Target> FindTargets(IffFile iff, List<IffChunk> kept)
    {
        var res = new List<Target>();
        foreach (var oc in kept)
        {
            var o = new Objd(oc.Data);
            foreach (var gc in DgrpsFor(iff, o))
            {
                var g = Dgrp.Read(gc.Data);
                foreach (var img in g.Images.Where(i => i.IsFront))
                    foreach (var s in img.Sprites)
                    {
                        var sc = iff.Get("SPR2", (ushort)s.SpriteId);
                        if (sc == null) continue;
                        Spr2 sp;
                        try { sp = Spr2.Read(sc.Data); } catch (Exception) { continue; }
                        if (s.FrameIndex >= sp.Frames.Count || sp.Frames[(int)s.FrameIndex].Width == 0) continue;
                        if (!res.Any(t => t.SpriteId == s.SpriteId && t.Frame == s.FrameIndex && t.Flip == s.Flip))
                            res.Add(new Target { SpriteId = (ushort)s.SpriteId, Frame = (int)s.FrameIndex, Flip = s.Flip, Zoom = img.Zoom });
                    }
            }
        }
        return res;
    }

    public static IffFile BuildIff(Image32 picture, string name, int price, string description, byte[] templateIff, Options? options = null)
    {
        options ??= new Options();
        if (picture == null) throw new ArgumentNullException(nameof(picture));
        if (templateIff == null) throw new ArgumentNullException(nameof(templateIff));
        if (price < 0 || price > 65535) throw new ArgumentOutOfRangeException(nameof(price), "Object prices must be 0..65535.");
        var iff = IffFile.Read(templateIff);
        iff.PreserveRsmp = false; // the resource map would be stale after our edits
        var (main, kept) = ChooseObjects(iff, options.ObjectId);
        var targets = FindTargets(iff, kept);
        if (targets.Count == 0) throw new InvalidDataException("Template has no front-facing wall sprite to paint into.");
        var templateGuids = iff.All("OBJD").Select(o => new Objd(o.Data).Guid).ToHashSet();

        // Flatten alpha in the source picture (posters are opaque).
        var pic = FloorBuilder.Flatten(picture);

        var replacedPalts = new HashSet<uint>();
        foreach (var group in targets.GroupBy(t => t.SpriteId))
        {
            var sprChunk = iff.Get("SPR2", group.Key)!;
            var spr = Spr2.Read(sprChunk.Data);
            var oldPal = iff.Get("PALT", (ushort)spr.DefaultPaletteId) is { } pc ? Palt.Read(pc.Data).Palette : new Palette();
            // SimsRedux: empty (0x0) frames get a 1x1 stand-in image; they're skipped when writing back.
            var images = spr.Frames.Select(f => f.ToImage(oldPal)).ToList();
            var originals = images.Select(i => i.Clone()).ToList();

            // Border detection on the largest (near) front frame, shared by all zooms.
            if (group.All(t => t.Frame >= spr.Frames.Count)) continue;
            var nearT = group.Where(t => t.Frame < spr.Frames.Count).OrderByDescending(t => spr.Frames[t.Frame].Width * spr.Frames[t.Frame].Height).First();
            var nearGeo = Geometry.Fit(spr.Frames[nearT.Frame]);
            var border = options.BorderFraction is double bf ? new Border(bf, bf, bf, bf) : DetectBorder(images[nearT.Frame], nearGeo);

            var frameMap = new Dictionary<(int frame, bool flip), int>();
            var paintedNormal = new HashSet<int>();
            foreach (var t in group.OrderBy(t => t.Flip))
            {
                if (spr.Frames[t.Frame].Width == 0 || spr.Frames[t.Frame].Height == 0) continue;
                bool mirror = t.Flip && options.FixMirroredViews;
                int dest = t.Frame;
                bool usedUnflipped = group.Any(o => o.Frame == t.Frame && !o.Flip);
                if (mirror && usedUnflipped)
                {
                    // Same frame is also shown unflipped: give the flipped view its own copy.
                    var copy = spr.Frames[t.Frame].Clone();
                    spr.Frames.Add(copy);
                    images.Add(originals[t.Frame].Clone());
                    dest = spr.Frames.Count - 1;
                }
                else if (!mirror && paintedNormal.Contains(t.Frame))
                {
                    frameMap[(t.Frame, t.Flip)] = t.Frame;
                    continue;
                }
                var geo = Geometry.Fit(spr.Frames[t.Frame]);
                PaintPicture(images[dest], spr.Frames[dest], geo, border, pic, mirror, options);
                if (!mirror) paintedNormal.Add(t.Frame);
                frameMap[(t.Frame, t.Flip)] = dest;
            }

            // New palette for the whole sprite (index 0 reserved for transparency).
            var newPal = Palette.Quantize(images, 256, reserveTransparent: true);
            replacedPalts.Add(spr.DefaultPaletteId);
            ushort newPaltId = NextFreeId(iff, "PALT");
            spr.DefaultPaletteId = newPaltId;
            for (int i = 0; i < spr.Frames.Count; i++)
            {
                var f = spr.Frames[i];
                f.PaletteId = newPaltId;
                if (f.Width == 0 || f.Height == 0) continue; // empty placeholder frames stay as they are
                f.TransparentIndex = 0;
                f.SetFromImage(images[i], newPal, options.Dither);
            }
            sprChunk.Data = spr.Write();
            iff.Chunks.Add(new IffChunk("PALT", newPaltId, Palt.FromPalette(newPal).Write(), "", 0x00));

            // Point flipped front views at their mirrored copies.
            if (options.FixMirroredViews)
            {
                foreach (var oc in kept)
                    foreach (var gc in DgrpsFor(iff, new Objd(oc.Data)))
                    {
                        var g = Dgrp.Read(gc.Data);
                        bool changed = false;
                        foreach (var img in g.Images.Where(i => i.IsFront))
                            foreach (var s in img.Sprites.Where(s => s.SpriteId == group.Key && s.Flip))
                                if (frameMap.TryGetValue(((int)s.FrameIndex, true), out var nf) && nf != s.FrameIndex)
                                {
                                    s.FrameIndex = (uint)nf; changed = true;
                                }
                        if (changed) gc.Data = g.Write();
                    }
            }
        }

        // OBJD: GUID, price, label. CTSS: name/description. BMP_: catalog thumbnails.
        for (int i = 0; i < kept.Count; i++)
        {
            var o = new Objd(kept[i].Data);
            uint guid = GuidFor(name, i);
            int salt = 1;
            while (templateGuids.Contains(guid)) guid = GuidFor(name + "#" + salt++, i);
            o.Guid = guid;
            if (kept[i] == main) o.Price = (ushort)price;
            kept[i].Data = o.ToBytes();
            kept[i].Label = kept.Count == 1 ? Truncate(name, 63) : Truncate($"{name} {i + 1}", 63);
        }
        var mainObjd = new Objd(main.Data);
        UpdateCatalogStrings(iff, mainObjd.CatalogStringsId, name, description);
        UpdateThumbnails(iff, mainObjd.CatalogStringsId, RenderPreview(iff, targets.Where(t => !t.Flip).OrderByDescending(t => t.Zoom).FirstOrDefault()));

        if (options.PruneOtherObjects) PruneOthers(iff, kept);
        RemoveUnusedPalts(iff, replacedPalts);
        return iff;
    }

    private static string Truncate(string? s, int n) => (s ?? "").Length <= n ? s ?? "" : s!.Substring(0, n);

    private static ushort NextFreeId(IffFile iff, string type)
    {
        var used = iff.All(type).Select(c => (int)c.Id).ToHashSet();
        int next = used.Count == 0 ? 1 : used.Max() + 1;
        if (next <= 65535) return (ushort)next;
        for (int id = 1; id <= 65535; id++) if (!used.Contains(id)) return (ushort)id;
        throw new InvalidOperationException("No free chunk id.");
    }

    private static void UpdateCatalogStrings(IffFile iff, ushort ctssId, string name, string description)
    {
        var c = iff.Get("CTSS", ctssId);
        StrList s;
        if (c == null) { s = StrList.Create(name, description); iff.Chunks.Add(new IffChunk("CTSS", ctssId, s.Write(), Truncate(name, 63), 0x10)); return; }
        s = StrList.Read(c.Data);
        var langs = s.Items.Select(i => i.LanguageCode == 0 ? (byte)1 : i.LanguageCode).Distinct().ToList();
        if (langs.Count == 0) langs.Add(1);
        foreach (var l in langs)
        {
            s.SetString(0, name ?? "", l);
            s.SetString(1, description ?? "", l);
        }
        c.Data = s.Write();
        c.Label = Truncate(name, 63);
    }

    private static Image32? RenderPreview(IffFile iff, Target? t)
    {
        if (t == null) return null;
        var spr = Spr2.Read(iff.Get("SPR2", t.SpriteId)!.Data);
        var pal = Palt.Read(iff.Get("PALT", (ushort)spr.DefaultPaletteId)!.Data).Palette;
        return spr.Frames[t.Frame].ToImage(pal);
    }

    private static void UpdateThumbnails(IffFile iff, ushort catalogId, Image32? preview)
    {
        if (preview == null) return;
        for (int k = 0; k < 4; k++)
        {
            var c = iff.Get("BMP_", (ushort)(catalogId + k * 2000));
            if (c == null) continue;
            int w = 74, h = 37;
            byte br = 0, bg = 0, bb = 0;
            try
            {
                var old = Bmp.Decode(c.Data);
                w = old.Width; h = old.Height;
                old.GetPixel(0, 0, out br, out bg, out bb, out _);
            }
            catch (Exception) { /* keep defaults */ }
            var canvas = new Image32(w, h);
            canvas.Fill(br, bg, bb, 255);
            double sc = Math.Min((double)w / preview.Width, (double)h / preview.Height);
            int pw = Math.Max(1, (int)Math.Round(preview.Width * sc)), ph = Math.Max(1, (int)Math.Round(preview.Height * sc));
            var small = preview.Resize(pw, ph);
            int ox = (w - pw) / 2, oy = (h - ph) / 2;
            for (int y = 0; y < ph; y++)
                for (int x = 0; x < pw; x++)
                {
                    small.GetPixel(x, y, out var r, out var g, out var b, out var a);
                    if (a == 0) continue;
                    canvas.SetPixel(ox + x, oy + y, (byte)((r * a + br * (255 - a)) / 255), (byte)((g * a + bg * (255 - a)) / 255), (byte)((b * a + bb * (255 - a)) / 255), 255);
                }
            c.Data = Bmp.Encode24(canvas);
        }
    }

    /// <summary>Removes the given PALT ids if no SPR2/SPR# references them any more.</summary>
    private static void RemoveUnusedPalts(IffFile iff, HashSet<uint> candidates)
    {
        var used = new HashSet<uint>();
        foreach (var c in iff.All("SPR2")) { try { used.Add(Spr2.Read(c.Data).DefaultPaletteId); } catch (Exception) { } }
        foreach (var c in iff.All("SPR#")) { try { used.Add(Spr.Read(c.Data).PaletteId); } catch (Exception) { } }
        foreach (var id in candidates) if (!used.Contains(id)) iff.Remove("PALT", (ushort)id);
    }

    /// <summary>
    /// Removes the template's other objects: their OBJD/OBJf, catalog strings and thumbnails, every DGRP
    /// not owned by a kept object (base graphics or dynamic sprites; Maxis files also carry orphan DGRPs
    /// left over from other objects), then the SPR2/PALT chunks only those DGRPs used.
    /// </summary>
    private static void PruneOthers(IffFile iff, List<IffChunk> kept)
    {
        var keptObjds = kept.Select(k => new Objd(k.Data)).ToList();
        var removed = iff.All("OBJD").Where(o => !kept.Contains(o)).ToList();
        var keptCat = keptObjds.Select(o => o.CatalogStringsId).ToHashSet();
        foreach (var r in removed)
        {
            var o = new Objd(r.Data);
            iff.Chunks.Remove(r);
            iff.Chunks.RemoveAll(c => c.Type == "OBJf" && c.Id == r.Id);
            if (!keptCat.Contains(o.CatalogStringsId))
            {
                iff.Chunks.RemoveAll(c => c.Type == "CTSS" && c.Id == o.CatalogStringsId);
                for (int k = 0; k < 4; k++) iff.Chunks.RemoveAll(c => c.Type == "BMP_" && c.Id == o.CatalogStringsId + k * 2000);
            }
        }
        var removedDgrps = iff.All("DGRP").Where(g => !keptObjds.Any(o => o.OwnsDgrp(g.Id))).ToList();
        var candidateSprites = new HashSet<uint>();
        foreach (var g in removedDgrps)
        {
            try { foreach (var img in Dgrp.Read(g.Data).Images) foreach (var s in img.Sprites) candidateSprites.Add(s.SpriteId); } catch (Exception) { }
            iff.Chunks.Remove(g);
        }
        var stillUsed = new HashSet<uint>();
        foreach (var g in iff.All("DGRP"))
        {
            try { foreach (var img in Dgrp.Read(g.Data).Images) foreach (var s in img.Sprites) stillUsed.Add(s.SpriteId); } catch (Exception) { }
        }
        var candidatePalts = new HashSet<uint>();
        foreach (var id in candidateSprites.Where(id => !stillUsed.Contains(id)))
        {
            var c = iff.Get("SPR2", (ushort)id);
            if (c == null) continue;
            try { candidatePalts.Add(Spr2.Read(c.Data).DefaultPaletteId); } catch (Exception) { }
            iff.Chunks.Remove(c);
        }
        RemoveUnusedPalts(iff, candidatePalts);
    }

    // ------------------------------------------------------------------ geometry & painting

    /// <summary>Picture-area inset as fractions of the poster's width/height.</summary>
    public readonly record struct Border(double Left, double Right, double Top, double Bottom);

    /// <summary>
    /// The poster parallelogram of one frame: columns [X0, X1) and straight top/bottom edges
    /// top(x) = TopA + TopB*x, bottom(x) = BotA + BotB*x (x at pixel centre).
    /// </summary>
    public sealed class Geometry
    {
        public int X0, X1;
        public double TopA, TopB, BotA, BotB;
        public double Top(double x) => TopA + TopB * x;
        public double Bottom(double x) => BotA + BotB * x;
        public double S(double x) => (x - X0) / (X1 - X0);
        public double T(double x, double y) => (y - Top(x)) / Math.Max(1e-6, Bottom(x) - Top(x));
        /// <summary>Inverse mapping: frame position for normalized poster coordinates.</summary>
        public (double x, double y) At(double s, double t) { double x = X0 + s * (X1 - X0); return (x, Top(x) + t * (Bottom(x) - Top(x))); }
        public double WidthPx => X1 - X0;
        public double HeightPx => Bottom((X0 + X1) / 2.0) - Top((X0 + X1) / 2.0);

        public static Geometry Fit(Spr2Frame f)
        {
            var cols = new List<(double x, double top, double bot)>();
            for (int x = 0; x < f.Width; x++)
            {
                int top = -1, bot = -1;
                for (int y = 0; y < f.Height; y++)
                    if (f.Alpha[y * f.Width + x] >= 128) { if (top < 0) top = y; bot = y; }
                if (top >= 0) cols.Add((x + 0.5, top, bot + 1));
            }
            if (cols.Count == 0) throw new InvalidDataException("Template frame is empty.");
            var g = new Geometry { X0 = (int)(cols.First().x - 0.5), X1 = (int)(cols.Last().x + 0.5) };
            // Fit lines on the inner columns (ends are anti-aliased / rounded).
            var inner = cols.Count > 8 ? cols.Skip(2).Take(cols.Count - 4).ToList() : cols;
            (g.TopA, g.TopB) = LineFit(inner.Select(c => (c.x, c.top)));
            (g.BotA, g.BotB) = LineFit(inner.Select(c => (c.x, c.bot)));
            return g;
        }

        private static (double a, double b) LineFit(IEnumerable<(double x, double y)> pts)
        {
            var p = pts.ToList();
            double n = p.Count, sx = p.Sum(q => q.x), sy = p.Sum(q => q.y), sxx = p.Sum(q => q.x * q.x), sxy = p.Sum(q => q.x * q.y);
            double den = n * sxx - sx * sx;
            if (Math.Abs(den) < 1e-9) return (sy / n, 0);
            double b = (n * sxy - sx * sy) / den;
            return ((sy - b * sx) / n, b);
        }
    }

    /// <summary>
    /// Detects the template's outer border (e.g. the black poster frame): from each edge walk inward
    /// while the colour stays close to the colour just inside the edge. Median over several scanlines,
    /// capped at 20% per side.
    /// </summary>
    public static Border DetectBorder(Image32 frameImg, Geometry g)
    {
        double W = g.WidthPx, H = g.HeightPx;
        double Measure(bool horizontal, bool fromStart)
        {
            var results = new List<double>();
            double steps = horizontal ? W : H;
            for (int k = 0; k < 9; k++)
            {
                double across = 0.3 + 0.4 * k / 8.0;
                (int r, int g2, int b, int a) Px(double along)
                {
                    double s = horizontal ? (fromStart ? along : 1 - along) : across;
                    double t = horizontal ? across : (fromStart ? along : 1 - along);
                    var (x, y) = g.At(s, t);
                    int xi = Math.Clamp((int)Math.Floor(x), 0, frameImg.Width - 1), yi = Math.Clamp((int)Math.Floor(y), 0, frameImg.Height - 1);
                    frameImg.GetPixel(xi, yi, out var r, out var gg, out var b, out var a);
                    return (r, gg, b, a);
                }
                var refc = Px(1.5 / steps);
                double edge = 0.2;
                for (int i = 2; i < steps * 0.2; i++)
                {
                    var c = Px((i + 0.5) / steps);
                    int d = Math.Abs(c.r - refc.r) + Math.Abs(c.g2 - refc.g2) + Math.Abs(c.b - refc.b);
                    if (d > 90) { edge = i / steps; break; }
                }
                results.Add(edge);
            }
            results.Sort();
            return results[results.Count / 2];
        }
        return new Border(Measure(true, true), Measure(true, false), Measure(false, true), Measure(false, false));
    }

    /// <summary>Paints the picture into the inner area of one frame image (alpha/z untouched).</summary>
    private static void PaintPicture(Image32 img, Spr2Frame frame, Geometry g, Border border, Image32 picture, bool mirror, Options options)
    {
        double sL = border.Left, sR = 1 - border.Right, tT = border.Top, tB = 1 - border.Bottom;
        // SimsRedux: the border pixels physically stay where they are in the stored frame, so the region
        // is the same for mirrored copies; only the picture content is mirrored (the game flips it back).
        double regionW = (sR - sL) * g.WidthPx, regionH = (tB - tT) * g.HeightPx;
        if (regionW < 1 || regionH < 1) return;
        // Real-world aspect of the region: horizontal px -> tiles along the wall, vertical px -> wall units.
        // (The zoom scale cancels out, so near-zoom constants work for every zoom.)
        double aspect = (regionW / NearTileWidthPx) / (regionH / NearPxPerUnitHeight);
        var fitted = FitPicture(picture, aspect, options);
        if (mirror) fitted = MirrorX(fitted);
        // Pre-filter to about 2x the region size so supersampling doesn't alias.
        int pw = Math.Max(2, (int)Math.Ceiling(regionW * 2)), ph = Math.Max(2, (int)Math.Ceiling(regionH * 2));
        if (fitted.Width > pw || fitted.Height > ph) fitted = fitted.Resize(Math.Min(pw, fitted.Width), Math.Min(ph, fitted.Height));

        const int SS = 4;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int i = y * img.Width + x;
                if (frame.Alpha[i] == 0) continue;
                double r = 0, gg = 0, b = 0; int inside = 0;
                for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                    {
                        double px = x + (sx + 0.5) / SS, py = y + (sy + 0.5) / SS;
                        double s = g.S(px), t = g.T(px, py);
                        if (s < sL || s >= sR || t < tT || t >= tB) continue;
                        double u = (s - sL) / (sR - sL), v = (t - tT) / (tB - tT);
                        fitted.SampleClamp(u * fitted.Width, v * fitted.Height, out var cr, out var cg, out var cb, out _);
                        r += cr; gg += cg; b += cb; inside++;
                    }
                if (inside == 0) continue;
                double cov = inside / (double)(SS * SS);
                int o = i * 4;
                img.Rgba[o] = Image32.ClampByte(r / inside * cov + img.Rgba[o] * (1 - cov));
                img.Rgba[o + 1] = Image32.ClampByte(gg / inside * cov + img.Rgba[o + 1] * (1 - cov));
                img.Rgba[o + 2] = Image32.ClampByte(b / inside * cov + img.Rgba[o + 2] * (1 - cov));
            }
    }

    private static Image32 FitPicture(Image32 pic, double aspect, Options options)
    {
        double pa = (double)pic.Width / pic.Height;
        switch (options.Fit)
        {
            case FitMode.Stretch:
                return pic;
            case FitMode.Cover:
                if (pa > aspect) { int w = Math.Max(1, (int)Math.Round(pic.Height * aspect)); return pic.Crop((pic.Width - w) / 2, 0, w, pic.Height); }
                else { int h = Math.Max(1, (int)Math.Round(pic.Width / aspect)); return pic.Crop(0, (pic.Height - h) / 2, pic.Width, h); }
            default: // Contain
                {
                    int cw = pic.Width, ch = pic.Height;
                    if (pa > aspect) ch = (int)Math.Round(pic.Width / aspect); else cw = (int)Math.Round(pic.Height * aspect);
                    var canvas = new Image32(Math.Max(1, cw), Math.Max(1, ch));
                    var (mr, mg, mb) = options.MatColor;
                    canvas.Fill(mr, mg, mb, 255);
                    int ox = (canvas.Width - pic.Width) / 2, oy = (canvas.Height - pic.Height) / 2;
                    for (int y = 0; y < pic.Height; y++)
                        Buffer.BlockCopy(pic.Rgba, y * pic.Width * 4, canvas.Rgba, ((y + oy) * canvas.Width + ox) * 4, pic.Width * 4);
                    return canvas;
                }
        }
    }

    private static Image32 MirrorX(Image32 img)
    {
        var o = new Image32(img.Width, img.Height);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                Buffer.BlockCopy(img.Rgba, (y * img.Width + x) * 4, o.Rgba, (y * img.Width + (img.Width - 1 - x)) * 4, 4);
        return o;
    }
}
