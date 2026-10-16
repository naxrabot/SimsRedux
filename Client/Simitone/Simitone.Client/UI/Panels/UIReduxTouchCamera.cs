using FSO.Common;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using System;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// SimsRedux: the Sims 3 style camera, made for fingers. Only used with touch controls (the
    /// mouse and keyboard camera, UIRevampedCamera, is switched off then).
    ///
    ///   One finger drag ........ grab the ground and drag it (like right-drag on a PC)
    ///   Two fingers held ....... move them together to pan, pinch to zoom, twist to turn
    ///   Tap / hold ............. the Sim or object's menu (UILotControlTouchHelper)
    ///
    /// Grabbing works the same in 2D and 3D: the ground point first touched stays under the
    /// finger(s), so the lot follows them exactly, and zooming happens around the fingers.
    /// </summary>
    public class UIReduxTouchCamera
    {
        private UILotControl Lot;
        private Vector2? GrabTile;
        private Vector2 BaseVector;
        private float StartZoom;
        private float? LastAngle;

        /// <summary>The twist so far (radians, clockwise positive) - in 2D a big enough one turns the lot.</summary>
        public float TwistAngle;

        public UIReduxTouchCamera(UILotControl lot)
        {
            Lot = lot;
        }

        /// <summary>The ground tile under a point on the screen (null if it can't be worked out).</summary>
        private Vector2? TileAt(Vector2 screenPos)
        {
            try
            {
                var world = Lot.World;
                if (world == null) return null;
                var pos = Lot.ToWorldScreen(screenPos) / FSOEnvironment.DPIScaleFactor;
                return world.EstTileAtPosWithScroll(pos);
            }
            catch { return null; }
        }

        /// <summary>Keeps the grabbed ground point under this point on the screen.</summary>
        private void HoldUnder(Vector2 screenPos)
        {
            if (GrabTile == null) { GrabTile = TileAt(screenPos); return; }
            var now = TileAt(screenPos);
            if (now == null) return;
            var delta = GrabTile.Value - now.Value;
            // (a huge jump means the estimate went wrong for a frame - e.g. pointing at the sky)
            if (delta.LengthSquared() > 0.00001f && delta.LengthSquared() < 400)
            {
                Lot.World.State.ScrollAnchor = null;
                Lot.World.State.CenterTile += delta;
            }
        }

        // ---- one finger ----

        public void BeginDrag(Vector2 finger)
        {
            GrabTile = TileAt(finger);
            LastAngle = null;
            TwistAngle = 0;
        }

        public void Drag(Vector2 finger)
        {
            HoldUnder(finger);
        }

        // ---- two fingers ----

        public void BeginTwo(Vector2 a, Vector2 b)
        {
            BaseVector = b - a;
            StartZoom = Lot.TargetZoom;
            GrabTile = TileAt((a + b) / 2);
            LastAngle = null;
            TwistAngle = 0;
        }

        public void MoveTwo(Vector2 a, Vector2 b, bool is3D)
        {
            var vector = b - a;

            // pinch: zoom by how far apart the fingers are now
            if (BaseVector.Length() > 1)
            {
                var zoom = (vector.Length() / BaseVector.Length()) * StartZoom;
                if (!float.IsNaN(zoom) && !float.IsInfinity(zoom))
                    Lot.TargetZoom = is3D ? zoom : Math.Max(0.25f, Math.Min(2f, zoom));
            }

            // pan: the ground between the fingers stays between them
            HoldUnder((a + b) / 2);

            // twist
            var na = BaseVector; var nb = vector;
            if (na.LengthSquared() < 1 || nb.LengthSquared() < 1) return;
            na.Normalize(); nb.Normalize();
            var clockwise = ((-na.Y) * nb.X + na.X * nb.Y) > 0;
            var angle = (float)Math.Acos(MathHelper.Clamp(Vector2.Dot(na, nb), -1f, 1f));
            TwistAngle = clockwise ? angle : -angle;
            if (float.IsNaN(TwistAngle)) TwistAngle = 0;

            if (is3D)
            {
                // 3D: turn smoothly with the fingers
                var rc = Lot.Rotate;
                if (LastAngle != null)
                {
                    float rot = rc.RotationX - (float)DirectionUtils.Difference(TwistAngle, LastAngle.Value);
                    if (!float.IsNaN(rot)) rc.RotationX = rot;
                }
                LastAngle = TwistAngle;
            }
        }

        /// <summary>All fingers up (or back to one finger: a fresh grab under it).</summary>
        public void End()
        {
            GrabTile = null;
            LastAngle = null;
        }
    }
}
