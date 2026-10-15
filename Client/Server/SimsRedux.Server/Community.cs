using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimsRedux.Server
{
    /// <summary>Small JSON files in the server's data folder (written safely: a new file, then swapped in).</summary>
    static class Store
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };

        public static T Load<T>(string file) where T : new()
        {
            var path = Path.Combine(Program.DataDir, file);
            try
            {
                if (File.Exists(path)) return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? new T();
            }
            catch (Exception e)
            {
                Log.Warn(file + " could not be read (" + e.Message + ") - starting it fresh. The broken file is kept as " + file + ".bad");
                try { File.Copy(path, path + ".bad", true); } catch { }
            }
            return new T();
        }

        public static void Save<T>(string file, T data)
        {
            try
            {
                var path = Path.Combine(Program.DataDir, file);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
                File.Move(tmp, path, true);
            }
            catch (Exception e) { Log.Warn(file + " couldn't be saved: " + e.Message); }
        }

        public static string Ago(DateTime utc)
        {
            var t = DateTime.UtcNow - utc;
            if (t.TotalMinutes < 1) return "just now";
            if (t.TotalHours < 1) return (int)t.TotalMinutes + "m ago";
            if (t.TotalDays < 1) return (int)t.TotalHours + "h ago";
            return (int)t.TotalDays + "d ago";
        }
    }

    // =====================================================================================
    //  Announcements: /say, events starting and ending, level-ups. Players' games ask for new
    //  ones every 15 seconds while they're in the online town (GET /api/notices).
    // =====================================================================================
    public static class Notices
    {
        public class Notice
        {
            public int Id;
            public DateTime Time;
            public string Title = "";
            public string Text = "";
            /// <summary>Only for this account (its key), or null for everyone.</summary>
            public string To;
        }

        private static readonly object Lock = new object();
        private static readonly List<Notice> List = new List<Notice>();
        private static int NextId = 1;

        public static void Add(string title, string text, string toAccountKey = null)
        {
            lock (Lock)
            {
                List.Add(new Notice { Id = NextId++, Time = DateTime.UtcNow, Title = title, Text = text, To = toAccountKey });
                if (List.Count > 100) List.RemoveAt(0);
            }
        }

        /// <summary>The newest id, and the ones after `after` meant for this account (after &lt; 0: just the newest id).</summary>
        public static (int last, List<Notice> notices) Since(int after, string accountKey)
        {
            lock (Lock)
            {
                var last = NextId - 1;
                if (after < 0) return (last, new List<Notice>());
                return (last, List.Where(n => n.Id > after && (n.To == null || n.To == accountKey)).ToList());
            }
        }
    }

    // =====================================================================================
    //  Server events: started now or scheduled from the panel. XP events raise the XP players
    //  earn; the others are announced to everyone and shown on the website's launcher.
    // =====================================================================================
    public static class Events
    {
        public class Kind
        {
            public string Title = "";
            public double Xp = 1;
            public string About = "";
        }

        public static readonly Dictionary<string, Kind> Kinds = new Dictionary<string, Kind>
        {
            ["doublexp"] = new Kind { Title = "Double XP", Xp = 2, About = "everything earns twice the XP" },
            ["triplexp"] = new Kind { Title = "Triple XP", Xp = 3, About = "everything earns three times the XP" },
            ["party"] = new Kind { Title = "Town Party", Xp = 1.5, About = "get together on a lot - +50% XP" },
            ["holiday"] = new Kind { Title = "Holiday Celebration", Xp = 1.5, About = "a holiday in the town - +50% XP" },
            ["contest"] = new Kind { Title = "Contest", Xp = 1, About = "a contest is on - see the message of the day" },
        };

        public class Ev
        {
            public string Kind = "";
            public string Title = "";
            public DateTime Start;
            public DateTime End;
            public bool Announced;
        }

        public class Saved { public List<Ev> Events = new List<Ev>(); }

        private static readonly object Lock = new object();
        private static Saved Data;
        private const string File = "events.json";

        private static Saved D { get { if (Data == null) Data = Store.Load<Saved>(File); return Data; } }

        public static List<Ev> All() { lock (Lock) return D.Events.OrderBy(e => e.Start).ToList(); }

        public static List<Ev> Active()
        {
            var now = DateTime.UtcNow;
            lock (Lock) return D.Events.Where(e => e.Start <= now && e.End > now).ToList();
        }

        /// <summary>What XP is multiplied by right now (the biggest active XP event).</summary>
        public static double XpMultiplier()
        {
            double m = 1;
            foreach (var e in Active())
                if (Kinds.TryGetValue(e.Kind, out var k) && k.Xp > m) m = k.Xp;
            return m;
        }

        public static string Add(string kind, DateTime startUtc, double hours, string title)
        {
            kind = kind.ToLowerInvariant();
            if (!Kinds.TryGetValue(kind, out var k)) return "no such event - choose one of: " + string.Join(", ", Kinds.Keys);
            if (hours <= 0 || hours > 24 * 14) return "an event lasts between a few minutes and 14 days";
            var ev = new Ev { Kind = kind, Title = string.IsNullOrWhiteSpace(title) ? k.Title : title.Trim(), Start = startUtc, End = startUtc.AddHours(hours) };
            lock (Lock) { D.Events.Add(ev); Store.Save(File, D); }
            Tick();
            return (startUtc <= DateTime.UtcNow ? "started" : "scheduled") + ": " + Describe(ev);
        }

        public static int Stop(string kindOrAll)
        {
            int n;
            lock (Lock)
            {
                var gone = D.Events.Where(e => kindOrAll == "all" || e.Kind == kindOrAll.ToLowerInvariant()).ToList();
                foreach (var e in gone)
                {
                    D.Events.Remove(e);
                    if (e.Announced) Notices.Add("Event over", e.Title + " has ended. Thanks for playing!");
                }
                n = gone.Count;
                Store.Save(File, D);
            }
            return n;
        }

        public static string Describe(Ev e)
        {
            var k = Kinds.TryGetValue(e.Kind, out var kk) ? kk : new Kind();
            return e.Title + " (" + e.Kind + ") " + e.Start.ToLocalTime().ToString("ddd MMM d h:mm tt") + " - " + e.End.ToLocalTime().ToString("ddd h:mm tt")
                + (k.Xp != 1 ? "  XP x" + k.Xp : "");
        }

        /// <summary>Every 30 seconds: announce events that start, finish the ones that end.</summary>
        public static void Tick()
        {
            var now = DateTime.UtcNow;
            lock (Lock)
            {
                bool changed = false;
                foreach (var e in D.Events.ToList())
                {
                    var k = Kinds.TryGetValue(e.Kind, out var kk) ? kk : new Kind();
                    if (e.End <= now)
                    {
                        D.Events.Remove(e);
                        if (e.Announced) Notices.Add("Event over", e.Title + " has ended. Thanks for playing!");
                        Log.Info("event ended: " + e.Title);
                        changed = true;
                    }
                    else if (!e.Announced && e.Start <= now)
                    {
                        e.Announced = true;
                        Notices.Add("Event: " + e.Title, e.Title + " has started - " + k.About + ". It runs until " + e.End.ToLocalTime().ToString("ddd h:mm tt") + " (server time).");
                        Log.Info("event started: " + Describe(e));
                        changed = true;
                    }
                }
                if (changed) Store.Save(File, D);
            }
        }
    }

    // =====================================================================================
    //  Account level and XP (online play only). Earned on the server from what the town's lots
    //  report: time playing, skill points, promotions; bigger milestones give bigger bonuses.
    //  The amounts are set from the panel (/xp set).
    // =====================================================================================
    public static class Progress
    {
        public class Settings
        {
            public int PerMinute = 2;           // each real minute on a lot
            public int PerSkillLevel = 20;      // each new skill point (0-10) a Sim reaches
            public int SkillMaxBonus = 250;     // a skill reaching 10
            public int PerPromotion = 100;      // each promotion
            public int JobTopBonus = 500;       // reaching the top of a career (level 10)
            public int LevelBase = 100;         // XP from level 1 to 2
            public int LevelStep = 50;          // each level after needs this much more
        }

        public class Player
        {
            public long Xp;
            public int Level = 1;
            public long Minutes;
            /// <summary>"sim|skill" -> best level reached (0-10).</summary>
            public Dictionary<string, int> Skills = new Dictionary<string, int>();
            /// <summary>sim -> job * 100 + level.</summary>
            public Dictionary<string, int> Jobs = new Dictionary<string, int>();
        }

        public class Saved
        {
            public Settings Settings = new Settings();
            public Dictionary<string, Player> Players = new Dictionary<string, Player>();
        }

        private static readonly object Lock = new object();
        private static Saved Data;
        private static bool Dirty;
        private const string File = "xp.json";
        private static Saved D { get { if (Data == null) Data = Store.Load<Saved>(File); return Data; } }
        public static Settings Values { get { lock (Lock) return D.Settings; } }

        /// <summary>XP needed to go from level n to n + 1.</summary>
        public static long ToNext(int level) { var s = Values; return s.LevelBase + (long)s.LevelStep * (level - 1); }

        public static Player Of(string key)
        {
            lock (Lock)
            {
                if (!D.Players.TryGetValue(key, out var p)) D.Players[key] = p = new Player();
                return p;
            }
        }

        /// <summary>(level, xp into this level, xp needed for the next)</summary>
        public static (int level, long into, long need) Where(string key)
        {
            lock (Lock)
            {
                var p = Of(key);
                long spent = 0;
                for (int l = 1; l < p.Level; l++) spent += ToNext(l);
                return (p.Level, p.Xp - spent, ToNext(p.Level));
            }
        }

        public static void Award(string key, long amount, string why, bool events = true)
        {
            if (string.IsNullOrEmpty(key) || amount == 0) return;
            if (events) amount = (long)Math.Round(amount * Events.XpMultiplier());
            int before, after;
            lock (Lock)
            {
                var p = Of(key);
                before = p.Level;
                p.Xp = Math.Max(0, p.Xp + amount);
                // the level that much XP reaches
                long spent = 0; int lvl = 1;
                while (spent + ToNext(lvl) <= p.Xp && lvl < 999) { spent += ToNext(lvl); lvl++; }
                p.Level = lvl;
                after = lvl;
                Dirty = true;
            }
            if (why != null && amount >= 50) Log.Info("xp: " + key + " +" + amount + " (" + why + ")");
            if (after > before)
            {
                Notices.Add("Level up!", "You're now level " + after + ". Keep playing to earn more XP.", key);
                Log.Info("xp: " + key + " reached level " + after);
            }
        }

        /// <summary>Every minute: XP for each account playing on a lot.</summary>
        public static void PlayMinute(IEnumerable<string> keys)
        {
            var per = Values.PerMinute;
            foreach (var k in keys.Distinct())
            {
                lock (Lock) { Of(k).Minutes++; }
                Award(k, per, null);
            }
        }

        /// <summary>A lot's report on one of its families (see Lots: "stats"). Skill points and promotions earn XP.</summary>
        public static void Stats(string key, List<SimStats> sims)
        {
            var s = Values;
            foreach (var sim in sims)
            {
                long xp = 0;
                var why = new List<string>();
                lock (Lock)
                {
                    var p = Of(key);
                    for (int i = 0; i < sim.Skills.Length; i++)
                    {
                        var lvl = Math.Clamp(sim.Skills[i] / 100, 0, 10);
                        var k = sim.Name + "|" + i;
                        if (!p.Skills.TryGetValue(k, out var best)) { p.Skills[k] = lvl; Dirty = true; continue; } // first time we see this Sim: where they are now
                        if (lvl <= best) continue;
                        xp += (lvl - best) * s.PerSkillLevel;
                        if (lvl >= 10 && best < 10) xp += s.SkillMaxBonus;
                        why.Add(sim.Name + " " + SimStats.SkillNames[i] + " " + lvl);
                        p.Skills[k] = lvl; Dirty = true;
                    }
                    var job = sim.Job * 100 + sim.JobLevel;
                    if (!p.Jobs.TryGetValue(sim.Name, out var had)) { p.Jobs[sim.Name] = job; Dirty = true; }
                    else if (sim.Job > 0 && had / 100 == sim.Job && sim.JobLevel > had % 100)
                    {
                        xp += (sim.JobLevel - had % 100) * s.PerPromotion;
                        if (sim.JobLevel >= 10) xp += s.JobTopBonus;
                        why.Add(sim.Name + " promoted to level " + sim.JobLevel);
                        p.Jobs[sim.Name] = job; Dirty = true;
                    }
                    else if (had / 100 != sim.Job) { p.Jobs[sim.Name] = job; Dirty = true; } // a new career starts from where it is
                }
                if (xp > 0) Award(key, xp, string.Join(", ", why));
            }
        }

        public static List<(string key, Player p)> Top(int n)
        {
            lock (Lock) return D.Players.OrderByDescending(x => x.Value.Xp).Take(n).Select(x => (x.Key, x.Value)).ToList();
        }

        public static string Set(string name, int value)
        {
            lock (Lock)
            {
                var f = typeof(Settings).GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (f == null) return "no such XP setting - see /xp settings";
                if (value < 0 || value > 1000000) return "keep it between 0 and 1,000,000";
                f.SetValue(D.Settings, value);
                Dirty = true;
            }
            Save();
            return "XP setting " + name + " = " + value;
        }

        public static string SettingsText()
        {
            var s = Values;
            return string.Join("\n", typeof(Settings).GetFields().Select(f => "  " + f.Name.PadRight(16) + f.GetValue(s)));
        }

        public static void Save()
        {
            lock (Lock)
            {
                if (!Dirty || Data == null) return;
                Store.Save(File, Data);
                Dirty = false;
            }
        }
    }

    /// <summary>One Sim in a lot's "stats" line.</summary>
    public class SimStats
    {
        public static readonly string[] SkillNames = { "Cooking", "Mechanical", "Charisma", "Body", "Logic", "Creativity" };
        public string Name = "";
        public int[] Skills = new int[6];
        public int Job, JobLevel;

        /// <summary>"name:c,m,ch,b,l,cr:job:level;..."</summary>
        public static List<SimStats> Parse(string text)
        {
            var list = new List<SimStats>();
            foreach (var part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = part.Split(':');
                if (f.Length < 4) continue;
                var sk = f[1].Split(',').Select(x => int.TryParse(x, out var v) ? v : 0).ToArray();
                if (sk.Length < 6) continue;
                int.TryParse(f[2], out var job);
                int.TryParse(f[3], out var lvl);
                list.Add(new SimStats { Name = f[0].Trim(), Skills = sk.Take(6).ToArray(), Job = job, JobLevel = lvl });
            }
            return list;
        }
    }

    // =====================================================================================
    //  Cheat checks (online only): the server compares each report from a lot with the last
    //  one and flags what the game can't do on its own - a huge money jump, skills or careers
    //  leaping ahead. Flags wait in the panel (/flags) for a person to look at; nothing is
    //  done to the player automatically.
    // =====================================================================================
    public static class AntiCheat
    {
        public class Flag
        {
            public int Id;
            public DateTime Time;
            public string Account = "";
            public string What = "";
            public string Detail = "";
            public bool Reviewed;
        }

        public class Saved { public int NextId = 1; public List<Flag> Flags = new List<Flag>(); }

        /// <summary>More money than this appearing between two reports (10 seconds apart) is flagged.</summary>
        public static int MoneyJump = 10000;
        /// <summary>More skill than this (100 = one skill point) between two reports is flagged.</summary>
        public static int SkillJump = 150;

        private class Last { public DateTime Time; public int Budget = -1; public Dictionary<string, int[]> Sims = new Dictionary<string, int[]>(); }

        private static readonly object Lock = new object();
        private static Saved Data;
        private const string File = "flags.json";
        private static Saved D { get { if (Data == null) Data = Store.Load<Saved>(File); return Data; } }
        private static readonly Dictionary<int, Last> Seen = new Dictionary<int, Last>();
        private static readonly Dictionary<int, (long amount, DateTime until)> Expected = new Dictionary<int, (long, DateTime)>();

        /// <summary>An admin's /money: that much is expected to appear for this family in the next few minutes.</summary>
        public static void ExpectMoney(int family, long amount)
        {
            lock (Lock)
            {
                Expected.TryGetValue(family, out var had);
                var keep = had.until > DateTime.UtcNow ? had.amount : 0;
                Expected[family] = (keep + amount, DateTime.UtcNow.AddMinutes(5));
            }
        }

        public static void Stats(string key, string account, int family, int budget, List<SimStats> sims)
        {
            var now = DateTime.UtcNow;
            lock (Lock)
            {
                if (!Seen.TryGetValue(family, out var last) || now - last.Time > TimeSpan.FromSeconds(90))
                {
                    // first report (or the lot was closed for a while): start comparing from here
                    last = new Last();
                    Seen[family] = last;
                }
                else
                {
                    if (budget >= 0 && last.Budget >= 0)
                    {
                        long gain = budget - last.Budget;
                        if (Expected.TryGetValue(family, out var exp) && exp.until > now && exp.amount != 0)
                        {
                            // an admin's /money accounts for (some of) it
                            var used = exp.amount > 0 ? Math.Min(exp.amount, Math.Max(0, gain)) : Math.Max(exp.amount, Math.Min(0, gain));
                            gain -= used;
                            Expected[family] = (exp.amount - used, exp.until);
                        }
                        if (gain > MoneyJump)
                            Add(account, "money", "family " + family + ": §" + last.Budget + " -> §" + budget + " in " + (int)(now - last.Time).TotalSeconds + "s");
                    }
                    foreach (var sim in sims)
                    {
                        if (!last.Sims.TryGetValue(sim.Name, out var before)) continue;
                        for (int i = 0; i < 6 && i < before.Length; i++)
                        {
                            var gain = sim.Skills[i] - before[i];
                            if (gain > SkillJump)
                                Add(account, "skill", sim.Name + " " + SimStats.SkillNames[i] + ": " + before[i] / 100.0 + " -> " + sim.Skills[i] / 100.0 + " in " + (int)(now - last.Time).TotalSeconds + "s");
                        }
                        if (before.Length > 7 && before[6] == sim.Job && sim.Job > 0 && sim.JobLevel - before[7] >= 2)
                            Add(account, "career", sim.Name + ": job level " + before[7] + " -> " + sim.JobLevel + " at once");
                    }
                }
                last.Time = now;
                if (budget >= 0) last.Budget = budget;
                foreach (var sim in sims) last.Sims[sim.Name] = sim.Skills.Concat(new[] { sim.Job, sim.JobLevel }).ToArray();
            }
        }

        private static void Add(string account, string what, string detail)
        {
            var f = new Flag { Id = D.NextId++, Time = DateTime.UtcNow, Account = account, What = what, Detail = detail };
            D.Flags.Add(f);
            if (D.Flags.Count > 500) D.Flags.RemoveAt(0);
            Store.Save(File, D);
            Log.Warn("CHEAT CHECK #" + f.Id + " " + account + ": " + what + " - " + detail + "   (review with /flags)");
        }

        public static List<Flag> List(bool all) { lock (Lock) return D.Flags.Where(f => all || !f.Reviewed).ToList(); }

        public static int Review(string idOrAll)
        {
            lock (Lock)
            {
                var them = D.Flags.Where(f => !f.Reviewed && (idOrAll == "all" || f.Id.ToString() == idOrAll)).ToList();
                foreach (var f in them) f.Reviewed = true;
                if (them.Count > 0) Store.Save(File, D);
                return them.Count;
            }
        }
    }

    // =====================================================================================
    //  Player reports (from the website's launcher): they wait in the panel (/reports).
    // =====================================================================================
    public static class Reports
    {
        public class Report
        {
            public int Id;
            public DateTime Time;
            public string From = "";
            public string About = "";
            public string Reason = "";
            public bool Done;
        }

        public class Saved { public int NextId = 1; public List<Report> Reports = new List<Report>(); }

        private static readonly object Lock = new object();
        private static Saved Data;
        private const string File = "reports.json";
        private static Saved D { get { if (Data == null) Data = Store.Load<Saved>(File); return Data; } }

        public static int Add(string from, string about, string reason)
        {
            lock (Lock)
            {
                var r = new Report { Id = D.NextId++, Time = DateTime.UtcNow, From = from, About = about, Reason = reason };
                D.Reports.Add(r);
                if (D.Reports.Count > 500) D.Reports.RemoveAt(0);
                Store.Save(File, D);
                Log.Warn("REPORT #" + r.Id + " from " + from + " about " + about + ": " + reason + "   (see /reports)");
                return r.Id;
            }
        }

        public static List<Report> List(bool all) { lock (Lock) return D.Reports.Where(r => all || !r.Done).ToList(); }

        public static int Close(string idOrAll)
        {
            lock (Lock)
            {
                var them = D.Reports.Where(r => !r.Done && (idOrAll == "all" || r.Id.ToString() == idOrAll)).ToList();
                foreach (var r in them) r.Done = true;
                if (them.Count > 0) Store.Save(File, D);
                return them.Count;
            }
        }
    }

    // =====================================================================================
    //  /kick: who's been sent off the town's lots, and until when.
    // =====================================================================================
    public static class Kicks
    {
        private static readonly Dictionary<string, DateTime> Until = new Dictionary<string, DateTime>();

        public static void Add(string key, int minutes) { lock (Until) Until[key] = DateTime.UtcNow.AddMinutes(minutes); }
        public static bool Remove(string key) { lock (Until) return Until.Remove(key); }

        /// <summary>Minutes left, or 0 if they can play.</summary>
        public static int MinutesLeft(string key)
        {
            lock (Until)
            {
                if (!Until.TryGetValue(key, out var t)) return 0;
                var left = t - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) { Until.Remove(key); return 0; }
                return (int)Math.Ceiling(left.TotalMinutes);
            }
        }
    }

    // =====================================================================================
    //  Automatic daily backups of everything (accounts, the town, XP, flags, reports), kept for
    //  two weeks, and restoring one (/restore - happens as the server restarts).
    // =====================================================================================
    public static class Backups
    {
        public const string Pending = "restore.pending";

        public static string Dir => Path.Combine(Program.DataDir, "backups");

        public static List<DirectoryInfo> List()
        {
            try { return new DirectoryInfo(Dir).GetDirectories().OrderByDescending(d => d.Name).ToList(); }
            catch { return new List<DirectoryInfo>(); }
        }

        public static long Size(DirectoryInfo d)
        {
            try { return d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { return 0; }
        }

        /// <summary>Every 10 minutes: one automatic backup a day, at the hour set in server.json (default 4 AM).</summary>
        public static void Tick(ServerConfig config, Action saveAll)
        {
            if (config.BackupHour < 0) return;
            var now = DateTime.Now;
            var lastAuto = List().FirstOrDefault(d => d.Name.EndsWith("_auto"));
            var lastTime = lastAuto?.CreationTime ?? DateTime.MinValue;
            bool due = (now.Hour == config.BackupHour && lastTime.Date < now.Date) || now - lastTime > TimeSpan.FromHours(30);
            if (!due) return;
            try { saveAll(); } catch { }
            Log.Info(Program.Backup("auto"));
            // keep the newest few automatic ones (the ones you made yourself stay)
            foreach (var old in List().Where(d => d.Name.EndsWith("_auto")).Skip(Math.Max(1, config.KeepBackups)))
            {
                try { old.Delete(true); Log.Info("backup: removed the old " + old.Name); } catch { }
            }
        }

        /// <summary>Called first thing when the server starts: puts a backup back if /restore asked for one.</summary>
        public static void RestoreIfAsked()
        {
            var marker = Path.Combine(Program.DataDir, Pending);
            if (!System.IO.File.Exists(marker)) return;
            var name = System.IO.File.ReadAllText(marker).Trim();
            System.IO.File.Delete(marker);
            var src = Path.Combine(Dir, name);
            if (name.Length == 0 || name.Contains("..") || !Directory.Exists(src)) { Log.Warn("restore: no backup called " + name); return; }
            try
            {
                foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(src, f);
                    if (rel.StartsWith("Town" + Path.DirectorySeparatorChar)) continue; // below
                    var to = Path.Combine(Program.DataDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    System.IO.File.Copy(f, to, true);
                }
                var town = Path.Combine(src, "Town");
                if (Directory.Exists(town))
                {
                    var live = Path.Combine(Program.DataDir, "Town");
                    if (Directory.Exists(live)) Directory.Delete(live, true);
                    CopyDir(town, live);
                }
                Log.Info("restore: put back the backup " + name);
            }
            catch (Exception e) { Log.Warn("restore failed part-way: " + e.Message + " (the copy made just before is in backups/)"); }
        }

        public static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) System.IO.File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }

    // =====================================================================================
    //  Moderators: powers given per account in the panel, used from the website's moderator page.
    // =====================================================================================
    public static class Powers
    {
        public static readonly string[] All = { "say", "kick", "reports", "flags", "events" };
        public static readonly Dictionary<string, string> About = new Dictionary<string, string>
        {
            ["say"] = "send a message to everyone online",
            ["kick"] = "send a player off the town's lots (up to an hour)",
            ["reports"] = "read players' reports and mark them dealt with",
            ["flags"] = "read the cheat-check flags and mark them looked at",
            ["events"] = "start and end events (up to a day)",
        };
    }

    public static class Moderation
    {
        /// <summary>Sends a player off every town lot with a reason; they can't come back for `minutes`.</summary>
        public static string Kick(Lots lots, Accounts accounts, string name, int minutes, string reason)
        {
            var acc = accounts.Find(name);
            var shown = acc?.Name ?? name;
            if (minutes > 0) Kicks.Add(Accounts.KeyOf(shown), minutes);
            var why = string.IsNullOrWhiteSpace(reason) ? "An admin removed you from the online town." : reason;
            why = new string(why.Where(c => c >= 32 && c != '|').ToArray());
            foreach (var w in lots.Running()) lots.Send(w, "kick " + shown + "|" + why);
            return "kicked " + shown + (minutes > 0 ? " - they can come back in " + minutes + " minute(s)" : "") + (acc == null ? "  (no account called that)" : "");
        }
    }

    // =====================================================================================
    //  Shutting down (or restarting) with a countdown everyone online hears about.
    // =====================================================================================
    public static class Shutdown
    {
        private static DateTime At;
        private static bool Restart;
        private static int From;
        private static readonly HashSet<int> Said = new HashSet<int>();
        private static readonly int[] Marks = { 600, 300, 120, 60, 30, 10, 5 };

        /// <summary>Seconds until the server stops, or -1.</summary>
        public static int SecondsLeft => At == default ? -1 : Math.Max(0, (int)Math.Ceiling((At - DateTime.UtcNow).TotalSeconds));
        public static bool Restarting => At != default && Restart;

        public static string Text(int seconds, bool restart)
        {
            var what = restart ? "Server restarting" : "Server shutting down";
            if (seconds >= 120 && seconds % 60 == 0) return what + " in " + seconds / 60 + " minutes";
            return what + " in " + seconds + " second" + (seconds == 1 ? "" : "s");
        }

        public static string Start(int seconds, bool restart)
        {
            lock (Said)
            {
                At = DateTime.UtcNow.AddSeconds(seconds);
                Restart = restart;
                From = seconds;
                Said.Clear();
            }
            var msg = Text(seconds, restart);
            Notices.Add(restart ? "Server restarting" : "Server shutting down", msg + ". Please finish up - your game is saved.");
            Log.Info(msg);
            return msg;
        }

        public static bool Cancel()
        {
            if (At == default) return false;
            At = default;
            Notices.Add("All clear", (Restart ? "The restart" : "The shutdown") + " was called off. Carry on!");
            Log.Info("shutdown called off");
            return true;
        }

        /// <summary>Every second: a reminder at 10 min, 5 min, 2 min, 60, 30, 10 and 5 seconds left, then stop.</summary>
        public static void Tick(Action<int> stop)
        {
            if (At == default) return;
            var left = SecondsLeft;
            if (left <= 0)
            {
                At = default;
                stop(Restart ? 75 : 0);
                return;
            }
            lock (Said)
            {
                foreach (var m in Marks)
                {
                    if (m >= From || left > m || Said.Contains(m)) continue;
                    foreach (var bigger in Marks) if (bigger >= m) Said.Add(bigger); // (one reminder per step, never a pile-up)
                    Notices.Add(Restart ? "Server restarting" : "Server shutting down", Text(m, Restart) + ".");
                    Log.Info(Text(m, Restart));
                    break;
                }
            }
        }
    }
}
