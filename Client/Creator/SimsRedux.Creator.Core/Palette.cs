namespace SimsRedux.Creator;

/// <summary>
/// A colour palette of up to 256 RGB entries with nearest-colour lookup.
/// SimsRedux: Sims 1 sprites are 8-bit paletted, so every imported texture is quantized here.
/// </summary>
public sealed class Palette
{
    /// <summary>RGB triplets, 3 bytes per entry, always 256 entries (unused entries are black).</summary>
    public byte[] Rgb { get; } = new byte[256 * 3];
    /// <summary>Number of entries actually in use.</summary>
    public int Count { get; set; }
    /// <summary>Index reserved for transparency (never returned by nearest-colour lookups), or -1.</summary>
    public int TransparentIndex { get; set; } = -1;

    private Dictionary<int, byte>? _cache;

    public Palette() { }

    public Palette(IReadOnlyList<(byte r, byte g, byte b)> colors)
    {
        Count = Math.Min(256, colors.Count);
        for (int i = 0; i < Count; i++) { Rgb[i * 3] = colors[i].r; Rgb[i * 3 + 1] = colors[i].g; Rgb[i * 3 + 2] = colors[i].b; }
    }

    public (byte r, byte g, byte b) this[int i] => (Rgb[i * 3], Rgb[i * 3 + 1], Rgb[i * 3 + 2]);

    public void Set(int i, byte r, byte g, byte b)
    {
        Rgb[i * 3] = r; Rgb[i * 3 + 1] = g; Rgb[i * 3 + 2] = b;
        if (i >= Count) Count = i + 1;
        _cache = null;
    }

    /// <summary>Index of the nearest palette colour (squared RGB distance, slightly weighted to green).</summary>
    public byte Nearest(int r, int g, int b)
    {
        int key = (r << 16) | (g << 8) | b;
        _cache ??= new Dictionary<int, byte>();
        if (_cache.TryGetValue(key, out var hit)) return hit;
        int best = 0; long bestD = long.MaxValue;
        int n = Math.Max(1, Count);
        for (int i = 0; i < n; i++)
        {
            if (i == TransparentIndex) continue;
            int dr = Rgb[i * 3] - r, dg = Rgb[i * 3 + 1] - g, db = Rgb[i * 3 + 2] - b;
            long d = 2L * dr * dr + 4L * dg * dg + 3L * db * db;
            if (d < bestD) { bestD = d; best = i; }
        }
        if (_cache.Count < 1 << 20) _cache[key] = (byte)best;
        return (byte)best;
    }

