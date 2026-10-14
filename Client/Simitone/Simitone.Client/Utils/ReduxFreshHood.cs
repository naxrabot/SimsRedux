using FSO.Common;
using FSO.Common.IO;
using FSO.Content;
using FSO.Content.TS1;
using FSO.Client.UI.Framework;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.IO;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: starting the single-player neighbourhood over with the game's starting families
    /// (the Goths, Newbies, Pleasants and the rest). The current neighbourhood is kept as a backup.
    ///
    /// Offered once, on the neighbourhood screen, to anyone whose neighbourhood has no families
    /// at all (an earlier version started new games from the game's blank template). It can be
    /// asked for again any time with the cheat "freshneighborhood".
    /// </summary>
    public static class ReduxFreshHood
    {
        private static bool AskedThisRun;

        private static string LocalFolder { get { return Path.Combine(FSOEnvironment.UserDir, "UserData/"); } }

        private static bool PendingAsk;

        /// <summary>The house numbers that have a family living in them, in a neighbourhood folder.</summary>
        private static System.Collections.Generic.HashSet<int> HousedHouses(string folder)
        {
            var set = new System.Collections.Generic.HashSet<int>();
            try
            {
                var nbhd = Path.Combine(folder, "Neighborhood.iff");
                if (!FSOFile.Exists(nbhd)) return set;
                var iff = new FSO.Files.Formats.IFF.IffFile(nbhd);
                foreach (var f in iff.List<FSO.Files.Formats.IFF.Chunks.FAMI>() ?? new System.Collections.Generic.List<FSO.Files.Formats.IFF.Chunks.FAMI>())
                    if (f.HouseNumber > 0) set.Add((int)f.HouseNumber);
            }
            catch { }
            return set;
        }

        /// <summary>
        /// On the neighbourhood screen: offer a fresh start (once) if none of the game's starting
        /// families live here any more - or if it was asked for with the cheat while on a lot.
        /// </summary>
        public static void OfferIfEmpty()
        {
            if (ReduxWorker.Active || ReduxTown.Active) return;
            if (PendingAsk)
            {
                PendingAsk = false;
                FSO.Common.Utils.GameThread.NextUpdate((s) => Ask(false));
                return;
            }
            if (AskedThisRun) return;
            AskedThisRun = true;
            try
            {
                // a brand-new game: the starting neighbourhood exactly as it came, never filled in yet
                if (!ReduxSettings.GetBool("hoodpopulated", false))
                {
                    var start = HousedHouses(Content.Get().Neighborhood.FreshSource());
                    var now = HousedHouses(LocalFolder);
                    if (start.Count > 0 && now.SetEquals(start))
                    {
                        Populate();
                        var screen = UIScreen.Current as TS1GameScreen;
                        FSO.Common.Utils.GameThread.NextUpdate((s2) => screen?.RefreshNeighbourhoodScreen());
                        return;
                    }
                }
                if (ReduxSettings.GetBool("freshhoodoffered", false)) return;
                var originals = HousedHouses(Content.Get().Neighborhood.FreshSource());
                if (originals.Count == 0) return; // nothing better to offer
                var mine = HousedHouses(LocalFolder);
                if (mine.Overlaps(originals)) return; // the starting families are still here
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] fresh neighbourhood check: " + e.Message);
                return;
            }
            FSO.Common.Utils.GameThread.NextUpdate((s) => Ask(true));
        }

        /// <summary>Asks, then starts over on Yes. "first" = the automatic offer (remembered if declined).</summary>
        public static void Ask(bool first)
        {
            var screen = UIScreen.Current as TS1GameScreen;
            if (screen == null) return;
            if (screen.vm != null)
            {
                PendingAsk = true;
                UIClassicDialog.Ok("Fresh Neighborhood", "Go back to the neighborhood and you'll be asked whether to start it fresh.");
                return;
            }
            if (ReduxTown.Active)
            {
                UIClassicDialog.Ok("Fresh Neighborhood", "This only changes your own neighborhood. Leave the online town first.");
                return;
            }
            var message = (first ? "None of the game's starting families live in your neighborhood.\n\n" : "")
                + "Start a fresh neighborhood with the original families (the Goths, Newbies, Pleasants and the rest)?\n\n"
                + "Your current neighborhood, with any families and houses you made, is kept as a backup.";
            UIClassicDialog.Show("Fresh Neighborhood", message,
                Tuple.Create<string, Action>("Start Fresh", () => Reset(screen)),
                Tuple.Create<string, Action>("Not Now", () => { if (first) ReduxSettings.Set("freshhoodoffered", true); }));
        }

        private static void Reset(TS1GameScreen screen)
        {
            ReduxSettings.Set("freshhoodoffered", true);
            var local = LocalFolder;
            string backup = null;
            try
            {
                if (FSODirectory.Exists(local))
                {
                    backup = Path.Combine(FSOEnvironment.UserDir, "UserData-backup-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "/");
                    CopyFolder(local, backup);
                    FSODirectory.Delete(local, true);
                }
                Content.Get().Neighborhood.InitSpecific(0); // copies the game's starting neighbourhood in
                Populate();
                Console.WriteLine("[SimsRedux] started a fresh neighbourhood" + (backup != null ? " (the old one is in " + backup + ")" : ""));
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] fresh neighbourhood failed: " + e);
                UIClassicDialog.Ok("Fresh Neighborhood", "Sorry, the neighborhood couldn't be started over: " + e.Message
                    + (backup != null ? "\n\nYour old neighborhood is in " + backup : ""));
            }
            screen.RefreshNeighbourhoodScreen();
        }

        // ------------------------------------------------------------------
        // Filling in a fresh single-player neighbourhood
        // ------------------------------------------------------------------

        /// <summary>How many families live in houses in a fresh single-player neighbourhood.</summary>
        private const int FamiliesInHouses = 4;
        /// <summary>How many empty lots are left for building on.</summary>
        private const int EmptyLotsToKeep = 3;

        /// <summary>
        /// A fresh single-player neighbourhood: families from the family bin move into houses for sale
        /// until 4 families live in the neighbourhood, and empty lots get a house copied from a lot
        /// of exactly the same shape (same size, same buildable area, road on the same side), leaving
        /// 3 empty lots to build on. The online town is left as the game made it.
        /// </summary>
        public static void Populate()
        {
            ReduxSettings.Set("hoodpopulated", true);
            try
            {
                var neigh = Content.Get().Neighborhood;
                var main = MainLots(neigh);
                int built = FillEmptyLots(neigh, main);
                int moved = MoveFamiliesIn(neigh, main);
                neigh.SaveNeighbourhood(false);
                neigh.LoadFolder(neigh.UserPath); // read it all back, houses included
                Console.WriteLine("[SimsRedux] fresh neighbourhood filled in: " + moved + " families moved in, " + built + " empty lots got a house");
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] filling in the neighbourhood failed: " + e);
            }
        }

        /// <summary>The house numbers on the main neighbourhood map (not Downtown, Vacation Island...).</summary>
        private static System.Collections.Generic.List<short> MainLots(TS1NeighborhoodProvider neigh)
        {
            var list = new System.Collections.Generic.List<short>();
            var str = neigh.LotLocations?.Get<FSO.Files.Formats.IFF.Chunks.STR>(4);
            if (str == null) return list;
            for (int i = 0; i < str.Length; i++)
            {
                var parts = str.GetString(i).Split(',');
                if (short.TryParse(parts[0].Trim(), out var n) && n != 99 && !list.Contains(n)) list.Add(n);
            }
            return list;
        }

        private static bool Residential(TS1NeighborhoodProvider neigh, short house)
        {
            return neigh.ZoningDictionary.TryGetValue(house, out var z) && z == 0;
        }

        private static int MoveFamiliesIn(TS1NeighborhoodProvider neigh, System.Collections.Generic.List<short> main)
        {
            var fams = neigh.MainResource.List<FSO.Files.Formats.IFF.Chunks.FAMI>() ?? new System.Collections.Generic.List<FSO.Files.Formats.IFF.Chunks.FAMI>();
            int housed = 0;
            foreach (var f in fams) if (f.HouseNumber > 0) housed++;
            int moved = 0;
            // the family bin, smallest budget first so everyone can afford something
            var bin = new System.Collections.Generic.List<FSO.Files.Formats.IFF.Chunks.FAMI>();
            foreach (var f in fams) if ((f.Unknown & 16) > 0 && f.HouseNumber == 0 && f.FamilyGUIDs != null && f.FamilyGUIDs.Length > 0) bin.Add(f);
            bin.Sort((a, b) => a.Budget.CompareTo(b.Budget));
            foreach (var fam in bin)
            {
                if (housed >= FamiliesInHouses) break;
                // the best house for sale they can afford (leaving them a little money)
                short best = 0; int bestPrice = -1;
                foreach (var h in main)
                {
                    if (!Residential(neigh, h) || neigh.GetFamilyForHouse(h) != null) continue;
                    var simi = SimiOf(neigh, h);
                    if (simi == null || simi.ArchitectureValue <= 0) continue; // an empty lot
                    var price = simi.PurchaseValue;
                    if (price > fam.Budget - 1000) continue;
                    if (price > bestPrice) { best = h; bestPrice = price; }
                }
                if (best == 0) continue;
                neigh.SetFamilyForHouse(best, fam, true);
                housed++; moved++;
                Console.WriteLine("[SimsRedux] fresh neighbourhood: family " + fam.ChunkID + " moved into house " + best);
            }
            return moved;
        }

        private static FSO.Files.Formats.IFF.Chunks.SIMI SimiOf(TS1NeighborhoodProvider neigh, short house)
        {
            try
            {
                var path = neigh.GetHousePath(house);
                if (!FSOFile.Exists(path)) return null;
                return neigh.GetHouse(house)?.Get<FSO.Files.Formats.IFF.Chunks.SIMI>(1);
            }
            catch { return null; }
        }

        private static int FillEmptyLots(TS1NeighborhoodProvider neigh, System.Collections.Generic.List<short> main)
        {
            // empty lots and houses for sale, with what makes their lots the same shape
            var empty = new System.Collections.Generic.List<short>();
            var donors = new System.Collections.Generic.List<short>();
            var shape = new System.Collections.Generic.Dictionary<short, string>();
            foreach (var h in main)
            {
                if (!Residential(neigh, h) || neigh.GetFamilyForHouse(h) != null) continue;
                var path = neigh.GetHousePath(h);
                if (!FSOFile.Exists(path)) continue;
                var raw = FSOFile.ReadAllBytes(path);
                var key = LotShape(raw, out var archValue);
                if (key == null) continue; // not a lot file from the original game
                shape[h] = key;
                if (archValue <= 0) empty.Add(h); else donors.Add(h);
            }
            int toFill = empty.Count - EmptyLotsToKeep;
            if (toFill <= 0) return 0;
            var rand = new Random(1234);
            var used = new System.Collections.Generic.HashSet<short>();
            int filled = 0;
            // fill the lots that have a match; the ones without one stay empty
            foreach (var target in empty)
            {
                if (filled >= toFill) break;
                var matches = donors.FindAll(d => shape[d] == shape[target]);
                if (matches.Count == 0) continue;
                var fresh = matches.FindAll(d => !used.Contains(d));
                var pick = (fresh.Count > 0 ? fresh : matches)[rand.Next((fresh.Count > 0 ? fresh : matches).Count)];
                var bytes = FSOFile.ReadAllBytes(neigh.GetHousePath(pick));
                if (!SetHouseNumber(bytes, target)) continue;
                FSOFile.WriteAllBytes(neigh.GetHousePath(target), bytes);
                used.Add(pick);
                filled++;
                Console.WriteLine("[SimsRedux] fresh neighbourhood: built a copy of house " + pick + " on empty lot " + target);
            }
            return filled;
        }

        // ---- reading the original game's lot files directly (they're kept byte for byte) ----

        /// <summary>Finds a chunk's data (offset, length) in an IFF file, by type and id.</summary>
        private static bool FindChunk(byte[] d, string type, int id, out int data, out int length)
        {
            data = length = 0;
            int pos = 64;
            while (pos + 76 <= d.Length)
            {
                var t = System.Text.Encoding.ASCII.GetString(d, pos, 4);
                long size = ((long)d[pos + 4] << 24) | ((long)d[pos + 5] << 16) | ((long)d[pos + 6] << 8) | d[pos + 7];
                int cid = (d[pos + 8] << 8) | d[pos + 9];
                if (size < 76 || pos + size > d.Length) return false;
                if (t == type && cid == id) { data = pos + 76; length = (int)size - 76; return true; }
                pos += (int)size;
            }
            return false;
        }

        /// <summary>
        /// What decides whether one lot's house fits another lot: the lot size, which side the road
        /// is on, and the exact buildable area. Null for a lot saved by this game (not the original format).
        /// </summary>
        private static string LotShape(byte[] d, out int archValue)
        {
            archValue = 0;
            if (!FindChunk(d, "SIMI", 1, out var simi, out var simiLen) || simiLen < 12 + 64) return null;
            if (!FindChunk(d, "HOUS", 0, out var hous, out var housLen) || housLen < 30) return null;
            if (!FindChunk(d, "ARRY", 8, out var flags, out var flagsLen)) return null;
            if (FindChunk(d, "FSOV", 1, out _, out _)) return null;
            var version = BitConverter.ToUInt32(d, simi + 4);
            int items = version > 0x3F ? 0x40 : 0x20;
            short size = BitConverter.ToInt16(d, simi + 12 + 23 * 2);
            // after the globals: short, 5 ints, then lot, objects and architecture values
            int after = simi + 12 + items * 2 + 2 + 4 * 5;
            if (after + 12 <= simi + simiLen) archValue = BitConverter.ToInt32(d, after + 8);
            short cameraDir = BitConverter.ToInt16(d, hous + 28);
            var hash = System.Security.Cryptography.SHA1.HashData(new ReadOnlySpan<byte>(d, flags, flagsLen));
            return size + "/" + (cameraDir & 1) + "/" + Convert.ToHexString(hash);
        }

        /// <summary>A copied lot has to know its own house number (it's saved under that number).</summary>
        private static bool SetHouseNumber(byte[] d, short house)
        {
            if (!FindChunk(d, "SIMI", 1, out var simi, out var len) || len < 12 + 22) return false;
            var b = BitConverter.GetBytes(house);
            d[simi + 12 + 10 * 2] = b[0];
            d[simi + 12 + 10 * 2 + 1] = b[1];
            return true;
        }

        private static void CopyFolder(string from, string to)
        {
            from = from.Replace('\\', '/');
            if (!from.EndsWith("/")) from += "/";
            FSODirectory.CreateDirectory(to);
            foreach (var f in FSODirectory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(from, f).Replace('\\', '/');
                if (rel.StartsWith("..")) continue;
                var target = Path.Combine(to, rel);
                FSODirectory.CreateDirectory(Path.GetDirectoryName(target));
                FSOFile.Copy(f, target, true);
            }
        }
    }
}
