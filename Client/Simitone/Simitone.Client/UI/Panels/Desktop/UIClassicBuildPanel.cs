using FSO.Client;
using FSO.Client.UI.Controls.Catalog;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Content.Interfaces;
using FSO.Content.Model;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.NetPlay.Model.Commands;
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
    /// SimsRedux: the original Sims 1 Build mode on the classic bottom bar.
    ///
    ///   Left: the tool buttons, in two rows like the original:
    ///     Terrain, Water, Wall &amp; Fence, Wallpaper, Floor, Hand
    ///     Door, Window, Stairs, Fireplace, Plants, Roof
    ///   Right (past the divider): whatever the chosen tool offers. Terrain shows Raise/Lower,
    ///   Level and Grass; Water shows Pool and Water; Roof shows the four pitches and the roof
    ///   patterns; everything else shows a row of pictures to pick from (wall styles, wallpapers,
    ///   floors, doors, windows...).
    ///
    /// The tools themselves are the same ones Simitone uses, so walls, floors, terrain and roofs
    /// all work exactly as before. Coordinates are in the classic control panel's layout.
    /// </summary>
    public class UIClassicBuildPanel : UIContainer
    {
        private TS1GameScreen Game;
        private Texture2D Px, ThumbFrame, UnknownThumb, Divider;
        private TextStyle Small, Title;
        private static readonly Color TextColor = new Color(220, 220, 250);

        // ---- layout ----
        private const int BarY = 83;
        private const int ToolX = 232, ToolStep = 40, ToolRowA = 108, ToolRowB = 157;
        private const int DividerX = 476;
        private const int AreaX = 486;                       // first thing right of the divider
        private const int ArrowLX = 486, ArrowY = 109;
        private const int ThumbX = 499, ThumbStep = 47, ThumbRow1 = 87, ThumbRow2 = 135;

        private enum Tool { None, Terrain, Water, Wall, Wallpaper, Floor, Hand, Door, Window, Stairs, Fireplace, Plants, Roof, Search }

        private class ToolInfo
        {
            public Tool Tool;
            public string Art, Name;
            public int Row, Col;
            public UITS1Button Button;
        }

        private List<ToolInfo> Tools = new List<ToolInfo>
        {
            new ToolInfo { Tool = Tool.Terrain,   Art = "toolBtnTerrain",   Name = "Terrain Tools",    Row = 0, Col = 0 },
            new ToolInfo { Tool = Tool.Water,     Art = "toolBtnWater",     Name = "Water Tools",      Row = 0, Col = 1 },
            new ToolInfo { Tool = Tool.Wall,      Art = "toolBtnWall",      Name = "Wall & Fence",     Row = 0, Col = 2 },
            new ToolInfo { Tool = Tool.Wallpaper, Art = "toolBtnWallpaper", Name = "Wallpaper",        Row = 0, Col = 3 },
            new ToolInfo { Tool = Tool.Floor,     Art = "toolBtnFloor",     Name = "Floors",           Row = 0, Col = 4 },
            new ToolInfo { Tool = Tool.Hand,      Art = "toolBtnHand",      Name = "Hand (pick up and move)", Row = 0, Col = 5 },
            new ToolInfo { Tool = Tool.Door,      Art = "toolBtnDoor",      Name = "Doors",            Row = 1, Col = 0 },
            new ToolInfo { Tool = Tool.Window,    Art = "toolBtnWindow",    Name = "Windows",          Row = 1, Col = 1 },
            new ToolInfo { Tool = Tool.Stairs,    Art = "toolBtnStairs",    Name = "Stairs",           Row = 1, Col = 2 },
            new ToolInfo { Tool = Tool.Fireplace, Art = "toolBtnFireplace", Name = "Fireplaces",       Row = 1, Col = 3 },
            new ToolInfo { Tool = Tool.Plants,    Art = "toolBtnTree",      Name = "Plants",           Row = 1, Col = 4 },
            new ToolInfo { Tool = Tool.Roof,      Art = "toolBtnRoof",      Name = "Roofs",            Row = 1, Col = 5 },
        };

        // what each catalog tool sells (Sims 1 build categories)
        private static readonly Dictionary<Tool, sbyte> ObjectCategories = new Dictionary<Tool, sbyte>
        {
            { Tool.Door, 8 }, { Tool.Window, 9 }, { Tool.Stairs, 10 }, { Tool.Plants, 11 }, { Tool.Fireplace, 12 },
        };

        /// <summary>One thing to pick on the right side: an object to buy, or a tool to switch on.</summary>
        private class Entry
        {
            public string Name, Description;
            public int Price;
            public uint GUID;                 // catalog object (doors, windows...)
            public Type Control;              // or a build tool (walls, floors...)
            public List<int> Parameters;
            public UICatalogResProvider Res;
            public ulong ResID;
            public Texture2D Icon;
            public string Type;               // SimsRedux: its group (Carpet, Wood, Fences...)
        }

        // SimsRedux: the group tabs (All, Carpet, Wood...) and the tab picked last for each tool
        private UIReduxTabStrip TypeTabs;
        private List<Entry> AllEntries = new List<Entry>();
        private static Dictionary<Tool, string> ChosenType = new Dictionary<Tool, string>();

        private Tool CurrentTool = Tool.None;
        private List<Entry> Entries = new List<Entry>();
        private int FirstItem;
        private int SelectedIndex = -1;
        private List<UIElement> AreaItems = new List<UIElement>();
        private List<UIClassicCatalogThumb> Thumbs = new List<UIClassicCatalogThumb>();
        private UITS1Button ArrowLeft, ArrowRight;
        private string HoverText;
        private string AreaHint;
        private bool WasVisible;
        private UIClassicSellCatcher SellCatcher;

        public UIClassicBuildPanel(TS1GameScreen game)
        {
            Game = game;
            Px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            ThumbFrame = TS1UIGraphics.Get("cpanel/Catalog/ThumbTemplate.bmp");
            UnknownThumb = TS1UIGraphics.Get("cpanel/CatalogUnknown.bmp");
            Divider = TS1UIGraphics.Get("cpanel/Backgrounds/BuildToothpkLeft.bmp");
            Small = TextStyle.DefaultLabel.Clone(); Small.Size = 10; Small.Color = TextColor;
            Title = TextStyle.DefaultLabel.Clone(); Title.Size = 12; Title.Color = TextColor;

            foreach (var t in Tools)
            {
                var tex = TS1UIGraphics.Get("cpanel/Buttons/" + t.Art + ".bmp");
                var btn = new UITS1Button(tex, 4);
                btn.SelectedFrame = 1;
                int w = Math.Max(1, tex.Width / 4), h = tex.Height;
                float cx = ToolX + ToolStep * t.Col + ToolStep / 2f;
                float cy = t.Row == 0 ? ToolRowA : ToolRowB;
                btn.Position = new Vector2((int)(cx - w / 2f), (int)(cy - h / 2f));
                var info = t;
                btn.OnButtonClick += (b) => SelectTool(info.Tool);
                AddHover(btn, t.Name);
                Add(btn);
                t.Button = btn;
            }

            // SimsRedux: search box and dropper, just above the bar
            Search = new UIReduxCatalogSearch();
            Search.Position = new Vector2(ToolX, BarY - UIReduxCatalogSearch.BoxH);
            Search.OnSearch += SearchFor;
            Search.OnDropper += ToggleDropper;
            Add(Search);

            var holder = game.LotControl.ObjectHolder;
            holder.OnPickup += Holder_OnPickup;
            holder.OnPutDown += Holder_OnPutDown;
            holder.OnDelete += Holder_OnDelete;
        }

        // ------------------------------------------------------------------
        // Search and the dropper (SimsRedux)
        // ------------------------------------------------------------------

        private UIReduxCatalogSearch Search;

        /// <summary>Every Build item (walls, wallpaper, floors, doors, windows, stairs, fireplaces, plants) whose name has the typed letters.</summary>
        private void SearchFor(string text)
        {
            if (text.Length < 2)
            {
                if (CurrentTool == Tool.Search) SelectTool(Tool.Hand);
                return;
            }
            var words = text.ToLowerInvariant();
            var all = new List<Entry>();
            try { all.AddRange(WallEntries()); } catch { }
            try { all.AddRange(WallpaperEntries()); } catch { }
            try { all.AddRange(FloorEntries(false)); } catch { }
            foreach (var cat in ObjectCategories.Values)
                try { all.AddRange(ObjectEntries(cat)); } catch { }
            var results = all.Where(x => (x.Name ?? "").ToLowerInvariant().Contains(words)).ToList();
            ClearHolding();
            ClearArea();
            CurrentTool = Tool.Search;
            foreach (var t in Tools) t.Button.Selected = false;
            AreaHint = results.Count == 0 ? "Nothing called \"" + text + "\"." : null;
            if (results.Count > 0) ShowEntries(results);
        }

        private bool DropperActive { get { return Game.LotControl?.CustomControl is UIReduxDropper; } }

        /// <summary>The dropper: click something on the lot (or a floor) to pick the same thing.</summary>
        private void ToggleDropper()
        {
            var lot = Game.LotControl;
            if (lot == null) return;
            if (DropperActive) { lot.CustomControl.Release(); lot.CustomControl = null; return; }
            ClearHolding();
            ToolCursor = FSO.Common.Rendering.Framework.CursorType.Normal;
            lot.CustomControl = new UIReduxDropper(lot, DropperObject, DropperFloor,
                () => HoverText = "Nothing to pick there - click an object or a floor.");
            HoverText = "Dropper: click an object or a floor on the lot.";
        }

        private void PutDropperAway()
        {
            var lot = Game.LotControl;
            if (lot?.CustomControl is UIReduxDropper) { lot.CustomControl.Release(); lot.CustomControl = null; }
        }

        /// <summary>Shows the page with this picture on it.</summary>
        private void ShowIndex(int index)
        {
            int perPage = Math.Max(1, Columns * Rows);
            FirstItem = (index / perPage) * perPage;
            RebuildThumbs();
        }

        private void DropperObject(uint guid)
        {
            PutDropperAway();
            var item = Content.Get().WorldCatalog.GetItemByGUID(guid);
            if (item != null)
            {
                var tool = ObjectCategories.Where(x => x.Value == item.Value.Category).Select(x => x.Key).FirstOrDefault();
                if (tool != Tool.None)
                {
                    ChosenType[tool] = ReduxCatalogTypes.All;
                    SelectTool(tool);
                    var index = Entries.FindIndex(x => x.GUID == guid);
                    if (index >= 0) { ShowIndex(index); PickEntry(index); return; }
                }
            }
            // a Buy mode object (or one hidden from the catalog): just place another one
            BuyObject(guid);
        }

        private void DropperFloor(ushort pattern)
        {
            PutDropperAway();
            ChosenType[Tool.Floor] = ReduxCatalogTypes.All;
            SelectTool(Tool.Floor);
            var index = Entries.FindIndex(x => x.Control == typeof(UIFloorPainter) && x.Parameters != null && x.Parameters.Count > 0 && x.Parameters[0] == pattern);
            if (index >= 0) { ShowIndex(index); PickEntry(index); }
            else HoverText = "That floor isn't in the catalog.";
        }

        private void AddHover(UITS1Button btn, string name)
        {
            btn.OnButtonHover += (b) => HoverText = name;
            btn.OnButtonExit += (b) => { if (HoverText == name) HoverText = null; };
        }

        // ------------------------------------------------------------------
        // Tools
        // ------------------------------------------------------------------

        private void SelectTool(Tool tool)
        {
            ClearHolding();
            ClearArea();
            CurrentTool = tool;
            foreach (var t in Tools) t.Button.Selected = t.Tool == tool;
            AreaHint = null;
            if (tool != Tool.Search) Search?.ClearText();

            switch (tool)
            {
                case Tool.Terrain: ShowTerrain(); break;
                case Tool.Water: ShowWater(); break;
                case Tool.Roof: ShowRoof(); break;
                case Tool.Hand:
                    AreaHint = "Click an object to pick it up and move it. Press Delete or click the bar to sell it.";
                    break;
                case Tool.Wall: ShowSorted(WallEntries(), ReduxCatalogTypes.Walls, "Walls"); break;
                case Tool.Wallpaper: ShowSorted(WallpaperEntries(), ReduxCatalogTypes.Wallpapers, "Other"); break;
                case Tool.Floor: ShowSorted(FloorEntries(false), ReduxCatalogTypes.Floors, "Other"); break;
                case Tool.Door: ShowSorted(ObjectEntries(ObjectCategories[tool]), ReduxCatalogTypes.Doors, "Doors"); break;
                case Tool.Plants: ShowSorted(ObjectEntries(ObjectCategories[tool]), ReduxCatalogTypes.Plants, "Other"); break;
                default:
                    sbyte cat;
                    if (ObjectCategories.TryGetValue(tool, out cat)) ShowEntries(ObjectEntries(cat));
                    break;
            }
        }

        private void ClearArea()
        {
            foreach (var e in AreaItems) Remove(e);
            AreaItems.Clear();
            foreach (var t in Thumbs) Remove(t);
            Thumbs.Clear();
            ArrowLeft = ArrowRight = null;
            if (GrowTab != null) GrowTab.Visible = false;
            TypeTabs = null;
            HoverEntry = null;
            AllEntries = new List<Entry>();
            Entries = new List<Entry>();
            SelectedIndex = -1;
            FirstItem = 0;
            HoverText = null;
        }

        private UITS1Button AreaButton(string path, int x, int y, string name, Action click, int frames = 4)
        {
            var tex = TS1UIGraphics.Get(path);
            var btn = new UITS1Button(tex, frames);
            btn.SelectedFrame = 1;
            btn.Position = new Vector2(x, y);
            btn.OnButtonClick += (b) => click();
            AddHover(btn, name);
            Add(btn);
            AreaItems.Add(btn);
            return btn;
        }

        // ---- Terrain: Raise / Lower, Level, Grass ----
        private void ShowTerrain()
        {
            UITS1Button raise = null, level = null, grass = null;
            Action<UITS1Button> pick = (b) => { foreach (var x in new[] { raise, level, grass }) if (x != null) x.Selected = x == b; };
            raise = AreaButton("cpanel/Build/TerrainUpIcon.bmp", AreaX + 4, BarY + 8, "Raise / Lower Terrain: click a corner and drag up or down. Ctrl smooths.",
                () => { pick(raise); StartTool(typeof(UITerrainRaiser), new List<int>(), 0, new UICatalogTerrainResProvider()); });
            level = AreaButton("cpanel/Build/TerrainLevelIcon.bmp", AreaX + 4, BarY + 52, "Level Terrain: drag across the ground to flatten it.",
                () => { pick(level); StartTool(typeof(UITerrainFlatten), new List<int>(), 1, new UICatalogTerrainResProvider()); });
            grass = AreaButton("cpanel/HDBuild/TerrainGrassIcon.bmp", AreaX + 52, BarY + 8, "Grass Tool: paint grass. Ctrl removes it.",
                () => { pick(grass); StartTool(typeof(UIGrassPaint), new List<int>(), 2, new UICatalogTerrainResProvider()); });
            AreaHint = "Raise / Lower, Level, Grass";
        }

        // ---- Water: Pool and Water (floor tools with the two special water "floors") ----
        private void ShowWater()
        {
            UITS1Button pool = null, water = null;
            var floors = Content.Get().WorldFloors;
            string poolName = SafeFloorName(65535, "Pool"), waterName = SafeFloorName(65534, "Water");
            pool = AreaButton("cpanel/Build/PoolToolIcon.bmp", AreaX + 4, BarY + 6, poolName + "  (drag to dig a pool)", () =>
            {
                pool.Selected = true; water.Selected = false;
                StartTool(typeof(UIFloorPainter), new List<int> { 65535 }, 65535, new UICatalogFloorResProvider());
            });
            water = AreaButton("cpanel/HDBuild/WaterToolIcon.bmp", AreaX + 4 + 52, BarY + 6, waterName + "  (drag to add water)", () =>
            {
                water.Selected = true; pool.Selected = false;
                StartTool(typeof(UIFloorPainter), new List<int> { 65534 }, 65534, new UICatalogFloorResProvider());
            });
            AreaHint = "Pool and water tools. Drag on the lot.";
        }

        private string SafeFloorName(ushort id, string fallback)
        {
            try
            {
                var floor = Content.Get().WorldFloors.List().Cast<FloorReference>().FirstOrDefault(x => x.ID == id);
                if (floor != null && !string.IsNullOrEmpty(floor.Name)) return floor.Name + "  §" + floor.Price;
            }
            catch { }
            return fallback;
        }

        // ---- Roof: four pitches, then the roof patterns ----
        private static readonly string[] PitchArt = { "roofflat", "roofshallow", "roofmedium", "roofsteep" };
        private static readonly string[] PitchNames = { "Flat Roof", "Shallow Roof", "Medium Roof", "Steep Roof" };
        private static readonly float[] Pitches = { 0f, 0.4f, 0.66f, 1.0f };
        private List<UITS1Button> PitchButtons = new List<UITS1Button>();

        private void ShowRoof()
        {
            PitchButtons.Clear();
            var arch = Game.vm.Context.Architecture;
            for (int i = 0; i < 4; i++)
            {
                var index = i;
                var btn = AreaButton("cpanel/Build/" + PitchArt[i] + ".bmp", AreaX + 2, BarY + 6 + i * 23, PitchNames[i], () => SetPitch(index));
                PitchButtons.Add(btn);
            }
            MarkPitch(arch.RoofPitch);

            var res = new UICatalogRoofResProvider();
            var list = new List<Entry>();
            int total = Content.Get().WorldRoofs.Count;
            for (int i = 0; i < total; i++)
                list.Add(new Entry { Name = "Roof pattern " + (i + 1), Price = 0, Control = typeof(UIRoofer), Parameters = new List<int> { i }, Res = res, ResID = (ulong)i });
            ShowEntries(list, 34);
        }

        private void SetPitch(int index)
        {
            var arch = Game.vm.Context.Architecture;
            Game.vm.SendCommand(new VMNetSetRoofCmd { Pitch = Pitches[index], Style = arch.RoofStyle });
            FSO.HIT.HITVM.Get().PlaySoundEvent(UISounds.Click);
            MarkPitch(Pitches[index]);
        }

        private void MarkPitch(float pitch)
        {
            int best = 0;
            for (int i = 1; i < 4; i++) if (Math.Abs(Pitches[i] - pitch) < Math.Abs(Pitches[best] - pitch)) best = i;
            for (int i = 0; i < PitchButtons.Count; i++) PitchButtons[i].Selected = i == best;
        }

        // ---- picture lists ----
        private List<Entry> WallEntries()
        {
            var list = new List<Entry>();
            var res = new UICatalogWallResProvider();
            for (int i = 0; i < UIBuyBrowsePanel.WallStyleIDs.Length; i++)
            {
                var id = (ulong)UIBuyBrowsePanel.WallStyleIDs[i];
                var e = new Entry { Control = typeof(UIWallPlacer), Parameters = new List<int> { UIBuyBrowsePanel.WallStylePatterns[i], UIBuyBrowsePanel.WallStyleIDs[i] }, Res = res, ResID = id };
                try { e.Name = res.GetName(id); e.Price = res.GetPrice(id); e.Description = res.GetDescription(id); } catch { e.Name = "Wall"; }
                list.Add(e);
            }
            return list;
        }

        private List<Entry> WallpaperEntries()
        {
            var list = new List<Entry>();
            var res = new UICatalogWallpaperResProvider();
            foreach (var w in Content.Get().WorldWalls.List().Cast<WallReference>())
                list.Add(new Entry { Name = w.Name, Price = w.Price, Description = w.Description, Control = typeof(UIWallPainter), Parameters = new List<int> { (int)w.ID }, Res = res, ResID = w.ID });
            return list.OrderBy(x => x.Price).ToList();
        }

        private List<Entry> FloorEntries(bool water)
        {
            var list = new List<Entry>();
            var res = new UICatalogFloorResProvider();
            foreach (var f in Content.Get().WorldFloors.List().Cast<FloorReference>())
            {
                if (water != (f.ID >= 65534)) continue;
                list.Add(new Entry { Name = f.Name, Price = f.Price, Description = f.Description, Control = typeof(UIFloorPainter), Parameters = new List<int> { (int)f.ID }, Res = res, ResID = f.ID });
            }
            return list.OrderBy(x => x.Price).ToList();
        }

        private List<Entry> ObjectEntries(sbyte category)
        {
            var list = new List<Entry>();
            foreach (var item in Content.Get().WorldCatalog.GetItemsByCategory(category))
                if (UIClassicBuyPanel.HasCatalogPicture(item.GUID)) // (no picture: the game's hidden/test objects)
                list.Add(new Entry { Name = UIClassicColourDialog.GetName(item.GUID), Description = item.Name, Price = (int)item.Price, GUID = item.GUID });
            return list.OrderBy(x => x.Price).ToList();
        }

        // ---- showing pictures ----
        private int ThumbStart = ThumbX;
        private void ShowEntries(List<Entry> list, int extraLeft = 0)
        {
            Entries = list;
            FirstItem = 0;
            SelectedIndex = -1;
            ThumbStart = ThumbX + extraLeft;
            if (ArrowLeft == null)
            {
                ArrowLeft = new UITS1Button(TS1UIGraphics.Get("cpanel/Buttons/ScrollLeftBuy.bmp"), 4);
                ArrowLeft.OnButtonClick += (b) => Page(-1);
                Add(ArrowLeft); AreaItems.Add(ArrowLeft);
                ArrowRight = new UITS1Button(TS1UIGraphics.Get("cpanel/Buttons/ScrollRight.bmp"), 4);
                ArrowRight.OnButtonClick += (b) => Page(1);
                Add(ArrowRight); AreaItems.Add(ArrowRight);
            }
            ArrowLeft.Position = new Vector2(ArrowLX + extraLeft, ArrowY);
            RebuildThumbs();
        }

        /// <summary>
        /// SimsRedux: the pictures split into groups (Carpet, Wood, Tile...), with a column of tabs
        /// to pick one. The tab picked last for this tool is opened again next time.
        /// </summary>
        private void ShowSorted(List<Entry> list, CatalogType[] types, string other)
        {
            foreach (var e in list) e.Type = ReduxCatalogTypes.TypeOf(e.Name, e.Description, types, other);
            var tabs = ReduxCatalogTypes.Tabs(list.Select(x => x.Type), types, other);
            if (tabs.Count == 0) { ShowEntries(list); return; }
            AllEntries = list;
            string chosen;
            if (!ChosenType.TryGetValue(CurrentTool, out chosen) || !tabs.Contains(chosen)) chosen = ReduxCatalogTypes.All;
            TypeTabs = new UIReduxTabStrip(tabs, chosen);
            TypeTabs.Position = new Vector2(AreaX, BarY + 6);
            TypeTabs.OnPick += (name) =>
            {
                ChosenType[CurrentTool] = name;
                ShowEntries(OfType(name), UIReduxTabStrip.StripWidth + 4);
            };
            Add(TypeTabs);
            AreaItems.Add(TypeTabs);
            ShowEntries(OfType(chosen), UIReduxTabStrip.StripWidth + 4);
        }

        private List<Entry> OfType(string type)
        {
            if (type == ReduxCatalogTypes.All) return AllEntries;
            return AllEntries.Where(x => x.Type == type).ToList();
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
            if (next >= Entries.Count) return;
            FirstItem = next;
            RebuildThumbs();
        }

        private int BuiltColumns = -1;
        // SimsRedux: the item card above the bar (the picture under the mouse, or the object being placed)
        private UIReduxItemCard Card = new UIReduxItemCard();
        private Entry HoverEntry;
        private uint HeldGUID;

        private void RebuildThumbs()
        {
            HoverEntry = null;
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
                    if (index >= Entries.Count) break;
                    var e = Entries[index];
                    var thumb = new UIClassicCatalogThumb(ThumbFrame, GetIcon(e) ?? UnknownThumb, index);
                    thumb.Position = new Vector2(ThumbStart + c * ThumbStep, RowY(r, rows));
                    thumb.Unaffordable = e.Price > budget;
                    thumb.Selected = index == SelectedIndex;
                    var entry = e;
                    thumb.OnHover = (t, on) => { if (on) HoverEntry = entry; else if (HoverEntry == entry) HoverEntry = null; };
                    thumb.OnClick = (t) => PickEntry(t.Index);
                    Add(thumb);
                    Thumbs.Add(thumb);
                }
            }
            if (ArrowRight != null) ArrowRight.Position = new Vector2(ThumbStart + cols * ThumbStep, ArrowY);
            if (ArrowLeft != null) ArrowLeft.Disabled = FirstItem == 0;
            if (ArrowRight != null) ArrowRight.Disabled = FirstItem + cols * rows >= Entries.Count;
            UpdateGrowTab();
        }

        // ---- SimsRedux: the grid can grow upwards (the arrow tab on top), up to 5 rows ----
        private const int RowStep = 48, MaxRows = 5;
        private static bool Tall;
        private UIReduxGrowTab GrowTab;

        private int Rows
        {
            get
            {
                if (!Tall) return 2;
                int cols = Math.Max(1, Columns);
                return Math.Max(2, Math.Min(MaxRows, (Entries.Count + cols - 1) / cols));
            }
        }

        private int RowY(int r, int rows) { return ThumbRow2 - (rows - 1 - r) * RowStep; }

        private int GridTop { get { return Thumbs.Count == 0 ? BarY : Math.Min(BarY, RowY(0, Rows) - 4); } }

        private void UpdateGrowTab()
        {
            if (GrowTab == null)
            {
                GrowTab = new UIReduxGrowTab();
                GrowTab.OnClick += () => { Tall = !Tall; FirstItem = 0; RebuildThumbs(); };
                Add(GrowTab);
            }
            int cols = Math.Max(1, Columns);
            GrowTab.Visible = Thumbs.Count > 0 && (Tall || Entries.Count > cols * 2);
            GrowTab.Up = !Tall || Rows <= 2;
            GrowTab.Position = new Vector2(ThumbStart + 4, GridTop - UIReduxGrowTab.H);
        }

        // icons are made once and kept (some providers make a new picture every time)
        private static Dictionary<string, Texture2D> IconCache = new Dictionary<string, Texture2D>();
        private Texture2D GetIcon(Entry e)
        {
            if (e.Icon != null) return e.Icon;
            if (e.GUID != 0) return e.Icon = UIClassicColourDialog.GetIcon(e.GUID);
            if (e.Res == null) return null;
            var key = e.Res.GetType().Name + ":" + e.ResID;
            Texture2D tex;
            if (!IconCache.TryGetValue(key, out tex))
            {
                try { tex = e.Res.GetIcon(e.ResID); } catch { tex = null; }
                IconCache[key] = tex;
            }
            return e.Icon = tex;
        }

        private void PickEntry(int index)
        {
            if (index < 0 || index >= Entries.Count) return;
            var e = Entries[index];
            SelectedIndex = index;
            foreach (var t in Thumbs) t.Selected = t.Index == SelectedIndex;
            if (e.GUID != 0) BuyObject(e.GUID);
            else if (e.Control != null) StartTool(e.Control, e.Parameters, e.ResID, e.Res);
        }

        /// <summary>Switch on one of Simitone's build tools (wall placer, floor painter...).</summary>
        // SimsRedux: the original Sims 1 cursor for the tool in use (shown while over the lot)
        private FSO.Common.Rendering.Framework.CursorType ToolCursor = FSO.Common.Rendering.Framework.CursorType.Normal;
        private FSO.Common.Rendering.Framework.CursorType? ShownCursor;

        private static FSO.Common.Rendering.Framework.CursorType CursorFor(Type control, List<int> parameters)
        {
            if (control == typeof(UITerrainRaiser)) return FSO.Common.Rendering.Framework.CursorType.SimsRaise;
            if (control == typeof(UITerrainFlatten)) return FSO.Common.Rendering.Framework.CursorType.SimsLevel;
            if (control == typeof(UIFloorPainter) && parameters != null && parameters.Count > 0)
            {
                if (parameters[0] == 65535) return FSO.Common.Rendering.Framework.CursorType.Hole;
                if (parameters[0] == 65534) return FSO.Common.Rendering.Framework.CursorType.Water;
            }
            return FSO.Common.Rendering.Framework.CursorType.Normal;
        }

        private void UpdateCursor()
        {
            var lot = Game.LotControl;
            FSO.Common.Rendering.Framework.CursorType? want = null;
            if (Visible && lot != null && lot.MouseIsOn && lot.ObjectHolder.Holding == null)
            {
                // (with no tool, the object holder shows the Sims 1 move hand itself)
                if (lot.CustomControl != null) want = ToolCursor;
            }
            if (want == ShownCursor) return;
            // left the lot / dropped the tool: back to the normal arrow (once)
            GameFacade.Cursor.SetCursor(want ?? FSO.Common.Rendering.Framework.CursorType.Normal);
            ShownCursor = want;
        }

        private void StartTool(Type control, List<int> parameters, ulong resID, UICatalogResProvider res)
        {
            ToolCursor = CursorFor(control, parameters);
            var lot = Game.LotControl;
            lot.ObjectHolder.ClearSelected();
            if (lot.CustomControl != null)
            {
                lot.CustomControl.Release();
                lot.CustomControl = null;
            }
            // (no Simitone info card - the classic bar shows the name and price)
            lot.QueryPanel.Active = false;
            lot.CustomControl = (UICustomLotControl)Activator.CreateInstance(control, lot.vm, lot.World, lot, parameters);
        }

        /// <summary>Doors, windows, stairs, fireplaces and plants are objects: buy and place like Buy mode.</summary>
        private void BuyObject(uint guid)
        {
            var lot = Game.LotControl;
            var holder = lot.ObjectHolder;
            holder.ClearSelected();
            if (lot.CustomControl != null)
            {
                lot.CustomControl.Release();
                lot.CustomControl = null;
            }
            var bought = lot.vm.Context.CreateObjectInstance(guid, LotTilePos.OUT_OF_WORLD, Direction.NORTH, holder.UseNet);
            if (bought != null && bought.Objects.Count != 0)
            {
                lot.QueryPanel.Active = false;
                HeldGUID = guid; // (the item card above the bar shows what it is)
                holder.SetSelected(bought);
            }
        }

        // ------------------------------------------------------------------
        // Holding objects (same as Buy mode: Shift places another, click the bar to sell)
        // ------------------------------------------------------------------

        private void Holder_OnPickup(UIObjectSelection holding, UpdateState state)
        {
            if (!Visible) return;
            var control = Game.LotControl;
            control.PickupPanel.Opacity = 0f;
            try
            {
                var entity = holding.RealEnt ?? holding.Group.BaseObject;
                var def = entity.MasterDefinition ?? entity.Object.OBJ;
                var name = entity.Object.Resource.Get<CTSS>(def.CatalogStringsID)?.GetString(0) ?? entity.ToString();
                var canSell = entity.IsUserMovable(control.vm.Context, true) == FSO.SimAntics.Model.VMPlacementError.Success;
                var value = entity.MultitileGroup.Price;
                EnsureSellCatcher();
                SellCatcher.Title = canSell ? ("Sell " + name + (value > 0 ? " for §" + value.ToString("##,#0") : "")) : (name + " can't be sold");
                SellCatcher.Hint = canSell ? "Click here to sell it.  Esc puts it back.  Right-click it on the lot for colours." : "Esc puts it back.";
                SellCatcher.ShowColoursLabel = false;
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Build sell info: " + e.Message);
            }
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
            Remove(SellCatcher);
            Add(SellCatcher);
        }

        private void Holder_OnPutDown(UIObjectSelection holding, UpdateState state)
        {
            if (!Visible) return;
            var control = Game.LotControl;
            control.QueryPanel.Active = false;
            control.PickupPanel.Opacity = 0f;
            if (SelectedIndex != -1 && SelectedIndex < Entries.Count && Entries[SelectedIndex].GUID != 0)
            {
                if (!holding.IsBought && state.ShiftDown)
                {
                    var dir = holding.Dir;
                    BuyObject(Entries[SelectedIndex].GUID);
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

        /// <summary>Drops the current tool or held object.</summary>
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
                // entering Build mode: start with the Hand, like the original
                SelectTool(Tool.Hand);
            }
            else if (!Visible && WasVisible)
            {
                ClearHolding();
                ClearArea();
                CurrentTool = Tool.None;
                foreach (var t in Tools) t.Button.Selected = false;
            }
            WasVisible = Visible;

            if (Visible)
            {
                // Esc drops the current tool and goes back to the Hand
                if (state.NewKeys.Contains(Keys.Escape) && Game.LotControl?.CustomControl != null)
                {
                    SelectTool(Tool.Hand);
                }
                if (Entries.Count > 0 && Columns != BuiltColumns) RebuildThumbs();
                // the dropper: I to pick it up / put it away
                if (!UIRevampedCamera.IsTyping(state) && state.NewKeys.Contains(Keys.I)) ToggleDropper();
                Search.DropperOn = DropperActive;
                if (CurrentTool == Tool.Roof && Game.vm != null) MarkPitch(Game.vm.Context.Architecture.RoofPitch);
            }

            var holding = Game.LotControl?.ObjectHolder?.Holding;
            if (SellCatcher != null)
            {
                SellCatcher.Visible = Visible && holding != null && holding.IsBought;
                if (SellCatcher.Visible) SellCatcher.Width = (int)(GlobalSettings.Default.GraphicsWidth / ((Parent?.Parent as UIClassicUCP)?.UIScale ?? 1f)) - 218;
            }
            if (Game.LotControl != null && Visible)
            {
                Game.LotControl.PickupPanel.Opacity = 0f;
                Game.LotControl.QueryPanel.Active = false; // the Simitone info card - the classic bar shows it instead
                // search and dropper sit on the bar's top edge, at the right
                var width = GlobalSettings.Default.GraphicsWidth / ((Parent?.Parent as UIClassicUCP)?.UIScale ?? 1f);
                Search.Position = new Vector2((int)(width - UIReduxCatalogSearch.TotalW - 12), GridTop - UIReduxCatalogSearch.BoxH);
            }
            if (Visible || ShownCursor != null) UpdateCursor();
            base.Update(state);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;

            // dark well behind the tool buttons, and the divider
            if (!ReduxTheme.Redux) Fill(batch, new Rectangle(ToolX - 4, BarY + 4, ToolStep * 6 + 8, 92), new Color(0, 0, 50) * 0.6f);
            if (Divider != null && Divider.Width > 1) DrawLocalTexture(batch, Divider, new Vector2(DividerX, BarY + 6));

            if (CurrentTool == Tool.Terrain)
            {
                DrawLocalString(batch, "Raise / Lower", new Vector2(AreaX + 104, BarY + 14), Small, new Rectangle(0, 0, 160, 16), TextAlignment.Left | TextAlignment.Middle);
                DrawLocalString(batch, "Level", new Vector2(AreaX + 48, BarY + 60), Small, new Rectangle(0, 0, 160, 16), TextAlignment.Left | TextAlignment.Middle);
                DrawLocalString(batch, "Grass", new Vector2(AreaX + 104, BarY + 30), Small, new Rectangle(0, 0, 160, 16), TextAlignment.Left | TextAlignment.Middle);
            }
            if (AreaHint != null && CurrentTool != Tool.Terrain)
            {
                int x = (CurrentTool == Tool.Water) ? AreaX + 116 : AreaX + 4;
                DrawLocalString(batch, AreaHint, new Vector2(x, BarY + 30), Title, new Rectangle(0, 0, 520, 40), TextAlignment.Left | TextAlignment.Middle);
            }
            if (CurrentTool == Tool.None)
                DrawLocalString(batch, "Pick a tool.", new Vector2(AreaX + 4, BarY + 30), Title, new Rectangle(0, 0, 300, 40), TextAlignment.Left | TextAlignment.Middle);
            // SimsRedux: the grown grid sits on a panel that rises out of the bar
            if (GridTop < BarY)
            {
                int gw = Math.Max(1, Columns) * ThumbStep + 12;
                DrawLocalTexture(batch, ReduxTheme.Box(gw, BarY - GridTop + 16, 10, ReduxTheme.Hi, ReduxTheme.Deep, ReduxTheme.Rim, 1), new Vector2(ThumbStart - 6, GridTop - 4));
            }

            base.Draw(batch);

            // the item card: the picture under the mouse, or else the object being placed
            var holdingNow = Game.LotControl?.ObjectHolder?.Holding;
            if (holdingNow == null || holdingNow.IsBought) HeldGUID = 0;
            if ((HoverEntry != null || HeldGUID != 0) && (SellCatcher == null || !SellCatcher.Visible))
            {
                var e = HoverEntry;
                if (e == null) Card.SetObject(HeldGUID, UIClassicColourDialog.GetIcon(HeldGUID));
                else if (e.GUID != 0) Card.SetObject(e.GUID, GetIcon(e));
                else Card.SetPlain(GetIcon(e), e.Name, e.Price, e.Description);
                Card.Draw(this, batch, ToolX, GridTop - (GrowTab != null && GrowTab.Visible ? UIReduxGrowTab.H : 0) - 2);
                return;
            }
            Card.Hide();
            // hints (the dropper...), in a small box above the bar
            if (!string.IsNullOrEmpty(HoverText) && (SellCatcher == null || !SellCatcher.Visible))
            {
                int w = 12 + HoverText.Length * 6;
                var box = new Rectangle(ToolX, BarY - 20, w, 17);
                Fill(batch, box, new Color(0, 0, 82));
                Fill(batch, new Rectangle(box.X, box.Y, box.Width, 1), new Color(120, 120, 200));
                DrawLocalString(batch, HoverText, new Vector2(box.X, box.Y), Small, new Rectangle(0, 0, box.Width, box.Height), TextAlignment.Center | TextAlignment.Middle);
            }
        }

        private void Fill(UISpriteBatch batch, Rectangle r, Color c)
        {
            DrawLocalTexture(batch, Px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), c);
        }
    }
}