    /// <summary>
    /// Maps an image to palette indices. Pixels with alpha below <paramref name="alphaThreshold"/>
    /// become <see cref="TransparentIndex"/> (or 0 if there is none).
    /// SimsRedux: optional Floyd-Steinberg dithering for smoother gradients (off by default because
    /// it makes tiling floor textures look noisy).
    /// </summary>
    public byte[] Map(Image32 img, bool dither = false, int alphaThreshold = 1)
    {
        var res = new byte[img.Width * img.Height];
        int w = img.Width;
        float[]? err = dither ? new float[(w + 2) * 2 * 3] : null;
        for (int y = 0; y < img.Height; y++)
        {
            if (err != null)
            {
                // rotate error rows: current <- next, next <- 0
                Array.Copy(err, (w + 2) * 3, err, 0, (w + 2) * 3);
                Array.Clear(err, (w + 2) * 3, (w + 2) * 3);
            }
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (img.Rgba[i + 3] < alphaThreshold) { res[y * w + x] = (byte)Math.Max(0, TransparentIndex); continue; }
                float r = img.Rgba[i], g = img.Rgba[i + 1], b = img.Rgba[i + 2];
                if (err != null) { int e = (x + 1) * 3; r += err[e]; g += err[e + 1]; b += err[e + 2]; }
                int ri = Math.Clamp((int)MathF.Round(r), 0, 255), gi = Math.Clamp((int)MathF.Round(g), 0, 255), bi = Math.Clamp((int)MathF.Round(b), 0, 255);
                byte idx = Nearest(ri, gi, bi);
                res[y * w + x] = idx;
                if (err != null)
                {
                    float er = r - Rgb[idx * 3], eg = g - Rgb[idx * 3 + 1], eb = b - Rgb[idx * 3 + 2];
                    void Add(int row, int xx, float f)
                    {
                        int e = row * (w + 2) * 3 + (xx + 1) * 3;
                        err![e] += er * f; err[e + 1] += eg * f; err[e + 2] += eb * f;
                    }
                    Add(0, x + 1, 7 / 16f); Add(1, x - 1, 3 / 16f); Add(1, x, 5 / 16f); Add(1, x + 1, 1 / 16f);
                }
            }
        }
        return res;
    }

    /// <summary>Quantizes one image to at most <paramref name="colors"/> colours.</summary>
    public static Palette Quantize(Image32 img, int colors = 256, bool reserveTransparent = false)
        => Quantize(new[] { img }, colors, reserveTransparent);

    /// <summary>
    /// Builds a palette for a set of images (e.g. all zoom levels of one sprite share a PALT).
    /// SimsRedux: median cut over a 5:5:5-bit colour histogram (weighted by pixel count), followed by
    /// a few k-means refinement passes on the histogram. If <paramref name="reserveTransparent"/> is set,
    /// index 0 is reserved (kept black) for transparency and real colours start at 1.
    /// </summary>
    public static Palette Quantize(IEnumerable<Image32> images, int colors = 256, bool reserveTransparent = false)
    {
        colors = Math.Clamp(colors, 2, 256);
        // Histogram at 5 bits per channel, but remember exact average colour per bin.
        var count = new long[32768];
        var sumR = new long[32768]; var sumG = new long[32768]; var sumB = new long[32768];
        foreach (var img in images)
        {
            var d = img.Rgba;
            for (int i = 0; i < d.Length; i += 4)
            {
                if (d[i + 3] == 0) continue;
                int key = ((d[i] >> 3) << 10) | ((d[i + 1] >> 3) << 5) | (d[i + 2] >> 3);
                count[key]++; sumR[key] += d[i]; sumG[key] += d[i + 1]; sumB[key] += d[i + 2];
            }
        }
        var bins = new List<Bin>();
        for (int k = 0; k < 32768; k++)
            if (count[k] > 0) bins.Add(new Bin((double)sumR[k] / count[k], (double)sumG[k] / count[k], (double)sumB[k] / count[k], count[k]));

        int first = reserveTransparent ? 1 : 0;
        int maxColors = colors - first;
        var pal = new Palette();
        if (reserveTransparent) { pal.TransparentIndex = 0; pal.Count = 1; }
        if (bins.Count == 0) { pal.Set(first, 0, 0, 0); return pal; }

        List<(double r, double g, double b)> centers;
        if (bins.Count <= maxColors)
        {
            centers = bins.Select(b => (b.R, b.G, b.B)).ToList();
        }
        else
        {
            // Median cut: repeatedly split the box with the largest (count-weighted) spread.
            var boxes = new List<List<Bin>> { bins };
            while (boxes.Count < maxColors)
            {
                int bestBox = -1; double bestScore = 0; int bestAxis = 0;
                for (int i = 0; i < boxes.Count; i++)
                {
                    var bx = boxes[i];
                    if (bx.Count < 2) continue;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        double mn = double.MaxValue, mx = double.MinValue; long n = 0;
                        foreach (var b in bx) { double v = b.Axis(axis); if (v < mn) mn = v; if (v > mx) mx = v; n += b.Count; }
                        double w = axis == 1 ? 1.2 : 1.0;
                        double score = (mx - mn) * w * Math.Sqrt(n);
                        if (score > bestScore) { bestScore = score; bestBox = i; bestAxis = axis; }
                    }
                }
                if (bestBox < 0) break;
                var box = boxes[bestBox];
                box.Sort((a, b) => a.Axis(bestAxis).CompareTo(b.Axis(bestAxis)));
                long total = box.Sum(b => b.Count), acc = 0;
                int split = 1;
                for (int i = 0; i < box.Count - 1; i++)
                {
                    acc += box[i].Count;
                    if (acc * 2 >= total) { split = i + 1; break; }
                    split = i + 1;
                }
                split = Math.Clamp(split, 1, box.Count - 1);
                boxes[bestBox] = box.GetRange(0, split);
                boxes.Add(box.GetRange(split, box.Count - split));
            }
            centers = boxes.Select(bx =>
            {
                double n = bx.Sum(b => (double)b.Count);
                return (bx.Sum(b => b.R * b.Count) / n, bx.Sum(b => b.G * b.Count) / n, bx.Sum(b => b.B * b.Count) / n);
            }).ToList();
            // k-means refinement (Lloyd iterations) on the histogram bins.
            for (int iter = 0; iter < 4; iter++)
            {
                var acc = new double[centers.Count, 4];
                foreach (var b in bins)
                {
                    int best = 0; double bestD = double.MaxValue;
                    for (int c = 0; c < centers.Count; c++)
                    {
                        double dr = centers[c].r - b.R, dg = centers[c].g - b.G, db = centers[c].b - b.B;
                        double d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
                        if (d < bestD) { bestD = d; best = c; }
                    }
                    acc[best, 0] += b.R * b.Count; acc[best, 1] += b.G * b.Count; acc[best, 2] += b.B * b.Count; acc[best, 3] += b.Count;
                }
                for (int c = 0; c < centers.Count; c++)
                    if (acc[c, 3] > 0) centers[c] = (acc[c, 0] / acc[c, 3], acc[c, 1] / acc[c, 3], acc[c, 2] / acc[c, 3]);
            }
        }
        for (int i = 0; i < centers.Count && first + i < 256; i++)
            pal.Set(first + i, Image32.ClampByte(centers[i].r), Image32.ClampByte(centers[i].g), Image32.ClampByte(centers[i].b));
        return pal;
    }

    private readonly record struct Bin(double R, double G, double B, long Count)
    {
        public double Axis(int a) => a == 0 ? R : a == 1 ? G : B;
    }
}

