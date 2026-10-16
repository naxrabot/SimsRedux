namespace SimsRedux.Creator;

/// <summary>A frame of an old-style SPR# sprite: palette indices plus an opacity mask (no z, no alpha).</summary>
public sealed class SprFrame
{
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Indices { get; set; } = Array.Empty<byte>();
    /// <summary>1 where a pixel is drawn, 0 where it is transparent.</summary>
    public byte[] Opaque { get; set; } = Array.Empty<byte>();

    public SprFrame() { }
    public SprFrame(int width, int height)
    {
        Width = width; Height = height;
        Indices = new byte[width * height];
        Opaque = new byte[width * height];
    }

    public Image32 ToImage(Palette palette)
    {
        var img = new Image32(Math.Max(1, Width), Math.Max(1, Height));
        for (int i = 0; i < Width * Height; i++)
        {
            if (Opaque[i] == 0) continue;
            var (r, g, b) = palette[Indices[i]];
            img.Rgba[i * 4] = r; img.Rgba[i * 4 + 1] = g; img.Rgba[i * 4 + 2] = b; img.Rgba[i * 4 + 3] = 255;
        }
        return img;
    }

    /// <summary>Sets indices from an RGBA image (alpha &lt; 128 = transparent).</summary>
    public void SetFromImage(Image32 img, Palette palette, bool dither = false)
    {
        if (img.Width != Width || img.Height != Height) throw new ArgumentException("Image size does not match frame size.");
        var idx = palette.Map(img, dither, 128);
        for (int i = 0; i < Width * Height; i++)
        {
            Opaque[i] = img.Rgba[i * 4 + 3] >= 128 ? (byte)1 : (byte)0;
            Indices[i] = Opaque[i] != 0 ? idx[i] : (byte)0;
        }
    }
}

/// <summary>
/// SPR# chunk: the old paletted sprite format used by Sims 1 walls (and some objects).
/// Header: version (uint32; if the first uint16 is 0 the chunk is big-endian), frame count, palette
/// id, then (for versions other than 1001) an offset table. Each frame: uint32 reserved, uint16 height,
/// uint16 width, then byte-pair commands: [cmd, count]. Row commands: 0x00/0x10 start marker (ignored),
/// 0x04 row (count = bytes in the row including the 2-byte header), 0x05 end, 0x09 skip count rows.
/// Pixel commands inside a row: 0x01 skip count transparent pixels, 0x02 count pixels of one colour
/// (index, padding byte), 0x03 count literal indices (padded to even length).
/// SimsRedux: mirrors FreeSO's reader in tso.files/Formats/IFF/Chunks/SPR.cs; FreeSO has no SPR#
/// writer, so the encoder below produces exactly what that reader expects (little-endian, version 1000).
/// </summary>
public sealed class Spr
{
    /// <summary>SimsRedux: FreeSO ignores the 0x00 start marker; we don't write it (set true if a tool needs it).</summary>
    public static bool WriteStartMarker = false;

    public uint Version { get; set; } = 1000;
    public ushort PaletteId { get; set; } = 1;
    public bool BigEndian { get; private set; }
    public List<SprFrame> Frames { get; } = new();

    public static Spr Read(byte[] data)
    {
        if (data.Length < 12) throw new InvalidDataException("SPR# chunk is too short.");
        var s = new Spr();
        int v1 = data[0] | data[1] << 8, v2 = data[2] | data[3] << 8;
        bool be = v1 == 0;
        uint U32(int o) => be
            ? (uint)(data[o] << 24 | data[o + 1] << 16 | data[o + 2] << 8 | data[o + 3])
            : BitConverter.ToUInt32(data, o);
        ushort U16(int o) => be ? (ushort)(data[o] << 8 | data[o + 1]) : BitConverter.ToUInt16(data, o);
        s.BigEndian = be;
        s.Version = be ? (uint)(((v2 | 0xFF00) >> 8) | ((v2 & 0xFF) << 8)) : (uint)v1;
        int count = (int)U32(4);
        s.PaletteId = (ushort)U32(8);
        if (count < 0 || count > 100000) throw new InvalidDataException("SPR# frame count is invalid.");
        if (s.Version != 1001)
        {
            if (12 + count * 4L > data.Length) throw new InvalidDataException("SPR# offset table is truncated.");
            for (int i = 0; i < count; i++)
            {
                int off = (int)U32(12 + i * 4);
                if (off < 0 || off + 8 > data.Length) throw new InvalidDataException($"SPR# frame {i} offset is invalid.");
                int p = off;
                s.Frames.Add(DecodeFrame(data, ref p, U16));
            }
        }
        else
        {
            int p = 12;
            while (p + 16 <= data.Length)
            {
                int size = (int)U32(p + 4);
                p += 8;
                int start = p;
                s.Frames.Add(DecodeFrame(data, ref p, U16));
                if (size >= 8) p = Math.Max(p, start + size - 8);
            }
        }
        return s;
    }

