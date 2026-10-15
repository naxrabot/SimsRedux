using System.Text;

namespace SimsRedux.Server
{
    /// <summary>
    /// The console commands, typed into the panel (or the server's own window). Only this PC can use
    /// them: they never come in over the network.
    /// </summary>
    public class Commands
    {
        private readonly ServerConfig Config;
        private readonly Accounts Accounts;
        private readonly GameFiles Files;
        private readonly Api Api;
        public Lots Lots;
        public Action<int> Quit; // exit code: 0 stop, 75 restart

        public Commands(ServerConfig config, Accounts accounts, GameFiles files, Api api)
        {
            Config = config; Accounts = accounts; Files = files; Api = api;
        }

        public static readonly string[][] Help =
        {
            new[] { "/help", "this list" },
            new[] { "/status", "who's online, accounts, game files, memory, uptime" },
            new[] { "/who", "accounts active in the last 5 minutes" },
            new[] { "/accounts", "every account with its role" },
            new[] { "/account add <name> <password> [role]", "make an account (role: admin, tester or player)" },
            new[] { "/role <name> <admin|tester|player>", "change an account's role (Admins and Testers get the game files)" },
            new[] { "/approve <name>", "let a new account in (accounts made on the website wait for this)" },
            new[] { "/pending", "accounts waiting for approval" },
            new[] { "/resetpw <name> <password>", "set a new password for someone who forgot theirs" },
            new[] { "/suspend <name>", "lock an account out" },
            new[] { "/unsuspend <name>", "let them back in" },
            new[] { "/delete <name>", "remove an account for good" },
            new[] { "/registration on|off", "let people make accounts on the website, or not" },
            new[] { "/files", "the game files Admins and Testers can download" },
            new[] { "/files folder <path>", "where the Sims 1 game folder is on this PC" },
            new[] { "/files rescan", "look at the game folder again" },
            new[] { "/motd <text>", "the message players see when they log in" },
            new[] { "/town", "the online town: its houses and which are open" },
            new[] { "/town fresh", "start a brand-new town: the game's Neighbourhood 1 with its starting families (the old one goes to backups/)" },
            new[] { "/town fresh empty", "start a brand-new town with houses but no families" },
            new[] { "/town sources", "every neighbourhood the server can find, with how many families live in it" },
            new[] { "/town time [<day> <time>]", "the online town's clock (runs while anyone plays) - e.g. /town time 2 7:30pm" },
            new[] { "/town owners", "which account's family lives in which house" },
            new[] { "/town disown <account>", "the account no longer owns its town family (they stay as a town family)" },
            new[] { "/town import [folder]", "copy a neighbourhood in as the town (default: your Neighborhood 1)" },
            new[] { "/money <account> <amount>", "give a player's town family money (a minus amount takes it away, e.g. /money joey -500)" },
            new[] { "/lots", "lots that are open right now, with who's on them" },
            new[] { "/lots open <house>", "open a lot (players can also just travel there)" },
            new[] { "/lots close <house|all>", "save and close a lot" },
            new[] { "/lots game <path>", "the game program that runs lots (Simitone.exe, or its folder)" },
            new[] { "/shutdown <seconds> [restart]", "stop (or restart) the server after a countdown everyone online is told about" },
            new[] { "/shutdown cancel", "call off a countdown" },
            new[] { "/maintenance on|off", "maintenance: only Admin accounts can log in and be online (others are sent off)" },
            new[] { "/mod <name> <powers|none>", "moderator powers for an account: say, kick, reports, flags, events (comma-separated)" },
            new[] { "/mods", "accounts with moderator powers" },
            new[] { "/backups auto <hour|off> [keep]", "when the daily backup runs (0-23, this PC's time) and how many are kept" },
            new[] { "/say <text>", "a message on every online player's screen (on a lot or the town map)" },
            new[] { "/kick <name> [minutes]", "send a player off the town's lots (default: they can't come back for 10 minutes)" },
            new[] { "/unkick <name>", "let a kicked player back in now" },
            new[] { "/event", "the event types, and events on now or coming up" },
            new[] { "/event start <type> [hours] [title]", "start an event now - e.g. /event start doublexp 3" },
            new[] { "/event at <yyyy-mm-dd> <time> <type> [hours] [title]", "schedule one - e.g. /event at 2026-12-24 6pm holiday 30 Christmas Eve" },
            new[] { "/event stop <type|all>", "end events early" },
            new[] { "/xp [name]", "a player's level and XP (no name: the top 10)" },
            new[] { "/xp give <name> <amount>", "give (or with minus, take) XP" },
            new[] { "/xp settings", "how much XP everything is worth" },
            new[] { "/xp set <setting> <value>", "change an XP amount - e.g. /xp set PerMinute 3" },
            new[] { "/reports [all]", "players' reports about other players (all = done ones too)" },
            new[] { "/reports done <id|all>", "mark reports as dealt with" },
            new[] { "/flags [all]", "the cheat checks' flags (online only) waiting for a look" },
            new[] { "/flags ok <id|all>", "mark flags as looked at" },
            new[] { "/backups", "the backups (one is made automatically every day)" },
            new[] { "/restore <backup> confirm", "put a backup back (the server restarts; a copy of now is kept first)" },
            new[] { "/name <text>", "the server's name" },
            new[] { "/save", "save everything now" },
            new[] { "/backup", "save and keep a dated copy in backups/" },
            new[] { "/restart", "restart the server" },
            new[] { "/quit", "save and stop the server" },
        };

