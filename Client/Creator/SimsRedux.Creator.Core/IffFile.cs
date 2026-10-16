using System.Text;

namespace SimsRedux.Creator;

/// <summary>
/// One chunk of a Sims 1 IFF file. SimsRedux: chunk payloads are kept as raw bytes; typed
/// readers/writers (Spr2, Spr, Palt, StrList, ...) convert to and from <see cref="Data"/>.
/// </summary>
public sealed class IffChunk
{
    /// <summary>Four-character type code, e.g. "SPR2", "PALT", "STR#", "OBJD".</summary>
    public string Type { get; set; }
    public ushort Id { get; set; }
    public ushort Flags { get; set; }
    public string Label { get; set; }
    public byte[] Data { get; set; }

    // The exact 64 label bytes as read (may contain junk after the terminator); reused on write
    // while Label is unchanged so untouched files round-trip byte-identically.
    internal byte[]? RawLabel;
    internal string? RawLabelText;

    public IffChunk(string type, ushort id, byte[] data, string label = "", ushort flags = 0x10)
    {
        if (type == null || type.Length != 4) throw new ArgumentException("Chunk type must be 4 characters.");
        Type = type; Id = id; Data = data ?? Array.Empty<byte>(); Label = label ?? ""; Flags = flags;
    }

    public IffChunk Clone()
    {
        var c = new IffChunk(Type, Id, (byte[])Data.Clone(), Label, Flags);
        c.RawLabel = RawLabel == null ? null : (byte[])RawLabel.Clone();
        c.RawLabelText = RawLabelText;
        return c;
    }

    public override string ToString() => $"{Type} #{Id} '{Label}' ({Data.Length} bytes)";
}

/// <summary>
/// Reader/writer for Sims 1 IFF files ("IFF FILE 2.5:TYPE FOLLOWED BY SIZE\0 JAMIE DOORNBOS &amp; MAXIS 1").
/// Layout: 60-byte signature, uint32 BE resource-map (rsmp) offset, then chunks: 4-char type,
/// uint32 BE size (including the 76-byte chunk header), uint16 BE id, uint16 BE flags, 64-byte label.
/// SimsRedux: the "rsmp" chunk is only an index; we keep it when reading so files round-trip, but by
/// default drop it when writing (and write offset 0) because FreeSO/Simitone don't need it and a
/// stale map would be wrong after edits.
/// </summary>
public sealed class IffFile
{
    public const string Signature = "IFF FILE 2.5:TYPE FOLLOWED BY SIZE\0 JAMIE DOORNBOS & MAXIS 1";
    private const int HeaderSize = 64;
    private const int ChunkHeaderSize = 76;

    public List<IffChunk> Chunks { get; } = new();

    /// <summary>Raw 60-byte signature as read (kept for byte-identical round trips).</summary>
    private byte[]? _rawSignature;

    /// <summary>When true, rsmp chunks are written back and the header offset points at the first one.</summary>
    public bool PreserveRsmp { get; set; }

    public static IffFile Load(string path) => Read(File.ReadAllBytes(path));

    public static IffFile Read(byte[] data)
    {
        if (data == null || data.Length < HeaderSize) throw new InvalidDataException("File is too small to be an IFF.");
        var sig = Encoding.ASCII.GetString(data, 0, 60).Replace("\0", "");
        if (!sig.StartsWith("IFF FILE 2.", StringComparison.Ordinal) || !sig.Contains("TYPE FOLLOWED BY SIZE"))
            throw new InvalidDataException("Not a Sims IFF file (bad signature).");
        var iff = new IffFile { _rawSignature = data.AsSpan(0, 60).ToArray() };
        int p = HeaderSize;
        while (p + ChunkHeaderSize <= data.Length)
        {
            string type = Encoding.Latin1.GetString(data, p, 4);
            uint size = ReadBE32(data, p + 4);
            if (size < ChunkHeaderSize || p + (long)size > data.Length)
                throw new InvalidDataException($"Chunk '{type}' at 0x{p:X} has invalid size {size}.");
            ushort id = ReadBE16(data, p + 8);
            ushort flags = ReadBE16(data, p + 10);
            var rawLabel = data.AsSpan(p + 12, 64).ToArray();
            int z = Array.IndexOf(rawLabel, (byte)0);
            string label = Encoding.Latin1.GetString(rawLabel, 0, z < 0 ? 64 : z);
            var body = data.AsSpan(p + ChunkHeaderSize, (int)size - ChunkHeaderSize).ToArray();
            iff.Chunks.Add(new IffChunk(type, id, body, label, flags) { RawLabel = rawLabel, RawLabelText = label });
            p += (int)size;
        }
        // SimsRedux: trailing bytes shorter than a chunk header are ignored (FreeSO does the same).
        if (iff.Chunks.Any(c => c.Type == "rsmp")) iff.PreserveRsmp = true;
        return iff;
    }

