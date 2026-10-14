
using FSO.Client;
using FSO.Client.Debug;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common;
using FSO.Common.Rendering.Framework;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using FSO.Files.RC;
using FSO.HIT;
using FSO.LotView;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine.TSOTransaction;
using FSO.SimAntics.Marshals;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay;
using FSO.SimAntics.NetPlay.Drivers;
using FSO.SimAntics.NetPlay.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.SimAntics.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.WorldUI;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FSO.Common.IO;
namespace Simitone.Client.UI.Screens
{
    public class TS1GameScreen : FSO.Client.UI.Framework.GameScreen
    {
        public UIContainer WindowContainer;
        public bool Downtown;
        public bool Desktop = !FSOEnvironment.SoftwareKeyboard;

        public UILotControl LotControl { get; set; }
        public UISimitoneFrontend Frontend { get; set; }
        private FSO.LotView.World World;
        public FSO.SimAntics.VM vm { get; set; }
        public VMNetDriver Driver;
        public UISimitoneBg Bg;
        public uint VisualBudget { get; set; }

        //for TS1 hybrid mode
        public UINeighborhoodSelectionPanel TS1NeighPanel;
        public FAMI ActiveFamily;

        public bool InLot
        {
            get
            {
                return (vm != null);
            }
        }

        private int m_ZoomLevel;
        public int ZoomLevel
        {
            get
            {
                return m_ZoomLevel;
            }
            set
            {
                value = Math.Max(1, Math.Min(3, value));

                if (value < 4)
                {
                    if (vm == null)
                    {

                    }
                    else
                    {
                        var targ = (WorldZoom)(4 - value); //near is 3 for some reason... will probably revise
                        //HITVM.Get().PlaySoundEvent(UIMusic.None);
                        LotControl.Visible = true;
                        Bg.Visible = false;
                        World.Visible = true;
                        //ucp.SetMode(UIUCP.UCPMode.LotMode);
                        LotControl.SetTargetZoom(targ);
                        if (m_ZoomLevel != value) vm.Context.World.InitiateSmoothZoom(targ);
                        vm.Context.World.State.Zoom = targ;
                        m_ZoomLevel = value;
                    }
                }
                else //cityrenderer! we'll need to recreate this if it doesn't exist...
                {
                    if (m_ZoomLevel < 4)
                    { //coming from lot view... snap zoom % to 0 or 1
                        if (World != null)
                        {
                            LotControl.Visible = false;
                        }
                    }
                    m_ZoomLevel = value;
                }
            }
        }

        private int _Rotation = 0;
        public int Rotation
        {
            get
            {
                return _Rotation;
            }
            set
            {
                _Rotation = value;
                World.State.CenterTile = World.EstTileAtPosWithScroll(new Vector2(ScreenWidth / 2, ScreenHeight / 2));
                if (World != null)
                {
                    switch (_Rotation)
                    {
                        case 0:
                            World.State.Rotation = WorldRotation.TopLeft; break;
                        case 1:
                            World.State.Rotation = WorldRotation.TopRight; break;
                        case 2:
                            World.State.Rotation = WorldRotation.BottomRight; break;
                        case 3:
                            World.State.Rotation = WorldRotation.BottomLeft; break;
                    }
                }
                World.RestoreTerrainToCenterTile();
            }
        }

        public sbyte Level
        {
            get
            {
                if (World == null) return 1;
                else return World.State.Level;
            }
            set
            {
                if (World != null)
                {
                    World.State.Level = value;
                }
            }
        }

        public sbyte Stories
        {
            get
            {
                if (World == null) return 2;
                return World.Stories;
            }
        }

        public VMAvatar SelectedAvatar
        {
            get
            {
                return vm.GetAvatarByPersist(vm.MyUID);
            }
        }

        public TS1GameScreen(NeighSelectionMode mode) : base()
        {
            Bg = new UISimitoneBg();
            Bg.Position = (new Vector2(ScreenWidth, ScreenHeight)) / 2;
            Add(Bg);

            WindowContainer = new UIContainer();
            Add(WindowContainer);

            if (Content.Get().TS1)
            {
                NeighSelection(mode);
            }
            if (ReduxWorker.Active) GameThread.NextUpdate((s) => ReduxWorker.Begin(this));
        }
        public int? MoveInFamily;

        // ---- SimsRedux online play ----
        /// <summary>Hosting this lot for friends (null when not hosting).</summary>
        public ReduxHost Host;
        /// <summary>Visiting a friend's lot (null when not visiting).</summary>
        public ReduxGuest Guest;
        public bool IsGuest { get { return Guest != null; } }
        /// <summary>Start hosting as soon as the next lot finishes loading.</summary>
        public bool HostAfterLoad;
        private UIClassicOnlineStatus OnlineStatus;
        private UIClassicLoadScreen JoinScreen;
        private int OnlineStateLog;

        public void StartMoveIn(int familyID)
        {
            MoveInFamily = familyID;
        }