        /// <summary>"7:30pm", "19:30", "7pm" -> minutes after midnight.</summary>
        private static bool TryParseClock(string text, out long minutes)
        {
            minutes = 0;
            var t = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            bool pm = t.EndsWith("pm"), am = t.EndsWith("am");
            if (pm || am) t = t.Substring(0, t.Length - 2);
            var parts = t.Split(':');
            if (!int.TryParse(parts[0], out var h)) return false;
            int m = 0;
            if (parts.Length > 1 && !int.TryParse(parts[1], out m)) return false;
            if (pm || am) { if (h < 1 || h > 12) return false; h = h % 12 + (pm ? 12 : 0); }
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            minutes = h * 60 + m;
            return true;
        }

        public void Run(string line)
        {
            line = (line ?? "").Trim();
            if (line.Length == 0) return;
            if (!line.StartsWith("/"))
            {
                Log.Reply("(Chat to players arrives with lots on this server. Type /help for commands.)");
                return;
            }
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            string Arg(int i) => i < parts.Length ? parts[i] : null;
            string Rest(int i) => i < parts.Length ? string.Join(' ', parts.Skip(i)) : "";
            string error;

            switch (cmd)
            {
                case "/help":
                    var sb = new StringBuilder("Commands:\n");
                    foreach (var h in Help) sb.Append("  ").Append(h[0].PadRight(40)).Append(h[1]).Append('\n');
                    Log.Reply(sb.ToString().TrimEnd());
                    break;

                case "/status":
                    var active = Accounts.RecentlyActive(TimeSpan.FromMinutes(5));
                    var mem = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576;
                    Log.Reply(Config.Name + " (" + Config.Channel + ") v" + Api.Version + "\n" +
                        "  accounts: " + Accounts.Count + ", active now: " + active.Count + ", on lots: " + (Api.LotConnections + Lots.PlayerCount) + "\n" +
                        "  town: " + (Lots.TownReady ? Lots.Houses().Count + " houses, " + Lots.Running().Count + " open" : "not set up (/town import)") + "\n" +
                        "  game files: " + (Files.Ready ? Files.Count + " files, " + Files.TotalBytes / 1048576 + " MB" : Files.Problem) + "\n" +
                        "  new accounts: " + (Config.Registration ? "open" : "closed") + "   memory: " + mem + " MB");
                    break;

                case "/who":
                    var who = Accounts.RecentlyActive(TimeSpan.FromMinutes(5));
                    Log.Reply(who.Count == 0 ? "nobody active in the last 5 minutes" : "active: " + string.Join(", ", who));
                    break;

                case "/accounts":
                    var all = Accounts.All();
                    if (all.Count == 0) { Log.Reply("no accounts yet - make yours with /account add <name> <password> admin"); break; }
                    var lines = new StringBuilder(all.Count + " account(s):\n");
                    foreach (var a in all)
                        lines.Append("  ").Append(a.Name.PadRight(22)).Append(a.Role.PadRight(8))
                             .Append(a.Suspended ? " SUSPENDED" : "")
                             .Append(a.Pending ? " WAITING FOR APPROVAL" : "")
                             .Append(a.RealName.Length > 0 || a.Email.Length > 0 ? "  [" + a.RealName + (a.RealName.Length > 0 && a.Email.Length > 0 ? ", " : "") + a.Email + "]" : "")
                             .Append("  last login ").Append(a.LastLogin == default ? "never" : a.LastLogin.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
                             .Append('\n');
                    Log.Reply(lines.ToString().TrimEnd());
                    break;

                case "/account":
                    if (Arg(1)?.ToLowerInvariant() != "add" || Arg(2) == null || Arg(3) == null) { Log.Reply("usage: /account add <name> <password> [admin|tester|player]"); break; }
                    // a name can have spaces: the role (if given) is the last word, the password the one before it
                    {
                        bool hasRole = parts.Length >= 5 && Roles.Valid(parts[^1].ToLowerInvariant());
                        var role = hasRole ? parts[^1].ToLowerInvariant() : Roles.Player;
                        int pwIndex = hasRole ? parts.Length - 2 : parts.Length - 1;
                        var name = string.Join(' ', parts.Skip(2).Take(pwIndex - 2));
                        var pw = parts[pwIndex];
                        var made = Accounts.Create(name, pw, role, "", out error);
                        Log.Reply(made == null ? "couldn't make it: " + error : "made " + made.Name + " (" + made.Role + ")");
                        if (made != null) Log.Info("account " + made.Name + " made from the console (" + made.Role + ")");
                    }
                    break;

                case "/role":
                    if (parts.Length < 3) { Log.Reply("usage: /role <name> <admin|tester|player>"); break; }
                    {
                        var role = parts[^1].ToLowerInvariant();
                        var name = string.Join(' ', parts.Skip(1).Take(parts.Length - 2));
                        Log.Reply(Accounts.SetRole(name, role, out error) ? name + " is now " + role : error);
                    }
                    break;

                case "/resetpw":
                    if (parts.Length < 3) { Log.Reply("usage: /resetpw <name> <new password>"); break; }
                    {
                        var name = string.Join(' ', parts.Skip(1).Take(parts.Length - 2));
                        Log.Reply(Accounts.SetPassword(name, parts[^1], out error) ? "new password set for " + name + " (they're logged out everywhere)" : error);
                    }
                    break;

                case "/approve":
                case "/unapprove":
                    if (Arg(1) == null) { Log.Reply("usage: " + cmd + " <name>"); break; }
                    Log.Reply(Accounts.SetPending(Rest(1), cmd == "/unapprove", out error)
                        ? (cmd == "/approve" ? Rest(1) + " is approved and can log in" : Rest(1) + " is waiting for approval again") : error);
                    break;

                case "/pending":
                    {
                        var waiting = Accounts.Waiting();
                        Log.Reply(waiting.Count == 0 ? "nobody is waiting for approval"
                            : waiting.Count + " waiting for approval: " + string.Join(", ", waiting.Select(w => w.Name + (w.RealName.Length > 0 ? " (" + w.RealName + (w.Email.Length > 0 ? ", " + w.Email : "") + ")" : ""))) + "\napprove with  /approve <name>");
                    }
                    break;

                case "/suspend":
                case "/unsuspend":
                    if (Arg(1) == null) { Log.Reply("usage: " + cmd + " <name>"); break; }
                    Log.Reply(Accounts.SetSuspended(Rest(1), cmd == "/suspend", out error) ? Rest(1) + (cmd == "/suspend" ? " is suspended" : " can log in again") : error);
                    break;

                case "/delete":
                    if (Arg(1) == null) { Log.Reply("usage: /delete <name>"); break; }
                    Log.Reply(Accounts.Delete(Rest(1), out error) ? "deleted " + Rest(1) : error);
                    break;

                case "/registration":
                    if (Arg(1) == "on" || Arg(1) == "off")
                    {
                        Config.Registration = Arg(1) == "on";
                        Config.Save();
                    }
                    Log.Reply("new accounts from the website are " + (Config.Registration ? "OPEN" : "CLOSED"));
                    break;

                case "/files":
                    if (Arg(1)?.ToLowerInvariant() == "folder" && Arg(2) != null)
                    {
                        Config.GameFolder = Rest(2).Trim('"');
                        Config.Save();
                        Files.Scan(Config.GameFolder);
                    }
                    else if (Arg(1)?.ToLowerInvariant() == "rescan") Files.Scan(Config.GameFolder);
                    Log.Reply(Files.Ready
                        ? "game files: " + Files.Count + " files, " + Files.TotalBytes / 1048576 + " MB from " + Files.Root + "\n  Admin and Tester accounts can download these on the website."
                        : "game files not available: " + Files.Problem);
                    break;

                case "/town":
                    if (Arg(1)?.ToLowerInvariant() == "import") { Log.Reply(Lots.ImportTown(Rest(2).Trim('"'))); break; }
                    if (Arg(1)?.ToLowerInvariant() == "fresh") { Log.Reply("setting up a fresh town (this can take a minute)..."); Log.Reply(Lots.FreshTown(Arg(2)?.ToLowerInvariant() == "empty")); break; }
                    if (Arg(1)?.ToLowerInvariant() == "sources") { Log.Reply(Lots.Sources()); break; }
                    if (Arg(1)?.ToLowerInvariant() == "owners")
                    {
                        var owners = Owners.All();
                        Log.Reply(owners.Count == 0 ? "no player families in the town yet"
                            : string.Join("\n", owners.Values.OrderBy(o => o.House).Select(o => "house " + o.House + ": " + o.Account + " (the " + o.Name + " family, family " + o.Family + ")")));
                        break;
                    }
                    if (Arg(1)?.ToLowerInvariant() == "time")
                    {
                        // /town time            what time it is in the town
                        // /town time 2 7:30pm   set it (day, then time)
                        if (Arg(2) == null) { Log.Reply("the town's time: " + TownClock.Describe(TownClock.Now) + " (it runs while anyone is playing in the town)"); break; }
                        if (!long.TryParse(Arg(2), out var tday) || tday < 1 || Arg(3) == null || !TryParseClock(Rest(3), out var tmins))
                        { Log.Reply("usage: /town time <day> <time>   e.g. /town time 2 7:30pm"); break; }
                        TownClock.Set((tday - 1) * 1440 + tmins, Lots);
                        Log.Reply("the town's time is now " + TownClock.Describe(TownClock.Now) + " - open lots change to it straight away");
                        break;
                    }
                    if (Arg(1)?.ToLowerInvariant() == "disown")
                    {
                        var key = Accounts.KeyOf(Rest(2));
                        if (Owners.Of(key) == null) { Log.Reply(Rest(2) + " doesn't own a family in the town"); break; }
                        Owners.Set(key, null);
                        Log.Reply(Rest(2) + " no longer owns their town family");
                        break;
                    }
                    if (!Lots.TownReady) { Log.Reply("no town yet - start one with  /town fresh"); break; }
                    {
                        var houses = Lots.Houses();
                        var open = Lots.Running().ToDictionary(w => w.House);
                        Log.Reply("town: " + Lots.TownDir + "\n" + IffPeek.Describe(Lots.TownDir) + "\n" + houses.Count + " houses: " +
                            string.Join(", ", houses.Select(h => h + (open.ContainsKey(h) ? (open[h].Ready ? " (open)" : " (opening)") : ""))) +
                            "\nPlayers travel to a house with the address  tsr-lot.mbrew.studio/lot/<house>");
                    }
                    break;

                case "/money":
                    {
                        if (parts.Length != 3 || !int.TryParse(Arg(2).Replace(",", "").TrimStart('\u00a7', '$'), out var mAmount) || mAmount == 0 || Math.Abs(mAmount) > 10000000)
                        { Log.Reply("usage: /money <account> <amount>   e.g. /money joey 5000  (or -500 to take some away)"); break; }
                        var mWho = Arg(1);
                        var mOwner = Owners.Of(Accounts.KeyOf(mWho));
                        if (mOwner == null) { Log.Reply(mWho + " doesn't have a family in the town"); break; }
                        var mHouse = (short)mOwner.House;
                        AntiCheat.ExpectMoney(mOwner.Family, mAmount); // (so the cheat checks don't flag it)
                        var mWhat = (mAmount > 0 ? "\u00a7" + mAmount + " to " : "\u00a7" + (-mAmount) + " from ") + mWho + "'s family (" + mOwner.Name + ")";
                        var mOpen = Lots.Running().FirstOrDefault(x => x.House == mHouse && x.Ready && !x.Closing && !x.Finished);
                        if (mOpen != null)
                        {
                            // their lot is open: the money changes right away for everyone on it
                            Lots.Send(mOpen, "money " + mOwner.Family + " " + mAmount);
                            Log.Reply("giving " + mWhat + " - house " + mHouse + " is open, so it shows straight away");
                            break;
                        }
                        if (Lots.Running().Any(x => x.House == mHouse)) { Log.Reply("house " + mHouse + " is opening or closing - try again in a few seconds"); break; }
                        // closed: the lot opens just long enough to change it, then closes again
                        Log.Reply("giving " + mWhat + " (house " + mHouse + " is closed - this takes a few seconds)");
                        var mFamNo = mOwner.Family;
                        _ = Task.Run(async () =>
                        {
                            var w = Lots.Get(mHouse, out var mErr, new[] { "-money" + mFamNo + ":" + mAmount });
                            if (w == null) { Log.Reply("couldn't give the money: " + mErr); return; }
                            await Lots.WaitExit(w, TimeSpan.FromSeconds(90));
                            Log.Reply(w.MoneyNow >= 0 ? mWho + "'s family now has \u00a7" + w.MoneyNow : "couldn't give the money: " + (w.Problem ?? "the lot didn't finish").Replace("error ", ""));
                        });
                    }
                    break;

                case "/lots":
                    switch (Arg(1)?.ToLowerInvariant())
                    {
                        case "open":
                            if (!short.TryParse(Arg(2), out var oh)) { Log.Reply("usage: /lots open <house number>"); break; }
                            Log.Reply(Lots.Get(oh, out error) != null ? "house " + oh + " is opening" : "couldn't: " + error);
                            break;
                        case "close":
                            if (Arg(2)?.ToLowerInvariant() == "all") { Lots.CloseAll(); Log.Reply("closed every lot"); break; }
                            if (!short.TryParse(Arg(2), out var ch)) { Log.Reply("usage: /lots close <house number|all>"); break; }
                            Log.Reply(Lots.Close(ch) ? "house " + ch + " is saving and closing" : "house " + ch + " isn't open");
                            break;
                        case "game":
                            if (Arg(2) != null) { Config.GameExe = Rest(2).Trim('"'); Config.Save(); }
                            Log.Reply("game program: " + (Lots.GameExe() ?? "NOT FOUND - /lots game <path to Simitone.exe>"));
                            break;
                        default:
                            var running = Lots.Running();
                            if (running.Count == 0) { Log.Reply("no lots open. Players open one by travelling to it; or /lots open <house>"); break; }
                            Log.Reply(string.Join("\n", running.Select(w => "house " + w.House + ": " + (w.Ready ? (w.Players == 0 ? "open, empty" : w.Players + " here (" + w.Who + ")") : "opening...") +
                                "  \u00b7 up " + (int)(DateTime.UtcNow - w.Started).TotalMinutes + "m  \u00b7 port " + w.Port)));
                            break;
                    }
                    break;

                case "/motd":
                    if (Arg(1) != null) { Config.Motd = Rest(1); Config.Save(); }
                    Log.Reply("message of the day: " + Config.Motd);
                    break;

                case "/name":
                    if (Arg(1) != null) { Config.Name = Rest(1); Config.Save(); }
                    Log.Reply("server name: " + Config.Name);
                    break;

                case "/save":
                    Accounts.SaveAll();
                    Config.Save();
                    Log.Reply("saved");
                    break;

                case "/backup":
                    Accounts.SaveAll();
                    Config.Save();
                    Log.Reply(Program.Backup());
                    break;

                case "/restart":
                    Lots.CloseAll();
                    Log.Info("restarting...");
                    Quit?.Invoke(75);
                    break;

                case "/shutdown":
                    {
                        if (Arg(1)?.ToLowerInvariant() == "cancel") { Log.Reply(Shutdown.Cancel() ? "countdown called off" : "no countdown running"); break; }
                        if (Arg(1) == null || !int.TryParse(Arg(1), out var secs) || secs < 0 || secs > 3600) { Log.Reply("usage: /shutdown <seconds 0-3600> [restart]   or   /shutdown cancel"); break; }
                        var restart = Arg(2)?.ToLowerInvariant() == "restart";
                        if (secs == 0) { Lots.CloseAll(); Log.Info(restart ? "restarting..." : "stopping..."); Quit?.Invoke(restart ? 75 : 0); break; }
                        Log.Reply(Shutdown.Start(secs, restart) + "  (/shutdown cancel to call it off)");
                    }
                    break;

                case "/maintenance":
                    {
                        var on = Arg(1)?.ToLowerInvariant();
                        if (on != "on" && on != "off") { Log.Reply("maintenance is " + (Config.Maintenance ? "ON" : "off") + " - usage: /maintenance on|off"); break; }
                        Config.Maintenance = on == "on";
                        Config.Save();
                        if (Config.Maintenance)
                        {
                            Notices.Add("Maintenance", "The server is going into maintenance. Online play will be back soon!");
                            int sent = 0;
                            foreach (var key in Api.PlayingNow())
                            {
                                var acc = Accounts.Find(key);
                                if (acc != null && acc.Role == Roles.Admin) continue;
                                Moderation.Kick(Lots, Accounts, acc?.Name ?? key, 0, "The server is in maintenance - online play will be back soon.");
                                sent++;
                            }
                            Log.Reply("maintenance ON: only Admin accounts can log in and be online" + (sent > 0 ? " (" + sent + " player(s) sent off)" : ""));
                        }
                        else
                        {
                            Notices.Add("Maintenance over", "The server is back - welcome back!");
                            Log.Reply("maintenance off: everyone can play again");
                        }
                    }
                    break;

                case "/mod":
                    {
                        if (Arg(2) == null) { Log.Reply("usage: /mod <name> <powers|none>   powers: " + string.Join(", ", Powers.All)); break; }
                        var modLine = Rest(1);
                        var last = modLine.LastIndexOf(' ');
                        var modName = modLine.Substring(0, last).Trim();
                        var modList = modLine.Substring(last + 1).ToLowerInvariant();
                        Log.Reply(Accounts.SetPowers(modName, modList == "none" ? new string[0] : modList.Split(',', StringSplitOptions.RemoveEmptyEntries)));
                    }
                    break;

                case "/mods":
                    {
                        var mods = Accounts.All().Where(a => (a.Powers?.Count ?? 0) > 0).ToList();
                        Log.Reply(mods.Count == 0 ? "no moderators yet (Admins can do everything anyway) - give powers with /mod <name> <powers>" :
                            "moderators:\n" + string.Join("\n", mods.Select(a => "  " + a.Name.PadRight(20) + string.Join(", ", a.Powers))));
                    }
                    break;

                case "/say":
                    if (Rest(1).Length == 0) { Log.Reply("usage: /say <text>"); break; }
                    Notices.Add("Message from " + Config.Name, Rest(1));
                    Log.Reply("sent to everyone online: " + Rest(1));
                    break;

                case "/kick":
                    {
                        var kWho = Arg(1);
                        if (kWho == null) { Log.Reply("usage: /kick <name> [minutes]"); break; }
                        var kMin = 10;
                        if (Arg(2) != null && (!int.TryParse(Arg(2), out kMin) || kMin < 0 || kMin > 100000)) { Log.Reply("usage: /kick <name> [minutes]"); break; }
                        Log.Reply(Moderation.Kick(Lots, Accounts, kWho, kMin, null) + (kMin > 0 ? "  (/unkick to let them in sooner)" : ""));
                    }
                    break;

                case "/unkick":
                    if (Arg(1) == null) { Log.Reply("usage: /unkick <name>"); break; }
                    Log.Reply(Kicks.Remove(Accounts.KeyOf(Arg(1))) ? Arg(1) + " can come back now" : Arg(1) + " wasn't kicked");
                    break;

                case "/event":
                    {
                        var sub = Arg(1)?.ToLowerInvariant();
                        if (sub == null)
                        {
                            var ev = new StringBuilder("event types:\n");
                            foreach (var k in Events.Kinds) ev.Append("  ").Append(k.Key.PadRight(10)).Append(k.Value.Title).Append(" - ").Append(k.Value.About).Append('\n');
                            var list = Events.All();
                            ev.Append(list.Count == 0 ? "no events on or coming up" : "on now / coming up:\n" + string.Join("\n", list.Select(e => "  " + Events.Describe(e))));
                            Log.Reply(ev.ToString());
                        }
                        else if (sub == "start")
                        {
                            if (Arg(2) == null) { Log.Reply("usage: /event start <type> [hours] [title]"); break; }
                            double hours = 2; int titleAt = 3;
                            if (Arg(3) != null && double.TryParse(Arg(3), out var h)) { hours = h; titleAt = 4; }
                            Log.Reply(Events.Add(Arg(2), DateTime.UtcNow, hours, Rest(titleAt)));
                        }
                        else if (sub == "at")
                        {
                            if (Arg(4) == null || !DateTime.TryParse(Arg(2), out var day) || !TryParseClock(Arg(3), out var mins))
                            { Log.Reply("usage: /event at <yyyy-mm-dd> <time> <type> [hours] [title]   e.g. /event at 2026-12-24 6pm holiday 30"); break; }
                            var start = day.Date.AddMinutes(mins);
                            if (start < DateTime.Now) { Log.Reply("that time has already passed (it's this PC's time)"); break; }
                            double hours = 2; int titleAt = 5;
                            if (Arg(5) != null && double.TryParse(Arg(5), out var h)) { hours = h; titleAt = 6; }
                            Log.Reply(Events.Add(Arg(4), start.ToUniversalTime(), hours, Rest(titleAt)));
                        }
                        else if (sub == "stop")
                        {
                            if (Arg(2) == null) { Log.Reply("usage: /event stop <type|all>"); break; }
                            var n = Events.Stop(Arg(2).ToLowerInvariant());
                            Log.Reply(n == 0 ? "no such event on or coming up" : "ended " + n + " event(s)");
                        }
                        else Log.Reply("usage: /event, /event start, /event at, /event stop - see /help");
                    }
                    break;

                case "/xp":
                    {
                        var sub = Arg(1)?.ToLowerInvariant();
                        if (sub == null)
                        {
                            var top = Progress.Top(10);
                            Log.Reply(top.Count == 0 ? "nobody has earned XP yet (it's earned playing online)" :
                                "top players:\n" + string.Join("\n", top.Select((t, i) => "  " + (i + 1) + ". " + t.key.PadRight(20) + " level " + t.p.Level + "  (" + t.p.Xp + " XP, " + t.p.Minutes + " min played)"))
                                + (Events.XpMultiplier() != 1 ? "\nXP is x" + Events.XpMultiplier() + " right now (an event)" : ""));
                        }
                        else if (sub == "settings") Log.Reply("XP amounts (/xp set <setting> <value>):\n" + Progress.SettingsText());
                        else if (sub == "set")
                        {
                            if (Arg(3) == null || !int.TryParse(Arg(3), out var v)) { Log.Reply("usage: /xp set <setting> <value> - see /xp settings"); break; }
                            Log.Reply(Progress.Set(Arg(2), v));
                        }
                        else if (sub == "give")
                        {
                            if (Arg(3) == null || !long.TryParse(Arg(3), out var amt)) { Log.Reply("usage: /xp give <name> <amount>"); break; }
                            var key = Accounts.KeyOf(Arg(2));
                            Progress.Award(key, amt, "given by an admin", false);
                            Progress.Save();
                            var lv = Progress.Where(key);
                            Log.Reply(Arg(2) + " is level " + lv.level + " (" + lv.into + " / " + lv.need + " XP to the next)");
                        }
                        else
                        {
                            var key = Accounts.KeyOf(Arg(1));
                            var p = Progress.Of(key);
                            var lv = Progress.Where(key);
                            Log.Reply(Arg(1) + ": level " + lv.level + " - " + lv.into + " / " + lv.need + " XP to the next, " + p.Xp + " XP in all, " + p.Minutes + " minutes played online");
                        }
                    }
                    break;

                case "/reports":
                    if (Arg(1)?.ToLowerInvariant() == "done")
                    {
                        if (Arg(2) == null) { Log.Reply("usage: /reports done <id|all>"); break; }
                        Log.Reply("marked " + Reports.Close(Arg(2).ToLowerInvariant()) + " report(s) as dealt with");
                        break;
                    }
                    {
                        var rAll = Arg(1)?.ToLowerInvariant() == "all";
                        var rs = Reports.List(rAll);
                        Log.Reply(rs.Count == 0 ? (rAll ? "no reports yet" : "no reports waiting") :
                            string.Join("\n", rs.Select(r => "  #" + r.Id + (r.Done ? " (done)" : "") + "  " + Store.Ago(r.Time) + "  " + r.From + " about " + r.About + ": " + r.Reason))
                            + (rAll ? "" : "\n(/reports done <id> when it's dealt with)"));
                    }
                    break;

                case "/flags":
                    if (Arg(1)?.ToLowerInvariant() == "ok")
                    {
                        if (Arg(2) == null) { Log.Reply("usage: /flags ok <id|all>"); break; }
                        Log.Reply("marked " + AntiCheat.Review(Arg(2).ToLowerInvariant()) + " flag(s) as looked at");
                        break;
                    }
                    {
                        var fAll = Arg(1)?.ToLowerInvariant() == "all";
                        var fs = AntiCheat.List(fAll);
                        Log.Reply(fs.Count == 0 ? (fAll ? "no flags yet" : "nothing flagged") :
                            string.Join("\n", fs.Select(f => "  #" + f.Id + (f.Reviewed ? " (ok)" : "") + "  " + Store.Ago(f.Time) + "  " + f.Account + "  " + f.What + ": " + f.Detail))
                            + (fAll ? "" : "\n(/flags ok <id> once you've looked - nothing happens to the player by itself)"));
                    }
                    break;

                case "/backups":
                    if (Arg(1)?.ToLowerInvariant() == "auto")
                    {
                        var h = Arg(2)?.ToLowerInvariant();
                        if (h == "off") Config.BackupHour = -1;
                        else if (h != null && int.TryParse(h, out var hr) && hr >= 0 && hr <= 23) Config.BackupHour = hr;
                        else { Log.Reply("usage: /backups auto <hour 0-23|off> [how many to keep]"); break; }
                        if (Arg(3) != null && int.TryParse(Arg(3), out var keep) && keep >= 1 && keep <= 365) Config.KeepBackups = keep;
                        Config.Save();
                        Log.Reply(Config.BackupHour < 0 ? "automatic backups are off" : "automatic backup every day at " + Config.BackupHour + ":00, keeping " + Config.KeepBackups);
                        break;
                    }
                    {
                        var bs = Backups.List();
                        Log.Reply(bs.Count == 0 ? "no backups yet" :
                            "backups (newest first; automatic ones at " + (Config.BackupHour < 0 ? "OFF" : Config.BackupHour + ":00") + ", " + Config.KeepBackups + " kept):\n"
                            + string.Join("\n", bs.Take(30).Select(d => "  " + d.Name.PadRight(36) + (Backups.Size(d) / 1048576.0).ToString("0.0") + " MB")));
                    }
                    break;

                case "/restore":
                    {
                        var name = Arg(1);
                        if (name == null || name.Contains("..") || name.Contains('/') || name.Contains('\\') || !Directory.Exists(Path.Combine(Backups.Dir, name)))
                        { Log.Reply("usage: /restore <backup> confirm   (names from /backups)"); break; }
                        if (Arg(2)?.ToLowerInvariant() != "confirm")
                        { Log.Reply("this puts EVERYTHING back to " + name + " (accounts, the town, XP...). Anything since is lost - a copy of now is kept first. Type: /restore " + name + " confirm"); break; }
                        Accounts.SaveAll(); Config.Save(); Progress.Save();
                        Lots.CloseAll();
                        Log.Info(Program.Backup("before-restore"));
                        File.WriteAllText(Path.Combine(Program.DataDir, Backups.Pending), name);
                        Log.Info("restoring " + name + " - the server restarts now...");
                        Quit?.Invoke(75);
                    }
                    break;

                case "/quit":
                case "/stop":
                    Lots.CloseAll();
                    Log.Info("stopping...");
                    Quit?.Invoke(0);
                    break;

                default:
                    Log.Reply("unknown command " + cmd + " - type /help");
                    break;
            }
        }
    }
}
