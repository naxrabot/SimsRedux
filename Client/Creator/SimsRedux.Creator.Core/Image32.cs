using System.IO.Compression;

namespace SimsRedux.Creator;

/// <summary>
/// A simple 32-bit RGBA image (4 bytes per pixel, R,G,B,A order, straight/non-premultiplied alpha).
/// SimsRedux: this is the one image type used throughout the creator so we never depend on
/// System.Drawing or XNA; everything (DDS, PNG, BMP, sprites) converts to and from it.
/// </summary>
public sealed class Image32
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }

    public Image32(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException($"Invalid image size {width}x{height}.");
        Width = width;
        Height = height;
        Rgba = new byte[checked(width * height * 4)];
    }

    public Image32(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException($"Invalid image size {width}x{height}.");
        if (rgba == null || rgba.Length != width * height * 4) throw new ArgumentException("RGBA buffer has the wrong size.");
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public Image32 Clone() => new Image32(Width, Height, (byte[])Rgba.Clone());

    public void GetPixel(int x, int y, out byte r, out byte g, out byte b, out byte a)
    {
        int i = (y * Width + x) * 4;
        r = Rgba[i]; g = Rgba[i + 1]; b = Rgba[i + 2]; a = Rgba[i + 3];
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        int i = (y * Width + x) * 4;
        Rgba[i] = r; Rgba[i + 1] = g; Rgba[i + 2] = b; Rgba[i + 3] = a;
    }

    public void Fill(byte r, byte g, byte b, byte a)
    {
        for (int i = 0; i < Rgba.Length; i += 4) { Rgba[i] = r; Rgba[i + 1] = g; Rgba[i + 2] = b; Rgba[i + 3] = a; }
    }

    /// <summary>Copies a rectangle out of this image (clamped to the image bounds).</summary>
    public Image32 Crop(int x, int y, int w, int h)
    {
        int x0 = Math.Clamp(x, 0, Width), y0 = Math.Clamp(y, 0, Height);
        int x1 = Math.Clamp(x + w, 0, Width), y1 = Math.Clamp(y + h, 0, Height);
        if (x1 <= x0 || y1 <= y0) throw new ArgumentException("Crop rectangle is outside the image.");
        var res = new Image32(x1 - x0, y1 - y0);
        for (int yy = y0; yy < y1; yy++)
            Buffer.BlockCopy(Rgba, (yy * Width + x0) * 4, res.Rgba, (yy - y0) * res.Width * 4, res.Width * 4);
        return res;
    }

    /// <summary>Crops the largest centred square out of the image.</summary>
    public Image32 CropSquare()
    {
        int s = Math.Min(Width, Height);
        return Crop((Width - s) / 2, (Height - s) / 2, s, s);
    }

    /// <summary>
    /// Resizes the image. SimsRedux: downscaling uses an exact area-average (box) filter, which gives
    /// clean results for the big reductions we do (1024px Sims 3 textures down to 16..128px sprites);
    /// upscaling uses bilinear filtering. Averaging is alpha-weighted so transparent pixels don't
    /// bleed dark fringes into the colour.
    /// </summary>
    public Image32 Resize(int newWidth, int newHeight)
    {
        if (newWidth <= 0 || newHeight <= 0) throw new ArgumentException("Invalid resize target.");
        if (newWidth == Width && newHeight == Height) return Clone();
        // Do each axis separately; each axis picks box (shrink) or bilinear (grow).
        var tmp = ResizeAxis(this, newWidth, true);
        return ResizeAxis(tmp, newHeight, false);
    }

    private static Image32 ResizeAxis(Image32 src, int newLen, bool horizontal)
    {
        int oldLen = horizontal ? src.Width : src.Height;
        int other = horizontal ? src.Height : src.Width;
        if (oldLen == newLen) return src;
        var dst = horizontal ? new Image32(newLen, other) : new Image32(other, newLen);
        // Precompute contributions: list of (srcIndex, weight) per destination sample.
        var contrib = new List<(int idx, double w)>[newLen];
        double scale = (double)oldLen / newLen;
        for (int d = 0; d < newLen; d++)
        {
            var list = new List<(int, double)>();
            if (newLen < oldLen)
            {
                // Box filter: integrate source over [d*scale, (d+1)*scale).
                double s0 = d * scale, s1 = (d + 1) * scale;
                int i0 = (int)Math.Floor(s0), i1 = (int)Math.Ceiling(s1);
                for (int i = i0; i < i1; i++)
                {
                    double w = Math.Min(s1, i + 1) - Math.Max(s0, i);
                    if (w > 1e-9) list.Add((Math.Clamp(i, 0, oldLen - 1), w));
                }
            }
            else
            {
                // Bilinear: sample at pixel centre.
                double s = (d + 0.5) * scale - 0.5;
                int i0 = (int)Math.Floor(s);
                double f = s - i0;
                list.Add((Math.Clamp(i0, 0, oldLen - 1), 1 - f));
                list.Add((Math.Clamp(i0 + 1, 0, oldLen - 1), f));
            }
            contrib[d] = list;
        }
        var s4 = src.Rgba; var d4 = dst.Rgba;
        for (int o = 0; o < other; o++)
        {
            for (int d = 0; d < newLen; d++)
            {
                double r = 0, g = 0, b = 0, a = 0, wsum = 0;
                foreach (var (idx, w) in contrib[d])
                {
                    int si = horizontal ? (o * src.Width + idx) * 4 : (idx * src.Width + o) * 4;
                    double aw = s4[si + 3] * w;
                    r += s4[si] * aw; g += s4[si + 1] * aw; b += s4[si + 2] * aw; a += aw; wsum += w;
                }
                int di = horizontal ? (o * newLen + d) * 4 : (d * other + o) * 4;
                if (a > 0)
                {
                    d4[di] = ClampByte(r / a); d4[di + 1] = ClampByte(g / a); d4[di + 2] = ClampByte(b / a);
                }
                d4[di + 3] = ClampByte(wsum > 0 ? a / wsum : 0);
            }
        }
        return dst;
    }

    /// <summary>Bilinear sample at continuous coordinates (pixel centres at +0.5), with wrap-around (tiling) addressing.</summary>
    public void SampleWrap(double x, double y, out double r, out double g, out double b, out double a)
    {
        double fx = x - 0.5, fy = y - 0.5;
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        double tx = fx - x0, ty = fy - y0;
        r = g = b = a = 0;
        double ws = 0;
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                double w = (i == 0 ? 1 - tx : tx) * (j == 0 ? 1 - ty : ty);
                if (w <= 0) continue;
                int px = Mod(x0 + i, Width), py = Mod(y0 + j, Height);
                int idx = (py * Width + px) * 4;
                double aw = Rgba[idx + 3] * w;
                r += Rgba[idx] * aw; g += Rgba[idx + 1] * aw; b += Rgba[idx + 2] * aw; a += aw; ws += w;
            }
        if (a > 0) { r /= a; g /= a; b /= a; }
        a = ws > 0 ? a / ws : 0;
    }

    /// <summary>Bilinear sample with clamp-to-edge addressing.</summary>
    public void SampleClamp(double x, double y, out double r, out double g, out double b, out double a)
    {
        double fx = Math.Clamp(x - 0.5, 0, Width - 1), fy = Math.Clamp(y - 0.5, 0, Height - 1);
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        double tx = fx - x0, ty = fy - y0;
        r = g = b = a = 0;
        double ws = 0;
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                double w = (i == 0 ? 1 - tx : tx) * (j == 0 ? 1 - ty : ty);
                if (w <= 0) continue;
                int px = Math.Min(x0 + i, Width - 1), py = Math.Min(y0 + j, Height - 1);
                int idx = (py * Width + px) * 4;
                double aw = Rgba[idx + 3] * w;
                r += Rgba[idx] * aw; g += Rgba[idx + 1] * aw; b += Rgba[idx + 2] * aw; a += aw; ws += w;
            }
        if (a > 0) { r /= a; g /= a; b /= a; }
        a = ws > 0 ? a / ws : 0;
    }

    internal static int Mod(int v, int m) { int r = v % m; return r < 0 ? r + m : r; }
    public static byte ClampByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    /// <summary>Mean absolute per-channel RGB difference over pixels where both images are opaque (alpha &gt;= 128).</summary>
    public static double MeanRgbError(Image32 a, Image32 b)
    {
        if (a.Width != b.Width || a.Height != b.Height) throw new ArgumentException("Images differ in size.");
        double sum = 0; long n = 0;
        for (int i = 0; i < a.Rgba.Length; i += 4)
        {
            if (a.Rgba[i + 3] < 128 || b.Rgba[i + 3] < 128) continue;
            sum += Math.Abs(a.Rgba[i] - b.Rgba[i]) + Math.Abs(a.Rgba[i + 1] - b.Rgba[i + 1]) + Math.Abs(a.Rgba[i + 2] - b.Rgba[i + 2]);
            n += 3;
        }
        return n == 0 ? 0 : sum / n;
    }

    // ------------------------------------------------------------------ PNG

    public void SavePng(string path) => File.WriteAllBytes(path, ToPng());

    /// <summary>
    /// Encodes as an 8-bit RGBA PNG. SimsRedux: our own encoder (zlib via System.IO.Compression);
    /// each row uses the PNG filter with the smallest sum of absolute values (standard heuristic).
    /// </summary>
    public byte[] ToPng()
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)Width); WriteBE(ihdr, 4, (uint)Height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WritePngChunk(ms, "IHDR", ihdr);

        int stride = Width * 4;
        var raw = new byte[(stride + 1) * Height];
        var prev = new byte[stride];
        var cand = new byte[5][];
        for (int f = 0; f < 5; f++) cand[f] = new byte[stride];
        for (int y = 0; y < Height; y++)
        {
            var cur = new ReadOnlySpan<byte>(Rgba, y * stride, stride);
            int best = 0; long bestSum = long.MaxValue;
            for (int f = 0; f < 5; f++)
            {
                long sum = 0;
                var c = cand[f];
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= 4 ? cur[i - 4] : 0, b = prev[i], cc = i >= 4 ? prev[i - 4] : 0;
                    int pred = f switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) >> 1, _ => Paeth(a, b, cc) };
                    byte v = (byte)(cur[i] - pred);
                    c[i] = v;
                    sum += v < 128 ? v : 256 - v;
                }
                if (sum < bestSum) { bestSum = sum; best = f; }
            }
            raw[y * (stride + 1)] = (byte)best;
            Buffer.BlockCopy(cand[best], 0, raw, y * (stride + 1) + 1, stride);
            cur.CopyTo(prev);
        }
        using (var zms = new MemoryStream())
        {
            using (var z = new ZLibStream(zms, CompressionLevel.Optimal, true)) z.Write(raw);
            WritePngChunk(ms, "IDAT", zms.ToArray());
        }
        WritePngChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static void WritePngChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, (uint)data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFF, t), data) ^ 0xFFFFFFFF;
        var c = new byte[4]; WriteBE(c, 0, crc); s.Write(c);
    }

    private static void WriteBE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    private static uint ReadBE(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    public static Image32 LoadPng(string path) => LoadPng(File.ReadAllBytes(path));

    /// <summary>
    /// Decodes a non-interlaced PNG (colour types grey, RGB, palette, grey+alpha, RGBA; bit depth 8,
    /// plus 16-bit truecolour/grey and 1/2/4-bit palette/grey for convenience).
    /// </summary>
    public static Image32 LoadPng(byte[] data)
    {
        if (data == null || data.Length < 8 || data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47)
            throw new InvalidDataException("Not a PNG file.");
        int pos = 8;
        int w = 0, h = 0, depth = 0, ctype = 0, interlace = 0;
        byte[]? plte = null, trns = null;
        using var idat = new MemoryStream();
        bool sawHeader = false;
        while (pos + 8 <= data.Length)
        {
            int len = (int)ReadBE(data, pos);
            if (len < 0 || pos + 12 + len > data.Length) throw new InvalidDataException("Truncated PNG chunk.");
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            int d = pos + 8;
            switch (type)
            {
                case "IHDR":
                    w = (int)ReadBE(data, d); h = (int)ReadBE(data, d + 4);
                    depth = data[d + 8]; ctype = data[d + 9]; interlace = data[d + 12];
                    sawHeader = true; break;
                case "PLTE": plte = data.AsSpan(d, len).ToArray(); break;
                case "tRNS": trns = data.AsSpan(d, len).ToArray(); break;
                case "IDAT": idat.Write(data, d, len); break;
            }
            pos += 12 + len;
            if (type == "IEND") break;
        }
        if (!sawHeader || w <= 0 || h <= 0 || w > 32768 || h > 32768) throw new InvalidDataException("Bad PNG header.");
        if (interlace != 0) throw new NotSupportedException("Interlaced PNGs are not supported.");
        int channels = ctype switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException($"PNG colour type {ctype} not supported.") };
        if (depth != 8 && depth != 16 && !(depth < 8 && (ctype == 0 || ctype == 3))) throw new NotSupportedException($"PNG bit depth {depth} not supported.");
        int bitsPP = channels * depth;
        int bpp = Math.Max(1, bitsPP / 8);
        int stride = (w * bitsPP + 7) / 8;
        byte[] raw;
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            z.CopyTo(outMs);
            raw = outMs.ToArray();
        }
        if (raw.Length < (stride + 1) * h) throw new InvalidDataException("PNG image data is truncated.");
        var img = new Image32(w, h);
        var prev = new byte[stride];
        var cur = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            int f = raw[y * (stride + 1)];
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = f switch
                {
                    0 => cur[i],
                    1 => (byte)(cur[i] + a),
                    2 => (byte)(cur[i] + b),
                    3 => (byte)(cur[i] + ((a + b) >> 1)),
                    4 => (byte)(cur[i] + Paeth(a, b, c)),
                    _ => throw new InvalidDataException("Bad PNG filter type.")
                };
            }
            for (int x = 0; x < w; x++)
            {
                byte r, g, bl, al = 255;
                int Sample(int ch) // returns 8-bit sample for channel ch
                {
                    if (depth == 8) return cur[x * channels + ch];
                    if (depth == 16) return cur[(x * channels + ch) * 2];
                    int bitPos = x * depth, by = cur[bitPos >> 3], shift = 8 - depth - (bitPos & 7);
                    return (by >> shift) & ((1 << depth) - 1);
                }
                switch (ctype)
                {
                    case 0:
                        {
                            int v = Sample(0);
                            if (depth < 8) v = v * 255 / ((1 << depth) - 1);
                            r = g = bl = (byte)v;
                            if (trns != null && trns.Length >= 2 && depth == 8 && v == trns[1]) al = 0;
                            break;
                        }
                    case 2: r = (byte)Sample(0); g = (byte)Sample(1); bl = (byte)Sample(2); break;
                    case 3:
                        {
                            int idx = Sample(0);
                            if (plte == null || idx * 3 + 2 >= plte.Length) { r = g = bl = 0; }
                            else { r = plte[idx * 3]; g = plte[idx * 3 + 1]; bl = plte[idx * 3 + 2]; }
                            if (trns != null && idx < trns.Length) al = trns[idx];
                            break;
                        }
                    case 4: r = g = bl = (byte)Sample(0); al = (byte)Sample(1); break;
                    default: r = (byte)Sample(0); g = (byte)Sample(1); bl = (byte)Sample(2); al = (byte)Sample(3); break;
                }
                img.SetPixel(x, y, r, g, bl, al);
            }
            (prev, cur) = (cur, prev);
        }
        return img;
    }

    /// <summary>
    /// Loads any image format we understand from bytes, sniffing the magic: PNG, DDS or BMP.
    /// </summary>
    public static Image32 Load(byte[] data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == (byte)'P') return LoadPng(data);
        if (data.Length >= 4 && data[0] == (byte)'D' && data[1] == (byte)'D' && data[2] == (byte)'S') return Dds.Decode(data);
        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M') return Bmp.Decode(data);
        throw new NotSupportedException("Unknown image format (supported: PNG, DDS, BMP).");
    }
}

/// <summary>CRC-32 (IEEE) used by the PNG encoder.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = MakeTable();
    private static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
