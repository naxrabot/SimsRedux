using FSO.Common.IO;
using FSO.Content;
using FSO.Content.TS1;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux online (2C): one of the player's single-player families, packed up to move into the
    /// online town. The family is copied - the single-player family stays where it is.
    ///
    /// "SRXF", int version, string family name, int budget, int count,
    /// then per Sim: uint guid, int file length, the character file, int count, short[] person data.
    /// </summary>
    public class ReduxFamilyPackage
    {
        public class Member
        {
            public uint Guid;
            public string Name = "";
            public byte[] File;
            public short[] PersonData;
        }

        public string Name = "";
        public int Budget;
        /// <summary>Every family starts the online town with this much (made there or brought from single player).</summary>
        public const int OnlineStartingMoney = 15000;
        public List<Member> Members = new List<Member>();

        // ------------------------------------------------------------------
        // Packing (the player's game, from their own neighbourhood)
        // ------------------------------------------------------------------

        /// <summary>The player's own families in the neighbourhood that's loaded now (made by them or taken over).</summary>
        public static List<ReduxFamilyPackage> FromCurrentNeighbourhood()
        {
            var list = new List<ReduxFamilyPackage>();
            var neigh = Content.Get().Neighborhood;
            foreach (var fam in neigh.MainResource.List<FAMI>() ?? new List<FAMI>())
            {
                if (fam.FamilyGUIDs == null || fam.FamilyGUIDs.Length == 0 || !ReduxRoster.IsPlayers(fam)) continue;
                try
                {
                    var p = Pack(fam);
                    if (p != null && p.Members.Count > 0) list.Add(p);
                }
                catch (Exception e) { Console.WriteLine("[SimsRedux] Couldn't pack family " + fam.ChunkID + ": " + e.Message); }
            }
            return list;
        }

        public static ReduxFamilyPackage Pack(FAMI fam)
        {
            var neigh = Content.Get().Neighborhood;
            var objs = Content.Get().WorldObjects as TS1ObjectProvider;
            var p = new ReduxFamilyPackage
            {
                Name = neigh.MainResource.Get<FAMs>(fam.ChunkID)?.GetString(0) ?? "",
                Budget = fam.Budget,
            };
            foreach (var guid in fam.FamilyGUIDs)
            {
                if (guid == 0 || objs == null || !objs.Entries.TryGetValue(guid, out var entry) || entry.FileName == null || !FSOFile.Exists(entry.FileName)) continue;
                var nid = neigh.GetNeighborIDForGUID(guid);
                var nb = (nid == null) ? null : neigh.GetNeighborByID(nid.Value);
                string simName = "";
                try
                {
                    var obj = Content.Get().WorldObjects.Get(guid);
                    simName = obj?.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0) ?? obj?.OBJ.ChunkLabel ?? "";
                }
                catch { }
                p.Members.Add(new Member
                {
                    Guid = guid,
                    Name = simName,
                    File = FSOFile.ReadAllBytes(entry.FileName),
                    PersonData = (short[])(nb?.PersonData ?? new short[0]).Clone(),
                });
            }
            return p;
        }

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                {
                    w.Write(Encoding.ASCII.GetBytes("SRXF"));
                    w.Write(1);
                    w.Write(Name ?? "");
                    w.Write(Budget);
                    w.Write(Members.Count);
                    foreach (var m in Members)
                    {
                        w.Write(m.Guid);
                        w.Write(m.File.Length);
                        w.Write(m.File);
                        w.Write(m.PersonData.Length);
                        foreach (var v in m.PersonData) w.Write(v);
                    }
                }
                return ms.ToArray();
            }
        }

        public static ReduxFamilyPackage FromBytes(byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "SRXF") throw new Exception("not a family");
                if (r.ReadInt32() != 1) throw new Exception("a family from a newer version of the game");
                var p = new ReduxFamilyPackage { Name = r.ReadString(), Budget = r.ReadInt32() };
                int count = r.ReadInt32();
                if (count < 1 || count > 8) throw new Exception("a family has 1 to 8 Sims");
                for (int i = 0; i < count; i++)
                {
                    var m = new Member { Guid = r.ReadUInt32() };
                    int len = r.ReadInt32();
                    if (len <= 0 || len > 8 * 1024 * 1024) throw new Exception("a Sim's file is damaged");
                    m.File = r.ReadBytes(len);
                    int pd = r.ReadInt32();
                    if (pd < 0 || pd > 1024) throw new Exception("a Sim's details are damaged");
                    m.PersonData = new short[pd];
                    for (int j = 0; j < pd; j++) m.PersonData[j] = r.ReadInt16();
                    p.Members.Add(m);
                }
                return p;
            }
        }

        // ------------------------------------------------------------------
        // Moving in (the lot worker, in the server's town)
        // ------------------------------------------------------------------

        /// <summary>
        /// Adds the family to the town that's loaded now and moves them into the house (paying for it
        /// from their budget). Returns the new family, or throws with a reason.
        /// </summary>
        public FAMI MoveInto(short house)
        {
            var neigh = Content.Get().Neighborhood;
            if (neigh.GetFamilyForHouse(house) != null) throw new Exception("someone already lives there");
            if (neigh.IsCommunity(house)) throw new Exception("that's a community lot");
            Budget = OnlineStartingMoney; // everyone starts the town with the same money
            var price = neigh.GetHouse(house)?.Get<SIMI>(1)?.PurchaseValue ?? 0;
            if (price > Budget) throw new Exception("the family has §" + Budget + " but the lot costs §" + price);

            var families = (neigh.MainResource.List<FAMI>() ?? new List<FAMI>()).OrderBy(x => x.ChunkID).ToList();
            ushort id = 0;
            foreach (var f in families) { if (f.ChunkID == id) id++; else if (f.ChunkID > id) break; }

            // every Sim's character file goes into the town (with a new id if one is already in use there)
            var folder = Path.Combine(neigh.UserPath, "Characters/");
            FSODirectory.CreateDirectory(folder);
            var guids = new List<uint>();
            var objs = Content.Get().WorldObjects as TS1ObjectProvider;
            foreach (var m in Members)
            {
                var guid = m.Guid;
                var file = m.File;
                if ((objs != null && objs.Entries.ContainsKey(guid)) || neigh.GetNeighborIDForGUID(guid) != null || guids.Contains(guid))
                {
                    var newGuid = FreeGuid(guids);
                    var temp = Path.Combine(folder, "Incoming" + guid.ToString("X8") + ".iff");
                    FSOFile.WriteAllBytes(temp, file);
                    file = ReduxHost.Reguid(temp, guid, newGuid);
                    try { FSOFile.Delete(temp); } catch { }
                    if (file == null) throw new Exception("a Sim couldn't be added to the town");
                    guid = newGuid;
                }
                var path = Path.Combine(folder, "Online" + guid.ToString("X8") + ".iff");
                FSOFile.WriteAllBytes(path, file);
                if (!neigh.RegisterCharacterFile(path).Contains(guid)) throw new Exception("a Sim's file couldn't be read");
                guids.Add(guid);

                var pd = (short[])m.PersonData.Clone();
                if (pd.Length > (int)VMPersonDataVariable.TS1FamilyNumber) pd[(int)VMPersonDataVariable.TS1FamilyNumber] = (short)id;
                var nb = FSO.SimAntics.Utils.SimitoneNeighbourGenerator.AddNeighbor(guid, 9, pd);
                if (pd.Length > (int)VMPersonDataVariable.NeighborId) pd[(int)VMPersonDataVariable.NeighborId] = nb.NeighbourID;
            }

            var fam = new FAMI()
            {
                ChunkLabel = "",
                ChunkID = id,
                ChunkProcessed = true,
                ChunkType = "FAMI",
                ChunkParent = neigh.MainResource,
                AddedByPatch = true,
                FamilyGUIDs = guids.ToArray(),
                FamilyNumber = (families.Count == 0 ? 0 : families.Max(x => x.FamilyNumber)) + 1,
                Unknown = 24,
                Budget = Budget,
            };
            neigh.MainResource.AddChunk(fam);
            var fams = new FAMs()
            {
                ChunkLabel = "",
                ChunkID = id,
                ChunkProcessed = true,
                ChunkType = "FAMs",
                ChunkParent = neigh.MainResource,
                AddedByPatch = true,
            };
            fams.InsertString(0, new STRItem() { Comment = "", Value = string.IsNullOrWhiteSpace(Name) ? "Online" : Name });
            neigh.MainResource.AddChunk(fams);

            neigh.SetFamilyForHouse(house, fam, true);
            neigh.SaveNeighbourhood(true);
            return fam;
        }

        private static uint FreeGuid(List<uint> avoid)
        {
            var objs = Content.Get().WorldObjects as TS1ObjectProvider;
            var neigh = Content.Get().Neighborhood;
            var rand = new Random();
            while (true)
            {
                var g = (uint)rand.Next(0x10000000, int.MaxValue);
                if (avoid.Contains(g) || neigh.GetNeighborIDForGUID(g) != null) continue;
                if (objs != null && objs.Entries.ContainsKey(g)) continue;
                return g;
            }
        }
    }
}
