using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Content.Interfaces;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the original Sims 1 Buy mode catalog on the classic bottom bar.
    ///
    ///   Start page: a row of Room icons (Living Room, Dining Room, ...) and a row of Function
    ///   icons (Seating, Surfaces, ...). On Downtown / Vacation / community lots the top row shows
    ///   that lot type's own categories instead.
    ///   Catalog page: the category's back button, the small subsort icons (All, Other, ...),
    ///   scroll arrows and two rows of object pictures. Click a picture to buy and place it.
    ///
    /// Buying and placing still uses Simitone's object holder, so it works exactly as before.
    /// Coordinates are in the classic control panel's layout (the wing is at 0,0).
    /// </summary>
    public class UIClassicBuyPanel : UIContainer
    {
        private TS1GameScreen Game;
        private Texture2D Px, ThumbFrame, UnknownThumb;
        private TextStyle Small, Title;
        private static readonly Color TextColor = new Color(220, 220, 250);

        // ---- layout ----
        private const int BarY = 83;
        private const int RowAY = 105, RowBY = 156;      // centres of the two category rows
        private const int CatX = 236, CatW = 44;          // first category cell, cell width
        private const int SubX = 232, SubY = 116, SubStep = 34, SubCols = 5;
        private const float SubScale = 32f / 36f;
        private const int ArrowLX = 408, ArrowY = 109;
        private const int ThumbX = 421, ThumbStep = 47, ThumbRow1 = 87, ThumbRow2 = 135, ThumbSize = 45;

        // ---- category data ----
        // function categories, in the order the original shows them (number = Sims 1 function flag bit)
        private static readonly int[] FunctionOrder = { 0, 1, 5, 3, 2, 4, 7, 6 };
        private static readonly string[] FunctionArt = { "Seating", "Surfaces", "Appliances", "Electronics", "Plumbing", "Decorative", "Misc", "Lighting" };
        private static readonly string[] FunctionNames = { "Seating", "Surfaces", "Appliances", "Electronics", "Plumbing", "Decorative", "Miscellaneous", "Lighting" };
        private static readonly string[] SubsortFolder = { "Seating", "Surfaces", "Appliances", "Electronics", "Plumbing", "Decorative", "Miscellaneous", "Lighting" };

        // rooms, in the order the original shows them (number = Sims 1 room sort flag bit)
        private static readonly int[] RoomBits = { 3, 5, 1, 7, 0, 2, 4, 6 };
        private static readonly string[] RoomArt = { "Living", "Dining", "Bedroom", "Study", "Kitchen", "Bathroom", "Outside", "Misc" };
        private static readonly string[] RoomNames = { "Living Room", "Dining Room", "Bedroom", "Study", "Kitchen", "Bathroom", "Outside", "Miscellaneous" };

        private enum View { Start, Function, Room, Special, Search }
        private View CurrentView = View.Start;
        private UICatalogMode Mode = UICatalogMode.Normal;
        private int CurrentCategory;           // function bit / room bit / special mask bit
        private string CurrentName = "";

        private class Subsort
        {
            public string Name;
            public Func<ObjectCatalogItem, bool> Filter;
            public UITS1Button Button;
        }

        private List<UITS1Button> CategoryButtons = new List<UITS1Button>();
        private List<string> CategoryNames = new List<string>();
        private List<Subsort> Subsorts = new List<Subsort>();
        private UITS1Button BackButton, ArrowLeft, ArrowRight;
        private string HoverText;

        private List<ObjectCatalogItem> Items = new List<ObjectCatalogItem>();
        private int FirstItem;
        private List<UIClassicCatalogThumb> Thumbs = new List<UIClassicCatalogThumb>();
        private int SelectedIndex = -1;
        private bool WasVisible;

        // selling: while holding an object that's already on the lot, clicking the bar sells it
        // (like dropping it back on the catalog in the original)
        private UIClassicSellCatcher SellCatcher;

        public UIClassicBuyPanel(TS1GameScreen game)
        {
            Game = game;
            Px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            ThumbFrame = TS1UIGraphics.Get("cpanel/Catalog/ThumbTemplate.bmp");
            UnknownThumb = TS1UIGraphics.Get("cpanel/CatalogUnknown.bmp");
            Small = TextStyle.DefaultLabel.Clone();
            Small.Size = 10;
            Small.Color = TextColor;
            Title = TextStyle.DefaultLabel.Clone();
            Title.Size = 12;
            Title.Color = TextColor;

            var holder = game.LotControl.ObjectHolder;
            holder.OnPickup += Holder_OnPickup;
            holder.OnPutDown += Holder_OnPutDown;
            holder.OnDelete += Holder_OnDelete;

            // SimsRedux: search box and dropper, just above the bar
            Search = new UIReduxCatalogSearch();
            Search.Position = new Vector2(SubX, BarY - UIReduxCatalogSearch.BoxH);
            Search.OnSearch += SearchFor;
            Search.OnDropper += ToggleDropper;
            Add(Search);

            ShowStart();
        }

        // ------------------------------------------------------------------
        // Search and the dropper (SimsRedux)
        // ------------------------------------------------------------------

        private UIReduxCatalogSearch Search;

        /// <summary>Every Buy item whose name has the typed letters in it (two or more letters).</summary>
        private void SearchFor(string text)
        {
            if (text.Length < 2)
            {
                if (CurrentView == View.Search) ShowStart();
                return;
            }
            ClearHolding();
            var words = text.ToLowerInvariant();
            var results = AllBuyItems().Where(x => HasCatalogPicture(x.GUID) && (GetName(x) ?? "").ToLowerInvariant().Contains(words)).ToList();
            OpenCatalog(View.Search, 0, "Search: " + text, "BackFMisc", results, new List<Subsort>());
        }

        private bool DropperActive { get { return Game.LotControl?.CustomControl is UIReduxDropper; } }

        /// <summary>The dropper: click something on the lot to buy another one like it.</summary>
        private void ToggleDropper()
        {
            var lot = Game.LotControl;
            if (lot == null) return;
            if (DropperActive) { lot.CustomControl.Release(); lot.CustomControl = null; return; }
            ClearHolding();
            lot.CustomControl = new UIReduxDropper(lot, DropperPicked,
                (floor) => HoverText = "Floors are picked in Build mode.",
                () => HoverText = "Nothing to pick there - click an object.");
            HoverText = "Dropper: click an object on the lot.";
        }

        /// <summary>The dropper picked an object: open its page in the catalog and start placing another one.</summary>
        private void DropperPicked(uint guid)
        {
            var lot = Game.LotControl;
            if (lot.CustomControl is UIReduxDropper) { lot.CustomControl.Release(); lot.CustomControl = null; }
            Search.ClearText();
            var item = Content.Get().WorldCatalog.GetItemByGUID(guid);
            if (item != null && item.Value.Category >= 0 && item.Value.Category < 8)
            {
                OpenFunction(item.Value.Category);
                var index = Items.FindIndex(x => x.GUID == guid);
                if (index >= 0)
                {
                    int perPage = Math.Max(1, Columns * Rows);
                    FirstItem = (index / perPage) * perPage;
                    RebuildThumbs();
                    BuyItem(index);
                    return;
                }
            }
            // not in the Buy catalog (a build item, or hidden from it): just place another one
            BuyGuid(guid);
        }

        private void EnsureSellCatcher()
        {
            if (SellCatcher == null)
            {
                SellCatcher = new UIClassicSellCatcher(Small, Title);
                SellCatcher.OnClick = () =>
                {
                    var holder = Game.LotControl.ObjectHolder;
                    if (holder.Holding == null) return;
                    if (holder.Holding.IsBought) holder.SellBack(null);
                    else holder.Cancel();
                };
            }
            // keep it on top of everything else on the bar
            Remove(SellCatcher);
            Add(SellCatcher);
        }

        // ------------------------------------------------------------------
        // Pages
        // ------------------------------------------------------------------

        private void ClearAll()
        {
            foreach (var b in CategoryButtons) Remove(b);
            CategoryButtons.Clear();
            CategoryNames.Clear();
            foreach (var s in Subsorts) if (s.Button != null) Remove(s.Button);
            Subsorts.Clear();
            foreach (var t in Thumbs) Remove(t);
            Thumbs.Clear();
            if (BackButton != null) { Remove(BackButton); BackButton = null; }
            if (ArrowLeft != null) { Remove(ArrowLeft); ArrowLeft = null; }
            if (ArrowRight != null) { Remove(ArrowRight); ArrowRight = null; }
            if (TypeTabs != null) { Remove(TypeTabs); TypeTabs = null; }
            if (GrowTab != null) GrowTab.Visible = false;
            ThumbStart = ThumbX;
            HoverText = null;
        }

        private UITS1Button AddIcon(string path, float cx, float cy, string name, Action click)
        {
            var tex = TS1UIGraphics.Get(path);
            var btn = new UITS1Button(tex, 4);
            int w = Math.Max(1, tex.Width / 4), h = tex.Height;
            btn.Position = new Vector2((int)(cx - w / 2), (int)(cy - h / 2));
            btn.OnButtonClick += (b) => click();
            AddHover(btn, name);
            Add(btn);
            CategoryButtons.Add(btn); // so it's removed when a catalog opens
            return btn;
        }

        private void AddHover(UITS1Button btn, string name)
        {
            btn.OnButtonHover += (b) => HoverText = name;
            btn.OnButtonExit += (b) => { if (HoverText == name) HoverText = null; };
        }

        /// <summary>The start page: room row (or the lot type's own row) above the function row.</summary>
        private void ShowStart()
        {
            ClearAll();
            Search?.ClearText();
            CurrentView = View.Start;
            Mode = GetMode();

            if (Mode == UICatalogMode.Normal)
            {
                for (int i = 0; i < 8; i++)
                {
                    var room = i;
                    AddIcon("cpanel/Catalog/BuyR" + RoomArt[i] + ".bmp", CatX + CatW * i + CatW / 2, RowAY, RoomNames[i], () => OpenRoom(room));
                }
            }
            else
            {
                var special = SpecialCategories();
                for (int i = 0; i < special.Count; i++)
                {
                    var s = special[i];
                    AddIcon(s.Art, CatX + CatW * i + CatW / 2, RowAY, s.Name, () => OpenSpecial(s));
                }
            }

            for (int i = 0; i < 8; i++)
            {
                var cat = FunctionOrder[i];
                AddIcon("cpanel/Catalog/BuyF" + FunctionArt[cat] + ".bmp", CatX + CatW * i + CatW / 2, RowBY, FunctionNames[cat], () => OpenFunction(cat));
            }
        }

        private class SpecialCategory
        {
            public string Name, Art, Back;
            public int Bit;
        }

        /// <summary>The top-row categories for Downtown, Vacation, community, Studio Town and Magic Town lots.</summary>
        private List<SpecialCategory> SpecialCategories()
        {
            var result = new List<SpecialCategory>();
            string[] art, back;
            switch (Mode)
            {
                case UICatalogMode.Downtown:
                    art = new[] { "BuyDDining", "BuyDShops", "BuyDOutdoor", "BuyDStreet" };
                    back = new[] { "BackDDining", "BackDShops", "BackDOutdoor", "BackDStreet" }; break;
                case UICatalogMode.Vacation:
                    art = new[] { "BuyVOne", "BuyVTwo", "BuyVThree", "BuyVFour" };
                    back = new[] { "BackVOne", "BackVTwo", "BackVThree", "BackVFour" }; break;
                case UICatalogMode.Community:
                    art = new[] { "BuyCOne", "BuyCTwo", "BuyCThree", "BuyCFour" };
                    back = new[] { "BackCOne", "BackCTwo", "BackCThree", "BackCFour" }; break;
                case UICatalogMode.Studiotown:
                    art = new[] { "BuySTDining", "BuySTShops", "BuySTStudio", "BuySTSpa" };
                    back = new[] { "BackDDining", "BackDShops", "BackSStudio", "BackSSpa" }; break;
                default: // Magic Town
                    art = new[] { "BuyDDining", "BuyDShops", "BuyMTmagic", "BuyMOutdoor" };
                    back = new[] { "BackDDining", "BackDShops", "BackMMagic", "BackDOutdoor" }; break;
            }
            List<UICatalogSubcat> subcats = null;
            UIBuyBrowsePanel.DTCategories.TryGetValue(Mode, out subcats);
            for (int i = 0; i < 4; i++)
            {
                string name = "";
                if (subcats != null && i < subcats.Count)
                    name = GameFacade.Strings.GetString(subcats[i].StrTable.ToString(), subcats[i].StrInd.ToString());
                result.Add(new SpecialCategory { Name = name, Art = "cpanel/Catalog/" + art[i] + ".bmp", Back = back[i], Bit = i });
            }
            var miscBack = Mode == UICatalogMode.Vacation ? "BackVMisc" : (Mode == UICatalogMode.Community ? "BackCMisc" : "BackDMisc");
            var miscArt = Mode == UICatalogMode.Vacation ? "BuyVMisc" : (Mode == UICatalogMode.Community ? "BuyCMisc" : (Mode == UICatalogMode.Studiotown ? "BuySTMisc" : "BuyDMisc"));
            result.Add(new SpecialCategory { Name = "Miscellaneous", Art = "cpanel/Catalog/" + miscArt + ".bmp", Back = miscBack, Bit = 7 });
            return result;
        }

        private UICatalogMode GetMode()
        {
            try { return Game.Frontend?.MainPanel?.GetLotType(false) ?? UICatalogMode.Normal; }
            catch { return UICatalogMode.Normal; }
        }

        /// <summary>Which sort mask an item has on this kind of lot (same rules as Simitone's catalog).</summary>
        private byte GetSubsort(ObjectCatalogItem item)
        {
            switch (Mode)
            {
                case UICatalogMode.Downtown: return item.DowntownSort;
                case UICatalogMode.Community: return item.CommunitySort;
                case UICatalogMode.Vacation: return item.VacationSort;
                case UICatalogMode.Studiotown: return item.StudiotownSort;
                case UICatalogMode.Magictown: return item.MagictownSort;
                default:
                    if (item.RoomSort == 0) return 0; // items without a room sort don't appear
                    return item.Subsort;
            }
        }

        private IEnumerable<ObjectCatalogItem> AllBuyItems()
        {
            var catalog = Content.Get().WorldCatalog;
            for (sbyte c = 0; c < 8; c++)
                foreach (var item in catalog.GetItemsByCategory(c))
                    yield return item;
        }

        private string SubsortPath(string folder, string file)
        {
            return "cpanel/Catalog/SubSortIcons/" + folder + "/" + file + ".bmp";
        }

        private string SpecialSubsortFolder()
        {
            switch (Mode)
            {
                case UICatalogMode.Community: return "CommunitySubsort";
                case UICatalogMode.Vacation: return "VacationSubsort";
                default: return "DowntownSubsort";
            }
        }

        private string SpecialSubsortPrefix()
        {
            switch (Mode)
            {
                case UICatalogMode.Community: return "BuySubSortCommunity";
                case UICatalogMode.Vacation: return "BuySubSortVacation";
                default: return "BuySubSortDowntown";
            }
        }

        /// <summary>Clicked a function icon (Seating, Surfaces...).</summary>
        private void OpenFunction(int category)
        {
            var source = Content.Get().WorldCatalog.GetItemsByCategory((sbyte)category)
                .Where(x => GetSubsort(x) > 0).ToList();
            var subs = new List<Subsort>();
            if (Mode == UICatalogMode.Normal)
            {
                List<UICatalogSubcat> cats = (category < UIBuyBrowsePanel.Categories.Count) ? UIBuyBrowsePanel.Categories[category] : new List<UICatalogSubcat>();
                string[] files = { "BuySubSortOne", "BuySubSortTwo", "BuySubSortThree", "BuySubSortFour" };
                foreach (var cat in cats)
                {
                    var bit = cat.MaskBit;
                    var name = GameFacade.Strings.GetString(cat.StrTable.ToString(), cat.StrInd.ToString());
                    string art;
                    Func<ObjectCatalogItem, bool> filter;
                    if (bit == 8) { art = "cpanel/Catalog/SubSortIcons/BuySubSortAll.bmp"; filter = (x) => true; }
                    else if (bit == 7) { art = "cpanel/Catalog/SubSortIcons/BuySubSortOther.bmp"; filter = (x) => (GetSubsort(x) & ((1 << 7) | 16)) > 0; }
                    else if (bit == 5) { art = SubsortPath("Miscellaneous", "BuySubSortPets"); filter = (x) => (GetSubsort(x) & (1 << 5)) > 0; }
                    else if (bit == 6) { art = SubsortPath("Miscellaneous", "BuySubSortMagic"); filter = (x) => (GetSubsort(x) & (1 << 6)) > 0; }
                    else { art = SubsortPath(SubsortFolder[category], files[bit]); var mask = 1 << bit; filter = (x) => (GetSubsort(x) & mask) > 0; }
                    subs.Add(new Subsort { Name = name, Filter = filter, Button = MakeSubButton(art, name) });
                }
            }
            else
            {
                foreach (var s in SpecialCategories())
                {
                    var mask = 1 << s.Bit;
                    subs.Add(new Subsort { Name = s.Name, Filter = (x) => (GetSubsort(x) & mask) > 0, Button = MakeSubButton(s.Art, s.Name) });
                }
                subs.Add(new Subsort { Name = "All", Filter = (x) => true, Button = MakeSubButton("cpanel/Catalog/SubSortIcons/BuySubSortAll.bmp", "All") });
            }
            OpenCatalog(View.Function, category, FunctionNames[category], "BackF" + FunctionArt[category], source, subs);
        }

        /// <summary>Clicked a room icon (Living Room, Kitchen...).</summary>
        private void OpenRoom(int index)
        {
            var mask = 1 << RoomBits[index];
            var source = AllBuyItems().Where(x => (x.RoomSort & mask) > 0).ToList();
            OpenCatalog(View.Room, index, RoomNames[index], "BackR" + RoomArt[index], source, FunctionSubsorts("RoomSubsort", "BuySubSortRoom"));
        }

        /// <summary>Clicked a Downtown / Vacation / community category.</summary>
        private void OpenSpecial(SpecialCategory cat)
        {
            var mask = (cat.Bit == 7) ? ((1 << 7) | 16) : (1 << cat.Bit);
            var source = AllBuyItems().Where(x => (GetSubsort(x) & mask) > 0).ToList();
            OpenCatalog(View.Special, cat.Bit, cat.Name, cat.Back, source, FunctionSubsorts(SpecialSubsortFolder(), SpecialSubsortPrefix()));
        }

        /// <summary>Subsorts by function (used inside a room or a lot-type category), plus All.</summary>
        private List<Subsort> FunctionSubsorts(string folder, string prefix)
        {
            var subs = new List<Subsort>();
            subs.Add(new Subsort { Name = "All", Filter = (x) => true, Button = MakeSubButton("cpanel/Catalog/SubSortIcons/BuySubSortAll.bmp", "All") });
            for (int i = 0; i < 8; i++)
            {
                var cat = FunctionOrder[i];
                var name = FunctionNames[cat];
                subs.Add(new Subsort { Name = name, Filter = (x) => x.Category == cat, Button = MakeSubButton(SubsortPath(folder, prefix + SubsortFolder[cat].Replace("Miscellaneous", "Misc")), name) });
            }
            return subs;
        }

        private UITS1Button MakeSubButton(string path, string name)
        {
            // SimsRedux: just the icon, without its square background (the chosen one gets a glow instead)
            var tex = ReduxTheme.Redux ? TS1UIGraphics.GetCutout(path, 4, 24) : TS1UIGraphics.Get(path);
            var btn = new UITS1Button(tex, 4);
            btn.ScaleX = btn.ScaleY = SubScale;
            AddHover(btn, name);
            return btn;
        }

        private List<ObjectCatalogItem> SourceItems;

        // SimsRedux: finer groups inside a subsort (Art, Mirrors, Rugs...), as a column of tabs
        private UIReduxTabStrip TypeTabs;
        private List<ObjectCatalogItem> SubItems = new List<ObjectCatalogItem>();
        private Dictionary<uint, string> ItemTypes = new Dictionary<uint, string>();
        private static Dictionary<int, string> ChosenType = new Dictionary<int, string>();
        private int TypeCategory = -1;
        private int ThumbStart = ThumbX;

        private void OpenCatalog(View view, int category, string name, string backArt, List<ObjectCatalogItem> source, List<Subsort> subs)
        {
            ClearAll();
            CurrentView = view;
            CurrentCategory = category;
            CurrentName = name;
            // (objects with no catalog picture are the game's hidden/test ones - never shown)
            SourceItems = source.Where(x => HasCatalogPicture(x.GUID)).OrderBy(x => x.Price).ToList();

            // back button (falls back to a plain one if this expansion's art is missing)
            var backTex = TS1UIGraphics.Get("cpanel/Catalog/BackButtons/" + backArt + ".bmp");
            if (backTex.Width <= 1) backTex = TS1UIGraphics.Get("cpanel/Catalog/BackButtons/BackFMisc.bmp");
            BackButton = new UITS1Button(backTex, 4);
            BackButton.Position = new Vector2(SubX, BarY + 3);
            BackButton.OnButtonClick += (b) => { ClearHolding(); ShowStart(); };
            AddHover(BackButton, "Back");
            Add(BackButton);

            // subsorts: only the ones that have something in them
            int n = 0;
            foreach (var s in subs)
            {
                var filter = s.Filter;
                if (!SourceItems.Any(x => filter(x))) { continue; }
                s.Button.Position = new Vector2(SubX + (n % SubCols) * SubStep, SubY + (n / SubCols) * 33);
                var sub = s;
                s.Button.OnButtonClick += (b) => SelectSubsort(sub);
                Add(s.Button);
                Subsorts.Add(s);
                n++;
            }

            ArrowLeft = new UITS1Button(TS1UIGraphics.Get("cpanel/Buttons/ScrollLeftBuy.bmp"), 4);
            ArrowLeft.Position = new Vector2(ArrowLX, ArrowY);
            ArrowLeft.OnButtonClick += (b) => Page(-1);
            Add(ArrowLeft);
            ArrowRight = new UITS1Button(TS1UIGraphics.Get("cpanel/Buttons/ScrollRight.bmp"), 4);
            ArrowRight.OnButtonClick += (b) => Page(1);
            Add(ArrowRight);

            // start on "All" like the original (or the first subsort if there's no All)
            var all = Subsorts.FirstOrDefault(x => x.Name == "All") ?? Subsorts.FirstOrDefault();
            if (all != null) SelectSubsort(all);
            else { Items = SourceItems; FirstItem = 0; RebuildThumbs(); }
        }

        private void SelectSubsort(Subsort sub)
        {
            foreach (var s in Subsorts) s.Button.Selected = (s == sub);
            SubItems = SourceItems.Where(sub.Filter).ToList();
            ShowTypeTabs();
        }

        /// <summary>
        /// SimsRedux: when everything in this subsort does the same job (all Decorative, say), split
        /// it into smaller groups by name - Art, Mirrors, Rugs, Plants... - with tabs to pick one.
        /// </summary>
        private void ShowTypeTabs()
        {
            if (TypeTabs != null) { Remove(TypeTabs); TypeTabs = null; }
            ItemTypes.Clear();
            TypeCategory = -1;
            List<string> tabs = new List<string>();
            if (SubItems.Count >= 6)
            {
                int cat = SubItems[0].Category;
                var types = SubItems.All(x => x.Category == cat) ? ReduxCatalogTypes.ForBuyCategory(cat) : null;
                if (types != null)
                {
                    foreach (var item in SubItems)
                        ItemTypes[item.GUID] = ReduxCatalogTypes.TypeOf(GetName(item) + " " + item.Name, GetDescription(item), types, "Other");
                    tabs = ReduxCatalogTypes.Tabs(ItemTypes.Values, types, "Other");
                    TypeCategory = cat;
                }
            }
            if (tabs.Count == 0)
            {
                ShowItems(SubItems, 0);
                return;
            }
            string chosen;
            if (!ChosenType.TryGetValue(TypeCategory, out chosen) || !tabs.Contains(chosen)) chosen = ReduxCatalogTypes.All;
            TypeTabs = new UIReduxTabStrip(tabs, chosen);
            TypeTabs.Position = new Vector2(ArrowLX, BarY + 6);
            TypeTabs.OnPick += (name) =>
            {
                ChosenType[TypeCategory] = name;
                ShowItems(ItemsOfType(name), UIReduxTabStrip.StripWidth + 4);
            };
            Add(TypeTabs);
            ShowItems(ItemsOfType(chosen), UIReduxTabStrip.StripWidth + 4);
        }

        private List<ObjectCatalogItem> ItemsOfType(string type)
        {
            if (type == ReduxCatalogTypes.All) return SubItems;
            string t;
            return SubItems.Where(x => ItemTypes.TryGetValue(x.GUID, out t) && t == type).ToList();
        }

        private void ShowItems(List<ObjectCatalogItem> items, int extraLeft)
        {
            Items = items;
            FirstItem = 0;
            SelectedIndex = -1;
            ThumbStart = ThumbX + extraLeft;
            if (ArrowLeft != null) ArrowLeft.Position = new Vector2(ArrowLX + extraLeft, ArrowY);
            RebuildThumbs();
        }

        private int Columns
        {
            get
            {
                var ucp = Parent?.Parent as UIClassicUCP;
                float scale = ucp?.UIScale ?? 1f;
                float width = GlobalSettings.Default.GraphicsWidth / scale;
                return Math.Max(1, (int)((width - ThumbStart - 24) / ThumbStep));
            }
        }

        private void Page(int dir)
        {
            int perPage = Columns * Rows;
            int next = FirstItem + dir * perPage;
            if (next < 0) next = 0;
            if (next >= Items.Count) return;
            FirstItem = next;
            RebuildThumbs();
        }

        private int BuiltColumns = -1;
        // ---- SimsRedux: the grid can grow upwards (the arrow tab on top), up to 5 rows ----
        private const int RowStep = 48, MaxRows = 5;
        private static bool Tall;
        private UIReduxGrowTab GrowTab;

        /// <summary>Rows of pictures: 2 on the bar, or as many as the items need (up to 5) when grown.</summary>
        private int Rows
        {
            get
            {
                if (!Tall) return 2;
                int cols = Math.Max(1, Columns);
                return Math.Max(2, Math.Min(MaxRows, (Items.Count + cols - 1) / cols));
            }
        }

        private int RowY(int r, int rows) { return ThumbRow2 - (rows - 1 - r) * RowStep; }

        /// <summary>The top of the picture grid (the bar's top when it's 2 rows).</summary>
        private int GridTop { get { return CurrentView == View.Start || Thumbs.Count == 0 ? BarY : Math.Min(BarY, RowY(0, Rows) - 4); } }

        private void UpdateGrowTab()
        {
            if (GrowTab == null)
            {
                GrowTab = new UIReduxGrowTab();
                GrowTab.OnClick += () => { Tall = !Tall; FirstItem = 0; RebuildThumbs(); };
                Add(GrowTab);
            }
            int cols = Math.Max(1, Columns);
            GrowTab.Visible = CurrentView != View.Start && (Tall || Items.Count > cols * 2);
            GrowTab.Up = !Tall || Rows <= 2;
            GrowTab.Position = new Vector2(ThumbStart + 4, GridTop - UIReduxGrowTab.H);
        }

        private void RebuildThumbs()
        {
            HoverGUID = 0; // (the pictures under the mouse are being replaced)
            foreach (var t in Thumbs) Remove(t);
            Thumbs.Clear();
            int cols = Columns;
            BuiltColumns = cols;
            int rows = Rows;
            int budget = Game.LotControl?.Budget ?? int.MaxValue;
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    int index = FirstItem + c * rows + r;
                    if (index >= Items.Count) break;
                    var item = Items[index];
                    var thumb = new UIClassicCatalogThumb(ThumbFrame, GetIcon(item.GUID) ?? UnknownThumb, index);
                    thumb.Position = new Vector2(ThumbStart + c * ThumbStep, RowY(r, rows));
                    thumb.Unaffordable = item.Price > budget;
                    thumb.Selected = index == SelectedIndex;
                    var guid = item.GUID;
                    thumb.OnHover = (t, on) => { if (on) HoverGUID = guid; else if (HoverGUID == guid) HoverGUID = 0; };
                    thumb.OnClick = (t) => BuyItem(t.Index);
                    Add(thumb);
                    Thumbs.Add(thumb);
                }
            }
            if (ArrowRight != null) ArrowRight.Position = new Vector2(ThumbStart + cols * ThumbStep, ArrowY);
            if (ArrowLeft != null) ArrowLeft.Disabled = FirstItem == 0;
            if (ArrowRight != null) ArrowRight.Disabled = FirstItem + cols * rows >= Items.Count;
            UpdateGrowTab();
        }

        // ------------------------------------------------------------------
        // Object pictures and names
        // ------------------------------------------------------------------

        private static Dictionary<uint, Texture2D> Icons = new Dictionary<uint, Texture2D>();
        private static Dictionary<uint, string> Names = new Dictionary<uint, string>();

        private static Dictionary<uint, bool> PictureCache = new Dictionary<uint, bool>();

        /// <summary>SimsRedux: does this object have a catalog picture? (The hidden and test objects don't.)</summary>
        public static bool HasCatalogPicture(uint guid)
        {
            bool has;
            if (PictureCache.TryGetValue(guid, out has)) return has;
            has = false;
            try
            {
                var obj = Content.Get().WorldObjects.Get(guid);
                has = obj?.OBJ != null && obj.Resource.Get<BMP>(obj.OBJ.CatalogStringsID) != null;
            }
            catch { }
            PictureCache[guid] = has;
            return has;
        }

        private Texture2D GetIcon(uint guid)
        {
            Texture2D tex;
            if (Icons.TryGetValue(guid, out tex)) return tex;
            tex = null;
            try
            {
                var obj = Content.Get().WorldObjects.Get(guid);
                var bmp = obj?.Resource.Get<BMP>(obj.OBJ.CatalogStringsID);
                if (bmp != null) tex = bmp.GetTexture(GameFacade.GraphicsDevice);
            }
            catch { }
            Icons[guid] = tex;
            return tex;
        }

        private string GetName(ObjectCatalogItem item)
        {
            string name;
            if (Names.TryGetValue(item.GUID, out name)) return name;
            name = item.Name;
            try
            {
                var obj = Content.Get().WorldObjects.Get(item.GUID);
                var str = obj?.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0);
                if (!string.IsNullOrEmpty(str)) name = str;
            }
            catch { }
            Names[item.GUID] = name;
            return name;
        }

        // SimsRedux: the catalog description (used to sort an item when its name says nothing)
        private static Dictionary<uint, string> Descriptions = new Dictionary<uint, string>();
        private string GetDescription(ObjectCatalogItem item)
        {
            string desc;
            if (Descriptions.TryGetValue(item.GUID, out desc)) return desc;
            desc = "";
            try
            {
                var obj = Content.Get().WorldObjects.Get(item.GUID);
                var ctss = obj?.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID);
                if (ctss != null && ctss.Length > 1) desc = ctss.GetString(1) ?? "";
            }
            catch { }
            Descriptions[item.GUID] = desc;
            return desc;
        }

        // ------------------------------------------------------------------
        // Buying (same steps as Simitone's catalog)
        // ------------------------------------------------------------------

        private void BuyItem(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            var control = Game.LotControl;
            var holder = control.ObjectHolder;
            holder.ClearSelected();
            if (control.CustomControl != null)
            {
                control.CustomControl.Release();
                control.CustomControl = null;
            }

            var item = Items[index];
            Place(item.GUID);
            SelectedIndex = index;
            foreach (var t in Thumbs) t.Selected = t.Index == SelectedIndex;
        }

        /// <summary>Starts placing a new one of this object (not from the catalog's pictures).</summary>
        private void BuyGuid(uint guid)
        {
            var control = Game.LotControl;
            control.ObjectHolder.ClearSelected();
            if (control.CustomControl != null)
            {
                control.CustomControl.Release();
                control.CustomControl = null;
            }
            Place(guid);
            Deselect();
        }

        private void Place(uint guid)
        {
            var control = Game.LotControl;
            var holder = control.ObjectHolder;
            var bought = control.vm.Context.CreateObjectInstance(guid, LotTilePos.OUT_OF_WORLD, Direction.NORTH, holder.UseNet);
            if (bought != null && bought.Objects.Count != 0)
            {
                control.QueryPanel.Active = false; // (the item card above the bar shows what it is instead)
                HeldGUID = guid;
                holder.SetSelected(bought);
            }
        }

        /// <summary>SimsRedux: the item card above the bar - for the picture under the mouse, or what's being placed.</summary>
        private UIReduxItemCard Card = new UIReduxItemCard();
        private uint HoverGUID, HeldGUID;

        public static string DescribeForSale(VMEntity ent)
        {
            try
            {
                var def = ent.MasterDefinition ?? ent.Object.OBJ;
                var name = ent.Object.Resource.Get<CTSS>(def.CatalogStringsID)?.GetString(0) ?? ent.ToString();
                var parts = new List<string>();
                Action<string, int> add = (label, v) => { if (v != 0) parts.Add(label + " " + (v > 0 ? "+" : "") + v); };
                add("Hunger", def.RatingHunger); add("Comfort", def.RatingComfort); add("Hygiene", def.RatingHygiene);
                add("Bladder", def.RatingBladder); add("Energy", def.RatingEnergy); add("Fun", def.RatingFun); add("Room", def.RatingRoom);
                var price = (int)def.Price;
                return name + (price > 0 ? "  §" + price.ToString("##,#0") : "") + (parts.Count > 0 ? "   " + string.Join("  ", parts) : "");
            }
            catch { return null; }
        }

        private void Holder_OnPickup(UIObjectSelection holding, UpdateState state)
        {
            if (!Visible) return;
            var control = Game.LotControl;
            control.PickupPanel.Opacity = 0f; // the classic bar shows its own "sell" message instead
            try
            {
                var entity = holding.RealEnt ?? holding.Group.BaseObject;
                var def = entity.MasterDefinition ?? entity.Object.OBJ;
                var name = entity.Object.Resource.Get<CTSS>(def.CatalogStringsID)?.GetString(0) ?? entity.ToString();
                var canSell = entity.IsUserMovable(control.vm.Context, true) == FSO.SimAntics.Model.VMPlacementError.Success;
                var value = entity.MultitileGroup.Price;
                EnsureSellCatcher();
                SellCatcher.Title = canSell ? ("Sell " + name + (value > 0 ? " for §" + value.ToString("##,#0") : "")) : (name + " can't be sold");
                SellCatcher.Hint = canSell ? "Click here to sell it.  Esc puts it back.  Or pick a new colour below (or right-click it on the lot)." : "Esc puts it back.";
                var guid = (entity.MultitileGroup.MultiTile && entity.MasterDefinition != null) ? entity.MasterDefinition.GUID : entity.Object.OBJ.GUID;
                ShowSellExtras(holding.MoveTarget != 0 ? holding.MoveTarget : entity.ObjectID, guid);
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Sell info: " + e.Message);
            }
        }

        // ------------------------------------------------------------------
        // Colours (SimsRedux): while holding an object from the lot, its other colours and a
        // Tint button are shown on the bar. Picking one puts the object back and changes it.
        // ------------------------------------------------------------------

        private List<UIElement> SellExtras = new List<UIElement>();
        private const int ExtrasY = 133;

        private void ClearSellExtras()
        {
            foreach (var e in SellExtras) Remove(e);
            SellExtras.Clear();
        }

        private void ShowSellExtras(short objectID, uint currentGUID)
        {
            ClearSellExtras();
            int x = 300;
            var variants = ReduxColourVariants.Get(currentGUID);
            var catalog = Content.Get().WorldCatalog;
            var current = catalog.GetItemByGUID(currentGUID);
            int currentPrice = (current == null) ? 0 : (int)current.Value.Price;
            if (variants.Count > 1)
            {
                for (int i = 0; i < variants.Count && x < Columns * ThumbStep + ThumbX - 140; i++)
                {
                    var guid = variants[i];
                    var item = catalog.GetItemByGUID(guid);
                    if (item == null) continue;
                    var thumb = new UIClassicCatalogThumb(ThumbFrame, GetIcon(guid) ?? UnknownThumb, i);
                    thumb.Position = new Vector2(x, ExtrasY);
                    thumb.Selected = guid == currentGUID;
                    var diff = (int)item.Value.Price - currentPrice;
                    var tip = GetName(item.Value) + (guid == currentGUID ? "  (this one)" : (diff == 0 ? "  (free)" : (diff > 0 ? "  (+§" + diff.ToString("##,#0") + ")" : "  (-§" + (-diff).ToString("##,#0") + " back)")));
                    thumb.OnHover = (t, on) => { if (on) HoverText = tip; else if (HoverText == tip) HoverText = null; };
                    thumb.OnClick = (t) => SwapTo(objectID, currentGUID, guid);
                    Add(thumb);
                    SellExtras.Add(thumb);
                    x += ThumbStep;
                }
            }
            var tint = new UITS1TileButton(TS1UIGraphics.Get("cpanel/Buttons/ButtonTileDialog.bmp"), "Tint...", 120);
            tint.Position = new Vector2(x + 10, ExtrasY + 6);
            tint.OnButtonClick += (b) => OpenTint(objectID);
            Add(tint);
            SellExtras.Add(tint);
        }

        private void SwapTo(short objectID, uint currentGUID, uint newGUID)
        {
            if (newGUID == currentGUID) return;
            var vm = Game.vm;
            var holder = Game.LotControl.ObjectHolder;
            if (holder.Holding != null) holder.Cancel(); // put it back first
            ClearSellExtras();
            UIClassicColourDialog.TrySwap(Game, objectID, currentGUID, newGUID);
        }

        private void OpenTint(short objectID)
        {
            var holder = Game.LotControl.ObjectHolder;
            if (holder.Holding != null) holder.Cancel(); // put it back first, then recolour it where it stands
            ClearSellExtras();
            UIScreen.GlobalShowDialog(new UIClassicTintDialog(Game, objectID), true);
        }

        private void Holder_OnPutDown(UIObjectSelection holding, UpdateState state)
        {
            if (!Visible) return;
            var control = Game.LotControl;
            control.QueryPanel.Active = false;
            control.PickupPanel.Opacity = 0f;
            if (SelectedIndex != -1)
            {
                if (!holding.IsBought && state.ShiftDown)
                {
                    // hold Shift to place another of the same thing
                    var dir = holding.Dir;
                    BuyItem(SelectedIndex);
                    control.QueryPanel.SetShown(false);
                    if (control.ObjectHolder.Holding != null) control.ObjectHolder.Holding.Dir = dir;
                }
                else Deselect();
            }
        }

        private void Holder_OnDelete(UIObjectSelection holding, UpdateState state)
        {
            if (!Visible) return;
            Game.LotControl.QueryPanel.Active = false;
            Game.LotControl.PickupPanel.Opacity = 0f;
            Deselect();
        }

        private void Deselect()
        {
            SelectedIndex = -1;
            foreach (var t in Thumbs) t.Selected = false;
        }

        /// <summary>Drops whatever is being held (when leaving the catalog or Buy mode).</summary>
        private void ClearHolding()
        {
            var control = Game.LotControl;
            if (control == null) return;
            control.QueryPanel.Active = false;
            control.PickupPanel.Opacity = 0f;
            if (control.CustomControl != null)
            {
                control.CustomControl.Release();
                control.CustomControl = null;
            }
            if (control.ObjectHolder.Holding != null) control.ObjectHolder.ClearSelected();
            Deselect();
        }

        // ------------------------------------------------------------------
        // Update / draw
        // ------------------------------------------------------------------

        public override void Update(UpdateState state)
        {
            if (Visible && !WasVisible)
            {
                // entering Buy mode: the lot type can change between visits
                if (GetMode() != Mode || CurrentView == View.Start) ShowStart();
            }
            else if (!Visible && WasVisible)
            {
                ClearHolding();
                HoverText = null;
            }
            WasVisible = Visible;

            if (Visible && CurrentView != View.Start && Columns != BuiltColumns) RebuildThumbs();
            var holding = Game.LotControl?.ObjectHolder?.Holding;
            if (SellCatcher != null)
            {
                SellCatcher.Visible = Visible && holding != null && holding.IsBought;
                if (!SellCatcher.Visible && SellExtras.Count > 0) ClearSellExtras();
                if (SellCatcher.Visible) SellCatcher.Width = (int)(GlobalSettings.Default.GraphicsWidth / ((Parent?.Parent as UIClassicUCP)?.UIScale ?? 1f)) - 218;
            }
            if (Game.LotControl != null && Visible)
            {
                Game.LotControl.PickupPanel.Opacity = 0f;
                Game.LotControl.QueryPanel.Active = false; // the Simitone info card - the classic bar shows it instead
            }
            if (holding == null || holding.IsBought) HeldGUID = 0;
            if (!Visible) HoverGUID = 0;
            if (Visible)
            {
                // search and dropper sit on the bar's top edge, at the right
                var width = GlobalSettings.Default.GraphicsWidth / ((Parent?.Parent as UIClassicUCP)?.UIScale ?? 1f);
                Search.Position = new Vector2((int)(width - UIReduxCatalogSearch.TotalW - 12), GridTop - UIReduxCatalogSearch.BoxH);
                if (GrowTab != null && CurrentView == View.Start) GrowTab.Visible = false;
                // the dropper: I to pick up / put away, Esc to put it away
                if (!UIRevampedCamera.IsTyping(state))
                {
                    if (state.NewKeys.Contains(Keys.I)) ToggleDropper();
                    else if (state.NewKeys.Contains(Keys.Escape) && DropperActive) ToggleDropper();
                }
                Search.DropperOn = DropperActive;
            }
            else if (DropperActive && Game.LotControl != null)
            {
                Game.LotControl.CustomControl.Release();
                Game.LotControl.CustomControl = null;
            }
            base.Update(state);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;

            if (CurrentView == View.Start)
            {
                // dark wells behind the two rows of icons (Redux: just the icons on the bar)
                if (!ReduxTheme.Redux)
                {
                    Fill(batch, new Rectangle(CatX - 4, RowAY - 22, CatW * 8 + 8, 44), new Color(0, 0, 50) * 0.6f);
                    Fill(batch, new Rectangle(CatX - 4, RowBY - 22, CatW * 8 + 8, 44), new Color(0, 0, 50) * 0.6f);
                }
                var label = HoverText ?? "";
                DrawLocalString(batch, label, new Vector2(CatX + CatW * 8 + 16, BarY + 38), Title, new Rectangle(0, 0, 220, 20), TextAlignment.Left | TextAlignment.Middle);
            }
            else
            {
                // category name next to the back button, and how many items there are
                DrawLocalString(batch, CurrentName, new Vector2(SubX + 38, BarY + 4), Title, new Rectangle(0, 0, 135, 22), TextAlignment.Left | TextAlignment.Middle);
                if (Items.Count == 0)
                    DrawLocalString(batch, "Nothing to buy here.", new Vector2(ThumbStart, BarY + 40), Small, new Rectangle(0, 0, 300, 16), TextAlignment.Left | TextAlignment.Middle);
                // SimsRedux: the grown grid sits on a panel that rises out of the bar
                if (GridTop < BarY)
                {
                    int gw = Math.Max(1, Columns) * ThumbStep + 12;
                    DrawLocalTexture(batch, ReduxTheme.Box(gw, BarY - GridTop + 16, 10, ReduxTheme.Hi, ReduxTheme.Deep, ReduxTheme.Rim, 1), new Vector2(ThumbStart - 6, GridTop - 4));
                }
                // the chosen subsort glows behind its icon (the icons have no background of their own)
                if (ReduxTheme.Redux)
                {
                    foreach (var sub in Subsorts)
                    {
                        if (sub.Button == null || !sub.Button.Selected) continue;
                        DrawLocalTexture(batch, ReduxTheme.Box(32, 32, 8, ReduxTheme.Gloss * 0.9f, ReduxTheme.Hi * 0.9f, ReduxTheme.Glow, 1), sub.Button.Position);
                    }
                }
            }

            base.Draw(batch);

            // name / price of whatever is under the mouse, in a small box above the bar
            // the item card: the picture under the mouse, or else what's being placed
            var cardGUID = HoverGUID != 0 ? HoverGUID : HeldGUID;
            if (cardGUID != 0 && (SellCatcher == null || !SellCatcher.Visible))
            {
                Card.SetObject(cardGUID, GetIcon(cardGUID));
                Card.Draw(this, batch, SubX, GridTop - (GrowTab != null && GrowTab.Visible ? UIReduxGrowTab.H : 0) - 2);
                return;
            }
            Card.Hide();
            var tip = HoverText;
            if ((CurrentView != View.Start || SellExtras.Count > 0 || DropperActive) && !string.IsNullOrEmpty(tip))
            {
                int w = 12 + tip.Length * 6;
                var box = new Rectangle(ThumbX, BarY - 20, w, 17);
                Fill(batch, box, new Color(0, 0, 82));
                Fill(batch, new Rectangle(box.X, box.Y, box.Width, 1), new Color(120, 120, 200));
                DrawLocalString(batch, tip, new Vector2(box.X, box.Y), Small, new Rectangle(0, 0, box.Width, box.Height), TextAlignment.Center | TextAlignment.Middle);
            }
        }

        private void Fill(UISpriteBatch batch, Rectangle r, Color c)
        {
            DrawLocalTexture(batch, Px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), c);
        }
    }

    /// <summary>SimsRedux: one object picture in the classic catalog (ThumbTemplate.bmp frame + catalog icon).</summary>
    public class UIClassicCatalogThumb : UIElement
    {
        public int Index;
        public bool Selected, Unaffordable, Hover;
        public Action<UIClassicCatalogThumb> OnClick;
        public Action<UIClassicCatalogThumb, bool> OnHover;
        private Texture2D Frame, Icon;
        private bool Down;

        public UIClassicCatalogThumb(Texture2D frame, Texture2D icon, int index)
        {
            Frame = frame;
            Icon = icon;
            Index = index;
            ListenForMouse(new Rectangle(0, 0, 45, 45), (type, state) =>
            {
                switch (type)
                {
                    case UIMouseEventType.MouseOver: Hover = true; OnHover?.Invoke(this, true); break;
                    case UIMouseEventType.MouseOut: Hover = false; Down = false; OnHover?.Invoke(this, false); break;
                    case UIMouseEventType.MouseDown: Down = true; break;
                    case UIMouseEventType.MouseUp:
                        if (Down && Hover)
                        {
                            FSO.HIT.HITVM.Get().PlaySoundEvent(UISounds.Click);
                            OnClick?.Invoke(this);
                        }
                        Down = false;
                        break;
                }
            });
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            // frame: 0 normal, 1 hover, 2 selected, 3 selected + hover
            if (Frame != null && Frame.Width > 1)
            {
                int fw = Frame.Width / 4;
                int f = Selected ? (Hover ? 3 : 2) : (Hover ? 1 : 0);
                DrawLocalTexture(batch, Frame, new Rectangle(f * fw, 0, fw, Frame.Height), Vector2.Zero);
            }
            if (Icon != null && Icon.Width > 1)
            {
                // Sims 1 catalog pictures hold two frames side by side: use the first
                var src = (Icon.Width >= Icon.Height * 1.5f) ? new Rectangle(0, 0, Icon.Width / 2, Icon.Height) : new Rectangle(0, 0, Icon.Width, Icon.Height);
                float s = 41f / Math.Max(src.Width, src.Height);
                var size = new Vector2(src.Width * s, src.Height * s);
                var pos = new Vector2((45 - size.X) / 2, (45 - size.Y) / 2);
                DrawLocalTexture(batch, Icon, src, pos, new Vector2(s, s), Unaffordable ? new Color(110, 110, 110) : Color.White);
            }
        }
    }

    /// <summary>
    /// SimsRedux: covers the classic bar while you hold an object from the lot. Click it to sell the
    /// object (or put back a new one), like dropping it on the catalog in the original game.
    /// </summary>
    public class UIClassicSellCatcher : UIElement
    {
        public Action OnClick;
        public string Title = "", Hint = "";
        public int Width = 800;
        public bool ShowColoursLabel = true;
        private TextStyle Small, Big;
        private bool Hover;
        private UIMouseEventRef Region;

        public UIClassicSellCatcher(TextStyle small, TextStyle big)
        {
            Small = small;
            Big = big;
            Visible = false;
            Region = ListenForMouse(new Rectangle(218, 83, Width, 100), (type, state) =>
            {
                if (type == UIMouseEventType.MouseOver) Hover = true;
                else if (type == UIMouseEventType.MouseOut) Hover = false;
                else if (type == UIMouseEventType.MouseDown && Visible)
                {
                    FSO.HIT.HITVM.Get().PlaySoundEvent(UISounds.Click);
                    OnClick?.Invoke();
                }
            });
        }

        public override void Update(UpdateState state)
        {
            Region.Region.Width = Width;
            base.Update(state);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            DrawLocalTexture(batch, px, null, new Vector2(218, 83), new Vector2(Width, 100), new Color(0, 0, 40) * (Hover ? 0.75f : 0.6f));
            DrawLocalString(batch, Title, new Vector2(236, 83 + 6), Big, new Rectangle(0, 0, 700, 20), TextAlignment.Left | TextAlignment.Middle);
            DrawLocalString(batch, Hint, new Vector2(236, 83 + 26), Small, new Rectangle(0, 0, 700, 18), TextAlignment.Left | TextAlignment.Middle);
            if (ShowColoursLabel) DrawLocalString(batch, "Colours:", new Vector2(236, 83 + 62), Small, new Rectangle(0, 0, 60, 18), TextAlignment.Left | TextAlignment.Middle);
        }
    }
}
