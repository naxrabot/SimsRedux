using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimsRedux.Server
{
    /// <summary>
    /// The server's settings, kept in server.json in its data folder. The panel passes the most
    /// important ones on the command line (port, channel, game folder), which win over the file.
    /// </summary>
    public class ServerConfig
    {
        public string Name { get; set; } = "The Sims Redux";
        /// <summary>"live" or "test": which of the two servers this is.</summary>
        public string Channel { get; set; } = "live";
        public int Port { get; set; } = 37564;
        /// <summary>127.0.0.1 = only this PC (the Cloudflare tunnel); 0.0.0.0 = the home network too.</summary>
        public string Bind { get; set; } = "127.0.0.1";
        /// <summary>The Sims 1 folder (the one with GameData in it) whose files Admins and Testers can download.</summary>
        public string GameFolder { get; set; } = "";
        /// <summary>Whether anyone can make an account from the website.</summary>
        public bool Registration { get; set; } = true;
        public string Motd { get; set; } = "Welcome to The Sims Redux!";
        /// <summary>The hour (0-23, this PC's time) for the daily automatic backup; -1 = no automatic backups.</summary>
        public int BackupHour { get; set; } = 4;
        /// <summary>Maintenance: only Admin accounts can log in and be online.</summary>
        public bool Maintenance { get; set; }
        /// <summary>How many automatic backups to keep (the oldest go).</summary>
        public int KeepBackups { get; set; } = 14;
        /// <summary>The public addresses of the Live and Test servers (the website's launcher offers both).</summary>
        public string LiveUrl { get; set; } = "https://tsr-lot.mbrew.studio";
        public string TestUrl { get; set; } = "https://tsr-test.mbrew.studio";
        /// <summary>Websites allowed to use this server's account and file services.</summary>
        public List<string> AllowedOrigins { get; set; } = new List<string>
        {
            "https://tsr.mbrew.studio",
            "https://naxrabot.github.io",
        };
        /// <summary>Until lots run on this server, online lots hosted from the game are passed through to this port.</summary>
        public int GameHostPort { get; set; } = 37565;
        /// <summary>The game program that runs the town's lots (Simitone.Windows.exe). Empty = where Visual Studio builds it.</summary>
        public string GameExe { get; set; } = "";

        [JsonIgnore] public string Path;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static ServerConfig Load(string path)
        {
            ServerConfig c = null;
            try
            {
                if (File.Exists(path)) c = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), Json);
            }
            catch (Exception e)
            {
                Log.Warn("server.json could not be read (" + e.Message + ") - using the defaults. The broken file is kept as server.json.bad");
                try { File.Copy(path, path + ".bad", true); } catch { }
            }
            c ??= new ServerConfig();
            c.Path = path;
            c.AllowedOrigins ??= new List<string>();
            return c;
        }

        public void Save()
        {
            try { DataFile.WriteAllTextSafely(Path, JsonSerializer.Serialize(this, Json)); }
            catch (Exception e) { Log.Warn("could not save server.json: " + e.Message); }
        }
    }

    public static class DataFile
    {
        /// <summary>Writes to a temporary file first, then swaps it in, so a crash mid-write can't leave half a file.</summary>
        public static void WriteAllTextSafely(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
