using System.Text;

namespace SimsRedux.Server
{
    /// <summary>
    /// The town's files for players' neighbourhood screens: the neighbourhood, lot positions and
    /// zoning, street names, every house and every Sim's character file, as one download.
    ///
    /// The "stamp" changes whenever any of those files changes (a lot saved, a family moved in),
    /// so the game only downloads the town again when something is different.
    ///
    /// Bundle: "SRXT", int version, string stamp, int count, then per file: string name, int length, bytes.
    /// </summary>
    public static class Town
    {
        public const int BundleVersion = 1;

        private static readonly object Lock = new object();
        private static string CachedStamp;
        private static byte[] CachedBundle;
        private static DateTime StampTime;
        private static string LastStamp;

        /// <summary>The files sent to players (relative names with forward slashes) and where they are.</summary>
        private static List<(string Name, string Full)> Files()
        {
            var list = new List<(string, string)>();
            var root = Lots.TownDir;
            if (!Directory.Exists(root)) return list;
            foreach (var f in Directory.GetFiles(root, "*.iff"))
                list.Add((Path.GetFileName(f), f));
            foreach (var sub in new[] { "Houses", "Characters" })
            {
                var dir = Path.Combine(root, sub);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".iff" && ext != ".bmp") continue;
                    list.Add((sub + "/" + Path.GetFileName(f), f));
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return list;
        }

        /// <summary>Changes whenever a town file is added, removed or changed. Worked out at most once a second.</summary>
        public static string Stamp()
        {
            lock (Lock)
            {
                if (LastStamp != null && (DateTime.UtcNow - StampTime).TotalSeconds < 1) return LastStamp;
                long hash = 17;
                foreach (var (name, full) in Files())
                {
                    try
                    {
                        var info = new FileInfo(full);
                        hash = hash * 31 + name.GetHashCode(StringComparison.Ordinal);
                        hash = hash * 31 + info.Length;
                        hash = hash * 31 + info.LastWriteTimeUtc.Ticks;
                    }
                    catch { }
                }
                LastStamp = ((ulong)hash).ToString("x16");
                StampTime = DateTime.UtcNow;
                return LastStamp;
            }
        }

        /// <summary>The whole town as one download (kept in memory until something changes).</summary>
        public static byte[] Bundle(out string stamp)
        {
            lock (Lock)
            {
                stamp = Stamp();
                if (CachedBundle != null && CachedStamp == stamp) return CachedBundle;

                using var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                {
                    var files = new List<(string, byte[])>();
                    foreach (var (name, full) in Files())
                    {
                        var bytes = ReadShared(full);
                        if (bytes != null) files.Add((name, bytes));
                    }
                    w.Write(Encoding.ASCII.GetBytes("SRXT"));
                    w.Write(BundleVersion);
                    w.Write(stamp);
                    w.Write(files.Count);
                    foreach (var (name, bytes) in files)
                    {
                        w.Write(name);
                        w.Write(bytes.Length);
                        w.Write(bytes);
                    }
                }
                CachedBundle = ms.ToArray();
                CachedStamp = stamp;
                Log.Info("town: packed for players (" + (CachedBundle.Length / 1024) + " KB)");
                return CachedBundle;
            }
        }

        /// <summary>Reads a file even while a lot is writing it (a few tries if it's busy).</summary>
        private static byte[] ReadShared(string path)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var data = new byte[fs.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int n = fs.Read(data, read, data.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return data;
                }
                catch (FileNotFoundException) { return null; }
                catch { Thread.Sleep(100); }
            }
            Log.Warn("town: couldn't read " + Path.GetFileName(path) + " for players");
            return null;
        }
    }
}
