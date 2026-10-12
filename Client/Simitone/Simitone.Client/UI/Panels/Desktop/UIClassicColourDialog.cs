using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the colour picker. Right-click an object in Buy or Build mode to open it.
    /// Shows the object's other colours (click one to swap, you pay or get back the price
    /// difference) and a Tint button to recolour it any way you like.
    /// </summary>
    public class UIClassicColourDialog : UIContainer
    {
        private static readonly Color TextColor = new Color(205, 205, 240);
        private const int W = 480, Cols = 8, Step = 52, MaxRows = 3;

        private TS1GameScreen Game;
        private short ObjectID;
        private uint CurrentGUID;
        private List<uint> Variants;
        private int Page;
        private int H;
        private string ObjectName, HoverText;
        private TextStyle Style, TitleStyle;
        private List<UIElement> PageItems = new List<UIElement>();
        private Texture2D ThumbFrame, UnknownThumb, TileTex;
        private UITS1TileButton PrevBtn, NextBtn;

        /// <summary>Opens the picker for this object (does nothing for objects that can't be changed).</summary>
        public static void Open(TS1GameScreen game, VMGameObject obj)
        {
            var group = obj.MultitileGroup;
            var baseObj = group.BaseObject as VMGameObject ?? obj;
            if (baseObj.IsUserMovable(game.vm.Context, true) != VMPlacementError.Success) return;
            var guid = (group.MultiTile && baseObj.MasterDefinition != null) ? baseObj.MasterDefinition.GUID : baseObj.Object.OBJ.GUID;
            if (Content.Get().WorldCatalog.GetItemByGUID(guid) == null) return; // not a Buy Mode object
            FSO.HIT.HITVM.Get().PlaySoundEvent(UISounds.Click);
            UIScreen.GlobalShowDialog(new UIClassicColourDialog(game, baseObj.ObjectID, guid), true);
        }

        public UIClassicColourDialog(TS1GameScreen game, short objectID, uint guid)
        {
            Game = game;
            ObjectID = objectID;
            CurrentGUID = guid;
            Style = TextStyle.DefaultLabel.Clone(); Style.Size = 12; Style.Color = TextColor;
            TitleStyle = TextStyle.DefaultLabel.Clone(); TitleStyle.Size = 18; TitleStyle.Color = TextColor;
            ThumbFrame = TS1UIGraphics.Get("cpanel/Catalog/ThumbTemplate.bmp");
            UnknownThumb = TS1UIGraphics.Get("cpanel/CatalogUnknown.bmp");
            TileTex = TS1UIGraphics.Get("cpanel/Buttons/ButtonTileDialog.bmp");

            Variants = ReduxColourVariants.Get(guid);
            ObjectName = GetName(guid);

            int rows = Math.Max(1, Math.Min(MaxRows, (Variants.Count + Cols - 1) / Cols));
            H = 56 + rows * Step + 40 + 54;

            int by = H - 46;
            AddButton("Tint...", 20, by, 130, () =>
            {
                Close();
                UIScreen.GlobalShowDialog(new UIClassicTintDialog(Game, ObjectID), true);
            });
            var target = Target;
            if (target != null && target.ReduxTint != 0)
                AddButton("Original colours", 160, by, 160, () =>
                {
                    Game.vm.SendCommand(new VMNetTintObjectCmd { ObjectID = ObjectID, Tint = 0 });
                    Close();
                });
            AddButton("Close", 340, by, 120, Close);

            if (Variants.Count > Cols * MaxRows)
            {
                PrevBtn = AddButton("<", W - 110, 14, 44, () => { if (Page > 0) { Page--; BuildPage(); } });
                NextBtn = AddButton(">", W - 60, 14, 44, () => { if ((Page + 1) * Cols * MaxRows < Variants.Count) { Page++; BuildPage(); } });
            }
            BuildPage();
        }

        private VMGameObject Target
        {
            get { return Game.vm?.GetObjectById(ObjectID) as VMGameObject; }
        }

        private UITS1TileButton AddButton(string text, int x, int y, int width, Action click)
        {
            var btn = new UITS1TileButton(TileTex, text, width);
            btn.Position = new Vector2(x, y);
            btn.OnButtonClick += (b) => click();
            Add(btn);
            return btn;
        }

        private void BuildPage()
        {
            foreach (var e in PageItems) Remove(e);
            PageItems.Clear();
            if (Variants.Count <= 1) return;

            var catalog = Content.Get().WorldCatalog;
            int currentPrice = (int)(catalog.GetItemByGUID(CurrentGUID)?.Price ?? 0);
            int perPage = Cols * MaxRows;
            int startX = (W - (Cols * Step - 7)) / 2;
            for (int i = 0; i < perPage; i++)
            {
                int index = Page * perPage + i;
                if (index >= Variants.Count) break;
                var guid = Variants[index];
                var item = catalog.GetItemByGUID(guid);
                if (item == null) continue;
                var thumb = new UIClassicCatalogThumb(ThumbFrame, GetIcon(guid) ?? UnknownThumb, index);
                thumb.Position = new Vector2(startX + (i % Cols) * Step, 56 + (i / Cols) * Step);
                thumb.Selected = guid == CurrentGUID;
                var diff = (int)item.Value.Price - currentPrice;
                var tip = GetName(guid) + (guid == CurrentGUID ? "  (this one)" : (diff == 0 ? "  (free)" : (diff > 0 ? "  (+§" + diff.ToString("##,#0") + ")" : "  (-§" + (-diff).ToString("##,#0") + " back)")));
                thumb.OnHover = (t, on) => { if (on) HoverText = tip; else if (HoverText == tip) HoverText = null; };
                thumb.OnClick = (t) =>
                {
                    if (guid == CurrentGUID) return;
                    Close();
                    TrySwap(Game, ObjectID, CurrentGUID, guid);
                };
                Add(thumb);
                PageItems.Add(thumb);
            }
        }

        /// <summary>Checks money and whether the object is free, then asks the game to swap it.</summary>
        public static void TrySwap(TS1GameScreen game, short objectID, uint currentGUID, uint newGUID)
        {
            if (newGUID == currentGUID) return;
            var vm = game.vm;
            var catalog = Content.Get().WorldCatalog;
            var diff = (int)(catalog.GetItemByGUID(newGUID)?.Price ?? 0) - (int)(catalog.GetItemByGUID(currentGUID)?.Price ?? 0);
            if (diff > 0 && game.LotControl.Budget < diff)
            {
                UIClassicDialog.Ok("Not Enough Money", "You need §" + diff.ToString("##,#0") + " more to change to that colour.");
                return;
            }
            var reason = VMNetSwapObjectCmd.CheckSwap(vm, vm.GetObjectById(objectID), newGUID);
            if (reason != null)
            {
                UIClassicDialog.Ok("Can't Change Colour", reason);
                return;
            }
            FSO.HIT.HITVM.Get().PlaySoundEvent(diff > 0 ? UISounds.MoneyBack : UISounds.ObjectPlace);
            vm.SendCommand(new VMNetSwapObjectCmd { ObjectID = objectID, NewGUID = newGUID });
        }

        private void Close()
        {
            UIScreen.RemoveDialog(this);
        }

        public override void Update(UpdateState state)
        {
            Position = new Vector2((int)((GlobalSettings.Default.GraphicsWidth - W) / 2), 40);
            if (state.NewKeys.Contains(Microsoft.Xna.Framework.Input.Keys.Escape)) { Close(); return; }
            if (Target == null) { Close(); return; } // object gone (sold, swapped...)
            base.Update(state);
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(0, 0, W, H);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            DrawLocalTexture(batch, px, null, new Vector2(6, 6), new Vector2(W, H), Color.Black * 0.35f);
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(W, H), new Color(0, 0, 82));
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(W, 2), new Color(120, 120, 200));
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(2, H), new Color(120, 120, 200));
            DrawLocalTexture(batch, px, null, new Vector2(0, H - 2), new Vector2(W, 2), new Color(0, 0, 40));
            DrawLocalTexture(batch, px, null, new Vector2(W - 2, 0), new Vector2(2, H), new Color(0, 0, 40));

            DrawLocalString(batch, "Colours: " + ObjectName, new Vector2(20, 12), TitleStyle, new Rectangle(0, 0, W - 140, 30), TextAlignment.Left | TextAlignment.Middle);

            var infoY = H - 92;
            string info;
            if (Variants.Count <= 1) info = "This one only comes in one colour. Use Tint to recolour it any way you like.";
            else info = HoverText ?? "Click a colour to swap to it, or use Tint for any colour.";
            if (Variants.Count <= 1)
                DrawLocalString(batch, info, new Vector2(20, 56), Style, new Rectangle(0, 0, W - 40, 40), TextAlignment.Center | TextAlignment.Middle);
            else
                DrawLocalString(batch, info, new Vector2(20, infoY), Style, new Rectangle(0, 0, W - 40, 30), TextAlignment.Center | TextAlignment.Middle);
            base.Draw(batch);
        }

        // ------------------------------------------------------------------
        // catalog pictures and names
        // ------------------------------------------------------------------

        private static Dictionary<uint, Texture2D> Icons = new Dictionary<uint, Texture2D>();
        private static Dictionary<uint, string> Names = new Dictionary<uint, string>();

        public static Texture2D GetIcon(uint guid)
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

        public static string GetName(uint guid)
        {
            string name;
            if (Names.TryGetValue(guid, out name)) return name;
            name = Content.Get().WorldCatalog.GetItemByGUID(guid)?.Name ?? "";
            try
            {
                var obj = Content.Get().WorldObjects.Get(guid);
                var str = obj?.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0);
                if (!string.IsNullOrEmpty(str)) name = str;
            }
            catch { }
            Names[guid] = name;
            return name;
        }
    }
}
