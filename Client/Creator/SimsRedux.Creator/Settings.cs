using System;
using System.IO;
using System.Text.Json;

namespace SimsRedux.Creator.App
{
    /// <summary>SimsRedux: the Creator's remembered folders (%AppData%\SimsReduxCreator\settings.json).</summary>
    public sealed class Settings
    {
        /// <summary>The Sims 3 install folder (searched for its .package files).</summary>
        public string Sims3Folder { get; set; } = @"C:\Program Files (x86)\Electronic Arts";

        /// <summary>The game's Content\redux folder (holds creator\ for Test and mods\ for Live).</summary>
        public string ReduxFolder { get; set; } = @"E:\Midnight Brew Games\TheSimsRedux\Client\Simitone\Simitone.Client\Content\redux";

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimsReduxCreator", "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
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