        public void NeighSelection(NeighSelectionMode mode)
        {
            Content.Get().Neighborhood.PreparePersonDataFromObject = PersonGeneratorHelper.PreparePersonDataFromObject;
            Content.Get().Neighborhood.AddMissingNeighbors();
            var nbd = (ushort)((mode == NeighSelectionMode.MoveInMagic) ? 7 : 4);
            TS1NeighPanel = new UINeighborhoodSelectionPanel(nbd);
            // SimsRedux: the classic top bar replaces the side buttons
            var classicBar = UIClassicNeighborhoodBar.Enabled;
            if (classicBar)
            {
                TS1NeighPanel.MoveInMode = mode != NeighSelectionMode.Normal;
                TS1NeighPanel.PopulateScreen(nbd); // rebuild so the bar knows about move-in mode
            }
            UIElement switcher = classicBar ? null : new UINeighbourhoodSwitcher(TS1NeighPanel, nbd, mode != NeighSelectionMode.Normal);
            TS1NeighPanel.OnHouseSelect += (house) =>
            {
                if (MoveInFamily != null)
                {
                    //move them in first
                    //confirm it
                    if (UIClassicNeighborhoodBar.Enabled)
                    {
                        UIClassicDialog.YesNo(GameFacade.Strings.GetString("132", "0"), GameFacade.Strings.GetString("132", "1"),
                            () => MoveInAndPlay((short)house, MoveInFamily.Value, switcher));
                        return;
                    }
                    UIMobileAlert confirmDialog = null;
                    confirmDialog = new UIMobileAlert(new UIAlertOptions()
                    {
                        Title = GameFacade.Strings.GetString("132", "0"),
                        Message = GameFacade.Strings.GetString("132", "1"),
                        Buttons = UIAlertButton.YesNo((b) =>
                        {
                            confirmDialog.Close();
                            MoveInAndPlay((short)house, MoveInFamily.Value, switcher);
                        },
                        (b) => confirmDialog.Close())
                    });
                    UIScreen.GlobalShowDialog(confirmDialog, true);
                }
                else
                {
                    PlayHouse((short)house, switcher);
                }
            };
            Add(TS1NeighPanel);
            if (switcher != null) Add(switcher);
        }

        public void PlayHouse(short house, UIElement switcher)
        {
            if (UIClassicNeighborhoodBar.Enabled && !ClassicLoading)
            {
                // SimsRedux: original loading screen first
                ClassicLoading = true;
                UIClassicLoadScreen.Run(house, false, () => { PlayHouse(house, switcher); ClassicLoading = false; });
                return;
            }
            ActiveFamily = Content.Get().Neighborhood.GetFamilyForHouse((short)house);
            // SimsRedux: starting from the neighbourhood is never a trip. Forget the last trip
            // (Downtown etc.) so its travelling Sim can't turn up on this family's lot.
            var travel = Content.Get().Neighborhood.GameState;
            travel.ActiveFamily = ActiveFamily;
            travel.LotTransitInfo = 0;
            travel.DowntownSimGUID = (ActiveFamily?.FamilyGUIDs != null && ActiveFamily.FamilyGUIDs.Length > 0) ? ActiveFamily.FamilyGUIDs[0] : 0;
            InitializeLot(Content.Get().Neighborhood.GetHousePath(house), false);// "UserData/Houses/House21.iff"
            Remove(TS1NeighPanel);
            if (switcher != null) Remove(switcher);
            if (HostAfterLoad)
            {
                HostAfterLoad = false;
                GameThread.NextUpdate((s) => StartHosting());
            }
        }

        public void MoveInAndPlay(short house, int family, UIElement switcher)
        {
            MoveInFamily = null;
            var neigh = Content.Get().Neighborhood;
            var fami = neigh.GetFamily((ushort)family);
            neigh.SetFamilyForHouse(house, fami, true);
            PlayHouse(house, switcher);
        }

        public void EvictLot(FAMI family, short houseID)
        {
            family.Budget += family.ValueInArch;
            family.ValueInArch = 0;
            Content.Get().Neighborhood.MoveOut(houseID);
            TS1NeighPanel.RefreshHouses(); // SimsRedux: plumbbob and hover popup follow the family out
            TS1NeighPanel.SelectHouse(houseID);
        }

        // ================= SimsRedux online play =================

        /// <summary>Start letting friends join the lot that's open now.</summary>
        public void StartHosting()
        {
            if (vm == null || IsGuest || Host != null) return;
            var server = Driver as VMServerDriver;
            if (server == null) return;
            var host = new ReduxHost(vm, server);
            string error;
            // the dedicated server (when it's running) owns the usual port and passes players to us
            var port = ReduxWorker.Active ? ReduxWorker.Port : ReduxOnlineInfo.HostPort();
            if (!host.Start(port, out error))
            {
                UIClassicDialog.Ok("Couldn't Host", "The game couldn't open port " + port + " for friends to join.\n\n" + error
                    + "\n\nIs another copy of the game already hosting?");
                return;
            }
            Host = host;
            host.OnNotice += (text) => OnlineStatus?.Notice(text);
            ShowOnlineStatus();
            if (ReduxWorker.Active) return; // nobody to read it
            UIClassicDialog.Ok("Hosting Online", "Friends can now join this lot.\n\nOn this PC (for testing): localhost\nOn your home network: this PC's IP address\nOver the internet: " + ReduxOnlineInfo.DefaultAddress
                + " (once the Cloudflare tunnel is running)\n\nThe lot stops being online when you go back to the neighborhood.");
        }

        private void ShowOnlineStatus()
        {
            if (OnlineStatus != null) Remove(OnlineStatus);
            OnlineStatus = new UIClassicOnlineStatus(this);
            Add(OnlineStatus);
        }

        /// <summary>
        /// Visit a friend's lot with one of your Sims. The lot itself arrives over the network:
        /// until it has, a loading box shows.
        /// </summary>
        public void JoinOnline(string address, string playerName, uint simGUID)
        {
            if (vm != null) return;
            string error;
            var join = ReduxGuest.MakeJoinPacket(playerName, simGUID, out error);
            if (join == null)
            {
                UIClassicDialog.Ok("Couldn't Join", error);
                return;
            }

            CleanupLastWorld();
            World = new FSO.LotView.World(GameFacade.GraphicsDevice);
            World.Opacity = 1;
            World.Visible = false;
            GameFacade.Scenes.Add(World);

            var driver = new VMClientDriver(GuestStateChange);
            Driver = driver;
            vm = new VM(new VMContext(World), Driver, new UIHeadlineRendererProvider());
            vm.ListenBHAVChanges();
            vm.Init();
            vm.OnFullRefresh += VMRefreshed;
            vm.OnRequestLotSwitch += VMLotSwitch;
            vm.OnGenericVMEvent += Vm_OnGenericVMEvent;
            ActiveFamily = null;
            Downtown = false;

            if (TS1NeighPanel != null) Remove(TS1NeighPanel);
            JoinScreen = UIClassicLoadScreen.ShowWaiting("Visiting a Friend", "Connecting...");

            Guest = new ReduxGuest(vm, driver, address, join);
            Guest.Connect();
        }

