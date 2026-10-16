namespace SimsRedux.Creator;

/// <summary>
/// One frame of a SPR2 sprite. Pixels are stored as separate planes: palette index, z-buffer and
/// alpha (0..255). A pixel is transparent when its alpha is 0.
/// SimsRedux: mirrors FreeSO's SPR2Frame (tso.files/Formats/IFF/Chunks/SPR2.cs).
/// </summary>
public sealed class Spr2Frame
{
    public const uint FlagColor = 1, FlagZ = 2, FlagAlpha = 4;

    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>1 = colour, 2 = z-buffer, 4 = alpha channel.</summary>
    public uint Flags { get; set; } = FlagColor;
    /// <summary>Per-frame palette id as stored (version 1000 files ignore it and use the sprite's default).</summary>
    public ushort PaletteId { get; set; }
    public ushort TransparentIndex { get; set; }
    public short X { get; set; }
    public short Y { get; set; }
    public byte[] Indices { get; set; } = Array.Empty<byte>();
    public byte[] Z { get; set; } = Array.Empty<byte>();
    public byte[] Alpha { get; set; } = Array.Empty<byte>();

    public bool HasZ => (Flags & FlagZ) != 0;
    public bool HasAlpha => (Flags & FlagAlpha) != 0;

    public Spr2Frame() { }

    public Spr2Frame(int width, int height, uint flags)
    {
        Width = width; Height = height; Flags = flags;
        Indices = new byte[width * height];
        Z = new byte[width * height];
        Alpha = new byte[width * height];
        Array.Fill(Z, (byte)255);
    }

    public Spr2Frame Clone() => new Spr2Frame
    {
        Width = Width, Height = Height, Flags = Flags, PaletteId = PaletteId, TransparentIndex = TransparentIndex, X = X, Y = Y,
        Indices = (byte[])Indices.Clone(), Z = (byte[])Z.Clone(), Alpha = (byte[])Alpha.Clone()
    };

    /// <summary>Renders the frame to RGBA using a palette.</summary>
    public Image32 ToImage(Palette palette)
    {
        if (Width == 0 || Height == 0) return new Image32(1, 1);
        var img = new Image32(Width, Height);
        for (int i = 0; i < Width * Height; i++)
        {
            if (Alpha[i] == 0) continue;
            var (r, g, b) = palette[Indices[i]];
            img.Rgba[i * 4] = r; img.Rgba[i * 4 + 1] = g; img.Rgba[i * 4 + 2] = b; img.Rgba[i * 4 + 3] = Alpha[i];
        }
        return img;
    }

    /// <summary>Renders the z-buffer as a grey image (transparent pixels are black).</summary>
    public Image32 ZToImage()
    {
        var img = new Image32(Math.Max(1, Width), Math.Max(1, Height));
        for (int i = 0; i < Width * Height; i++)
        {
            byte v = Alpha[i] == 0 ? (byte)0 : (byte)(255 - Z[i]);
            img.Rgba[i * 4] = v; img.Rgba[i * 4 + 1] = v; img.Rgba[i * 4 + 2] = v; img.Rgba[i * 4 + 3] = 255;
        }
        return img;
    }

    /// <summary>Fills colour/alpha from an RGBA image using a palette (z is left untouched).</summary>
    public void SetFromImage(Image32 img, Palette palette, bool dither = false)
    {
        if (img.Width != Width || img.Height != Height) throw new ArgumentException("Image size does not match frame size.");
        var idx = palette.Map(img, dither);
        for (int i = 0; i < Width * Height; i++)
        {
            byte a = img.Rgba[i * 4 + 3];
            if (!HasAlpha) a = a >= 128 ? (byte)255 : (byte)0;
            Alpha[i] = a;
            Indices[i] = a == 0 ? (byte)TransparentIndex : idx[i];
            if (a == 0 && HasZ) Z[i] = 255;
        }
    }
}

/// <summary>
/// SPR2 chunk (sprites with optional z-buffer and alpha). Version 1000 layout: uint32 version,
/// uint32 frame count, uint32 default palette id, uint32 offset table, then each frame:
/// uint16 width, uint16 height, uint32 flags, uint16 palette id, uint16 transparent index,
/// int16 y, int16 x, followed by RLE row commands (see <see cref="Spr2.DecodeFrame"/>).
/// Version 1001 (TSO) frames are prefixed by uint32 version and uint32 size and have no offset table.
/// SimsRedux: reader mirrors FreeSO's SPR2.cs; writer mirrors SPR2FrameEncoder.cs (TS1 = version 1000).
/// </summary>
public sealed class Spr2
{
    public uint Version { get; set; } = 1000;
    public uint DefaultPaletteId { get; set; } = 1;
    public List<Spr2Frame> Frames { get; } = new();

