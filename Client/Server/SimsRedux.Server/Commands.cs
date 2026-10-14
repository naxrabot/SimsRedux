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
            new[] { "/town import [folder]", "copy a neighbourhood in as the town (default: your Neighborhood 1)" },
            new[] { "/lots", "lots that are open right now, with who's on them" },
            new[] { "/lots open <house>", "open a lot (players can also just travel there)" },
            new[] { "/lots close <house|all>", "save and close a lot" },
            new[] { "/lots game <path>", "the game program that runs lots (Simitone.exe, or its folder)" },
            new[] { "/name <text>", "the server's name" },
            new[] { "/save", "save everything now" },
            new[] { "/backup", "save and keep a dated copy in backups/" },
            new[] { "/restart", "restart the server" },
            new[] { "/quit", "save and stop the server" },
        };

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
                    if (Arg(1)?.ToLowerInvariant() == "fresh") { Log.Reply(Lots.FreshTown(Arg(2)?.ToLowerInvariant() == "empty")); break; }
                    if (!Lots.TownReady) { Log.Reply("no town yet - start one with  /town fresh"); break; }
                    {
                        var houses = Lots.Houses();
                        var open = Lots.Running().ToDictionary(w => w.House);
                        Log.Reply("town: " + Lots.TownDir + "\n" + houses.Count + " houses: " +
                            string.Join(", ", houses.Select(h => h + (open.ContainsKey(h) ? (open[h].Ready ? " (open)" : " (opening)") : ""))) +
                            "\nPlayers travel to a house with the address  tsr-lot.mbrew.studio/lot/<house>");
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
