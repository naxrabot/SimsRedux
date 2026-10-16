
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
        private FAMI _activeFamily;
        /// <summary>
        /// The family being played. SimsRedux online: at home on your own family's lot (where the
        /// server runs the lot), it's the family the lot says lives there - money, friends and all.
        /// </summary>
        public FAMI ActiveFamily
        {
            get
            {
                if (_activeFamily != null || Guest == null || !Guest.IsOwner) return _activeFamily;
                var lotFamily = vm?.TS1State?.CurrentFamily;
                if (lotFamily != null && lotFamily.ChunkID == ReduxTown.MyFamily) return lotFamily;
                // (the lot hasn't said yet: the town's copy the lot sent when you arrived)
                try { return Content.Get().Neighborhood.GetFamily((ushort)ReduxTown.MyFamily) ?? lotFamily; }
                catch { return lotFamily; }
            }
            set { _activeFamily = value; }
        }

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
                    // (SimsRedux: a lot that never finished loading - an online join that failed -
                    // has a VM but no lot controls yet; leaving it must not stop half way)
                    if (vm == null || LotControl == null)
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
                if (World == null) return;
                World.State.CenterTile = World.EstTileAtPosWithScroll(new Vector2(ScreenWidth / 2, ScreenHeight / 2));
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
                return vm?.GetAvatarByPersist(vm.MyUID);
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

        /// <summary>
        /// SimsRedux: on any online lot (visiting, at home in the town, or hosting friends) the game
        /// always runs at normal speed for everyone - no pausing, no speeding up, not even in Buy or
        /// Build mode - so nobody's game falls out of step. (The server's own copy of a lot still
        /// pauses itself while nobody is on it.)
        /// </summary>
        public bool OnlineNoPause { get { return (Guest != null || Host != null) && !ReduxWorker.Active; } }

        /// <summary>The current game screen's OnlineNoPause (for panels that only have the lot).</summary>
        public static bool CurrentOnlineNoPause { get { return (UIScreen.Current as TS1GameScreen)?.OnlineNoPause == true; } }
        /// <summary>2C: on someone else's lot (a guest who isn't the family's owner).</summary>
        public bool IsVisitor { get { return Guest != null && !Guest.IsOwner; } }
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
            // SimsRedux: the classic top bar replaces the side buttons (and needs to know about
            // move-in mode before the screen is built, so it's only built once)
            var classicBar = UIClassicNeighborhoodBar.Enabled;
            TS1NeighPanel = new UINeighborhoodSelectionPanel(nbd, classicBar && mode != NeighSelectionMode.Normal);
            UIElement switcher = classicBar ? null : new UINeighbourhoodSwitcher(TS1NeighPanel, nbd, mode != NeighSelectionMode.Normal);
            TS1NeighPanel.OnHouseSelect += (house) =>
            {
                if (ReduxTown.Active)
                {
                    // SimsRedux online: a family made in the town's Create-A-Family moves in through the server
                    if (MoveInFamily != null) MoveIntoTown((short)house);
                    else TravelToTownHouse((short)house);
                }
                else if (MoveInFamily != null)
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
            try { Console.WriteLine("[SimsRedux] neighbourhood screen (" + (ReduxTown.Active ? "online town" : "your own") + "): " + TS1NeighPanel.Describe()); } catch { }
        }

        public void PlayHouse(short house, UIElement switcher)
        {
            if (UIClassicNeighborhoodBar.Enabled && !ClassicLoading)
            {
                // SimsRedux: original loading screen first
                ClassicLoading = true;
                // (reset even if loading the lot throws, or no house could be opened again)
                UIClassicLoadScreen.Run(house, false, () => { try { PlayHouse(house, switcher); } finally { ClassicLoading = false; } });
                return;
            }
            // SimsRedux: "Just Watch" a background family (nothing can be done to or with them)
            Watching = WatchNext;
            WatchNext = false;
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
            if (Watching)
            {
                var famName = (ActiveFamily == null) ? "" : (Content.Get().Neighborhood.MainResource.Get<FAMs>(ActiveFamily.ChunkID)?.GetString(0) ?? "");
                GameThread.NextUpdate((s) => UIClassicDialog.Ok("Just Watching", "The " + famName + " family is living their own life - they'll do their own thing while you look around."
                    + "\n\nTo play them, go back to the neighborhood, click their house and choose Take Over."));
            }
        }

        /// <summary>SimsRedux: the next lot opened is only watched (a background family, not taken over).</summary>
        public static bool WatchNext;
        /// <summary>SimsRedux: watching a background family: no commands, no buying or building.</summary>
        public bool Watching;

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
            vm.ReduxNoPause = true; // online: the lot never pauses
            if (!ReduxWorker.Active && vm.SpeedMultiplier != 1) vm.SendCommand(new VMNetSetSpeedCmd() { Speed = 1 });
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
            JoinOnline(address, join);
        }

        /// <summary>Visit a lot with a "here's me and my Sim" packet made earlier.</summary>
        public void JoinOnline(string address, byte[] join)
        {
            if (vm != null || join == null) return;

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
            vm.ReduxNoPause = true; // online: the lot never pauses (same as the server's copy)
            ActiveFamily = null;
            Downtown = false;

            if (TS1NeighPanel != null) Remove(TS1NeighPanel);
            // a house in the online town (".../lot/12") or a friend's lot
            var townHouse = System.Text.RegularExpressions.Regex.Match(address ?? "", @"/lot/(\d+)\s*(\?.*)?$");
            JoinScreen = UIClassicLoadScreen.ShowWaiting(townHouse.Success ? "Travelling to House " + townHouse.Groups[1].Value : "Visiting a Friend", "Connecting to the server...", true);
            JoinStarted = DateTime.UtcNow;

            Guest = new ReduxGuest(vm, driver, address, join);
            Guest.Connect();
        }

        private DateTime JoinStarted;
        private int JoinDrawFrames;

        /// <summary>While connecting: after a few seconds, explain the wait (the server may be opening the lot).</summary>
        private void UpdateJoinMessage()
        {
            if (JoinScreen == null || Guest == null || !Guest.Connecting) return;
            var secs = (DateTime.UtcNow - JoinStarted).TotalSeconds;
            if (secs > 4) JoinScreen.SetWaiting("Opening the lot on the server - this can take up to a minute...", (float)Math.Min(0.4, 0.05 + secs / 150));
        }

        private void GuestStateChange(int state, float progress)
        {
            // 1 = connected, 2 = loading the lot (progress), 3 = ready
            if (state == 1) JoinScreen?.SetWaiting("Joining...", 0.45f);
            if (state == 2) JoinScreen?.SetWaiting("Loading the lot...", 0.45f + progress * 0.55f);
            if (state == 3) GameThread.NextUpdate((s) => GuestReady());
        }

        private void GuestReady()
        {
            if (vm == null || Guest == null || LotControl != null) return;
            // the loading wallpaper stays up until the lot has actually been drawn for a moment
            // (closing it now showed a grey screen while the lot's pictures were still loading)
            JoinScreen?.SetWaiting("Arriving...", 0.95f);
            JoinDrawFrames = 0;
            LotControl = new UILotControl(vm, World);
            this.AddAt(0, LotControl);
            m_ZoomLevel = 4;
            ZoomLevel = 1;
            GameFacade.Cursor.SetCursor(CursorType.Normal);
            Frontend = new UISimitoneFrontend(this);
            this.Add(Frontend);
            ShowOnlineStatus();
            ReduxOnlineInfo.Log("On the host's lot");
            if (Guest.IsOwner)
            {
                var lotFam = vm.TS1State?.CurrentFamily;
                FAMI hoodFam = null;
                try { hoodFam = Content.Get().Neighborhood.GetFamily((ushort)ReduxTown.MyFamily); } catch { }
                ReduxOnlineInfo.Log("[home] my family " + ReduxTown.MyFamily + "; the lot's family: " + (lotFam == null ? "none" : lotFam.ChunkID + " with §" + lotFam.Budget)
                    + "; the town's copy: " + (hoodFam == null ? "none" : "§" + hoodFam.Budget) + "; showing §" + (ActiveFamily?.Budget ?? -1));
            }
        }

        private void GuestLost()
        {
            var reason = Guest?.Failure ?? "The connection to the host was lost.";
            ReduxOnlineInfo.Log("Left the host's lot: " + reason);
            if (ReduxTown.Active)
            {
                // back to the town: the message waits until the town is on screen (the town's
                // loading box closing would otherwise take the message with it)
                TownMessage = reason;
                ExitLot();
                return;
            }
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

        private static bool TownSpeedExplained;

        public void ChangeSpeedTo(int speed)
        {
            if (IsVisitor) return; // SimsRedux: the host (or the family's owner) controls the speed
            if (OnlineNoPause)
            {
                // online, the game runs at normal speed for everyone - it can't be paused or sped up
                if (!TownSpeedExplained)
                {
                    TownSpeedExplained = true;
                    UIClassicDialog.Ok("Normal Speed Only", "Online, the game always runs at normal speed for everyone - it can't be paused or sped up, even in Buy and Build mode."
                        + (ReduxTown.Active ? "\n\nThe town's clock only moves while someone is playing in the town - when nobody is, the whole town waits." : ""));
                }
                return;
            }
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
            ReduxNotices.Tick();
            UpdateJoinMessage();
            if (Guest != null && !Guest.Process()) GuestLost();
            // the lot is on screen: the loading wallpaper can go
            if (JoinScreen != null && LotControl != null && World != null && World.Visible && vm != null && vm.Ready)
            {
                if (++JoinDrawFrames >= 45) { JoinScreen.Close(); JoinScreen = null; }
                else JoinScreen.SetWaiting("Arriving...", 0.95f + JoinDrawFrames / 45f * 0.05f);
            }
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
            ReduxVisitors.Tick(this); // SimsRedux: townies and walk-bys (single player)
            if (vm != null) vm.Update();

            //SaveHouseButton_OnButtonClick(null);
        }

        private void DoSwitchLot(int target)
        {
            if (vm == null) return;
            // SimsRedux: a taxi called from the phone (only if it's this trip - the game's own travel may have come first)
            var taxi = PendingTaxi;
            PendingTaxi = null;
            if (taxi != null && taxi.House != target) taxi = null;
            if (!Downtown) SavedLot = vm.Save();
            if (ActiveFamily != null && target == ActiveFamily.HouseNumber && SavedLot != null)
            {
                Downtown = false;
                if (taxi != null) ActiveFamily.SelectWholeFamily(); // home: the whole family is playable again
                InitializeLot(SavedLot);
                SavedLot = null;
                if (taxi != null) ApplyTaxiMotives(taxi);
            }
            else
            {
                Downtown = true;
                TaxiArrival = (taxi != null && taxi.Visit) ? taxi : null;
                try { InitializeLot(Content.Get().Neighborhood.GetHousePath(target), false); }
                finally { TaxiArrival = null; }
                VisitingHouse = taxi != null && taxi.Visit;
            }
        }

        // ================= SimsRedux: Call Taxi (single player) =================

        private class TaxiTrip
        {
            public short House;
            /// <summary>Visiting another family's house (not a community lot, not home).</summary>
            public bool Visit;
            /// <summary>The Sims going (object GUIDs).</summary>
            public uint[] Travellers = new uint[0];
            /// <summary>Their motives when they left: bladder, comfort, energy, fun, hunger, hygiene, social.</summary>
            public Dictionary<uint, short[]> Motives = new Dictionary<uint, short[]>();
        }
        private TaxiTrip PendingTaxi;
        private TaxiTrip TaxiArrival;

        /// <summary>
        /// SimsRedux: on another family's house, reached by taxi. That house is never saved
        /// (your Sims would end up living in it); going home restores your own lot.
        /// </summary>
        public bool VisitingHouse;

        /// <summary>SimsRedux: the phone can call a taxi: single player, playing a family, not already travelling.</summary>
        public bool CanCallTaxi
        {
            get
            {
                return vm != null && vm.TS1 && !IsGuest && Host == null && !ReduxTown.Active && !ReduxWorker.Active
                    && !Watching && ActiveFamily != null && SwitchLot <= 0;
            }
        }

        /// <summary>SimsRedux: where a taxi can go from here (home first when away, then houses, then community lots).</summary>
        public List<Simitone.Client.UI.Panels.LotControls.UICallTaxiAlert.Destination> TaxiDestinations()
        {
            var result = new List<Simitone.Client.UI.Panels.LotControls.UICallTaxiAlert.Destination>();
            if (!CanCallTaxi) return result;
            var neigh = Content.Get().Neighborhood;
            short current = vm.GetGlobalValue(10);
            short home = (short)ActiveFamily.HouseNumber;
            if (home > 0 && current != home && Downtown && SavedLot != null)
                result.Add(new Simitone.Client.UI.Panels.LotControls.UICallTaxiAlert.Destination(home, "Home - " + TaxiFamilyName(ActiveFamily)));

            var seen = new HashSet<short>() { current, home };
            var community = new List<Simitone.Client.UI.Panels.LotControls.UICallTaxiAlert.Destination>();
            foreach (var mode in new int[] { 4, 2, 3, 5, 7 })
            {
                STR str = null;
                try { str = neigh.LotLocations?.Get<STR>((ushort)mode); } catch { }
                if (str == null) continue;
                string area = TaxiAreaName(mode);
                for (int i = 0; i < str.Length; i++)
                {
                    short num;
                    var parts = (str.GetString(i) ?? "").Split(',');
                    if (parts.Length == 0 || !short.TryParse(parts[0].Trim(), out num)) continue;
                    if (num <= 0 || num == 99 || seen.Contains(num)) continue;
                    seen.Add(num);
                    try
                    {
                        if (!FSOFile.Exists(neigh.GetHousePath(num))) continue;
                        var fam = neigh.GetFamilyForHouse(num);
                        var name = TaxiLotName(num);
                        if (fam != null)
                        {
                            // someone lives here: visit them
                            result.Add(new Simitone.Client.UI.Panels.LotControls.UICallTaxiAlert.Destination(num,
                                area + "The " + TaxiFamilyName(fam) + " family (" + name + ")"));
                        }
                        else if (neigh.IsCommunity(num))
                        {
                            community.Add(new Simitone.Client.UI.Panels.LotControls.UICallTaxiAlert.Destination(num,
                                area + name + (mode == 4 ? " (community lot)" : "")));
                        }
                        // (empty houses and houses for sale: nobody to visit, and no phone to get home)
                    }
                    catch (Exception e) { Console.WriteLine("[SimsRedux] taxi: skipped lot " + num + ": " + e.Message); }
                }
            }
            result.AddRange(community);
            return result;
        }

        private static string TaxiAreaName(int mode)
        {
            switch (mode)
            {
                case 2: return "Downtown: ";
                case 3: return "Vacation Island: ";
                case 5: return "Studio Town: ";
                case 7: return "Magic Town: ";
                default: return "";
            }
        }

        private static string TaxiFamilyName(FAMI fam)
        {
            string name = null;
            try { name = Content.Get().Neighborhood.MainResource.Get<FAMs>(fam.ChunkID)?.GetString(0); } catch { }
            return string.IsNullOrEmpty(name) ? "Unknown" : name;
        }

        private static string TaxiLotName(short house)
        {
            var neigh = Content.Get().Neighborhood;
            string name = null;
            try { name = neigh.GetHouseNameDesc(house)?.Item1; } catch { }
            if (string.IsNullOrEmpty(name))
            {
                try
                {
                    var street = neigh.StreetNames;
                    var assignment = street?.Get<STR>(2001)?.GetString(house - 1);
                    int streetName;
                    if (int.TryParse(assignment, out streetName))
                        name = street.Get<STR>(2000)?.GetString(streetName - 1)?.Replace("%s", house.ToString());
                }
                catch { }
            }
            return string.IsNullOrEmpty(name) ? ("Lot " + house) : name;
        }

        /// <summary>
        /// SimsRedux: a taxi takes "caller" (and, except to Downtown, the rest of the family on this lot)
        /// to another lot. Works like the game's own travel: this lot is kept to come back to, the
        /// Sims' motives and the time go with them, and Downtown / Vacation / Studio Town / Magic Town
        /// and community lots build the arriving Sims with their own scripts. Another family's house
        /// has no such script, so the Sims are walked in at the mailbox here (see ArriveAtHouse).
        /// </summary>
        public bool TaxiTo(short house, VMAvatar caller)
        {
            try
            {
                if (!CanCallTaxi || caller == null || caller.Dead || house <= 0) return false;
                if (vm.GetObjectById(caller.ObjectID) != caller) return false; // not on this lot any more
                if (house == vm.GetGlobalValue(10)) return false;
                var neigh = Content.Get().Neighborhood;
                var family = vm.TS1State.CurrentFamily ?? ActiveFamily;
                bool home = house == ActiveFamily.HouseNumber;
                if (home && (SavedLot == null || !Downtown)) return false;
                if (!home && !FSOFile.Exists(neigh.GetHousePath(house))) return false;
                bool community = !home && neigh.IsCommunity(house) && neigh.GetFamilyForHouse(house) == null;
                int mode = UIClassicLoadScreen.ModeForHouse(house);
                // Downtown (Hot Date) is a trip for one; everywhere else the family on the lot comes along
                short transit = (short)((community && mode == 2) ? 0 : 1);

                var people = new List<VMAvatar>() { caller };
                if (transit >= 1)
                {
                    foreach (var ava in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>())
                    {
                        if (ava == caller || ava.Dead || ava.Position == LotTilePos.OUT_OF_WORLD) continue;
                        if (ava.GetPersonData(VMPersonDataVariable.TS1FamilyNumber) == family.ChunkID) people.Add(ava);
                    }
                }

                var trip = new TaxiTrip() { House = house, Visit = !home && !community };
                int pi = 0;
                foreach (var person in people)
                {
                    var m = new short[] {
                        person.GetMotiveData(VMMotive.Bladder),
                        person.GetMotiveData(VMMotive.Comfort),
                        person.GetMotiveData(VMMotive.Energy),
                        person.GetMotiveData(VMMotive.Fun),
                        person.GetMotiveData(VMMotive.Hunger),
                        person.GetMotiveData(VMMotive.Hygiene),
                        person.GetMotiveData(VMMotive.Social)
                    };
                    trip.Motives[person.Object.OBJ.GUID] = m;
                    if (home) continue; // home is restored as it was left; the motives are put back after
                    // the same "sim data effects" the game's own travel writes (lots read them on arrival)
                    var nid = person.GetPersonData(VMPersonDataVariable.NeighborId);
                    var inv = neigh.GetInventoryByNID(nid);
                    if (inv == null)
                    {
                        inv = new List<InventoryItem>();
                        neigh.SetInventoryForNID(nid, inv);
                    }
                    for (int i = 0; i < m.Length; i++) SetTaxiData(inv, (uint)i, m[i]);
                    if (pi++ == 0)
                    {
                        SetTaxiData(inv, 7, (short)vm.Context.Clock.Hours);
                        SetTaxiData(inv, 8, (short)vm.Context.Clock.Minutes);
                    }
                }
                trip.Travellers = people.Select(x => x.Object.OBJ.GUID).Distinct().ToArray();

                if (!home)
                {
                    var cross = neigh.GameState;
                    cross.ActiveFamily = family;
                    cross.DowntownSimGUID = caller.Object.OBJ.GUID;
                    cross.LotTransitInfo = transit;
                }

                Console.WriteLine("[SimsRedux] taxi: " + caller.Name + " to lot " + house + (home ? " (home)" : (community ? " (community)" : " (visiting)"))
                    + ", " + people.Count + " Sim(s)");
                PendingTaxi = trip;
                vm.SpeedMultiplier = 0;
                SwitchLot = house;
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] taxi couldn't leave: " + e);
                PendingTaxi = null;
                return false;
            }
        }

        private static void SetTaxiData(List<InventoryItem> inventory, uint guid, short data)
        {
            var replace = inventory.FirstOrDefault(x => x.Type == 2 && x.GUID == guid);
            if (replace == null)
            {
                replace = new InventoryItem() { Type = 2, GUID = guid };
                inventory.Add(replace);
            }
            replace.Count = (ushort)data;
        }

        /// <summary>SimsRedux: the travellers get the motives they left with.</summary>
        private void ApplyTaxiMotives(TaxiTrip trip)
        {
            if (vm == null || trip?.Motives == null) return;
            try
            {
                foreach (var ava in vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().ToList())
                {
                    short[] m;
                    if (ava.Dead || !trip.Motives.TryGetValue(ava.Object.OBJ.GUID, out m) || m == null || m.Length < 7) continue;
                    ava.SetMotiveData(VMMotive.Bladder, m[0]);
                    ava.SetMotiveData(VMMotive.Comfort, m[1]);
                    ava.SetMotiveData(VMMotive.Energy, m[2]);
                    ava.SetMotiveData(VMMotive.Fun, m[3]);
                    ava.SetMotiveData(VMMotive.Hunger, m[4]);
                    ava.SetMotiveData(VMMotive.Hygiene, m[5]);
                    ava.SetMotiveData(VMMotive.Social, m[6]);
                }
            }
            catch (Exception e) { Console.WriteLine("[SimsRedux] taxi: couldn't restore motives: " + e.Message); }
        }

        /// <summary>
        /// SimsRedux: arrived at another family's house by taxi. The travellers are this lot's
        /// playable family while here: they walk in at the mailbox, and nothing can be bought or
        /// built (it isn't your house, and it's never saved).
        /// </summary>
        private void ArriveAtHouse(TaxiTrip trip)
        {
            try
            {
                ActiveFamily.RuntimeSubset = trip.Travellers;
                vm.TS1State.ActivateFamily(vm, ActiveFamily); // the house's own save put its family's number back
                vm.TS1State.VerifyFamily(vm);
                ApplyTaxiMotives(trip);
                vm.Context.Architecture.BuildBuyEnabled = false;
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] taxi: arriving at lot " + trip.House + " went wrong: " + e);
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
            // (the lot controls and frontend are removed by CleanupLastWorld, once)
            vm.SuppressBHAVChanges();
        }

        private VMMarshal SavedLot;

        public void InitializeLot()
        {
            CleanupLastWorld();
            VisitingHouse = false; // (DoSwitchLot sets it again after arriving at another family's house)
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
                // SimsRedux taxi: visiting another family's house - the travellers are the family played here
                var taxiArrival = (Downtown && ActiveFamily != null) ? TaxiArrival : null;
                if (taxiArrival != null)
                {
                    ActiveFamily.RuntimeSubset = taxiArrival.Travellers;
                    vm.TS1State.ActivateFamily(vm, ActiveFamily);
                }
                BlueprintReset(lotName, null);
                if (taxiArrival != null) ArriveAtHouse(taxiArrival);
                
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
            // SimsRedux: like Sims 1, every lot gets the game's global objects (lot controllers) a house
            // was saved without. Single player only - online lots stay exactly as the server has them.
            if (!isSurrounding && Host == null && !IsGuest && !ReduxTown.Active && !ReduxWorker.Active)
                VMTS1Activator.EnsureGlobalSimObjects(vm);

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
            if ((short)lotId == -1)
            {
                if (ActiveFamily == null) return; // "go home" with no family to go home to
                lotId = (uint)ActiveFamily.HouseNumber;
            }
            vm.SpeedMultiplier = 0;
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
            UILotControl.RefreshTally++;
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
                    var canSave = vm != null && !IsGuest; // (a visitor's game never saves the host's lot)
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
                UIClassicDialog.YesNo("Leave Lot?", ReduxTown.Active ? "Leave this lot and go back to the town?" : "Do you want to leave your friend's lot and go back to your own neighborhood?", () => ExitLot());
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
            if (VisitingHouse)
            {
                // SimsRedux taxi: another family's house is never saved (your Sims would move in);
                // only the neighbourhood (who met whom) is. Your own lot is kept to go home to.
                Console.WriteLine("[SimsRedux] visiting by taxi: saving the neighbourhood, not this house");
                try { Content.Get().Neighborhood.SaveNeighbourhood(true); }
                catch (Exception e) { Console.WriteLine("[SimsRedux] couldn't save the neighbourhood: " + e.Message); }
                return;
            }
            if (Host != null) { Host.SaveWithoutVisitors(SaveLot); return; }
            SaveLot();
        }

        private void SaveLot()
        {
            //save the house first
            var iff = new IffFile();
            vm.TS1State.UpdateSIMI(vm);
            var marshal = vm.Save(true); // SimsRedux: leaves visiting friends' Sims and passing townies (ReduxVisitors) out
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
            Downtown = false;
            VisitingHouse = false;
            PendingTaxi = null;
            SavedLot = null;
            Watching = false;
            if (wasGuest && ReduxTown.Active)
            {
                // SimsRedux: back to the online town (fetching it again if a lot changed it)
                ShowTown("Back to the Town");
                return;
            }
            if (wasGuest)
            {
                // back to the player's own neighbourhood
                try { Content.Get().Neighborhood.InitSpecific(0); }
                catch (Exception e) { ReduxOnlineInfo.Log("Couldn't reload your neighbourhood: " + e.Message); }
            }
            NeighSelection(NeighSelectionMode.Normal);
        }

        // ================= SimsRedux: the online town (drop 2B) =================

        private bool TownBusy;
        /// <summary>Why we were sent back to the town (shown once the town is on screen).</summary>
        private string TownMessage;

        /// <summary>
        /// Go to the server's town with one of your own Sims: the Sim is packed up from your own
        /// neighbourhood first, then the town is downloaded (if it changed) and shown.
        /// </summary>
        public void EnterTown()
        {
            if (vm != null || TownBusy) return;
            // your own families, packed up now (before the town replaces your neighbourhood),
            // in case you move one of them into the town
            try { ReduxTown.MyFamilies = ReduxFamilyPackage.FromCurrentNeighbourhood(); }
            catch (Exception e) { ReduxOnlineInfo.Log("[town] couldn't list your families: " + e.Message); ReduxTown.MyFamilies.Clear(); }
            ShowTown("Going to the Town");
        }

        /// <summary>Old way in (picked a single-player Sim): still goes to the town; the Sim isn't needed any more.</summary>
        public void EnterTown(uint simGUID) { EnterTown(); }

        /// <summary>Fetch the town if needed, then put it on the neighbourhood screen.</summary>
        private void ShowTown(string title)
        {
            TownBusy = true;
            if (TS1NeighPanel != null) { Remove(TS1NeighPanel); TS1NeighPanel = null; }
            var wait = UIClassicLoadScreen.ShowWaiting(title, "Connecting to the server...", true);
            ReduxTown.Sync((text, p) => wait?.SetWaiting(text, p), (error) =>
            {
                wait?.Close();
                TownBusy = false;
                if (error == null)
                {
                    try
                    {
                        Content.Get().Neighborhood.LoadFolder(ReduxTown.Folder);
                        ReduxTown.Active = true;
                    }
                    catch (Exception e)
                    {
                        error = "The town couldn't be opened: " + e.Message;
                        ReduxOnlineInfo.Log("[town] " + e);
                    }
                }
                if (error != null)
                {
                    LeaveTownNow();
                    if (TownMessage != null) { error = TownMessage + "\n\n" + error; TownMessage = null; }
                    UIClassicDialog.Ok("The Town", error);
                    return;
                }
                NeighSelection(NeighSelectionMode.Normal);
                if (TownMessage != null)
                {
                    var msg = TownMessage;
                    TownMessage = null;
                    GameThread.NextUpdate((s3) => UIClassicDialog.Ok("The Town", msg));
                }
                try
                {
                    ReduxOnlineInfo.Log("[town] showing the town: " + TS1NeighPanel?.Describe());
                    GameThread.NextUpdate((s2) => ReduxOnlineInfo.Log("[town] a moment later: " + TS1NeighPanel?.Describe()));
                }
                catch (Exception e) { ReduxOnlineInfo.Log("[town] " + e.Message); }
            });
        }

        /// <summary>SimsRedux: rebuild the neighbourhood screen (after the neighbourhood itself was replaced).</summary>
        public void RefreshNeighbourhoodScreen()
        {
            if (vm != null) return;
            if (TS1NeighPanel != null) { Remove(TS1NeighPanel); TS1NeighPanel = null; }
            NeighSelection(NeighSelectionMode.Normal);
        }

        /// <summary>SimsRedux: Leave Multiplayer - from a lot in the town (or the town itself) straight back to single player.</summary>
        public void LeaveMultiplayer()
        {
            if (vm != null)
            {
                ReduxTown.Active = false; // so leaving the lot goes home, not back to the town
                ExitLot();
                return;
            }
            LeaveTown();
        }

        /// <summary>Back to your own neighbourhood from the town.</summary>
        public void LeaveTown()
        {
            if (vm != null || TownBusy) return;
            if (TS1NeighPanel != null) { Remove(TS1NeighPanel); TS1NeighPanel = null; }
            LeaveTownNow();
        }

        private void LeaveTownNow()
        {
            ReduxTown.Active = false;
            try { Content.Get().Neighborhood.InitSpecific(0); }
            catch (Exception e) { ReduxOnlineInfo.Log("Couldn't reload your neighbourhood: " + e.Message); }
            NeighSelection(NeighSelectionMode.Normal);
        }

        /// <summary>A house in the town was picked: travel to its lot on the server.</summary>
        public void TravelToTownHouse(short house)
        {
            if (vm != null || !ReduxTown.Active) return;
            HostAfterLoad = false;
            if (house == ReduxTown.MyHouse)
            {
                // home: you control your whole family there
                JoinOnline(ReduxTown.LotAddress(house), ReduxGuest.MakeOwnerJoinPacket(ReduxTown.Name));
                if (Guest != null) Guest.IsOwner = true;
                return;
            }
            if (ReduxTown.MyFamily < 0)
            {
                UIClassicDialog.Ok("Visit", "Move your family into the town first: click an empty lot or a house for sale and choose Move In.");
                return;
            }
            // visiting: one of your family goes
            var members = MyTownFamily();
            if (members.Count == 0)
            {
                UIClassicDialog.Ok("Visit", "Your family's Sims couldn't be found in the town.");
                return;
            }
            Action<uint> go = (guid) =>
            {
                string error;
                var join = ReduxGuest.MakeJoinPacket(ReduxTown.Name, guid, out error);
                if (join == null) { UIClassicDialog.Ok("Visit", error); return; }
                JoinOnline(ReduxTown.LotAddress(house), join);
            };
            if (members.Count == 1) { go(members[0].Item1); return; }
            var buttons = new List<Tuple<string, Action>>();
            foreach (var m in members.Take(5)) { var g = m.Item1; buttons.Add(Tuple.Create<string, Action>(m.Item2, () => go(g))); }
            buttons.Add(Tuple.Create<string, Action>("Cancel", null));
            UIClassicDialog.Show("Visit", "Who's going?", buttons.ToArray());
        }

        /// <summary>2C: your family's Sims in the town (id, first name).</summary>
        private List<Tuple<uint, string>> MyTownFamily()
        {
            var list = new List<Tuple<uint, string>>();
            try
            {
                var neigh = Content.Get().Neighborhood;
                var fam = neigh.MainResource.List<FAMI>()?.FirstOrDefault(f => f.ChunkID == ReduxTown.MyFamily);
                if (fam?.FamilyGUIDs == null) return list;
                foreach (var guid in fam.FamilyGUIDs)
                {
                    var obj = Content.Get().WorldObjects.Get(guid);
                    if (obj == null) continue;
                    string n = null;
                    try { n = obj.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0); } catch { }
                    list.Add(Tuple.Create(guid, string.IsNullOrWhiteSpace(n) ? obj.OBJ.ChunkLabel : n));
                }
            }
            catch (Exception e) { ReduxOnlineInfo.Log("[town] couldn't list your family: " + e.Message); }
            return list;
        }

        /// <summary>2C: move one of your families into an empty lot or house for sale in the town.</summary>
        public void MoveIntoTown(short house)
        {
            if (vm != null || !ReduxTown.Active || TownBusy) return;
            if (ReduxTown.MyFamily >= 0)
            {
                UIClassicDialog.Ok("Move In", "Your family already lives in the town. (Moving house comes later.)");
                return;
            }
            int price = 0;
            try { price = Content.Get().Neighborhood.GetHouse(house)?.Get<SIMI>(1)?.PurchaseValue ?? 0; } catch { }
            var money = ReduxFamilyPackage.OnlineStartingMoney;
            Action<ReduxFamilyPackage, bool> confirm = (fam, made) =>
            {
                if (money < price)
                {
                    UIClassicDialog.Ok("Move In", "Families start the online town with §" + money.ToString("##,#0") + ", but this lot costs §" + price.ToString("##,#0") + ". Pick a cheaper house or an empty lot.");
                    return;
                }
                UIClassicDialog.YesNo("Move In", "Move the " + fam.Name + " family (" + fam.Members.Count + " Sim" + (fam.Members.Count == 1 ? "" : "s") + ") in here for §" + price.ToString("##,#0") + "?\n\n"
                    + "They start the online town with §" + money.ToString("##,#0") + ", leaving §" + (money - price).ToString("##,#0") + ". "
                    + (made ? "" : "They're copied from your own neighborhood - your single-player family stays where it is. ")
                    + "Online, only you can control them.",
                    () => { MoveInFamily = null; DoMoveIn(house, fam); });
            };

            // a family just made in Create-A-Family (in the town)
            if (MoveInFamily != null)
            {
                var made = Content.Get().Neighborhood.GetFamily((ushort)MoveInFamily.Value);
                if (made == null || (made.Unknown & 8) == 0)
                {
                    UIClassicDialog.Ok("Move In", "That's one of the town's own families - make your own family to move in.");
                    MoveInFamily = null;
                    return;
                }
                var pack = ReduxFamilyPackage.Pack(made);
                if (pack == null || pack.Members.Count == 0) { UIClassicDialog.Ok("Move In", "That family couldn't be read."); return; }
                confirm(pack, true);
                return;
            }

            // make a new online family, or bring one of your own families from single player
            var families = ReduxTown.MyFamilies;
            var buttons = new List<Tuple<string, Action>>();
            buttons.Add(Tuple.Create<string, Action>("Create a New Family", () =>
                UIClassicDialog.Ok("Create a New Family", "Make your online family in Create-A-Family. When they're done, choose Move In there, then click this lot (or any empty lot or house for sale).",
                    () => new UITransDialog("cas", () => { GameController.EnterCAS(); }))));
            // families already made in the town's Create-A-Family (waiting in the family bin)
            try
            {
                var neigh = Content.Get().Neighborhood;
                foreach (var f in neigh.MainResource.List<FAMI>() ?? new List<FAMI>())
                {
                    if ((f.Unknown & 8) == 0 || f.HouseNumber != 0 || f.FamilyGUIDs == null || f.FamilyGUIDs.Length == 0) continue;
                    var pack = ReduxFamilyPackage.Pack(f);
                    if (pack == null || pack.Members.Count == 0) continue;
                    buttons.Add(Tuple.Create<string, Action>("The " + pack.Name + " family (new)", () => confirm(pack, true)));
                    if (buttons.Count >= 3) break;
                }
            }
            catch (Exception e) { ReduxOnlineInfo.Log("[town] couldn't list new families: " + e.Message); }
            foreach (var f in families.Take(5 - buttons.Count)) { var ff = f; buttons.Add(Tuple.Create<string, Action>("Bring the " + f.Name + " family", () => confirm(ff, false))); }
            buttons.Add(Tuple.Create<string, Action>("Cancel", null));
            UIClassicDialog.Show("Move In", "Who's moving into the town?\n\nMake a brand-new family for the online town, or bring one of your families from your own neighborhood (they're copied - your single-player family stays).", buttons.ToArray());
        }

        /// <summary>2C: delete your online family (they leave the town; the house goes up for sale) to start a new one.</summary>
        public void MoveOutOfTown()
        {
            if (vm != null || !ReduxTown.Active || TownBusy || ReduxTown.MyFamily < 0) return;
            var name = string.IsNullOrEmpty(ReduxTown.MyFamilyName) ? "your" : "the " + ReduxTown.MyFamilyName;
            UIClassicDialog.YesNo("Move Out", "Delete " + name + " family from the online town?\n\nThey leave the town for good and the house goes up for sale. "
                + "Your single-player families aren't touched. Afterwards you can make a new family or bring another one in.",
                () =>
                {
                    TownBusy = true;
                    var wait = UIClassicLoadScreen.ShowWaiting("Moving Out", "Your family is packing up...", true);
                    ReduxTown.MoveOut((error) =>
                    {
                        wait?.Close();
                        TownBusy = false;
                        if (error != null) { UIClassicDialog.Ok("Move Out", error); return; }
                        ShowTown("Back to the Town"); // the town again, without them
                    });
                });
        }

        private void DoMoveIn(short house, ReduxFamilyPackage fam)
        {
            TownBusy = true;
            var wait = UIClassicLoadScreen.ShowWaiting("Moving In", "The " + fam.Name + " family is moving in...", true);
            ReduxTown.MoveIn(house, fam, (error) =>
            {
                wait?.Close();
                TownBusy = false;
                if (error != null) { UIClassicDialog.Ok("Move In", error); return; }
                ReduxOnlineInfo.Log("[town] the " + fam.Name + " family moved into house " + house);
                TravelToTownHouse(house); // straight home
            });
        }
    }

    public enum NeighSelectionMode
    {
        Normal,
        MoveIn,
        MoveInMagic
    }
}