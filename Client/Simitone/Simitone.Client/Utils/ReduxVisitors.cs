using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: townies and walk-bys, in single player and on the server's online lots.
    ///
    /// Sims 1's own lot scripts bring visitors in, and this never replaces them: it only tops a lot
    /// up when it has fewer visitors than a Sims 1 lot of that kind would have at that time of day,
    /// counting every visitor on the lot (whoever brought them). The Sims come from the game's own
    /// neighbour list (townies, the extra Sims ReduxPopulation made, and the neighbourhood's families -
    /// never the service NPCs or the family playing here), and leave with the game's own global
    /// "Leave" routine (they walk to one of the lot's portals and go).
    ///
    /// - Community lots (Downtown, Vacation Island, Studio Town, Magic Town and the neighbourhood's own):
    ///   a few townies are already there when you arrive, more arrive from the street during opening
    ///   hours (8 AM to 2 AM), and they head home after a while.
    /// - Home lots: now and then (7 AM to 9 PM) a neighbour or townie walks by on the sidewalk. Greet them
    ///   and they stay a while; ignore them and they move on.
    ///
    /// Every change goes through VMNetReduxTownieCmd, so on an online lot all the players' games make
    /// the same townie arrive at the same moment. Only the game running the lot decides (single
    /// player, or the server's lot) - a player visiting someone else's lot never does.
    /// Off with the setting "townies" = false.
    /// </summary>
    public static class ReduxVisitors
    {
        private class Visitor
        {
            public long LeaveAt;
            public long LeavePushed = -1;
            public long AskedAt;
            public bool WalkBy;
        }

        // the portals Sims 1's own "find random portal" (global 373) sends leaving visitors to
        private static readonly HashSet<uint> PortalGUIDs = new HashSet<uint>
        {
            0x81E6BEF9, 0x8A75FD8E, 0x8DC6AD13, 0x8DA0E519, 0xA7247E8F, 0xBC17335C,
            0x02171B7B, 0x7465CB76, 0xE4E76CDE, 0x12F818BE, 0x12E84823, 0x12C90029
        };
        private static readonly HashSet<uint> MailboxGUIDs = new HashSet<uint> { 0xEF121974, 0x1D95C9B0 };

        private const int MaxPeopleCommunity = 14;
        private const int MaxPeopleHome = 12;

        private static VM LastVM;
        /// <summary>The townies this brought in, by their GUID (the lot gives them an object id when they arrive).</summary>
        private static Dictionary<uint, Visitor> Mine = new Dictionary<uint, Visitor>();
        private static long FirstMinute = -1;
        private static long NextCheck;
        private static bool Seeded;
        private static int Extra; // this visit's own +-1 on how busy a community lot is
        private static bool Enabled = true;
        private static long LastFix = -1;
        /// <summary>Choices made outside the lot's own simulation (never the lot's random numbers: online, every game shares those).</summary>
        private static readonly Random Rand = new Random();

        private static long GameMinute(VM vm)
        {
            var clock = vm.Context.Clock;
            return clock.Ticks / Math.Max(1, clock.TicksPerMinute);
        }

        /// <summary>This game runs the lot: single player, or hosting it (the server's lot, or a friend hosting).</summary>
        private static bool RunsTheLot(TS1GameScreen screen)
        {
            return !screen.IsGuest && !ReduxTown.Active;
        }

        private static bool Online(TS1GameScreen screen)
        {
            return screen.Host != null || ReduxWorker.Active;
        }

        private static void Send(VM vm, VMNetReduxTownieCmd cmd)
        {
            vm.SendCommand(cmd);
        }

        public static void Tick(TS1GameScreen screen)
        {
            var vm = screen?.vm;
            if (vm == null || !vm.TS1 || vm.Context?.Architecture == null)
            {
                LastVM = null;
                return;
            }
            if (vm != LastVM)
            {
                LastVM = vm;
                Mine = new Dictionary<uint, Visitor>();
                FirstMinute = -1;
                NextCheck = 0;
                Seeded = false;
                Extra = 0;
                LastFix = -1;
                Enabled = ReduxSettings.GetBool("townies", true);
            }
            if (!vm.Ready || vm.SpeedMultiplier <= 0 || !RunsTheLot(screen)) return;
            bool online = Online(screen);
            // single player: only while a family is playing here (not the lot's build-only view)
            if (!online && (vm.TS1State?.CurrentFamily == null || vm.GetGlobalValue(32) != 0)) return;
            // online: only while someone is on the lot (the server's lot waits empty for a little while before closing)
            if (online && (screen.Host == null || screen.Host.GuestCount == 0)) return;

            var now = GameMinute(vm);
            if (now != LastFix)
            {
                LastFix = now;
                try { FixPeople(vm); } catch (Exception e) { Console.WriteLine("[SimsRedux] townies (fix): " + e.Message); }
                try { RemoveTrolleys(vm); } catch (Exception e) { Console.WriteLine("[SimsRedux] trolleys: " + e.Message); }
            }
            if (!Enabled) return;
            if (FirstMinute < 0) FirstMinute = now;
            try
            {
                if (!Seeded)
                {
                    // give the lot's own scripts a moment first (the family walks in, the game's own visitors appear)
                    if (now - FirstMinute < 3) return;
                    Seeded = true;
                    Extra = Rand.Next(3) - 1;
                    AdoptLeftovers(vm, now);
                    Seed(vm, now);
                    NextCheck = now + 15;
                    return;
                }
                if (now < NextCheck) return;
                NextCheck = now + 15;
                SendHome(vm, now);
                TopUp(vm, now);
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] townies: " + e.Message);
                NextCheck = now + 60;
            }
        }

        // ------------------------------------------------------------------
        // What kind of lot, and how busy it should be
        // ------------------------------------------------------------------

        private static bool IsCommunity(VM vm)
        {
            var house = vm.GetGlobalValue(10);
            // Downtown, Vacation Island, Studio Town, Magic Town
            if ((house >= 21 && house <= 31) || (house >= 40 && house <= 49) || (house >= 81 && house <= 99)) return true;
            return Content.Get().Neighborhood.IsCommunity(house);
        }

        /// <summary>How many visitors a community lot has at this hour (Sims 1: busiest in the evening, shut at night).</summary>
        private static int CommunityTarget(int hour)
        {
            if (hour >= 8 && hour < 12) return 2 + Extra;
            if (hour >= 12 && hour < 18) return 3 + Extra;
            if (hour >= 18) return 4 + Extra;
            if (hour < 2) return 2 + Extra;
            return 0; // 2 AM to 8 AM
        }

        private static bool WalkByHours(int hour)
        {
            return hour >= 7 && hour < 21;
        }

        private static bool IsFamily(VM vm, VMAvatar ava)
        {
            var fam = vm.TS1State?.CurrentFamily;
            if (fam == null) return false;
            if (ava.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) == fam.ChunkID) return true;
            return fam.FamilyGUIDs != null && fam.FamilyGUIDs.Contains(ava.Object.OBJ.GUID);
        }

        private static bool IsTraveller(VM vm, VMAvatar ava)
        {
            var subset = vm.TS1State?.CurrentFamily?.RuntimeSubset;
            return subset != null && subset.Contains(ava.Object.OBJ.GUID);
        }

        /// <summary>A player's own Sim on an online lot (they're looked after by the players, not by this).</summary>
        private static bool IsPlayers(VMAvatar ava)
        {
            return ava.PersistID >= ReduxOnlineInfo.FirstGuestID;
        }

        private static List<VMAvatar> People(VM vm)
        {
            return vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>()
                .Where(x => !x.Dead && !x.IsPet).ToList();
        }

        private static VMAvatar Find(VM vm, uint guid)
        {
            return vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().FirstOrDefault(x => !x.Dead && x.Object.OBJ.GUID == guid);
        }

        /// <summary>Visitors on the lot right now, whoever brought them (the game's own scripts included).</summary>
        private static List<VMAvatar> Visitors(VM vm, List<VMAvatar> people)
        {
            return people.Where(x => x.Position != LotTilePos.OUT_OF_WORLD && !IsPlayers(x)
                && x.GetPersonData(VMPersonDataVariable.PersonType) == 1 && !IsFamily(vm, x)).ToList();
        }

        /// <summary>
        /// Your own Sims always count as family (never visitors), on every lot. On a community lot everyone
        /// else gets greet status 1, which is what Hot Date's social menu needs there (anything else: no
        /// menu) - townies, other players' Sims and the shop staff (person type 2) alike.
        /// </summary>
        private static void FixPeople(VM vm)
        {
            bool community = IsCommunity(vm);
            foreach (var ava in People(vm))
            {
                if (ava.Position == LotTilePos.OUT_OF_WORLD) continue;
                var type = ava.GetPersonData(VMPersonDataVariable.PersonType);
                if (IsFamily(vm, ava) || IsTraveller(vm, ava))
                {
                    // (a trip downtown could leave them marked as a visitor, and a visitor can't talk to other visitors)
                    if (type == 1) Send(vm, new VMNetReduxTownieCmd { Mode = VMNetReduxTownieCmd.TownieMode.SetPerson, ObjectID = ava.ObjectID, PersonType = 0 });
                    continue;
                }
                if (community && (type == 1 || type == 2) && ava.GetPersonData(VMPersonDataVariable.GreetStatus) != 1)
                    Send(vm, new VMNetReduxTownieCmd { Mode = VMNetReduxTownieCmd.TownieMode.SetPerson, ObjectID = ava.ObjectID, Greet = 1 });
            }
        }

        /// <summary>
        /// Unleashed's Old Town trolley kept pulling up (and piling up) in front of every lot. It isn't
        /// used for anything here, so any that turn up are taken away.
        /// </summary>
        private static void RemoveTrolleys(VM vm)
        {
            foreach (var ent in vm.Entities.ToList())
            {
                if (ent == null || ent.Dead || ent is VMAvatar) continue;
                var label = ent.Object?.OBJ?.ChunkLabel ?? "";
                if (label.IndexOf("trolley", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Console.WriteLine("[SimsRedux] removing a trolley (" + label + ")");
                Send(vm, new VMNetReduxTownieCmd { Mode = VMNetReduxTownieCmd.TownieMode.Remove, ObjectID = ent.ObjectID });
            }
        }

        // ------------------------------------------------------------------
        // Arriving
        // ------------------------------------------------------------------

        /// <summary>When you arrive: a community lot already has some townies on it.</summary>
        private static void Seed(VM vm, long now)
        {
            var hour = vm.Context.Clock.Hours;
            var people = People(vm);
            if (IsCommunity(vm))
            {
                int want = CommunityTarget(hour) - Visitors(vm, people).Count;
                int room = MaxPeopleCommunity - people.Count;
                int sent = 0;
                for (int i = 0; i < Math.Min(want, room); i++)
                    if (Spawn(vm, now, true, false)) sent++;
                if (sent > 0) Console.WriteLine("[SimsRedux] townies: " + sent + " already here on arrival");
            }
            else if (WalkByHours(hour) && Rand.Next(2) == 0 && people.Count < MaxPeopleHome)
            {
                Spawn(vm, now, false, true);
            }
        }

        /// <summary>Every quarter hour: more townies come in from the street, or someone walks by.</summary>
        private static void TopUp(VM vm, long now)
        {
            var hour = vm.Context.Clock.Hours;
            var people = People(vm);
            var visitors = Visitors(vm, people);
            int pending = Mine.Count(x => Find(vm, x.Key) == null);
            if (IsCommunity(vm))
            {
                if (people.Count + pending >= MaxPeopleCommunity) return;
                if (visitors.Count + pending >= CommunityTarget(hour)) return;
                Spawn(vm, now, false, false); // one at a time, walking in from the street
            }
            else
            {
                if (!WalkByHours(hour) || people.Count + pending >= MaxPeopleHome || visitors.Count >= 4) return;
                // one stranger on the sidewalk at a time, now and then
                if (Mine.Values.Any(x => x.WalkBy)) return;
                // (nor while a stranger the game itself brought is still about)
                if (visitors.Any(x => x.GetPersonData(VMPersonDataVariable.GreetStatus) < 2)) return;
                if (Rand.Next(4) != 0) return;
                Spawn(vm, now, false, true);
            }
        }

        /// <summary>
        /// Brings one Sim from the game's own neighbour list onto the lot. inside = placed among the lot's
        /// things (already there on arrival); otherwise at one of the lot's portals (the street).
        /// </summary>
        private static bool Spawn(VM vm, long now, bool inside, bool walkBy)
        {
            var hour = vm.Context.Clock.Hours;
            var neigh = Content.Get().Neighborhood;
            var entries = neigh.Neighbors?.Entries;
            if (entries == null) return false;
            var objs = Content.Get().WorldObjects;

            var onLot = new HashSet<uint>(vm.Context.ObjectQueries.Avatars.Select(x => x.Object.OBJ.GUID));
            var fam = vm.TS1State?.CurrentFamily;
            var famGUIDs = new HashSet<uint>(fam?.FamilyGUIDs ?? new uint[0]);
            var playerFamilies = PlayerFamilies(vm);

            var townies = new List<Neighbour>();
            var neighbours = new List<Neighbour>();
            foreach (var n in entries)
            {
                if (n == null || n.GUID == 0 || n.PersonData == null) continue;
                if (onLot.Contains(n.GUID) || famGUIDs.Contains(n.GUID) || Mine.ContainsKey(n.GUID)) continue;
                if ((Data(n, VMPersonDataVariable.Gender) & (8 | 16)) != 0) continue; // pets
                if (Data(n, VMPersonDataVariable.IsGhost) > 0) continue;
                if (Data(n, VMPersonDataVariable.PersonsAge) < 18 && (hour < 8 || hour >= 20)) continue; // children are home by 8
                // only Sims with their own character file: townies and the neighbourhood's Sims (not maids, burglars...)
                GameObjectReference reference;
                if (!objs.Entries.TryGetValue(n.GUID, out reference) || reference.Source != GameObjectSource.User) continue;

                var famNum = Data(n, VMPersonDataVariable.TS1FamilyNumber);
                var nfam = (famNum > 0) ? neigh.GetFamily((ushort)famNum) : null;
                if (nfam != null && fam != null && nfam.ChunkID == fam.ChunkID) continue;
                if (nfam != null && playerFamilies.Contains(nfam.ChunkID)) continue; // online: a player's own family is theirs to move about
                if (nfam == null || (nfam.HouseNumber <= 0 && nfam.Unknown == 0)) townies.Add(n); // townies (no family, or a townie family - the extra Sims made for the town are those too)
                else if (nfam.HouseNumber > 0) neighbours.Add(n); // a family living in the neighbourhood
                // (families waiting in the family bin don't wander about)
            }

            // community lots are mostly townies; on a home lot, mostly the neighbours walk by
            List<Neighbour> pool;
            if (!walkBy) pool = (townies.Count > 0 && (neighbours.Count == 0 || Rand.Next(10) < 7)) ? townies : neighbours;
            else pool = (neighbours.Count > 0 && (townies.Count == 0 || Rand.Next(10) < 6)) ? neighbours : townies;
            if (pool.Count == 0) return false;
            var pick = pool[Rand.Next(pool.Count)];

            var anchor = inside ? InsideSpot(vm) : (Portal(vm) ?? Mailbox(vm));
            if (anchor == null) anchor = Mailbox(vm) ?? InsideSpot(vm);
            if (anchor == null) return false;

            // community lot: greet status 1 (Hot Date's social menu needs it); home: a stranger not yet greeted
            Send(vm, new VMNetReduxTownieCmd
            {
                Mode = VMNetReduxTownieCmd.TownieMode.Arrive,
                GUID = pick.GUID,
                AnchorID = anchor.ObjectID,
                Greet = (short)(IsCommunity(vm) ? 1 : 0),
            });

            long stay = walkBy ? 30 + Rand.Next(40) : 90 + Rand.Next(150);
            Mine[pick.GUID] = new Visitor { LeaveAt = now + stay, WalkBy = walkBy, AskedAt = now };
            Console.WriteLine("[SimsRedux] townies: " + (pick.Name ?? pick.GUID.ToString("X8")) + (walkBy ? " walking by" : (inside ? " here already" : " arriving")) + " (staying about " + stay + " minutes)");
            return true;
        }

        /// <summary>Online: the families players own (from the lot's own record of who controls what).</summary>
        private static HashSet<int> PlayerFamilies(VM vm)
        {
            var set = new HashSet<int>();
            try
            {
                if (vm.ReduxFamilyControl != null) foreach (var f in vm.ReduxFamilyControl.Values) set.Add(f);
                foreach (var a in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>())
                    if (IsPlayers(a)) set.Add(a.GetPersonData(VMPersonDataVariable.TS1FamilyNumber));
                foreach (var f in ReduxWorker.OwnedFamilies()) set.Add(f);
            }
            catch { }
            set.Remove(0);
            return set;
        }

        private static short Data(Neighbour n, VMPersonDataVariable v)
        {
            var i = (int)v;
            return (n.PersonData != null && i < n.PersonData.Length) ? n.PersonData[i] : (short)0;
        }

        private static VMEntity Portal(VM vm)
        {
            var portals = vm.Entities.Where(x => !x.Dead && x.Position != LotTilePos.OUT_OF_WORLD && PortalGUIDs.Contains(x.Object.OBJ.GUID)).ToList();
            if (portals.Count == 0) return null;
            return portals[Rand.Next(portals.Count)];
        }

        private static VMEntity Mailbox(VM vm)
        {
            return vm.Entities.FirstOrDefault(x => !x.Dead && x.Position != LotTilePos.OUT_OF_WORLD && MailboxGUIDs.Contains(x.Object.OBJ.GUID));
        }

        /// <summary>Somewhere among the lot's things, on the ground floor, inside the lot.</summary>
        private static VMEntity InsideSpot(VM vm)
        {
            var things = vm.Entities.Where(x => x is VMGameObject && !x.Dead && x.Container == null
                && x.Position != LotTilePos.OUT_OF_WORLD && x.Position.Level == 1
                && !vm.Context.IsUserOutOfBounds(x.Position)).ToList();
            if (things.Count == 0) return null;
            return things[Rand.Next(things.Count)];
        }

        // ------------------------------------------------------------------
        // Leaving
        // ------------------------------------------------------------------

        /// <summary>
        /// A lot reloaded (going home, loading a save made before this version) can still have townies
        /// this brought in. They head off soon.
        /// </summary>
        private static void AdoptLeftovers(VM vm, long now)
        {
            foreach (var ava in People(vm))
            {
                if ((ava.GetValue(VMStackObjectVariable.MovementFlags) & (short)VMMovementFlags.FSOExcludeUserSave) == 0) continue;
                if (IsFamily(vm, ava) || IsPlayers(ava) || ava.GetPersonData(VMPersonDataVariable.PersonType) != 1) continue;
                Mine[ava.Object.OBJ.GUID] = new Visitor { LeaveAt = now + 20, WalkBy = false, AskedAt = now };
            }
        }

        private static void SendHome(VM vm, long now)
        {
            var hour = vm.Context.Clock.Hours;
            bool community = IsCommunity(vm);
            foreach (var pair in Mine.ToList())
            {
                var info = pair.Value;
                var ava = Find(vm, pair.Key);
                if (ava == null)
                {
                    // gone (the game's own scripts sent them off), or never managed to arrive
                    if (info.LeavePushed >= 0 || now - info.AskedAt > 5) Mine.Remove(pair.Key);
                    continue;
                }
                bool greeted = ava.GetPersonData(VMPersonDataVariable.GreetStatus) >= 2;
                if (info.WalkBy && greeted)
                {
                    // greeted on the sidewalk: now they're visiting for a while
                    info.WalkBy = false;
                    info.LeaveAt = Math.Max(info.LeaveAt, now + 120 + Rand.Next(120));
                }
                bool closing = community ? (hour >= 2 && hour < 8) : (!greeted && !WalkByHours(hour));
                if (info.LeavePushed < 0)
                {
                    if (now >= info.LeaveAt || closing)
                    {
                        Send(vm, new VMNetReduxTownieCmd { Mode = VMNetReduxTownieCmd.TownieMode.Leave, ObjectID = ava.ObjectID });
                        info.LeavePushed = now;
                    }
                }
                else if (now - info.LeavePushed > 120)
                {
                    // still here two hours after setting off (stuck, or kept busy): they just go
                    Send(vm, new VMNetReduxTownieCmd { Mode = VMNetReduxTownieCmd.TownieMode.Remove, ObjectID = ava.ObjectID });
                    Mine.Remove(pair.Key);
                }
                else if (now - info.LeavePushed > 30 && !ava.Thread.Queue.Any(x => x.ActionRoutine?.ID == VMNetReduxTownieCmd.LeaveRoutine))
                {
                    // something cancelled it (a chat, say): set off again
                    Send(vm, new VMNetReduxTownieCmd { Mode = VMNetReduxTownieCmd.TownieMode.Leave, ObjectID = ava.ObjectID });
                }
            }
        }
    }
}
