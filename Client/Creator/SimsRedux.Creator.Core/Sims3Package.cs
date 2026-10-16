using System.IO.Compression;
using System.Text;

namespace SimsRedux.Creator;

/// <summary>One index entry of a Sims 3 (DBPF 2.0) package.</summary>
public sealed class Sims3Resource
{
    public uint Type { get; init; }
    public uint Group { get; init; }
    public ulong Instance { get; init; }
    public long Offset { get; init; }
    /// <summary>Size of the stored (possibly compressed) data.</summary>
    public int FileSize { get; init; }
    /// <summary>Size after decompression.</summary>
    public int MemSize { get; init; }
    /// <summary>0x0000 none, 0xFFFF RefPack, 0x5A42 zlib.</summary>
    public ushort Compressed { get; init; }
    public ushort Committed { get; init; }

    public override string ToString() => $"{Type:X8}:{Group:X8}:{Instance:X16} ({FileSize} bytes)";
}

/// <summary>A texture (_IMG resource) found in a Sims 3 package.</summary>
public sealed class Sims3Texture
{
    public Sims3Resource Resource { get; init; } = null!;
    public int Width { get; init; }
    public int Height { get; init; }
    public DdsFormat Format { get; init; }
    /// <summary>Name from the package's _KEY name map, or null.</summary>
    public string? Name { get; init; }
    /// <summary>Path of the package this texture came from.</summary>
    public string PackagePath { get; init; } = "";

    /// <summary>A display label: the name when known, otherwise the instance id.</summary>
    public string DisplayName => Name ?? $"0x{Resource.Instance:X16}";

    public override string ToString() => $"{DisplayName} {Width}x{Height} {Format}";
}

/// <summary>
/// Reader for The Sims 3 DBPF 2.0 packages. SimsRedux: opening only reads the header and index;
/// the file stays open (read-only, shared) and resources are read lazily on demand. Dispose
/// to close the file.
/// </summary>
public sealed class Sims3Package : IDisposable
{
    /// <summary>_IMG resource type (DDS texture).</summary>
    public const uint TextureType = 0x00B2D882;
    /// <summary>_KEY resource type (name map).</summary>
    public const uint NameMapType = 0x0166038C;

    // SimsRedux: sanity limits so a corrupt index can't make us allocate gigabytes or loop for ages.
    private const int MaxEntries = 2_000_000;
    private const int MaxResourceSize = 256 * 1024 * 1024;

    private readonly FileStream? _file;
    private readonly byte[]? _memory;
    private readonly object _lock = new();
    private Dictionary<ulong, string>? _names;

    public string Path { get; }
    public int MajorVersion { get; }
    public int MinorVersion { get; }
    public IReadOnlyList<Sims3Resource> Resources { get; }

    private Sims3Package(string path, FileStream? file, byte[]? memory)
    {
        Path = path;
        _file = file;
        _memory = memory;
        var header = ReadAt(0, 96);
        if (header.Length < 96 || header[0] != 'D' || header[1] != 'B' || header[2] != 'P' || header[3] != 'F')
            throw new InvalidDataException($"{path}: not a DBPF package.");
        MajorVersion = BitConverter.ToInt32(header, 4);
        MinorVersion = BitConverter.ToInt32(header, 8);
        if (MajorVersion != 2)
            throw new InvalidDataException($"{path}: DBPF version {MajorVersion}.{MinorVersion} is not a Sims 3 package (expected 2.0).");
        int count = BitConverter.ToInt32(header, 0x24);
        int indexSize = BitConverter.ToInt32(header, 0x2C);
        long indexOffset = BitConverter.ToUInt32(header, 0x40);
        if (indexOffset == 0) indexOffset = BitConverter.ToUInt32(header, 0x28);
        long length = Length;
        if (count < 0 || count > MaxEntries) throw new InvalidDataException($"{path}: bad index entry count {count}.");
        if (count == 0) { Resources = Array.Empty<Sims3Resource>(); return; }
        if (indexSize <= 0 || indexOffset <= 0 || indexOffset + indexSize > length)
            throw new InvalidDataException($"{path}: index is outside the file.");
        var idx = ReadAt(indexOffset, indexSize);
        Resources = ParseIndex(idx, count, length, path);
    }

    private static List<Sims3Resource> ParseIndex(byte[] idx, int count, long fileLength, string path)
    {
        int p = 0;
        uint Next()
        {
            if (p + 4 > idx.Length) throw new InvalidDataException($"{path}: index is truncated.");
            uint v = BitConverter.ToUInt32(idx, p); p += 4; return v;
        }
        uint flags = Next();
        uint cType = 0, cGroup = 0, cInstHi = 0;
        if ((flags & 1) != 0) cType = Next();
        if ((flags & 2) != 0) cGroup = Next();
        if ((flags & 4) != 0) cInstHi = Next();
        var list = new List<Sims3Resource>(count);
        for (int i = 0; i < count; i++)
        {
            uint type = (flags & 1) != 0 ? cType : Next();
            uint group = (flags & 2) != 0 ? cGroup : Next();
            uint instHi = (flags & 4) != 0 ? cInstHi : Next();
            uint instLo = Next();
            uint offset = Next();
            uint fileSize = Next() & 0x7FFFFFFF;
            uint memSize = Next();
            uint comp = Next();
            var r = new Sims3Resource
            {
                Type = type, Group = group, Instance = ((ulong)instHi << 32) | instLo,
                Offset = offset, FileSize = (int)fileSize, MemSize = (int)Math.Min(memSize, int.MaxValue),
                Compressed = (ushort)(comp & 0xFFFF), Committed = (ushort)(comp >> 16)
            };
            // SimsRedux: skip (rather than fail on) entries that point outside the file; deleted
            // entries in some packages have size 0.
            if (r.FileSize > 0 && r.Offset + r.FileSize <= fileLength) list.Add(r);
        }
        return list;
    }

