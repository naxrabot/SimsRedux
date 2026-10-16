using System;
using System.Collections.Generic;
using System.Linq;

namespace SimsRedux.Creator;

/// <summary>
/// SimsRedux: "what it will look like in the game" pictures of a built item, decoded back from the
/// built file itself (so the preview shows exactly what was written).
///   floor   - a 3x3 patch of the floor at the closest zoom
///   wall    - an inside corner of the wall at the closest zoom
///   picture - the picture hanging on a wall at the closest zoom
/// </summary>
public static class Previews
{
    /// <summary>The three zooms the game has, closest first (for the bigger preview).</summary>
    public static readonly string[] ZoomNames = { "Near", "Medium", "Far" };

    public static Image32 Render(string kind, byte[] built) => Render(kind, built, 0);

    /// <summary>
    /// Renders one zoom: 0 = near (closest), 1 = medium, 2 = far. Pictures are drawn at 2x, floors laid out
    /// 3 x 3 at 2x, walls 1:1 (each zoom as the game stores it, so smaller zooms look smaller).
    /// </summary>
    public static Image32 Render(string kind, byte[] built, int zoom)
    {
        if (zoom < 0 || zoom > 2) throw new ArgumentOutOfRangeException(nameof(zoom));
        var iff = IffFile.Read(built);
        switch ((kind ?? "").ToLowerInvariant())
        {
            case "floor":
            {
                var spr = Spr2.Read(iff.Get("SPR2", (ushort)(zoom == 0 ? 513 : zoom == 1 ? 257 : 1))!.Data);
                var frame = spr.Frames[0];
                var pal = PaletteFor(iff, frame.PaletteId);
                return OnBackground(TiledFloor(frame.ToImage(pal)), 40, 40, 40);
            }
            case "wall":
            {
                var id = zoom == 0 ? WallBuilder.NearSpriteId : zoom == 1 ? WallBuilder.MediumSpriteId : WallBuilder.FarSpriteId;
                var spr = Spr.Read(iff.Get("SPR#", id)!.Data);
                var pal = PaletteFor(iff, spr.PaletteId);
                return OnBackground(WallBuilder.PreviewCorner(spr, pal), 200, 200, 200);
            }
            case "picture":
            {
                // the picture's front-facing frames (one per zoom, found the same way the builder finds them), biggest first
                var fronts = new List<Image32>();
                List<PictureBuilder.Target> targets;
                try { targets = PictureBuilder.FindTargets(iff); } catch { targets = new(); }
                foreach (var t in targets.Where(t => !t.Flip))
                {
                    try
                    {
                        var s = Spr2.Read(iff.Get("SPR2", t.SpriteId)!.Data);
                        var f = s.Frames[t.Frame];
                        fronts.Add(f.ToImage(PaletteFor(iff, f.PaletteId)));
                    }
                    catch { }
                }
                if (fronts.Count == 0)
                {
                    // (fallback: frame 1 of every sprite)
                    foreach (var c in iff.All("SPR2"))
                        try { var s = Spr2.Read(c.Data); if (s.Frames.Count > 1) fronts.Add(s.Frames[1].ToImage(PaletteFor(iff, s.Frames[1].PaletteId))); } catch { }
                }
                var sizes = fronts.OrderByDescending(f => f.Width * f.Height).ToList();
                if (sizes.Count == 0) return new Image32(1, 1);
                return OnBackground(Zoom(sizes[Math.Min(zoom, sizes.Count - 1)], 2), 200, 200, 200);
            }
        }
        throw new ArgumentException("Unknown item kind '" + kind + "'.");
    }

    private static Palette PaletteFor(IffFile iff, ushort id)
    {
        var chunk = iff.Get("PALT", id) ?? iff.All("PALT").FirstOrDefault();
        return chunk == null ? new Palette() : Palt.Read(chunk.Data).Palette;
    }

    /// <summary>Lays the floor's diamond out 3 x 3, like a bit of a room.</summary>
    private static Image32 TiledFloor(Image32 diamond)
    {
        int n = diamond.Height;
        var canvas = new Image32(n * 2 * 3 + 2, n * 3 + 2);
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

    private static Image32 Zoom(Image32 img, int k)
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

    private static Image32 OnBackground(Image32 img, byte br, byte bg, byte bb)
    {
        var o = new Image32(img.Width, img.Height);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                img.GetPixel(x, y, out var r, out var g, out var b, out var a);
                o.SetPixel(x, y, (byte)((r * a + br * (255 - a)) / 255), (byte)((g * a + bg * (255 - a)) / 255), (byte)((b * a + bb * (255 - a)) / 255), 255);
            }
        return o;
    }
}
