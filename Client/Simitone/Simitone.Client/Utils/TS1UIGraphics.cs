using FSO.Client;
using FSO.Content;
using FSO.Files;
using FSO.Files.FAR1;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: loads original The Sims 1 menu art straight from the player's own
    /// UIGraphics/UIGraphics.far. Nothing is copied into the project.
    /// Paths use the names inside the archive, e.g. "Nbhd/CreateACharBack.bmp".
    /// Lookups ignore case and accept / or \.
    /// </summary>
    public static class TS1UIGraphics
    {
        private static FAR1Archive Archive;
        private static Dictionary<string, FarEntry> Entries;
        private static Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static bool Tried;

        public static bool Available
        {
            get { Init(); return Archive != null; }
        }

        private static string Normalize(string path)
        {
            return path.Replace('/', '\\').ToLowerInvariant();
        }

        private static void Init()
        {
            if (Tried) return;
            Tried = true;
            try
            {
                // SimsRedux: works before the game content is loaded too (cursors are set up very early)
                string basePath = null;
                try { basePath = Content.Get()?.TS1BasePath; } catch { }
                if (string.IsNullOrEmpty(basePath)) basePath = GlobalSettings.Default.TS1HybridPath;
                var farPath = Path.Combine(basePath, "UIGraphics", "UIGraphics.far");
                if (!File.Exists(farPath))
                {
                    Console.WriteLine("[SimsRedux] UIGraphics.far not found at " + farPath);
                    return;
                }
                // The Sims 1 uses FAR 1a (4-byte filename lengths), so v1b = false.
                Archive = new FAR1Archive(farPath, false);
                Entries = new Dictionary<string, FarEntry>();
                foreach (var entry in Archive.GetAllFarEntries())
                {
                    if (entry.Filename == null) continue;
                    Entries[Normalize(entry.Filename)] = entry;
                }
                Console.WriteLine("[SimsRedux] Loaded UIGraphics.far with " + Entries.Count + " files.");
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Failed to open UIGraphics.far: " + e.Message);
                Archive = null;
            }
        }

        /// <summary>
        /// SimsRedux: copies the original Sims 1 font files (Fonts/*.ffn) out of UIGraphics.far into
        /// Documents\Simitone\redux_fonts, once, so the font format can be worked out.
        /// </summary>
        public static void DumpFontsOnce()
        {
            try
            {
                var dir = Path.Combine(FSO.Common.FSOEnvironment.UserDir, "redux_fonts");
                if (Directory.Exists(dir)) return;
                Init();
                if (Archive == null) return;
                Directory.CreateDirectory(dir);
                foreach (var pair in Entries)
                {
                    var name = pair.Key;
                    if (!name.StartsWith("fonts\\") || name.Substring(6).Contains("\\")) continue; // only the main fonts folder
                    var data = Archive.GetEntry(pair.Value);
                    File.WriteAllBytes(Path.Combine(dir, name.Substring(6)), data);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Font copy: " + e.Message);
            }
        }

        /// <summary>SimsRedux: the raw bytes of a file in UIGraphics.far (null if missing).</summary>
        public static byte[] GetBytes(string path)
        {
            Init();
            FarEntry entry;
            if (Archive == null || !Entries.TryGetValue(Normalize(path), out entry)) return null;
            try { return Archive.GetEntry(entry); }
            catch { return null; }
        }

        /// <summary>
        /// Gets a texture from UIGraphics.far. Pink (magenta) pixels become transparent.
        /// Returns a 1x1 texture if the file is missing, so the UI never crashes.
        /// </summary>
        public static Texture2D Get(string path)
        {
            Init();
            var key = Normalize(path);
            Texture2D result;
            if (Cache.TryGetValue(key, out result)) return result;

            var gd = GameFacade.GraphicsDevice;
            FarEntry entry;
            if (Archive != null && Entries.TryGetValue(key, out entry))
            {
                try
                {
                    var data = Archive.GetEntry(entry);
                    using (var stream = new MemoryStream(data))
                    {
                        result = ImageLoader.FromStream(gd, stream);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("[SimsRedux] Failed to decode " + path + ": " + e.Message);
                }
            }
            else
            {
                Console.WriteLine("[SimsRedux] Missing UI graphic: " + path);
            }

            if (result == null) result = new Texture2D(gd, 1, 1);
            Cache[key] = result;
            return result;
        }

        /// <summary>
        /// Like Get, but also removes the solid background baked into some Sims 1 buttons
        /// (e.g. Done/Cancel), so they sit cleanly on any panel.
        /// Works by flood-filling from each frame's edges through smoothly-changing colours;
        /// the bright rim of the button stops the fill.
        /// </summary>
        public static Texture2D GetCutout(string path, int frames, int tolerance = 10)
        {
            var key = Normalize(path) + "#cutout";
            Texture2D cached;
            if (Cache.TryGetValue(key, out cached)) return cached;

            var source = Get(path);
            int w = source.Width, h = source.Height;
            if (w < 4 || h < 4 || frames < 1)
            {
                Cache[key] = source;
                return source;
            }

            var data = new Color[w * h];
            source.GetData(data);
            var visited = new bool[w * h];
            int frameW = w / frames;
            var queue = new Queue<int>();

            for (int f = 0; f < frames; f++)
            {
                int x0 = f * frameW, x1 = x0 + frameW - 1;
                queue.Clear();
                // seed from the frame's border
                for (int x = x0; x <= x1; x++) { Seed(queue, visited, data, x + 0 * w); Seed(queue, visited, data, x + (h - 1) * w); }
                for (int y = 0; y < h; y++) { Seed(queue, visited, data, x0 + y * w); Seed(queue, visited, data, x1 + y * w); }

                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    int x = i % w, y = i / w;
                    var c = data[i];
                    TryGrow(queue, visited, data, c, x - 1, y, x0, x1, w, h, tolerance);
                    TryGrow(queue, visited, data, c, x + 1, y, x0, x1, w, h, tolerance);
                    TryGrow(queue, visited, data, c, x, y - 1, x0, x1, w, h, tolerance);
                    TryGrow(queue, visited, data, c, x, y + 1, x0, x1, w, h, tolerance);
                }
            }

            for (int i = 0; i < data.Length; i++)
                if (visited[i]) data[i] = Color.Transparent;

            var result = new Texture2D(GameFacade.GraphicsDevice, w, h);
            result.SetData(data);
            Cache[key] = result;
            return result;
        }

        /// <summary>
        /// Guesses how many button frames are laid side by side in a Sims 1 button sheet,
        /// by comparing the transparent outline of neighbouring frames.
        /// </summary>
        public static int GuessFrames(Texture2D tex)
        {
            int w = tex.Width, h = tex.Height;
            if (w < 8 || h < 2) return 1;
            var data = new Color[w * h];
            tex.GetData(data);
            // fully opaque sheets give no outline to compare: assume the usual 4 frames
            bool anyClear = false;
            foreach (var c in data) if (c.A == 0) { anyClear = true; break; }
            if (!anyClear) return 4;
            int[] candidates = { 8, 6, 5, 4, 3, 2 };
            foreach (var n in candidates)
            {
                int fw = w / n;
                if (fw < 4) continue;
                int same = 0, total = 0;
                for (int f = 1; f < n; f++)
                {
                    for (int y = 0; y < h; y += 2)
                    {
                        for (int x = 0; x < fw; x += 2)
                        {
                            bool a = data[x + y * w].A > 0;
                            bool b = data[x + f * fw + y * w].A > 0;
                            if (a == b) same++;
                            total++;
                        }
                    }
                }
                if (total > 0 && same / (float)total > 0.97f) return n;
            }
            return 4;
        }

        private static void Seed(Queue<int> queue, bool[] visited, Color[] data, int i)
        {
            if (visited[i]) return;
            visited[i] = true;
            queue.Enqueue(i);
        }

        private static void TryGrow(Queue<int> queue, bool[] visited, Color[] data, Color from,
            int x, int y, int x0, int x1, int w, int h, int tolerance)
        {
            if (x < x0 || x > x1 || y < 0 || y >= h) return;
            int i = x + y * w;
            if (visited[i]) return;
            var c = data[i];
            if (c.A == 0) { visited[i] = true; queue.Enqueue(i); return; }
            int diff = Math.Abs(c.R - from.R) + Math.Abs(c.G - from.G) + Math.Abs(c.B - from.B);
            if (diff > tolerance) return;
            visited[i] = true;
            queue.Enqueue(i);
        }
    }
}
