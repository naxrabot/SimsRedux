// SimsRedux: packs the website's shipped mods into a few compressed bundles.
//
// Run with .NET 10:  dotnet run Client/Tools/BundleContent/BundleContent.cs -- <site>/Content
//
// The custom content (Content/redux/mods: objects, walls, floors, skins) is thousands of files that
// barely compress on their own. This puts them into gzip bundles instead - about half the size -
// each named after what's in it, so a browser that already has a bundle keeps using it
// (js/simsfs.js caches them) until it changes.
//
// Bundle format (the same as the server's /api/pack): for each file
//   [u16 path length][path, UTF-8, relative to Content/][u32 size][bytes]
// Rewrites <site>/Content/files.txt: the bundled files come out of it, the bundles go in.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

if (args.Length < 1) { Console.WriteLine("usage: BundleContent <site>/Content"); return 1; }
var content = Path.GetFullPath(args[0]);
var mods = Path.Combine(content, "redux", "mods");
if (!Directory.Exists(mods)) { Console.WriteLine("SimsRedux: no redux/mods folder - nothing to bundle"); return 0; }

var files = Directory.GetFiles(mods, "*", SearchOption.AllDirectories)
    .Select(f => (rel: Path.GetRelativePath(content, f).Replace('\\', '/'), full: f))
    .OrderBy(x => x.rel, StringComparer.Ordinal).ToList();

const long Chunk = 48L * 1024 * 1024; // raw bytes per bundle
var bundles = new List<List<(string rel, string full)>>();
var cur = new List<(string rel, string full)>();
long size = 0;
foreach (var f in files)
{
    var s = new FileInfo(f.full).Length;
    if (cur.Count > 0 && size + s > Chunk) { bundles.Add(cur); cur = new List<(string, string)>(); size = 0; }
    cur.Add(f); size += s;
}
if (cur.Count > 0) bundles.Add(cur);

var names = new List<string>();
long rawTotal = 0, gzTotal = 0;
for (int i = 0; i < bundles.Count; i++)
{
    var data = new MemoryStream();
    var w = new BinaryWriter(data);
    foreach (var (rel, full) in bundles[i])
    {
        var b = File.ReadAllBytes(full);
        var p = Encoding.UTF8.GetBytes(rel);
        w.Write((ushort)p.Length); w.Write(p); w.Write((uint)b.Length); w.Write(b);
    }
    w.Flush();
    var raw = data.ToArray();
    var digest = Convert.ToHexString(SHA1.HashData(raw)).ToLowerInvariant().Substring(0, 10);
    var name = "redux/mods-" + (i + 1) + "-" + digest + ".srxpack.gz";
    using (var o = File.Create(Path.Combine(content, name)))
    using (var gz = new GZipStream(o, CompressionLevel.SmallestSize))
        gz.Write(raw);
    names.Add(name);
    rawTotal += raw.Length; gzTotal += new FileInfo(Path.Combine(content, name)).Length;
}

foreach (var f in files) File.Delete(f.full);
foreach (var d in Directory.GetDirectories(mods, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
    if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
if (!Directory.EnumerateFileSystemEntries(mods).Any()) Directory.Delete(mods);

var list = Path.Combine(content, "files.txt");
var bundled = new HashSet<string>(files.Select(f => f.rel));
var lines = File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0 && !bundled.Contains(l.Replace('\\', '/'))).ToList();
lines.AddRange(names);
File.WriteAllText(list, string.Join("\n", lines) + "\n");
Console.WriteLine($"SimsRedux: bundled {files.Count} mod files: {rawTotal / 1e6:0.0} MB -> {gzTotal / 1e6:0.0} MB in {names.Count} bundles");
return 0;
