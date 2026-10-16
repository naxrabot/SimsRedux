using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the Help window (the "?" button on the control panel, or F1). A classic navy
    /// box with a row of topic buttons and the controls for each part of the game, including
    /// everything SimsRedux adds (Sims 3 camera, free placement, colours, ...).
    /// </summary>
    public class UIClassicHelpDialog : UIContainer
    {
        private static UIClassicHelpDialog Open;
        private const int W = 720, H = 490;
        private static readonly Color TextColor = new Color(215, 215, 245);
        private TextStyle Body, Heading, TitleStyle;
        private int Page;
        private List<UITS1TileButton> Tabs = new List<UITS1TileButton>();

        private static readonly string[] TabNames = { "Camera", "Live", "Buy", "Build", "Town", "Options" };

        private static readonly string[][] Pages =
        {
            new[]
            {
                "#Moving the camera",
                "W A S D or the arrow keys move around. Hold Shift to move faster.",
                "Hold the right mouse button and drag to grab the ground and pull it.",
                "Q and E turn the view. Z and X (or the mouse wheel) zoom in and out.",
                "Hold the middle mouse button and drag left or right to turn the view.",
                "#Floors and walls",
                "The buttons on the left of the control panel go up and down a floor, and",
                "choose walls up, cutaway, walls down or roof view.",
                "#Speed",
                "The speed buttons pause the game or play it at normal, fast or fastest.",
                "Options > Play switches back to the original camera or turns edge scrolling on.",
            },
            new[]
            {
                "#Controlling your Sims",
                "Click a Sim, an object or the ground to see what your Sim can do there.",
                "Click a face on the left of the bar to take control of that Sim.",
                "Actions line up in the top left. Click one to cancel it.",
                "If a Sim ignores a cancel for a few seconds, they're reset so they can't get stuck.",
                "#The Live bar",
                "Mood shows needs. Then Personality, Interests, Job, House, Relationships",
                "(with Everyone / Family / Friends) and Inventory (Gifts / Magic / Other).",
                "Hover a face on the Relationships page for both relationship numbers.",
            },
            new[]
            {
                "#Buying",
                "Pick a room (top row) or a type of object (bottom row), then a picture to buy it.",
                "Click the lot to place it. Hold Shift while placing to place another one.",
                "Hold Alt while moving an object to place it off the grid.",
                "#Selling and colours",
                "Pick up an object from the lot and click the bar to sell it. Esc puts it back.",
                "Right-click an object on the lot to change its colour: pick another version",
                "or use Tint to recolour it any way you like.",
            },
            new[]
            {
                "#Build tools",
                "Terrain: raise or lower, level, and paint grass. Water: dig pools and add water.",
                "Wall & Fence, Wallpaper and Floors: pick a style, then drag on the lot.",
                "Doors, Windows, Stairs, Fireplaces and Plants are placed like objects.",
                "Roofs: choose the pitch on the left and the pattern on the right.",
                "The Hand picks up and moves things. Esc drops the current tool.",
            },
            new[]
            {
                "#The neighborhood",
                "Point at a house to see who lives there or what it costs. Click it to play or visit.",
                "A green plumbbob floats over the homes of your families.",
                "W A S D, the arrow keys or right-drag scroll the map.",
                "The buttons along the top make families, go downtown, on vacation and more.",
                "#Lots",
                "New House: pick an empty lot to build on it. Bulldoze: pick a lot nobody lives",
                "in to clear it. Rezone: switch a lot between home and community lot.",
                "Click the tool's button again or press Esc to put it away.",
                "#Online",
                "Online (top right): Host lets friends join the house you play next.",
                "Join visits a friend's lot with one of your Sims.",
            },
            new[]
            {
                "#Options",
                "Save, go back to the neighborhood, or quit from the Options bar.",
                "Graphics: lighting, smooth zoom, anti-aliasing and window or full screen.",
                "Sound: music, sound effects, voices and ambience volume.",
                "Play: Sims 3 style or classic camera, and screen-edge scrolling.",
                "#Getting help",
                "Press F1 or click the ? on the control panel to open this window again.",
            },
        };

        public static void Toggle()
        {
            if (Open != null) { Open.Close(); return; }
            Open = new UIClassicHelpDialog();
            UIScreen.GlobalShowDialog(Open, false);
        }

        public UIClassicHelpDialog()
        {
            Body = TextStyle.DefaultLabel.Clone(); Body.Size = 12; Body.Color = TextColor;
            Heading = TextStyle.DefaultLabel.Clone(); Heading.Size = 13; Heading.Color = new Color(120, 255, 140);
            TitleStyle = TextStyle.DefaultLabel.Clone(); TitleStyle.Size = 18; TitleStyle.Color = TextColor;

            var tile = TS1UIGraphics.Get("cpanel/Buttons/ButtonTileDialog.bmp");
            int x = 16;
            var measure = TextStyle.DefaultLabel.Clone();
            measure.Size = 14;
            for (int i = 0; i < TabNames.Length; i++)
            {
                var index = i;
                // as wide as the word needs, so the words never spill out of their buttons
                int w = (int)Math.Max(70, measure.MeasureString(TabNames[i]).X + 34);
                var btn = new UITS1TileButton(tile, TabNames[i], w);
                btn.TextSize = 14;
                btn.Position = new Vector2(x, 52);
                btn.OnButtonClick += (b) => Page = index;
                Add(btn);
                Tabs.Add(btn);
                x += w + 6;
            }
            var close = new UITS1TileButton(tile, "Close", 110);
            close.Position = new Vector2(W - 126, H - 48);
            close.OnButtonClick += (b) => Close();
            Add(close);
        }

        private void Close()
        {
            if (Open == this) Open = null;
            UIScreen.RemoveDialog(this);
        }

        /// <summary>Also forget it when it's taken away some other way (e.g. a screen change clears all dialogs).</summary>
        public override void Removed()
        {
            if (Open == this) Open = null;
            base.Removed();
        }

        public override void Update(UpdateState state)
        {
            Position = new Vector2((int)((GlobalSettings.Default.GraphicsWidth - W) / 2), (int)Math.Max(20, (GlobalSettings.Default.GraphicsHeight - H) / 2 - 60));
            if (state.NewKeys.Contains(Microsoft.Xna.Framework.Input.Keys.Escape)) { Close(); return; }
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

            DrawLocalString(batch, "Help", new Vector2(18, 12), TitleStyle, new Rectangle(0, 0, 300, 30), TextAlignment.Left | TextAlignment.Middle);

            // which topic is open
            var tab = Tabs[Page];
            var r = new Rectangle((int)tab.X - 3, (int)tab.Y - 3, (int)tab.Width + 6, 39);
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, 2), Color.Cyan);
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Bottom - 2), new Vector2(r.Width, 2), Color.Cyan);
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(2, r.Height), Color.Cyan);
            DrawLocalTexture(batch, px, null, new Vector2(r.Right - 2, r.Y), new Vector2(2, r.Height), Color.Cyan);

            // the page
            DrawLocalTexture(batch, px, null, new Vector2(16, 100), new Vector2(W - 32, H - 160), new Color(0, 0, 50));
            int y = 108;
            foreach (var line in Pages[Page])
            {
                bool head = line.StartsWith("#");
                if (head && y > 110) y += 6;
                DrawLocalString(batch, head ? line.Substring(1) : line, new Vector2(28, y), head ? Heading : Body, new Rectangle(0, 0, W - 56, 20), TextAlignment.Left | TextAlignment.Middle);
                y += 21;
            }
            base.Draw(batch);
        }
    }
}
