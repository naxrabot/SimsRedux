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

        public static int GetInt(string key, int fallback)
        {
            Load();
            string v; int result;
            if (Values.TryGetValue(key, out v) && int.TryParse(v, out result)) return result;
            return fallback;
        }

        public static bool GetBool(string key, bool fallback)
        {
            Load();
            string v;
            if (Values.TryGetValue(key, out v)) return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            return fallback;
        }

        public static string GetString(string key, string fallback)
        {
            Load();
            string v;
            return Values.TryGetValue(key.ToLowerInvariant(), out v) ? v : fallback;
        }

        public static bool Has(string key)
        {
            Load();
            return Values.ContainsKey(key);
        }

        public static void Set(string key, object value)
        {
            Load();
            Values[key] = (value is bool) ? (((bool)value) ? "1" : "0") : value.ToString();
            Save();
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
            if (Has("lighting")) g.LightingMode = Math.Max(0, Math.Min(3, GetInt("lighting", 3)));
            if (Has("antialias")) g.AntiAlias = GetInt("antialias", g.AntiAlias);
            if (Has("smoothzoom")) g.SmoothZoom = GetBool("smoothzoom", g.SmoothZoom);
        }
    }
}
