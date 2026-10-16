using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SimsRedux.Server
{
    /// <summary>
    /// What the server answers:
    ///   GET  /healthz              how the server is (the panel reads this)
    ///   POST /api/register         make an account          { name, password, email? }
    ///   POST /api/login            log in                   { name, password } -> { token, name, role }
    ///   POST /api/logout           log out (token)
    ///   GET  /api/me               who this token is
    ///   POST /api/password         change password          { old, new }
    ///   GET  /api/files            the game-file list       (Admin/Tester only)
    ///   GET  /api/file?p=...       one game file            (Admin/Tester only)
    ///   GET  /api/town/info        the town's stamp and open lots (logged in)
    ///   GET  /api/town             the town's files, for the neighbourhood screen (logged in)
    ///   GET  /lot                  an online lot (WebSocket) - passed to the game hosting it
    ///   GET  /lot/12?t=token       a house in the town (WebSocket) - opened on this server if needed
    /// </summary>
    public class Api
    {
        public const string Version = "1.0";
        private readonly ServerConfig Config;
        private readonly Accounts Accounts;
        private readonly GameFiles Files;
        public Lots Lots;
        private readonly DateTime Started = DateTime.UtcNow;

        private readonly RateLimiter LoginByAddress = new RateLimiter(10, TimeSpan.FromMinutes(5));
        private readonly RateLimiter LoginByName = new RateLimiter(10, TimeSpan.FromMinutes(15));
        private readonly RateLimiter RegisterByAddress = new RateLimiter(5, TimeSpan.FromHours(1));
        private readonly RateLimiter Everything = new RateLimiter(3000, TimeSpan.FromMinutes(1));
        private readonly RateLimiter ReportsByAccount = new RateLimiter(5, TimeSpan.FromHours(1));
        private readonly RateLimiter ModActions = new RateLimiter(30, TimeSpan.FromMinutes(10));
        public const string MaintenanceText = "The server is down for maintenance - online play will be back soon.";

        /// <summary>Accounts on a town lot right now (key -> connections), for XP while playing.</summary>
        private readonly Dictionary<string, int> OnLots = new Dictionary<string, int>();
        public List<string> PlayingNow() { lock (OnLots) return OnLots.Where(x => x.Value > 0).Select(x => x.Key).ToList(); }

        /// <summary>Online-lot connections being passed through right now.</summary>
        public int LotConnections;
        private readonly HashSet<string> DownloadsAnnounced = new HashSet<string>();

        public Api(ServerConfig config, Accounts accounts, GameFiles files)
        {
            Config = config; Accounts = accounts; Files = files;
        }

        public void Tidy()
        {
            LoginByAddress.Tidy(); LoginByName.Tidy(); RegisterByAddress.Tidy(); Everything.Tidy(); ReportsByAccount.Tidy(); ModActions.Tidy();
        }

        public object Health()
        {
            long rss;
            using (var proc = Process.GetCurrentProcess()) rss = proc.WorkingSet64;
            return new
            {
                ok = true,
                name = Config.Name,
                channel = Config.Channel,
                version = Version,
                players = LotConnections + (Lots?.PlayerCount ?? 0),
                lots = Lots?.Running().Count ?? 0,
                town = Lots.TownReady,
                online = Accounts.RecentlyActive(TimeSpan.FromMinutes(5)).Count,
                accounts = Accounts.Count,
                registration = Config.Registration,
                maintenance = Config.Maintenance,
                shutdownIn = Shutdown.SecondsLeft,
                shutdownRestart = Shutdown.Restarting,
                uptime = (int)(DateTime.UtcNow - Started).TotalSeconds,
                rssMB = (int)(rss / 1048576),
                files = new { ready = Files.Ready, count = Files.Count, mb = (int)(Files.TotalBytes / 1048576), problem = Files.Problem },
                motd = Config.Motd,
            };
        }

        public async Task Handle(Request req, Stream stream, TcpClient client)
        {
            var cors = Cors(req);

            // (game-file downloads are thousands of requests, but only for logged-in Admins and Testers)
            bool fileRequest = req.Path == "/api/file" || req.Path == "/api/files";
            if (!fileRequest && !Everything.Allow(req.From))
            {
                await HttpServer.SendJson(stream, 429, new { error = "Too many requests - slow down a little." }, cors);
                return;
            }
            if (req.Method == "OPTIONS")
            {
                await HttpServer.Send(stream, 204, "text/plain", null, cors);
                return;
            }

            // a house in the town: /lot/12
            if (req.Path.StartsWith("/lot/") && IsWebSocket(req))
            {
                await JoinTownLot(req, stream);
                return;
            }

            switch (req.Path)
            {
                case "/healthz":
                    // from the internet: just "it's up" (the full details - folders, memory, files - are for the panel on this PC)
                    if (req.Public) await HttpServer.SendJson(stream, 200, new { ok = true, channel = Config.Channel, players = LotConnections + (Lots?.PlayerCount ?? 0) }, cors);
                    else await HttpServer.SendJson(stream, 200, Health(), cors);
                    return;
                case "/lot":
                case "/":
                    if (IsWebSocket(req)) { await RelayLot(req, stream); return; }
                    if (req.Path == "/")
                    {
                        await HttpServer.Send(stream, 200, "text/plain; charset=utf-8",
                            Encoding.UTF8.GetBytes(Config.Name + " server. Play at https://tsr.mbrew.studio\n"), cors);
                        return;
                    }
                    break;
                case "/api/status":
                    // the website's launcher: is online play up, and how many people are on (counts only, no names)
                    if (req.Method == "GET")
                    {
                        await HttpServer.SendJson(stream, 200, new
                        {
                            ok = true,
                            channel = Config.Channel,
                            maintenance = Config.Maintenance,
                            game = Simitone.Client.Utils.ReduxBuild.Number,
                            servers = new { live = Config.LiveUrl, test = Config.TestUrl },
                            town = Lots.TownReady,
                            online = Accounts.RecentlyActive(TimeSpan.FromMinutes(5)).Count,
                            playing = Lots?.PlayerCount ?? 0,
                            lots = Lots?.Running().Count ?? 0,
                            motd = Config.Motd,
                            events = Events.Active().Select(e => new { title = e.Title, kind = e.Kind, ends = e.End.ToString("o") }),
                        }, cors);
                        return;
                    }
                    break;
                case "/api/patchnotes":
                    // the panel's changes to the patch notes (the launcher puts them over the shipped ones)
                    if (req.Method == "GET")
                    {
                        object notes = new { patches = Array.Empty<object>() };
                        try
                        {
                            var f = Path.Combine(Program.DataDir, "patchnotes.json");
                            if (File.Exists(f)) notes = System.Text.Json.JsonDocument.Parse(File.ReadAllText(f)).RootElement.Clone();
                        }
                        catch (Exception e) { Log.Warn("patchnotes.json couldn't be read: " + e.Message); }
                        await HttpServer.SendJson(stream, 200, notes, cors);
                        return;
                    }
                    break;
                case "/api/mod": if (req.Method == "GET") { await ModInfo(req, stream, cors); return; } break;
                case "/api/mod/do": if (req.Method == "POST") { await ModDo(req, stream, cors); return; } break;
                case "/api/notices": if (req.Method == "GET") { await GetNotices(req, stream, cors); return; } break;
                case "/api/report": if (req.Method == "POST") { await PostReport(req, stream, cors); return; } break;
                case "/api/register": if (req.Method == "POST") { await Register(req, stream, cors); return; } break;
                case "/api/login": if (req.Method == "POST") { await Login(req, stream, cors); return; } break;
                case "/api/logout": if (req.Method == "POST") { Accounts.Logout(req.BearerToken()); await HttpServer.SendJson(stream, 200, new { ok = true }, cors); return; } break;
                case "/api/me": if (req.Method == "GET") { await Me(req, stream, cors); return; } break;
                case "/api/password": if (req.Method == "POST") { await ChangePassword(req, stream, cors); return; } break;
                case "/api/files": if (req.Method == "GET") { await FileList(req, stream, cors); return; } break;
                case "/api/file": if (req.Method == "GET") { await FileGet(req, stream, cors); return; } break;
                case "/api/town/info": if (req.Method == "GET") { await TownInfo(req, stream, cors); return; } break;
                case "/api/town": if (req.Method == "GET") { await TownGet(req, stream, cors); return; } break;
                case "/api/town/fresh": if (req.Method == "POST") { await TownFresh(req, stream, cors); return; } break;
                case "/api/town/movein": if (req.Method == "POST") { await TownMoveIn(req, stream, cors); return; } break;
                case "/api/town/moveout": if (req.Method == "POST") { await TownMoveOut(req, stream, cors); return; } break;
            }
            await HttpServer.SendJson(stream, 404, new { error = "Not found" }, cors);
        }

        // ---------- the website is on another address, so the browser asks permission first ----------

        private Dictionary<string, string> Cors(Request req)
        {
            var h = new Dictionary<string, string>();
            var origin = req.Header("Origin");
            if (origin != null && OriginAllowed(origin))
            {
                h["Access-Control-Allow-Origin"] = origin;
                h["Vary"] = "Origin";
                h["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                h["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
                h["Access-Control-Max-Age"] = "600";
            }
            return h;
        }

        private bool OriginAllowed(string origin)
        {
            if (Config.AllowedOrigins.Any(o => string.Equals(o.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase))) return true;
            // testing the website on this PC
            return origin.StartsWith("http://localhost:") || origin.StartsWith("http://127.0.0.1:") || origin == "http://localhost";
        }

        // ---------- accounts ----------

        private static string Str(JsonElement? body, string name)
        {
            if (body == null || body.Value.ValueKind != JsonValueKind.Object) return null;
            return body.Value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private async Task Register(Request req, Stream stream, Dictionary<string, string> cors)
        {
            if (!Config.Registration)
            {
                await HttpServer.SendJson(stream, 403, new { error = "New accounts are closed right now. Ask the server owner for one." }, cors);
                return;
            }
            if (!RegisterByAddress.Allow(req.From))
            {
                await HttpServer.SendJson(stream, 429, new { error = "Too many new accounts from here. Try again later." }, cors);
                return;
            }
            var body = req.JsonBody();
            // the owner needs to know who's asking (real name) and how to reach them for a password reset (email)
            var email = new string((Str(body, "email") ?? "").Where(c => c > 32 && c < 127).ToArray());
            var realName = new string((Str(body, "realName") ?? "").Where(c => c >= 32).ToArray()).Trim();
            if (realName.Length < 2 || realName.Length > 60)
            {
                await HttpServer.SendJson(stream, 400, new { error = "Please enter your real name, so the admin knows who you are." }, cors);
                return;
            }
            if (email.Length > 120 || !System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$"))
            {
                await HttpServer.SendJson(stream, 400, new { error = "Please enter a valid email address (it's used if you forget your password)." }, cors);
                return;
            }
            // new accounts wait for the server owner to approve them in the panel
            var a = Accounts.Create(Str(body, "name"), Str(body, "password"), Roles.Player, email, out var error, pending: true, realName: realName);
            if (a == null)
            {
                await HttpServer.SendJson(stream, 400, new { error }, cors);
                return;
            }
            Log.Warn("new account " + a.Name + " (" + a.RealName + ", " + a.Email + ") is waiting for your approval (from " + req.From + ") - approve it in Accounts, or type  /approve " + a.Name);
            await HttpServer.SendJson(stream, 200, new { ok = true, pending = true, name = a.Name, message = "Please wait for admin approval before connecting." }, cors);
        }

        private async Task Login(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var body = req.JsonBody();
            var name = Str(body, "name") ?? "";
            if (!LoginByAddress.Allow(req.From) || !LoginByName.Allow(Accounts.KeyOf(name)))
            {
                Log.Warn("login attempts slowed down: " + name + " from " + req.From);
                await HttpServer.SendJson(stream, 429, new { error = "Too many tries. Wait a few minutes and try again." }, cors);
                return;
            }
            var token = Accounts.Login(name, Str(body, "password"), req.From, out var a, out var error);
            if (token == null)
            {
                await HttpServer.SendJson(stream, 401, new { error }, cors);
                return;
            }
            if (Config.Maintenance && a.Role != Roles.Admin)
            {
                Accounts.Logout(token);
                await HttpServer.SendJson(stream, 503, new { error = MaintenanceText }, cors);
                return;
            }
            Log.Info(a.Name + " logged in (" + a.Role + ", from " + req.From + ")");
            await HttpServer.SendJson(stream, 200, new { ok = true, token, name = a.Name, role = a.Role, files = Roles.GetsGameFiles(a.Role) && Files.Ready, motd = Config.Motd, powers = Accounts.PowersOf(a) }, cors);
        }

        private async Task Me(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Not logged in." }, cors); return; }
            var lv = Progress.Where(a.Key);
            await HttpServer.SendJson(stream, 200, new { ok = true, name = a.Name, role = a.Role, files = Roles.GetsGameFiles(a.Role) && Files.Ready, motd = Config.Motd,
                level = lv.level, xp = lv.into, next = lv.need, powers = Accounts.PowersOf(a), maintenance = Config.Maintenance }, cors);
        }

        // ---------- moderators (the website's moderator page) ----------

        /// <summary>What a moderator can see: their powers, and the lists those powers cover.</summary>
        private async Task ModInfo(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            var powers = Accounts.PowersOf(a);
            if (a == null || powers.Count == 0) { await HttpServer.SendJson(stream, 403, new { error = "You don't have moderator powers." }, cors); return; }
            await HttpServer.SendJson(stream, 200, new
            {
                ok = true,
                powers,
                online = powers.Contains("kick") ? Accounts.RecentlyActive(TimeSpan.FromMinutes(5)) : null,
                reports = powers.Contains("reports") ? Reports.List(false).Select(r => new { id = r.Id, time = r.Time.ToString("o"), from = r.From, about = r.About, reason = r.Reason }) : null,
                flags = powers.Contains("flags") ? AntiCheat.List(false).Select(f => new { id = f.Id, time = f.Time.ToString("o"), account = f.Account, what = f.What, detail = f.Detail }) : null,
                events = powers.Contains("events") ? Events.All().Select(e => new { kind = e.Kind, title = e.Title, start = e.Start.ToString("o"), end = e.End.ToString("o") }) : null,
                kinds = powers.Contains("events") ? Events.Kinds.Select(k => new { kind = k.Key, title = k.Value.Title, about = k.Value.About }) : null,
            }, cors);
        }

        /// <summary>A moderator's action: say, kick, report done, flag ok, event start/stop. Checked against their powers and logged.</summary>
        private async Task ModDo(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            var powers = Accounts.PowersOf(a);
            if (a == null || powers.Count == 0) { await HttpServer.SendJson(stream, 403, new { error = "You don't have moderator powers." }, cors); return; }
            if (!ModActions.Allow(a.Key)) { await HttpServer.SendJson(stream, 429, new { error = "That's a lot of actions - wait a few minutes." }, cors); return; }
            var body = req.JsonBody();
            var action = (Str(body, "action") ?? "").ToLowerInvariant();
            string clean(string t, int max)
            {
                var c = new string((t ?? "").Where(ch => ch >= 32).ToArray()).Trim();
                return c.Length > max ? c.Substring(0, max) : c;
            }
            int num(string n)
            {
                if (body is JsonElement b && b.ValueKind == JsonValueKind.Object && b.TryGetProperty(n, out var e))
                {
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v)) return v;
                    if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var v2)) return v2;
                }
                return 0;
            }
            string need = action switch { "say" => "say", "kick" => "kick", "report" => "reports", "flag" => "flags", "event" => "events", "endevent" => "events", _ => null };
            if (need == null || !powers.Contains(need)) { await HttpServer.SendJson(stream, 403, new { error = "You can't do that." }, cors); return; }
            string result;
            switch (action)
            {
                case "say":
                    var text = clean(Str(body, "text"), 300);
                    if (text.Length == 0) { result = "Type a message first."; break; }
                    Notices.Add("Message from " + a.Name, text);
                    result = "Sent to everyone online.";
                    break;
                case "kick":
                    var who = clean(Str(body, "name"), 40);
                    var target = Accounts.Find(who);
                    if (target == null) { result = "No account called " + who + "."; break; }
                    if (target.Role == Roles.Admin) { result = "Admins can't be kicked."; break; }
                    var mins = Math.Clamp(num("minutes"), 0, 60);
                    Moderation.Kick(Lots, Accounts, target.Name, mins, "A moderator removed you from the online town.");
                    result = "Kicked " + target.Name + (mins > 0 ? " for " + mins + " minutes." : ".");
                    break;
                case "report":
                    result = Reports.Close(num("id").ToString()) > 0 ? "Marked as dealt with." : "That report was already dealt with.";
                    break;
                case "flag":
                    result = AntiCheat.Review(num("id").ToString()) > 0 ? "Marked as looked at." : "That flag was already looked at.";
                    break;
                case "event":
                    var hours = Math.Clamp(num("hours"), 1, 24);
                    result = Events.Add(clean(Str(body, "kind"), 20), DateTime.UtcNow, hours, clean(Str(body, "title"), 60));
                    break;
                default: // endevent
                    var n = Events.Stop(clean(Str(body, "kind"), 20).ToLowerInvariant());
                    result = n == 0 ? "No such event running." : "Ended.";
                    break;
            }
            Log.Info("moderator " + a.Name + ": " + action + " - " + result);
            await HttpServer.SendJson(stream, 200, new { ok = true, message = result }, cors);
        }

        /// <summary>The online town's announcements for this player (their game asks every 15 seconds).</summary>
        private async Task GetNotices(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Not logged in." }, cors); return; }
            int.TryParse(req.QueryValue("after") ?? "-1", out var after);
            var (last, list) = Notices.Since(after, a.Key);
            await HttpServer.SendJson(stream, 200, new { ok = true, last, notices = list.Select(n => new { id = n.Id, title = n.Title, text = n.Text }) }, cors);
        }

        /// <summary>A player reports another player (from the website's launcher). It waits in the panel.</summary>
        private async Task PostReport(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Log in first." }, cors); return; }
            var body = req.JsonBody();
            string clean(string t, int max)
            {
                var c = new string((t ?? "").Where(ch => ch >= 32).ToArray()).Trim();
                return c.Length > max ? c.Substring(0, max) : c;
            }
            var about = clean(Str(body, "about"), 40);
            var reason = clean(Str(body, "reason"), 400);
            if (about.Length == 0 || reason.Length < 5) { await HttpServer.SendJson(stream, 400, new { error = "Say who it's about and what happened." }, cors); return; }
            if (!ReportsByAccount.Allow(a.Key)) { await HttpServer.SendJson(stream, 429, new { error = "You've sent a lot of reports - try again in an hour." }, cors); return; }
            var id = Reports.Add(a.Name, about, reason);
            await HttpServer.SendJson(stream, 200, new { ok = true, id }, cors);
        }

        private async Task ChangePassword(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Not logged in." }, cors); return; }
            var body = req.JsonBody();
            if (!LoginByName.Allow(a.Key)) { await HttpServer.SendJson(stream, 429, new { error = "Too many tries. Wait a few minutes." }, cors); return; }
            if (Accounts.Login(a.Name, Str(body, "old"), req.From, out _, out _) is not string check)
            {
                await HttpServer.SendJson(stream, 401, new { error = "Your current password isn't right." }, cors);
                return;
            }
            Accounts.Logout(check);
            if (!Accounts.SetPassword(a.Name, Str(body, "new"), out var error))
            {
                await HttpServer.SendJson(stream, 400, new { error }, cors);
                return;
            }
            Log.Info(a.Name + " changed their password");
            var token = Accounts.Login(a.Name, Str(body, "new"), req.From, out _, out _);
            await HttpServer.SendJson(stream, 200, new { ok = true, token }, cors);
        }

        // ---------- the game files (Admins and Testers only) ----------

        private Account FilesAccount(Request req, out int status, out string error)
        {
            status = 0; error = null;
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { status = 401; error = "Log in first."; return null; }
            if (!Roles.GetsGameFiles(a.Role)) { status = 403; error = "Only Admin and Tester accounts can get the game files from the server. Pick your own Sims folder instead."; return null; }
            if (!Files.Ready) { status = 503; error = "The server doesn't have the game files set up (" + Files.Problem + ")."; return null; }
            return a;
        }

        private async Task FileList(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = FilesAccount(req, out var status, out var error);
            if (a == null) { await HttpServer.SendJson(stream, status, new { error }, cors); return; }
            lock (DownloadsAnnounced)
            {
                if (DownloadsAnnounced.Add(a.Key)) Log.Info(a.Name + " is getting the game files from the server (" + Files.Count + " files, " + (Files.TotalBytes / 1048576) + " MB)");
            }
            var list = Files.List();
            await HttpServer.SendJson(stream, 200, new
            {
                ok = true,
                count = list.Count,
                bytes = list.Sum(e => e.Size),
                files = list.Select(e => new { p = e.Path, s = e.Size }),
            }, cors);
        }

        private async Task FileGet(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = FilesAccount(req, out var status, out var error);
            if (a == null) { await HttpServer.SendJson(stream, status, new { error }, cors); return; }
            var entry = Files.Get(req.QueryValue("p"));
            if (entry == null) { await HttpServer.SendJson(stream, 404, new { error = "No such game file." }, cors); return; }
            long size;
            try { size = new FileInfo(entry.FullPath).Length; }
            catch { await HttpServer.SendJson(stream, 404, new { error = "That file has gone." }, cors); return; }
            await HttpServer.SendFile(stream, entry.FullPath, size, cors);
        }

        // ---------- the town ----------

        private async Task TownInfo(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Log in first." }, cors); return; }
            if (Config.Maintenance && a.Role != Roles.Admin) { await HttpServer.SendJson(stream, 503, new { error = MaintenanceText }, cors); return; }
            if (!Lots.TownReady) { await HttpServer.SendJson(stream, 503, new { error = "The server's town isn't set up yet (/town import)." }, cors); return; }
            await HttpServer.SendJson(stream, 200, new
            {
                ok = true,
                name = a.Name,
                stamp = Town.Stamp(),
                mine = Owners.Of(a.Key) is Owners.Owner o ? new { family = o.Family, house = o.House, name = o.Name } : null,
                owners = Owners.All().Values.Select(x => new { house = x.House, account = x.Account, name = x.Name }),
                lots = (Lots?.Running() ?? new List<Lots.Worker>()).Select(w => new { house = w.House, players = w.Players, open = w.Ready }),
            }, cors);
        }

        private readonly HashSet<string> TownAnnounced = new HashSet<string>();

        private async Task TownGet(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Log in first." }, cors); return; }
            if (Config.Maintenance && a.Role != Roles.Admin) { await HttpServer.SendJson(stream, 503, new { error = MaintenanceText }, cors); return; }
            if (!Lots.TownReady) { await HttpServer.SendJson(stream, 503, new { error = "The server's town isn't set up yet (/town import)." }, cors); return; }
            byte[] bundle;
            string stamp;
            try { bundle = Town.Bundle(out stamp); }
            catch (Exception e)
            {
                Log.Warn("town: couldn't pack the town for " + a.Name + ": " + e.Message);
                await HttpServer.SendJson(stream, 500, new { error = "The town couldn't be read right now - try again." }, cors);
                return;
            }
            lock (TownAnnounced)
            {
                if (TownAnnounced.Count > 5000) TownAnnounced.Clear(); // (one per account per town change: don't grow forever)
                if (TownAnnounced.Add(a.Key + stamp)) Log.Info(a.Name + " is getting the town (" + (bundle.Length / 1024) + " KB)");
            }
            await HttpServer.Send(stream, 200, "application/octet-stream", bundle, cors);
        }

        /// <summary>
        /// Move a family into the town (2C). The body is the family (its Sims' files and details)
        /// from the player's own neighbourhood; the lot is opened with it and the family moves in
        /// there, buying the lot with its own money. One family per account.
        /// </summary>
        private async Task TownMoveIn(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Log in first." }, cors); return; }
            if (Config.Maintenance && a.Role != Roles.Admin) { await HttpServer.SendJson(stream, 503, new { error = MaintenanceText }, cors); return; }
            if (!short.TryParse(req.QueryValue("house"), out var house)) { await HttpServer.SendJson(stream, 400, new { error = "Which house?" }, cors); return; }
            if (Owners.Of(a.Key) is Owners.Owner mine) { await HttpServer.SendJson(stream, 409, new { error = "Your family already lives in the town (house " + mine.House + ")." }, cors); return; }
            if (req.Body.Length < 16 || req.Body[0] != 'S' || req.Body[1] != 'R' || req.Body[2] != 'X' || req.Body[3] != 'F')
            { await HttpServer.SendJson(stream, 400, new { error = "That wasn't a family." }, cors); return; }
            // one move at a time per account and per house (two at once could give an account two families, or a house two)
            if (!BeginMove(a.Key, house)) { await HttpServer.SendJson(stream, 409, new { error = "A move for you or that house is already happening - try again in a minute." }, cors); return; }
            try
            {
                if (Owners.Of(a.Key) is Owners.Owner mineNow) { await HttpServer.SendJson(stream, 409, new { error = "Your family already lives in the town (house " + mineNow.House + ")." }, cors); return; }
                var fams = IffPeek.Families(Path.Combine(Lots.TownDir, "Neighborhood.iff"));
                if (fams != null && fams.Houses.Contains(house)) { await HttpServer.SendJson(stream, 409, new { error = "Someone already lives there." }, cors); return; }
                if (Lots.Running().Any(w => w.House == house)) { await HttpServer.SendJson(stream, 409, new { error = "Someone is on that lot right now - try again in a few minutes." }, cors); return; }

                var dir = Path.Combine(Program.DataDir, "Town", "incoming");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, a.Key + "-" + DateTime.UtcNow.Ticks + ".srxf");
                try
                {
                    File.WriteAllBytes(file, req.Body);
                    Log.Info(a.Name + " is moving their family into house " + house + " (" + (req.Body.Length / 1024) + " KB)");

                    var w = Lots.Get(house, out var error, new[] { "-movein" + file, "-owner" + a.Name });
                    if (w == null) { await HttpServer.SendJson(stream, 503, new { error }, cors); return; }
                    // wait for the family to move in (the lot loads, adds them, buys the house) and the lot to open
                    var until = DateTime.UtcNow.AddSeconds(90);
                    while (DateTime.UtcNow < until && !w.Finished && (w.MovedInFamily < 0 || !w.Ready) && w.Problem == null) await Task.Delay(250);
                    if (w.MovedInFamily < 0)
                    {
                        var why = w.Problem ?? (w.Finished ? "the lot closed" : "it took too long");
                        Log.Warn("moving " + a.Name + "'s family in failed: " + why);
                        await HttpServer.SendJson(stream, 500, new { error = "Your family couldn't move in: " + why.Replace("error ", "") }, cors);
                        return;
                    }
                    var name = new string((req.QueryValue("name") ?? "").Where(c => c >= 32).ToArray()).Trim();
                    if (name.Length > 40) name = name.Substring(0, 40);
                    if (!Owners.Set(a.Key, new Owners.Owner { Account = a.Name, Family = w.MovedInFamily, House = house, Name = name }))
                    {
                        await HttpServer.SendJson(stream, 500, new { error = "Your family moved in, but the server couldn't record that it's yours - ask the admin." }, cors);
                        return;
                    }
                    Log.Info(a.Name + "'s family (" + name + ") now lives in house " + house);
                    await HttpServer.SendJson(stream, 200, new { ok = true, family = w.MovedInFamily, house }, cors);
                }
                finally { try { File.Delete(file); } catch { } }
            }
            finally { EndMove(a.Key, house); }
        }

        /// <summary>Accounts and houses with a move in or out under way.</summary>
        private readonly HashSet<string> Moving = new HashSet<string>();

        private bool BeginMove(string accountKey, short house)
        {
            lock (Moving)
            {
                if (Moving.Contains("a:" + accountKey) || Moving.Contains("h:" + house)) return false;
                Moving.Add("a:" + accountKey);
                Moving.Add("h:" + house);
                return true;
            }
        }

        private void EndMove(string accountKey, short house)
        {
            lock (Moving) { Moving.Remove("a:" + accountKey); Moving.Remove("h:" + house); }
        }

        /// <summary>
        /// Your family leaves the online town for good (so you can make a new one): the lot is opened
        /// without anyone on it, the family moves out (the house goes up for sale) and you no longer own it.
        /// </summary>
        private async Task TownMoveOut(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Log in first." }, cors); return; }
            if (Config.Maintenance && a.Role != Roles.Admin) { await HttpServer.SendJson(stream, 503, new { error = MaintenanceText }, cors); return; }
            var mine = Owners.Of(a.Key);
            if (mine == null) { await HttpServer.SendJson(stream, 409, new { error = "You don't have a family in the town." }, cors); return; }
            var house = (short)mine.House;
            if (!BeginMove(a.Key, house)) { await HttpServer.SendJson(stream, 409, new { error = "A move for you or that house is already happening - try again in a minute." }, cors); return; }
            try
            {
                // the lot closes first if it's open (anyone there is sent back to the town)
                var open = Lots.Running().FirstOrDefault(x => x.House == house);
                if (open != null)
                {
                    Lots.Close(house);
                    await Lots.WaitExit(open, TimeSpan.FromSeconds(30));
                }
                Log.Info(a.Name + " is moving their family (" + mine.Name + ") out of house " + house);
                var w = Lots.Get(house, out var error, new[] { "-moveout" + mine.Family });
                if (w == null) { await HttpServer.SendJson(stream, 503, new { error }, cors); return; }
                await Lots.WaitExit(w, TimeSpan.FromSeconds(90));
                if (!w.MovedOut)
                {
                    var why = w.Problem ?? "the lot didn't finish";
                    Log.Warn("moving " + a.Name + "'s family out failed: " + why);
                    await HttpServer.SendJson(stream, 500, new { error = "Your family couldn't move out: " + why.Replace("error ", "") }, cors);
                    return;
                }
                if (!Owners.Set(a.Key, null))
                {
                    await HttpServer.SendJson(stream, 500, new { error = "Your family moved out, but the server couldn't record it - ask the admin." }, cors);
                    return;
                }
                Log.Info(a.Name + "'s family has left the town (house " + house + " is for sale)");
                await HttpServer.SendJson(stream, 200, new { ok = true }, cors);
            }
            finally { EndMove(a.Key, house); }
        }

        /// <summary>
        /// Start the online town over (the in-game admin cheat). Admins only: the same as typing
        /// /town fresh in the server panel. Every open lot is closed first.
        /// </summary>
        private async Task TownFresh(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Log in first." }, cors); return; }
            if (a.Role != Roles.Admin)
            {
                Log.Warn(a.Name + " (" + a.Role + ") tried to start the online town over - only admins can");
                await HttpServer.SendJson(stream, 403, new { error = "Only the server admin can start the online town over." }, cors);
                return;
            }
            Log.Info(a.Name + " is starting the online town over (from the game)");
            var result = await Task.Run(() => Lots.FreshTown(false));
            Log.Info(result);
            bool ok = result.StartsWith("fresh town set up");
            await HttpServer.SendJson(stream, ok ? 200 : 500, ok ? (object)new { ok = true, message = result } : new { error = result }, cors);
        }

        // ---------- online lots ----------

        /// <summary>
        /// Travel to a house in the town: its lot is opened (a lot worker started) if it isn't
        /// already, then the connection is passed to it unchanged, both ways.
        /// </summary>
        private async Task JoinTownLot(Request req, Stream stream)
        {
            if (Lots == null || !short.TryParse(req.Path.Substring(5).Trim('/'), out var house))
            {
                await Refuse(stream, 404, "No such lot.");
                return;
            }
            // only approved accounts can travel to the town's lots
            var who = Accounts.Check(req.QueryValue("t"));
            if (who == null)
            {
                Log.Info("online: someone without an approved login (from " + req.From + ") tried to go to house " + house);
                await Refuse(stream, 401, "Log in with an approved account first.");
                return;
            }
            if (Config.Maintenance && who.Role != Roles.Admin)
            {
                await Refuse(stream, 503, MaintenanceText);
                return;
            }
            var kicked = Kicks.MinutesLeft(who.Key);
            if (kicked > 0)
            {
                Log.Info("online: " + who.Name + " tried to come back after being kicked (" + kicked + " min left)");
                await Refuse(stream, 403, "An admin removed you from the online town. You can come back in " + kicked + " minute" + (kicked == 1 ? "" : "s") + ".");
                return;
            }
            Log.Info("online: " + who.Name + " is travelling to house " + house);
            // two tries: if the lot was just closing (everyone had left), wait for it to finish and open it again
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var w = Lots.Get(house, out var error);
                if (w == null)
                {
                    Log.Info("online: someone (from " + req.From + ") tried to go to house " + house + ", but " + error);
                    await Refuse(stream, 503, error);
                    return;
                }
                if (w.Closing)
                {
                    Log.Info("online: house " + house + " is closing - reopening it for a player (from " + req.From + ")");
                    await Lots.WaitExit(w, TimeSpan.FromSeconds(30));
                    continue;
                }
                if (!w.Ready)
                {
                    Log.Info("online: waiting for house " + house + " to open for a player (from " + req.From + ")");
                    if (!await Lots.WaitReady(w, TimeSpan.FromSeconds(80)))
                    {
                        if (w.Closing || w.Finished) continue;
                        Log.Warn("online: house " + house + " didn't open in time" + (w.Problem != null ? " (" + w.Problem + ")" : ""));
                        await Refuse(stream, 503, "The lot couldn't be opened. " + (w.Problem ?? ""));
                        return;
                    }
                }
                bool done;
                lock (OnLots) OnLots[who.Key] = (OnLots.TryGetValue(who.Key, out var c) ? c : 0) + 1;
                try { done = await Relay(req, stream, w.Port, "house " + house, WithAccount(req.Raw, who.Name)); }
                finally { lock (OnLots) { OnLots[who.Key]--; if (OnLots[who.Key] <= 0) OnLots.Remove(who.Key); } }
                if (done) return;
                // it was open but didn't answer (closing at that very moment): start it again
                Log.Info("online: house " + house + " didn't answer - reopening it");
                await Lots.WaitExit(w, TimeSpan.FromSeconds(30));
            }
            Log.Warn("online: couldn't get a player (from " + req.From + ") onto house " + house);
            await Refuse(stream, 503, "The lot couldn't be opened right now - try again.");
        }

        /// <summary>
        /// Turns a player away from a lot. The reason also goes in a header, because a game that's
        /// connecting to a lot can read headers but not the page itself - so it can say why.
        /// </summary>
        private static Task Refuse(Stream stream, int status, string reason)
        {
            var oneLine = new string((reason ?? "").Where(c => c >= 32 && c < 127).ToArray());
            return HttpServer.Send(stream, status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(reason ?? ""),
                new Dictionary<string, string> { ["X-Redux-Error"] = oneLine });
        }

        private static bool IsWebSocket(Request req) =>
            req.Method == "GET" && (req.Header("Upgrade") ?? "").Equals("websocket", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Until lots run on this server (the next step), a lot hosted from the game on this PC is
        /// reached through here: the connection is handed to the game unchanged, both ways.
        /// </summary>
        private Task RelayLot(Request req, Stream stream) => Relay(req, stream, Config.GameHostPort, null, null);

        /// <summary>
        /// The request passed on to a town lot, with a line saying which account this is (any such
        /// line the player's own game sent is removed first, so nobody can pretend to be someone else).
        /// </summary>
        private static byte[] WithAccount(byte[] raw, string account)
        {
            var text = Encoding.ASCII.GetString(raw);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end < 0) return raw;
            // the lot reads header names trimmed and the last one wins, so " X-Redux-Account:" counts too:
            // drop any line whose trimmed name is ours, then add the real one last
            static bool IsAccountLine(string l)
            {
                var c = l.IndexOf(':');
                return c > 0 && l.Substring(0, c).Trim().Equals("X-Redux-Account", StringComparison.OrdinalIgnoreCase);
            }
            var lines = text.Substring(0, end).Split("\r\n").Where(l => !IsAccountLine(l)).ToList();
            var safe = new string((account ?? "").Where(c => c >= 32 && c < 127).ToArray());
            lines.Add("X-Redux-Account: " + safe);
            var head = Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n\r\n");
            var rest = raw.AsSpan(Encoding.ASCII.GetByteCount(text.Substring(0, end + 4))).ToArray();
            return head.Concat(rest).ToArray();
        }

        /// <summary>Passes the connection through to a lot on this PC. False if nothing answered there (and nothing was sent yet for a town lot).</summary>
        private async Task<bool> Relay(Request req, Stream stream, int port, string what, byte[] raw)
        {
            using var game = new TcpClient();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await game.ConnectAsync("127.0.0.1", port, cts.Token);
            }
            catch
            {
                if (what != null) return false; // the caller decides what to do
                Log.Info("online: someone tried to join (from " + req.From + "), but no lot is being hosted - open a lot in the Windows game and choose Host Online first");
                await HttpServer.Send(stream, 503, "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Nobody is hosting a lot right now."));
                return true;
            }
            if (what == null) Interlocked.Increment(ref LotConnections);
            if (what == null) Log.Info("online: a player joined the hosted lot (from " + req.From + ")");
            try
            {
                game.NoDelay = true;
                var gs = game.GetStream();
                await gs.WriteAsync(raw ?? req.Raw);
                var a = stream.CopyToAsync(gs);
                var b = gs.CopyToAsync(stream);
                await Task.WhenAny(a, b);
            }
            catch { }
            finally
            {
                if (what == null)
                {
                    Interlocked.Decrement(ref LotConnections);
                    Log.Info("online: a player left the hosted lot");
                }
            }
            return true;
        }
    }
}