    /// <summary>Opens a package file and reads its index. Throws InvalidDataException for non-packages.</summary>
    public static Sims3Package Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.RandomAccess);
        try { return new Sims3Package(path, fs, null); }
        catch { fs.Dispose(); throw; }
    }

    /// <summary>Opens a package held in memory (used by tests and for packages extracted from archives).</summary>
    public static Sims3Package FromBytes(byte[] data, string name = "(memory)") => new Sims3Package(name, null, data);

    private long Length => _file?.Length ?? _memory!.Length;

    private byte[] ReadAt(long offset, int size)
    {
        if (size < 0 || offset < 0 || offset + size > Length) throw new InvalidDataException($"{Path}: read past end of file.");
        var buf = new byte[size];
        if (_memory != null) { Buffer.BlockCopy(_memory, (int)offset, buf, 0, size); return buf; }
        lock (_lock)
        {
            _file!.Position = offset;
            int got = 0;
            while (got < size)
            {
                int n = _file.Read(buf, got, size - got);
                if (n <= 0) throw new InvalidDataException($"{Path}: unexpected end of file.");
                got += n;
            }
        }
        return buf;
    }

    /// <summary>Reads and decompresses a resource.</summary>
    public byte[] Read(Sims3Resource r) => Read(r, int.MaxValue);

    /// <summary>
    /// Reads a resource, decompressing at least <paramref name="maxBytes"/> bytes (or everything).
    /// SimsRedux: used to peek at DDS headers without decoding whole textures.
    /// </summary>
    public byte[] Read(Sims3Resource r, int maxBytes)
    {
        if (r.FileSize > MaxResourceSize || r.MemSize > MaxResourceSize) throw new InvalidDataException($"{Path}: resource {r} is too large.");
        switch (r.Compressed)
        {
            case 0x0000:
                return ReadAt(r.Offset, Math.Min(r.FileSize, maxBytes));
            case 0xFFFF:
                {
                    var raw = ReadAt(r.Offset, r.FileSize);
                    return RefPack.Decompress(raw, maxBytes);
                }
            case 0x5A42:
                {
                    var raw = ReadAt(r.Offset, r.FileSize);
                    using var z = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
                    int want = Math.Min(r.MemSize > 0 ? r.MemSize : MaxResourceSize, maxBytes);
                    var res = new byte[want];
                    int got = 0;
                    while (got < want)
                    {
                        int n = z.Read(res, got, want - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got < want) Array.Resize(ref res, got);
                    return res;
                }
            default:
                throw new NotSupportedException($"{Path}: unsupported compression type 0x{r.Compressed:X4} for {r}.");
        }
    }

    /// <summary>Instance-to-name map from the package's _KEY resources (empty if none).</summary>
    public IReadOnlyDictionary<ulong, string> Names
    {
        get
        {
            if (_names != null) return _names;
            var names = new Dictionary<ulong, string>();
            foreach (var r in Resources)
            {
                if (r.Type != NameMapType) continue;
                try { ParseNameMap(Read(r), names); }
                catch (Exception) { /* SimsRedux: a broken name map just means no names. */ }
            }
            _names = names;
            return names;
        }
    }

    /// <summary>Parses a _KEY name map: uint32 version, uint32 count, then (uint64 instance, uint32 length, ASCII name).</summary>
    public static void ParseNameMap(byte[] data, Dictionary<ulong, string> into)
    {
        if (data.Length < 8) return;
        int count = BitConverter.ToInt32(data, 4);
        int p = 8;
        for (int i = 0; i < count && p + 12 <= data.Length; i++)
        {
            ulong inst = BitConverter.ToUInt64(data, p);
            int len = BitConverter.ToInt32(data, p + 8);
            p += 12;
            if (len < 0 || p + len > data.Length) break;
            into[inst] = Encoding.ASCII.GetString(data, p, len);
            p += len;
        }
    }

    /// <summary>
    /// Lists every _IMG texture with its size and format, read from the DDS header only
    /// (for compressed resources only the start of the stream is decompressed where possible).
    /// Unreadable entries are skipped.
    /// </summary>
    public IEnumerable<Sims3Texture> ListTextures()
    {
        var names = Names;
        foreach (var r in Resources)
        {
            if (r.Type != TextureType) continue;
            DdsInfo? info = null;
            try
            {
                var head = Read(r, Dds.HeaderBytes);
                if (!Dds.TryReadInfo(head, out info)) info = null;
            }
            catch (Exception) { info = null; }
            if (info == null) continue;
            names.TryGetValue(r.Instance, out var name);
            yield return new Sims3Texture { Resource = r, Width = info.Width, Height = info.Height, Format = info.Format, Name = name, PackagePath = Path };
        }
    }

    /// <summary>Decodes a texture to RGBA.</summary>
    public Image32 LoadTexture(Sims3Texture t) => Dds.Decode(Read(t.Resource));

    public void Dispose() => _file?.Dispose();

    // ------------------------------------------------------------------ writer (tests / tools)

    /// <summary>A resource to be written by <see cref="Write"/>.</summary>
    public sealed class NewResource
    {
        public uint Type; public uint Group; public ulong Instance;
        public byte[] Data = Array.Empty<byte>();
        /// <summary>0x0000 none, 0xFFFF RefPack, 0x5A42 zlib.</summary>
        public ushort Compression;
    }

    /// <summary>
    /// Writes a minimal DBPF 2.0 package (no constant index fields). SimsRedux: only used for tests
    /// and debugging; the creator never writes to the user's Sims 3 install.
    /// </summary>
    public static byte[] Write(IEnumerable<NewResource> resources, uint indexFlags = 0)
    {
        var list = resources.ToList();
        using var ms = new MemoryStream();
        ms.Write(new byte[96]);
        var entries = new List<(NewResource r, long off, int fsize)>();
        foreach (var r in list)
        {
            byte[] stored = r.Compression switch
            {
                0 => r.Data,
                0xFFFF => RefPack.Compress(r.Data),
                0x5A42 => ZlibCompress(r.Data),
                _ => throw new NotSupportedException()
            };
            long off = ms.Position;
            ms.Write(stored);
            entries.Add((r, off, stored.Length));
        }
        long indexOffset = ms.Position;
        var bw = new BinaryWriter(ms);
        bw.Write(indexFlags);
        if ((indexFlags & 1) != 0) bw.Write(list.Count > 0 ? list[0].Type : 0u);
        if ((indexFlags & 2) != 0) bw.Write(list.Count > 0 ? list[0].Group : 0u);
        if ((indexFlags & 4) != 0) bw.Write(list.Count > 0 ? (uint)(list[0].Instance >> 32) : 0u);
        foreach (var (r, off, fsize) in entries)
        {
            if ((indexFlags & 1) == 0) bw.Write(r.Type);
            if ((indexFlags & 2) == 0) bw.Write(r.Group);
            if ((indexFlags & 4) == 0) bw.Write((uint)(r.Instance >> 32));
            bw.Write((uint)r.Instance);
            bw.Write((uint)off);
            bw.Write((uint)fsize | 0x80000000u);
            bw.Write((uint)r.Data.Length);
            bw.Write(r.Compression);
            bw.Write((ushort)1);
        }
        bw.Flush();
        long indexSize = ms.Position - indexOffset;
        var bytes = ms.ToArray();
        Encoding.ASCII.GetBytes("DBPF").CopyTo(bytes, 0);
        BitConverter.GetBytes(2).CopyTo(bytes, 4);
        BitConverter.GetBytes(0).CopyTo(bytes, 8);
        BitConverter.GetBytes(entries.Count).CopyTo(bytes, 0x24);
        BitConverter.GetBytes((int)indexSize).CopyTo(bytes, 0x2C);
        BitConverter.GetBytes(3).CopyTo(bytes, 0x3C); // index minor version (as written by EA tools)
        BitConverter.GetBytes((uint)indexOffset).CopyTo(bytes, 0x40);
        return bytes;
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data);
        return ms.ToArray();
    }
}

