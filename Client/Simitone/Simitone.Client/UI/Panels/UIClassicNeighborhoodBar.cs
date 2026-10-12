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

        // button layout measured from the original screens
        private const int ButtonY = 5;
        private const int MainFirstX = 204, Spacing = 50;
        private const int SubFirstX = 330;

        public UIClassicNeighborhoodBar(UINeighborhoodSelectionPanel panel, ushort mode, bool moveIn)
        {
            Panel = panel;
            Mode = mode;
            Banner = TS1UIGraphics.Get(mode == 4 ? "NghUI/banner_neighborhood.bmp" : "NghUI/banner_downtown.bmp");
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
            // neighbourhood switcher (only one neighbourhood for now)
            var prev = Btn("NghUI/Previous.bmp", 110, 10);
            var next = Btn("NghUI/Next.bmp", 146, 10);
            prev.Disabled = true;
            next.Disabled = true;

            NeighNumber = new UILabel();
            NeighNumber.Caption = "1";
            NeighNumber.Position = new Vector2(128, 10);
            NeighNumber.Size = new Vector2(16, 18);
            NeighNumber.Alignment = TextAlignment.Center;
            NeighNumber.CaptionStyle = NeighNumber.CaptionStyle.Clone();
            NeighNumber.CaptionStyle.Size = 14;
            NeighNumber.CaptionStyle.Color = Color.White;
            Add(NeighNumber);

            int x = MainFirstX;
            var family = Btn("NghUI/MoveIn.bmp", x, ButtonY); x += Spacing;
            var newHouse = Btn("NghUI/Import.bmp", x, ButtonY); x += Spacing;
            var bulldoze = Btn("NghUI/Bulldoze.bmp", x, ButtonY); x += Spacing;
            var rezone = Btn("NghUI/Rezone.bmp", x, ButtonY); x += Spacing;
            var downtown = Btn("NghUI/Downtown.bmp", x, ButtonY); x += Spacing;
            var vacation = Btn("NghUI/Vacation.bmp", x, ButtonY); x += Spacing;
            var studio = Btn("NghUI/Studiotown.bmp", x, ButtonY); x += Spacing;
            var magic = Btn("NghUI/MagicLand.bmp", x, ButtonY); x += Spacing;
            var exit = Btn("NghUI/Exit.bmp", x, ButtonY);

            family.OnButtonClick += (b) => GoToCAS();
            downtown.OnButtonClick += (b) => Panel.ClassicGoTo(2);
            vacation.OnButtonClick += (b) => Panel.ClassicGoTo(3);
            studio.OnButtonClick += (b) => Panel.ClassicGoTo(5);
            magic.OnButtonClick += (b) => Panel.ClassicGoTo(7);
            exit.OnButtonClick += (b) => ConfirmExit();

            // not supported yet
            newHouse.Disabled = true;
            bulldoze.Disabled = true;
            rezone.Disabled = true;

            // while moving a family in, you can only pick a house here
            if (moveIn)
            {
                family.Disabled = true;
                downtown.Disabled = vacation.Disabled = studio.Disabled = true;
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
            bulldoze.Disabled = true; // not supported yet
            import.Disabled = true;   // not supported yet
        }

        private void GoToCAS()
        {
            new UITransDialog("cas", () => { GameController.EnterCAS(); });
        }

        private void ConfirmExit()
        {
            UIClassicDialog.YesNo("Quit The Sims?", "Are you sure you want to quit?", () => GameFacade.Game.Exit());
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
