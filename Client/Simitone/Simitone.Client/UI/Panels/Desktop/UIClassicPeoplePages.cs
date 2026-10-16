using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: shared bits for the classic Relationships and Inventory pages: the sort buttons
    /// on the left, the scroll arrows, a row of pictures, and the name box above the bar.
    /// Coordinates are in the classic control panel's layout (the wing is at 0,0).
    /// </summary>
    public abstract class UIClassicPagedRow : UIContainer
    {
        protected TS1GameScreen Game;
        protected Texture2D Px;
        protected TextStyle Small, Tiny, Title;
        protected static readonly Color TextColor = new Color(220, 220, 250);

        protected const int BarY = 83;
        protected const int PX = 440;                     // left edge of the page
        protected const int SortX = PX, SortY = BarY + 14, SortStep = 24;
        protected const int ArrowLX = PX + 54, ArrowY = BarY + 26;
        protected const int CellX = PX + 68, CellStep = 52;

        protected List<UITS1Button> SortButtons = new List<UITS1Button>();
        protected UITS1Button ArrowLeft, ArrowRight;
        protected int First;
        protected int HoverCell = -1;
        protected string HoverText;
        protected string SortName = "";

        protected UIClassicPagedRow(TS1GameScreen game)
        {
            Game = game;
            Px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            Small = TextStyle.DefaultLabel.Clone(); Small.Size = 10; Small.Color = TextColor;
            Tiny = TextStyle.DefaultLabel.Clone(); Tiny.Size = 9; Tiny.Color = TextColor;
            Title = TextStyle.DefaultLabel.Clone(); Title.Size = 12; Title.Color = TextColor;

            ArrowLeft = new UITS1Button(TS1UIGraphics.Get("cpanel/Buttons/ScrollLeftBuy.bmp"), 4);
            ArrowLeft.Position = new Vector2(ArrowLX, ArrowY);
            ArrowLeft.OnButtonClick += (b) => { First = Math.Max(0, First - Columns); };
            Add(ArrowLeft);
            ArrowRight = new UITS1Button(TS1UIGraphics.Get("cpanel/Buttons/ScrollRight.bmp"), 4);
            ArrowRight.OnButtonClick += (b) => { if (First + Columns < Count) First += Columns; };
            Add(ArrowRight);
        }

        protected abstract int Count { get; }

        /// <summary>On screen: this page and the Live bar it sits on are both showing.</summary>
        protected bool Shown
        {
            get { return Visible && (Parent == null || Parent.Visible); }
        }

        protected UITS1Button AddSort(string art, int index, string name, Action click)
        {
            var btn = new UITS1Button(TS1UIGraphics.Get("cpanel/Buttons/" + art + ".bmp"), 4);
            btn.SelectedFrame = 1;
            btn.Position = new Vector2(SortX + (index % 2) * SortStep, SortY + (index / 2) * SortStep);
            btn.OnButtonClick += (b) =>
            {
                foreach (var s in SortButtons) s.Selected = s == btn;
                SortName = name;
                First = 0;
                click();
            };
            btn.OnButtonHover += (b) => HoverText = name;
            btn.OnButtonExit += (b) => { if (HoverText == name) HoverText = null; };
            Add(btn);
            SortButtons.Add(btn);
            return btn;
        }

        protected void SelectSort(int index, string name)
        {
            for (int i = 0; i < SortButtons.Count; i++) SortButtons[i].Selected = i == index;
            SortName = name;
        }

        /// <summary>How many pictures fit on the bar at this window size.</summary>
        protected int Columns
        {
            get
            {
                float width = UIClassicUCP.RightEdge(this);
                return Math.Max(1, (int)((width - CellX - 30) / CellStep));
            }
        }

        protected Rectangle CellRect(int col)
        {
            return new Rectangle(CellX + col * CellStep, BarY + 5, 45, 45);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!Visible) return;
            int cols = Columns;
            if (First >= Count) First = Math.Max(0, ((Count - 1) / cols) * cols);
            ArrowRight.Position = new Vector2(CellX + cols * CellStep, ArrowY);
            ArrowLeft.Disabled = First == 0;
            ArrowRight.Disabled = First + cols >= Count;

            // which picture is under the mouse
            var mouse = GlobalPoint(new Vector2(state.MouseState.X, state.MouseState.Y));
            HoverCell = -1;
            if (state.MouseState.X >= 0)
            {
                for (int c = 0; c < cols; c++)
                {
                    var r = CellRect(c);
                    r.Height = 92;
                    if (r.Contains((int)mouse.X, (int)mouse.Y) && First + c < Count) { HoverCell = First + c; break; }
                }
            }
        }

        protected void Fill(UISpriteBatch batch, Rectangle r, Color c)
        {
            DrawLocalTexture(batch, Px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), c);
        }

        /// <summary>The sort name under the sort buttons and the name box above the bar.</summary>
        protected void DrawCommon(UISpriteBatch batch, string emptyText)
        {
            DrawLocalString(batch, SortName, new Vector2(PX - 2, BarY + 66), Tiny, new Rectangle(0, 0, 52, 14), TextAlignment.Center | TextAlignment.Middle);
            if (Count == 0)
                DrawLocalString(batch, emptyText, new Vector2(CellX + 4, BarY + 30), Small, new Rectangle(0, 0, 400, 30), TextAlignment.Left | TextAlignment.Middle);
        }

        protected void DrawHoverBox(UISpriteBatch batch, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int w = 12 + text.Length * 6;
            var box = new Rectangle(CellX, BarY - 20, w, 17);
            Fill(batch, box, new Color(0, 0, 82));
            Fill(batch, new Rectangle(box.X, box.Y, box.Width, 1), new Color(120, 120, 200));
            DrawLocalString(batch, text, new Vector2(box.X, box.Y), Small, new Rectangle(0, 0, box.Width, box.Height), TextAlignment.Center | TextAlignment.Middle);
        }
    }

    /// <summary>
    /// SimsRedux: the classic Relationships page. Everyone the selected Sim knows, as faces in the
    /// original People frames. The face's background goes from red (enemies) through grey to green
    /// (good friends). Under each face: today's relationship and the lifetime one.
    /// Sort buttons: Everyone, Family, Friends.
    /// </summary>
    public class UIClassicRelationshipPage : UIClassicPagedRow
    {
        private enum Sort { All, Family, Friends }
        private Sort CurrentSort = Sort.All;
        private Texture2D Frame, UnknownRel, Smiley;

        private class Person
        {
            public short NID;
            public uint GUID;
            public string Name, ShortName;
            public int Daily, Lifetime;
            public bool Family, Friend;
        }
        private List<Person> People = new List<Person>();
        private short LastNID = -1;
        private static Dictionary<uint, Texture2D> Faces = new Dictionary<uint, Texture2D>();
        private static HashSet<uint> FaceFailed = new HashSet<uint>();

        protected override int Count { get { return People.Count; } }

        public UIClassicRelationshipPage(TS1GameScreen game) : base(game)
        {
            Frame = TS1UIGraphics.Get("cpanel/People/PeopleTemplate.bmp");
            UnknownRel = TS1UIGraphics.Get("cpanel/People/UnknownRel.bmp");
            Smiley = TS1UIGraphics.Get("cpanel/Buttons/JobFriendSmiley.bmp");
            AddSort("RelAllSort", 0, "Everyone", () => CurrentSort = Sort.All);
            AddSort("RelFamilySort", 1, "Family", () => CurrentSort = Sort.Family);
            AddSort("RelFriendSort", 2, "Friends", () => CurrentSort = Sort.Friends);
            SelectSort(0, "Everyone");
        }

        public override void Update(UpdateState state)
        {
            if (Shown) Refresh();
            base.Update(state);
            if (Shown) MakeOneFace();
        }

        /// <summary>Reads the selected Sim's relationships from the neighbourhood.</summary>
        private void Refresh()
        {
            People.Clear();
            var sel = Game.SelectedAvatar;
            if (sel == null) return;
            try
            {
                var hood = Content.Get().Neighborhood;
                var nid = sel.GetPersonData(VMPersonDataVariable.NeighborId);
                if (nid != LastNID) { LastNID = nid; First = 0; }
                var me = hood.GetNeighborByID(nid);
                if (me?.Relationships == null) return;
                var family = Game.vm?.TS1State?.CurrentFamily;
                foreach (var pair in me.Relationships)
                {
                    var other = hood.GetNeighborByID((short)pair.Key);
                    if (other == null) continue;
                    var values = pair.Value;
                    var p = new Person
                    {
                        NID = (short)pair.Key,
                        GUID = other.GUID,
                        Name = SimName(other),
                        Daily = (values.Count > 0) ? values[0] : 0,
                        Lifetime = (values.Count > 2) ? values[2] : 0,
                        Family = family != null && family.FamilyGUIDs.Contains(other.GUID),
                    };
                    p.Friend = p.Daily >= 50; // the Sims 1 rule: a daily relationship of 50 or more
                    p.ShortName = p.Name.Split(' ')[0];
                    if (CurrentSort == Sort.Family && !p.Family) continue;
                    if (CurrentSort == Sort.Friends && !p.Friend) continue;
                    People.Add(p);
                }
                // best relationships first, like the original
                People = People.OrderByDescending(x => x.Daily).ThenByDescending(x => x.Lifetime).ToList();
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Relationships page: " + e.Message);
            }
        }

        private static Dictionary<uint, string> NameCache = new Dictionary<uint, string>();
        /// <summary>The Sim's name from their character file (the neighbourhood only keeps an id like "user00008").</summary>
        private static string SimName(FSO.Files.Formats.IFF.Chunks.Neighbour n)
        {
            string name;
            if (NameCache.TryGetValue(n.GUID, out name)) return name;
            name = null;
            try
            {
                var obj = Content.Get().WorldObjects.Get(n.GUID);
                if (obj != null) name = obj.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0);
            }
            catch { }
            if (string.IsNullOrWhiteSpace(name)) name = (string.IsNullOrEmpty(n.Name) || n.Name.StartsWith("user")) ? "Someone" : n.Name;
            NameCache[n.GUID] = name;
            return name;
        }

        /// <summary>
        /// Faces are drawn from the Sim's 3D head, which takes a moment, so only one new face is
        /// made per frame (and never while drawing).
        /// </summary>
        private void MakeOneFace()
        {
            int cols = Columns;
            for (int i = First; i < Math.Min(People.Count, First + cols); i++)
            {
                var guid = People[i].GUID;
                if (Faces.ContainsKey(guid) || FaceFailed.Contains(guid)) continue;
                Faces[guid] = MakeFace(guid);
                if (Faces[guid] == null) { Faces.Remove(guid); FaceFailed.Add(guid); }
                return;
            }
        }

        private Texture2D MakeFace(uint guid)
        {
            try
            {
                var vm = Game.vm;
                // someone on the lot right now: use them
                foreach (var ava in vm.Context.ObjectQueries.Avatars)
                    if (ava.Object.OBJ.GUID == guid) return UIIconCache.GetObject(ava);
                // otherwise make them for a moment, take the picture, and remove them again
                var temp = vm.Context.CreateObjectInstance(guid, LotTilePos.OUT_OF_WORLD, Direction.NORTH, true);
                if (temp == null || temp.BaseObject == null) return null;
                var face = UIIconCache.GetObject(temp.BaseObject);
                temp.Delete(vm.Context);
                return face;
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Couldn't make a face: " + e.Message);
                return null;
            }
        }

        private static int MoodFrame(int daily)
        {
            if (daily <= -50) return 0;
            if (daily < -10) return 1;
            if (daily <= 10) return 2;
            if (daily < 50) return 3;
            return 4;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            DrawCommon(batch, (CurrentSort == Sort.All) ? "Doesn't know anyone yet." : (CurrentSort == Sort.Family ? "No family relationships yet." : "No friends yet. Friends have a relationship of 50 or more."));

            int cols = Columns;
            for (int c = 0; c < cols; c++)
            {
                int i = First + c;
                if (i >= People.Count) break;
                var p = People[i];
                var r = CellRect(c);

                // frame first (normal / hover): its middle is solid, the face goes on top
                if (Frame != null && Frame.Width > 1)
                {
                    int fw = Frame.Width / 4;
                    DrawLocalTexture(batch, Frame, new Rectangle((i == HoverCell ? 1 : 0) * fw, 0, fw, Frame.Height), new Vector2(r.X, r.Y));
                }

                // background colour for how they get on (from UnknownRel.bmp: red ... green)
                if (UnknownRel != null && UnknownRel.Width >= 5) // (5 frames side by side)
                {
                    int fw = UnknownRel.Width / 5;
                    var src = new Rectangle(MoodFrame(p.Daily) * fw, 0, fw, UnknownRel.Height);
                    Texture2D face;
                    if (Faces.TryGetValue(p.GUID, out face) && face != null)
                    {
                        // just the colour from the corner of the picture, then the face on top
                        DrawLocalTexture(batch, UnknownRel, new Rectangle(src.X + 1, 1, 2, 2), new Vector2(r.X + 3, r.Y + 3), new Vector2(39 / 2f, 39 / 2f));
                        float s = 39f / Math.Max(face.Width, face.Height);
                        DrawLocalTexture(batch, face, null, new Vector2(r.X + 3 + (39 - face.Width * s) / 2, r.Y + 3 + (39 - face.Height * s) / 2), new Vector2(s, s));
                    }
                    else
                    {
                        DrawLocalTexture(batch, UnknownRel, src, new Vector2(r.X + 3, r.Y + 3), new Vector2(39f / fw, 39f / UnknownRel.Height));
                    }
                }

                // friend smiley in the corner
                if (p.Friend && Smiley != null && Smiley.Width > 1)
                {
                    int sw = Smiley.Width / 4;
                    DrawLocalTexture(batch, Smiley, new Rectangle(sw, 0, sw, Smiley.Height), new Vector2(r.Right - sw - 1, r.Y + 1));
                }

                // today's and lifetime relationship
                DrawRelBar(batch, new Rectangle(r.X, r.Bottom + 4, 45, 7), p.Daily);
                DrawRelBar(batch, new Rectangle(r.X, r.Bottom + 14, 45, 7), p.Lifetime);
                DrawLocalString(batch, p.ShortName, new Vector2(r.X - 4, r.Bottom + 24), Tiny, new Rectangle(0, 0, 53, 14), TextAlignment.Center | TextAlignment.Middle);
            }

            base.Draw(batch);

            if (HoverCell >= 0 && HoverCell < People.Count)
            {
                var p = People[HoverCell];
                DrawHoverBox(batch, p.Name + "   Daily " + p.Daily + "   Lifetime " + p.Lifetime + (p.Friend ? "   (Friend)" : "") + (p.Family ? "   (Family)" : ""));
            }
            else DrawHoverBox(batch, HoverText);
        }

        /// <summary>A small -100..100 bar: grows right in green, left in red, from the middle.</summary>
        private void DrawRelBar(UISpriteBatch batch, Rectangle r, int value)
        {
            value = Math.Max(-100, Math.Min(100, value));
            Fill(batch, r, new Color(0, 0, 30));
            int mid = r.X + r.Width / 2;
            int w = (int)Math.Round((r.Width / 2 - 1) * Math.Abs(value) / 100f);
            if (value > 0) Fill(batch, new Rectangle(mid, r.Y + 1, w, r.Height - 2), new Color(40, 220, 60));
            else if (value < 0) Fill(batch, new Rectangle(mid - w, r.Y + 1, w, r.Height - 2), new Color(220, 40, 40));
            Fill(batch, new Rectangle(mid, r.Y, 1, r.Height), new Color(120, 120, 200));
        }
    }

    /// <summary>
    /// SimsRedux: the classic Inventory page. The selected Sim's gifts, souvenirs, magic and
    /// ingredients in catalog frames, with how many of each. Sort buttons: Gifts, Magic, Other.
    /// </summary>
    public class UIClassicInventoryPage : UIClassicPagedRow
    {
        // inventory types (same split Simitone used): 7 = magic, 8 = ingredients and other bits,
        // 2 and 6 are hidden counters the game keeps for itself
        private static readonly HashSet<int> Hidden = new HashSet<int> { 2, 6, 7, 8 };
        private int Filter = -1; // -1 gifts and souvenirs, 7 magic, 8 other
        private Texture2D Frame, Unknown;

        private class Item
        {
            public uint GUID;
            public int Count;
            public string Name;
            public Texture2D Icon;
        }
        private List<Item> Items = new List<Item>();
        private static Dictionary<uint, Texture2D> Icons = new Dictionary<uint, Texture2D>();
        private static Dictionary<uint, string> Names = new Dictionary<uint, string>();

        protected override int Count { get { return Items.Count; } }

        public UIClassicInventoryPage(TS1GameScreen game) : base(game)
        {
            Frame = TS1UIGraphics.Get("cpanel/Catalog/ThumbTemplate.bmp");
            Unknown = TS1UIGraphics.Get("cpanel/CatalogUnknown.bmp");
            AddSort("InventoryGiftSort", 0, "Gifts", () => Filter = -1);
            AddSort("InventoryMagicSort", 1, "Magic", () => Filter = 7);
            AddSort("InventoryOtherSort", 2, "Other", () => Filter = 8);
            SelectSort(0, "Gifts");
        }

        public override void Update(UpdateState state)
        {
            if (Shown) Refresh();
            base.Update(state);
        }

        private void Refresh()
        {
            Items.Clear();
            var sel = Game.SelectedAvatar;
            if (sel == null) return;
            try
            {
                var hood = Content.Get().Neighborhood;
                var nid = sel.GetPersonData(VMPersonDataVariable.NeighborId);
                var inv = hood.GetInventoryByNID(nid);
                if (inv == null) return;
                foreach (var entry in inv)
                {
                    if (entry.Count == 0) continue;
                    bool show = (Filter == -1) ? !Hidden.Contains(entry.Type) : entry.Type == Filter;
                    if (!show) continue;
                    var name = GetName(entry.GUID);
                    if (name == null) continue; // not a real object (a counter): skip it
                    Items.Add(new Item { GUID = entry.GUID, Count = entry.Count, Name = name, Icon = GetIcon(entry.GUID) });
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Inventory page: " + e.Message);
            }
        }

        private static string GetName(uint guid)
        {
            string name;
            if (Names.TryGetValue(guid, out name)) return name;
            name = null;
            try
            {
                var obj = Content.Get().WorldObjects.Get(guid);
                if (obj != null) name = obj.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0) ?? obj.OBJ.ChunkLabel ?? "Item";
            }
            catch { }
            Names[guid] = name;
            return name;
        }

        private static Texture2D GetIcon(uint guid)
        {
            Texture2D tex;
            if (Icons.TryGetValue(guid, out tex)) return tex;
            tex = null;
            try
            {
                var obj = Content.Get().WorldObjects.Get(guid);
                tex = obj?.Resource.Get<BMP>(obj.OBJ.CatalogStringsID)?.GetTexture(GameFacade.GraphicsDevice);
            }
            catch { }
            Icons[guid] = tex;
            return tex;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            DrawCommon(batch, Filter == -1 ? "No gifts or souvenirs yet." : (Filter == 7 ? "No magic items yet." : "Nothing here yet."));

            int cols = Columns;
            for (int c = 0; c < cols; c++)
            {
                int i = First + c;
                if (i >= Items.Count) break;
                var item = Items[i];
                var r = CellRect(c);
                if (Frame != null && Frame.Width > 1)
                {
                    int fw = Frame.Width / 4;
                    DrawLocalTexture(batch, Frame, new Rectangle((i == HoverCell ? 1 : 0) * fw, 0, fw, Frame.Height), new Vector2(r.X, r.Y));
                }
                var icon = item.Icon ?? Unknown;
                if (icon != null && icon.Width > 1)
                {
                    // Sims 1 catalog pictures hold two frames side by side: use the first
                    var src = (icon.Width >= icon.Height * 1.5f) ? new Rectangle(0, 0, icon.Width / 2, icon.Height) : new Rectangle(0, 0, icon.Width, icon.Height);
                    float s = 41f / Math.Max(src.Width, src.Height);
                    DrawLocalTexture(batch, icon, src, new Vector2(r.X + (45 - src.Width * s) / 2, r.Y + (45 - src.Height * s) / 2), new Vector2(s, s));
                }
                // how many
                if (item.Count > 1)
                {
                    var text = "x" + item.Count;
                    Fill(batch, new Rectangle(r.Right - 6 - text.Length * 6, r.Bottom - 15, 4 + text.Length * 6, 13), new Color(0, 0, 60) * 0.85f);
                    DrawLocalString(batch, text, new Vector2(r.Right - 6 - text.Length * 6, r.Bottom - 15), Tiny, new Rectangle(0, 0, 4 + text.Length * 6, 13), TextAlignment.Center | TextAlignment.Middle);
                }
                var shortName = item.Name.Length > 9 ? item.Name.Substring(0, 8) + ".." : item.Name;
                DrawLocalString(batch, shortName, new Vector2(r.X - 4, r.Bottom + 4), Tiny, new Rectangle(0, 0, 53, 14), TextAlignment.Center | TextAlignment.Middle);
            }

            base.Draw(batch);

            if (HoverCell >= 0 && HoverCell < Items.Count)
                DrawHoverBox(batch, Items[HoverCell].Name + (Items[HoverCell].Count > 1 ? "  (" + Items[HoverCell].Count + ")" : ""));
            else DrawHoverBox(batch, HoverText);
        }
    }
}
