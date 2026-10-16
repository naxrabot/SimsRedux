using FSO.Common.IO;
using FSO.Content;
using FSO.Content.TS1;
using FSO.SimAntics.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: fills a neighbourhood with more people. Sims 1 only ships a handful of families, so
    /// townies and walk-bys kept being the same few faces. The first time a neighbourhood is opened
    /// (single player), or when the server sets up its town (online), this makes a batch of random
    /// families and single Sims the same way Create-A-Family does (random heads, bodies, skin, names
    /// and personalities).
    ///
    /// They're background Sims, like the Goths: never in the family bin and never one of the player's
    /// families. The families move into houses that are for sale (a few are always left for the
    /// player), and the singles are townies. All of them are out and about - on community lots and
    /// walking past your home.
    ///
    /// It runs once per neighbourhood (a "redux_population.txt" file in the neighbourhood folder
    /// remembers it, and which families it made). Off with the setting "populate" = false.
    /// </summary>
    public static class ReduxPopulation
    {
        private const string MarkerFile = "redux_population.txt";
        /// <summary>Written into the marker by this version (5.4 put its families in the family bin; they're moved out of it).</summary>
        private const string Version = "v2";

        private static HashSet<int> Generated;
        private static List<string> TexNames;
        private static string GeneratedFor;

        private static readonly string[] MaleNames =
        {
            "Adam", "Ben", "Carl", "Dale", "Eddie", "Frank", "Gus", "Hank", "Ivan", "Jack", "Kurt", "Leo",
            "Marcus", "Nate", "Oscar", "Pete", "Ray", "Sam", "Ted", "Vince", "Wes", "Zack", "Derek", "Malik",
            "Rico", "Theo", "Luis", "Owen", "Jamal", "Felix",
        };
        private static readonly string[] FemaleNames =
        {
            "Alice", "Bea", "Cara", "Dana", "Ella", "Faye", "Gina", "Holly", "Iris", "Jade", "Kim", "Lola",
            "Maya", "Nina", "Opal", "Paula", "Rosa", "Sara", "Tina", "Vera", "Wendy", "Zoe", "Keisha", "Lena",
            "Mona", "Rita", "Talia", "Ruby", "Nadia", "Joy",
        };
        private static readonly string[] Surnames =
        {
            "Abbott", "Barker", "Castillo", "Dawson", "Ellis", "Fischer", "Garcia", "Hale", "Ingram", "Jensen",
            "Kowalski", "Lambert", "Moreno", "Nakamura", "Okafor", "Parker", "Quinn", "Reyes", "Sullivan",
            "Thornton", "Underwood", "Vance", "Whitaker", "Young", "Zimmer", "Brooks", "Rivera", "Holloway",
        };
        private static readonly string[] Skins = { "lgt", "med", "drk" };

        private static string Marker
        {
            get
            {
                var path = Content.Get().Neighborhood?.UserPath;
                return string.IsNullOrEmpty(path) ? null : Path.Combine(path, MarkerFile);
            }
        }

        /// <summary>Did this neighbourhood get this family from here (so they're out and about until they move in)?</summary>
        public static bool IsGenerated(int familyChunkID)
        {
            var marker = Marker;
            if (marker == null) return false;
            if (GeneratedFor != marker)
            {
                GeneratedFor = marker;
                Generated = new HashSet<int>();
                try
                {
                    if (FSOFile.Exists(marker))
                    {
                        foreach (var line in FSOFile.ReadAllLines(marker))
                        {
                            int id;
                            if (line.StartsWith("family ") && int.TryParse(line.Substring(7).Trim(), out id)) Generated.Add(id);
                        }
                    }
                }
                catch { }
            }
            return Generated.Contains(familyChunkID);
        }

        /// <summary>
        /// Makes the extra Sims once (or brings 5.4's ones up to date). families/singles: how many;
        /// keepFree: how many houses for sale are always left empty for players. Returns a line about
        /// what it did (or null if there was nothing to do).
        /// </summary>
        public static string EnsurePopulated(int families = 8, int singles = 8, int keepFree = 2, HashSet<int> emptyHomesExcept = null)
        {
            try
            {
                if (!ReduxSettings.GetBool("populate", true)) return null;
                var marker = Marker;
                if (marker == null) return null;
                var neigh = Content.Get().Neighborhood;
                var rand = new Random();
                bool emptyHomes = emptyHomesExcept != null;
                if (FSOFile.Exists(marker))
                {
                    var text = FSOFile.ReadAllText(marker);
                    if (text.Contains(Version) && emptyHomes && !text.Contains(EmptyHomesMark))
                    {
                        // the online town: everyone moves out of the houses (players' families stay)
                        int evicted = EmptyHomes(neigh, emptyHomesExcept);
                        neigh.SaveNeighbourhood(true);
                        FSOFile.AppendAllText(marker, EmptyHomesMark + "\r\n");
                        return evicted + " families moved out of their houses - every home is for sale, and they're townies now";
                    }
                    if (text.Contains(Version)) return null;
                    // made by 5.4: those families were put in the family bin - they become background Sims
                    GeneratedFor = null;
                    var old = new List<FSO.Files.Formats.IFF.Chunks.FAMI>();
                    foreach (var f in neigh.MainResource.List<FSO.Files.Formats.IFF.Chunks.FAMI>() ?? new List<FSO.Files.Formats.IFF.Chunks.FAMI>())
                        if (IsGenerated(f.ChunkID)) old.Add(f);
                    foreach (var f in old) f.Unknown = 0;
                    int moved = 0, out1 = 0;
                    if (emptyHomes) out1 = EmptyHomes(neigh, emptyHomesExcept);
                    else moved = MoveIntoHouses(neigh, old, keepFree, rand);
                    neigh.SaveNeighbourhood(true);
                    FSOFile.AppendAllText(marker, Version + "\r\n" + (emptyHomes ? EmptyHomesMark + "\r\n" : ""));
                    return emptyHomes ? "the town's extra Sims are townies now, and " + out1 + " families moved out of their houses"
                        : "the town's extra Sims are background Sims now (" + moved + " families moved into houses)";
                }

                // written first: if anything goes wrong part way, it's never made twice
                FSOFile.WriteAllText(marker, "SimsRedux: extra Sims made " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\r\n" + Version + "\r\n" + (emptyHomes ? EmptyHomesMark + "\r\n" : ""));
                var surnames = Surnames.OrderBy(x => rand.Next()).ToList();
                var made = new List<FSO.Files.Formats.IFF.Chunks.FAMI>();
                int sims = 0;
                for (int i = 0; i < families + singles && i < surnames.Count; i++)
                {
                    bool single = i >= families;
                    try
                    {
                        var fam = MakeFamily(rand, surnames[i], single, marker);
                        if (fam != null) { made.Add(fam); sims += fam.FamilyGUIDs?.Length ?? 0; }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[SimsRedux] population: couldn't make the " + surnames[i] + " family: " + e.Message);
                    }
                }
                // background Sims: not waiting in the family bin, not one of the player's families
                foreach (var f in made) f.Unknown = 0;
                int housed = 0, evictedNew = 0;
                if (emptyHomes) evictedNew = EmptyHomes(neigh, emptyHomesExcept);
                else housed = MoveIntoHouses(neigh, made, keepFree, rand);
                neigh.SaveNeighbourhood(true);
                GeneratedFor = null; // re-read the list
                TexNames = null;
                var line = "made " + sims + " new Sims in " + made.Count + " households"
                    + (emptyHomes ? " (all townies; " + evictedNew + " families moved out so every home is for sale)" : " (" + housed + " moved into houses, the rest are townies)");
                Console.WriteLine("[SimsRedux] population: " + line);
                return line;
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] population failed: " + e.Message);
                return "couldn't make the extra Sims: " + e.Message;
            }
        }

        private const string EmptyHomesMark = "emptyhomes";

        /// <summary>
        /// The online town: every family living in a house moves out (the house goes up for sale, things and
        /// all) and becomes townies - except the families players own. Returns how many moved out.
        /// </summary>
        private static int EmptyHomes(TS1NeighborhoodProvider neigh, HashSet<int> keep)
        {
            int moved = 0;
            foreach (var fam in (neigh.MainResource.List<FSO.Files.Formats.IFF.Chunks.FAMI>() ?? new List<FSO.Files.Formats.IFF.Chunks.FAMI>()).ToList())
            {
                if (fam.HouseNumber <= 0 || keep.Contains(fam.ChunkID)) continue;
                var house = (short)fam.HouseNumber;
                try
                {
                    neigh.MoveOut(house);
                    fam.Unknown = 0; // a townie family now: out and about, not waiting in the family bin
                    moved++;
                    Console.WriteLine("[SimsRedux] population: family " + fam.ChunkID + " moved out of house " + house);
                }
                catch (Exception e) { Console.WriteLine("[SimsRedux] population: couldn't move family " + fam.ChunkID + " out: " + e.Message); }
            }
            return moved;
        }

        /// <summary>
        /// Families (two or more Sims) move into houses that are for sale, leaving keepFree of them empty.
        /// Singles stay townies. Returns how many moved in.
        /// </summary>
        private static int MoveIntoHouses(TS1NeighborhoodProvider neigh, List<FSO.Files.Formats.IFF.Chunks.FAMI> fams, int keepFree, Random rand)
        {
            var forSale = new List<short>();
            foreach (var h in ReduxFreshHood.MainLots(neigh))
            {
                if (!ReduxFreshHood.Residential(neigh, h) || neigh.GetFamilyForHouse(h) != null) continue;
                var simi = ReduxFreshHood.SimiOf(neigh, h);
                if (simi == null || simi.ArchitectureValue <= 0) continue; // an empty lot, not a home
                forSale.Add(h);
            }
            forSale = forSale.OrderBy(x => rand.Next()).ToList();
            int canUse = Math.Max(0, forSale.Count - keepFree);
            int moved = 0;
            foreach (var fam in fams.OrderByDescending(x => x.FamilyGUIDs?.Length ?? 0))
            {
                if (moved >= canUse) break;
                if (fam.HouseNumber > 0 || (fam.FamilyGUIDs?.Length ?? 0) < 2) continue;
                var house = forSale[moved];
                neigh.SetFamilyForHouse(house, fam, false);
                fam.Unknown = 1; // a family living in the neighbourhood (like the game's own)
                fam.Budget = 8000 + rand.Next(20) * 1000;
                moved++;
                Console.WriteLine("[SimsRedux] population: family " + fam.ChunkID + " moved into house " + house);
            }
            return moved;
        }

        private static FSO.Files.Formats.IFF.Chunks.FAMI MakeFamily(Random rand, string surname, bool single, string marker)
        {
            var skin = Skins[rand.Next(Skins.Length)];
            var members = new List<SimTemplateCreateInfo>();
            if (single)
            {
                members.Add(MakeSim(rand, rand.Next(2) == 0, false, skin, surname));
            }
            else
            {
                // a couple, a parent with kids, or roommates - with up to two children
                int shape = rand.Next(10);
                if (shape < 6)
                {
                    bool firstMale = rand.Next(2) == 0;
                    members.Add(MakeSim(rand, firstMale, false, skin, surname));
                    members.Add(MakeSim(rand, rand.Next(10) < 8 ? !firstMale : firstMale, false, rand.Next(4) == 0 ? Skins[rand.Next(3)] : skin, surname));
                }
                else if (shape < 8)
                {
                    members.Add(MakeSim(rand, rand.Next(2) == 0, false, skin, surname));
                }
                else
                {
                    for (int r = 0; r < 3; r++) members.Add(MakeSim(rand, rand.Next(2) == 0, false, Skins[rand.Next(3)], surname));
                }
                int kids = (shape < 8) ? rand.Next(shape < 6 ? 3 : 2) + (shape >= 6 ? 1 : 0) : 0;
                for (int k = 0; k < kids; k++) members.Add(MakeSim(rand, rand.Next(2) == 0, true, skin, surname));
            }
            members.RemoveAll(x => x == null);
            if (members.Count == 0) return null;

            var fam = SimitoneNeighbourGenerator.CreateFamily(surname, members.Count, members.ToArray());
            if (fam == null) return null;
            FSOFile.AppendAllText(marker, "family " + fam.ChunkID + "\r\n");
            return fam;
        }

        /// <summary>One random Sim, the way Create-A-Sim would make them (null if no clothes fit).</summary>
        private static SimTemplateCreateInfo MakeSim(Random rand, bool male, bool child, string skin, string surname)
        {
            var type = (male ? "m" : "f") + (child ? "c" : "a");
            var bcf = Content.Get().BCFGlobal;
            List<string> heads;
            if (!bcf.CollectionsByName["c"].ClothesByAvatarType.TryGetValue(type, out heads) || heads == null) return null;
            var bodies = bcf.CollectionsByName["b"].GeneralAvatarType(child ? type + "chd" : type);
            var texnames = TexNames ?? (TexNames = ((TS1AvatarTextureProvider)Content.Get().AvatarTextures).GetAllNames().ToList());

            var headChoices = new List<KeyValuePair<string, string>>();
            foreach (var h in heads)
            {
                var tex = texnames.FirstOrDefault(y => y.StartsWith(ExtractID(h, skin)));
                if (tex != null) headChoices.Add(new KeyValuePair<string, string>(h, RemoveExt(tex)));
            }
            var bodyChoices = new List<KeyValuePair<string, string>>();
            foreach (var b in bodies)
            {
                var tex = texnames.FirstOrDefault(y => y.StartsWith(ExtractID(b, skin)));
                if (tex != null) bodyChoices.Add(new KeyValuePair<string, string>(b, RemoveExt(tex)));
            }
            if (headChoices.Count == 0 || bodyChoices.Count == 0) return null;
            var head = headChoices[rand.Next(headChoices.Count)];
            var body = bodyChoices[rand.Next(bodyChoices.Count)];
            var hand = RemoveExt(texnames.FirstOrDefault(y => RemoveExt(y) == "huao" + FindHG(body.Value))) ?? ("huao" + skin);
            var handTex = hand.Length > 4 ? hand.Substring(4) : skin;

            var ind = body.Key.IndexOf("_");
            if (ind < 3) return null;
            var code = type + body.Key.Substring(ind - 3, 3);
            var info = new SimTemplateCreateInfo(code, skin);
            var first = male ? MaleNames[rand.Next(MaleNames.Length)] : FemaleNames[rand.Next(FemaleNames.Length)];
            info.Name = first;
            info.Bio = first + " " + surname + " is new in town.";
            info.PersonalityPoints = RandomPersonality(rand);

            info.BodyStringReplace[1] = body.Key + ",BODY=" + body.Value;
            info.BodyStringReplace[2] = head.Key + ",HEAD-HEAD=" + head.Value;
            var h2 = child ? "u" : (male ? "m" : "f");
            info.BodyStringReplace[17] = "H" + h2 + "LO,HAND=huao" + handTex;
            info.BodyStringReplace[18] = "H" + h2 + "RO,HAND=huao" + handTex;
            info.BodyStringReplace[19] = "H" + h2 + "LP,HAND=huao" + handTex;
            info.BodyStringReplace[20] = "H" + h2 + "RP,HAND=huao" + handTex;
            info.BodyStringReplace[21] = "H" + h2 + "LO,HAND=huao" + handTex;
            info.BodyStringReplace[22] = "H" + h2 + "RC,HAND=huao" + handTex;
            return info;
        }

        /// <summary>25 personality points over the five traits (0-10 each), like Create-A-Sim.</summary>
        private static short[] RandomPersonality(Random rand)
        {
            var pts = new int[5];
            int left = 25;
            while (left > 0)
            {
                int t = rand.Next(5);
                if (pts[t] >= 10) continue;
                pts[t]++;
                left--;
            }
            return pts.Select(x => (short)(x * 100)).ToArray();
        }

        private static string FindHG(string item)
        {
            var ind = item.IndexOf('_');
            return ind != -1 ? item.Substring(ind) : item;
        }

        private static string RemoveExt(string item)
        {
            if (item == null) return null;
            var ind = item.LastIndexOf('.');
            return ind != -1 ? item.Substring(0, ind) : item;
        }

        private static string ExtractID(string item, string skin)
        {
            var ind = item.IndexOf('_');
            if (ind != -1) item = item.Substring(0, ind);
            return item + skin;
        }
    }
}