    private static SprFrame DecodeFrame(byte[] d, ref int p, Func<int, ushort> u16)
    {
        // uint32 reserved, uint16 height, uint16 width
        int height = u16(p + 4), width = u16(p + 6);
        p += 8;
        var f = new SprFrame(width, height);
        int y = 0;
        int guard = 0;
        while (guard++ < 1_000_000)
        {
            if (p + 2 > d.Length) throw new InvalidDataException("SPR# frame data is truncated.");
            int cmd = d[p], count = d[p + 1];
            p += 2;
            if (cmd == 0x00 || cmd == 0x10) continue;
            if (cmd == 0x05) break;
            if (cmd == 0x09) { y += count; continue; }
            if (cmd != 0x04) throw new InvalidDataException($"Unknown SPR# row command 0x{cmd:X2}.");
            int bytes = count - 2;
            int x = 0;
            while (bytes > 0)
            {
                if (p + 2 > d.Length) throw new InvalidDataException("SPR# row is truncated.");
                int pc = d[p], pn = d[p + 1];
                p += 2; bytes -= 2;
                switch (pc)
                {
                    case 0x01: x += pn; break;
                    case 0x02:
                        {
                            if (p + 2 > d.Length) throw new InvalidDataException("SPR# row is truncated.");
                            byte idx = d[p]; p += 2; bytes -= 2;
                            for (int j = 0; j < pn; j++, x++) Put(f, x, y, idx);
                            break;
                        }
                    case 0x03:
                        if (p + pn > d.Length) throw new InvalidDataException("SPR# row is truncated.");
                        for (int j = 0; j < pn; j++, x++) Put(f, x, y, d[p + j]);
                        p += pn; bytes -= pn;
                        if (pn % 2 != 0) { p++; bytes--; }
                        break;
                    default:
                        throw new InvalidDataException($"Unknown SPR# pixel command 0x{pc:X2}.");
                }
            }
            y++;
        }
        return f;
    }

    private static void Put(SprFrame f, int x, int y, byte idx)
    {
        if (x < 0 || y < 0 || x >= f.Width || y >= f.Height) throw new InvalidDataException("SPR# pixel outside the frame.");
        int o = y * f.Width + x;
        f.Indices[o] = idx; f.Opaque[o] = 1;
    }

    /// <summary>Writes a little-endian version 1000 SPR# chunk.</summary>
    public byte[] Write()
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(1000u); bw.Write((uint)Frames.Count); bw.Write((uint)PaletteId);
        long table = ms.Position;
        for (int i = 0; i < Frames.Count; i++) bw.Write(0u);
        var offsets = new uint[Frames.Count];
        for (int i = 0; i < Frames.Count; i++)
        {
            offsets[i] = (uint)ms.Position;
            bw.Write(EncodeFrame(Frames[i]));
        }
        bw.Flush();
        ms.Position = table;
        foreach (var o in offsets) bw.Write(o);
        bw.Flush();
        return ms.ToArray();
    }

    public static byte[] EncodeFrame(SprFrame f)
    {
        if (f.Width > 255 * 2 - 8) throw new ArgumentException("SPR# frames wider than ~500 pixels can't be encoded.");
        var o = new List<byte>();
        o.AddRange(BitConverter.GetBytes(0u));
        o.AddRange(BitConverter.GetBytes((ushort)f.Height));
        o.AddRange(BitConverter.GetBytes((ushort)f.Width));
        if (WriteStartMarker) { o.Add(0x00); o.Add(0x00); }
        int blank = 0;
        for (int y = 0; y < f.Height; y++)
        {
            var row = EncodeRow(f, y, allowFill: true);
            if (row != null && row.Count + 2 > 255) row = EncodeRow(f, y, allowFill: false);
            if (row != null && row.Count + 2 > 255) throw new InvalidOperationException("SPR# row too long to encode.");
            if (row == null) { blank++; continue; }
            while (blank > 0) { int n = Math.Min(255, blank); o.Add(0x09); o.Add((byte)n); blank -= n; }
            o.Add(0x04); o.Add((byte)(row.Count + 2));
            o.AddRange(row);
        }
        while (blank > 0) { int n = Math.Min(255, blank); o.Add(0x09); o.Add((byte)n); blank -= n; }
        o.Add(0x05); o.Add(0x00);
        return o.ToArray();
    }

    /// <summary>Encodes one row's pixel commands, or null if the row is fully transparent.</summary>
    private static List<byte>? EncodeRow(SprFrame f, int y, bool allowFill)
    {
        int w = f.Width;
        int last = -1;
        for (int x = 0; x < w; x++) if (f.Opaque[y * w + x] != 0) last = x;
        if (last < 0) return null;
        var r = new List<byte>();
        int xi = 0;
        while (xi <= last)
        {
            int k = y * w + xi;
            if (f.Opaque[k] == 0)
            {
                int s = xi;
                while (xi <= last && f.Opaque[y * w + xi] == 0 && xi - s < 255) xi++;
                r.Add(0x01); r.Add((byte)(xi - s));
                continue;
            }
            // run of identical colours?
            int run = 1;
            while (xi + run <= last && run < 255 && f.Opaque[k + run] != 0 && f.Indices[k + run] == f.Indices[k]) run++;
            if (allowFill && run >= 4)
            {
                r.Add(0x02); r.Add((byte)run); r.Add(f.Indices[k]); r.Add(0);
                xi += run;
                continue;
            }
            // literal run until a transparent pixel or (when allowed) a long fill run starts
            int st = xi;
            while (xi <= last && f.Opaque[y * w + xi] != 0 && xi - st < 254)
            {
                if (allowFill && xi > st)
                {
                    int rr = 1, kk = y * w + xi;
                    while (xi + rr <= last && rr < 4 && f.Opaque[kk + rr] != 0 && f.Indices[kk + rr] == f.Indices[kk]) rr++;
                    if (rr >= 4) break;
                }
                xi++;
            }
            int n = xi - st;
            r.Add(0x03); r.Add((byte)n);
            for (int i = 0; i < n; i++) r.Add(f.Indices[y * w + st + i]);
            if (n % 2 != 0) r.Add(0);
        }
        return r;
    }
}
