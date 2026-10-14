using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SimsRedux.Server
{
    public static class Roles
    {
        public const string Admin = "admin", Tester = "tester", Player = "player";
        public static readonly string[] All = { Admin, Tester, Player };
        public static bool Valid(string r) => All.Contains(r);
        /// <summary>Admins and Testers may download the Sims 1 game files from the server.</summary>
        public static bool GetsGameFiles(string r) => r == Admin || r == Tester;
    }

    public class Account
    {
        public string Name { get; set; }
        public string Role { get; set; } = Roles.Player;
        public string Email { get; set; } = "";
        public string Salt { get; set; }
        public string Hash { get; set; }
        public int Iterations { get; set; }
        public DateTime Created { get; set; }
        public DateTime LastLogin { get; set; }
        public bool Suspended { get; set; }
        /// <summary>Made on the website and waiting for the server owner to approve it (can't log in until then).</summary>
        public bool Pending { get; set; }

        [JsonIgnore] public string Key => Accounts.KeyOf(Name);
    }

    public class Session
    {
        /// <summary>SHA-256 of the token: the token itself is never stored, so this file can't be used to log in.</summary>
        public string TokenHash { get; set; }
        public string Account { get; set; }
        public DateTime Expires { get; set; }
        public DateTime LastSeen { get; set; }
        public string From { get; set; }
    }

    /// <summary>
    /// Player accounts (accounts.json) and who is logged in (sessions.json). Passwords are stored
    /// as salted PBKDF2 hashes; login tokens only as hashes.
    /// </summary>
    public class Accounts
    {
        public const int MinPassword = 6;
        private const int HashIterations = 120_000;
        private static readonly TimeSpan SessionLife = TimeSpan.FromDays(30);
        private static readonly Regex NameRule = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 _\-]{1,18}[A-Za-z0-9]$");

        private readonly object Lock = new object();
        private readonly string AccountsPath, SessionsPath;
        private Dictionary<string, Account> ByKey = new Dictionary<string, Account>();
        private Dictionary<string, Session> Sessions = new Dictionary<string, Session>();
        private bool SessionsDirty;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public Accounts(string dataDir)
        {
            AccountsPath = Path.Combine(dataDir, "accounts.json");
            SessionsPath = Path.Combine(dataDir, "sessions.json");
            Load();
        }

        public static string KeyOf(string name) => (name ?? "").Trim().ToLowerInvariant();

        public int Count { get { lock (Lock) return ByKey.Count; } }

        public List<Account> All() { lock (Lock) return ByKey.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(); }

        public Account Find(string name)
        {
            lock (Lock) return ByKey.TryGetValue(KeyOf(name), out var a) ? a : null;
        }

        /// <summary>Accounts that used the server in the last few minutes.</summary>
        public List<string> RecentlyActive(TimeSpan within)
        {
            var since = DateTime.UtcNow - within;
            lock (Lock) return Sessions.Values.Where(s => s.LastSeen >= since).Select(s => s.Account).Distinct()
                    .Select(k => ByKey.TryGetValue(k, out var a) ? a.Name : k).OrderBy(n => n).ToList();
        }

        // ---------- making and changing accounts ----------

        public static string CheckName(string name)
        {
            name = (name ?? "").Trim();
            if (!NameRule.IsMatch(name)) return "Names are 3 to 20 letters or numbers (spaces, - and _ in the middle are fine).";
            if (name.Contains("  ")) return "Names can't have two spaces in a row.";
            var key = KeyOf(name);
            if (key == "traveler" || key == "the traveler" || key == "server" || key == "admin" || key == "system")
                return "That name is reserved.";
            return null;
        }

        public static string CheckPassword(string pw)
        {
            if ((pw ?? "").Length < MinPassword) return "Passwords need at least " + MinPassword + " characters.";
            if (pw.Length > 128) return "That password is too long.";
            return null;
        }

        public Account Create(string name, string password, string role, string email, out string error, bool pending = false)
        {
            error = CheckName(name) ?? CheckPassword(password);
            if (error != null) return null;
            if (!Roles.Valid(role)) { error = "Unknown role " + role; return null; }
            name = name.Trim();
            lock (Lock)
            {
                if (ByKey.ContainsKey(KeyOf(name))) { error = "That name is taken."; return null; }
                var a = new Account { Name = name, Role = role, Email = (email ?? "").Trim(), Created = DateTime.UtcNow, Pending = pending };
                SetPasswordOn(a, password);
                ByKey[a.Key] = a;
                SaveAccounts();
                return a;
            }
        }

        public bool SetPassword(string name, string password, out string error)
        {
            error = CheckPassword(password);
            if (error != null) return false;
            lock (Lock)
            {
                var a = Find(name);
                if (a == null) { error = "No account called " + name; return false; }
                SetPasswordOn(a, password);
                // a new password logs out everywhere else
                foreach (var k in Sessions.Where(s => s.Value.Account == a.Key).Select(s => s.Key).ToList()) Sessions.Remove(k);
                SessionsDirty = true;
                SaveAccounts();
                return true;
            }
        }

        public bool SetRole(string name, string role, out string error)
        {
            error = null;
            if (!Roles.Valid(role)) { error = "Roles are: " + string.Join(", ", Roles.All); return false; }
            lock (Lock)
            {
                var a = Find(name);
                if (a == null) { error = "No account called " + name; return false; }
                a.Role = role;
                SaveAccounts();
                return true;
            }
        }

        public bool SetSuspended(string name, bool suspended, out string error)
        {
            error = null;
            lock (Lock)
            {
                var a = Find(name);
                if (a == null) { error = "No account called " + name; return false; }
                a.Suspended = suspended;
                if (suspended) LogoutAll(a.Key);
                SaveAccounts();
                return true;
            }
        }

        /// <summary>Approve an account made on the website (or put it back to waiting).</summary>
        public bool SetPending(string name, bool pending, out string error)
        {
            error = null;
            lock (Lock)
            {
                var a = Find(name);
                if (a == null) { error = "No account called " + name; return false; }
                a.Pending = pending;
                if (pending) LogoutAll(a.Key);
                SaveAccounts();
                return true;
            }
        }

        public List<Account> Waiting() { lock (Lock) return ByKey.Values.Where(a => a.Pending).OrderBy(a => a.Created).ToList(); }

        public bool Delete(string name, out string error)
        {
            error = null;
            lock (Lock)
            {
                var a = Find(name);
                if (a == null) { error = "No account called " + name; return false; }
                ByKey.Remove(a.Key);
                LogoutAll(a.Key);
                SaveAccounts();
                return true;
            }
        }

        private static readonly Account Dummy = MakeDummy();
        private static Account MakeDummy() { var a = new Account { Name = "dummy" }; SetPasswordOn(a, Guid.NewGuid().ToString()); return a; }

        private static void SetPasswordOn(Account a, string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            a.Salt = Convert.ToBase64String(salt);
            a.Iterations = HashIterations;
            a.Hash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, HashIterations, HashAlgorithmName.SHA256, 32));
        }

        private static bool PasswordMatches(Account a, string password)
        {
            try
            {
                var got = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? ""), Convert.FromBase64String(a.Salt), a.Iterations, HashAlgorithmName.SHA256, 32);
                return CryptographicOperations.FixedTimeEquals(got, Convert.FromBase64String(a.Hash));
            }
            catch { return false; }
        }

        // ---------- logging in ----------

        /// <summary>Checks the password and starts a session. Returns the token to give the player.</summary>
        public string Login(string name, string password, string from, out Account account, out string error)
        {
            error = null;
            account = Find(name);
            // the same answer, and the same time taken, whether the name or the password is wrong
            if (account == null) { PasswordMatches(Dummy, password); error = "Wrong name or password."; return null; }
            if (!PasswordMatches(account, password)) { account = null; error = "Wrong name or password."; return null; }
            if (account.Suspended) { error = "This account is suspended."; account = null; return null; }
            if (account.Pending) { error = "Please wait for admin approval."; account = null; return null; }
            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            lock (Lock)
            {
                Sessions[HashToken(token)] = new Session
                {
                    TokenHash = HashToken(token),
                    Account = account.Key,
                    Expires = DateTime.UtcNow + SessionLife,
                    LastSeen = DateTime.UtcNow,
                    From = from,
                };
                account.LastLogin = DateTime.UtcNow;
                SessionsDirty = true;
                SaveAccounts();
            }
            return token;
        }

        /// <summary>The account a login token belongs to, or null if it's unknown or expired.</summary>
        public Account Check(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 100) return null;
            lock (Lock)
            {
                var h = HashToken(token);
                if (!Sessions.TryGetValue(h, out var s)) return null;
                var now = DateTime.UtcNow;
                if (s.Expires < now) { Sessions.Remove(h); SessionsDirty = true; return null; }
                if (!ByKey.TryGetValue(s.Account, out var a) || a.Suspended || a.Pending) return null;
                // using it keeps it alive (saved every so often, not on every request)
                if ((now - s.LastSeen).TotalMinutes >= 1)
                {
                    s.LastSeen = now;
                    s.Expires = now + SessionLife;
                    SessionsDirty = true;
                }
                return a;
            }
        }

        public void Logout(string token)
        {
            lock (Lock) { if (Sessions.Remove(HashToken(token ?? ""))) SessionsDirty = true; }
        }

        private void LogoutAll(string key)
        {
            foreach (var k in Sessions.Where(s => s.Value.Account == key).Select(s => s.Key).ToList()) Sessions.Remove(k);
            SessionsDirty = true;
        }

        private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // ---------- saving ----------

        private void Load()
        {
            try
            {
                if (File.Exists(AccountsPath))
                {
                    var list = JsonSerializer.Deserialize<List<Account>>(File.ReadAllText(AccountsPath), Json) ?? new List<Account>();
                    ByKey = list.Where(a => !string.IsNullOrWhiteSpace(a.Name)).ToDictionary(a => a.Key);
                }
            }
            catch (Exception e)
            {
                // never start with an empty list over a file we couldn't read: that would lose every account on the next save
                throw new Exception("accounts.json could not be read (" + e.Message + "). Fix or restore it from backups/ before starting.");
            }
            try
            {
                if (File.Exists(SessionsPath))
                {
                    var list = JsonSerializer.Deserialize<List<Session>>(File.ReadAllText(SessionsPath), Json) ?? new List<Session>();
                    var now = DateTime.UtcNow;
                    Sessions = list.Where(s => s.Expires > now && s.TokenHash != null).ToDictionary(s => s.TokenHash);
                }
            }
            catch { Sessions = new Dictionary<string, Session>(); } // only means people log in again
        }

        private void SaveAccounts()
        {
            try { DataFile.WriteAllTextSafely(AccountsPath, JsonSerializer.Serialize(ByKey.Values.ToList(), Json)); }
            catch (Exception e) { Log.Warn("could not save accounts.json: " + e.Message); }
        }

        /// <summary>Saves sessions if they changed. Called every so often and when stopping.</summary>
        public void SaveSessions(bool force = false)
        {
            lock (Lock)
            {
                if (!SessionsDirty && !force) return;
                try
                {
                    var now = DateTime.UtcNow;
                    DataFile.WriteAllTextSafely(SessionsPath, JsonSerializer.Serialize(Sessions.Values.Where(s => s.Expires > now).ToList(), Json));
                    SessionsDirty = false;
                }
                catch (Exception e) { Log.Warn("could not save sessions.json: " + e.Message); }
            }
        }

        public void SaveAll()
        {
            lock (Lock) SaveAccounts();
            SaveSessions(true);
        }
    }
}
