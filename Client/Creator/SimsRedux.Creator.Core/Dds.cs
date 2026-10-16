namespace SimsRedux.Creator;

/// <summary>Pixel formats understood by <see cref="Dds"/>.</summary>
public enum DdsFormat
{
    Unknown = 0,
    DXT1, DXT3, DXT5,
    A8R8G8B8, X8R8G8B8, R8G8B8, A8B8G8R8, X8B8G8R8,
    /// <summary>Any other uncompressed RGB(A) layout described by bit masks.</summary>
    RgbMasked,
    L8, A8L8, A8,
}

/// <summary>Header information of a DDS file.</summary>
public sealed class DdsInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int MipCount { get; init; }
    public DdsFormat Format { get; init; }
    public string FourCC { get; init; } = "";
    public int BitCount { get; init; }
    public uint RMask { get; init; }
    public uint GMask { get; init; }
    public uint BMask { get; init; }
    public uint AMask { get; init; }
    internal uint PfFlags { get; init; }
    /// <summary>Byte offset of the first mip's pixel data.</summary>
    public int DataOffset { get; init; }

    public override string ToString() => $"{Width}x{Height} {Format}" + (MipCount > 1 ? $" ({MipCount} mips)" : "");
}

/// <summary>
/// DirectDraw Surface decoder (first mip only). SimsRedux: The Sims 3 stores textures as _IMG
/// resources that are plain DDS files (mostly DXT1/DXT5, some DXT3 and uncompressed ARGB/L8).
/// </summary>
public static class Dds
{
    /// <summary>Size of the bytes needed to read the header (DX10 extended header included).</summary>
    public const int HeaderBytes = 148;

    private const uint DDPF_ALPHAPIXELS = 0x1, DDPF_ALPHA = 0x2, DDPF_FOURCC = 0x4, DDPF_RGB = 0x40, DDPF_LUMINANCE = 0x20000;

    /// <summary>Reads width/height/format from the header without decoding pixels. Needs at least 128 bytes.</summary>
    public static bool TryReadInfo(ReadOnlySpan<byte> data, out DdsInfo info)
    {
        info = null!;
        if (data.Length < 128 || data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ') return false;
        int height = (int)U32(data, 12), width = (int)U32(data, 16), mips = (int)U32(data, 28);
        uint pfFlags = U32(data, 80);
        uint fourcc = U32(data, 84);
        int bits = (int)U32(data, 88);
        uint rm = U32(data, 92), gm = U32(data, 96), bm = U32(data, 100), am = U32(data, 104);
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384) return false;
        string fcc = (pfFlags & DDPF_FOURCC) != 0
            ? new string(new[] { (char)(fourcc & 0xFF), (char)((fourcc >> 8) & 0xFF), (char)((fourcc >> 16) & 0xFF), (char)(fourcc >> 24) }).TrimEnd('\0')
            : "";
        int dataOffset = 128;
        DdsFormat fmt = DdsFormat.Unknown;
        if ((pfFlags & DDPF_FOURCC) != 0)
        {
            switch (fcc)
            {
                case "DXT1": fmt = DdsFormat.DXT1; break;
                case "DXT2": case "DXT3": fmt = DdsFormat.DXT3; break;
                case "DXT4": case "DXT5": fmt = DdsFormat.DXT5; break;
                case "DX10":
                    if (data.Length < 148) return false;
                    dataOffset = 148;
                    uint dxgi = U32(data, 128);
                    fmt = dxgi switch
                    {
                        70 or 71 or 72 => DdsFormat.DXT1,
                        73 or 74 or 75 => DdsFormat.DXT3,
                        76 or 77 or 78 => DdsFormat.DXT5,
                        27 or 28 or 29 => DdsFormat.A8B8G8R8,
                        87 or 90 or 91 => DdsFormat.A8R8G8B8,
                        88 or 92 or 93 => DdsFormat.X8R8G8B8,
                        61 => DdsFormat.L8,
                        65 => DdsFormat.A8,
                        _ => DdsFormat.Unknown
                    };
                    if (fmt == DdsFormat.A8B8G8R8) { bits = 32; rm = 0xFF; gm = 0xFF00; bm = 0xFF0000; am = 0xFF000000; }
                    if (fmt == DdsFormat.A8R8G8B8) { bits = 32; rm = 0xFF0000; gm = 0xFF00; bm = 0xFF; am = 0xFF000000; }
                    if (fmt == DdsFormat.X8R8G8B8) { bits = 32; rm = 0xFF0000; gm = 0xFF00; bm = 0xFF; am = 0; }
                    if (fmt == DdsFormat.L8) { bits = 8; rm = 0xFF; }
                    if (fmt == DdsFormat.A8) { bits = 8; am = 0xFF; }
                    break;
                default:
                    // Some writers store D3DFORMAT numbers as the FourCC.
                    fmt = fourcc switch
                    {
                        21 => DdsFormat.A8R8G8B8,
                        22 => DdsFormat.X8R8G8B8,
                        20 => DdsFormat.R8G8B8,
                        50 => DdsFormat.L8,
                        51 => DdsFormat.A8L8,
                        28 => DdsFormat.A8,
                        _ => DdsFormat.Unknown
                    };
                    switch (fmt)
                    {
                        case DdsFormat.A8R8G8B8: bits = 32; rm = 0xFF0000; gm = 0xFF00; bm = 0xFF; am = 0xFF000000; break;
                        case DdsFormat.X8R8G8B8: bits = 32; rm = 0xFF0000; gm = 0xFF00; bm = 0xFF; am = 0; break;
                        case DdsFormat.R8G8B8: bits = 24; rm = 0xFF0000; gm = 0xFF00; bm = 0xFF; am = 0; break;
                        case DdsFormat.L8: bits = 8; rm = 0xFF; am = 0; break;
                        case DdsFormat.A8L8: bits = 16; rm = 0xFF; am = 0xFF00; break;
                        case DdsFormat.A8: bits = 8; rm = 0; am = 0xFF; break;
                    }
                    break;
            }
        }
        else if ((pfFlags & DDPF_LUMINANCE) != 0)
        {
            fmt = bits == 16 ? DdsFormat.A8L8 : DdsFormat.L8;
            if ((pfFlags & DDPF_ALPHAPIXELS) == 0) am = 0;
        }
        else if ((pfFlags & DDPF_RGB) != 0)
        {
            if ((pfFlags & DDPF_ALPHAPIXELS) == 0) am = 0;
            if (bits == 32 && rm == 0xFF0000 && gm == 0xFF00 && bm == 0xFF) fmt = am != 0 ? DdsFormat.A8R8G8B8 : DdsFormat.X8R8G8B8;
            else if (bits == 32 && rm == 0xFF && gm == 0xFF00 && bm == 0xFF0000) fmt = am != 0 ? DdsFormat.A8B8G8R8 : DdsFormat.X8B8G8R8;
            else if (bits == 24 && rm == 0xFF0000 && gm == 0xFF00 && bm == 0xFF) fmt = DdsFormat.R8G8B8;
            else if (bits == 8 || bits == 16 || bits == 24 || bits == 32) fmt = DdsFormat.RgbMasked;
        }
        else if ((pfFlags & DDPF_ALPHA) != 0 && bits == 8)
        {
            fmt = DdsFormat.A8;
        }
        info = new DdsInfo
        {
            Width = width, Height = height, MipCount = Math.Max(1, mips), Format = fmt, FourCC = fcc,
            BitCount = bits, RMask = rm, GMask = gm, BMask = bm, AMask = am, PfFlags = pfFlags, DataOffset = dataOffset
        };
        return true;
    }

