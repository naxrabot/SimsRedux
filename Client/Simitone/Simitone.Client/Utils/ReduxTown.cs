using FSO.Common;
using FSO.Common.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: the online town on the dedicated server (drop 2B).
    ///
    /// Logging in, downloading the town's neighbourhood (only again when it has changed), and the
    /// address of a house's lot. While the town is showing (Active), the neighbourhood screen is
    /// the server's town and clicking a house travels there with the Sim picked when entering.
    ///
    /// The browser version uses the account the website logged in with.
    /// </summary>
    public static class ReduxTown
    {
        public const string DefaultServer = "https://tsr-lot.mbrew.studio";

        /// <summary>The town is on the neighbourhood screen right now (not the player's own neighbourhood).</summary>
        public static bool Active;
        /// <summary>"Here's me and my Sim", made from the player's own neighbourhood before the town replaced it.</summary>
        public static byte[] JoinPacket;
        public static uint TravelSim;

        // ---- 2C: your family in the town ----
        /// <summary>The house your family lives in, in the town (0 = you haven't moved in yet).</summary>
        public static int MyHouse;
        /// <summary>Your family's number in the town (-1 = none).</summary>
        public static int MyFamily = -1;
        public static string MyFamilyName = "";
        /// <summary>Who lives where: house number -> account name (the other players' families).</summary>
        public static Dictionary<int, string> HouseOwners = new Dictionary<int, string>();
        /// <summary>Your own single-player families, packed up when you went to the town (to move one in).</summary>
        public static List<ReduxFamilyPackage> MyFamilies = new List<ReduxFamilyPackage>();

        public static string Folder { get { return Path.Combine(FSOEnvironment.UserDir, "OnlineTown/"); } }

        private static readonly HttpClient Http = new HttpClient() { Timeout = TimeSpan.FromMinutes(5) };

        // ---- the account ----

        public static string Server
        {
            get
            {
                var web = ReduxPlatform.AccountServer?.Invoke();
                if (!string.IsNullOrEmpty(web)) return web.TrimEnd('/');
                return ReduxSettings.GetString("townserver", DefaultServer).TrimEnd('/');
            }
        }

        public static string Token
        {
            get
            {
                var own = ReduxSettings.GetString("towntoken", "");
                if (own.Length > 0) return own;
                return ReduxPlatform.AccountToken?.Invoke() ?? "";
            }
        }

        public static string Name
        {
            get
            {
                if (ReduxSettings.GetString("towntoken", "").Length > 0) return ReduxSettings.GetString("townname", "");
                return ReduxPlatform.AccountName?.Invoke() ?? "";
            }
        }

        public static bool LoggedIn { get { return Token.Length > 0; } }

        /// <summary>Logs in with the server. Calls back on the game thread with null, or what went wrong.</summary>
        public static void Login(string name, string password, Action<string> done)
        {
            Run(async () =>
            {
                var body = JsonSerializer.Serialize(new Dictionary<string, string> { { "name", name }, { "password", password } });
                using (var resp = await Http.PostAsync(Server + "/api/login", new StringContent(body, Encoding.UTF8, "application/json")))
                {
                    var text = await resp.Content.ReadAsStringAsync();
                    var json = Parse(text);
                    if (!resp.IsSuccessStatusCode) return Error(json, "The server said no (" + (int)resp.StatusCode + ").");
                    ReduxSettings.Set("towntoken", Get(json, "token") ?? "");
                    ReduxSettings.Set("townname", Get(json, "name") ?? name);
                    return null;
                }
            }, done);
        }

        public static void Logout()
        {
            var token = ReduxSettings.GetString("towntoken", "");
            ReduxSettings.Set("towntoken", "");
            if (token.Length == 0) return;
            Run(async () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, Server + "/api/logout") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (await Http.SendAsync(req)) { }
                return null;
            }, null);
        }

        /// <summary>Admin only (the server checks): start the online town over for everyone.</summary>
        public static void StartTownOver(Action<string> done)
        {
            Run(async () =>
            {
                using (var resp = await Send(HttpMethod.Post, "/api/town/fresh"))
                {
                    var json = Parse(await resp.Content.ReadAsStringAsync());
                    if (!resp.IsSuccessStatusCode) return Error(json, "The server said no (" + (int)resp.StatusCode + ").");
                    ReduxSettings.Set("townstamp", ""); // download it again next time
                    return null;
                }
            }, done);
        }

        private static void ReadOwnership(JsonElement? json)
        {
            MyHouse = 0; MyFamily = -1; MyFamilyName = "";
            var owners = new Dictionary<int, string>();
            try
            {
                if (json != null && json.Value.TryGetProperty("mine", out var mine) && mine.ValueKind == JsonValueKind.Object)
                {
                    MyHouse = mine.GetProperty("house").GetInt32();
                    MyFamily = mine.GetProperty("family").GetInt32();
                    MyFamilyName = mine.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                }
                if (json != null && json.Value.TryGetProperty("owners", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var o in list.EnumerateArray())
                        owners[o.GetProperty("house").GetInt32()] = o.TryGetProperty("account", out var a) ? (a.GetString() ?? "") : "";
            }
            catch (Exception e) { ReduxOnlineInfo.Log("[town] couldn't read who lives where: " + e.Message); }
            HouseOwners = owners;
        }

        /// <summary>
        /// 2C: moves one of your families into a house in the town (the server opens the lot and they
        /// move in, paying for it). done(null) on success.
        /// </summary>
        public static void MoveIn(int house, ReduxFamilyPackage family, Action<string> done)
        {
            var body = family.ToBytes();
            Run(async () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, Server + "/api/town/movein?house=" + house + "&name=" + Uri.EscapeDataString(family.Name ?? ""));
                var token = Token;
                if (token.Length > 0) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Content = new ByteArrayContent(body);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using (var resp = await Http.SendAsync(req))
                {
                    var json = Parse(await resp.Content.ReadAsStringAsync());
                    if (!resp.IsSuccessStatusCode) return Error(json, "The server said no (" + (int)resp.StatusCode + ").");
                    MyHouse = house;
                    MyFamilyName = family.Name;
                    if (json != null && json.Value.TryGetProperty("family", out var f)) MyFamily = f.GetInt32();
                    ReduxSettings.Set("townstamp", ""); // the town changed
                    return null;
                }
            }, done);
        }

        /// <summary>Your family leaves the online town for good (the house goes up for sale).</summary>
        public static void MoveOut(Action<string> done)
        {
            Run(async () =>
            {
                using (var resp = await Send(HttpMethod.Post, "/api/town/moveout"))
                {
                    var json = Parse(await resp.Content.ReadAsStringAsync());
                    if (!resp.IsSuccessStatusCode) return Error(json, "The server said no (" + (int)resp.StatusCode + ").");
                    MyHouse = 0; MyFamily = -1; MyFamilyName = "";
                    ReduxSettings.Set("townstamp", "");
                    return null;
                }
            }, done);
        }

        // ---- the town's files ----

        /// <summary>
        /// Makes sure the newest town is in the OnlineTown folder (downloads only if it changed).
        /// progress(text, 0..1) and done(null or what went wrong) are called on the game thread.
        /// </summary>
        public static void Sync(Action<string, float> progress, Action<string> done)
        {
            Run(async () =>
            {
                Report(progress, "Checking the town...", 0.1f);
                var info = await Send(HttpMethod.Get, "/api/town/info");
                var infoText = await info.Content.ReadAsStringAsync();
                var json = Parse(infoText);
                if ((int)info.StatusCode == 401)
                {
                    ReduxSettings.Set("towntoken", "");
                    return "You've been logged out of the server. Log in again from the Online window.";
                }
                if (!info.IsSuccessStatusCode) return Error(json, "The server couldn't send the town (" + (int)info.StatusCode + ").");
                var stamp = Get(json, "stamp") ?? "";
                ReadOwnership(json);

                bool have = stamp.Length > 0 && stamp == ReduxSettings.GetString("townstamp", "")
                    && FSOFile.Exists(Path.Combine(Folder, "Neighborhood.iff"));
                if (have) { Report(progress, "The town is up to date.", 1f); return null; }

                Report(progress, "Downloading the town...", 0.2f);
                byte[] bundle;
                using (var resp = await Send(HttpMethod.Get, "/api/town"))
                {
                    if (!resp.IsSuccessStatusCode) return Error(Parse(await resp.Content.ReadAsStringAsync()), "The server couldn't send the town (" + (int)resp.StatusCode + ").");
                    bundle = await resp.Content.ReadAsByteArrayAsync();
                }

                Report(progress, "Unpacking the town...", 0.8f);
                var newStamp = Unpack(bundle);
                ReduxSettings.Set("townstamp", newStamp);
                ReduxOnlineInfo.Log("[town] Downloaded the town (" + (bundle.Length / 1024) + " KB, " + newStamp + ")");
                Report(progress, "Done.", 1f);
                return null;
            }, done);
        }

        /// <summary>Writes the town's files into the OnlineTown folder (replacing what was there).</summary>
        private static string Unpack(byte[] bundle)
        {
            using (var r = new BinaryReader(new MemoryStream(bundle), Encoding.UTF8))
            {
                var magic = Encoding.ASCII.GetString(r.ReadBytes(4));
                if (magic != "SRXT") throw new Exception("The town download wasn't a town.");
                var version = r.ReadInt32();
                if (version != 1) throw new Exception("The server's town is a newer kind - update the game.");
                var stamp = r.ReadString();
                var count = r.ReadInt32();

                var folder = Folder;
                try { if (FSODirectory.Exists(folder)) FSODirectory.Delete(folder, true); } catch { }
                FSODirectory.CreateDirectory(folder);
                FSODirectory.CreateDirectory(Path.Combine(folder, "Houses/"));
                FSODirectory.CreateDirectory(Path.Combine(folder, "Characters/"));
                for (int i = 0; i < count; i++)
                {
                    var name = r.ReadString();
                    var len = r.ReadInt32();
                    if (len < 0 || len > 64 * 1024 * 1024) throw new Exception("The town download is damaged.");
                    var bytes = r.ReadBytes(len);
                    if (!ReduxOnlineInfo.SafeRelativePath(name)) continue;
                    var path = Path.Combine(folder, name);
                    FSODirectory.CreateDirectory(Path.GetDirectoryName(path));
                    FSOFile.WriteAllBytes(path, bytes);
                }
                return stamp;
            }
        }

        // ---- travelling ----

        /// <summary>The lot of a house in the town, e.g. wss://tsr-lot.mbrew.studio/lot/12?t=...</summary>
        public static string LotAddress(int house)
        {
            var server = Server;
            string ws;
            if (server.StartsWith("https://")) ws = "wss://" + server.Substring(8);
            else if (server.StartsWith("http://")) ws = "ws://" + server.Substring(7);
            else ws = "wss://" + server;
            return ws + "/lot/" + house + "?t=" + Uri.EscapeDataString(Token);
        }

        // ---- helpers ----

        private static async Task<HttpResponseMessage> Send(HttpMethod method, string path)
        {
            var req = new HttpRequestMessage(method, Server + path);
            var token = Token;
            if (token.Length > 0) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await Http.SendAsync(req);
        }

        private static void Run(Func<Task<string>> work, Action<string> done)
        {
            Task.Run(async () =>
            {
                string result;
                try { result = await work(); }
                catch (HttpRequestException e) { result = "Couldn't reach the server at " + Server + ".\n\n" + e.Message; }
                catch (TaskCanceledException) { result = "The server took too long to answer."; }
                catch (Exception e) { result = e.Message; }
                if (result != null) ReduxOnlineInfo.Log("[town] " + result);
                if (done != null) FSO.Common.Utils.GameThread.NextUpdate((s) => done(result));
            });
        }

        private static void Report(Action<string, float> progress, string text, float p)
        {
            if (progress != null) FSO.Common.Utils.GameThread.NextUpdate((s) => progress(text, p));
        }

        private static JsonElement? Parse(string text)
        {
            try { return JsonDocument.Parse(text).RootElement; } catch { return null; }
        }

        private static string Get(JsonElement? json, string name)
        {
            if (json == null || json.Value.ValueKind != JsonValueKind.Object) return null;
            return json.Value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private static string Error(JsonElement? json, string fallback)
        {
            return Get(json, "error") ?? fallback;
        }
    }
}
