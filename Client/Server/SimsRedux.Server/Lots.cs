using System.Diagnostics;
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
            public volatile bool Ready, Finished, Closing;
            public int Players;
            public string Who = "";
            public string Problem;
            /// <summary>Set when a family moved in on this lot (its family number in the town).</summary>
            public int MovedInFamily = -1;
            /// <summary>Set when a player's family was taken out of the town on this lot.</summary>
            public volatile bool MovedOut;
            /// <summary>The family's money after the admin's /money (or -1 until then).</summary>
            public volatile int MoneyNow = -1;
            /// <summary>What the game said after filling the town with extra Sims (-populate).</summary>
            public volatile string PopulateResult;
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

        public const string PopulationMarker = "redux_population.txt";
        private volatile bool Populating;

        /// <summary>The town already has its extra background Sims (made by this version or later).</summary>
        public bool TownPopulated
        {
            get
            {
                try
                {
                    var p = Path.Combine(TownDir, PopulationMarker);
                    if (!File.Exists(p)) return false;
                    var t = File.ReadAllText(p);
                    return t.Contains("v2") && t.Contains("emptyhomes"); // (5.6: every home is emptied for the players too)
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Fills the town with extra background Sims (all townies), and moves every family that isn't a
        /// player's out of its house, so every home is for sale for the players. Runs the game once, on its own, while no lot is open
        /// (so nothing else is writing the town). It only ever happens once per town.
        /// </summary>
        public async Task<string> PopulateTown()
        {
            if (!TownReady) return "there's no town yet - start one with  /town fresh";
            if (TownPopulated) return "the town already has its extra Sims";
            short house;
            lock (Lock)
            {
                if (Populating) return "already doing that";
                if (Open.Values.Any(w => !w.Finished)) return "close every lot first (/lots close all), then try again";
                house = Houses().FirstOrDefault();
                if (house == 0) return "the town has no houses";
                Populating = true;
            }
            try
            {
                var w = Get(house, out var error, new[] { "-populate" });
                if (w == null) return "couldn't start the game for it: " + error;
                // (making 20 households and saving the town can take a while on a slow PC - it's only
                // stopped if it really hangs, never part way through a normal run)
                await WaitExit(w, TimeSpan.FromMinutes(15));
                return w.PopulateResult ?? (w.Problem ?? "the game didn't finish").Replace("error ", "");
            }
            finally { Populating = false; }
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
        public Worker Get(short house, out string error) => Get(house, out error, null);

        /// <summary>Opens a lot (or gives the one that's open). extraArgs: more for the game, e.g. moving a family in.</summary>
        public Worker Get(short house, out string error, string[] extraArgs)
        {
            error = null;
            lock (Lock)
            {
                // (the town is being given its new neighbours: no lots until that's done - not even
                // the one it's using, which never takes players)
                if (Populating && !(extraArgs != null && extraArgs.Contains("-populate"))) { error = "the town is getting its new neighbours - try again in a minute"; return null; }
                if (Open.TryGetValue(house, out var w) && !w.Finished)
                {
                    // a move in/out or /money needs a lot started for it: the open one never got those
                    // instructions, and waiting on it would end with it killed under its players
                    if (extraArgs != null) { error = "someone is on that lot right now - try again in a few minutes"; return null; }
                    return w;
                }
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
                    WindowStyle = ProcessWindowStyle.Hidden, // the lot's game window is never shown on the server PC
                };
                foreach (var a in new[] { "-path" + Config.GameFolder.TrimEnd('\\', '/'), "-worker", "-house" + house, "-port" + port, "-town" + TownDir, "-time" + TownClock.Now })
                    psi.ArgumentList.Add(a);
                if (extraArgs != null) foreach (var a in extraArgs) psi.ArgumentList.Add(a);
                // SimsRedux: the Test server's lots also load the Sims Redux Creator's new items (being tried out there)
                if (string.Equals(Config.Channel, "test", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add("-creator");
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
            if (text.StartsWith("money ") && int.TryParse(text.Substring(6).Trim(), out var cash)) { w.MoneyNow = cash; Log.Info("lot " + w.House + ": the family now has \u00a7" + cash); return; }
            if (text.StartsWith("populated ")) { w.PopulateResult = text.Substring(10); Log.Info("town: " + w.PopulateResult); return; }
            if (text == "movedout") { w.MovedOut = true; Log.Info("lot " + w.House + ": the family moved out"); return; }
            if (text.StartsWith("movedin ") && int.TryParse(text.Substring(8).Trim(), out var fam)) { w.MovedInFamily = fam; Log.Info("lot " + w.House + ": a family moved in (family " + fam + ")"); return; }
            if (text.StartsWith("closing")) w.Closing = true; // new arrivals wait for it to finish, then it reopens
            if (text == "saved") return; // every few minutes - not worth a line each time
            if (text.StartsWith("stats "))
            {
                // "stats <family> <money or -1> <sims>": XP and the cheat checks, for families players own
                var f = text.Split(' ', 4);
                if (f.Length == 4 && int.TryParse(f[1], out var sFam) && int.TryParse(f[2], out var sBudget))
                {
                    var owner = Owners.All().FirstOrDefault(o => o.Value.Family == sFam);
                    if (owner.Value != null)
                    {
                        var sims = SimStats.Parse(f[3]);
                        try { Progress.Stats(owner.Key, sims); } catch (Exception e) { Log.Warn("xp: " + e.Message); }
                        try { AntiCheat.Stats(owner.Key, owner.Value.Account, sFam, sBudget, sims); } catch (Exception e) { Log.Warn("cheat check: " + e.Message); }
                    }
                }
                return;
            }
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

        /// <summary>Waits for a worker's game to finish closing.</summary>
        public async Task WaitExit(Worker w, TimeSpan max)
        {
            var until = DateTime.UtcNow + max;
            while (!w.Finished && DateTime.UtcNow < until) await Task.Delay(200);
            if (!w.Finished) { try { w.Process.Kill(); } catch { } await Task.Delay(500); }
            // "exited" can come before the game's last lines (movedout, money...) have been read: let them arrive
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await w.Process.WaitForExitAsync(cts.Token);
            }
            catch { }
            lock (Lock) { if (Open.TryGetValue(w.House, out var cur) && cur == w) Open.Remove(w.House); }
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
            if (string.IsNullOrWhiteSpace(from))
                from = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Simitone", "UserData");
            if (!File.Exists(Path.Combine(from, "Neighborhood.iff"))) return "no Neighborhood.iff in " + from + " - point it at a UserData folder";

            // open lots would save the old town back over the new one: close them first
            if (Running().Count > 0)
            {
                Log.Info("closing the open lots before replacing the town...");
                CloseAll();
                if (Running().Count > 0) return "a lot didn't close - try again in a moment (or /lots close all)";
            }
            try
            {
                Log.Reply("setting up the town from " + from + " - this can take a moment...");
                if (Directory.Exists(TownDir))
                {
                    // keep the old town (copied, so a file that's still open somewhere can't stop it)
                    var old = Path.Combine(Program.DataDir, "backups", "town-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                    CopyTown(TownDir, old);
                    Log.Info("the old town was kept in " + old);
                    foreach (var f in Directory.EnumerateFiles(TownDir, "*", SearchOption.AllDirectories))
                    {
                        try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
                        catch (Exception e) { return "couldn't clear the old town (" + Path.GetFileName(f) + ": " + e.Message + ") - is something still using it?"; }
                    }
                }
                CopyTown(from, TownDir);
                TownClock.Reset(); // a new town starts a new clock (day 1, 8:00 AM)
                // a fresh copy gets fresh file times, so every player's game sees the town has changed
                var now = DateTime.Now;
                foreach (var f in Directory.EnumerateFiles(TownDir, "*", SearchOption.AllDirectories))
                    try { File.SetLastWriteTime(f, now); } catch { }
                int sims = 0;
                try { sims = Directory.GetFiles(Path.Combine(TownDir, "Characters"), "*.iff").Length; } catch { }
                var nbhd = new FileInfo(Path.Combine(TownDir, "Neighborhood.iff")).Length;
                Log.Info("town copied from " + from + " (" + Houses().Count + " houses, " + sims + " Sims, Neighborhood.iff " + (nbhd / 1024) + " KB)");
                return "town set up from " + from + ": " + Houses().Count + " houses, " + sims + " Sims";
            }
            catch (Exception e) { return "couldn't copy the town: " + e.Message; }
        }

        /// <summary>
        /// A brand-new town from the Sims folder, nobody's own Sims in it:
        ///   "/town fresh"          the game's Neighbourhood 1 as it came (UserData): the Goths,
        ///                          Newbies, Pleasants and the other starting families in their houses
        ///   "/town fresh empty"    the game's blank template (TemplateUserData): houses, no families
        /// The old town is kept in backups/.
        /// </summary>
        public string FreshTown(bool empty)
        {
            var game = (Config.GameFolder ?? "").Trim().Trim('"');
            if (game.Length == 0 || !Directory.Exists(game)) return "the server doesn't know where the Sims folder is - set it with /files folder <path>";
            var source = Path.Combine(game, empty ? "TemplateUserData" : "UserData");
            if (!File.Exists(Path.Combine(source, "Neighborhood.iff")) || !File.Exists(Path.Combine(source, "LotLocations.iff")))
                return "there's no starting neighbourhood in " + source + " - use /town import <folder> with a UserData folder instead";
            var result = ImportTown(source);
            if (!TownReady) return result;
            // the blank template can be missing the expansion packs' lot names and descriptions
            if (empty)
            {
                foreach (var name in new[] { "NeighborhoodDesc.iff", "STDesc.iff", "MTDesc.iff" })
                {
                    var to = Path.Combine(TownDir, "Houses", name);
                    var from = Path.Combine(game, "UserData", "Houses", name);
                    try { if (!File.Exists(to) && File.Exists(from)) { Directory.CreateDirectory(Path.GetDirectoryName(to)); File.Copy(from, to); } }
                    catch { }
                }
            }
            int sims = 0;
            try { sims = Directory.GetFiles(Path.Combine(TownDir, "Characters"), "*.iff").Length; } catch { }
            var fam = IffPeek.Families(Path.Combine(TownDir, "Neighborhood.iff"));
            Log.Info("a fresh town was set up from " + source + " - " + IffPeek.Describe(TownDir));
            var reply = "fresh town set up from " + source + ": " + Houses().Count + " houses, " + sims + " Sims, " + IffPeek.Describe(TownDir);
            if (!empty && (fam == null || fam.InHouses == 0))
                reply += "\nThat neighbourhood has no families living in houses. Type  /town sources  to see every neighbourhood found, then  /town import <folder>  to use one that has them.";
            return reply;
        }

        /// <summary>
        /// Copies only what a neighbourhood is made of: the files at the top (Neighborhood.iff,
        /// LotLocations.iff...), Houses and Characters. A game's UserData folder can also hold big
        /// folders of downloads and exports, which the town doesn't need.
        /// </summary>
        private static void CopyTown(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.EnumerateFiles(from))
            {
                // (a single-player neighbourhood's note about its own extra Sims isn't the town's - but a
                // server town's own note comes along, so a town brought back from a backup isn't
                // given a second batch of neighbours)
                if (string.Equals(Path.GetFileName(f), PopulationMarker, StringComparison.OrdinalIgnoreCase))
                {
                    string note = "";
                    try { note = File.ReadAllText(f); } catch { }
                    if (!note.Contains("emptyhomes")) continue;
                }
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            }
            foreach (var sub in new[] { "Houses", "Characters" })
            {
                var src = Path.Combine(from, sub);
                if (Directory.Exists(src)) Copy(src, Path.Combine(to, sub));
            }
        }

        /// <summary>Every neighbourhood folder the server can find, with its families (to pick one for the town).</summary>
        public string Sources()
        {
            var found = new List<string>();
            void Look(string dir)
            {
                try
                {
                    if (!File.Exists(Path.Combine(dir, "Neighborhood.iff"))) return;
                    found.Add(dir + "\n      " + IffPeek.Describe(dir));
                }
                catch { }
            }
            var game = (Config.GameFolder ?? "").Trim().Trim('"');
            if (Directory.Exists(game))
            {
                foreach (var d in Directory.GetDirectories(game).OrderBy(x => x)) Look(d);
            }
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            foreach (var d in new[] { Path.Combine(docs, "Simitone"), Path.Combine(docs, "EA Games"), Path.Combine(docs, "Electronic Arts") })
            {
                if (!Directory.Exists(d)) continue;
                try { foreach (var u in Directory.GetDirectories(d, "*", SearchOption.AllDirectories).Take(400)) Look(u); } catch { }
            }
            var backups = Path.Combine(Program.DataDir, "backups");
            if (Directory.Exists(backups))
                foreach (var b in Directory.GetDirectories(backups, "town-*").OrderBy(x => x)) Look(Path.Combine(b, "UserData"));
            Look(TownDir);
            if (found.Count == 0) return "no neighbourhoods found";
            return "neighbourhoods found (use one with  /town import <folder>):\n  " + string.Join("\n  ", found);
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var d in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
            foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)), true);
        }
    }
}
