using System.Text;

namespace SimsRedux.Server
{
    /// <summary>
    /// Everything the server says goes to the console (which the panel shows) and to server.log.
    /// </summary>
    public static class Log
    {
        private static readonly object Lock = new object();
        private static StreamWriter File;

        public static void Open(string path)
        {
            try
            {
                // keep the log from growing forever: start fresh past 20 MB, keeping one old copy
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 20 * 1024 * 1024)
                {
                    var old = path + ".old";
                    if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                    System.IO.File.Move(path, old);
                }
                File = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception e)
            {
                Console.WriteLine("(could not open " + path + ": " + e.Message + ")");
            }
        }

        /// <summary>A line for the console and the log file.</summary>
        public static void Info(string text) { Write(text); }

        /// <summary>A problem: same as Info, marked so it stands out in the log.</summary>
        public static void Warn(string text) { Write("! " + text); }

        /// <summary>Command output: shown, but not timestamped in the console.</summary>
        public static void Reply(string text)
        {
            lock (Lock)
            {
                foreach (var line in (text ?? "").Replace("\r", "").Split('\n')) Console.WriteLine(line);
                try { File?.WriteLine(text); } catch { }
            }
        }

        private static void Write(string text)
        {
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            lock (Lock)
            {
                Console.WriteLine(stamp + " · " + text);
                try { File?.WriteLine(DateTime.Now.ToString("yyyy-MM-dd ") + stamp + "  " + text); } catch { }
            }
        }

        public static void Close()
        {
            lock (Lock) { try { File?.Dispose(); } catch { } File = null; }
        }
    }
}
