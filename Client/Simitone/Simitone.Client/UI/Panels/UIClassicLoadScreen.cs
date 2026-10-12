using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// SimsRedux: original Sims 1 loading screens. Travelling to Downtown shows the taxi picture,
    /// Vacation Island the boat, Studio Town the limo, Magic Town the balloon, Old Town the bus.
    /// Loading a normal house shows a small classic "Loading..." box over the neighbourhood.
    ///
    /// The game loads lots in one go (the screen freezes while it loads), so this draws the
    /// picture first, then runs the load, then removes itself.
    /// </summary>
    public class UIClassicLoadScreen : UIContainer
    {
        private Texture2D Picture;
        private string Message;
        private Action Work;
        private int Frames;
        private bool Started;

        // neighbourhood mode -> loading picture (1024x768 versions)
        private static readonly Dictionary<int, string> TravelPictures = new Dictionary<int, string>()
        {
            { 2, "Downtown/Taxi_loadscreen_1024x768.bmp" },
            { 3, "VIsland/vacation_loadscreen_1024x768.bmp" },
            { 5, "Studiotown/Studiotown_loadscreen_1024x768.bmp" },
            { 7, "Magicland/magicland_loadscreen_1024x768.bmp" },
        };
        private const string BusPicture = "Community/Bus_loadscreen_1024x768.bmp";
        private const string TaxiPicture = "Downtown/Taxi_loadscreen_1024x768.bmp";

        private UIClassicLoadScreen(string picture, string message, Action work)
        {
            if (picture != null) Picture = TS1UIGraphics.Get(picture);
            Message = message;
            Work = work;
        }

        /// <summary>Shows the right loading screen for a lot, then runs "work".</summary>
        public static void Run(int houseID, bool goingHome, Action work)
        {
            if (!UIClassicNeighborhoodBar.Enabled)
            {
                work();
                return;
            }
            string picture = null;
            try
            {
                var mode = ModeForHouse(houseID);
                if (goingHome && mode == 4) picture = TaxiPicture; // the taxi brings you home
                else if (TravelPictures.ContainsKey(mode)) picture = TravelPictures[mode];
                else if (mode == 4 && IsCommunity(houseID)) picture = BusPicture;
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Loading screen lookup failed: " + e.Message);
            }

            var screen = new UIClassicLoadScreen(picture, "Loading...", work);
            UIScreen.GlobalShowDialog(screen, true);
        }

        /// <summary>Which neighbourhood map (4 main, 2 Downtown, 3 Vacation, 5 Studio Town, 7 Magic Town) a lot is on.</summary>
        public static int ModeForHouse(int houseID)
        {
            var locations = Content.Get().Neighborhood.LotLocations;
            foreach (var mode in new int[] { 4, 2, 3, 5, 7 })
            {
                var str = locations.Get<STR>((ushort)mode);
                if (str == null) continue;
                for (int i = 0; i < str.Length; i++)
                {
                    var parts = str.GetString(i).Split(',');
                    int num;
                    if (parts.Length > 0 && int.TryParse(parts[0].Trim(), out num) && num == houseID) return mode;
                }
            }
            return 4;
        }

        private static bool IsCommunity(int houseID)
        {
            var zones = Content.Get().Neighborhood.ZoningDictionary;
            short zone;
            if (!zones.TryGetValue((short)houseID, out zone))
                zone = (short)((houseID >= 81 && houseID <= 89) ? 2 : 1);
            return zone > 0 && Content.Get().Neighborhood.GetFamilyForHouse((short)houseID) == null && zone != 0;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            // wait until the picture has actually been drawn, then load
            if (!Started && Frames >= 2)
            {
                Started = true;
                GameThread.NextUpdate((s) =>
                {
                    try { Work?.Invoke(); }
                    catch (Exception e) { Console.WriteLine("[SimsRedux] Load failed: " + e); }
                    GameThread.NextUpdate((s2) => UIScreen.RemoveDialog(this));
                });
            }
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(0, 0, GlobalSettings.Default.GraphicsWidth, GlobalSettings.Default.GraphicsHeight);
        }

        public override void Draw(UISpriteBatch batch)
        {
            Frames++;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            float sw = GlobalSettings.Default.GraphicsWidth, sh = GlobalSettings.Default.GraphicsHeight;

            if (Picture != null && Picture.Width > 1)
            {
                // full-screen travel picture, scaled to fit, black bars around it
                DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(sw, sh), Color.Black);
                float scale = Math.Min(sw / Picture.Width, sh / Picture.Height);
                var pos = new Vector2((sw - Picture.Width * scale) / 2, (sh - Picture.Height * scale) / 2);
                DrawLocalTexture(batch, Picture, null, pos, new Vector2(scale, scale));
            }
            else
            {
                // small classic box over the current screen
                var size = new Vector2(300, 80);
                var pos = new Vector2((int)((sw - size.X) / 2), (int)((sh - size.Y) / 2));
                DrawLocalTexture(batch, px, null, pos + new Vector2(6, 6), size, Color.Black * 0.35f);
                DrawLocalTexture(batch, px, null, pos, size, new Color(0, 0, 82));
                DrawLocalTexture(batch, px, null, pos, new Vector2(size.X, 2), new Color(120, 120, 200));
                DrawLocalTexture(batch, px, null, pos, new Vector2(2, size.Y), new Color(120, 120, 200));
                DrawLocalTexture(batch, px, null, pos + new Vector2(0, size.Y - 2), new Vector2(size.X, 2), new Color(0, 0, 40));
                DrawLocalTexture(batch, px, null, pos + new Vector2(size.X - 2, 0), new Vector2(2, size.Y), new Color(0, 0, 40));

                var style = TextStyle.DefaultLabel.Clone();
                style.Size = 18;
                style.Color = new Color(205, 205, 240);
                DrawLocalString(batch, Message, pos, style, new Rectangle(0, 0, (int)size.X, (int)size.Y), TextAlignment.Center | TextAlignment.Middle);
            }
            base.Draw(batch);
        }
    }
}
