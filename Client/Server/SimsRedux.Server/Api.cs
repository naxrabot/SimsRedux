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

        /// <summary>Online-lot connections being passed through right now.</summary>
        public int LotConnections;
        private readonly HashSet<string> DownloadsAnnounced = new HashSet<string>();

        public Api(ServerConfig config, Accounts accounts, GameFiles files)
        {
            Config = config; Accounts = accounts; Files = files;
        }

        public void Tidy()
        {
            LoginByAddress.Tidy(); LoginByName.Tidy(); RegisterByAddress.Tidy(); Everything.Tidy();
        }

        public object Health()
        {
            var proc = Process.GetCurrentProcess();
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
                uptime = (int)(DateTime.UtcNow - Started).TotalSeconds,
                rssMB = (int)(proc.WorkingSet64 / 1048576),
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
                    await HttpServer.SendJson(stream, 200, Health(), cors);
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
                case "/api/register": if (req.Method == "POST") { await Register(req, stream, cors); return; } break;
                case "/api/login": if (req.Method == "POST") { await Login(req, stream, cors); return; } break;
                case "/api/logout": if (req.Method == "POST") { Accounts.Logout(req.BearerToken()); await HttpServer.SendJson(stream, 200, new { ok = true }, cors); return; } break;
                case "/api/me": if (req.Method == "GET") { await Me(req, stream, cors); return; } break;
                case "/api/password": if (req.Method == "POST") { await ChangePassword(req, stream, cors); return; } break;
                case "/api/files": if (req.Method == "GET") { await FileList(req, stream, cors); return; } break;
                case "/api/file": if (req.Method == "GET") { await FileGet(req, stream, cors); return; } break;
                case "/api/town/info": if (req.Method == "GET") { await TownInfo(req, stream, cors); return; } break;
                case "/api/town": if (req.Method == "GET") { await TownGet(req, stream, cors); return; } break;
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
            // new accounts wait for the server owner to approve them in the panel
            var a = Accounts.Create(Str(body, "name"), Str(body, "password"), Roles.Player, Str(body, "email"), out var error, pending: true);
            if (a == null)
            {
                await HttpServer.SendJson(stream, 400, new { error }, cors);
                return;
            }
            Log.Warn("new account " + a.Name + " is waiting for your approval (from " + req.From + ") - approve it in Accounts, or type  /approve " + a.Name);
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
            Log.Info(a.Name + " logged in (" + a.Role + ", from " + req.From + ")");
            await HttpServer.SendJson(stream, 200, new { ok = true, token, name = a.Name, role = a.Role, files = Roles.GetsGameFiles(a.Role) && Files.Ready, motd = Config.Motd }, cors);
        }

        private async Task Me(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Not logged in." }, cors); return; }
            await HttpServer.SendJson(stream, 200, new { ok = true, name = a.Name, role = a.Role, files = Roles.GetsGameFiles(a.Role) && Files.Ready, motd = Config.Motd }, cors);
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
            if (!Lots.TownReady) { await HttpServer.SendJson(stream, 503, new { error = "The server's town isn't set up yet (/town import)." }, cors); return; }
            await HttpServer.SendJson(stream, 200, new
            {
                ok = true,
                name = a.Name,
                stamp = Town.Stamp(),
                lots = (Lots?.Running() ?? new List<Lots.Worker>()).Select(w => new { house = w.House, players = w.Players, open = w.Ready }),
            }, cors);
        }

        private readonly HashSet<string> TownAnnounced = new HashSet<string>();

        private async Task TownGet(Request req, Stream stream, Dictionary<string, string> cors)
        {
            var a = Accounts.Check(req.BearerToken());
            if (a == null) { await HttpServer.SendJson(stream, 401, new { error = "Log in first." }, cors); return; }
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
                if (TownAnnounced.Add(a.Key + stamp)) Log.Info(a.Name + " is getting the town (" + (bundle.Length / 1024) + " KB)");
            }
            await HttpServer.Send(stream, 200, "application/octet-stream", bundle, cors);
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
                await HttpServer.Send(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("No such lot."));
                return;
            }
            // only approved accounts can travel to the town's lots
            var who = Accounts.Check(req.QueryValue("t"));
            if (who == null)
            {
                Log.Info("online: someone without an approved login (from " + req.From + ") tried to go to house " + house);
                await HttpServer.Send(stream, 401, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Log in with an approved account first."));
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
                    await HttpServer.Send(stream, 503, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(error));
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
                        await HttpServer.Send(stream, 503, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("The lot couldn't be opened. " + (w.Problem ?? "")));
                        return;
                    }
                }
                if (await Relay(req, stream, w.Port, "house " + house)) return;
                // it was open but didn't answer (closing at that very moment): start it again
                Log.Info("online: house " + house + " didn't answer - reopening it");
                await Lots.WaitExit(w, TimeSpan.FromSeconds(30));
            }
            Log.Warn("online: couldn't get a player (from " + req.From + ") onto house " + house);
            await HttpServer.Send(stream, 503, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("The lot couldn't be opened right now - try again."));
        }

        private static bool IsWebSocket(Request req) =>
            req.Method == "GET" && (req.Header("Upgrade") ?? "").Equals("websocket", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Until lots run on this server (the next step), a lot hosted from the game on this PC is
        /// reached through here: the connection is handed to the game unchanged, both ways.
        /// </summary>
        private Task RelayLot(Request req, Stream stream) => Relay(req, stream, Config.GameHostPort, null);

        /// <summary>Passes the connection through to a lot on this PC. False if nothing answered there (and nothing was sent yet for a town lot).</summary>
        private async Task<bool> Relay(Request req, Stream stream, int port, string what)
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
                await gs.WriteAsync(req.Raw);
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