    /// <summary>Decodes the first mip level of a DDS file into an RGBA image.</summary>
    public static Image32 Decode(byte[] data)
    {
        if (!TryReadInfo(data, out var info)) throw new InvalidDataException("Not a valid DDS file.");
        if (info.Format == DdsFormat.Unknown) throw new NotSupportedException($"Unsupported DDS pixel format (FourCC '{info.FourCC}', {info.BitCount} bits).");
        var img = new Image32(info.Width, info.Height);
        int w = info.Width, h = info.Height, off = info.DataOffset;
        switch (info.Format)
        {
            case DdsFormat.DXT1:
            case DdsFormat.DXT3:
            case DdsFormat.DXT5:
                {
                    int blockSize = info.Format == DdsFormat.DXT1 ? 8 : 16;
                    int bw = (w + 3) / 4, bh = (h + 3) / 4;
                    if (off + (long)bw * bh * blockSize > data.Length) throw new InvalidDataException("DDS data is truncated.");
                    var block = new byte[16 * 4];
                    for (int by = 0; by < bh; by++)
                        for (int bx = 0; bx < bw; bx++)
                        {
                            int p = off + (by * bw + bx) * blockSize;
                            if (info.Format == DdsFormat.DXT1) DecodeColorBlock(data, p, block, true);
                            else
                            {
                                DecodeColorBlock(data, p + 8, block, false);
                                if (info.Format == DdsFormat.DXT3) DecodeExplicitAlpha(data, p, block);
                                else DecodeInterpolatedAlpha(data, p, block);
                            }
                            for (int py = 0; py < 4; py++)
                                for (int px = 0; px < 4; px++)
                                {
                                    int x = bx * 4 + px, y = by * 4 + py;
                                    if (x >= w || y >= h) continue;
                                    Buffer.BlockCopy(block, (py * 4 + px) * 4, img.Rgba, (y * w + x) * 4, 4);
                                }
                        }
                    break;
                }
            default:
                {
                    int bpp = info.BitCount / 8;
                    if (bpp < 1 || bpp > 4) throw new NotSupportedException($"Unsupported DDS bit count {info.BitCount}.");
                    // SimsRedux: rows of uncompressed DDS are tightly packed in practice (pitch = width*bpp).
                    if (off + (long)w * h * bpp > data.Length) throw new InvalidDataException("DDS data is truncated.");
                    bool lum = info.Format is DdsFormat.L8 or DdsFormat.A8L8;
                    bool alphaOnly = info.Format == DdsFormat.A8;
                    for (int i = 0, p = off; i < w * h; i++, p += bpp)
                    {
                        uint v = 0;
                        for (int k = 0; k < bpp; k++) v |= (uint)data[p + k] << (8 * k);
                        byte r, g, b, a;
                        if (alphaOnly) { r = g = b = 255; a = Extract(v, info.AMask == 0 ? 0xFF : info.AMask); }
                        else if (lum) { r = g = b = Extract(v, info.RMask == 0 ? 0xFF : info.RMask); a = info.AMask != 0 ? Extract(v, info.AMask) : (byte)255; }
                        else
                        {
                            r = Extract(v, info.RMask); g = Extract(v, info.GMask); b = Extract(v, info.BMask);
                            a = info.AMask != 0 ? Extract(v, info.AMask) : (byte)255;
                        }
                        int d = i * 4;
                        img.Rgba[d] = r; img.Rgba[d + 1] = g; img.Rgba[d + 2] = b; img.Rgba[d + 3] = a;
                    }
                    break;
                }
        }
        return img;
    }

