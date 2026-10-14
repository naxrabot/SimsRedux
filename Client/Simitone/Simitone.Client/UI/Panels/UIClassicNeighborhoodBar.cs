using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.Utils;
using System;
using System.Linq;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// SimsRedux: the original Sims 1 neighbourhood top bar (logo, neighbourhood arrows,
    /// round buttons for families, travel, exit...), drawn from the player's UIGraphics.far.
    /// Lives inside UINeighborhoodSelectionPanel, so it uses the 800x600 map coordinates.
    ///
    /// Neighbourhood modes: 4 = main, 2 = Downtown, 3 = Vacation Island, 5 = Studio Town, 7 = Magic Town.
    /// </summary>
    public class UIClassicNeighborhoodBar : UIContainer
    {
        /// <summary>True when the classic interface is turned on and the Sims 1 art can be found.</summary>
        public static bool Enabled
        {
            get
            {
                var args = FSOEnvironment.Args ?? "";
                return (args.Contains("-classiccas") || args.Contains("-classic")) && TS1UIGraphics.Available;
            }
        }

        private UINeighborhoodSelectionPanel Panel;
        private Texture2D Banner;
        private Texture2D Logo;
        private ushort Mode;
        private UILabel NeighNumber;
        private UITS1Button NewHouseBtn, BulldozeBtn, RezoneBtn;

        // button layout measured from the original screens
        private const int ButtonY = 5;
        private const int MainFirstX = 204, Spacing = 50;
        private const int SubFirstX = 330;

        public UIClassicNeighborhoodBar(UINeighborhoodSelectionPanel panel, ushort mode, bool moveIn)
        {
            Panel = panel;
            Mode = mode;
            Banner = TS1UIGraphics.Get(mode == 4 ? "NghUI/banner_neighborhood.bmp" : "NghUI/banner_downtown.bmp");
            panel.ToolPick = UseTool;
            Logo = TS1UIGraphics.Get("NghUI/TheSimsLogo.bmp");

            switch (mode)
            {
                case 4: BuildMain(moveIn); break;
                case 2: BuildSub("Downtown/Return.bmp", "Downtown/Bulldoze.bmp", "Downtown/Import.bmp", false); break;
                case 3: BuildSub("NghUI/Return.bmp", "NghUI/Bulldoze.bmp", "VIsland/Import.bmp", false); break;
                case 5: BuildSub("Studiotown/Return.bmp", "Studiotown/Bulldoze.bmp", "Studiotown/import.bmp", false); break;
                case 7: BuildSub("Magicland/Return.bmp", "Magicland/Bulldoze.bmp", "Magicland/Import.bmp", true); break;
                default: BuildSub("NghUI/Return.bmp", "NghUI/Bulldoze.bmp", "NghUI/Import.bmp", false); break;
            }
        }

        private UITS1Button Btn(string path, int x, int y)
        {
            var tex = TS1UIGraphics.Get(path);
            var btn = new UITS1Button(tex, TS1UIGraphics.GuessFrames(tex));
            btn.Position = new Vector2(x, y);
            Add(btn);
            return btn;
        }

        private void BuildMain(bool moveIn)
        {
            // neighbourhood switcher (only one neighbourhood for now):  < 1 >  in one row, centred
            // in the bar, pushed right up to the round buttons to leave room for the version text
            var bandMid = BandMiddle();
            var prevTex = TS1UIGraphics.Get("NghUI/Previous.bmp");
            var nextTex = TS1UIGraphics.Get("NghUI/Next.bmp");
            int prevW = prevTex.Width / Math.Max(1, TS1UIGraphics.GuessFrames(prevTex));
            int nextW = nextTex.Width / Math.Max(1, TS1UIGraphics.GuessFrames(nextTex));
            const int numberW = 20;
            int nextX = MainFirstX - 10 - nextW;
            int numberX = nextX - numberW;
            int prevX = numberX - prevW;
            var prev = Btn("NghUI/Previous.bmp", prevX, (int)Math.Round(bandMid - prevTex.Height / 2f));
            var next = Btn("NghUI/Next.bmp", nextX, (int)Math.Round(bandMid - nextTex.Height / 2f));
            prev.Disabled = true;
            next.Disabled = true;

            NeighNumber = new UILabel();
            NeighNumber.Caption = "1";
            NeighNumber.Position = new Vector2(numberX, bandMid - 11);
            NeighNumber.Size = new Vector2(numberW, 22);
            NeighNumber.Alignment = TextAlignment.Center | TextAlignment.Middle;
            NeighNumber.CaptionStyle = NeighNumber.CaptionStyle.Clone();
            NeighNumber.CaptionStyle.Size = 14;
            NeighNumber.CaptionStyle.Color = Color.White;
            Add(NeighNumber);

            // SimsRedux: "Redux / Beta / v0.1" next to The Sims logo
            int textX = 8 + (Logo?.Width ?? 70) + 2;
            int textW = Math.Max(30, prevX - 4 - textX);
            AddVersionLine("Redux", 13, Color.White, textX, bandMid - 20, textW, 16);
            AddVersionLine(ReduxVersion.Stage, 9, new Color(255, 205, 70), textX, bandMid - 4, textW, 12);
            AddVersionLine(ReduxVersion.Text, 9, new Color(205, 210, 255), textX, bandMid + 8, textW, 12);

            ReduxVersion.ShowUpdatePopupOnce();
            if (!moveIn) ReduxFreshHood.OfferIfEmpty();

            // SimsRedux: in the online town, "Online Town" sits where the neighbourhood arrows are
            bool town = ReduxTown.Active;
            if (town)
            {
                prev.Visible = next.Visible = NeighNumber.Visible = false;
                int tw = nextX + nextW - prevX;
                AddVersionLine("Online", 11, new Color(120, 255, 140), prevX - 6, bandMid - 13, tw + 12, 13);
                AddVersionLine("Town", 11, new Color(120, 255, 140), prevX - 6, bandMid, tw + 12, 13);
            }

            int x = MainFirstX;
            var family = Btn("NghUI/MoveIn.bmp", x, ButtonY); x += Spacing;
            var newHouse = Btn("NghUI/Import.bmp", x, ButtonY); x += Spacing;
            var bulldoze = Btn("NghUI/Bulldoze.bmp", x, ButtonY); x += Spacing;
            var rezone = Btn("NghUI/Rezone.bmp", x, ButtonY); x += Spacing;
            var downtown = Btn("NghUI/Downtown.bmp", x, ButtonY); x += Spacing;
            var vacation = Btn("NghUI/Vacation.bmp", x, ButtonY); x += Spacing;
            var studio = Btn("NghUI/Studiotown.bmp", x, ButtonY); x += Spacing;
            var magic = Btn("NghUI/MagicLand.bmp", x, ButtonY); x += Spacing;
            // SimsRedux: no Quit button in the browser (a web page can't quit; you just close the tab)
            bool showExit = !System.OperatingSystem.IsBrowser();
            var exit = showExit ? Btn("NghUI/Exit.bmp", x, ButtonY) : null;

            family.OnButtonClick += (b) => GoToCAS();
            downtown.OnButtonClick += (b) => Panel.ClassicGoTo(2);
            vacation.OnButtonClick += (b) => Panel.ClassicGoTo(3);
            studio.OnButtonClick += (b) => Panel.ClassicGoTo(5);
            magic.OnButtonClick += (b) => Panel.ClassicGoTo(7);
            if (exit != null) exit.OnButtonClick += (b) => ConfirmExit();

            // SimsRedux: pick a tool, then click a lot
            NewHouseBtn = newHouse; BulldozeBtn = bulldoze; RezoneBtn = rezone;
            newHouse.OnButtonClick += (b) => PickTool(UINeighborhoodSelectionPanel.NeighTool.NewHouse);
            bulldoze.OnButtonClick += (b) => PickTool(UINeighborhoodSelectionPanel.NeighTool.Bulldoze);
            rezone.OnButtonClick += (b) => PickTool(UINeighborhoodSelectionPanel.NeighTool.Rezone);

            // SimsRedux: play online (go to the town, host a house, or visit a friend)
            if (!moveIn)
            {
                var online = new UITS1TileButton(TS1UIGraphics.Get("cpanel/Buttons/ButtonTileDialog.bmp"), town ? "Leave Town" : "Online", 110);
                online.TextSize = town ? 13 : 15;
                online.Position = new Vector2((showExit ? x + Spacing : x) + 6, 9); // takes the Quit button's place in the browser
                if (town) online.OnButtonClick += (b) => UIClassicDialog.YesNo("Leave the Town?", "Go back to your own neighborhood?",
                    () => (UIScreen.Current as Simitone.Client.UI.Screens.TS1GameScreen)?.LeaveTown());
                else online.OnButtonClick += (b) => UIClassicOnlineDialog.AskToConnect();
                Add(online);
            }

            // the town is for travelling for now: families, building and the other neighbourhoods come later
            if (town)
            {
                // (the family button makes a new family for the online town)
                downtown.Disabled = vacation.Disabled = studio.Disabled = magic.Disabled = true;
                newHouse.Disabled = bulldoze.Disabled = rezone.Disabled = true;
            }

            // while moving a family in, you can only pick a house here
            if (moveIn)
            {
                family.Disabled = true;
                downtown.Disabled = vacation.Disabled = studio.Disabled = true;
                newHouse.Disabled = bulldoze.Disabled = rezone.Disabled = true;
            }
        }

        private void AddVersionLine(string text, int size, Color color, int x, float y, int w, int h)
        {
            var label = new UILabel();
            label.Caption = text;
            label.Position = new Vector2(x, y);
            label.Size = new Vector2(w, h);
            label.Alignment = TextAlignment.Center | TextAlignment.Middle;
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = color;
            label.CaptionStyle.Shadow = true;
            Add(label);
        }

        /// <summary>
        /// The middle (top to bottom) of the purple bar between the logo and the round buttons,
        /// measured from the banner picture itself so things line up whatever the art.
        /// </summary>
        private float BandMiddle()
        {
            const float fallback = 30;
            try
            {
                if (Banner == null) return fallback;
                int w = Banner.Width, h = Banner.Height;
                int col = Math.Min(w - 1, MainFirstX - 30);
                var data = new Color[w * h];
                Banner.GetData(data);
                int top = -1, bottom = -1;
                for (int y = 0; y < h; y++)
                {
                    if (data[col + y * w].A > 0)
                    {
                        if (top < 0) top = y;
                        bottom = y;
                    }
                }
                if (top < 0 || bottom - top < 16) return fallback;
                return (top + bottom) / 2f;
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Couldn't measure the neighbourhood bar: " + e.Message);
                return fallback;
            }
        }

        private void BuildSub(string returnPath, string bulldozePath, string importPath, bool hasFamily)
        {
            if (hasFamily)
            {
                var family = Btn("NghUI/MoveIn.bmp", MainFirstX, ButtonY);
                family.OnButtonClick += (b) => GoToCAS();
            }
            int x = SubFirstX;
            var back = Btn(returnPath, x, ButtonY); x += Spacing;
            var bulldoze = Btn(bulldozePath, x, ButtonY); x += Spacing;
            var import = Btn(importPath, x, ButtonY);

            back.OnButtonClick += (b) => Panel.ClassicGoTo(4);
            BulldozeBtn = bulldoze;
            bulldoze.OnButtonClick += (b) => PickTool(UINeighborhoodSelectionPanel.NeighTool.Bulldoze);
            import.Disabled = true;   // not supported yet
        }

        /// <summary>Clicking a tool picks it; clicking it again puts it away.</summary>
        private void PickTool(UINeighborhoodSelectionPanel.NeighTool tool)
        {
            Panel.Tool = (Panel.Tool == tool) ? UINeighborhoodSelectionPanel.NeighTool.None : tool;
        }

        /// <summary>Lights up the button of the tool in use.</summary>
        public void ToolChanged()
        {
            var tool = Panel.Tool;
            if (NewHouseBtn != null) NewHouseBtn.Selected = tool == UINeighborhoodSelectionPanel.NeighTool.NewHouse;
            if (BulldozeBtn != null) BulldozeBtn.Selected = tool == UINeighborhoodSelectionPanel.NeighTool.Bulldoze;
            if (RezoneBtn != null) RezoneBtn.Selected = tool == UINeighborhoodSelectionPanel.NeighTool.Rezone;
        }

        /// <summary>A lot was clicked with a tool picked: check it, ask, then do it.</summary>
        private void UseTool(int house)
        {
            var tool = Panel.Tool;
            UIClassicLotInfo.HoverInfo info;
            try { info = UIClassicLotInfo.GetHover(house); }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Couldn't read lot " + house + ": " + e.Message);
                return;
            }
            bool ok;
            var hint = UIClassicLotInfo.ToolHint(info, tool, out ok);
            var screen = UIScreen.Current as Simitone.Client.UI.Screens.TS1GameScreen;
            if (screen == null) return;
            var name = info.Title;

            switch (tool)
            {
                case UINeighborhoodSelectionPanel.NeighTool.NewHouse:
                    if (!ok)
                    {
                        UIClassicDialog.Ok("New House", info.Occupied
                            ? "Someone already lives at " + name + ". Pick an empty lot to build a new house."
                            : name + " already has a building on it. Bulldoze it first, or pick an empty lot.");
                        return;
                    }
                    UIClassicDialog.YesNo("New House", "Do you want to build a new " + (info.Community ? "community lot" : "house") + " on " + name + "?",
                        () => { Panel.Tool = UINeighborhoodSelectionPanel.NeighTool.None; screen.BuildNewHouse((short)house); });
                    break;

                case UINeighborhoodSelectionPanel.NeighTool.Bulldoze:
                    if (!ok)
                    {
                        UIClassicDialog.Ok("Bulldoze", info.Occupied
                            ? "The family living at " + name + " must be evicted before it can be bulldozed. Click the lot and choose Evict."
                            : "There is nothing on " + name + " to bulldoze.");
                        return;
                    }
                    UIClassicDialog.YesNo("Bulldoze", "Are you sure you want to bulldoze " + name + "? Everything on the lot will be destroyed. This can't be undone.",
                        () => { Panel.Tool = UINeighborhoodSelectionPanel.NeighTool.None; screen.BulldozeLot((short)house); });
                    break;

                case UINeighborhoodSelectionPanel.NeighTool.Rezone:
                    if (!ok)
                    {
                        UIClassicDialog.Ok("Rezone", "The family living at " + name + " must be evicted before the lot can be rezoned.");
                        return;
                    }
                    var toCommunity = !info.Community;
                    UIClassicDialog.YesNo("Rezone", toCommunity
                        ? "Make " + name + " a community lot? Families won't be able to live there, but your Sims can visit it."
                        : "Make " + name + " a home lot? Families will be able to move in.",
                        () =>
                        {
                            Panel.Tool = UINeighborhoodSelectionPanel.NeighTool.None;
                            if (!FSO.Content.Content.Get().Neighborhood.SetZone((short)house, toCommunity))
                                UIClassicDialog.Ok("Rezone", "The new zoning couldn't be saved, so it will be lost when you quit.");
                            Panel.RefreshHouses();
                        });
                    break;
            }
        }

        private void GoToCAS()
        {
            if (ReduxTown.Active && ReduxTown.MyFamily >= 0)
            {
                UIClassicDialog.Ok("Create a Family", "Your family already lives in the online town.");
                return;
            }
            new UITransDialog("cas", () => { GameController.EnterCAS(); });
        }

        private void ConfirmExit()
        {
            if (System.OperatingSystem.IsBrowser())
            {
                // a web page can't close itself: just tell the player how to leave
                UIClassicDialog.Ok("Quit The Sims?", "To quit, just close this browser tab.\n\nYour neighbourhood is kept in this browser, so it'll be here next time.");
                return;
            }
            UIClassicDialog.YesNo("Quit The Sims?", "Are you sure you want to quit?", () => { Simitone.Client.UI.Screens.TS1GameScreen.ExitConfirmed = true; GameFacade.Game.Exit(); });
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            DrawLocalTexture(batch, Banner, Vector2.Zero);
            DrawLocalTexture(batch, Logo, new Vector2(8, 4));
            base.Draw(batch);
        }
    }
}