/// <summary>Finds Sims 3 packages in an install folder.</summary>
public static class Sims3Library
{
    /// <summary>
    /// Returns every *.package found under a "GameData/Shared/Packages" folder anywhere below
    /// <paramref name="sims3InstallFolder"/> (base game and every expansion/stuff pack). If no such
    /// folders exist (e.g. the user pointed at a Packages folder directly), all *.package files below
    /// the folder are returned. Unreadable folders are skipped silently.
    /// </summary>
    public static IReadOnlyList<string> FindPackages(string sims3InstallFolder)
    {
        var all = new List<string>();
        if (string.IsNullOrWhiteSpace(sims3InstallFolder) || !Directory.Exists(sims3InstallFolder)) return all;
        var stack = new Stack<string>();
        stack.Push(sims3InstallFolder);
        int visited = 0;
        while (stack.Count > 0 && visited < 100_000)
        {
            var dir = stack.Pop();
            visited++;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.package")) all.Add(f);
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    // SimsRedux: don't follow junctions/symlinks (avoids cycles).
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    stack.Push(d);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        static bool InGameData(string p)
        {
            var norm = p.Replace('\\', '/');
            return norm.Contains("/GameData/Shared/Packages/", StringComparison.OrdinalIgnoreCase);
        }
        var gameData = all.Where(InGameData).ToList();
        var result = gameData.Count > 0 ? gameData : all;
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}
