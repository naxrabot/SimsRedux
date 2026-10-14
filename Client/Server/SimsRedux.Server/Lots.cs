using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SimsRedux.Server
{
    /// <summary>
    /// The town's lots. Each open lot is run by a "lot worker": the game, started hidden in worker
    /// mode, hosting that one house from the town folder. A lot opens when someone travels to it,
    /// and its worker saves and closes by itself a couple of minutes after everyone has left.
    /// </summary>
    public class Lots
    {
        public class Worker
        {
            public short House;
            public int Port;
            public Process Process;
            public DateTime Started;
            public volatile bool Ready, Finished;
            public int Players;
            public string Who = "";
            public string Problem;
        }

        private readonly ServerConfig Config;
        private readonly Dictionary<short, Worker> Open = new Dictionary<short, Worker>();
        private readonly object Lock = new object();
        public const int FirstPort = 37600, LastPort = 37699;

        public Lots(ServerConfig config) { Config = config; }

        /// <summary>The town's UserData folder (inside the server's data folder).</summary>
        public static string TownDir => Path.Combine(Program.DataDir, "Town", "UserData");
        public static bool TownReady => File.Exists(Path.Combine(TownDir, "Neighborhood.iff"));

        public List<short> Houses()
        {
            var list = new List<short>();
            var dir = Path.Combine(TownDir, "Houses");
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.EnumerateFiles(dir, "House*.iff"))
            {
                var m = Regex.Match(Path.GetFileName(f), @"^House(\d+)\.iff$", RegexOptions.IgnoreCase);
                if (m.Success && short.TryParse(m.Groups[1].Value, out var n)) list.Add(n);
            }
            list.Sort();
            return list;
        }

        public List<Worker> Running() { lock (Lock) return Open.Values.Where(w => !w.Finished).OrderBy(w => w.House).ToList(); }
        public int PlayerCount { get { lock (Lock) return Open.Values.Where(w => !w.Finished).Sum(w => w.Players); } }

        // ---------- the game program that runs lots ----------

        public string GameExe()
        {
            // the game builds as Simitone.exe; a folder holding it is fine too
            string InFolder(string dir)
            {
                foreach (var name in new[] { "Simitone.exe", "Simitone.Windows.exe" })
                    if (File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);
                return null;
            }
            if (!string.IsNullOrEmpty(Config.GameExe))
            {
                if (File.Exists(Config.GameExe)) return Config.GameExe;
                if (Directory.Exists(Config.GameExe) && InFolder(Config.GameExe) is string inSet) return inSet;
            }
            // where Visual Studio builds the game, next to this server (Client\Server\bin -> Client\Simitone\...)
            var simitone = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Simitone", "Simitone.Windows", "bin"));
            foreach (var cfg in new[] { "Release", "Debug" })
                if (InFolder(Path.Combine(simitone, cfg, "net9.0-windows")) is string exe) return exe;
            return null;
        }

        /// <summary>The worker for a house, starting one if it isn't open. Null (with a reason) if it can't.</summary>
        public Worker Get(short house, out string error)
        {
            error = null;
            lock (Lock)
            {
                if (Open.TryGetValue(house, out var w) && !w.Finished) return w;
                if (!TownReady) { error = "the town hasn't been set up yet (/town import)"; return null; }
                if (!Houses().Contains(house)) { error = "there's no house " + house + " in the town"; return null; }
                var exe = GameExe();
                if (exe == null) { error = "the game program wasn't found - build the solution, or set it with /lots game <path to Simitone.exe>"; return null; }
                if (string.IsNullOrEmpty(Config.GameFolder)) { error = "the Sims 1 game folder isn't set (Game files... in the panel)"; return null; }
                int port = FreePort();
                if (port == 0) { error = "too many lots are open"; return null; }

                w = new Worker { House = house, Port = port, Started = DateTime.UtcNow };
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                foreach (var a in new[] { "-path" + Config.GameFolder.TrimEnd('\\', '/'), "-worker", "-house" + house, "-port" + port, "-town" + TownDir })
                    psi.ArgumentList.Add(a);
                try { w.Process = Process.Start(psi); }
                catch (Exception e) { error = "couldn't start the game for this lot: " + e.Message; return null; }
                Open[house] = w;
                Watch(w);
                Log.Info("lot " + house + ": opening (port " + port + ")");
                return w;
            }
        }

        private int FreePort()
        {
            var used = Open.Values.Where(w => !w.Finished).Select(w => w.Port).ToHashSet();
            for (int p = FirstPort; p <= LastPort; p++) if (!used.Contains(p)) return p;
            return 0;
        }

        private void Watch(Worker w)
        {
            var p = w.Process;
            p.OutputDataReceived += (s, e) => { if (e.Data != null) Heard(w, e.Data); };
            p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data) && e.Data.Contains("Exception")) Log.Warn("lot " + w.House + ": " + e.Data); };
            p.EnableRaisingEvents = true;
            p.Exited += (s, e) =>
            {
                w.Finished = true;
                int code = 0;
                try { code = p.ExitCode; } catch { }
                Log.Info("lot " + w.House + ": closed" + (code != 0 ? " (exit code " + code + ")" : ""));
                lock (Lock) { if (Open.TryGetValue(w.House, out var cur) && cur == w) Open.Remove(w.House); }
            };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }

        /// <summary>What a worker says on its console ("[worker] ...").</summary>
        private void Heard(Worker w, string line)
        {
            if (!line.StartsWith("[worker] ")) return;
            var text = line.Substring(9);
            if (text.StartsWith("hosting")) { w.Ready = true; Log.Info("lot " + w.House + ": open (" + (int)(DateTime.UtcNow - w.Started).TotalSeconds + "s to start)"); return; }
            if (text.StartsWith("players "))
            {
                var rest = text.Substring(8);
                var sp = rest.IndexOf(' ');
                int.TryParse(sp < 0 ? rest : rest.Substring(0, sp), out w.Players);
                w.Who = sp < 0 ? "" : rest.Substring(sp + 1);
                Log.Info("lot " + w.House + ": " + (w.Players == 0 ? "empty" : w.Players + " here (" + w.Who + ")"));
                return;
            }
            if (text.StartsWith("error")) { w.Problem = text; Log.Warn("lot " + w.House + ": " + text); return; }
            if (text == "saved") return; // every few minutes - not worth a line each time
            Log.Info("lot " + w.House + ": " + text);
        }

        /// <summary>Waits for a lot to be ready for players (it takes a little while to load).</summary>
        public async Task<bool> WaitReady(Worker w, TimeSpan max)
        {
            var until = DateTime.UtcNow + max;
            while (DateTime.UtcNow < until)
            {
                if (w.Finished) return false;
                if (w.Ready) return true;
                await Task.Delay(250);
            }
            return w.Ready;
        }

        public void Send(Worker w, string command)
        {
            try { w.Process.StandardInput.WriteLine(command); w.Process.StandardInput.Flush(); } catch { }
        }

        public bool Close(short house)
        {
            Worker w;
            lock (Lock) { if (!Open.TryGetValue(house, out w) || w.Finished) return false; }
            Send(w, "quit");
            return true;
        }

        /// <summary>Asks every lot to save and close, waiting a little for them to finish.</summary>
        public void CloseAll()
        {
            var list = Running();
            if (list.Count == 0) return;
            Log.Info("closing " + list.Count + " lot(s)...");
            foreach (var w in list) Send(w, "quit");
            var until = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < until && list.Any(w => !w.Finished)) Thread.Sleep(200);
            foreach (var w in list.Where(w => !w.Finished)) { try { w.Process.Kill(); } catch { } }
        }

        // ---------- the town ----------

        /// <summary>Copies a neighbourhood (a UserData folder) in as the server's town.</summary>
        public string ImportTown(string from)
        {
            if (Running().Count > 0) return "close all lots first (/lots close all)";
            if (string.IsNullOrWhiteSpace(from))
                from = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Simitone", "UserData");
            if (!File.Exists(Path.Combine(from, "Neighborhood.iff"))) return "no Neighborhood.iff in " + from + " - point it at a UserData folder";
            try
            {
                if (Directory.Exists(TownDir))
                {
                    var old = Path.Combine(Program.DataDir, "backups", "town-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                    Directory.CreateDirectory(Path.GetDirectoryName(old));
                    Directory.Move(Path.Combine(Program.DataDir, "Town"), old);
                    Log.Info("the old town was kept in " + old);
                }
                Copy(from, TownDir);
                Log.Info("town imported from " + from + " (" + Houses().Count + " houses)");
                return "town set up from " + from + ": " + Houses().Count + " houses";
            }
            catch (Exception e) { return "couldn't copy the town: " + e.Message; }
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var d in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
            foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)), true);
        }

        /// <summary>Is something listening on this port (the worker, once it's hosting)?</summary>
        public static bool Listening(int port)
        {
            try
            {
                using var c = new TcpClient();
                return c.ConnectAsync("127.0.0.1", port).Wait(300) && c.Connected;
            }
            catch { return false; }
        }
    }
}
