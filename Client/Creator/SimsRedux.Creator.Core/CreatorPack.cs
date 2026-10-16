using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimsRedux.Creator;

/// <summary>One item made with the creator, as listed in creator.json.</summary>
public sealed class CreatorItem
{
    /// <summary>"floor", "wall" or "picture".</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("price")] public int Price { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>Path of the built file relative to the pack folder, with forward slashes.</summary>
    [JsonPropertyName("file")] public string File { get; set; } = "";
    /// <summary>Where the texture came from (e.g. "FullBuild0.package#0x00B2D882:0x00000000:0x...", or an image path).</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "";

    // SimsRedux: where to find the texture again, so an item can be edited or re-made later
    // (older items only have the "source" text above; ReadSource() understands that too).
    /// <summary>Full path of the Sims 3 .package the texture came from (or just its file name for older items).</summary>
    [JsonPropertyName("sourcePackage")] public string SourcePackage { get; set; } = "";
    /// <summary>The texture's instance id in that package, as 16 hex digits.</summary>
    [JsonPropertyName("sourceInstance")] public string SourceInstance { get; set; } = "";
    /// <summary>Or: the picture file it came from.</summary>
    [JsonPropertyName("sourceFile")] public string SourceFile { get; set; } = "";
    /// <summary>The colour it was coloured in with ("#RRGGBB"), or "" for none.</summary>
    [JsonPropertyName("tint")] public string Tint { get; set; } = "";

    /// <summary>Where the item's texture came from, from the fields above or the older "source" text.</summary>
    public ItemSource ReadSource()
    {
        if (SourcePackage.Length > 0 || SourceFile.Length > 0)
            return new ItemSource
            {
                Package = SourcePackage.Length > 0 ? SourcePackage : null,
                Instance = ulong.TryParse(SourceInstance.Replace("0x", "", StringComparison.OrdinalIgnoreCase), System.Globalization.NumberStyles.HexNumber, null, out var inst) ? inst : null,
                File = SourceFile.Length > 0 ? SourceFile : null,
                Tint = Tint.Length > 0 ? Tint : null,
            };
        return ItemSource.ParseLegacy(Source);
    }

    /// <summary>Stores where the texture came from (and the plain-text "source" line kept for older Creators).</summary>
    public void WriteSource(ItemSource? src)
    {
        if (src == null) return;
        SourcePackage = src.Package ?? "";
        SourceInstance = src.Instance.HasValue ? src.Instance.Value.ToString("X16") : "";
        SourceFile = src.File ?? "";
        Tint = src.Tint ?? "";
        Source = src.Describe();
    }
}

/// <summary>Where an item's texture came from: a texture in a Sims 3 package, or a picture file; plus a colour.</summary>
public sealed class ItemSource
{
    public string? Package { get; set; }
    public ulong? Instance { get; set; }
    public string? File { get; set; }
    public string? Tint { get; set; }

    public bool IsPackage => Package != null && Instance.HasValue;
    public bool IsFile => File != null;

    /// <summary>One line, e.g. "FullBuild0.package 00000000ABCD1234 colour #AA8866".</summary>
    public string Describe()
    {
        var s = IsPackage ? Package!.Substring(Package!.LastIndexOfAny(new[] { '\\', '/' }) + 1) + " " + Instance!.Value.ToString("X16") : File ?? "";
        if (Tint != null) s += " colour " + Tint;
        return s;
    }