        private void GuestStateChange(int state, float progress)
        {
            // 1 = connected, 2 = loading the lot (progress), 3 = ready
            if (state == 2) JoinScreen?.SetWaiting("Loading the lot...", progress);
            if (state == 3) GameThread.NextUpdate((s) => GuestReady());
        }

        private void GuestReady()
        {
            if (vm == null || Guest == null || LotControl != null) return;
            if (JoinScreen != null) { JoinScreen.Close(); JoinScreen = null; }
            LotControl = new UILotControl(vm, World);
            this.AddAt(0, LotControl);
            m_ZoomLevel = 4;
            ZoomLevel = 1;
            GameFacade.Cursor.SetCursor(CursorType.Normal);
            Frontend = new UISimitoneFrontend(this);
            this.Add(Frontend);
            ShowOnlineStatus();
            ReduxOnlineInfo.Log("On the host's lot");
        }

        private void GuestLost()
        {
            var reason = Guest?.Failure ?? "The connection to the host was lost.";
            ReduxOnlineInfo.Log("Left the host's lot: " + reason);
            ExitLot();
            UIClassicDialog.Ok("Back Home", reason);
        }

        /// <summary>SimsRedux: open the lot in Build mode first (New House).</summary>
        public bool StartInBuild;

        /// <summary>
        /// SimsRedux: New House. Opens an empty lot with nobody living there, straight into
        /// Build mode, so a house can be built and then sold or moved into later.
        /// </summary>
        public void BuildNewHouse(short house)
        {
            StartInBuild = true;
            PlayHouse(house, null);
        }

        /// <summary>
        /// SimsRedux: Bulldoze. Loads the lot behind the loading box, removes everything that
        /// can be sold (the mailbox, rubbish bin and other fixed objects stay), all walls and
        /// floors, saves it and goes back to the neighbourhood.
        /// </summary>
        public void BulldozeLot(short house)
        {
            if (vm != null) return; // only from the neighbourhood
            int removed = 0;
            UIClassicLoadScreen.RunSteps("Bulldozing " + UIClassicLoadScreen.LotName(house), true,
                () =>
                {
                    // something went wrong: back to the neighbourhood without saving
                    if (vm != null) { Remove(TS1NeighPanel); ExitLot(); }
                    UIClassicDialog.Ok("Bulldoze", "Sorry, this lot couldn't be bulldozed. Nothing was changed.");
                },
                new UIClassicLoadScreen.Step("Loading the lot...", () =>
                {
                    ActiveFamily = null;
                    Downtown = false;
                    InitializeLot(Content.Get().Neighborhood.GetHousePath(house), false);
                    if (vm == null) throw new Exception("The lot didn't load.");
                    Remove(TS1NeighPanel);
                }, 6),
                new UIClassicLoadScreen.Step("Bringing in the bulldozer...", () => { removed = ClearLot(); }, 12),
                new UIClassicLoadScreen.Step("Clearing away the rubble...", null, 10),
                new UIClassicLoadScreen.Step("Saving the neighborhood...", () =>
                {
                    Save();
                    Console.WriteLine("[SimsRedux] Bulldozed lot " + house + " (" + removed + " objects removed).");
                    ExitLot();
                }, 4));
        }

        /// <summary>SimsRedux: removes every object players could sell, then every wall and floor. Returns how many objects went.</summary>
        private int ClearLot()
        {
            int removed = 0;
            var ctx = vm.Context;
            foreach (var ent in vm.Entities.ToList())
            {
                if (ent.Dead || ent is VMAvatar) continue;
                var flags = (VMMovementFlags)ent.GetValue(VMStackObjectVariable.MovementFlags);
                if ((flags & VMMovementFlags.PlayersCanMove) == 0 || (flags & VMMovementFlags.PlayersCanDelete) == 0) continue;
                try
                {
                    ent.ExecuteEntryPoint(12, ctx, true); // the object's own clean-up
                    if (!ent.Dead) ent.Delete(true, ctx);
                    removed++;
                }
                catch (Exception e)
                {
                    Console.WriteLine("[SimsRedux] Couldn't remove " + ent.ToString() + ": " + e.Message);
                }
            }

            var arch = ctx.Architecture;
            for (int i = 0; i < arch.Stories; i++)
            {
                Array.Clear(arch.Walls[i], 0, arch.Walls[i].Length);
                Array.Clear(arch.Floors[i], 0, arch.Floors[i].Length);
            }
            arch.RegenWallsAt();
            arch.SignalAllDirty();
            arch.Tick(); // rebuild rooms, roof and the picture now (the lot is paused)
            return removed;
        }

        public override void GameResized()
        {
            base.GameResized();
            Bg.Position = (new Vector2(ScreenWidth, ScreenHeight)) / 2;
            World?.GameResized();
        }

        public void Initialize(string propertyName, bool external)
        {
            GameFacade.CurrentCityName = propertyName;
            ZoomLevel = 1; //screen always starts at near zoom
            InitializeLot(propertyName, external);
        }

        private int SwitchLot = -1;
        private bool ClassicLoading;

