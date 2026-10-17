using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// SimsRedux: while a Sim plays Twisted Metal 2 on a computer, the computer's monitor shows the game -
    /// screenshots, changing every few seconds in a random order (Content/redux/tm2/*.jpg).
    ///
    /// The monitor's screen is a small rectangle in the 3D world, measured from the computer: how far in
    /// front of its middle, how high above the desk, how wide and how tall. Its four corners are worked
    /// out like everything else in the world (camera and zoom included), so the picture stays on the
    /// monitor at every zoom, rotation, and in 3D. It's only drawn when the monitor faces the camera.
    ///
    /// Lining it up (PC): while someone's playing, hold Ctrl + Shift and use
    ///   I / K  forward / back        U / O  up / down        J / L  narrower / wider
    ///   N / M  shorter / taller      F      flip to the other side      Y / H  sideways
    /// The numbers are kept in redux_settings.ini (tm2screen3=...) and shown in a corner notice.
    /// </summary>
    public static class UIReduxComputerScreens
    {
        private const int Pictures = 7;
        private const double SecondsPerPicture = 3.5;
        // (a new name: numbers saved with the old, too-big picture aren't used)
        private const string SettingKey = "tm2screen3";

        // where the screen is, in tile units: in front of the computer's middle, to the side of it, above
        // its base, and its size. Lined up from two screenshots of the basic computer: the monitor sits a
        // little to one side of the computer's middle, and its glass is about 0.36 x 0.27.
        private static float Forward = 0.12f, Side = 0.035f, Height = 0.44f, Width = 0.33f, Tall = 0.245f;
        private static bool Flip;
        private static bool Loaded;

        private static List<Texture2D> Shots;
        private static bool ShotsTried;
        private static Dictionary<short, Tuple<int, DateTime>> Showing = new Dictionary<short, Tuple<int, DateTime>>();
        private static Random Rand = new Random();
        private static BasicEffect Effect;
        private static VertexPositionTexture[] Quad = new VertexPositionTexture[6];

        private static void LoadSettings()
        {
            if (Loaded) return;
            Loaded = true;
            try
            {
                var s = ReduxSettings.GetString(SettingKey, "");
                var p = s.Split(',');
                if (p.Length >= 5)
                {
                    Forward = float.Parse(p[0], CultureInfo.InvariantCulture);
                    Height = float.Parse(p[1], CultureInfo.InvariantCulture);
                    Width = float.Parse(p[2], CultureInfo.InvariantCulture);
                    Tall = float.Parse(p[3], CultureInfo.InvariantCulture);
                    Flip = p[4].Trim() == "1";
                    if (p.Length >= 6) Side = float.Parse(p[5], CultureInfo.InvariantCulture);
                }
            }
            catch { }
        }

        private static string SettingText()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2:0.00},{3:0.00},{4},{5:0.000}", Forward, Height, Width, Tall, Flip ? 1 : 0, Side);
        }

        private static List<Texture2D> GetShots()
        {
            if (ShotsTried) return Shots;
            ShotsTried = true;
            Shots = new List<Texture2D>();
            for (int i = 1; i <= Pictures; i++)
            {
                try
                {
                    var path = System.IO.Path.Combine(FSOEnvironment.ContentDir, "redux/tm2/tm2_" + i + ".jpg");
                    if (!FSO.Common.IO.FSOFile.Exists(path)) continue;
                    using (var stream = FSO.Common.IO.FSOFile.OpenRead(path))
                        Shots.Add(TextureLoad.FromStream(GameFacade.GraphicsDevice, stream));
                }
                catch (Exception e) { Console.WriteLine("[SimsRedux] Twisted Metal 2 picture " + i + ": " + e.Message); }
            }
            return Shots;
        }

        /// <summary>Ctrl + Shift + keys: line the picture up with the monitor (see above).</summary>
        public static void Update(UILotControl lot, UpdateState state)
        {
            if (lot?.vm == null || state.NewKeys.Count == 0) return;
            if (!(state.CtrlDown && state.ShiftDown)) return;
            if (FSO.SimAntics.Utils.ReduxComputer.PlayingTwistedMetal(lot.vm).Count == 0) return;
            LoadSettings();
            bool changed = false;
            foreach (var k in state.NewKeys)
            {
                switch (k)
                {
                    case Keys.I: Forward += 0.02f; changed = true; break;
                    case Keys.K: Forward -= 0.02f; changed = true; break;
                    case Keys.U: Height += 0.02f; changed = true; break;
                    case Keys.O: Height -= 0.02f; changed = true; break;
                    case Keys.J: Width = Math.Max(0.05f, Width - 0.02f); changed = true; break;
                    case Keys.L: Width += 0.02f; changed = true; break;
                    case Keys.N: Tall = Math.Max(0.05f, Tall - 0.02f); changed = true; break;
                    case Keys.M: Tall += 0.02f; changed = true; break;
                    case Keys.F: Flip = !Flip; changed = true; break;
                    case Keys.Y: Side -= 0.01f; changed = true; break;
                    case Keys.H: Side += 0.01f; changed = true; break;
                }
            }
            if (!changed) return;
            ReduxSettings.Set(SettingKey, SettingText());
            Simitone.Client.UI.Controls.UIReduxToasts.Show("Monitor picture", SettingKey + " = " + SettingText());
        }

        public static void Draw(UILotControl lot, UISpriteBatch batch)
        {
            var vm = lot?.vm;
            var world = lot?.World;
            if (vm == null || world == null || !vm.Ready) return;
            var computers = FSO.SimAntics.Utils.ReduxComputer.PlayingTwistedMetal(vm);
            if (computers.Count == 0) return;
            var shots = GetShots();
            if (shots.Count == 0) return;
            LoadSettings();

            var state = world.State;
            var gd = batch.GraphicsDevice;
            bool paused = false;
            try
            {
                foreach (var comp in computers)
                {
                    var ui = comp.WorldUI as ObjectComponent;
                    if (ui == null || comp.Position.Level > state.Level) continue;

                    // the computer's middle, which way it faces, and the screen's corners (tile units)
                    var centre = ui.Position + new Vector3(0.5f, 0.5f, 0);
                    var fwd = Vector3.Transform(new Vector3(0, Flip ? 1 : -1, 0), Matrix.CreateRotationZ(ui.RadianDirection));
                    var right = new Vector3(-fwd.Y, fwd.X, 0);
                    var mid = centre + fwd * Forward + right * Side + new Vector3(0, 0, Height);

                    // only when the screen faces the camera
                    var n3 = WorldSpace.GetWorldFromTile(fwd);
                    var viewN = Vector3.TransformNormal(n3, state.View);
                    if (viewN.Z <= 0.05f) continue;

                    var up = new Vector3(0, 0, Tall / 2);
                    var side = right * (Width / 2);
                    var tl = Project(lot, state, mid - side + up);
                    var tr = Project(lot, state, mid + side + up);
                    var bl = Project(lot, state, mid - side - up);
                    var br = Project(lot, state, mid + side - up);
                    // (never mirrored: the left of the picture is always on the left of the screen)
                    if (tr.X < tl.X) { var t = tl; tl = tr; tr = t; t = bl; bl = br; br = t; }

                    // which picture: a new random one every few seconds
                    Tuple<int, DateTime> now;
                    if (!Showing.TryGetValue(comp.ObjectID, out now) || DateTime.UtcNow >= now.Item2)
                    {
                        int next = Rand.Next(shots.Count);
                        if (now != null && shots.Count > 1 && next == now.Item1) next = (next + 1) % shots.Count;
                        now = new Tuple<int, DateTime>(next, DateTime.UtcNow.AddSeconds(SecondsPerPicture));
                        Showing[comp.ObjectID] = now;
                    }

                    if (!paused) { batch.Pause(); paused = true; }
                    DrawQuad(gd, shots[now.Item1], tl, tr, bl, br);
                }
            }
            catch (Exception e) { Console.WriteLine("[SimsRedux] monitor picture: " + e.Message); }
            finally { if (paused) batch.Resume(); }
        }

        /// <summary>A point in the lot (tile units) to screen pixels - the same way the speech bubbles do it.</summary>
        private static Vector2 Project(UILotControl lot, WorldState state, Vector3 tilePt)
        {
            var w = WorldSpace.GetWorldFromTile(tilePt);
            var p = Vector4.Transform(new Vector4(w, 1), state.View * state.Projection);
            if (state.CameraMode < CameraRenderMode._3D) p.Z = 1;
            var res = new Vector2(p.X / p.Z, -p.Y / p.Z);
            var size = PPXDepthEngine.GetWidthHeight();
            var px = new Vector2((size.X / PPXDepthEngine.SSAA) * 0.5f * (res.X + 1f), (size.Y / PPXDepthEngine.SSAA) * 0.5f * (res.Y + 1f));
            var dpi = FSOEnvironment.DPIScaleFactor;
            var middle = new Vector2(UIScreen.Current.ScreenWidth * dpi / 2f, UIScreen.Current.ScreenHeight * dpi / 2f);
            var scale = lot.World.BackbufferScale <= 0 ? 1f : lot.World.BackbufferScale;
            var corrected = (px - middle) * scale + middle;
            return corrected * state.PreciseZoom; // (screen pixels)
        }

        private static void DrawQuad(GraphicsDevice gd, Texture2D tex, Vector2 tl, Vector2 tr, Vector2 bl, Vector2 br)
        {
            if (Effect == null || Effect.IsDisposed)
            {
                Effect = new BasicEffect(gd) { TextureEnabled = true, VertexColorEnabled = false, LightingEnabled = false };
            }
            var vp = gd.Viewport;
            Effect.World = Matrix.Identity;
            Effect.View = Matrix.Identity;
            Effect.Projection = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, 1);
            Effect.Texture = tex;
            Effect.DiffuseColor = new Vector3(0.88f, 0.88f, 0.88f); // (a touch dimmer, like a screen in the room rather than on top of it)
            Quad[0] = new VertexPositionTexture(new Vector3(tl, 0), new Vector2(0, 0));
            Quad[1] = new VertexPositionTexture(new Vector3(tr, 0), new Vector2(1, 0));
            Quad[2] = new VertexPositionTexture(new Vector3(bl, 0), new Vector2(0, 1));
            Quad[3] = new VertexPositionTexture(new Vector3(tr, 0), new Vector2(1, 0));
            Quad[4] = new VertexPositionTexture(new Vector3(br, 0), new Vector2(1, 1));
            Quad[5] = new VertexPositionTexture(new Vector3(bl, 0), new Vector2(0, 1));
            gd.BlendState = BlendState.Opaque;
            gd.DepthStencilState = DepthStencilState.None;
            gd.RasterizerState = RasterizerState.CullNone;
            gd.SamplerStates[0] = SamplerState.LinearClamp;
            foreach (var pass in Effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.TriangleList, Quad, 0, 2);
            }
        }
    }
}