/// <summary>
/// PALT chunk: uint32 version, uint32 entry count, 8 reserved bytes, then count RGB triplets
/// (mirrors FreeSO's PALT.cs). SimsRedux: real Sims 1 files use version 1 (FreeSO writes 0); we
/// keep whatever was read and default to 1 for new chunks, matching the Maxis/frosti files.
/// </summary>
public sealed class Palt
{
    public uint Version { get; set; } = 1;
    public Palette Palette { get; set; } = new();
    /// <summary>Entry count written to the chunk (normally 256).</summary>
    public int EntryCount { get; set; } = 256;
    private byte[] _reserved = new byte[8];

    public static Palt Read(byte[] data)
    {
        if (data.Length < 16) throw new InvalidDataException("PALT chunk is too short.");
        var p = new Palt { Version = BitConverter.ToUInt32(data, 0) };
        int n = (int)Math.Min(BitConverter.ToUInt32(data, 4), 256u);
        p._reserved = data.AsSpan(8, 8).ToArray();
        n = Math.Min(n, (data.Length - 16) / 3);
        p.EntryCount = n;
        for (int i = 0; i < n; i++) p.Palette.Set(i, data[16 + i * 3], data[17 + i * 3], data[18 + i * 3]);
        p.Palette.Count = n;
        return p;
    }

    public byte[] Write()
    {
        int n = EntryCount;
        var d = new byte[16 + n * 3];
        BitConverter.GetBytes(Version).CopyTo(d, 0);
        BitConverter.GetBytes((uint)n).CopyTo(d, 4);
        Buffer.BlockCopy(_reserved, 0, d, 8, 8);
        Buffer.BlockCopy(Palette.Rgb, 0, d, 16, n * 3);
        return d;
    }

    public static Palt FromPalette(Palette p) => new Palt { Palette = p, EntryCount = 256 };
}
