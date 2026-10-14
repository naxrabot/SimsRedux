using FSO.Common.IO;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using System;
using System.Collections.Generic;
using System.IO;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: which families in the single-player neighbourhood are the player's own.
    ///
    /// Families made in Create-A-Family are the player's. The game's own families (the Goths,
    /// Pleasants...) are background families until the player takes one over, which adds it to
    /// the roster. Only roster families get the green plumbob and are played normally; the rest
    /// can be watched living their own lives.
    ///
    /// The roster is kept with the neighbourhood (redux_roster.txt in its folder), so a fresh
    /// neighbourhood starts with an empty one.
    /// </summary>
    public static class ReduxRoster
    {
        private static string LoadedFor;
        private static HashSet<int> Taken = new HashSet<int>();

        private static string FilePath
        {
            get { return Path.Combine(Content.Get().Neighborhood.UserPath ?? "", "redux_roster.txt"); }
        }

        private static void Load()
        {
            var path = FilePath;
            if (LoadedFor == path) return;
            LoadedFor = path;
            Taken = new HashSet<int>();
            try
            {
                if (!FSOFile.Exists(path)) return;
                foreach (var line in FSOFile.ReadAllLines(path))
                    if (int.TryParse(line.Trim(), out var id)) Taken.Add(id);
            }
            catch (Exception e) { Console.WriteLine("[SimsRedux] Couldn't read the family roster: " + e.Message); }
        }

        /// <summary>Is this one of the player's own families (made by them, or taken over)?</summary>
        public static bool IsPlayers(FAMI fam)
        {
            if (fam == null) return false;
            if ((fam.Unknown & 8) != 0) return true; // made in Create-A-Family
            Load();
            return Taken.Contains(fam.ChunkID);
        }

        /// <summary>Takes a background family over: from now on it's one of the player's families.</summary>
        public static void TakeOver(FAMI fam)
        {
            if (fam == null) return;
            Load();
            if (!Taken.Add(fam.ChunkID)) return;
            try
            {
                var lines = new List<string>();
                foreach (var id in Taken) lines.Add(id.ToString());
                FSOFile.WriteAllLines(FilePath, lines);
            }
            catch (Exception e) { Console.WriteLine("[SimsRedux] Couldn't save the family roster: " + e.Message); }
        }

        /// <summary>Forget what was read (after the neighbourhood itself was replaced).</summary>
        public static void Reset() { LoadedFor = null; }
    }
}
