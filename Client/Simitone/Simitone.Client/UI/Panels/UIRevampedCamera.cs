using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Client;
using FSO.LotView.Utils.Camera;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.UI.Screens;
using System;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// SimsRedux: Sims 3 style camera controls for lots.
    ///
    ///   W A S D / arrow keys ... move the camera (hold Shift to move faster)
    ///   Q / E .................. rotate left / right
    ///   Z / X .................. zoom in / out (the mouse wheel zooms too)
    ///   Right mouse drag ....... grab the ground and drag it
    ///   Middle mouse drag ...... rotate (and tilt in 3D mode)
    ///
    /// Screen-edge scrolling is off. Both can be changed in Options > Play. Launch options:
    ///   -edgescroll  turns screen-edge scrolling back on
    ///   -oldcamera   goes back to Simitone's original controls
    /// </summary>
    public class UIRevampedCamera
    {
        public static bool Enabled
        {
            get { return !HasArg("-oldcamera") && !Simitone.Client.Utils.ReduxSettings.ClassicCamera; }
        }

        public static bool EdgeScroll
        {
            get { return HasArg("-edgescroll") || Simitone.Client.Utils.ReduxSettings.EdgeScroll; }
        }

        /// <summary>
        /// True when a text box on screen has the keyboard (so WASD types instead of moving).
        /// A text box from a screen that's already gone (e.g. the Create-a-Sim bio box) can keep
        /// the keyboard after you leave it; that one doesn't count, and is let go.
        /// </summary>
        public static bool IsTyping(FSO.Common.Rendering.Framework.Model.UpdateState state)
        {
            var focus = state.InputManager?.GetFocus();
            if (focus == null) return false;
            var el = focus as FSO.Client.UI.Framework.UIElement;
            if (el == null) return true;
            FSO.Client.UI.Framework.UIElement p = el;
            bool stale = false;
            while (p != null)
            {
                if (!p.Visible) { stale = true; break; }
                if (p.Parent == null) break;
                p = p.Parent;
            }
            if (!stale && p is FSO.Client.UI.Framework.UIScreen && p != FSO.Client.UI.Framework.UIScreen.Current) stale = true;
            if (stale)
            {
                state.InputManager.SetFocus(null);
                return false;
            }
            return true;
        }

        private static bool HasArg(string arg)
        {
            // split on any whitespace: Visual Studio can pass arguments on separate lines
            var args = (FSOEnvironment.Args ?? "").ToLowerInvariant()
                .Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return Array.IndexOf(args, arg) >= 0;
        }

        private UILotControl Lot;
        private Vector2? GrabTile;       // the ground point held by the right mouse button
        private bool MiddleDown;
        private int MiddleStartX;        // 2D: where the middle-drag started (for 90 degree turns)

        public bool Dragging { get { return GrabTile != null; } }

        // SimsRedux: a right-click (press and let go without dragging) is passed on, e.g. to open
        // the colour picker in Buy mode. Dragging still moves the camera.
        public Action<Point> OnRightClick;
        private Point? RightDownAt;
        private bool RightMovedFar, RightWasDown;
        private const int ClickSlop = 6; // pixels the mouse may wobble and still count as a click

        private const float PanSpeed = 0.07f;   // tiles-ish per frame at 60fps, same feel as edge scrolling
        private const float ZoomSpeed = 1.2f;   // zoom steps per second
        private const int TurnDistance = 120;   // 2D: pixels of middle-drag for one 90 degree turn

        public UIRevampedCamera(UILotControl lot)
        {
            Lot = lot;
        }

        private TS1GameScreen Game
        {
            get { return Lot.Parent as TS1GameScreen; }
        }

        private bool Is3D
        {
            get { return Lot.World.State.Cameras.ActiveType == CameraControllerType._3D; }
        }

        /// <summary>Runs the controls. Returns true if the camera moved this frame.</summary>
        public bool Update(UpdateState state, bool mouseIsOn)
        {
            var world = Lot.World;
            if (world == null || world.State == null) return false;
            bool moved = false;
            float frame = 60f / FSOEnvironment.RefreshRate;

            // ---------------- keyboard ----------------
            bool typing = IsTyping(state);
            if (state.WindowFocused && !typing && !state.CtrlDown)
            {
                var kb = state.KeyboardState;
                var dir = Vector2.Zero;
                if (kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.Up)) dir.Y -= 1;
                if (kb.IsKeyDown(Keys.S) || kb.IsKeyDown(Keys.Down)) dir.Y += 1;
                if (kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left)) dir.X -= 1;
                if (kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right)) dir.X += 1;
                if (dir != Vector2.Zero)
                {
                    dir.Normalize();
                    var speed = PanSpeed * ((kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift)) ? 2.5f : 1f);
                    world.State.ScrollAnchor = null;
                    world.Scroll(dir * speed * frame);
                    moved = true;
                }

                // rotate: smooth in 3D, 90 degree steps in 2D (like the original game)
                if (Is3D)
                {
                    var cam = world.State.Cameras.Camera3D;
                    if (kb.IsKeyDown(Keys.Q)) { cam.RotationX -= 2f / FSOEnvironment.RefreshRate; moved = true; }
                    if (kb.IsKeyDown(Keys.E)) { cam.RotationX += 2f / FSOEnvironment.RefreshRate; moved = true; }
                }
                else if (Game != null)
                {
                    if (state.NewKeys.Contains(Keys.Q)) { Game.Rotation = (Game.Rotation + 3) % 4; moved = true; }
                    if (state.NewKeys.Contains(Keys.E)) { Game.Rotation = (Game.Rotation + 1) % 4; moved = true; }
                }

                // zoom
                float zoom = 0;
                if (kb.IsKeyDown(Keys.Z)) zoom += 1;
                if (kb.IsKeyDown(Keys.X)) zoom -= 1;
                if (zoom != 0)
                {
                    Lot.TargetZoom = Math.Max(0.25f, Math.Min(2f, Lot.TargetZoom + zoom * ZoomSpeed / FSOEnvironment.RefreshRate));
                    moved = true;
                }
            }

            // ---------------- right mouse: grab and drag the ground ----------------
            var mouse = state.MouseState;
            var mousePos = Lot.ToWorldScreen(new Vector2(mouse.X, mouse.Y)) / FSOEnvironment.DPIScaleFactor;
            if (mouse.RightButton == ButtonState.Pressed)
            {
                if (GrabTile == null)
                {
                    if (mouseIsOn) GrabTile = world.EstTileAtPosWithScroll(mousePos);
                }
                else
                {
                    // move the camera so the ground point we grabbed stays under the mouse
                    var now = world.EstTileAtPosWithScroll(mousePos);
                    var delta = GrabTile.Value - now;
                    if (delta.LengthSquared() > 0.00001f)
                    {
                        world.State.ScrollAnchor = null;
                        world.State.CenterTile += delta;
                        moved = true;
                    }
                }
            }
            else GrabTile = null;

            // right-click (not a drag)
            bool right = mouse.RightButton == ButtonState.Pressed;
            if (right && !RightWasDown)
            {
                RightDownAt = mouseIsOn ? new Point(mouse.X, mouse.Y) : (Point?)null;
                RightMovedFar = false;
            }
            else if (right && RightDownAt != null)
            {
                if (Math.Abs(mouse.X - RightDownAt.Value.X) > ClickSlop || Math.Abs(mouse.Y - RightDownAt.Value.Y) > ClickSlop) RightMovedFar = true;
            }
            else if (!right && RightWasDown)
            {
                if (RightDownAt != null && !RightMovedFar) OnRightClick?.Invoke(RightDownAt.Value);
                RightDownAt = null;
            }
            RightWasDown = right;

            // ---------------- middle mouse: rotate ----------------
            // (3D mode: the 3D camera already orbits and tilts while the middle button is held)
            bool middle = mouse.MiddleButton == ButtonState.Pressed;
            if (!Is3D && Game != null)
            {
                if (middle && !MiddleDown && mouseIsOn) MiddleStartX = mouse.X;
                if (middle && MiddleDown)
                {
                    var dx = mouse.X - MiddleStartX;
                    if (Math.Abs(dx) >= TurnDistance)
                    {
                        Game.Rotation = (Game.Rotation + (dx > 0 ? 1 : 3)) % 4;
                        MiddleStartX = mouse.X;
                        moved = true;
                    }
                }
            }
            MiddleDown = middle;

            // ---------------- optional screen-edge scrolling ----------------
            if (!moved && GrabTile == null && EdgeScroll && mouseIsOn && !state.TouchMode)
                moved = world.TestScroll(state);

            return moved;
        }
    }
}
