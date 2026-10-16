using System;
using System.IO;
using System.Text.Json;

namespace SimsRedux.Creator.App
{
    /// <summary>SimsRedux: what the Creator remembers between runs (%AppData%\SimsReduxCreator\settings.json).</summary>
    public sealed class Settings
    {
        /// <summary>The Sims 3 install folder (searched for its .package files).</summary>
        public string Sims3Folder { get; set; } = @"C:\Program Files (x86)\Electronic Arts";

        /// <summary>The game's Content\redux folder (holds creator\ for Test and mods\ for Live).</summary>
        public string ReduxFolder { get; set; } = @"E:\Midnight Brew Games\TheSimsRedux\Client\Simitone\Simitone.Client\Content\redux";

        // SimsRedux: the window and browser as they were left
        public int ViewIndex { get; set; }
        public string Search { get; set; } = "";
        public int WindowX { get; set; } = -1;
        public int WindowY { get; set; } = -1;
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }
        public double SplitFraction { get; set; } = 0.58;

        /// <summary>The item that was being made last time (reopened when the Creator starts).</summary>
        public LastItemInfo LastItem { get; set; } = new LastItemInfo();

        public sealed class LastItemInfo
        {
            public string Kind { get; set; } = "floor";
            public string Name { get; set; } = "";
            public int Price { get; set; } = -1;
            public string Description { get; set; } = "";
            public string Package { get; set; } = "";
            public string Instance { get; set; } = "";
            public string File { get; set; } = "";
            public string Tint { get; set; } = "";
        }

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimsReduxCreator", "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                    if (s.LastItem == null) s.LastItem = new LastItemInfo();
                    return s;
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public string CreatorFolder => Path.Combine(ReduxFolder, "creator");
        public string ModsFolder => Path.Combine(ReduxFolder, "mods");
    }
}
