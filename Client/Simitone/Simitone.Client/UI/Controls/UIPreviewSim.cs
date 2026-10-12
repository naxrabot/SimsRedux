using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.LotView;
using FSO.LotView.Utils;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;
using System;

namespace Simitone.Client.UI.Controls
{
    /// <summary>
    /// SimsRedux: draws a single sim in the UI (used by the classic Create-a-Sim preview and
    /// family screen). Ported from FreeSO's UISim, which lives in a project Simitone.Client
    /// doesn't reference. Renders at 140x200 and scales to Size.
    /// </summary>
    public class UIPreviewSim : UIElement
    {
        private _3DTargetScene Scene;
        private WorldCamera Camera;
        public AdultVitaboyModel Avatar { get; private set; }

        public float RotationRange = 45;
        public float RotationStartAngle = 45;
        public float RotationSpeed = new TimeSpan(0, 0, 10).Ticks;
        public bool AutoRotate = true;
        public float TimeOffset;

        private Vector2 _Size = new Vector2(140, 200);
        private Vector2 _SimScale = Vector2.One;

        public override Vector2 Size
        {
            get { return _Size; }
            set
            {
                _Size = value;
                _SimScale = new Vector2(1, 1) * (value.Y / 200f);
            }
        }

        public UIPreviewSim()
        {
            FSO.Vitaboy.Avatar.DefaultTechnique = (GlobalSettings.Default.Lighting) ? 3 : 0;
            Camera = new WorldCamera(GameFacade.GraphicsDevice);
            Camera.Zoom = WorldZoom.Near;
            Camera.CenterTile = new Vector3(-1, -1, 0) * FSOEnvironment.DPIScaleFactor;
            Scene = new _3DTargetScene(GameFacade.GraphicsDevice, Camera,
                new Point((int)(140 * FSOEnvironment.DPIScaleFactor), (int)(200 * FSOEnvironment.DPIScaleFactor)),
                (GlobalSettings.Default.AntiAlias > 0) ? 8 : 0);
            Scene.ID = "UIPreviewSim";

            GameFacade.Game.GraphicsDevice.DeviceReset += GraphicsDevice_DeviceReset;

            Avatar = new AdultVitaboyModel();
            Avatar.Scene = Scene;
            var scale = FSOEnvironment.DPIScaleFactor;
            Avatar.Scale = new Vector3(scale, scale, scale);
            Scene.Add(Avatar);

            GameFacade.Scenes.AddExternal(Scene);
        }

        private void GraphicsDevice_DeviceReset(object sender, EventArgs e)
        {
            Scene.DeviceReset(GameFacade.GraphicsDevice);
        }

        public override void GameResized()
        {
            base.GameResized();
            Camera.CenterTile = new Vector3(-1, -1, 0) * FSOEnvironment.DPIScaleFactor;
            Scene.SetSize(new Point((int)(140 * FSOEnvironment.DPIScaleFactor), (int)(200 * FSOEnvironment.DPIScaleFactor)));
            var scale = FSOEnvironment.DPIScaleFactor;
            Avatar.Scale = new Vector3(scale, scale, scale);
            Camera.ProjectionDirty();
        }

        public override void Removed()
        {
            GameFacade.Game.GraphicsDevice.DeviceReset -= GraphicsDevice_DeviceReset;
            GameFacade.Scenes.RemoveExternal(Scene);
            Scene.Dispose();
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (AutoRotate)
            {
                var time = state.Time.TotalGameTime.Ticks + TimeOffset;
                var phase = (time % RotationSpeed) / RotationSpeed;
                var multiplier = Math.Sin((Math.PI * 2) * phase);
                var newAngle = RotationStartAngle + (RotationRange * multiplier);
                Avatar.RotationY = MathHelper.ToRadians((float)newAngle);
            }
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            if (!Visible) return;
            base.PreDraw(batch);
            if (!UISpriteBatch.Invalidated && !_3DScene.IsInvalidated)
            {
                batch.Pause();
                Scene.Draw(GameFacade.GraphicsDevice);
                batch.Resume();
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            DrawLocalTexture(batch, Scene.Target, null,
                new Vector2((_Size.X - 140 * _SimScale.X) / 2, 0),
                new Vector2(1f / FSOEnvironment.DPIScaleFactor, 1f / FSOEnvironment.DPIScaleFactor) * _SimScale);
        }
    }
}