    /// <summary>
    /// Reads the plain "source" text older items have: "Pkg.package 00000000ABCD1234 colour #RRGGBB",
    /// "Pkg.package#0x00B2D882:0x00000000:0x1234", or a picture file path.
    /// </summary>
    public static ItemSource ParseLegacy(string? text)
    {
        var src = new ItemSource();
        var s = (text ?? "").Trim();
        int c = s.LastIndexOf(" colour #", StringComparison.OrdinalIgnoreCase);
        if (c >= 0)
        {
            var hex = s.Substring(c + 8).Trim();
            if (Colours.TryParseHex(hex, out _, out _, out _)) src.Tint = "#" + hex.TrimStart('#').ToUpperInvariant();
            s = s.Substring(0, c).Trim();
        }
        if (s.Length == 0) return src;
        int p = s.IndexOf(".package", StringComparison.OrdinalIgnoreCase);
        if (p > 0)
        {
            src.Package = s.Substring(0, p + 8);
            var rest = s.Substring(p + 8).Trim().TrimStart('#');
            var last = rest.Split(':', ' ').LastOrDefault(x => x.Length > 0) ?? "";
            last = last.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? last.Substring(2) : last;
            if (ulong.TryParse(last, System.Globalization.NumberStyles.HexNumber, null, out var inst)) src.Instance = inst;
            else src.Package = null;
            return src;
        }
        src.File = s;
        return src;
    }
}

/// <summary>
/// An output folder of creator items (normally Content/redux/creator). SimsRedux: writes each built
/// file into a per-kind subfolder, a manifest.txt (one relative path per line, '#' comments, the format
/// the game's mod loader reads) and a creator.json the Creator app uses to reopen/re-export items.
/// </summary>
public sealed class CreatorPack
{
    public const string ManifestFile = "manifest.txt";
    public const string IndexFile = "creator.json";

    public static readonly string[] DefaultHeader =
    {
        "# SimsRedux: items made with the Sims Redux Creator (one file per line, relative to this folder).",
        "# Loaded only on the Test server until promoted to Live.",
    };

    public string Folder { get; }
    public List<CreatorItem> Items { get; } = new();

    private CreatorPack(string folder) { Folder = folder; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Opens (or starts) a pack in a folder; existing creator.json items are loaded.</summary>
    public static CreatorPack Open(string folder)
    {
        Directory.CreateDirectory(folder);
        var pack = new CreatorPack(folder);
        var idx = Path.Combine(folder, IndexFile);
        if (System.IO.File.Exists(idx))
        {
            var doc = JsonSerializer.Deserialize<IndexDoc>(System.IO.File.ReadAllText(idx), Json);
            if (doc?.Items != null) pack.Items.AddRange(doc.Items);
        }
        return pack;
    }

    /// <summary>Subfolder and extension used for each kind.</summary>
    public static (string folder, string ext) Layout(string kind) => kind.ToLowerInvariant() switch
    {
        // SimsRedux: under Downloads/, like the original game's own downloads folder - the game picks up
        // .flr, .wll and .iff files from any Downloads/ folder in the creator and mods manifests
        "floor" => ("Downloads/Floors", ".flr"),
        "wall" => ("Downloads/Walls", ".wll"),
        "picture" => ("Downloads/Objects", ".iff"),
        _ => throw new ArgumentException($"Unknown item kind '{kind}'.")
    };

    /// <summary>A safe lowercase file name for an item ("redux_" + letters/digits/underscores).</summary>
    public static string FileNameFor(string name)
    {
        var sb = new StringBuilder("redux_");
        foreach (var ch in (name ?? "").Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb[^1] != '_') sb.Append('_');
        }
        var s = sb.ToString().TrimEnd('_');
        return s.Length <= 6 ? "redux_item" : (s.Length > 60 ? s[..60] : s);
    }

    /// <summary>
    /// Writes a built file and records it. An existing item with the same kind and name is replaced
    /// (re-export keeps the same file name, and for pictures the same GUID).
    /// </summary>
    public CreatorItem Add(string kind, string name, int price, string description, string source, byte[] data)
        => Add(kind, name, price, description, data, ItemSource.ParseLegacy(source), source);

    /// <summary>Same as above, remembering exactly where the texture came from (for editing it later).</summary>
    public CreatorItem Add(string kind, string name, int price, string description, byte[] data, ItemSource? src)
        => Add(kind, name, price, description, data, src, null);