    public static Spr2 Read(byte[] data)
    {
        if (data.Length < 12) throw new InvalidDataException("SPR2 chunk is too short.");
        var s = new Spr2 { Version = BitConverter.ToUInt32(data, 0) };
        if (s.Version == 1000)
        {
            int count = BitConverter.ToInt32(data, 4);
            s.DefaultPaletteId = BitConverter.ToUInt32(data, 8);
            if (count < 0 || 12 + count * 4L > data.Length) throw new InvalidDataException("SPR2 frame count is invalid.");
            for (int i = 0; i < count; i++)
            {
                int off = BitConverter.ToInt32(data, 12 + i * 4);
                if (off < 0 || off + 16 > data.Length) throw new InvalidDataException($"SPR2 frame {i} offset is invalid.");
                int pos = off;
                s.Frames.Add(DecodeFrame(data, ref pos, data.Length));
            }
        }
        else if (s.Version == 1001)
        {
            s.DefaultPaletteId = BitConverter.ToUInt32(data, 4);
            int count = BitConverter.ToInt32(data, 8);
            int pos = 12;
            for (int i = 0; i < count; i++)
            {
                if (pos + 8 > data.Length) throw new InvalidDataException("SPR2 (1001) is truncated.");
                int size = BitConverter.ToInt32(data, pos + 4);
                pos += 8;
                int end = pos + size;
                if (size < 16 || end > data.Length) throw new InvalidDataException("SPR2 (1001) frame size is invalid.");
                int p = pos;
                s.Frames.Add(DecodeFrame(data, ref p, end));
                pos = end;
            }
        }
        else throw new InvalidDataException($"Unknown SPR2 version {s.Version}.");
        return s;
    }

