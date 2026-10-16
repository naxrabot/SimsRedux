using FSO.Client;
using FSO.Common;
using FSO.Content;
using FSO.SimAntics.NetPlay.Model.Commands;
using Simitone.Client.UI.Screens;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux dedicated server: the game started by the server as a "lot worker".
    ///
    ///   Simitone.exe -path"&lt;Sims folder&gt;" -worker -house12 -port37600 -town"&lt;town folder&gt;"
    ///
    /// It opens one house from the server's town (not the player's own neighbourhood), hosts it
    /// online on the given port with no window showing, and the server passes players to it.
    /// When everyone has left it saves the lot into the town and closes. It talks to the server
    /// through its console: lines starting "[worker]" out, and "save" / "quit" in.
    /// </summary>
    public static class ReduxWorker
    {
        public static bool Active;
        public static short House;
        public static int Port;
        /// <summary>The town's UserData folder (Neighborhood.iff, Houses, Characters...).</summary>
        public static string Town;
        /// <summary>2C: a family moving in here (their package file) and the account they belong to.</summary>
        public static string MoveInFile, MoveInOwner;
        /// <summary>A player's family leaving the town (their family number), or -1.</summary>
        public static int MoveOutFamily = -1;
        /// <summary>The admin giving (or taking) money while the lot is closed: family number and amount.</summary>
        public static int MoneyFamily = -1, MoneyAmount;
        /// <summary>Fill the town with extra background Sims (ReduxPopulation), then close - run by the server with no lots open.</summary>
        public static bool Populate;
        /// <summary>2D: the town's time when this lot opened (Sim minutes since day 1, midnight), or -1.</summary>
        public static long TownMinutes = -1;

        /// <summary>How long an empty lot stays open (for someone coming back) before it saves and closes.</summary>
        private static readonly TimeSpan EmptyGrace = TimeSpan.FromMinutes(2);
        /// <summary>How long a lot waits for its first player before giving up.</summary>
        private static readonly TimeSpan FirstPlayerWait = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan AutoSave = TimeSpan.FromMinutes(10);

        private static TS1GameScreen Screen;
        private static DateTime Started, EmptySince, LastSave, BlockedSince, LastSpeedChange;
        private static bool EverHadPlayers, Closing;
        private static int LastCount = -1;
        /// <summary>Lines from the server, read on their own thread (queued, so none is ever lost).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Commands = new System.Collections.Concurrent.ConcurrentQueue<string>();

        /// <summary>Reads the worker arguments (from Program.cs). Returns true if this is a worker.</summary>
        public static bool Configure(string[] args)
        {
            foreach (var a in args)
            {
                if (!a.StartsWith("-")) continue;
                var s = a.Substring(1);
                if (s == "worker") Active = true;
                else if (s.StartsWith("house") && short.TryParse(s.Substring(5), out var h)) House = h;
                else if (s.StartsWith("port") && int.TryParse(s.Substring(4), out var p)) Port = p;
                else if (s.StartsWith("town")) Town = s.Substring(4).Trim('"').Replace('\\', '/').TrimEnd('/') + "/";
                else if (s.StartsWith("moveout") && int.TryParse(s.Substring(7), out var mo)) MoveOutFamily = mo;
                else if (s.StartsWith("time") && long.TryParse(s.Substring(4), out var tm)) TownMinutes = tm;
                else if (s.StartsWith("money"))
                {
                    // -money<family>:<amount>
                    var parts = s.Substring(5).Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var mf) && int.TryParse(parts[1], out var ma)) { MoneyFamily = mf; MoneyAmount = ma; }
                }
                else if (s.StartsWith("movein")) MoveInFile = s.Substring(6).Trim('"');
                else if (s == "populate") Populate = true;
                else if (s.StartsWith("owner")) MoveInOwner = s.Substring(5).Trim('"');
            }
            if (!Active) return false;
            if (House <= 0 || Port <= 0 || string.IsNullOrEmpty(Town))
            {
                Say("error bad arguments (need -house<N> -port<N> -town<folder>)");
                Environment.Exit(2);
            }
            // the game's normal settings folder is used as-is (it starts up exactly like the game);
            // the lot itself is only ever loaded from and saved into the town folder
            // sound stays switched on (the game expects it everywhere) but at volume 0 - see SimitoneGame
            Started = DateTime.UtcNow;

            var input = new Thread(() =>
            {
                try
                {
                    string line;
                    while ((line = Console.ReadLine()) != null) Commands.Enqueue(line.Trim()); // (kick keeps its message as typed)
                }
                catch { }
            }) { IsBackground = true, Name = "worker console" };
            input.Start();
            Say("starting house " + House + " on port " + Port);
            return true;
        }

        public static void Say(string text)
        {
            // one line per message: the server reads them line by line
            text = (text ?? "").Replace("\r", "").Replace("\n", " | ");
            try { Console.WriteLine("[worker] " + text); Console.Out.Flush(); } catch { }
        }

        /// <summary>A crash: the whole report to the server, and to a file next to the town.</summary>
        public static void Crashed(object exception)
        {
            Say("error crashed: " + exception);
            try
            {
                var file = Path.Combine(Path.GetDirectoryName(Town.TrimEnd('/')) ?? ".", "worker-crash-house" + House + ".txt");
                File.WriteAllText(file, DateTime.Now + "\r\n" + exception);
            }
            catch { }
        }

        /// <summary>The town's family owners (written by the server): the family number an account owns, or -1.</summary>
        public static int OwnedFamily(string account)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(Town)) return -1;
            try
            {
                var path = Path.Combine(Town, "owners.json");
                if (!File.Exists(path)) return -1;
                using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        var acct = p.Value.TryGetProperty("Account", out var a) ? a.GetString() : p.Name;
                        if (string.Equals(acct, account, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, account, StringComparison.OrdinalIgnoreCase))
                            return p.Value.TryGetProperty("Family", out var f) ? f.GetInt32() : -1;
                    }
                }
            }
            catch (Exception e) { Say("error reading owners: " + e.Message); }
            return -1;
        }

        /// <summary>2D: the player's family living in house... is it a player's family? Their family name, or null (a town family).</summary>
        private static DateTime LastStats;
        private static readonly FSO.SimAntics.Model.VMPersonDataVariable[] StatSkills =
        {
            FSO.SimAntics.Model.VMPersonDataVariable.CookingSkill, FSO.SimAntics.Model.VMPersonDataVariable.MechanicalSkill,
            FSO.SimAntics.Model.VMPersonDataVariable.CharismaSkill, FSO.SimAntics.Model.VMPersonDataVariable.BodySkill,
            FSO.SimAntics.Model.VMPersonDataVariable.LogicSkill, FSO.SimAntics.Model.VMPersonDataVariable.CreativitySkill,
        };

        /// <summary>
        /// "stats &lt;family&gt; &lt;money or -1&gt; &lt;sim&gt;:&lt;6 skills&gt;:&lt;job&gt;:&lt;job level&gt;;..." - one line per family
        /// with Sims on the lot (money only for the family that lives here).
        /// </summary>
        private static void ReportStats(FSO.SimAntics.VM vm)
        {
            var byFamily = new Dictionary<int, List<string>>();
            foreach (var e in vm.Context.ObjectQueries.Avatars)
            {
                var a = e as FSO.SimAntics.VMAvatar;
                if (a == null || a.IsPet || a.Position == FSO.LotView.Model.LotTilePos.OUT_OF_WORLD) continue;
                int fam = a.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.TS1FamilyNumber);
                if (fam <= 0) continue;
                var name = new string((a.Name ?? "?").Where(c => c >= 32 && c != ':' && c != ';' && c != ',' && c != '|').ToArray()).Trim();
                if (name.Length == 0) name = "Sim" + a.ObjectID;
                var line = name + ":" + string.Join(",", StatSkills.Select(v => a.GetPersonData(v)))
                    + ":" + a.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.JobType)
                    + ":" + a.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.JobPromotionLevel);
                if (!byFamily.TryGetValue(fam, out var list)) byFamily[fam] = list = new List<string>();
                list.Add(line);
            }
            var lotFam = vm.TS1State?.CurrentFamily;
            foreach (var kv in byFamily)
                Say("stats " + kv.Key + " " + (lotFam != null && lotFam.ChunkID == kv.Key ? lotFam.Budget : -1) + " " + string.Join(";", kv.Value));
        }

        public static string FamilyOwnerName(int family)
        {
            if (family < 0 || string.IsNullOrEmpty(Town)) return null;
            try
            {
                var path = Path.Combine(Town, "owners.json");
                if (!File.Exists(path)) return null;
                using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        if (p.Value.TryGetProperty("Family", out var f) && f.GetInt32() == family)
                        {
                            var n = p.Value.TryGetProperty("Name", out var nm) ? nm.GetString() : null;
                            return string.IsNullOrWhiteSpace(n) ? "that" : n;
                        }
                    }
                }
            }
            catch (Exception e) { Say("error reading owners: " + e.Message); }
            return null;
        }

        private static HashSet<int> OwnedCache = new HashSet<int>();
        private static DateTime OwnedRead;

        /// <summary>The town families that belong to players (from owners.json, read at most once a minute). Empty off the server.</summary>
        public static HashSet<int> OwnedFamilies()
        {
            if (!Active || string.IsNullOrEmpty(Town)) return new HashSet<int>();
            if ((DateTime.UtcNow - OwnedRead).TotalSeconds < 60) return OwnedCache;
            OwnedRead = DateTime.UtcNow;
            var set = new HashSet<int>();
            try
            {
                var path = Path.Combine(Town, "owners.json");
                if (File.Exists(path))
                {
                    using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
                        foreach (var p in doc.RootElement.EnumerateObject())
                            if (p.Value.TryGetProperty("Family", out var f) && f.TryGetInt32(out var n)) set.Add(n);
                }
            }
            catch (Exception e) { Say("error reading owners: " + e.Message); }
            OwnedCache = set;
            return set;
        }

        /// <summary>The families this lot looks after (whoever lives here, or moved in / out here).</summary>
        private static readonly HashSet<int> MyFamilies = new HashSet<int>();

        /// <summary>
        /// 2D: what this lot may write into the town's shared neighbourhood when it saves - its own
        /// family (and their Sims) and everyone on the lot except visiting players' Sims (their own
        /// lot looks after them). Everything else is kept as the other lots left it.
        /// </summary>
        private static Tuple<HashSet<short>, HashSet<int>> TownOwnership()
        {
            var hood = Content.Get().Neighborhood;
            var fams = new HashSet<int>(MyFamilies);
            var vm = Screen?.vm;
            var cur = vm?.TS1State?.CurrentFamily;
            if (cur != null) fams.Add(cur.ChunkID);
            var nbs = new HashSet<short>();
            foreach (var id in fams)
            {
                var fam = hood.GetFamily((ushort)id);
                if (fam?.FamilyGUIDs == null) continue;
                foreach (var guid in fam.FamilyGUIDs)
                {
                    var nid = hood.GetNeighborIDForGUID(guid);
                    if (nid != null) nbs.Add(nid.Value);
                }
            }
            if (vm != null)
            {
                foreach (var ava in vm.Context.ObjectQueries.Avatars.OfType<FSO.SimAntics.VMAvatar>())
                {
                    if ((ava.GetValue(FSO.SimAntics.Model.VMStackObjectVariable.MovementFlags) & (short)FSO.SimAntics.VMMovementFlags.FSOExcludeUserSave) != 0) continue;
                    var guid = ava.Object?.OBJ?.GUID ?? 0;
                    var nid = guid == 0 ? null : hood.GetNeighborIDForGUID(guid);
                    if (nid != null) nbs.Add(nid.Value);
                }
            }
            return Tuple.Create(nbs, fams);
        }

        /// <summary>Called once the game screen exists: load the town and open the house.</summary>
        public static void Begin(TS1GameScreen screen)
        {
            Screen = screen;
            try
            {
                var hood = Content.Get().Neighborhood;
                hood.LoadFolder(Town);
                if (!File.Exists(hood.GetHousePath(House)))
                {
                    Say("error there's no house " + House + " in the town");
                    Environment.Exit(3);
                }
                HideWindow();
                if (Populate)
                {
                    // the server asked for the town's extra Sims (with no other lot open, so nothing else is writing the town)
                    try
                    {
                        // online: every home is left for the players - the town's own families and the new ones are townies
                        var line = ReduxPopulation.EnsurePopulated(10, 10, 0, new HashSet<int>(OwnedFamilies()));
                        Say("populated " + (line ?? "nothing to do - the town already has its extra Sims"));
                    }
                    catch (Exception e) { Say("error populate " + e.Message); Environment.Exit(8); }
                    Environment.Exit(0);
                }
                // 2D: other lots are open too - this one only ever saves its own family and Sims
                var houseFam = hood.GetFamilyForHouse(House);
                if (houseFam != null) MyFamilies.Add(houseFam.ChunkID);
                hood.SharedTownOwnership = TownOwnership;
                if (MoveOutFamily >= 0)
                {
                    // a player's family leaves the town: the house goes up for sale, the lot doesn't open
                    try
                    {
                        var fam = hood.GetFamilyForHouse(House);
                        if (fam == null || fam.ChunkID != MoveOutFamily) { Say("error that family doesn't live in house " + House); Environment.Exit(6); }
                        hood.MoveOut(House);
                        fam.Unknown = 0; // not waiting in the family bin either: gone from the town
                        hood.SaveNeighbourhood(true);
                        Say("movedout");
                    }
                    catch (Exception e) { Say("error moveout " + e.Message); Environment.Exit(6); }
                    Environment.Exit(0);
                }
                if (MoneyFamily >= 0)
                {
                    // the admin gave (or took) money while the lot was closed: change it and close again
                    try
                    {
                        var fam = hood.GetFamilyForHouse(House);
                        if (fam == null || fam.ChunkID != MoneyFamily) { Say("error that family doesn't live in house " + House); Environment.Exit(7); }
                        fam.Budget = Math.Max(0, fam.Budget + MoneyAmount);
                        hood.SaveNeighbourhood(true);
                        Say("money " + fam.Budget);
                    }
                    catch (Exception e) { Say("error money " + e.Message); Environment.Exit(7); }
                    Environment.Exit(0);
                }
                if (!string.IsNullOrEmpty(MoveInFile))
                {
                    // 2C: a player's family moves in here first (bought with their own money)
                    try
                    {
                        var pack = ReduxFamilyPackage.FromBytes(File.ReadAllBytes(MoveInFile));
                        var fam = pack.MoveInto(House);
                        MyFamilies.Add(fam.ChunkID);
                        Say("movedin " + fam.ChunkID);
                        Say("family " + fam.ChunkID + " has §" + fam.Budget + " after buying the lot");
                    }
                    catch (Exception e)
                    {
                        Say("error movein " + e.Message);
                        Environment.Exit(5);
                    }
                }
                screen.HostAfterLoad = true;
                screen.PlayHouse(House, null);
                Say("loading house " + House);
            }
            catch (Exception e)
            {
                Say("error couldn't open the house: " + e.Message);
                Environment.Exit(4);
            }
        }

        /// <summary>Every frame: report players, answer stuck questions, save, and close when empty.</summary>
        public static void Tick()
        {
            if (!Active || Screen == null || Closing) return;
            var now = DateTime.UtcNow;
            var host = Screen.Host;

            while (Commands.TryDequeue(out var raw))
            {
                var cmd = raw.ToLowerInvariant();
                if (cmd == "save") { SaveNow(); }
                else if (cmd.StartsWith("money ")) { if (host != null) GiveMoney(cmd); else Commands.Enqueue(raw); break; }
                else if (cmd.StartsWith("time ") && long.TryParse(cmd.Substring(5), out var townNow)) { TownMinutes = townNow; if (host != null) KeepTownTime(false); }
                else if (cmd == "quit") { Quit("asked to close"); return; }
                else if (cmd.StartsWith("kick "))
                {
                    // "kick <account>|<message the player sees>"
                    var rest = raw.Substring(5).Trim();
                    var bar = rest.IndexOf('|');
                    var who = (bar < 0 ? rest : rest.Substring(0, bar)).Trim();
                    var why = bar < 0 ? "An admin removed you from the online town." : rest.Substring(bar + 1).Trim();
                    var n = host?.Kick(who, why) ?? 0;
                    Say("kicked " + who + (n == 0 ? " (not on this lot)" : ""));
                }
            }

            if (host == null)
            {
                if (now - Started > TimeSpan.FromMinutes(3)) Quit("the lot never finished loading");
                return;
            }
            if (LastCount < 0)
            {
                Say("hosting port " + Port);
                KeepTownTime(true); // 2D: the same time of day as everywhere else in the town
                var lotFam = Screen.vm?.TS1State?.CurrentFamily;
                if (lotFam != null) Say("family " + lotFam.ChunkID + " lives here with §" + lotFam.Budget);
                LastSave = now;
                EmptySince = now;
                LastCount = 0;
            }

            int count = host.GuestCount;
            if (count != LastCount)
            {
                Say("players " + count + (count > 0 ? " " + string.Join(", ", host.GuestNames) : ""));
                if (count > 0) EverHadPlayers = true;
                if (count == 0) EmptySince = now;
                LastCount = count;
            }

            // the lot runs while someone is on it (at normal speed, like opening a lot in single player)
            // and pauses while it's empty, so nothing happens to a family with nobody watching
            var speedVm = Screen.vm;
            if (speedVm != null && speedVm.SpeedMultiplier != -1 && now - LastSpeedChange > TimeSpan.FromSeconds(2))
            {
                LastSpeedChange = now;
                if (count > 0 && speedVm.SpeedMultiplier == 0) Screen.ChangeSpeedTo(1);
                else if (count == 0 && speedVm.SpeedMultiplier > 0) Screen.ChangeSpeedTo(0);
            }

            // every 10 seconds while players are here: each family's skills, jobs and money, for the
            // server's XP / levels and its cheat checks (online only - single player never runs this)
            if (count > 0 && now - LastStats > TimeSpan.FromSeconds(10) && Screen.vm != null)
            {
                LastStats = now;
                try { ReportStats(Screen.vm); } catch (Exception e) { Say("stats failed: " + e.Message); }
            }

            // nobody is at the keyboard on a worker: a question that pauses the game gets the
            // default answer after a while so the lot carries on
            var vm = Screen.vm;
            if (vm?.GlobalBlockingDialog != null)
            {
                if (BlockedSince == default)
                {
                    BlockedSince = now;
                    var asker = vm.GlobalBlockingDialog;
                    Say("waiting on a question from " + (asker?.ToString() ?? "?") + " (id " + (asker?.ObjectID ?? 0) + ")");
                }
                else if (now - BlockedSince > TimeSpan.FromSeconds(PlayersQuestion(vm) ? 180 : 20))
                {
                    Say("nobody answered the question in time - answering it so the lot carries on");
                    vm.SendCommand(new VMNetDialogResponseCmd { ResponseCode = 0, ResponseText = "" });
                    BlockedSince = default;
                }
            }
            else BlockedSince = default;

            if (now - LastSave > AutoSave) SaveNow();

            if (host.ConnectionCount > 0) EmptySince = now; // someone is still joining
            if (count == 0 && (EverHadPlayers ? now - EmptySince > EmptyGrace : now - Started > FirstPlayerWait))
                Quit(EverHadPlayers ? "everyone left" : "nobody came");
        }

        /// <summary>
        /// "money &lt;family&gt; &lt;amount&gt;" from the server (the admin's /money): changes the money of the
        /// family living on this open lot, for everyone on it at once.
        /// </summary>
        private static void GiveMoney(string cmd)
        {
            try
            {
                var parts = cmd.Split(' ');
                if (parts.Length != 3 || !int.TryParse(parts[1], out var famId) || !int.TryParse(parts[2], out var amount)) { Say("error money: bad command"); return; }
                var vm = Screen.vm;
                var fam = vm?.TS1State?.CurrentFamily;
                if (fam == null || fam.ChunkID != famId) { Say("error money: that family doesn't live here"); return; }
                if (fam.Budget + amount < 0) amount = -fam.Budget; // can't go below §0
                var newBudget = fam.Budget + amount;

                // the money cheat, sent by the lot itself (players can't send it), so everyone's game changes together
                var sim = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<FSO.SimAntics.VMAvatar>(vm.Context.ObjectQueries.Avatars), a => a.PersistID != 0);
                if (sim != null)
                {
                    vm.SendCommand(new VMNetCheatCmd
                    {
                        ActorUID = sim.PersistID,
                        Context = new FSO.SimAntics.Utils.VMCheatContext { CheatBehavior = FSO.SimAntics.Utils.VMCheatContext.VMCheatType.Budget, Amount = amount },
                    });
                }
                else if ((Screen.Host?.GuestCount ?? 0) == 0)
                {
                    fam.Budget = newBudget; // nobody here to keep in step
                }
                else { Say("error money: no Sim on the lot to pay"); return; }
                Say("money " + newBudget);
                LastSave = DateTime.MinValue; // save soon
            }
            catch (Exception e) { Say("error money " + e.Message); }
        }

        /// <summary>
        /// 2D: puts this lot on the town's clock - when it opens (always), and when the server says the
        /// time (only if it has drifted more than a couple of minutes, e.g. it was paused while empty).
        /// </summary>
        private static void KeepTownTime(bool opening)
        {
            try
            {
                var vm = Screen?.vm;
                if (vm == null || TownMinutes < 0) return;
                var clock = vm.Context.Clock;
                int townDay = (int)(TownMinutes / 1440 % 30) + 1;      // the game counts days 1-30
                int townOfDay = (int)(TownMinutes % 1440);
                int lotOfDay = clock.Hours * 60 + clock.Minutes;
                int diff = (clock.DayOfMonth - townDay) * 1440 + (lotOfDay - townOfDay);
                if (!opening && Math.Abs(diff) <= 2) return;
                vm.SendCommand(new VMNetSetTimeCmd
                {
                    Hours = townOfDay / 60,
                    Minutes = townOfDay % 60,
                    Seconds = 0,
                    UTCStart = clock.UTCStart,
                    DayOfMonth = townDay,
                });
                if (opening || Math.Abs(diff) > 10)
                    Say("town time: day " + (TownMinutes / 1440 + 1) + " " + (townOfDay / 60) + ":" + (townOfDay % 60).ToString("00") + (opening ? " (lot opened)" : " (was " + diff + " minutes off)"));
            }
            catch (Exception e) { Say("error town time: " + e.Message); }
        }

        /// <summary>
        /// Is the question about a Sim a player is controlling? Then that player is reading it (a job
        /// list, a name to type...) - the lot waits for them, only stepping in after a long while.
        /// </summary>
        private static bool PlayersQuestion(FSO.SimAntics.VM vm)
        {
            var asker = vm.GlobalBlockingDialog as FSO.SimAntics.VMAvatar;
            if (asker == null) return false;
            return asker.PersistID >= ReduxOnlineInfo.FirstGuestID || vm.ReduxFamilyControl.ContainsKey(asker.PersistID);
        }

        private static void SaveNow()
        {
            try
            {
                Screen.Save();
                LastSave = DateTime.UtcNow;
                Say("saved");
            }
            catch (Exception e) { Say("error saving: " + e.Message); }
        }

        private static void Quit(string why)
        {
            Closing = true;
            SaveNow();
            Say("closing (" + why + ")");
            try { Screen.Host?.Stop("The lot is closing."); } catch { }
            Environment.Exit(0);
        }

        // ---------- no window on the server's screen ----------

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte alpha, uint flags);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private static IntPtr Disguised;

        /// <summary>
        /// Keeps the lot's game window off the server PC's screen. The game shows its window itself
        /// after starting up (whatever the server asked for), so the window is first made fully
        /// see-through, taken off the taskbar and moved far off-screen - then even when it's "shown"
        /// there's nothing to see - and it's also hidden outright.
        /// </summary>
        public static void HideWindow(IntPtr handle = default)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                if (handle == IntPtr.Zero) handle = GameFacade.Game?.Window?.Handle ?? IntPtr.Zero;
                if (handle == IntPtr.Zero) return;
                if (Disguised != handle)
                {
                    const int GWL_EXSTYLE = -20;
                    const long WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x8000000;
                    var ex = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
                    ex = (ex | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
                    SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(ex));
                    SetLayeredWindowAttributes(handle, 0, 0, 0x2); // LWA_ALPHA, fully see-through
                    SetWindowPos(handle, IntPtr.Zero, -32000, -32000, 0, 0, 0x1 | 0x4 | 0x10 | 0x20); // NOSIZE|NOZORDER|NOACTIVATE|FRAMECHANGED
                    Disguised = handle;
                }
                ShowWindow(handle, 0); // SW_HIDE
            }
            catch { }
        }
    }
}
