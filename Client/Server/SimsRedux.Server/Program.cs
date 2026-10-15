namespace SimsRedux.Server
{
    /// <summary>
    /// The Sims Redux dedicated server.
    ///
    ///   SimsRedux.Server.exe [--data <folder>] [--port <n>] [--channel live|test] [--game-folder <path>]
    ///
    /// Normally started by the server panel, which passes these. Everything it keeps (settings,
    /// accounts, logins, the log, backups) lives in the data folder. Type /help in the console.
    /// Exit code 75 asks whoever started it (the panel) to start it again.
    /// </summary>
    public static class Program
    {
        public static string DataDir;
        private static Accounts Accounts;
        private static ServerConfig Config;
        private static readonly ManualResetEventSlim Done = new ManualResetEventSlim();
        private static int ExitCode;

        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            string data = null, channel = null, gameFolder = null;
            int port = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string next() => i + 1 < args.Length ? args[++i] : null;
                switch (args[i].ToLowerInvariant())
                {
                    case "--data": data = next(); break;
                    case "--port": int.TryParse(next(), out port); break;
                    case "--channel": channel = next()?.ToLowerInvariant(); break;
                    case "--game-folder": gameFolder = next(); break;
                    case "--help": case "-h": case "/?":
                        Console.WriteLine("SimsRedux.Server [--data <folder>] [--port <n>] [--channel live|test] [--game-folder <path>]");
                        return 0;
                }
            }

            DataDir = Path.GetFullPath(data ?? Path.Combine(AppContext.BaseDirectory, "ServerData", channel ?? "live"));
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(Path.Combine(DataDir, "backups"));
            Log.Open(Path.Combine(DataDir, "server.log"));

            Config = ServerConfig.Load(Path.Combine(DataDir, "server.json"));
            if (channel == "live" || channel == "test") Config.Channel = channel;
            if (port > 0) Config.Port = port;
            if (gameFolder != null) Config.GameFolder = gameFolder.Trim().Trim('"');
            Config.Save();

            Log.Reply("");
            Log.Reply("  THE SIMS REDUX  ·  dedicated server v" + Api.Version + "  ·  " + Config.Channel.ToUpperInvariant());
            Log.Reply("  ────────────────────────────────────────────────");
            Log.Reply("  name      : " + Config.Name);
            Log.Reply("  data      : " + DataDir);
            Log.Reply("  listening : " + Config.Bind + ":" + Config.Port + (Config.Bind == "127.0.0.1" ? "  (this PC and the Cloudflare tunnel)" : "  (this PC and the home network)"));
            Log.Reply("");

            try { Accounts = new Accounts(DataDir); }
            catch (Exception e)
            {
                Log.Warn(e.Message);
                Log.Close();
                return 2;
            }
            var files = new GameFiles();
            files.Scan(Config.GameFolder);
            var lots = new Lots(Config);
            var api = new Api(Config, Accounts, files) { Lots = lots };
            var commands = new Commands(Config, Accounts, files, api) { Lots = lots, Quit = code => { ExitCode = code; Done.Set(); } };

            var http = new HttpServer(api.Handle);
            if (!http.Start(Config.Bind, Config.Port, out var error))
            {
                Log.Warn("couldn't start: " + error);
                Log.Close();
                return 3;
            }

            Log.Info("server started · " + Accounts.Count + " account(s) · new accounts " + (Config.Registration ? "open" : "closed"));
            if (Accounts.All().All(a => a.Role != Roles.Admin))
                Log.Info("no Admin account yet: make yours with  /account add <name> <password> admin");
            Log.Info("Console ready - type /help for commands.");

            // housekeeping: save logins now and then, forget old rate-limit counts
            var timer = new Timer(_ => { try { Accounts.SaveSessions(); api.Tidy(); } catch { } }, null, 30000, 30000);
            // 2D: the online town's one clock (runs while anyone is playing in the town)
            var clockTimer = new Timer(_ => { try { TownClock.Tick(lots); } catch { } }, null, 1000, 1000);

            // console commands come in on stdin (the panel writes them there)
            var input = new Thread(() =>
            {
                try
                {
                    string line;
                    while ((line = Console.ReadLine()) != null)
                    {
                        try { commands.Run(line); }
                        catch (Exception e) { Log.Warn("command failed: " + e.Message); }
                    }
                }
                catch { }
            }) { IsBackground = true, Name = "console" };
            input.Start();

            Console.CancelKeyPress += (s, e) => { e.Cancel = true; ExitCode = 0; Done.Set(); };
            if (!Lots.TownReady) Log.Info("no online town yet: start one with  /town fresh");
            else Log.Info("town: " + lots.Houses().Count + " houses \u00b7 game program: " + (lots.GameExe() ?? "NOT FOUND (/lots game <path>)"));
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { Accounts?.SaveAll(); } catch { } };

            Done.Wait();
            timer.Dispose();
            lots.CloseAll();
            TownClock.Flush();
            http.Shutdown();
            Accounts.SaveAll();
            Config.Save();
            Log.Info(ExitCode == 75 ? "restarting" : "stopped");
            Log.Close();
            return ExitCode;
        }

        /// <summary>A dated copy of the server's data files in backups/.</summary>
        public static string Backup()
        {
            try
            {
                var dir = Path.Combine(DataDir, "backups", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                Directory.CreateDirectory(dir);
                int n = 0;
                foreach (var f in new[] { "server.json", "accounts.json", "sessions.json" })
                {
                    var src = Path.Combine(DataDir, f);
                    if (File.Exists(src)) { File.Copy(src, Path.Combine(dir, f), true); n++; }
                }
                Log.Info("backup: " + dir);
                return "backed up " + n + " file(s) to " + dir;
            }
            catch (Exception e) { return "backup failed: " + e.Message; }
        }
    }
}