    private CreatorItem Add(string kind, string name, int price, string description, byte[] data, ItemSource? src, string? sourceText)
    {
        var (sub, ext) = Layout(kind);
        var existing = Items.FirstOrDefault(i => i.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        string rel = existing?.File ?? $"{sub}/{FileNameFor(name)}{ext}";
        if (existing == null)
        {
            // avoid clobbering a different item that happens to sanitize to the same name
            int n = 2;
            while (Items.Any(i => i.File.Equals(rel, StringComparison.OrdinalIgnoreCase)))
                rel = $"{sub}/{FileNameFor(name)}_{n++}{ext}";
        }
        var full = Path.Combine(Folder, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, data);
        var item = existing ?? new CreatorItem();
        item.Kind = kind.ToLowerInvariant(); item.Name = name; item.Price = price; item.Description = description ?? ""; item.File = rel;
        if (sourceText != null) item.Source = sourceText; // (older callers: just the text)
        else item.WriteSource(src);
        if (existing == null) Items.Add(item);
        return item;
    }

    /// <summary>
    /// Changes an item that's already in the pack (edit and re-export). It may get a new kind or name: the
    /// file is then renamed to match and the old one deleted. Throws if another item already has that name.
    /// </summary>
    public CreatorItem Update(CreatorItem item, string kind, string name, int price, string description, byte[] data, ItemSource? src)
    {
        if (!Items.Contains(item)) throw new ArgumentException("That item isn't in this pack.");
        var (sub, ext) = Layout(kind);
        if (Items.Any(i => i != item && i.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"There's already a {kind.ToLowerInvariant()} called \"{name}\".");
        string rel = item.File;
        bool sameKind = item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase);
        if (!sameKind || !FileNameFor(item.Name).Equals(FileNameFor(name), StringComparison.Ordinal))
        {
            rel = $"{sub}/{FileNameFor(name)}{ext}";
            int n = 2;
            while (Items.Any(i => i != item && i.File.Equals(rel, StringComparison.OrdinalIgnoreCase)))
                rel = $"{sub}/{FileNameFor(name)}_{n++}{ext}";
        }
        var full = Path.Combine(Folder, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, data);
        if (!rel.Equals(item.File, StringComparison.OrdinalIgnoreCase))
        {
            var old = Path.Combine(Folder, item.File.Replace('/', Path.DirectorySeparatorChar));
            try { if (System.IO.File.Exists(old)) System.IO.File.Delete(old); } catch (IOException) { }
        }
        item.Kind = kind.ToLowerInvariant(); item.Name = name; item.Price = price; item.Description = description ?? ""; item.File = rel;
        if (src != null) item.WriteSource(src);
        return item;
    }

    /// <summary>Removes an item and deletes its file.</summary>
    public bool Remove(CreatorItem item)
    {
        if (!Items.Remove(item)) return false;
        var full = Path.Combine(Folder, item.File.Replace('/', Path.DirectorySeparatorChar));
        try { if (System.IO.File.Exists(full)) System.IO.File.Delete(full); } catch (IOException) { }
        return true;
    }

    /// <summary>
    /// Writes manifest.txt and creator.json. Comment lines of an existing manifest are kept (or a default
    /// header is written); entries are exactly the files of the current items.
    /// </summary>
    public void Save()
    {
        var manifestPath = Path.Combine(Folder, ManifestFile);
        var header = new List<string>();
        if (System.IO.File.Exists(manifestPath))
            header.AddRange(System.IO.File.ReadAllLines(manifestPath).TakeWhile(l => l.TrimStart().StartsWith("#") || l.Trim().Length == 0).Where(l => l.Trim().Length > 0));
        if (header.Count == 0) header.AddRange(DefaultHeader);
        var lines = header.Concat(Items.Select(i => i.File).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        System.IO.File.WriteAllText(manifestPath, string.Join("\n", lines) + "\n");
        System.IO.File.WriteAllText(Path.Combine(Folder, IndexFile), JsonSerializer.Serialize(new IndexDoc { Version = 1, Items = Items }, Json));
    }

    /// <summary>Reads the file list of a manifest (comments and blank lines skipped).</summary>
    public static List<string> ReadManifest(string path) =>
        System.IO.File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();

    private sealed class IndexDoc
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("items")] public List<CreatorItem> Items { get; set; } = new();
    }
}
