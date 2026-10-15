using System.Text.Json;

namespace SimsRedux.Server
{
    /// <summary>
    /// 2D: one clock for the whole online town, kept by the server.
    ///
    /// It runs at the game's normal speed (one Sim minute every real second, a Sim day in 24 real
    /// minutes) while anyone is playing on any lot in the town, and stops while nobody is - the
    /// town waits for its players. Every lot is set to this time when it opens and kept on it while
    /// it runs, so it's the same time of day on every lot. Players can't speed time up or pause it.
    ///
    /// Kept with the town (Town/UserData/clock.json), so a fresh town starts a fresh clock.
    /// </summary>
    public static class TownClock
    {
        /// <summary>A new town starts on day 1 at 8:00 AM.</summary>
        public const long StartMinutes = 8 * 60;
        /// <summary>How often running lots are told the time (seconds) - they fix themselves if they've drifted.</summary>
        public const int TellEvery = 20;

        private static readonly object Lock = new object();
        private static long Minutes = -1;
        private static int SinceSave, SinceTell;
        private static bool Dirty;

        private static string FilePath => Path.Combine(Lots.TownDir, "clock.json");

        private class Saved { public long Minutes { get; set; } }

        /// <summary>The town's time: Sim minutes since day 1, midnight.</summary>
        public static long Now
        {
            get
            {
                lock (Lock)
                {
                    if (Minutes < 0) Load();
                    return Minutes;
                }
            }
        }

        public static string Describe(long minutes)
        {
            var day = minutes / 1440 + 1;
            var h = (int)(minutes % 1440 / 60);
            var m = (int)(minutes % 60);
            return "day " + day + ", " + ((h % 12 == 0) ? 12 : h % 12) + ":" + m.ToString("00") + (h < 12 ? " AM" : " PM");
        }

        private static void Load()
        {
            Minutes = StartMinutes;
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath));
                    if (s != null && s.Minutes >= 0) Minutes = s.Minutes;
                }
            }
            catch (Exception e) { Log.Warn("town clock: couldn't read clock.json (" + e.Message + ") - starting at " + Describe(Minutes)); }
        }

        private static void Save()
        {
            try
            {
                if (!Directory.Exists(Lots.TownDir)) return;
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(new Saved { Minutes = Minutes }));
                File.Move(tmp, FilePath, true);
                Dirty = false;
            }
            catch (Exception e) { Log.Warn("town clock: couldn't save (" + e.Message + ")"); }
        }

        /// <summary>A brand-new town: the clock starts again (called when the town is replaced).</summary>
        public static void Reset()
        {
            lock (Lock)
            {
                Minutes = StartMinutes;
                Save();
            }
        }

        /// <summary>Sets the town's time (the admin's /town time).</summary>
        public static void Set(long minutes, Lots lots)
        {
            lock (Lock)
            {
                Minutes = Math.Max(0, minutes);
                Save();
            }
            TellLots(lots);
        }

        /// <summary>Every real second (the server's timer).</summary>
        public static void Tick(Lots lots)
        {
            if (lots == null || !Lots.TownReady) return;
            bool someonePlaying;
            try { someonePlaying = lots.Running().Any(w => w.Ready && w.Players > 0); }
            catch { return; }
            lock (Lock)
            {
                if (Minutes < 0) Load();
                if (someonePlaying) { Minutes++; Dirty = true; }
                if (Dirty && ++SinceSave >= 30) { SinceSave = 0; Save(); }
            }
            if (++SinceTell >= TellEvery) { SinceTell = 0; TellLots(lots); }
        }

        /// <summary>Every open lot hears the town's time (a lot that has drifted puts itself right).</summary>
        public static void TellLots(Lots lots)
        {
            var now = Now;
            foreach (var w in lots.Running())
                if (w.Ready && !w.Closing) lots.Send(w, "time " + now);
        }

        /// <summary>Saves now (the server is stopping).</summary>
        public static void Flush()
        {
            lock (Lock) { if (Minutes >= 0 && Dirty) Save(); }
        }
    }
}
