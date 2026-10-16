using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimsRedux.Server
{
    /// <summary>
    /// Cross saves: each account's single-player saves (neighbourhood, houses, Sims, settings) kept on
    /// the server, so every device the player logs in on has the same game.
    ///
    /// The website sends the saves as one packed, compressed bundle. The server never opens it beyond
    /// checking it really is one - it just keeps it, numbered, with the last few kept as backups:
    ///   ServerData/live/Saves/&lt;account id&gt;/current.srxs   the latest
    ///   ServerData/live/Saves/&lt;account id&gt;/meta.json      its number, when, which device
    ///   ServerData/live/Saves/&lt;account id&gt;/backups/...    the ones before it (the newest 5)
    /// Only the account itself can read or write its saves.
    /// </summary>
    public static class Saves
    {
        public const int MaxBytes = 48 * 1024 * 1024;
        public const int KeepBackups = 5;

        public class Meta
        {
            public long Version { get; set; }
            public string When { get; set; }
            public string Device { get; set; }
            public long Size { get; set; }
        }

        private static readonly Dictionary<string, object> Locks = new Dictionary<string, object>();
        private static object LockFor(string id) { lock (Locks) { if (!Locks.TryGetValue(id, out var o)) Locks[id] = o = new object(); return o; } }

        /// <summary>The account's folder name: a hash of the account key (never the raw name in a path).</summary>
        private static string IdOf(string accountKey)
        {
            var h = SHA256.HashData(Encoding.UTF8.GetBytes("srx-saves:" + (accountKey ?? "")));
            return Convert.ToHexString(h).Substring(0, 24).ToLowerInvariant();
        }

        private static string Folder(string accountKey) => Path.Combine(Program.DataDir, "Saves", IdOf(accountKey));

        public static Meta Info(string accountKey)
        {
            var dir = Folder(accountKey);
            lock (LockFor(dir))
            {
                return ReadMeta(dir);
            }
        }

        private static Meta ReadMeta(string dir)
        {
            try
            {
                var f = Path.Combine(dir, "meta.json");
                if (!File.Exists(f) || !File.Exists(Path.Combine(dir, "current.srxs"))) return null;
                return JsonSerializer.Deserialize<Meta>(File.ReadAllText(f));
            }
            catch (Exception e) { Log.Warn("saves: couldn't read " + dir + ": " + e.Message); return null; }
        }

        /// <summary>The latest saves (null if there are none).</summary>
        public static byte[] Get(string accountKey, out Meta meta)
        {
            var dir = Folder(accountKey);
            lock (LockFor(dir))
            {
                meta = ReadMeta(dir);
                if (meta == null) return null;
                return File.ReadAllBytes(Path.Combine(dir, "current.srxs"));
            }
        }

        /// <summary>Is this really a saves bundle (compressed, starting "SRXS")?</summary>
        public static bool LooksRight(byte[] body)
        {
            if (body == null || body.Length < 20 || body.Length > MaxBytes) return false;
            if (body[0] != 0x1f || body[1] != 0x8b) return false;
            try
            {
                using var gz = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
                var head = new byte[4];
                int n = 0;
                while (n < 4) { int r = gz.Read(head, n, 4 - n); if (r <= 0) break; n += r; }
                return n == 4 && head[0] == 'S' && head[1] == 'R' && head[2] == 'X' && head[3] == 'S';
            }
            catch { return false; }
        }

        private static string CleanDevice(string device)
        {
            var d = new string((device ?? "").Where(c => c >= 32 && c < 127).ToArray()).Trim();
            if (d.Length == 0) d = "a device";
            return d.Length > 40 ? d.Substring(0, 40) : d;
        }

        /// <summary>
        /// Keeps new saves. baseVersion is the number the device last had: if the server's are newer
        /// (another device saved since), nothing is changed and conflict is true - unless force.
        /// </summary>
        public static Meta Put(string accountKey, byte[] body, long baseVersion, bool force, string device, out bool conflict)
        {
            conflict = false;
            var dir = Folder(accountKey);
            lock (LockFor(dir))
            {
                Directory.CreateDirectory(dir);
                var meta = ReadMeta(dir);
                long current = meta?.Version ?? 0;
                if (!force && current != baseVersion) { conflict = true; return meta; }
                // the one being replaced becomes a backup
                if (meta != null) Backup(dir, File.ReadAllBytes(Path.Combine(dir, "current.srxs")), meta.Version, meta.Device);
                var next = new Meta { Version = current + 1, When = DateTime.UtcNow.ToString("o"), Device = CleanDevice(device), Size = body.Length };
                var tmp = Path.Combine(dir, "current.srxs.saving");
                File.WriteAllBytes(tmp, body);
                File.Move(tmp, Path.Combine(dir, "current.srxs"), true);
                File.WriteAllText(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(next));
                return next;
            }
        }

        /// <summary>Kept only as a backup (the saves a player chose not to keep, when two devices disagreed).</summary>
        public static void PutBackup(string accountKey, byte[] body, string device)
        {
            var dir = Folder(accountKey);
            lock (LockFor(dir))
            {
                Directory.CreateDirectory(dir);
                Backup(dir, body, 0, CleanDevice(device));
            }
        }

        private static void Backup(string dir, byte[] data, long version, string device)
        {
            var b = Path.Combine(dir, "backups");
            Directory.CreateDirectory(b);
            var name = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_v" + version + "_" + new string((device ?? "").Where(char.IsLetterOrDigit).Take(20).ToArray()) + ".srxs";
            File.WriteAllBytes(Path.Combine(b, name), data);
            foreach (var old in Directory.GetFiles(b, "*.srxs").OrderByDescending(f => f).Skip(KeepBackups))
            {
                try { File.Delete(old); } catch { }
            }
        }
    }
}