    public void Save(string path) => File.WriteAllBytes(path, ToBytes());

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        var sig = _rawSignature ?? PadTo(Encoding.ASCII.GetBytes(Signature), 60);
        ms.Write(sig, 0, 60);
        ms.Write(new byte[4]); // rsmp offset, patched below
        long rsmpOffset = 0;
        foreach (var c in Chunks)
        {
            if (c.Type == "rsmp")
            {
                if (!PreserveRsmp) continue;
                if (rsmpOffset == 0) rsmpOffset = ms.Position;
            }
            WriteChunk(ms, c);
        }
        var bytes = ms.ToArray();
        WriteBE32(bytes, 60, (uint)rsmpOffset);
        return bytes;
    }

    private static void WriteChunk(Stream s, IffChunk c)
    {
        var hdr = new byte[ChunkHeaderSize];
        var t = Encoding.Latin1.GetBytes(c.Type);
        Buffer.BlockCopy(t, 0, hdr, 0, 4);
        WriteBE32(hdr, 4, (uint)(c.Data.Length + ChunkHeaderSize));
        WriteBE16(hdr, 8, c.Id);
        WriteBE16(hdr, 10, c.Flags);
        byte[] label;
        if (c.RawLabel != null && c.RawLabelText == c.Label) label = c.RawLabel;
        else
        {
            label = new byte[64];
            var lb = Encoding.Latin1.GetBytes(c.Label ?? "");
            Buffer.BlockCopy(lb, 0, label, 0, Math.Min(63, lb.Length));
        }
        Buffer.BlockCopy(label, 0, hdr, 12, 64);
        s.Write(hdr);
        s.Write(c.Data);
    }

    // ------------------------------------------------------------------ lookup helpers

    public IffChunk? Get(string type, ushort id) => Chunks.FirstOrDefault(c => c.Type == type && c.Id == id);
    public IEnumerable<IffChunk> All(string type) => Chunks.Where(c => c.Type == type);

    /// <summary>Adds a chunk, or replaces the data/label/flags of an existing chunk with the same type and id.</summary>
    public IffChunk Set(string type, ushort id, byte[] data, string? label = null, ushort? flags = null)
    {
        var c = Get(type, id);
        if (c == null)
        {
            c = new IffChunk(type, id, data, label ?? "", flags ?? 0x10);
            Chunks.Add(c);
        }
        else
        {
            c.Data = data;
            if (label != null) c.Label = label;
            if (flags != null) c.Flags = flags.Value;
        }
        return c;
    }

    public bool Remove(string type, ushort id) => Chunks.RemoveAll(c => c.Type == type && c.Id == id) > 0;

    public IffFile Clone()
    {
        var n = new IffFile { _rawSignature = _rawSignature, PreserveRsmp = PreserveRsmp };
        foreach (var c in Chunks) n.Chunks.Add(c.Clone());
        return n;
    }

    private static byte[] PadTo(byte[] b, int n) { var r = new byte[n]; Buffer.BlockCopy(b, 0, r, 0, Math.Min(n, b.Length)); return r; }
    internal static uint ReadBE32(byte[] d, int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
    internal static ushort ReadBE16(byte[] d, int o) => (ushort)(d[o] << 8 | d[o + 1]);
    internal static void WriteBE32(byte[] d, int o, uint v) { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }
    internal static void WriteBE16(byte[] d, int o, ushort v) { d[o] = (byte)(v >> 8); d[o + 1] = (byte)v; }
}