        public void ChangeSpeedTo(int speed)
        {
            if (IsGuest) return; // SimsRedux: the host controls the speed on their lot
            //0 speed is 0x
            //1 speed is 1x
            //2 speed is 3x
            //3 speed is 10x

            if (vm == null) return;
            if (vm.SpeedMultiplier == -1) return;

            switch (vm.SpeedMultiplier)
            {
                case 0:
                    switch (speed)
                    {
                        case 1:
                            HITVM.Get().PlaySoundEvent(UISounds.SpeedPTo1); break;
                        case 2:
                            HITVM.Get().PlaySoundEvent(UISounds.SpeedPTo2); break;
                        case 3:
                            HITVM.Get().PlaySoundEvent(UISounds.SpeedPTo3); break;
                    }
                    break;
                case 1:
                    switch (speed)
                    {
                        case 0:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed1ToP); break;
                        case 2:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed1To2); break;
                        case 3:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed1To3); break;
                    }
                    break;
                case 3:
                    switch (speed)
                    {
                        case 0:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed2ToP); break;
                        case 1:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed2To1); break;
                        case 3:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed2To3); break;
                    }
                    break;
                case 10:
                    switch (speed)
                    {
                        case 0:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed3ToP); break;
                        case 1:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed3To1); break;
                        case 2:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed3To2); break;
                    }
                    break;
            }

            int target = 1;
            switch (speed)
            {
                case 0: target = 0; break;
                case 1: target = 1; break;
                case 2: target = 3; break;
                case 3: target = 10; break;
            }
            // SimsRedux online: while hosting, the speed goes through the lot so friends' games match
            if (Host != null) vm.SendCommand(new VMNetSetSpeedCmd() { Speed = target });
            else vm.SpeedMultiplier = target;
            vm.ResetTickAlign();
        }

        public override void Update(FSO.Common.Rendering.Framework.Model.UpdateState state)
        {
            GameFacade.Game.IsFixedTimeStep = (vm == null || vm.Ready);
            
            Visible = World?.Visible != false && World?.State.Cameras.HideUI != true;
            GameFacade.Game.IsMouseVisible = Visible;

            if (state.NewKeys.Contains(Keys.D1)) ChangeSpeedTo(1);
            if (state.NewKeys.Contains(Keys.D2)) ChangeSpeedTo(2);
            if (state.NewKeys.Contains(Keys.D3)) ChangeSpeedTo(3);
            if (state.NewKeys.Contains(Keys.P)) ChangeSpeedTo(0);
            if (state.NewKeys.Contains(Keys.D0))
            {
                //frame advance
                ChangeSpeedTo(1);
                GameThread.NextUpdate((FSO.Common.Rendering.Framework.Model.UpdateState ustate) => ChangeSpeedTo(0));
            }
            base.Update(state);

            // SimsRedux: F12 used to switch between 3D and 2D. The game is 2D only now.
            if (GraphicsModeControl.Mode != GlobalGraphicsMode.Full2D) GraphicsModeControl.ChangeMode(GlobalGraphicsMode.Full2D);

            /*
            if (state.NewKeys.Contains(Keys.F12))
            {
                ChangeSpeedTo(1);
                //running 10000 ticks
                var timer = new System.Diagnostics.Stopwatch();
                timer.Start();

                for (int i=0; i<10000; i++)
                {
                    vm.Tick();
                }

                timer.Stop();
                UIScreen.GlobalShowDialog(new UIMobileAlert(new UIAlertOptions() {
                    Title = "Benchmark",
                    Message = "10000 ticks took " + timer.ElapsedMilliseconds + "ms."
                }), true);
            }
            */

            if (World != null)
            {
                //stub smooth zoom?
            }

            if (SwitchLot > 0)
            {
                var target = SwitchLot;
                SwitchLot = -1;
                if (UIClassicNeighborhoodBar.Enabled)
                {
                    // SimsRedux: show the taxi / boat / limo / balloon picture, then travel
                    var goingHome = ActiveFamily != null && target == ActiveFamily.HouseNumber;
                    UIClassicLoadScreen.Run(target, goingHome, () => DoSwitchLot(target));
                }
                else DoSwitchLot(target);
            }
            //vm.Context.Clock.Hours = 12;
            // SimsRedux online: messages from friends / the host go in before the lot ticks
            Host?.Process();
            ReduxWorker.Tick();
            if (Guest != null && !Guest.Process()) GuestLost();
            if ((Host != null || IsGuest) && vm != null && ++OnlineStateLog >= 300)
            {
                // SimsRedux online: a line every ~5 seconds about the lot, for tracking down problems
                OnlineStateLog = 0;
                var me = vm.GetAvatarByPersist(vm.MyUID);
                ReduxOnlineInfo.Log((Host != null ? "[host]" : "[guest]") + " state: speed=" + vm.SpeedMultiplier + " last=" + vm.LastSpeedMultiplier
                    + " ready=" + vm.Ready + " questionFrom=" + (vm.GlobalBlockingDialog == null ? "none" : vm.GlobalBlockingDialog.ObjectID + "/" + vm.GlobalBlockingDialog.PersistID)
                    + " me=" + vm.MyUID + " mySim=" + (me == null ? "MISSING" : me.ObjectID + " " + me.Name + " pid=" + me.PersistID)
                    + " selected=" + (LotControl?.ActiveEntity == null ? "none" : LotControl.ActiveEntity.ObjectID.ToString())
                    + " mode=" + (LotControl == null ? "-" : (LotControl.LiveMode ? "live" : "buy/build"))
                    + (Host != null ? " friends=" + Host.GuestCount : "")
                    + " lotUpdates=" + (LotControl?.DebugUpdates ?? -1) + "/" + (LotControl?.DebugFullUpdates ?? -1)
                    + " refreshes=" + RefreshCount
                    + " lotVisible=" + (LotControl?.Visible.ToString() ?? "-")
                    + " onTop=" + string.Join(",", GetChildren().Skip(Math.Max(0, GetChildren().Count - 4)).Select(x => x.GetType().Name)));
            }
            if (vm != null) vm.Update();

            //SaveHouseButton_OnButtonClick(null);
        }

        private void DoSwitchLot(int target)
        {
            if (vm == null) return;
            if (!Downtown) SavedLot = vm.Save();
            if (target == ActiveFamily.HouseNumber && SavedLot != null)
            {
                Downtown = false;
                InitializeLot(SavedLot);
                SavedLot = null;
            }
            else
            {
                Downtown = true;
                InitializeLot(Content.Get().Neighborhood.GetHousePath(target), false);
            }
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            base.PreDraw(batch);
            vm?.PreDraw();
        }

        public void CleanupLastWorld()
        {
            if (vm == null) return;
            // SimsRedux: a lot that never finished loading (a failed online join) can be missing
            // any part of this; leaving it must never stop the game
            try { CleanupLastWorldParts(); }
            catch (Exception e) { ReduxOnlineInfo.Log("leaving the lot: " + e.Message); }
            try { if (LotControl != null) this.Remove(LotControl); } catch { }
            try { if (Frontend != null) this.Remove(Frontend); } catch { }
            Frontend = null;
            vm = null;
            World = null;
            Driver = null;
            LotControl = null;
        }

        private void CleanupLastWorldParts()
        {

            //clear our cache too, if the setting lets us do that
            TimedReferenceController.Clear();
            TimedReferenceController.Clear();

            if (ZoomLevel < 4) ZoomLevel = 5;
            // SimsRedux: a lot that never finished loading (e.g. joining a friend failed) has no
            // sounds or world yet, so each part is only cleaned up if it exists
            vm.Context?.Ambience?.Kill();
            foreach (var ent in vm.Entities ?? new List<VMEntity>())
            { //stop object sounds
                var threads = ent.SoundThreads;
                if (threads == null) continue;
                for (int i = 0; i < threads.Count; i++)
                {
                    threads[i].Sound?.RemoveOwner(ent.ObjectID);
                }
                threads.Clear();
            }
            try { vm.CloseNet(VMCloseNetReason.LeaveLot); }
            catch (Exception e) { ReduxOnlineInfo.Log("closing the lot's connection: " + e.Message); }
            if (World != null)
            {
                GameFacade.Scenes.Remove(World);
                World.Dispose();
            }
            //LotControl.Dispose();
            if (LotControl != null) this.Remove(LotControl);
            if (Frontend != null) this.Remove(Frontend);
            Frontend = null;
            vm.SuppressBHAVChanges();
        }

        private VMMarshal SavedLot;

        public void InitializeLot()
        {
            CleanupLastWorld();
            World = new FSO.LotView.World(GameFacade.GraphicsDevice);

            World.Opacity = 1;
            GameFacade.Scenes.Add(World);

            var globalLink = new VMTS1GlobalLinkStub();
            Driver = new VMServerDriver(globalLink);

            vm = new VM(new VMContext(World), Driver, new UIHeadlineRendererProvider());
            vm.ListenBHAVChanges();
            vm.Init();

            LotControl = new UILotControl(vm, World);
            this.AddAt(0, LotControl);

            if (m_ZoomLevel > 3)
            {
                World.Visible = false;
                LotControl.Visible = false;
            }

            ZoomLevel = Math.Max(ZoomLevel, 4);

            if (IDEHook.IDE != null) IDEHook.IDE.StartIDE(vm);

            vm.OnFullRefresh += VMRefreshed;
            //vm.OnEODMessage += LotControl.EODs.OnEODMessage;
            vm.OnRequestLotSwitch += VMLotSwitch;
            vm.OnGenericVMEvent += Vm_OnGenericVMEvent;
        }

        public void InitializeLot(VMMarshal marshal)
        {
            InitializeLot();
            vm.MyUID = 65537;
            vm.Load(marshal);

            vm.TS1State.ActivateFamily(vm, ActiveFamily);

            var settings = GlobalSettings.Default;
            var myClient = new VMNetClient
            {
                PersistID = 1,
                RemoteIP = "local",
                AvatarState = new VMNetAvatarPersistState()
                {
                    Name = settings.LastUser ?? "",
                    DefaultSuits = new VMAvatarDefaultSuits(settings.DebugGender),
                    BodyOutfit = settings.DebugBody,
                    HeadOutfit = settings.DebugHead,
                    PersistID = 1,
                    SkinTone = (byte)settings.DebugSkin,
                    Gender = (short)(settings.DebugGender ? 1 : 0),
                    Permissions = FSO.SimAntics.Model.TSOPlatform.VMTSOAvatarPermissions.Admin,
                    Budget = 1000000
                }

            };

            var server = (VMServerDriver)Driver;
            server.ConnectClient(myClient);

            GameFacade.Cursor.SetCursor(CursorType.Normal);
            ZoomLevel = 1;

            Frontend = new UISimitoneFrontend(this);
            this.Add(Frontend);
        }

        public void ShowLoadErrors(List<VMLoadError> errors, bool verbose)
        {
            var errorMsg = GameFacade.Strings.GetString("153", "16");

            if (verbose)
            {
                errorMsg += "\n";
                foreach (var error in errors)
                {
                    errorMsg += "\n" + error.ToString();
                }
            }

            //signal thru the VM so we can stop time appropriately
            vm.LastSpeedMultiplier = vm.SpeedMultiplier;
            vm.SpeedMultiplier = 0;
            vm.SignalDialog(new VMDialogInfo
            {
                Block = true,
                Caller = null,
                Yes = "OK",
                DialogID = 0,
                Title = GameFacade.Strings.GetString("153", "17"),
                Message = errorMsg,
            });

            /*
            CloseAlert = new UIMobileAlert(new FSO.Client.UI.Controls.UIAlertOptions
            {
                Title = GameFacade.Strings.GetString("153", "17"), //missing objects!
                Message = errorMsg,
                Buttons = UIAlertButton.Ok(
                        (b) => { CloseAlert.Close(); CloseAlert = null; }
                        )
            });
            */
        }

        public void InitializeLot(string lotName, bool external)
        {
            if (lotName == "" || lotName[0] == '!') return;
            InitializeLot();
            
            if (!external)
            {
                if (!Downtown && ActiveFamily != null)
                {
                    ActiveFamily.SelectWholeFamily();
                    vm.TS1State.ActivateFamily(vm, ActiveFamily);
                }
                BlueprintReset(lotName, null);
                
                if (vm.LoadErrors.Count > 0) GameThread.NextUpdate((state) => ShowLoadErrors(vm.LoadErrors, true));

                vm.MyUID = 65537;
                var settings = GlobalSettings.Default;
                var myClient = new VMNetClient
                {
                    PersistID = 1,
                    RemoteIP = "local",
                    AvatarState = new VMNetAvatarPersistState()
                    {
                        Name = settings.LastUser ?? "",
                        DefaultSuits = new VMAvatarDefaultSuits(settings.DebugGender),
                        BodyOutfit = settings.DebugBody,
                        HeadOutfit = settings.DebugHead,
                        PersistID = 1,
                        SkinTone = (byte)settings.DebugSkin,
                        Gender = (short)(settings.DebugGender ? 1 : 0),
                        Permissions = FSO.SimAntics.Model.TSOPlatform.VMTSOAvatarPermissions.Admin,
                        Budget = 1000000
                    }
                };

                if (Downtown)
                {
                    var ngbh = Content.Get().Neighborhood;
                    var crossData = ngbh.GameState;
                    var neigh = ngbh.GetNeighborIDForGUID(crossData.DowntownSimGUID);
                    if (neigh != null) {
                        var inv = ngbh.GetInventoryByNID(neigh.Value);
                        if (inv != null) {
                            var hr = inv.FirstOrDefault(x => x.Type == 2 && x.GUID == 7)?.Count ?? 0;
                            var min = inv.FirstOrDefault(x => x.Type == 2 && x.GUID == 8)?.Count ?? 0;
                            Driver.SendCommand(new VMNetSetTimeCmd()
                            {
                                Hours = hr,
                                Minutes = min,
                            });
                        }
                    }
                }

                var server = (VMServerDriver)Driver;
                server.ConnectClient(myClient);
                if (!Downtown && ActiveFamily != null) RemoveStrayFamilySims();
                LoadSurrounding(short.Parse(lotName.Substring(lotName.Length - 6, 2)));
                LotDebug("after load (" + Path.GetFileName(lotName) + ")");
                GameThread.SetTimeout(() => LotDebug("3 seconds later"), 3000);

                GameFacade.Cursor.SetCursor(CursorType.Normal);
                ZoomLevel = 1;
            }

            Frontend = new UISimitoneFrontend(this);
            this.Add(Frontend);
        }

        public void LoadSurrounding(short houseID)
        {
            return;
            var surrounding = new NBHm(new OBJ(FSOFile.OpenRead(@"C:\Users\Rhys\Desktop\fso 2018\nb4.obj")));
            NBHmHouse myH = null;
            var myHeight = vm.Context.Blueprint.InterpAltitude(new Vector3(0, 0, 0));
            if (!surrounding.Houses.TryGetValue(houseID, out myH)) return;
            foreach (var house in surrounding.Houses)
            {
                if (house.Key == houseID) continue;
                var h = house.Value;
                //let's make their lot as a surrounding lot
                var gd = World.State.Device;
                var subworld = World.MakeSubWorld(gd);
                subworld.Initialize(gd);
                var tempVM = new VM(new VMContext(subworld), new VMServerDriver(new VMTSOGlobalLinkStub()), new VMNullHeadlineProvider());
                tempVM.Init();
                BlueprintReset(Content.Get().Neighborhood.GetHousePath(house.Key), tempVM);
                subworld.State.Level = 5;
                var subHeight = tempVM.Context.Blueprint.InterpAltitude(new Vector3(0, 0, 0));
                tempVM.Context.Blueprint.BaseAlt = (int)Math.Round(((subHeight - myHeight) + myH.Position.Y - h.Position.Y) / tempVM.Context.Blueprint.TerrainFactor);
                subworld.UseFade = false;
                subworld.GlobalPosition = new Vector2((myH.Position.X - h.Position.X), (myH.Position.Z - h.Position.Z));

                foreach (var obj in tempVM.Entities)
                {
                    obj.Position = obj.Position;
                }

                vm.Context.Blueprint.SubWorlds.Add(subworld);
            }
            vm.Context.World.InitSubWorlds();
        }

        public void BlueprintReset(string path, VM vm)
        {
            string filename = Path.GetFileName(path);
            bool isSurrounding = true;
            if (vm == null)
            {
                isSurrounding = false;
                vm = this.vm;
            }
            try
            {
                using (var file = new BinaryReader(FSOFile.OpenRead(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/") + filename.Substring(0, filename.Length - 4) + ".fsov")))
                {
                    var marshal = new FSO.SimAntics.Marshals.VMMarshal();
                    marshal.Deserialize(file);
                    //vm.SendCommand(new VMStateSyncCmd()
                    //{
                    //    State = marshal
                    //});

                    vm.Load(marshal);
                    vm.Reset();
                }
            }
            catch (Exception)
            {
                var floorClip = Rectangle.Empty;
                var offset = new Point();
                var targetSize = 0;

                var isIff = path.EndsWith(".iff");
                short jobLevel = -1;
                if (isIff) jobLevel = short.Parse(path.Substring(path.Length - 6, 2));
                vm.SendCommand(new VMBlueprintRestoreCmd
                {
                    JobLevel = jobLevel,
                    XMLData = FSOFile.ReadAllBytes(path),
                    IffData = isIff,

                    FloorClipX = floorClip.X,
                    FloorClipY = floorClip.Y,
                    FloorClipWidth = floorClip.Width,
                    FloorClipHeight = floorClip.Height,
                    OffsetX = offset.X,
                    OffsetY = offset.Y,
                    TargetSize = targetSize
                });
            }

            var isSimless = (ActiveFamily == null && !isSurrounding);
            vm.SpeedMultiplier = -1;
            vm.Tick();
            vm.SpeedMultiplier = 1;

            if (isSimless)
            {
                vm.SpeedMultiplier = -1;
            }
            vm.SetGlobalValue(32, (short)(isSimless ? 1 : 0));
        }


        private void Vm_OnGenericVMEvent(VMEventType type, object data)
        {
            switch (type)
            {
                case VMEventType.TS1BuildBuyChange:
                    Frontend?.ModeSwitcher?.UpdateBuildBuy();
                    Frontend?.DesktopUCP?.UpdateBuildBuy();
                    break;
            }
        }

        private void VMLotSwitch(uint lotId)
        {
            if (IsGuest || Host != null) return; // SimsRedux: no travelling while online (for now)
            vm.SpeedMultiplier = 0;
            if ((short)lotId == -1)
            {
                lotId = (uint)ActiveFamily.HouseNumber;
            }
            SwitchLot = (int)lotId;
        }

        /// <summary>
        /// SimsRedux: a family's home lot can't hold a member of another household. An older bug
        /// brought the last travelling Sim onto new families' lots, and some houses were saved
        /// with that Sim still in them. Anyone like that is sent home (removed) when the lot loads.
        /// </summary>
        private void RemoveStrayFamilySims()
        {
            try
            {
                var neigh = Content.Get().Neighborhood;
                var otherFamilies = new HashSet<uint>();
                foreach (var fam in neigh.MainResource.List<FAMI>() ?? new List<FAMI>())
                {
                    if (fam == ActiveFamily || fam.FamilyGUIDs == null) continue;
                    foreach (var g in fam.FamilyGUIDs) otherFamilies.Add(g);
                }
                var mine = new HashSet<uint>(ActiveFamily.FamilyGUIDs ?? new uint[0]);
                foreach (var ava in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().ToList())
                {
                    var guid = ava.Object.OBJ.GUID;
                    if (mine.Contains(guid) || !otherFamilies.Contains(guid) || ava.Dead) continue;
                    ReduxOnlineInfo.Log("Sent " + ava.Name + " home: another family's Sim was saved on this lot");
                    if (ava.PersistID != 0) vm.Context.ObjectQueries.RemoveAvatarPersist(ava.PersistID);
                    ava.Delete(true, vm.Context);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Couldn't check for stray Sims: " + e.Message);
            }
        }

        /// <summary>
        /// SimsRedux: writes every Sim on the lot to lot_debug.log (who they are, which family,
        /// who controls them), to track down Sims appearing on lots they don't belong on.
        /// </summary>
        private void LotDebug(string when)
        {
            try
            {
                if (vm == null) return;
                var neigh = Content.Get().Neighborhood;
                var fam = vm.TS1State?.CurrentFamily;
                var lines = new List<string>();
                lines.Add(DateTime.Now.ToString("HH:mm:ss") + " ---- " + when);
                lines.Add("  lot global10=" + vm.GetGlobalValue(10) + " family=" + (fam == null ? "none" : fam.ChunkID + " house=" + fam.HouseNumber
                    + " members=" + string.Join(",", (fam.FamilyGUIDs ?? new uint[0]).Select(x => x.ToString("X8")))
                    + " active=" + string.Join(",", (fam.RuntimeSubset ?? new uint[0]).Select(x => x.ToString("X8")))));
                lines.Add("  trip: family=" + (neigh.GameState.ActiveFamily?.ChunkID.ToString() ?? "none") + " sim=" + neigh.GameState.DowntownSimGUID.ToString("X8") + " transit=" + neigh.GameState.LotTransitInfo
                    + " selected(global3)=" + vm.GetGlobalValue(3) + " me=" + vm.MyUID);
                foreach (var a in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>())
                {
                    lines.Add("  sim " + a.ObjectID + " " + a.Name + " guid=" + a.Object.OBJ.GUID.ToString("X8")
                        + " familyNo=" + a.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.TS1FamilyNumber)
                        + " personType=" + a.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.PersonType)
                        + " pid=" + a.PersistID + " inFamily=" + (fam?.FamilyGUIDs?.Contains(a.Object.OBJ.GUID) == true));
                }
                FSO.Common.IO.FSOFile.AppendAllLines(Path.Combine(FSOEnvironment.UserDir, "lot_debug.log"), lines);
            }
            catch { }
        }

        private int RefreshCount;
        private void VMRefreshed()
        {
            RefreshCount++;
            // SimsRedux online: a visitor's lot arrives before the lot controls exist
            if (vm == null || LotControl == null) return;
            LotControl.ActiveEntity = null;
            LotControl.RefreshCut();
        }

        private void SaveHouseButton_OnButtonClick(UIElement button)
        {
            if (vm == null) return;

            var exporter = new VMWorldExporter();
            FSODirectory.CreateDirectory(Path.Combine(FSOEnvironment.UserDir, "Blueprints/cas.xml"));
            exporter.SaveHouse(vm, Path.Combine(FSOEnvironment.UserDir, "Blueprints/cas.xml"));
            var marshal = vm.Save();
            FSODirectory.CreateDirectory(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/"));
            using (var output = FSOFile.Open(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/cas.fsov"), FileMode.Create))
            {
                marshal.SerializeInto(new BinaryWriter(output));
            }
        }

        private UIMobileAlert CloseAlert;
        /// <summary>SimsRedux: set once the player has said yes to quitting, so the window really closes.</summary>
        public static bool ExitConfirmed;

        public override bool CloseAttempt()
        {
            if (ExitConfirmed) return true;
            if (CloseAlert != null) return true;

            GameThread.NextUpdate(x =>
            {
                // SimsRedux: the classic Sims 1 popup in the classic interface
                if (UIClassicNeighborhoodBar.Enabled)
                {
                    if (ClassicCloseOpen) return;
                    ClassicCloseOpen = true;
                    var save = vm != null && !IsGuest;
                    if (save)
                        UIClassicDialog.Show(GameFacade.Strings.GetString("153", "1"), GameFacade.Strings.GetString("153", "6"),
                            Tuple.Create<string, Action>("Yes", () => { Save(); ExitConfirmed = true; GameFacade.Game.Exit(); }),
                            Tuple.Create<string, Action>("No", () => { ExitConfirmed = true; GameFacade.Game.Exit(); }),
                            Tuple.Create<string, Action>("Cancel", () => { ClassicCloseOpen = false; }));
                    else
                        UIClassicDialog.Show(GameFacade.Strings.GetString("153", "1"), GameFacade.Strings.GetString("153", "2"),
                            Tuple.Create<string, Action>("Yes", () => { ExitConfirmed = true; GameFacade.Game.Exit(); }),
                            Tuple.Create<string, Action>("No", () => { ClassicCloseOpen = false; }));
                    return;
                }
                if (CloseAlert == null)
                {
                    var canSave = vm != null;
                    CloseAlert = new UIMobileAlert(new FSO.Client.UI.Controls.UIAlertOptions
                    {
                        Title = GameFacade.Strings.GetString("153", "1"), //quit?
                        Message = GameFacade.Strings.GetString("153", canSave?"6":"2"), //are you sure (2), save before quitting (3)
                        Buttons = 
                        canSave?
                        UIAlertButton.YesNoCancel(
                            (b) => { Save(); GameFacade.Game.Exit(); },
                            (b) => { GameFacade.Game.Exit(); },
                            (b) => { CloseAlert.Close(); CloseAlert = null; }
                            )
                        :
                        UIAlertButton.YesNo(
                            (b) => { GameFacade.Game.Exit(); },
                            (b) => { CloseAlert.Close(); CloseAlert = null; }
                            )
                    });
                    GlobalShowDialog(CloseAlert, true);
                }
            });
            return false;
        }

        private bool ClassicCloseOpen;

        public void ReturnToNeighbourhood()
        {
            if (IsGuest)
            {
                UIClassicDialog.YesNo("Leave Lot?", "Do you want to leave your friend's lot and go back to your own neighborhood?", () => ExitLot());
                return;
            }
            if (UIClassicNeighborhoodBar.Enabled)
            {
                // SimsRedux: the original "save before going back?" question
                if (ClassicCloseOpen) return;
                ClassicCloseOpen = true;
                UIClassicDialog.Show(GameFacade.Strings.GetString("153", "3"), GameFacade.Strings.GetString("153", "4"),
                    Tuple.Create<string, Action>("Yes", () => { ClassicCloseOpen = false; Save(); ExitLot(); }),
                    Tuple.Create<string, Action>("No", () => { ClassicCloseOpen = false; ExitLot(); }),
                    Tuple.Create<string, Action>("Cancel", () => { ClassicCloseOpen = false; }));
                return;
            }
            if (CloseAlert == null)
            {
                CloseAlert = new UIMobileAlert(new FSO.Client.UI.Controls.UIAlertOptions
                {
                    Title = GameFacade.Strings.GetString("153", "3"), //save
                    Message = GameFacade.Strings.GetString("153", "4"), //Do you want to save the game?
                    Buttons =
                    UIAlertButton.YesNoCancel(
                        (b) => { Save(); ExitLot(); CloseAlert.Close(); CloseAlert = null; },
                        (b) => { ExitLot(); CloseAlert.Close(); CloseAlert = null; },
                        (b) => { CloseAlert.Close(); CloseAlert = null; }
                        )
                });
                GlobalShowDialog(CloseAlert, true);
            }
        }

        public void Save()
        {
            // SimsRedux online: a visitor never saves the host's lot or neighbourhood, and the
            // host saves without the visiting Sims (they belong to the friends' own games)
            if (IsGuest || vm == null) return;
            if (Host != null) { Host.SaveWithoutVisitors(SaveLot); return; }
            SaveLot();
        }

        private void SaveLot()
        {
            //save the house first
            var iff = new IffFile();
            vm.TS1State.UpdateSIMI(vm);
            var marshal = vm.Save(Host != null); // SimsRedux: leaves visiting friends' Sims out
            var fsov = new FSOV();
            fsov.ChunkLabel = "Simitone Lot Data";
            fsov.ChunkID = 1;
            fsov.ChunkProcessed = true;
            fsov.ChunkType = "FSOV";
            fsov.AddedByPatch = true;

            using (var stream = new MemoryStream())
            {
                marshal.SerializeInto(new BinaryWriter(stream));
                fsov.Data = stream.ToArray();
            }

            iff.AddChunk(fsov);

            var simi = vm.TS1State.SimulationInfo;
            simi.ChunkProcessed = true;
            simi.AddedByPatch = true;
            iff.AddChunk(simi);

            Texture2D roofless = null;
            var thumb = World.GetLotThumb(GameFacade.GraphicsDevice, (tex) => roofless = FSO.Common.Utils.TextureUtils.Decimate(tex, GameFacade.GraphicsDevice, 2, false));
            thumb = FSO.Common.Utils.TextureUtils.Decimate(thumb, GameFacade.GraphicsDevice, 2, false);

            var tPNG = GeneratePNG(thumb);
            tPNG.ChunkID = 513;
            iff.AddChunk(tPNG);

            var rPNG = GeneratePNG(roofless);
            rPNG.ChunkID = 512;
            iff.AddChunk(rPNG);

            Content.Get().Neighborhood.SaveHouse(vm.GetGlobalValue(10), iff);
            Content.Get().Neighborhood.SaveNeighbourhood(true);
        }

        public PNG GeneratePNG(Texture2D data)
        {
            var png = new PNG();
            using (var stream = new MemoryStream())
            {
                data.SaveAsPng(stream, data.Width, data.Height);
                png.data = stream.ToArray();
            }

            png.ChunkLabel = "Lot Thumbnail";
            png.ChunkProcessed = true;
            png.ChunkType = "PNG_";
            png.AddedByPatch = true;

            return png;
        }

        public void ExitLot()
        {
            // SimsRedux online: stop hosting / leave the friend's lot first
            if (Host != null)
            {
                Host.Stop("The host left the lot.");
                Host = null;
            }
            bool wasGuest = Guest != null;
            if (Guest != null)
            {
                Guest.Leave();
                Guest = null;
            }
            if (OnlineStatus != null) { Remove(OnlineStatus); OnlineStatus = null; }
            if (JoinScreen != null) { JoinScreen.Close(); JoinScreen = null; }
            CleanupLastWorld();
            if (wasGuest)
            {
                // back to the player's own neighbourhood
                try { Content.Get().Neighborhood.InitSpecific(0); }
                catch (Exception e) { ReduxOnlineInfo.Log("Couldn't reload your neighbourhood: " + e.Message); }
            }
            NeighSelection(NeighSelectionMode.Normal);
            Downtown = false;
            SavedLot = null;
        }
    }

    public enum NeighSelectionMode
    {
        Normal,
        MoveIn,
        MoveInMagic
    }
}