using FSO.Client;
using FSO.Client.UI.Panels.LotControls;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using System;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the dropper. Click something on the lot and the catalog picks the same thing for you:
    /// an object (a chair, a door, a tree...) or the floor under the mouse. Buy and Build mode decide
    /// what to do with it (open its page in the catalog and start placing another one).
    /// </summary>
    public class UIReduxDropper : UICustomLotControl
    {
        private UILotControl Lot;
        private Action<uint> OnObject;
        private Action<ushort> OnFloor;
        private Action OnNothing;

        public UIReduxDropper(UILotControl lot, Action<uint> onObject, Action<ushort> onFloor, Action onNothing)
        {
            Lot = lot;
            OnObject = onObject;
            OnFloor = onFloor;
            OnNothing = onNothing;
        }

        public override void MouseDown(UpdateState state)
        {
            try
            {
                var vm = Lot.vm;
                var world = Lot.World;
                if (vm == null || world == null) return;
                var pt = Lot.ToWorldScreen(new Vector2(state.MouseState.X, state.MouseState.Y));
                var id = world.GetObjectIDAtScreenPos((int)pt.X, (int)pt.Y, GameFacade.GraphicsDevice);
                if (id > 0)
                {
                    var ent = vm.GetObjectById(id);
                    if (ent is VMGameObject)
                    {
                        // a multi-tile object is bought by its main part (like picking it up does)
                        var guid = (ent.MultitileGroup.MultiTile && ent.MasterDefinition != null) ? ent.MasterDefinition.GUID : ent.Object.OBJ.GUID;
                        OnObject?.Invoke(guid);
                        return;
                    }
                }
                var tile = world.EstTileAtPosWithScroll(pt / FSOEnvironment.DPIScaleFactor);
                var arch = vm.Context.Architecture;
                short x = (short)tile.X, y = (short)tile.Y;
                if (x >= 0 && y >= 0 && x < arch.Width && y < arch.Height)
                {
                    var floor = arch.GetFloor(x, y, world.State.Level);
                    if (floor.Pattern != 0 && floor.Pattern < 65534)
                    {
                        OnFloor?.Invoke(floor.Pattern);
                        return;
                    }
                }
                OnNothing?.Invoke();
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] dropper: " + e.Message);
            }
        }

        public override void MouseUp(UpdateState state) { }
        public override void Update(UpdateState state, bool scrolled) { }
        public override void Release() { }
    }
}