    /// <summary>
    /// Decodes one frame's header and RLE data. Row commands are uint16: top 3 bits command, low 13 count.
    /// 0 = row of pixel commands (count = bytes in the row incl. this marker), 4 = skip count transparent rows,
    /// 5 = end. Pixel commands: 1 = count (z, index) pairs, 2 = count (z, index, alpha) triples (padded to
    /// even length), 3 = count transparent pixels, 6 = count indices only (padded to even length).
    /// </summary>
    public static Spr2Frame DecodeFrame(byte[] d, ref int p, int end)
    {
        if (p + 16 > end) throw new InvalidDataException("SPR2 frame header is truncated.");
        var f = new Spr2Frame
        {
            Width = BitConverter.ToUInt16(d, p),
            Height = BitConverter.ToUInt16(d, p + 2),
            Flags = BitConverter.ToUInt32(d, p + 4),
            PaletteId = BitConverter.ToUInt16(d, p + 8),
            TransparentIndex = BitConverter.ToUInt16(d, p + 10),
            Y = BitConverter.ToInt16(d, p + 12),
            X = BitConverter.ToInt16(d, p + 14),
        };
        p += 16;
        int w = f.Width, h = f.Height, n = w * h;
        f.Indices = new byte[n]; f.Z = new byte[n]; f.Alpha = new byte[n];
        Array.Fill(f.Z, (byte)255);
        Array.Fill(f.Indices, (byte)f.TransparentIndex);
        int y = 0;
        int guard = 0;
        while (p + 2 <= end && guard++ < 1_000_000)
        {
            ushort marker = BitConverter.ToUInt16(d, p); p += 2;
            int cmd = marker >> 13, count = marker & 0x1FFF;
            if (cmd == 5) break;
            if (cmd == 4) { y += count; continue; }
            if (cmd != 0) throw new InvalidDataException($"Unknown SPR2 row command {cmd}.");
            int rowEnd = p + count - 2;
            if (count < 2 || rowEnd > end) throw new InvalidDataException("SPR2 row is truncated.");
            if (y >= h) throw new InvalidDataException("SPR2 row past the frame height.");
            int x = 0;
            while (p + 2 <= rowEnd)
            {
                ushort pm = BitConverter.ToUInt16(d, p); p += 2;
                int pc = pm >> 13, pn = pm & 0x1FFF;
                if (x + pn > w) throw new InvalidDataException("SPR2 pixel run past the frame width.");
                switch (pc)
                {
                    case 1:
                    case 2:
                        {
                            int bytesPer = pc == 2 ? 3 : 2;
                            if (p + pn * bytesPer > end) throw new InvalidDataException("SPR2 pixel data truncated.");
                            for (int i = 0; i < pn; i++)
                            {
                                int o = y * w + x;
                                f.Z[o] = d[p]; f.Indices[o] = d[p + 1];
                                // FreeSO scales the 5-bit alpha by 8.2258 (255/31) and truncates.
                                f.Alpha[o] = pc == 2 ? (byte)(d[p + 2] * 8.2258064516129032258064516129032) : (byte)255;
                                p += bytesPer; x++;
                            }
                            if (pc == 2 && (pn * 3) % 2 != 0) p++; // padding
                            break;
                        }
                    case 3:
                        for (int i = 0; i < pn; i++)
                        {
                            int o = y * w + x;
                            f.Alpha[o] = 0; f.Z[o] = 255; f.Indices[o] = (byte)f.TransparentIndex;
                            x++;
                        }
                        break;
                    case 6:
                        if (p + pn > end) throw new InvalidDataException("SPR2 pixel data truncated.");
                        for (int i = 0; i < pn; i++)
                        {
                            int o = y * w + x;
                            f.Indices[o] = d[p]; f.Alpha[o] = 255; f.Z[o] = 0;
                            p++; x++;
                        }
                        if (pn % 2 != 0) p++;
                        break;
                    default:
                        throw new InvalidDataException($"Unknown SPR2 pixel command {pc}.");
                }
            }
            p = rowEnd;
            y++;
        }
        return f;
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        if (Version == 1001)
        {
            bw.Write(1001u); bw.Write(DefaultPaletteId); bw.Write((uint)Frames.Count);
            foreach (var f in Frames)
            {
                var fd = EncodeFrame(f);
                bw.Write(1001u); bw.Write((uint)fd.Length); bw.Write(fd);
            }
        }
        else
        {
            bw.Write(1000u); bw.Write((uint)Frames.Count); bw.Write(DefaultPaletteId);
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
        }
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Encodes a frame (header + RLE). SimsRedux: follows FreeSO's SPR2FrameEncoder command choice
    /// (alpha 0 -> transparent run, partial alpha -> cmd 2, z &gt; 0 -> cmd 1, else cmd 6), but never emits
    /// z/alpha commands for planes the frame's flags don't declare (FreeSO's decoder would crash),
    /// and drops trailing transparent runs like the Maxis/frosti files do.
    /// </summary>
    public static byte[] EncodeFrame(Spr2Frame f)
    {
        using var ms = new MemoryStream();
        var o = new BinaryWriter(ms);
        o.Write((ushort)f.Width); o.Write((ushort)f.Height); o.Write(f.Flags);
        o.Write(f.PaletteId); o.Write(f.TransparentIndex); o.Write(f.Y); o.Write(f.X);
        int blank = 0;
        var row = new MemoryStream();
        var data = new List<byte>();
        for (int y = 0; y < f.Height; y++)
        {
            row.SetLength(0);
            var rw = new BinaryWriter(row);
            int x = 0;
            int lastSolidEnd = 0; // row length (bytes) up to the end of the last non-transparent run
            while (x < f.Width)
            {
                int cmd = Cmd(f, y * f.Width + x);
                int start = x;
                while (x < f.Width && Cmd(f, y * f.Width + x) == cmd && x - start < 0x1FFF) x++;
                int cnt = x - start;
                data.Clear();
                for (int i = start; i < x; i++)
                {
                    int k = y * f.Width + i;
                    switch (cmd)
                    {
                        case 1: data.Add(f.Z[k]); data.Add(f.Indices[k]); break;
                        case 2: data.Add(f.Z[k]); data.Add(f.Indices[k]); data.Add((byte)Math.Ceiling(f.Alpha[k] / 8.2258064516129032258064516129032)); break;
                        case 6: data.Add(f.Indices[k]); break;
                    }
                }
                if (data.Count % 2 != 0) data.Add(0);
                rw.Write((ushort)((cmd << 13) | cnt));
                rw.Write(data.ToArray());
                rw.Flush();
                if (cmd != 3) lastSolidEnd = (int)row.Length;
            }
            if (lastSolidEnd == 0) { blank++; continue; }
            if (blank > 0) { o.Write((ushort)((4 << 13) | blank)); blank = 0; }
            o.Write((ushort)(lastSolidEnd + 2));
            o.Write(row.GetBuffer(), 0, lastSolidEnd);
        }
        if (blank > 0) o.Write((ushort)((4 << 13) | blank));
        o.Write((ushort)(5 << 13));
        o.Flush();
        return ms.ToArray();
    }

    private static int Cmd(Spr2Frame f, int k)
    {
        byte a = f.Alpha[k];
        if (a == 0) return 3;
        if (a < 255 && f.HasAlpha && f.HasZ) return 2;
        if (f.HasZ && f.Z[k] > 0) return 1;
        return 6;
    }
}
