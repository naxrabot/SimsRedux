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
    /// Loading a normal house shows a classic navy box over the neighbourhood with the lot's
    /// name, what is happening and a progress bar.
    ///
    /// The game loads a lot in one go (the screen freezes while it loads), so the work is split
    /// into steps: the bar fills up to each step, the step runs, then it moves on to the next.
    /// </summary>
    public class UIClassicLoadScreen : UIContainer
    {
        public class Step
        {
            public string Message;
            public Action Work;
            /// <summary>Frames to show this step (filling the bar) before its work runs.</summary>
            public int Frames;
            public Step(string message, Action work, int frames) { Message = message; Work = work; Frames = Math.Max(1, frames); }
        }

        private Texture2D Picture;
        /// <summary>What fills the screen around the picture (black for the Sims 1 travel pictures).</summary>
        private Color PictureFill = Color.Black;
        private string Title;
        private string Message;
        private List<Step> Steps;
        private Action OnFail;
        private int StepIndex;
        private int StepFrame;
        private bool Busy;
        private bool Finished;
        private int FinishFrames;
        private int DrawnFrames;
        private float Progress;
        private TextStyle TitleStyle, MsgStyle;
        /// <summary>Darken the whole screen behind the box (hides the lot while it's being worked on).</summary>
        public bool Cover;

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

        private UIClassicLoadScreen(string picture, string title, List<Step> steps, Action onFail)
        {
            if (picture != null) Picture = TS1UIGraphics.Get(picture);
            Title = title;
            Steps = steps;
            OnFail = onFail;
            Message = steps.Count > 0 ? steps[0].Message : "";
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

            // the bar fills while these show, then the lot really loads (the last step)
            var steps = new List<Step>
            {
                new Step("Opening the front door...", null, 8),
                new Step("Unpacking the furniture...", null, 8),
                new Step("Waking up the Sims...", work, 6),
            };
            var screen = new UIClassicLoadScreen(picture, "Loading " + LotName(houseID), steps, null);
            UIScreen.GlobalShowDialog(screen, true);
        }

        /// <summary>
        /// Shows the classic box and runs each step in turn (with the bar filling between them).
        /// If a step fails, the rest are skipped and onFail runs instead.
        /// </summary>
        public static UIClassicLoadScreen RunSteps(string title, bool cover, Action onFail, params Step[] steps)
        {
            var screen = new UIClassicLoadScreen(null, title, new List<Step>(steps), onFail);
            screen.Cover = cover;
            UIScreen.GlobalShowDialog(screen, true);
            return screen;
        }

        /// <summary>
        /// SimsRedux online: a loading box that stays up until Close() is called, for things that
        /// happen over the network (the bar moves when SetWaiting gives it progress).
        /// </summary>
        private bool Waiting;
        public static UIClassicLoadScreen ShowWaiting(string title, string message, bool wallpaper = false)
        {
            var screen = new UIClassicLoadScreen(null, title, new List<Step>(), null);
            if (wallpaper)
            {
                // the full-screen Sims Redux wallpaper, with the title, message and bar on it
                screen.Picture = Simitone.Client.UI.Screens.UISimitoneBg.SharedWallpaper();
                screen.PictureFill = Simitone.Client.UI.Screens.UISimitoneBg.SharedWallpaperBlue;
            }
            screen.Waiting = true;
            screen.Cover = true;
            screen.Message = message;
            screen.Progress = 0.05f;
            UIScreen.GlobalShowDialog(screen, true);
            return screen;
        }

        public void SetWaiting(string message, float progress)
        {
            Message = message;
            Progress = Math.Max(Progress, MathHelper.Clamp(progress, 0, 1));
        }

        public void Close()
        {
            UIScreen.RemoveDialog(this);
        }

        /// <summary>The lot's name (or street address), for the loading box.</summary>
        public static string LotName(int houseID)
        {
            try
            {
                var info = UIClassicLotInfo.Get(houseID);
                if (!string.IsNullOrEmpty(info.Name)) return info.Name;
                if (!string.IsNullOrEmpty(info.Street)) return info.Street;
            }
            catch { }
            return "Lot " + houseID;
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
            var neigh = Content.Get().Neighborhood;
            return neigh.IsCommunity((short)houseID) && neigh.GetFamilyForHouse((short)houseID) == null;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (Waiting)
            {
                // creep the bar along so it never looks frozen while waiting
                if (Progress < 0.9f) Progress += 0.0005f;
                return;
            }
            if (DrawnFrames < 2) return; // make sure the box is on screen before anything freezes

            if (Finished)
            {
                if (++FinishFrames == 3) UIScreen.RemoveDialog(this);
                return;
            }
            if (Busy) return;
            if (StepIndex >= Steps.Count) { Finish(); return; }

            var step = Steps[StepIndex];
            Message = step.Message;
            StepFrame++;
            // fill the bar up to the end of this step
            float from = (float)StepIndex / Steps.Count, to = (float)(StepIndex + 1) / Steps.Count;
            Progress = from + (to - from) * Math.Min(1f, StepFrame / (float)step.Frames) * 0.95f;

            if (StepFrame >= step.Frames)
            {
                Busy = true;
                GameThread.NextUpdate((s) =>
                {
                    bool ok = true;
                    try { step.Work?.Invoke(); }
                    catch (Exception e)
                    {
                        ok = false;
                        Console.WriteLine("[SimsRedux] Loading step failed (" + step.Message + "): " + e);
                    }
                    if (!ok)
                    {
                        try { OnFail?.Invoke(); } catch (Exception e) { Console.WriteLine("[SimsRedux] " + e.Message); }
                        Finish();
                        return;
                    }
                    StepIndex++;
                    StepFrame = 0;
                    Busy = false;
                    if (StepIndex >= Steps.Count) Finish();
                });
            }
        }

        private void Finish()
        {
            Progress = 1f;
            Message = "Done!";
            Finished = true;
            Busy = false;
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(0, 0, GlobalSettings.Default.GraphicsWidth, GlobalSettings.Default.GraphicsHeight);
        }

        private void DrawBar(UISpriteBatch batch, Texture2D px, Vector2 pos, Vector2 size)
        {
            DrawLocalTexture(batch, px, null, pos, size, new Color(0, 0, 40));
            DrawLocalTexture(batch, px, null, pos, new Vector2(size.X, 1), new Color(0, 0, 20));
            DrawLocalTexture(batch, px, null, pos + new Vector2(0, size.Y - 1), new Vector2(size.X, 1), new Color(110, 110, 190));
            var fill = (int)((size.X - 4) * MathHelper.Clamp(Progress, 0, 1));
            if (fill > 0)
            {
                DrawLocalTexture(batch, px, null, pos + new Vector2(2, 2), new Vector2(fill, size.Y - 4), new Color(70, 200, 70));
                DrawLocalTexture(batch, px, null, pos + new Vector2(2, 2), new Vector2(fill, (size.Y - 4) / 2), new Color(140, 240, 120));
            }
        }

        private void DrawBox(UISpriteBatch batch, Texture2D px, Vector2 pos, Vector2 size)
        {
            DrawLocalTexture(batch, px, null, pos + new Vector2(6, 6), size, Color.Black * 0.35f);
            DrawLocalTexture(batch, px, null, pos, size, new Color(0, 0, 82));
            DrawLocalTexture(batch, px, null, pos, new Vector2(size.X, 2), new Color(120, 120, 200));
            DrawLocalTexture(batch, px, null, pos, new Vector2(2, size.Y), new Color(120, 120, 200));
            DrawLocalTexture(batch, px, null, pos + new Vector2(0, size.Y - 2), new Vector2(size.X, 2), new Color(0, 0, 40));
            DrawLocalTexture(batch, px, null, pos + new Vector2(size.X - 2, 0), new Vector2(2, size.Y), new Color(0, 0, 40));
        }

        public override void Draw(UISpriteBatch batch)
        {
            DrawnFrames++;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            float sw = GlobalSettings.Default.GraphicsWidth, sh = GlobalSettings.Default.GraphicsHeight;

            // (made once, not every frame)
            if (TitleStyle == null)
            {
                TitleStyle = TextStyle.DefaultLabel.Clone();
                TitleStyle.Size = 16;
                TitleStyle.Color = new Color(225, 225, 250);
                MsgStyle = TextStyle.DefaultLabel.Clone();
                MsgStyle.Size = 12;
                MsgStyle.Color = new Color(170, 170, 230);
            }
            var titleStyle = TitleStyle;
            var msgStyle = MsgStyle;

            if (Picture != null && Picture.Width > 1)
            {
                // full-screen travel picture, scaled to fit, black bars around it
                DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(sw, sh), PictureFill);
                float scale = Math.Min(sw / Picture.Width, sh / Picture.Height);
                var ppos = new Vector2((sw - Picture.Width * scale) / 2, (sh - Picture.Height * scale) / 2);
                DrawLocalTexture(batch, Picture, null, ppos, new Vector2(scale, scale));

                // a slim bar near the bottom of the picture
                var bsize = new Vector2(320, 14);
                var bpos = new Vector2((int)((sw - bsize.X) / 2), (int)(ppos.Y + Picture.Height * scale - 46));
                DrawBar(batch, px, bpos, bsize);
                if (Waiting)
                {
                    // online joining: say where we're going, on a dark band so it reads on any picture
                    var band = new Vector2(bsize.X + 120, 74);
                    var bandPos = new Vector2(bpos.X - 60, bpos.Y - 62);
                    DrawLocalTexture(batch, px, null, bandPos, band, new Color(0, 0, 40) * 0.8f);
                    DrawLocalString(batch, Title ?? "", bandPos + new Vector2(0, 6), titleStyle, new Rectangle(0, 0, (int)band.X, 24), TextAlignment.Center | TextAlignment.Middle);
                }
                DrawLocalString(batch, Message ?? "", bpos + new Vector2(1, -21), msgStyle, new Rectangle(0, 0, (int)bsize.X, 18), TextAlignment.Center | TextAlignment.Middle);
            }
            else
            {
                if (Cover) DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(sw, sh), Color.Black * 0.85f);

                // classic box over the current screen
                var size = new Vector2(380, 116);
                var pos = new Vector2((int)((sw - size.X) / 2), (int)((sh - size.Y) / 2));
                DrawBox(batch, px, pos, size);

                DrawLocalString(batch, Title ?? "Loading...", pos + new Vector2(14, 12), titleStyle, new Rectangle(0, 0, (int)size.X - 28, 26), TextAlignment.Center | TextAlignment.Middle);
                DrawLocalString(batch, Message ?? "", pos + new Vector2(14, 42), msgStyle, new Rectangle(0, 0, (int)size.X - 28, 20), TextAlignment.Center | TextAlignment.Middle);
                DrawBar(batch, px, pos + new Vector2(24, 74), new Vector2(size.X - 48, 18));
            }
            base.Draw(batch);
        }
    }
}