    private static uint U32(ReadOnlySpan<byte> d, int o) => (uint)(d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24);

    /// <summary>Extracts a channel by mask and scales it to 0..255.</summary>
    private static byte Extract(uint v, uint mask)
    {
        if (mask == 0) return 0;
        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        uint m = mask >> shift;
        uint c = (v >> shift) & m;
        return m == 255 ? (byte)c : (byte)((c * 255 + m / 2) / m);
    }

    private static void Rgb565(ushort c, out int r, out int g, out int b)
    {
        r = (c >> 11) & 31; g = (c >> 5) & 63; b = c & 31;
        r = (r << 3) | (r >> 2); g = (g << 2) | (g >> 4); b = (b << 3) | (b >> 2);
    }

    private static void DecodeColorBlock(byte[] d, int p, byte[] block, bool dxt1)
    {
        ushort c0 = (ushort)(d[p] | d[p + 1] << 8), c1 = (ushort)(d[p + 2] | d[p + 3] << 8);
        Rgb565(c0, out int r0, out int g0, out int b0);
        Rgb565(c1, out int r1, out int g1, out int b1);
        var pal = new int[4, 4];
        pal[0, 0] = r0; pal[0, 1] = g0; pal[0, 2] = b0; pal[0, 3] = 255;
        pal[1, 0] = r1; pal[1, 1] = g1; pal[1, 2] = b1; pal[1, 3] = 255;
        if (!dxt1 || c0 > c1)
        {
            pal[2, 0] = (2 * r0 + r1) / 3; pal[2, 1] = (2 * g0 + g1) / 3; pal[2, 2] = (2 * b0 + b1) / 3; pal[2, 3] = 255;
            pal[3, 0] = (r0 + 2 * r1) / 3; pal[3, 1] = (g0 + 2 * g1) / 3; pal[3, 2] = (b0 + 2 * b1) / 3; pal[3, 3] = 255;
        }
        else
        {
            pal[2, 0] = (r0 + r1) / 2; pal[2, 1] = (g0 + g1) / 2; pal[2, 2] = (b0 + b1) / 2; pal[2, 3] = 255;
            pal[3, 0] = 0; pal[3, 1] = 0; pal[3, 2] = 0; pal[3, 3] = 0; // 1-bit transparent black
        }
        uint idx = (uint)(d[p + 4] | d[p + 5] << 8 | d[p + 6] << 16 | d[p + 7] << 24);
        for (int i = 0; i < 16; i++)
        {
            int c = (int)((idx >> (2 * i)) & 3);
            block[i * 4] = (byte)pal[c, 0]; block[i * 4 + 1] = (byte)pal[c, 1]; block[i * 4 + 2] = (byte)pal[c, 2]; block[i * 4 + 3] = (byte)pal[c, 3];
        }
    }

    private static void DecodeExplicitAlpha(byte[] d, int p, byte[] block)
    {
        for (int i = 0; i < 16; i++)
        {
            int nib = (d[p + i / 2] >> ((i & 1) * 4)) & 0xF;
            block[i * 4 + 3] = (byte)(nib * 17);
        }
    }

    private static void DecodeInterpolatedAlpha(byte[] d, int p, byte[] block)
    {
        int a0 = d[p], a1 = d[p + 1];
        var pal = new int[8];
        pal[0] = a0; pal[1] = a1;
        if (a0 > a1) for (int i = 1; i < 7; i++) pal[i + 1] = ((7 - i) * a0 + i * a1) / 7;
        else
        {
            for (int i = 1; i < 5; i++) pal[i + 1] = ((5 - i) * a0 + i * a1) / 5;
            pal[6] = 0; pal[7] = 255;
        }
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)d[p + 2 + i] << (8 * i);
        for (int i = 0; i < 16; i++) block[i * 4 + 3] = (byte)pal[(int)((bits >> (3 * i)) & 7)];
    }
}
