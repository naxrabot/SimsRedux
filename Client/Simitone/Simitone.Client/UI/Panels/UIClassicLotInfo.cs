using FSO.Client;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using Simitone.Client.UI.Screens;
using System;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// SimsRedux: works out what to say about a lot (name, description, price / family, whether
    /// you can move in) using the game's own text, then shows it in a classic Sims 1 popup.
    /// Same rules as Simitone's house panel, just without the touch-style screen.
    /// </summary>
    public static class UIClassicLotInfo
    {
        public class LotInfo
        {
            public string Street = "";
            public string Name = "";
            public string Description = "";
            public string Status = "";
            public bool CanSelect = true;
            public bool Occupied;
        }

        public static LotInfo Get(int houseID)
        {
            var info = new LotInfo();
            var neigh = Content.Get().Neighborhood;
            var house = neigh.GetHouse(houseID);

            var street = neigh.StreetNames;
            var assignment = street?.Get<STR>(2001)?.GetString(houseID - 1);
            int streetName;
            if (int.TryParse(assignment, out streetName))
                info.Street = street.Get<STR>(2000).GetString(streetName - 1).Replace("%s", houseID.ToString());

            var nameDesc = neigh.GetHouseNameDesc(houseID);
            info.Name = string.IsNullOrEmpty(nameDesc.Item1) ? info.Street : nameDesc.Item1;

            var family = neigh.GetFamilyForHouse((short)houseID);
            var moveInID = (FSO.Client.UI.Framework.UIScreen.Current as TS1GameScreen)?.MoveInFamily;
            var moveIn = (moveInID == null) ? null : neigh.GetFamily((ushort)moveInID.Value);

            if (family != null)
            {
                info.Occupied = true;
                info.Description = GameFacade.Strings.GetString("134", "0", new string[] {
                    neigh.MainResource.Get<FAMs>(family.ChunkID)?.GetString(0) ?? "?",
                    "§" + (family.ValueInArch + family.Budget).ToString("##,#0"),
                    family.FamilyFriends.ToString()
                });
                if (moveIn != null)
                {
                    info.Status = GameFacade.Strings.GetString("132", "15"); // house occupied
                    info.CanSelect = false;
                }
                return info;
            }

            info.Description = nameDesc.Item2 ?? "";

            var zones = neigh.ZoningDictionary;
            short zone;
            if (!zones.TryGetValue((short)houseID, out zone))
                zone = (short)((houseID >= 81 && houseID <= 89) ? 2 : 1);

            if (zone > 0)
            {
                // community lot
                if (moveIn != null)
                {
                    info.Status = Trim(GameFacade.Strings.GetString("134", "18"), 8);
                    info.CanSelect = false;
                }
                else info.Status = Trim(GameFacade.Strings.GetString("134", "17"), 8);
                return info;
            }

            var price = house.Get<SIMI>(1)?.PurchaseValue ?? 0;
            var priceStr = "§" + price.ToString("##,#0");
            string moveInName = (moveIn == null) ? "" : (moveIn.ChunkParent.Get<FAMs>(moveIn.ChunkID)?.GetString(0) ?? "");

            if (houseID >= 90 && houseID <= 92)
            {
                // Magic Town lots also need MagiCoins
                var magicoins = neigh.GetMagicoinsForFamily(family);
                var requiredMC = int.Parse(GameFacade.Strings.GetString("134", (32 + Math.Abs(91 - houseID)).ToString()));
                if (moveIn != null)
                {
                    bool coins = magicoins >= requiredMC, money = moveIn.Budget >= price;
                    if (coins && money)
                        info.Status = GameFacade.Strings.GetString("134", "31", new string[] { "", "", priceStr, requiredMC.ToString() });
                    else if (coins)
                        info.Status = Trim(GameFacade.Strings.GetString("134", "38", new string[] { "", "", priceStr, requiredMC.ToString(), moveInName, "§" + moveIn.Budget.ToString("##,#0") }), 4);
                    else if (money)
                        info.Status = Trim(GameFacade.Strings.GetString("134", "36", new string[] { "", "", priceStr, requiredMC.ToString(), moveInName, magicoins.ToString() }), 4);
                    else
                        info.Status = Trim(GameFacade.Strings.GetString("134", "37", new string[] { "", "", priceStr, requiredMC.ToString(), moveInName, magicoins.ToString() }), 4);
                    info.CanSelect = coins && money;
                }
                else
                    info.Status = Trim(GameFacade.Strings.GetString("134", "29", new string[] { "", "", priceStr, requiredMC.ToString() }), 4);
                return info;
            }

            if (moveIn != null)
            {
                if (moveIn.Budget >= price)
                    info.Status = Trim(GameFacade.Strings.GetString("134", "5", new string[] { "", "", priceStr, "§" + (moveIn.Budget - price).ToString("##,#0") }), 4);
                else
                {
                    info.Status = Trim(GameFacade.Strings.GetString("134", "4", new string[] { "", "", priceStr, moveInName, "§" + moveIn.Budget.ToString("##,#0") }), 4);
                    info.CanSelect = false;
                }
            }
            else
                info.Status = Trim(GameFacade.Strings.GetString("134", "1", new string[] { "", "", priceStr }), 4);

            return info;
        }

        /// <summary>SimsRedux: the short summary shown when the mouse is over a house on the map.</summary>
        public class HoverInfo
        {
            public string Title = "";
            public System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
            public bool Occupied;
            public bool Community;
            /// <summary>Nothing has been built here yet (no walls or floors).</summary>
            public bool Empty;
        }

        public static HoverInfo GetHover(int houseID)
        {
            var hover = new HoverInfo();
            var neigh = Content.Get().Neighborhood;
            var info = Get(houseID);
            var family = neigh.GetFamilyForHouse((short)houseID);
            SIMI simi = null;
            try { simi = neigh.GetHouse(houseID)?.Get<SIMI>(1); } catch { }

            if (family != null)
            {
                hover.Occupied = true;
                var famName = neigh.MainResource.Get<FAMs>(family.ChunkID)?.GetString(0) ?? "?";
                hover.Title = "The " + famName + " Family";
                hover.Lines.Add("Family funds: §" + family.Budget.ToString("##,#0"));
                hover.Lines.Add("House value: §" + family.ValueInArch.ToString("##,#0"));
            }
            else if (neigh.IsCommunity((short)houseID))
            {
                hover.Community = true;
                hover.Title = info.Name;
                hover.Lines.Add("Community Lot");
                hover.Empty = simi != null && simi.ArchitectureValue <= 0;
            }
            else
            {
                hover.Title = info.Name;
                hover.Empty = simi != null && simi.ArchitectureValue <= 0;
                var price = simi?.PurchaseValue ?? 0;
                hover.Lines.Add((hover.Empty ? "Empty lot: §" : "For sale: §") + price.ToString("##,#0"));
            }
            if (!string.IsNullOrEmpty(info.Street) && info.Street != hover.Title) hover.Lines.Add(info.Street);
            if (string.IsNullOrEmpty(hover.Title)) hover.Title = "Lot " + houseID;
            return hover;
        }

        /// <summary>
        /// SimsRedux: what a neighbourhood tool would do to this lot (shown in the hover popup).
        /// "ok" says whether the tool can be used here. Null when no tool is picked.
        /// </summary>
        public static string ToolHint(HoverInfo info, UINeighborhoodSelectionPanel.NeighTool tool, out bool ok)
        {
            ok = false;
            switch (tool)
            {
                case UINeighborhoodSelectionPanel.NeighTool.NewHouse:
                    if (info.Occupied) return "Someone lives here";
                    if (!info.Empty) return "Not an empty lot: bulldoze it first";
                    ok = true;
                    return info.Community ? "Click to build a community lot here" : "Click to build a new house here";
                case UINeighborhoodSelectionPanel.NeighTool.Bulldoze:
                    if (info.Occupied) return "Evict the family first";
                    ok = true;
                    return info.Empty ? "Click to clear the lot again" : "Click to bulldoze";
                case UINeighborhoodSelectionPanel.NeighTool.Rezone:
                    if (info.Occupied) return "Evict the family first";
                    ok = true;
                    return info.Community ? "Click to make this a home lot" : "Click to make this a community lot";
            }
            ok = true;
            return null;
        }

        private static void ConfirmEvict(int houseID)
        {
            var neigh = Content.Get().Neighborhood;
            var family = neigh.GetFamilyForHouse((short)houseID);
            if (family == null) return;
            var name = neigh.MainResource.Get<FAMs>(family.ChunkID)?.GetString(0) ?? "this family";
            UIClassicDialog.YesNo("Evict Family?",
                "Are you sure you want to evict the " + name + " family? They will move out and keep the value of the house.",
                () => (FSO.Client.UI.Framework.UIScreen.Current as TS1GameScreen)?.EvictLot(family, (short)houseID));
        }

        private static string Trim(string s, int start)
        {
            if (s == null) return "";
            return s.Length > start ? s.Substring(start) : s;
        }

        /// <summary>
        /// Shows the classic lot popup. "select" is called when the player picks
        /// Move In / Visit Lot (the normal move-in or enter-lot flow then continues).
        /// </summary>
        public static void Show(int houseID, Action<int> select)
        {
            LotInfo info;
            try { info = Get(houseID); }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Couldn't read lot info: " + e.Message);
                select(houseID);
                return;
            }

            var moveIn = (FSO.Client.UI.Framework.UIScreen.Current as TS1GameScreen)?.MoveInFamily != null;
            var title = info.Name;
            if (!string.IsNullOrEmpty(info.Street) && info.Street != info.Name) title = info.Name + "  (" + info.Street + ")";
            var message = info.Description;
            if (!string.IsNullOrEmpty(info.Status)) message = (message.Length > 0 ? message + "\n\n" : "") + info.Status;

            if (!info.CanSelect)
            {
                UIClassicDialog.Ok(title, message);
                return;
            }

            // SimsRedux: in the online town you travel to a lot on the server (nobody evicts anyone)
            if (Simitone.Client.Utils.ReduxTown.Active)
            {
                bool lived = info.Occupied, community = false;
                try { community = GetHover(houseID).Community; } catch { }
                if (!lived && !community)
                {
                    UIClassicDialog.Ok(title, (message.Length > 0 ? message + "\n\n" : "") + "Nobody lives here yet, so there's nothing to visit. (Moving in and building in the town come later.)");
                    return;
                }
                UIClassicDialog.Show(title, message,
                    Tuple.Create("Travel Here", (Action)(() => select(houseID))),
                    Tuple.Create("Cancel", (Action)null));
                return;
            }

            var action = moveIn ? "Move In" : (info.Occupied ? "Play" : "Visit Lot");
            if (info.Occupied && !moveIn)
            {
                UIClassicDialog.Show(title, message,
                    Tuple.Create(action, (Action)(() => select(houseID))),
                    Tuple.Create("Evict", (Action)(() => ConfirmEvict(houseID))),
                    Tuple.Create("Cancel", (Action)null));
                return;
            }
            UIClassicDialog.Show(title, message,
                Tuple.Create(action, (Action)(() => select(houseID))),
                Tuple.Create("Cancel", (Action)null));
        }
    }
}
