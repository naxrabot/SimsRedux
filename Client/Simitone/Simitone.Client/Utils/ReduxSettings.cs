using FSO.Client;
using FSO.Common;
using System;
using System.Collections.Generic;
using System.IO;

using FSO.Common.IO;
namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: the player's own choices from the classic Options pages (Graphics / Play),
    /// kept in Documents\Simitone\redux_settings.ini.
    ///
    /// Simitone's start-up code overwrites some of its own saved settings every launch
    /// (lighting, anti-aliasing), so ours are kept separately and put back on top at start-up.
    /// Volumes and window mode use the normal game settings file.
    /// </summary>
    public static class ReduxSettings
    {
        private static Dictionary<string, string> Values;
        /// <summary>The online town's login and downloads change settings from their own threads.</summary>
        private static readonly object Lock = new object();

        private static string FilePath
        {
            get { return Path.Combine(FSOEnvironment.UserDir ?? "", "redux_settings.ini"); }
        }

        private static void Load()
        {
            if (Values != null) return;
            Values = new Dictionary<string, string>();
            try
            {
                if (!FSOFile.Exists(FilePath)) return;
                foreach (var line in FSOFile.ReadAllLines(FilePath))
                {
                    var i = line.IndexOf('=');
                    if (i <= 0) continue;
                    Values[line.Substring(0, i).Trim().ToLowerInvariant()] = line.Substring(i + 1).Trim();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Couldn't read redux_settings.ini: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string>();
                foreach (var pair in Values) lines.Add(pair.Key + "=" + pair.Value);
                FSOFile.WriteAllLines(FilePath, lines);
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Couldn't save redux_settings.ini: " + e.Message);
            }
        }

        /// <summary>
        /// How smart Sims are on their own in single player: -1 = Off (your family does nothing on
        /// their own), 0 = the original Sims 1 brain, 1 = Smarter, 2 = Smartest (Sims 3/4-style, the
        /// default). Online lots are always Smartest.
        /// </summary>
        public static int Autonomy
        {
            get { var v = Math.Max(-1, Math.Min(2, GetInt("autonomy", 2))); FSO.SimAntics.Primitives.VMFindBestAction.ReduxLevel = v; return v; }
            set { var v = Math.Max(-1, Math.Min(2, value)); Set("autonomy", v); FSO.SimAntics.Primitives.VMFindBestAction.ReduxLevel = v; }
        }

        public static int GetInt(string key, int fallback)
        {
            lock (Lock)
            {
                Load();
                string v; int result;
                if (Values.TryGetValue(key, out v) && int.TryParse(v, out result)) return result;
                return fallback;
            }
        }

        public static bool GetBool(string key, bool fallback)
        {
            lock (Lock)
            {
                Load();
                string v;
                if (Values.TryGetValue(key, out v)) return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                return fallback;
            }
        }

        public static string GetString(string key, string fallback)
        {
            lock (Lock)
            {
                Load();
                string v;
                return Values.TryGetValue(key.ToLowerInvariant(), out v) ? v : fallback;
            }
        }

        public static bool Has(string key)
        {
            lock (Lock)
            {
                Load();
                return Values.ContainsKey(key);
            }
        }

        public static void Set(string key, object value)
        {
            lock (Lock)
            {
                Load();
                // one line per setting: a line break in a value (a name from the server...) would start a made-up setting
                Values[key] = (value is bool) ? (((bool)value) ? "1" : "0") : (value?.ToString() ?? "").Replace("\r", "").Replace("\n", " ");
                Save();
            }
        }

        // ---- named settings ----

        /// <summary>true = Simitone's original camera, false = Sims 3 style (default).</summary>
        public static bool ClassicCamera
        {
            get { return GetBool("classiccamera", false); }
            set { Set("classiccamera", value); }
        }

        public static bool EdgeScroll
        {
            get { return GetBool("edgescroll", false); }
            set { Set("edgescroll", value); }
        }

        /// <summary>Put the player's saved Graphics choices over Simitone's start-up defaults.</summary>
        public static void ApplyToGlobal()
        {
            var g = GlobalSettings.Default;
            try { var smarts = Autonomy; } catch { } // the Sim Smarts setting reaches the game's brain
            if (Has("lighting")) g.LightingMode = Math.Max(0, Math.Min(3, GetInt("lighting", 3)));
            if (Has("antialias")) g.AntiAlias = GetInt("antialias", g.AntiAlias);
            if (Has("smoothzoom")) g.SmoothZoom = GetBool("smoothzoom", g.SmoothZoom);
        }
    }
}
