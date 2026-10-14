using System.Text.RegularExpressions;

namespace SimsRedux.Server
{
    /// <summary>
    /// The Sims 1 files on this PC that Admin and Tester accounts can download, so they can play
    /// in a browser without installing the game. Only files in the list can ever be sent: a request
    /// is looked up in the list, never turned into a path on disk.
    /// </summary>
    public class GameFiles
    {
        // the same files the website skips when a player picks their own folder (js/simsfs.js)
        private static readonly Regex SkipExt = new Regex(@"\.(exe|dll|ico|msi|cab|hdr|inx|chm|pdf|lnk|url|tmp|log)$", RegexOptions.IgnoreCase);

        public class Entry
        {
            public string Path;      // relative, with forward slashes, e.g. "GameData/UIText.iff"
            public string FullPath;
            public long Size;
        }

        private readonly object Lock = new object();
        private Dictionary<string, Entry> ByKey = new Dictionary<string, Entry>();
        private List<Entry> Ordered = new List<Entry>();

        public string Folder { get; private set; } = "";
        public string Root { get; private set; } = "";
        public string Problem { get; private set; } = "no game folder set";
        public bool Ready { get { lock (Lock) return Ordered.Count > 0; } }
        public int Count { get { lock (Lock) return Ordered.Count; } }
        public long TotalBytes { get { lock (Lock) return Ordered.Sum(e => e.Size); } }

        /// <summary>Finds the Sims folder (the one with GameData\UIText.iff) and lists what's in it.</summary>
        public void Scan(string folder)
        {
            Folder = folder ?? "";
            var list = new List<Entry>();
            string root = null, problem = null;
            try
            {
                if (string.IsNullOrWhiteSpace(folder)) problem = "no game folder set (use /files folder <path>, or the panel's Settings)";
                else if (!Directory.Exists(folder)) problem = "the game folder doesn't exist: " + folder;
                else
                {
                    root = FindSimsRoot(folder);
                    if (root == null) problem = "no GameData\\UIText.iff in " + folder + " (or the folder just inside it) - is that the Sims folder?";
                }
                if (root != null)
                {
                    foreach (var full in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System }))
                    {
                        var rel = System.IO.Path.GetRelativePath(root, full).Replace('\\', '/');
                        if (SkipExt.IsMatch(rel) || rel.StartsWith("..")) continue;
                        long size;
                        try { size = new FileInfo(full).Length; } catch { continue; }
                        list.Add(new Entry { Path = rel, FullPath = full, Size = size });
                    }
                    list.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
                    if (list.Count == 0) problem = "the game folder is empty";
                }
            }
            catch (Exception e) { problem = "couldn't read the game folder: " + e.Message; }

            lock (Lock)
            {
                Root = root ?? "";
                Problem = problem;
                Ordered = problem == null ? list : new List<Entry>();
                ByKey = Ordered.GroupBy(e => e.Path.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
            }
            if (problem == null) Log.Info("game files: " + list.Count + " files, " + (TotalBytes / 1048576) + " MB, from " + root);
            else Log.Warn("game files: " + problem);
        }

        private static string FindSimsRoot(string folder)
        {
            bool IsRoot(string d)
            {
                var gd = Directory.EnumerateDirectories(d).FirstOrDefault(x => string.Equals(System.IO.Path.GetFileName(x), "GameData", StringComparison.OrdinalIgnoreCase));
                return gd != null && Directory.EnumerateFiles(gd).Any(f => string.Equals(System.IO.Path.GetFileName(f), "UIText.iff", StringComparison.OrdinalIgnoreCase));
            }
            if (IsRoot(folder)) return folder;
            foreach (var d in Directory.EnumerateDirectories(folder)) if (IsRoot(d)) return d;
            return null;
        }

        public List<Entry> List() { lock (Lock) return Ordered.ToList(); }

        public Entry Get(string relPath)
        {
            if (string.IsNullOrEmpty(relPath)) return null;
            lock (Lock) return ByKey.TryGetValue(relPath.Replace('\\', '/').TrimStart('/').ToLowerInvariant(), out var e) ? e : null;
        }
    }
}
