/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework;
using FSO.HIT;

using FSO.LotView;
using FSO.SimAntics;
using FSO.LotView.Components;
using Microsoft.Xna.Framework.Input;
using FSO.LotView.Model;
using FSO.SimAntics.Primitives;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.SimAntics.Utils;
using FSO.Common;
using FSO.Client;
using FSO.Content;
using FSO.Client.Debug;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using FSO.LotView.RC;
using Simitone.Client.UI.Panels.LotControls;
using FSO.Client.UI.Panels.LotControls;
using FSO.UI.Panels.LotControls;
using Simitone.Client.UI.Controls;

using FSO.Common.IO;
namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// Generates pie menus when the player clicks on objects.
    /// </summary>
    public class UILotControl : UIContainer, ILotControl
    {
        private UIMouseEventRef MouseEvt;
        public bool MouseIsOn;
        public I3DRotate Rotate { get { return World.State.Cameras.Camera3D; } }

        private UIPieMenu PieMenu;

        private bool ShowTooltip;
        private bool TipIsError;
        private Texture2D RMBCursor;

        public FSO.SimAntics.VM vm;
        public FSO.LotView.World World { get; set; }
        public VMEntity ActiveEntity { get; set; }
        public int Budget { get
            {
                return vm.TS1State.CurrentFamily?.Budget ?? int.MaxValue;
            }
        }
        public uint SelectedSimID
        {
            get
            {
                return (vm == null) ? 0 : vm.MyUID;
            }
        }
        public short ObjectHover;
        public bool InteractionsAvailable;
        public UIInteractionQueue Queue;

        public bool LiveMode = true;
        public bool PanelActive = false;
        public UILotControlTouchHelper Touch;
        public UIArchTouchHelper ArchTouch;

        public int WallsMode = 1;

        private int OldMX;
        private int OldMY;
        private bool FoundMe; //if false and avatar changes, center. Should center on join lot.

        public bool RMBScroll;
        private bool DebugRightHeld; // SimsRedux: right button is opening the debug menu, don't scroll
        /// <summary>SimsRedux: Sims 3 style camera controls (WASD, Q/E, Z/X, drag to move).</summary>
        public UIRevampedCamera RevampedCamera;
        /// <summary>SimsRedux: Sims 1 style cutaway (whole floor), not Simitone's cut around the mouse. -mousecutaway turns the old way back on.</summary>
        public static bool ClassicCutaway => !(FSOEnvironment.Args ?? "").Contains("-mousecutaway");
        private int RMBScrollX;
        private int RMBScrollY;

        //1 = near, 0.5 = med, 0.25 = far
        //"target" because we rescale the game target to fit this zoom level.
        public float TargetZoom = 1; 

        // NOTE: Blocking dialog system assumes that nothing goes wrong with data transmission (which it shouldn't, because we're using TCP)
        // and that the code actually blocks further dialogs from appearing while waiting for a response.
        // If we are to implement controlling multiple sims, this must be changed.
        private UIMobileDialog BlockingDialog;
        private UINeighborhoodSelectionPanel TS1NeighSelector;
        private ulong LastDialogID;

        // SimsRedux: stuck-dialog guard. In The Sims 1 a question box pauses the whole game until
        // it's answered. If the box never appears (or vanished), the game sits paused with the Sim
        // frozen mid-action. If that lasts a couple of seconds, show the question again, and if
        // that doesn't work, answer it with the last button so the game carries on.
        private FSO.SimAntics.Model.VMDialogInfo LastBlockInfo;
        private int StuckFrames, StuckRetries;

        /// <summary>SimsRedux: when clicking a Sim shows no menu, notes who was clicked and by whom (pie_debug.log).</summary>
        private void PieLog(VMAvatar target)
        {
            try
            {
                Func<VMEntity, string> who = (e) =>
                {
                    var a = e as VMAvatar;
                    if (a == null) return e?.ToString() ?? "none";
                    return a.Name + " guid=" + a.Object.OBJ.GUID.ToString("X8")
                        + " type=" + a.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.PersonType)
                        + " greet=" + a.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.GreetStatus)
                        + " familyNo=" + a.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.TS1FamilyNumber)
                        + " age=" + a.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.PersonsAge)
                        + " pos=" + a.Position + " hidden=" + a.GetValue(FSO.SimAntics.Model.VMStackObjectVariable.Hidden)
                        + " semiglobal=" + (a.SemiGlobal?.Iff?.Filename ?? "none");
                };
                int count = target.TreeTable?.Interactions?.Length ?? -1;
                var path = System.IO.Path.Combine(FSO.Common.FSOEnvironment.UserDir, "pie_debug.log");
                FSOFile.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + " lot " + vm.GetGlobalValue(10) + " no menu for " + who(target)
                    + " (" + count + " interactions) | clicked by " + who(ActiveEntity) + "\r\n");
                if (PieTraced.Add(target.Object.OBJ.GUID)) FSOFile.AppendAllText(path, PieTrace(target));
            }
            catch { }
        }

        private static HashSet<uint> PieTraced = new HashSet<uint>();

        private static string PointerName(byte p)
        {
            if (p == 253) return "error";
            if (p == 254) return "true";
            if (p == 255) return "false";
            return p.ToString();
        }

        /// <summary>
        /// SimsRedux: runs each of the Sim's interactions' checks again with the script trace on, and
        /// writes why each one said no (the last steps of its check). Once per Sim per session.
        /// </summary>
        private string PieTrace(VMAvatar target)
        {
            var sb = new System.Text.StringBuilder();
            var caller = ActiveEntity;
            var ttab = target.TreeTable;
            if (caller == null || ttab == null) return "";
            var oldEntity = FSO.SimAntics.Engine.VMThread.TraceEntity;
            var oldOps = FSO.SimAntics.Engine.VMThread.TraceOperands;
            var oldRoutines = FSO.SimAntics.Engine.VMThread.TraceRoutines.ToList();
            FSO.SimAntics.Engine.VMThread.TraceRoutines.Clear();
            int detailed = 0;
            try
            {
                FSO.SimAntics.Engine.VMThread.TraceEntity = caller;
                FSO.SimAntics.Engine.VMThread.TraceOperands = true;
                foreach (var ia in ttab.Interactions)
                {
                    var id = (int)ia.TTAIndex;
                    string name = "?";
                    try { name = target.TreeTableStrings?.GetString(id) ?? "?"; } catch { }
                    var action = target.GetAction(id, caller, vm.Context, false);
                    if (action == null) { sb.Append("   - " + name + ": no action (missing tree)\r\n"); continue; }
                    FSO.SimAntics.Engine.VMThread.TraceLog.Clear();
                    caller.ObjectData[(int)FSO.SimAntics.Model.VMStackObjectVariable.HideInteraction] = 0;
                    var result = caller.Thread.CheckAction(action);
                    var hidden = caller.ObjectData[(int)FSO.SimAntics.Model.VMStackObjectVariable.HideInteraction];
                    var steps = FSO.SimAntics.Engine.VMThread.TraceLog.ToList();
                    sb.Append("   - " + name + ": " + (result != null ? "OK" + (hidden == 1 ? " but hidden" : "") : "no") + " flags=" + ia.Flags + " steps=" + steps.Count + "\r\n");
                    if (result == null && detailed < 8 && steps.Count > 0)
                    {
                        detailed++;
                        foreach (var line in steps.Skip(Math.Max(0, steps.Count - 25))) sb.Append("        " + line + "\r\n");
                    }
                }
            }
            catch (Exception e) { sb.Append("   trace failed: " + e.Message + "\r\n"); }
            try
            {
                // every script the checks went through, in full (so the whole rule can be read, not just the steps taken)
                sb.Append("   scripts used by the checks:\r\n");
                foreach (var routine in FSO.SimAntics.Engine.VMThread.TraceRoutines.OrderBy(x => x.ID).Take(60))
                {
                    sb.Append("     " + (routine.Chunk?.ChunkLabel ?? "?") + " (" + routine.ID + "), args " + routine.Arguments + ", locals " + routine.Locals + "\r\n");
                    var ins = routine.Instructions ?? new FSO.SimAntics.VMInstruction[0];
                    for (int i = 0; i < ins.Length; i++)
                    {
                        var x = ins[i];
                        sb.Append("       " + i + ": op " + x.Opcode + " T->" + PointerName(x.TruePointer) + " F->" + PointerName(x.FalsePointer)
                            + "  " + FSO.SimAntics.Engine.VMThread.DescribeOperand(x.Operand) + "\r\n");
                    }
                }
            }
            catch (Exception e) { sb.Append("   script dump failed: " + e.Message + "\r\n"); }
            finally
            {
                FSO.SimAntics.Engine.VMThread.TraceRoutines.Clear();
                foreach (var r in oldRoutines) FSO.SimAntics.Engine.VMThread.TraceRoutines.Add(r);
                FSO.SimAntics.Engine.VMThread.TraceEntity = oldEntity;
                FSO.SimAntics.Engine.VMThread.TraceOperands = oldOps;
                FSO.SimAntics.Engine.VMThread.TraceLog.Clear();
                caller.ObjectData[(int)FSO.SimAntics.Model.VMStackObjectVariable.HideInteraction] = 0;
            }
            return sb.ToString();
        }

        private static void DialogLog(string text)
        {
            try
            {
                var path = System.IO.Path.Combine(FSO.Common.FSOEnvironment.UserDir, "dialog_debug.log");
                FSOFile.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + " " + text + "\r\n");
            }
            catch { }
        }

        private void CheckStuckDialog()
        {
            if (vm.GlobalBlockingDialog != null && !OwnsCaller(vm.GlobalBlockingDialog)) { StuckFrames = 0; return; }
            if (!vm.TS1 || vm.GlobalBlockingDialog == null || BlockingDialog != null || ClassicBlocking != null || TS1NeighSelector != null)
            {
                StuckFrames = 0;
                if (vm.GlobalBlockingDialog == null) StuckRetries = 0;
                return;
            }
            if (++StuckFrames < 120) return; // about 2 seconds with nothing on screen
            StuckFrames = 0;
            var owner = vm.GlobalBlockingDialog;
            var waiting = owner.Thread?.BlockingState as FSO.SimAntics.Primitives.VMDialogResult;
            DialogLog("Game paused for a question that isn't on screen. Owner=" + owner.ObjectID + " waiting=" + (waiting != null) + " retries=" + StuckRetries
                + " last=" + (LastBlockInfo == null ? "none" : (LastBlockInfo.Title + " / " + LastBlockInfo.DialogID)));
            if (waiting != null && !waiting.Responded && LastBlockInfo != null && LastBlockInfo.Caller == owner && StuckRetries < 2)
            {
                StuckRetries++;
                LastDialogID = 0;
                vm_OnDialog(LastBlockInfo); // show the question again
            }
            else
            {
                // give up and answer it (last button = "no" / "cancel"), which also un-pauses the game
                StuckRetries = 0;
                byte code = 1;
                var type = LastBlockInfo?.Operand?.Type ?? VMDialogType.Message;
                if (type == VMDialogType.Message) code = 0;
                else if (type == VMDialogType.YesNoCancel) code = 2;
                DialogLog("Answering it with button " + code + " so the game can carry on.");
                vm.SendCommand(new VMNetDialogResponseCmd { ResponseCode = code, ResponseText = "" });
            }
        }

        private static uint GOTO_GUID = 0x000007C4;
        public VMEntity GotoObject;

        private Rectangle MouseCutRect = new Rectangle(-4, -4, 4, 4);
        private List<uint> CutRooms = new List<uint>();
        private HashSet<uint> LastCutRooms = new HashSet<uint>(); //final rooms, including those outside. used to detect dirty.
        public sbyte LastFloor = -1;
        public WorldRotation LastRotation = WorldRotation.TopLeft;
        private bool[] LastCuts; //cached roomcuts, to apply rect cut to.
        private int LastWallMode = -1; //invalidates last roomcuts
        private bool LastRectCutNotable = false; //set if the last rect cut made a noticable change to the cuts array. If true refresh regardless of new cut effect.

        public UIObjectHolder ObjectHolder;
        public UICustomLotControl CustomControl;
        public UIQueryPanel QueryPanel;
        public UIPickupPanel PickupPanel;

        /// <summary>
        /// Creates a new UILotControl instance.
        /// </summary>
        /// <param name="vm">A SimAntics VM instance.</param>
        /// <param name="World">A World instance.</param>
        public UILotControl(FSO.SimAntics.VM vm, FSO.LotView.World World)
        {
            this.vm = vm;
            this.World = World;
            
            MouseEvt = this.ListenForMouse(new Microsoft.Xna.Framework.Rectangle(0, 0,
                GlobalSettings.Default.GraphicsWidth, GlobalSettings.Default.GraphicsHeight), OnMouse);

            Queue = new UIInteractionQueue(ActiveEntity, vm);
            this.Add(Queue);

            ObjectHolder = new UIObjectHolder(vm, World, this);
            RevampedCamera = new UIRevampedCamera(this);
            RevampedCamera.OnRightClick = OnRightClickWorld;
            Touch = new UILotControlTouchHelper(this);
            Add(Touch);
            ArchTouch = new UIArchTouchHelper(this);
            Add(ArchTouch);
            SetupQuery();


            RMBCursor = GetTexture(0x24B00000001); //exploreanchor.bmp

            vm.OnDialog += vm_OnDialog;
            vm.OnBreakpoint += Vm_OnBreakpoint;
        }

        public void SetupQuery()
        {
            /*UIContainer parent = null;
            if (QueryPanel?.Parent?.Parent != null)
            {
                parent = QueryPanel.Parent;
            }*/

            QueryPanel = new UIQueryPanel(World);
            QueryPanel.X = 0;
            QueryPanel.Y = -114;

            PickupPanel = new UIPickupPanel();
            PickupPanel.OnResponse += (resp) =>
            {
                if (resp) ObjectHolder.SellBack(null);
                else ObjectHolder.Cancel();
            };
        }

        public override void GameResized()
        {
            base.GameResized();
            MouseEvt.Region.Width = GlobalSettings.Default.GraphicsWidth;
            MouseEvt.Region.Height = GlobalSettings.Default.GraphicsHeight;

            //SetupQuery();
        }


        private void Vm_OnBreakpoint(VMEntity entity)
        {
            if (IDEHook.IDE != null) IDEHook.IDE.IDEBreakpointHit(vm, entity);
        }

        public static bool ShowSimanticsExceptions = true;
        private static HashSet<string> SeenSimanticsErrors = new HashSet<string>();

        /// <summary>SimsRedux: groups script errors by the error + the interaction that caused it.</summary>
        private static string SimanticsErrorKey(string message)
        {
            if (message == null) return "";
            var lines = message.Split('\n');
            // key = the error itself + the script it happened in. The object/Sim name and how deep
            // the script was called are left out, so the same error from several objects pops up once.
            var parts = new List<string>();
            string lastFrame = "";
            bool seenName = false;
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("A SimAntics Exception") || t.StartsWith("been suppressed") || t.StartsWith("The object will be reset")) continue;
                if (t.StartsWith("(")) { seenName = true; continue; }
                if (t.StartsWith(">")) { if (t.Length > 1) lastFrame = t; continue; }
                if (!seenName) parts.Add(t);
            }
            parts.Add(lastFrame);
            return string.Join("|", parts);
        }
        void vm_OnDialog(FSO.SimAntics.Model.VMDialogInfo info)
        {
            // SimsRedux: the game's nag popups (Help System, expansion announcements) never show -
            // the game running the lot answers them at once; players on someone else's lot just skip them
            if (info != null && !info.Block && Simitone.Client.Utils.ReduxDialogs.IsTownLetter(info))
            {
                DialogLog("(town letter, not shown) " + info.Title);
                return;
            }
            var auto = Simitone.Client.Utils.ReduxDialogs.AutoAnswer(info);
            if (auto != null)
            {
                DialogLog("(answered automatically) " + info.Title);
                if (vm.GlobalLink != null) vm.SendCommand(new VMNetDialogResponseCmd { ResponseCode = auto.Value, ResponseText = "" });
                return;
            }
            // SimsRedux online: only show the questions for this player's own Sims
            if (info != null && !OwnsDialog(info))
            {
                DialogLog("(not mine, skipped) " + info.Title + " | block=" + info.Block + " caller=" + (info.Caller?.PersistID.ToString() ?? "none"));
                return;
            }
            if (info != null)
            {
                DialogLog("Dialog: " + info.Title + " | block=" + info.Block + " id=" + info.DialogID + " type=" + (info.Operand?.Type.ToString() ?? "-") + (info.DialogID == LastDialogID ? " (duplicate)" : ""));
                if (info.Block) { LastBlockInfo = info; }
            }
            else DialogLog("Dialog cleared");
            if (info != null && ((info.DialogID == LastDialogID && info.DialogID != 0 && info.Block))) return;
            //return if same dialog as before, or not ours
            if ((info == null || info.Block) && ClassicBlocking != null)
            {
                // SimsRedux: same for the classic popup
                ClassicBlocking.Close();
                ClassicBlocking = null;
                LastDialogID = 0;
            }
            if ((info == null || info.Block) && BlockingDialog != null)
            {
                //cancel current dialog because it's no longer valid
                UIScreen.RemoveDialog(BlockingDialog);
                LastDialogID = 0;
                BlockingDialog = null;
            }
            if (info == null) return; //return if we're just clearing a dialog.

            // SimsRedux online: a visiting player only sees questions about their own Sim; the lot
            // answers the others (the family living there, NPCs)
            var game = FSO.Client.UI.Framework.UIScreen.Current as Simitone.Client.UI.Screens.TS1GameScreen;
            // (2C: on your own family's lot, questions about any of your family come to you too)
            if (game != null && game.IsGuest && info.Caller is VMAvatar && info.Caller.PersistID != vm.MyUID
                && !(game.Guest.IsOwner && ((VMAvatar)info.Caller).GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.TS1FamilyNumber) == Simitone.Client.Utils.ReduxTown.MyFamily
                     && info.Caller.PersistID < Simitone.Client.Utils.ReduxOnlineInfo.FirstGuestID)) return;

            if (!ShowSimanticsExceptions && info.Title == "SimAntics Exception!")
                return;

            // SimsRedux: log every script error to a file, but only pop up each kind once per session
            if (info.Title == "SimAntics Exception!")
            {
                try
                {
                    var logPath = System.IO.Path.Combine(FSO.Common.FSOEnvironment.UserDir, "simantics_errors.log");
                    FSOFile.AppendAllText(logPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]\r\n" + info.Message + "\r\n\r\n");
                }
                catch { }
                var key = SimanticsErrorKey(info.Message);
                if (SeenSimanticsErrors.Contains(key)) return;
                SeenSimanticsErrors.Add(key);
            }

            // SimsRedux: in the classic interface, the game's questions use the original Sims 1 popup
            VMDialogType ctype = (info.Operand == null) ? VMDialogType.Message : info.Operand.Type;
            // SimsRedux: a plain notice (just an OK button) pops up in the top-right corner and the game
            // carries on - it's answered straight away, so nothing waits on it
            if (ctype == VMDialogType.Message)
            {
                Simitone.Client.UI.Controls.UIReduxToasts.Show(info.Title, info.Message);
                if (info.Block) ClassicResponse(0);
                DialogLog("(shown as a corner notice) " + info.Title);
                return;
            }
            if (UIClassicNeighborhoodBar.Enabled && (ctype == VMDialogType.Message || ctype == VMDialogType.YesNo || ctype == VMDialogType.YesNoCancel))
            {
                var block = info.Block;
                Func<byte, Action> answer = (code) => () => { if (block) ClassicResponse(code); };
                Tuple<string, Action>[] buttons;
                if (ctype == VMDialogType.YesNo)
                    buttons = new[] { Tuple.Create(info.Yes ?? "Yes", answer(0)), Tuple.Create(info.No ?? "No", answer(1)) };
                else if (ctype == VMDialogType.YesNoCancel)
                    buttons = new[] { Tuple.Create(info.Yes ?? "Yes", answer(0)), Tuple.Create(info.No ?? "No", answer(1)), Tuple.Create(info.Cancel ?? "Cancel", answer(2)) };
                else
                    buttons = new[] { Tuple.Create(string.IsNullOrEmpty(info.Yes) ? "OK" : info.Yes, answer(0)) };
                var classic = new UIClassicDialog(info.Title, info.Message, buttons);
                UIScreen.GlobalShowDialog(classic, true);
                if (block)
                {
                    ClassicBlocking = classic;
                    LastDialogID = info.DialogID;
                }
                return;
            }

            var options = new UIAlertOptions
            {
                Title = info.Title,
                Message = info.Message,
                Width = 325 + (int)(info.Message.Length / 3.5f),
                Alignment = TextAlignment.Left,
                TextSize = 12
            };

            var b0Event = (info.Block) ? new ButtonClickDelegate(DialogButton0) : null;
            var b1Event = (info.Block) ? new ButtonClickDelegate(DialogButton1) : null;
            var b2Event = (info.Block) ? new ButtonClickDelegate(DialogButton2) : null;

            VMDialogType type = (info.Operand == null) ? VMDialogType.Message : info.Operand.Type;

            switch (type)
            {
                default:
                case VMDialogType.Message:
                    options.Buttons = new UIAlertButton[] { new UIAlertButton(UIAlertButtonType.OK, b0Event, info.Yes) };
                    break;
                case VMDialogType.YesNo:
                    options.Buttons = new UIAlertButton[]
                    {
                        new UIAlertButton(UIAlertButtonType.Yes, b0Event, info.Yes),
                        new UIAlertButton(UIAlertButtonType.No, b1Event, info.No),
                    };
                    break;
                case VMDialogType.YesNoCancel:
                    options.Buttons = new UIAlertButton[]
                    {
                        new UIAlertButton(UIAlertButtonType.Yes, b0Event, info.Yes),
                        new UIAlertButton(UIAlertButtonType.No, b1Event, info.No),
                        new UIAlertButton(UIAlertButtonType.Cancel, b2Event, info.Cancel),
                    };
                    break;
                case VMDialogType.TextEntry:
                    options.Buttons = new UIAlertButton[] { new UIAlertButton(UIAlertButtonType.OK, b0Event, info.Yes) };
                    options.TextEntry = true;
                    break;
                case VMDialogType.NumericEntry:
                    if (!vm.TS1) goto case VMDialogType.TextEntry;
                    else goto case VMDialogType.TS1Neighborhood;
                case VMDialogType.TS1Vacation:
                case VMDialogType.TS1Neighborhood:
                case VMDialogType.TS1StudioTown:
                case VMDialogType.TS1Magictown:
                    TS1NeighSelector = new UINeighborhoodSelectionPanel((ushort)VMDialogPrivateStrings.TypeToNeighID[type]);
                    Parent.Add(TS1NeighSelector);
                    ((TS1GameScreen)Parent).Bg.Visible = true;
                    ((TS1GameScreen)Parent).LotControl.Visible = false;
                    TS1NeighSelector.OnHouseSelect += HouseSelected;
                    return;
                case VMDialogType.TS1PhoneBook:
                    {
                        // SimsRedux: the phone book must always be answered, or the Sim (and the whole
                        // game, which pauses for it) waits forever. If it can't even be shown, hang up.
                        var callerAva = info.Caller as VMAvatar;
                        var callerUID = info.Caller?.PersistID ?? vm.MyUID;
                        UICallNeighborAlert phone = null;
                        try
                        {
                            short callerNID = (callerAva == null) ? (short)0 : callerAva.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.NeighborId);
                            bool taxi = callerAva != null && game != null && game.CanCallTaxi;
                            phone = new UICallNeighborAlert(callerNID, vm, taxi);
                        }
                        catch (Exception e)
                        {
                            DialogLog("Phone book couldn't be shown (" + e.Message + "), hanging up.");
                            phone = null;
                        }
                        if (phone == null)
                        {
                            vm.SendCommand(new VMNetDialogResponseCmd { ActorUID = callerUID, ResponseCode = 0, ResponseText = "" });
                            return;
                        }
                        BlockingDialog = phone;
                        LastDialogID = info.DialogID;
                        UIScreen.GlobalShowDialog(phone, true);
                        phone.OnResult += (result) =>
                        {
                            // a neighbour's id, or (cancel) no number at all: the phone book's "cancelled" exit
                            vm.SendCommand(new VMNetDialogResponseCmd
                            {
                                ActorUID = callerUID,
                                ResponseCode = (byte)((result > 0) ? 1 : 0),
                                ResponseText = (result > 0) ? result.ToString() : ""
                            });
                            if (BlockingDialog == phone) BlockingDialog = null;
                            LastDialogID = 0;
                        };
                        phone.OnTaxi += () =>
                        {
                            // hang up the phone book, then pick where the taxi goes
                            vm.SendCommand(new VMNetDialogResponseCmd { ActorUID = callerUID, ResponseCode = 0, ResponseText = "" });
                            if (BlockingDialog == phone) BlockingDialog = null;
                            LastDialogID = 0;
                            ShowTaxiPicker(callerAva);
                        };
                        return;
                    }

                case VMDialogType.TS1PetChoice:
                case VMDialogType.TS1Clothes:
                    var ts1categories = new string[] { "b", "f", "s", "l", "w", "h" };
                    var pet = type == VMDialogType.TS1PetChoice;
                    var stackObj = info.Caller.Thread.Stack.Last().StackObject;
                    
                    var skin = new UISelectSkinAlert(pet?null:(info.Caller as VMAvatar), pet?((stackObj as VMAvatar).IsCat?"cat":"dog"):ts1categories[info.Caller.Thread.TempRegisters[0]], vm);
                    BlockingDialog = skin;
                    UIScreen.GlobalShowDialog(skin, true);
                    skin.OnResult += (result) =>
                    {
                        vm.SendCommand(new VMNetDialogResponseCmd
                        {
                            ActorUID = info.Caller.PersistID,
                            ResponseCode = (byte)((result > -1)?1:0),
                            ResponseText = result.ToString()
                        });
                        BlockingDialog = null;
                    };
                    return;
            }

            var alert = new UIMobileAlert(options);

            UIScreen.GlobalShowDialog(alert, true);

            if (info.Block)
            {
                BlockingDialog = alert;
                LastDialogID = info.DialogID;
            }

            var entity = info.Icon;
            if (entity is VMGameObject)
            {
                var objects = entity.MultitileGroup.Objects;
                ObjectComponent[] objComps = new ObjectComponent[objects.Count];
                for (int i = 0; i < objects.Count; i++)
                {
                    objComps[i] = (ObjectComponent)objects[i].WorldUI;
                }
                var thumb = World.GetObjectThumb(objComps, entity.MultitileGroup.GetBasePositions(), GameFacade.GraphicsDevice);
                alert.SetIcon(thumb, 256, 256);
            }
        }

        private void HouseSelected(int house)
        {
            if (ActiveEntity == null || TS1NeighSelector == null) return;
            vm.SendCommand(new VMNetDialogResponseCmd
            {
                ActorUID = ActiveEntity.PersistID,
                ResponseCode = (byte)((house > 0) ? 1 : 0),
                ResponseText = house.ToString()
            });
            Parent.Remove(TS1NeighSelector);
            TS1NeighSelector = null;
        }

        /// <summary>
        /// SimsRedux: "Call Taxi" from the phone (single player only): pick a lot, then the Sim
        /// (and their family on this lot) travel there the same way as a trip Downtown.
        /// </summary>
        private void ShowTaxiPicker(VMAvatar caller)
        {
            var game = Parent as TS1GameScreen;
            if (game == null || caller == null || !game.CanCallTaxi)
            {
                UIClassicDialog.Ok("Call Taxi", "There are no taxis here right now.");
                return;
            }
            List<UICallTaxiAlert.Destination> places;
            try { places = game.TaxiDestinations(); }
            catch (Exception e)
            {
                DialogLog("Taxi: couldn't list the lots: " + e.Message);
                places = new List<UICallTaxiAlert.Destination>();
            }
            if (places.Count == 0)
            {
                UIClassicDialog.Ok("Call Taxi", "There's nowhere for a taxi to take you right now.");
                return;
            }
            var picker = new UICallTaxiAlert(places);
            UIScreen.GlobalShowDialog(picker, true);
            picker.OnPick += (house) =>
            {
                if (!game.TaxiTo(house, caller))
                    UIClassicDialog.Ok("Call Taxi", "The taxi couldn't take you there right now.");
            };
        }

        // SimsRedux: the classic popup currently waiting for an answer
        private UIClassicDialog ClassicBlocking;

        private void ClassicResponse(byte code)
        {
            DialogLog("Answered (classic) with button " + code);
            ClassicBlocking = null;
            LastDialogID = 0;
            vm.SendCommand(new VMNetDialogResponseCmd { ResponseCode = code, ResponseText = "" });
        }

        private void DialogButton0(UIElement button) { DialogResponse(0); }
        private void DialogButton1(UIElement button) { DialogResponse(1); }
        private void DialogButton2(UIElement button) { DialogResponse(2); }

        private void DialogResponse(byte code)
        {
            DialogLog("Answered with button " + code);
            if (BlockingDialog == null || !(BlockingDialog is UIMobileAlert)) return;
            BlockingDialog.Close();
            var ma = (UIMobileAlert)BlockingDialog;
            LastDialogID = 0;
            vm.SendCommand(new VMNetDialogResponseCmd
            {
                ResponseCode = code,
                ResponseText = (ma.ResponseText == null) ? "" : ma.ResponseText
            });
            BlockingDialog = null;
        }

        // SimsRedux touch with the Redux layout: Buy and Build with a finger
        private int TouchHoldID = -1;     // the finger moving the held object
        private Point TouchHoldStart;
        private DateTime TouchHoldTime;
        private float TouchHoldMoved;
        private int TouchToolID = -1;     // the finger drawing with a build tool
        /// <summary>2 = press the held object down next update, 1 = let go of it (it's placed).</summary>
        public int PendingPlace;

        private static Point FingerOf(UpdateState state, int id)
        {
            var m = state.MouseStates.FirstOrDefault(x => x.ID == id);
            return m != null ? new Point(m.MouseState.X, m.MouseState.Y) : new Point(state.MouseState.X, state.MouseState.Y);
        }

        /// <summary>Place the held object where it is now (the touch Place button, or a tap).</summary>
        public void TouchPlace() { if (ObjectHolder.Holding != null && PendingPlace == 0) PendingPlace = 2; }

        /// <summary>Opens the Tint window for the object at this point on the screen (touch: a long press).</summary>
        public void OpenTintAt(Point screen) { OnRightClickWorld(screen); }

        private void OnMouse(UIMouseEventType type, UpdateState state)
        {
            if (!vm.Ready) return;

            if (type == UIMouseEventType.MouseOver)
            {
                if (QueryPanel.Mode == 1) QueryPanel.SetShown(false);
                MouseIsOn = true;
            }
            else if (type == UIMouseEventType.MouseOut)
            {
                MouseIsOn = false;
                GameFacade.Cursor.SetCursor(CursorType.Normal);
                Tooltip = null;
            }
            else if (type == UIMouseEventType.MouseDown)
            {
                if (!FSOEnvironment.SoftwareKeyboard)
                {
                    if (!LiveMode)
                    {
                        if (CustomControl != null) CustomControl.MouseDown(state);
                        else ObjectHolder.MouseDown(state);
                        return;
                    }
                }
                if (Simitone.Client.Utils.ReduxTouch.ReduxLayout && !LiveMode)
                {
                    var id = state.CurrentMouseID;
                    var p = FingerOf(state, id);
                    if (CustomControl != null)
                    {
                        // a build tool: the finger is the mouse (draw walls, lay floors...); two fingers move the camera
                        if (TouchToolID == -1 && Touch.MiceDown.Count == 0)
                        {
                            TouchToolID = id;
                            var wp = ToWorldScreen(new Vector2(p.X, p.Y));
                            CustomControl.MousePosition = new Point((int)wp.X, (int)wp.Y);
                            CustomControl.MouseDown(state);
                            return;
                        }
                    }
                    else if (ObjectHolder.Holding != null)
                    {
                        // holding something: one finger moves it (a tap puts it down there); a second finger moves the camera
                        if (TouchHoldID == -1 && Touch.MiceDown.Count == 0)
                        {
                            TouchHoldID = id;
                            TouchHoldStart = p;
                            TouchHoldTime = DateTime.UtcNow;
                            TouchHoldMoved = 0;
                            ObjectHolder.TouchPoint = p;
                            return;
                        }
                        if (TouchHoldID != -1) { Touch.MiceDown.Add(TouchHoldID); TouchHoldID = -1; }
                    }
                }
                Touch.MiceDown.Add(state.CurrentMouseID);
            }
            else if (type == UIMouseEventType.MouseUp)
            {
                if (state.CurrentMouseID == TouchToolID)
                {
                    TouchToolID = -1;
                    if (CustomControl != null) CustomControl.MouseUp(state);
                    return;
                }
                if (state.CurrentMouseID == TouchHoldID)
                {
                    TouchHoldID = -1;
                    // a tap (not a drag): put it down right there
                    if (TouchHoldMoved < 24 && (DateTime.UtcNow - TouchHoldTime).TotalSeconds < 0.5) TouchPlace();
                    return;
                }
                Touch.MiceDown.Remove(state.CurrentMouseID);
                if (!FSOEnvironment.SoftwareKeyboard)
                {
                    if (!LiveMode)
                    {
                        if (CustomControl != null) CustomControl.MouseUp(state);
                        else ObjectHolder.MouseUp(state);
                        return;
                    }
                }
                state.UIState.TooltipProperties.Show = false;
                state.UIState.TooltipProperties.Opacity = 0;
                ShowTooltip = false;
                TipIsError = false;
            }
        }

        public void SimulateMD(UpdateState state)
        {
            if (CustomControl != null) CustomControl.MouseDown(state);
            else ObjectHolder.MouseDown(state);
        }

        public void SimulateMU(UpdateState state)
        {
            if (CustomControl != null) CustomControl.MouseUp(state);
            else ObjectHolder.MouseUp(state);
        }

        public void AddModifier(UILotControlModifiers mod)
        {
            if (CustomControl != null)
                CustomControl.Modifiers |= mod;
        }

        public void RemoveModifier(UILotControlModifiers mod)
        {
            if (CustomControl != null)
                CustomControl.Modifiers &= ~mod;
        }

        public void ShowPieMenu(Point pt, UpdateState state)
        {
            // SimsRedux: just watching a background family - they do their own thing
            if ((FSO.Client.UI.Framework.UIScreen.Current as Simitone.Client.UI.Screens.TS1GameScreen)?.Watching == true) return;
            if (!LiveMode)
            {
                /*
                if (CustomControl != null) CustomControl.MouseDown(state);
                else ObjectHolder.MouseDown(state);
                */
                if (FSOEnvironment.SoftwareKeyboard && ObjectHolder.Holding == null)
                {
                    if (Simitone.Client.Utils.ReduxTouch.ReduxLayout) ObjectHolder.TouchPoint = new Point(state.MouseState.X, state.MouseState.Y); // (the finger, on the screen)
                    ObjectHolder.MouseDown(state);
                }
                return;
            }
            if (PieMenu == null && ActiveEntity != null)
            {
                if (GotoObject == null) return; // the lot hasn't run a frame yet
                VMEntity obj;
                //get new pie menu, make new pie menu panel for it
                var tilePos = World.EstTileAtPosWithScroll(new Vector2(pt.X, pt.Y) / FSOEnvironment.DPIScaleFactor);

                LotTilePos targetPos = LotTilePos.FromBigTile((short)tilePos.X, (short)tilePos.Y, World.State.Level);
                if (vm.Context.SolidToAvatars(targetPos).Solid) targetPos = LotTilePos.OUT_OF_WORLD;

                GotoObject.SetPosition(targetPos, Direction.NORTH, vm.Context);

                var newHover = World.GetObjectIDAtScreenPos(pt.X,
                    pt.Y,
                    GameFacade.GraphicsDevice);

                ObjectHover = newHover;

                bool objSelected = ObjectHover > 0;
                if (objSelected || (GotoObject.Position != LotTilePos.OUT_OF_WORLD && ObjectHover <= 0))
                {
                    if (objSelected)
                    {
                        obj = vm.GetObjectById(ObjectHover);
                    }
                    else
                    {
                        obj = GotoObject;
                    }
                    // (SimsRedux online: only ever a Sim of your own family - never another player's)
                    if (obj is VMAvatar && state.CtrlDown && CanTakeControl((VMAvatar)obj))
                    {
                        //debug switch to avatar
                        vm.SendCommand(new VMNetChangeControlCmd()
                        {
                            TargetID = obj.ObjectID
                        });
                    }
                    else if (obj != null)
                    {
                        obj = obj.MultitileGroup.GetInteractionGroupLeader(obj);
                        if (obj is VMGameObject && ((VMGameObject)obj).Disabled > 0)
                        {
                            var flags = ((VMGameObject)obj).Disabled;

                            if ((flags & VMGameObjectDisableFlags.ForSale) > 0)
                            {
                                //for sale
                                var retailPrice = obj.MultitileGroup.Price; //wrong... should get this from catalog
                                var salePrice = obj.MultitileGroup.SalePrice;
                                ShowErrorTooltip(state, 22, true, "$" + retailPrice.ToString("##,#0"), "$" + salePrice.ToString("##,#0"));
                            }
                            else if ((flags & VMGameObjectDisableFlags.LotCategoryWrong) > 0)
                                ShowErrorTooltip(state, 21, true); //category wrong
                            else if ((flags & VMGameObjectDisableFlags.TransactionIncomplete) > 0)
                                ShowErrorTooltip(state, 27, true); //transaction not yet complete
                            else if ((flags & VMGameObjectDisableFlags.ObjectLimitExceeded) > 0)
                                ShowErrorTooltip(state, 24, true); //object is temporarily disabled... todo: something more helpful
                            else if ((flags & VMGameObjectDisableFlags.PendingRoommateDeletion) > 0)
                                ShowErrorTooltip(state, 16, true); //pending roommate deletion
                        }
                        else
                        {
                            var menu = obj.GetPieMenu(vm, ActiveEntity, false, true);
                            if (menu.Count == 0 && obj is VMAvatar) PieLog((VMAvatar)obj);
                            if (menu.Count != 0)
                            {
                                HITVM.Get().PlaySoundEvent(UISounds.PieMenuAppear);
                                PieMenu = new UIPieMenu(menu, obj, ActiveEntity, this);
                                this.Add(PieMenu);
                                PieMenu.X = state.MouseState.X / FSOEnvironment.DPIScaleFactor;
                                PieMenu.Y = state.MouseState.Y / FSOEnvironment.DPIScaleFactor;
                                PieMenu.UpdateHeadPosition(state.MouseState.X, state.MouseState.Y);
                            }
                        }
                    }

                }
                else
                {
                    ShowErrorTooltip(state, 0, true);
                }
            }
            else
            {
                if (PieMenu != null) PieMenu.RemoveSimScene();
                this.Remove(PieMenu);
                PieMenu = null;
            }
        }

        private void ShowErrorTooltip(UpdateState state, uint id, bool playSound, params string[] args)
        {
            if (playSound) HITVM.Get().PlaySoundEvent(UISounds.Error);
            state.UIState.TooltipProperties.Show = true;
            state.UIState.TooltipProperties.Color = Color.Black;
            state.UIState.TooltipProperties.Opacity = 1;
            state.UIState.TooltipProperties.Position = new Vector2(state.MouseState.X,
                state.MouseState.Y);
            state.UIState.Tooltip = GameFacade.Strings.GetString("159", id.ToString(), args);
            state.UIState.TooltipProperties.UpdateDead = false;
            ShowTooltip = true;
            TipIsError = true;
        }

        private int AutoSelectCooldown;
        /// <summary>SimsRedux: takes control of the game's selected Sim, or the first family member on the lot.</summary>
        /// <summary>
        /// SimsRedux online: is this Sim (or object) one this player should answer for? Offline,
        /// always. On a shared lot: your own Sim, and for the host also the family's other Sims
        /// and the game's own questions (no Sim attached).
        /// </summary>
        private bool OwnsCaller(VMEntity caller)
        {
            var screen = Parent as TS1GameScreen;
            bool online = screen != null && (screen.Host != null || screen.IsGuest);
            if (!online) return true;
            bool isHost = !screen.IsVisitor; // (2C: the family's owner answers for their home lot)
            // only friends' own Sims belong to them; everything else (the host's family, objects
            // like the Help System, the game itself) is the host's to answer
            if (caller is VMAvatar && caller.PersistID == vm.MyUID) return true;
            if (caller is VMAvatar && caller.PersistID >= ReduxOnlineInfo.FirstGuestID) return false;
            return isHost;
        }

        private bool OwnsDialog(FSO.SimAntics.Model.VMDialogInfo info)
        {
            return OwnsCaller(info.Caller);
        }

        /// <summary>
        /// SimsRedux online: may this player switch to controlling this Sim? Offline, anyone (as
        /// before). Online, only a Sim of the lot's family you play (host / family owner) - never a
        /// visitor's Sim, and a visitor never switches at all.
        /// </summary>
        public bool CanTakeControl(VMAvatar ava)
        {
            if (ava == null || ava.Dead) return false;
            var screen = Parent as TS1GameScreen;
            if (screen == null || (screen.Host == null && !screen.IsGuest))
            {
                // SimsRedux: offline too, only your own family's Sims - never shop staff, townies or visitors
                // (you talk to them and use their services; you don't take them over)
                var fam = vm.TS1State?.CurrentFamily;
                if (fam == null) return true; // (no family playing this lot: as before)
                return ava.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.TS1FamilyNumber) == fam.ChunkID
                    || (fam.FamilyGUIDs != null && fam.FamilyGUIDs.Contains(ava.Object.OBJ.GUID));
            }
            if (screen.IsVisitor || screen.Watching) return false;
            if (ava.PersistID >= ReduxOnlineInfo.FirstGuestID && ava.PersistID != vm.MyUID) return false;
            if ((ava.GetValue(FSO.SimAntics.Model.VMStackObjectVariable.MovementFlags) & (short)VMMovementFlags.FSOExcludeUserSave) != 0) return false; // a visiting Sim
            var family = vm.TS1State?.CurrentFamily;
            return family?.RuntimeSubset != null && family.RuntimeSubset.Contains(ava.Object.OBJ.GUID);
        }

        /// <summary>SimsRedux: leaving the lot also closes its open questions (they'd answer a lot that's gone).</summary>
        public override void Removed()
        {
            base.Removed();
            if (BlockingDialog != null) { UIScreen.RemoveDialog(BlockingDialog); BlockingDialog = null; }
            if (ClassicBlocking != null) { ClassicBlocking.Close(); ClassicBlocking = null; }
            if (TS1NeighSelector != null) { TS1NeighSelector.Parent?.Remove(TS1NeighSelector); TS1NeighSelector = null; }
            LastDialogID = 0;
        }

        private void TryAutoSelectSim()
        {
            // never on a lot run by the server: its own copy of the game must not take a Sim away
            // from the player controlling it (it did, once a second - the flickering Sim)
            if (Simitone.Client.Utils.ReduxWorker.Active) return;
            if ((Parent as TS1GameScreen)?.IsVisitor == true) return; // a visitor only ever has their own Sim
            if (AutoSelectCooldown > 0) { AutoSelectCooldown--; return; }
            AutoSelectCooldown = 60; // try about once a second, not every frame
            try
            {
                var family = vm.TS1State?.CurrentFamily;
                if (family == null) return;
                var members = vm.Context.ObjectQueries.Avatars
                    .Where(x => !x.Dead && family.RuntimeSubset.Contains(x.Object.OBJ.GUID))
                    .ToList();
                if (members.Count == 0) return;
                var selected = vm.GetObjectById(vm.GetGlobalValue(3));
                var target = (selected != null && members.Contains(selected)) ? selected : members[0];
                vm.SendCommand(new VMNetChangeControlCmd() { TargetID = target.ObjectID });
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Couldn't select a Sim: " + e.Message);
            }
        }

        /// <summary>
        /// SimsRedux: when zoomed between the normal zoom steps, the lot is drawn scaled around the
        /// screen centre. Mouse positions have to be scaled back the same way before asking the world
        /// what's under the mouse, or clicks land up and to the left of where you clicked.
        /// (Simitone's pie menu already did this; picking up objects and hovering didn't.)
        /// </summary>
        /// <summary>
        /// SimsRedux: right-clicking an object in Buy or Build mode opens its colour picker
        /// (other colours to swap to, and Tint).
        /// </summary>
        private void OnRightClickWorld(Point screen)
        {
            try
            {
                if (LiveMode || ObjectHolder.Holding != null) return;
                // (one Tint window at a time - also while it's stepped aside for its colour dropper)
                if (CustomControl is Simitone.Client.UI.Panels.Desktop.UIReduxDropper
                    || Simitone.Client.UI.Panels.Desktop.UIClassicTintDialog.Active != null) return;
                var game = Parent as TS1GameScreen;
                if (game == null || vm == null) return;
                var pt = ToWorldScreen(new Vector2(screen.X, screen.Y));
                var id = World.GetObjectIDAtScreenPos((int)pt.X, (int)pt.Y, GameFacade.GraphicsDevice);
                if (id <= 0) return;
                var obj = vm.GetObjectById(id) as VMGameObject;
                if (obj == null) return;
                // SimsRedux: right-click goes straight to Tint (the whole object takes a new colour)
                var lead = obj.MultitileGroup?.BaseObject as VMGameObject ?? obj;
                UIScreen.GlobalShowDialog(new Simitone.Client.UI.Panels.Desktop.UIClassicTintDialog(game, lead.ObjectID), true);
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Right-click colours: " + e.Message);
            }
        }

        public Vector2 ToWorldScreen(Vector2 mouse)
        {
            var scale = World?.BackbufferScale ?? 1f;
            if (scale <= 0) scale = 1f;
            var mid = new Vector2(UIScreen.Current.ScreenWidth / 2f, UIScreen.Current.ScreenHeight / 2f);
            return (mouse - mid) / scale + mid;
        }

        public void ClosePie()
        {
            if (PieMenu != null)
            {
                PieMenu.RemoveSimScene();
                Queue.PieMenuClickPos = PieMenu.Position;
                this.Remove(PieMenu);
                PieMenu = null;
            }
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(0, 0, GlobalSettings.Default.GraphicsWidth, GlobalSettings.Default.GraphicsHeight);
        }

        public void LiveModeUpdate(UpdateState state, bool scrolled)
        {
            if (MouseIsOn && !RMBScroll && ActiveEntity != null && !FSOEnvironment.SoftwareKeyboard)
            {

                if (state.MouseState.X != OldMX || state.MouseState.Y != OldMY)
                {
                    OldMX = state.MouseState.X;
                    OldMY = state.MouseState.Y;
                    var hoverPt = ToWorldScreen(new Vector2(state.MouseState.X, state.MouseState.Y));
                    var newHover = World.GetObjectIDAtScreenPos((int)hoverPt.X,
                        (int)hoverPt.Y,
                        GameFacade.GraphicsDevice);

                    if (ObjectHover != newHover)
                    {
                        ObjectHover = newHover;
                        if (ObjectHover > 0)
                        {
                            var obj = vm.GetObjectById(ObjectHover);
                            if (obj != null)
                            {
                                var menu = obj.GetPieMenu(vm, ActiveEntity, false, true);
                                InteractionsAvailable = (menu.Count > 0);
                            }
                        }
                    }

                    if (!TipIsError) ShowTooltip = false;
                    if (ObjectHover > 0)
                    {
                        var obj = vm.GetObjectById(ObjectHover);
                        if (!TipIsError && obj != null)
                        {
                            if (obj is VMAvatar)
                            {
                                state.UIState.TooltipProperties.Show = true;
                                state.UIState.TooltipProperties.Color = Color.Black;
                                state.UIState.TooltipProperties.Opacity = 1;
                                state.UIState.TooltipProperties.Position = new Vector2(state.MouseState.X,
                                    state.MouseState.Y);
                                state.UIState.Tooltip = GetAvatarString(obj as VMAvatar);
                                state.UIState.TooltipProperties.UpdateDead = false;
                                ShowTooltip = true;
                            }
                            else if (((VMGameObject)obj).Disabled > 0)
                            {
                                var flags = ((VMGameObject)obj).Disabled;
                                if ((flags & VMGameObjectDisableFlags.ForSale) > 0)
                                {
                                    //for sale
                                    //try to get catalog price
                                    var guid = obj.MasterDefinition?.GUID ?? obj.Object.OBJ.GUID;
                                    var item = Content.Get().WorldCatalog.GetItemByGUID(guid);

                                    var retailPrice = (int?)(item?.Price) ?? obj.MultitileGroup.Price;
                                    var salePrice = obj.MultitileGroup.SalePrice;
                                    ShowErrorTooltip(state, 22, false, "$" + retailPrice.ToString("##,#0"), "$" + salePrice.ToString("##,#0"));
                                    TipIsError = false;
                                }
                            }

                        }
                    }
                    if (!ShowTooltip)
                    {
                        state.UIState.TooltipProperties.Show = false;
                        state.UIState.TooltipProperties.Opacity = 0;
                    }
                }
            }
            else
            {
                ObjectHover = 0;
            }

            if (!scrolled)
            { //set cursor depending on interaction availability
                CursorType cursor;

                if (PieMenu == null && MouseIsOn)
                {
                    if (ObjectHover == 0)
                    {
                        cursor = CursorType.LiveNothing;
                    }
                    else
                    {
                        if (InteractionsAvailable)
                        {
                            if (vm.GetObjectById(ObjectHover) is VMAvatar) cursor = CursorType.LivePerson;
                            else cursor = CursorType.LiveObjectAvail;
                        }
                        else
                        {
                            cursor = CursorType.LiveObjectUnavail;
                        }
                    }
                }
                else
                {

                    cursor = CursorType.Normal;
                }

                CursorManager.INSTANCE.SetCursor(cursor);
            }

        }

        private string GetAvatarString(VMAvatar ava)
        {
            return ava.ToString();
        }

        public void RefreshCut()
        {
            LastFloor = -1;
            LastWallMode = -1;

            if (vm.Context.Blueprint != null && LastCuts != null)
            {
                vm.Context.Blueprint.Cutaway = LastCuts;
                vm.Context.Blueprint.Changes.SetFlag(BlueprintGlobalChanges.WALL_CUT_CHANGED);
            }

            //MouseCutRect = new Rectangle(0,0,0,0);
        }

        public void SetTargetZoom(WorldZoom zoom)
        {
            switch (zoom)
            {
                case WorldZoom.Near:
                    TargetZoom = 1f; break;
                case WorldZoom.Medium:
                    TargetZoom = 0.5f; break;
                case WorldZoom.Far:
                    TargetZoom = 0.25f; break;
            }
            LastZoom = World.State.Zoom;
        }

        public override void Draw(UISpriteBatch batch)
        {
            //DrawLocalTexture(batch, World.State.Light.LightMap, new Rectangle(0,0, World.State.Light.LightMap.Width/3, World.State.Light.LightMap.Height/2), new Vector2());
            if (RMBScroll && !UIRevampedCamera.Enabled)
            {
                DrawLocalTexture(batch, RMBCursor, new Vector2(RMBScrollX - RMBCursor.Width / 2, RMBScrollY - RMBCursor.Height / 2));
            }
            base.Draw(batch);
        }

        private WorldZoom LastZoom;
        /// <summary>SimsRedux: frames this control has run, and how far (for the online debug log).</summary>
        public int DebugUpdates, DebugFullUpdates;

        public override void Update(UpdateState state)
        {
            base.Update(state);
            DebugUpdates++;

            if (!vm.Ready || vm.Context.Architecture == null) return;
            DebugFullUpdates++;

            CheckStuckDialog();

            //handling smooth scaled zoom
            var camType = World.State.Cameras.ActiveType;
            Touch._3D = camType != FSO.LotView.Utils.Camera.CameraControllerType._2D;
            if (World.State.Cameras.ActiveType == FSO.LotView.Utils.Camera.CameraControllerType._3D)
            {
                if (World.BackbufferScale != 1) World.BackbufferScale = 1;
                var s3d = World.State.Cameras.Camera3D;
                if (TargetZoom < -0.25f)
                {
                    TargetZoom -= (TargetZoom - 0.25f) * (1f - (float)Math.Pow(0.975f, 60f / FSOEnvironment.RefreshRate));
                }
                s3d.Zoom3D += ((9.75f - (TargetZoom - 0.25f) * 5.7f) - s3d.Zoom3D) / 10;
            }
            else if (World.State.Cameras.ActiveType == FSO.LotView.Utils.Camera.CameraControllerType._2D)
            {
                if (World.State.Zoom != LastZoom)
                {
                    //zoom has been changed by something else. inherit the value
                    SetTargetZoom(World.State.Zoom);
                    LastZoom = World.State.Zoom;
                }

                float BaseScale;
                WorldZoom targetZoom;
                if (TargetZoom < 0.5f)
                {
                    targetZoom = WorldZoom.Far;
                    BaseScale = 0.25f;
                }
                else if (TargetZoom < 1f)
                {
                    targetZoom = WorldZoom.Medium;
                    BaseScale = 0.5f;
                }
                else
                {
                    targetZoom = WorldZoom.Near;
                    BaseScale = 1f;
                }
                World.BackbufferScale = TargetZoom / BaseScale;
                if (World.State.Zoom != targetZoom) World.State.Zoom = targetZoom;
                WorldConfig.Current.SmoothZoom = false;
            }
            
            if (ActiveEntity == null || ActiveEntity.Dead || ActiveEntity.PersistID != SelectedSimID)
            {
                var before = ActiveEntity;
                ActiveEntity = vm.Entities.FirstOrDefault(x => x is VMAvatar && x.PersistID == SelectedSimID); //try and hook onto a sim if we have none selected.
                // SimsRedux online: note every change of who you're controlling (and why), to find Sims that flicker
                if (before != ActiveEntity && vm.GlobalLink == null && ControlChanges++ < 200)
                    Simitone.Client.Utils.ReduxOnlineInfo.Log("[control] " + (before == null ? "none" : before.Name + " (id " + before.PersistID + (before.Dead ? ", gone" : "") + ")")
                        + " -> " + (ActiveEntity == null ? "none" : ActiveEntity.Name) + " | I'm " + SelectedSimID + " | selected-Sim setting " + vm.GetGlobalValue(3) + " | lot refreshes " + RefreshTally);
                //if (ActiveEntity == null) ActiveEntity = vm.Entities.FirstOrDefault(x => x is VMAvatar);

                // (a Sim who is off the lot - at work, say - has nowhere to look at: the lot is shown instead)
                if (!FoundMe && !LotMiddleShown && ActiveEntity != null && ActiveEntity.Position != LotTilePos.OUT_OF_WORLD)
                {
                    vm.Context.World.State.CenterTile = new Vector2(ActiveEntity.VisualPosition.X, ActiveEntity.VisualPosition.Y);
                    vm.Context.World.State.ScrollAnchor = null;
                    FoundMe = true;
                }
                Queue.QueueOwner = ActiveEntity;
            }

            // SimsRedux: never leave the camera looking at nothing - if there's still no Sim to look at
            // a few seconds after arriving, look at the middle of the lot
            if (!FoundMe)
            {
                if (CameraWaitStart == default) CameraWaitStart = DateTime.UtcNow;
                else if ((DateTime.UtcNow - CameraWaitStart).TotalSeconds > 3 && !LotMiddleShown)
                {
                    LotMiddleShown = true;
                    var size = vm.Context.Architecture.Width;
                    vm.Context.World.State.CenterTile = new Vector2(size / 2f, size / 2f);
                    vm.Context.World.State.ScrollAnchor = null;
                    Simitone.Client.Utils.ReduxOnlineInfo.Log("[camera] no Sim to look at yet - showing the middle of the lot. "
                        + string.Join("; ", vm.Context.ObjectQueries.Avatars.OfType<VMAvatar>().Select(a => a.Name + " id " + a.PersistID + (a.Position == LotTilePos.OUT_OF_WORLD ? " (away)" : " at " + a.Position.TileX + "," + a.Position.TileY)))
                        + " | I'm " + SelectedSimID);
                }
            }

            // SimsRedux: no Sim is under the player's control (this can happen with a save where the
            // "selected Sim" got lost). Without one, clicking Sims and objects does nothing, so pick one.
            if (ActiveEntity == null) TryAutoSelectSim();

            if (GotoObject == null) GotoObject = vm.Context.CreateObjectInstance(GOTO_GUID, LotTilePos.OUT_OF_WORLD, Direction.NORTH, true).Objects[0];


            //update plumbbob
            // SimsRedux: the classic green Sims plumbbob (falls back to Simitone's arrow)
            var plumb = Content.Get().RCMeshes.Get("plumbbob.fsom") ?? Content.Get().RCMeshes.Get("arrow.fsom");
            foreach (VMAvatar avatar in vm.Context.ObjectQueries.Avatars)
            {
                if (avatar.Avatar == null) continue;
                var isActive = (avatar == ActiveEntity);
                if ((avatar.Avatar.HeadObject == plumb) != isActive)
                {
                    avatar.Avatar.HeadObject = (avatar == ActiveEntity) ? plumb : null;
                    avatar.Avatar.HeadObjectSpeedyVel = 0.2f;
                }
                avatar.Avatar.HeadObjectRotation += 3f / FSOEnvironment.RefreshRate;
                if (isActive)
                {
                    avatar.Avatar.HeadObjectRotation += avatar.Avatar.HeadObjectSpeedyVel;
                    avatar.Avatar.HeadObjectSpeedyVel *= 0.98f;
                } else if (avatar.GetValue(FSO.SimAntics.Model.VMStackObjectVariable.Category) == 87)
                {
                    avatar.Avatar.HeadObject = Content.Get().RCMeshes.Get("star.fsom");
                }
            }
            /*
            if (ActiveEntity != null && BlockingDialog != null)
            {
                //are we still waiting on a blocking dialog? if not, cancel.
                if (ActiveEntity.Thread != null && (ActiveEntity.Thread.BlockingState == null || !(ActiveEntity.Thread.BlockingState is VMDialogResult)))
                {
                    BlockingDialog.Close();
                    LastDialogID = 0;
                    BlockingDialog = null;
                }
            }*/

            if (Visible)
            {
                if (ShowTooltip) state.UIState.TooltipProperties.UpdateDead = false;

                bool scrolled = false;

                World.State.Cameras.CameraFirstPerson.CaptureMouse = true;

                // SimsRedux debug menu: Ctrl + Left Shift + right-click a Sim (after the "debugredux" cheat)
                bool rightDown = state.MouseState.RightButton == ButtonState.Pressed;
                if (!rightDown) DebugRightHeld = false;
                else if (!DebugRightHeld && !RMBScroll && MouseIsOn && Simitone.Client.Utils.ReduxDebug.Enabled
                    && state.CtrlDown && state.KeyboardState.IsKeyDown(Keys.LeftShift))
                {
                    DebugRightHeld = true;
                    var dbgPt = ToWorldScreen(new Vector2(state.MouseState.X, state.MouseState.Y));
                    var dbgId = World.GetObjectIDAtScreenPos((int)dbgPt.X, (int)dbgPt.Y, GameFacade.GraphicsDevice);
                    var dbgObj = dbgId > 0 ? vm.GetObjectById(dbgId) : null;
                    if (dbgObj is VMAvatar dbgSim)
                        Simitone.Client.Utils.ReduxDebug.ShowMenu(vm, dbgSim);
                    else if (dbgObj is VMGameObject dbgThing)
                        Simitone.Client.Utils.ReduxDebug.ShowObjectMenu(vm, dbgThing);
                    else
                        HITVM.Get().PlaySoundEvent(UISounds.Error);
                }

                if (DebugRightHeld)
                {
                    RMBScroll = false;
                }
                else if (FSOEnvironment.SoftwareKeyboard)
                {
                    // SimsRedux touch controls: the camera is the touch version (UIReduxTouchCamera,
                    // run by the touch helper); the mouse and keyboard camera stays out of it
                    RMBScroll = false;
                }
                else if (UIRevampedCamera.Enabled)
                {
                    scrolled = RevampedCamera.Update(state, MouseIsOn);
                    RMBScroll = RevampedCamera.Dragging; // hides hover highlights while dragging, like before
                }
                else
                {
                    if (RMBScroll)
                    {
                        World.State.ScrollAnchor = null;
                        Vector2 scrollBy = new Vector2();
                        if (state.TouchMode)
                        {
                            scrollBy = new Vector2(RMBScrollX - state.MouseState.X, RMBScrollY - state.MouseState.Y);
                            RMBScrollX = state.MouseState.X;
                            RMBScrollY = state.MouseState.Y;
                            scrollBy /= 128f;
                            scrollBy /= FSOEnvironment.DPIScaleFactor;
                        }
                        else
                        {
                            scrollBy = new Vector2(state.MouseState.X - RMBScrollX, state.MouseState.Y - RMBScrollY);
                            scrollBy *= 0.0005f;

                            var angle = (Math.Atan2(state.MouseState.X - RMBScrollX, (RMBScrollY - state.MouseState.Y) * 2) / Math.PI) * 4;
                            angle += 8;
                            angle %= 8;

                            CursorType type = CursorType.ArrowUp;
                            switch ((int)Math.Round(angle))
                            {
                                case 0: type = CursorType.ArrowUp; break;
                                case 1: type = CursorType.ArrowUpRight; break;
                                case 2: type = CursorType.ArrowRight; break;
                                case 3: type = CursorType.ArrowDownRight; break;
                                case 4: type = CursorType.ArrowDown; break;
                                case 5: type = CursorType.ArrowDownLeft; break;
                                case 6: type = CursorType.ArrowLeft; break;
                                case 7: type = CursorType.ArrowUpLeft; break;
                            }
                            GameFacade.Cursor.SetCursor(type);
                        }
                        World.Scroll(scrollBy * (60f / FSOEnvironment.RefreshRate));
                        scrolled = true;
                    }
                    if (MouseIsOn)
                    {
                        if (state.MouseState.RightButton == ButtonState.Pressed)
                        {
                            if (RMBScroll == false)
                            {
                                RMBScroll = true;
                                RMBScrollX = state.MouseState.X;
                                RMBScrollY = state.MouseState.Y;
                            }
                        }
                        else
                        {
                            if (!scrolled && GlobalSettings.Default.EdgeScroll && !state.TouchMode) scrolled = World.TestScroll(state);
                        }
                    }

                    if (state.MouseState.RightButton != ButtonState.Pressed)
                    {
                        if (RMBScroll) GameFacade.Cursor.SetCursor(CursorType.Normal);
                        RMBScroll = false;
                    }
                }

                if (!LiveMode && PieMenu != null)
                {
                    PieMenu.RemoveSimScene();
                    this.Remove(PieMenu);
                    PieMenu = null;
                }

                if (state.NewKeys.Contains(Keys.F11))
                {
                    var utils = new FSO.SimAntics.Test.CollisionTestUtils();
                    utils.VerifyAllCollision(vm);
                }

                if (state.NewKeys.Contains(Keys.F8))
                {
                    UIMobileAlert alert = null;
                    alert = new UIMobileAlert(new UIAlertOptions()
                    {
                        Title = "Debug Lot Thumbnail",
                        Message = "Arch Value: "+VMArchitectureStats.GetArchValue(vm.Context.Architecture),
                        Buttons = UIAlertButton.Ok((btn) => UIScreen.RemoveDialog(alert))
                    });
                    Texture2D roofless = null;
                    var thumb = World.GetLotThumb(GameFacade.GraphicsDevice, (tex) => roofless = FSO.Common.Utils.TextureUtils.Decimate(tex, GameFacade.GraphicsDevice, 2, false));
                    thumb = FSO.Common.Utils.TextureUtils.Decimate(thumb, GameFacade.GraphicsDevice, 2, false);
                    alert.SetIcon(thumb, thumb.Width, thumb.Height);
                    UIScreen.GlobalShowDialog(alert, true);
                }
                if (LiveMode) LiveModeUpdate(state, scrolled);
                else if (CustomControl != null)
                {
                    if (Simitone.Client.Utils.ReduxTouch.ReduxLayout)
                    {
                        // the Redux layout on touch: the tool follows the finger
                        CustomControl.Modifiers = 0;
                        var fp = TouchToolID != -1 ? FingerOf(state, TouchToolID) : new Point(state.MouseState.X, state.MouseState.Y);
                        var wp = ToWorldScreen(new Vector2(fp.X, fp.Y));
                        CustomControl.MousePosition = new Point((int)wp.X, (int)wp.Y);
                    }
                    else if (FSOEnvironment.SoftwareKeyboard) CustomControl.MousePosition = new Point(UIScreen.Current.ScreenWidth / 2, UIScreen.Current.ScreenHeight / 2);
                    else
                    {
                        CustomControl.Modifiers = 0;
                        if (state.CtrlDown) CustomControl.Modifiers |= UILotControlModifiers.CTRL;
                        if (state.ShiftDown) CustomControl.Modifiers |= UILotControlModifiers.SHIFT;
                        // SimsRedux: build tools get the zoom-corrected mouse position, so walls, floors
                        // and terrain land under the cursor at every zoom level
                        var wp = ToWorldScreen(new Vector2(state.MouseState.X, state.MouseState.Y));
                        CustomControl.MousePosition = new Point((int)wp.X, (int)wp.Y);
                    }
                    CustomControl.Update(state, scrolled);
                }
                else
                {
                    if (Simitone.Client.Utils.ReduxTouch.ReduxLayout)
                    {
                        // the finger moving the held object
                        if (TouchHoldID != -1 && ObjectHolder.Holding != null)
                        {
                            var fp = FingerOf(state, TouchHoldID);
                            TouchHoldMoved = Math.Max(TouchHoldMoved, (fp - TouchHoldStart).ToVector2().Length());
                            ObjectHolder.TouchPoint = fp;
                        }
                        // a tap or the Place button: press, then let go on the next update (like a click)
                        if (PendingPlace == 1) { ObjectHolder.MouseUp(state); PendingPlace = 0; }
                        else if (PendingPlace == 2) { if (ObjectHolder.Holding != null) { ObjectHolder.MouseDown(state); PendingPlace = 1; } else PendingPlace = 0; }
                    }
                    ObjectHolder.Update(state, scrolled);
                }

                //set cutaway around mouse
                UpdateCutaway(state);
                UpdateDoorHiding();

                if (RMBScrollX == int.MinValue) Dummy(); //cannon fodder for mono AOT compilation: never called but gives these constructors a meaning in life
            }
        }

        private void Dummy()
        {
            CustomControl = new UIWallPlacer(vm, World, this, new List<int>());
            CustomControl = new UIFloorPainter(vm, World, this, new List<int>());
            CustomControl = new UIWallPainter(vm, World, this, new List<int>());
            CustomControl = new UIGrassPaint(vm, World, this, new List<int>());
            CustomControl = new UIRoofer(vm, World, this, new List<int>());
            CustomControl = new UITerrainFlatten(vm, World, this, new List<int>());
            CustomControl = new UITerrainRaiser(vm, World, this, new List<int>());
        }

        // SimsRedux: doors. Walls up and roof: every door shows. Walls down: only doors that lead
        // outside show (inside doors would stand up like walls in the middle of rooms). Cutaway: a
        // door in a wall that's cut away is hidden with its wall.
        private int ControlChanges;
        /// <summary>How many times the whole lot has been re-sent to us (each one rebuilds every Sim).</summary>
        public static int RefreshTally;
        private DateTime CameraWaitStart;
        private bool LotMiddleShown;

        private HashSet<VMEntity> HiddenDoors = new HashSet<VMEntity>();
        private int DoorCheckTimer;
        private int LastDoorWallsMode = -1;
        private void UpdateDoorHiding()
        {
            if (vm?.Context == null) return;
            var cutDoors = World?.State?.Platform is FSO.LotView.Platform.WorldPlatform2D ? vm.Context.Blueprint?.WallComp?.CutDoorTiles : null;
            bool cutChanged = WallsMode == 1 && !ReferenceEquals(cutDoors, LastCutDoors);
            if (LastDoorWallsMode == WallsMode && !cutChanged && DoorCheckTimer-- > 0) return;
            DoorCheckTimer = 30; // about twice a second: catches doors that were just placed
            LastCutDoors = cutDoors;
            LastDoorWallsMode = WallsMode;

            int doors = 0, hidden = 0, outside = 0;
            foreach (var ent in vm.Entities)
            {
                if (!(ent is VMGameObject) || ent.Dead) continue;
                var flags2 = (VMEntityFlags2)ent.GetValue(FSO.SimAntics.Model.VMStackObjectVariable.FlagField2);
                if ((flags2 & VMEntityFlags2.ArchitectualDoor) == 0) continue;
                var comp = ent.WorldUI as ObjectComponent;
                if (comp == null) continue;
                bool leadsOut = DoorLeadsOutside(ent);
                bool hide = (WallsMode == 0 && !leadsOut) || (WallsMode == 1 && DoorIsCut(ent, cutDoors));
                doors++; if (hide) hidden++; if (leadsOut) outside++;
                if (hide)
                {
                    if (comp.Visible) comp.Visible = false;
                    HiddenDoors.Add(ent);
                }
                else if (HiddenDoors.Remove(ent))
                {
                    comp.Visible = ent.GetValue(FSO.SimAntics.Model.VMStackObjectVariable.Hidden) == 0;
                }
            }
            // online: say what happened when the walls mode changes (finding doors that stay up)
            if (WallsMode != LoggedDoorMode && (Parent as TS1GameScreen)?.IsGuest == true)
            {
                LoggedDoorMode = WallsMode;
                Simitone.Client.Utils.ReduxOnlineInfo.Log("[doors] walls mode " + WallsMode + ": " + doors + " doors, " + hidden + " hidden, " + outside + " lead outside, "
                    + vm.Context.RoomInfo.Length + " rooms, cut doors " + (cutDoors?.Count ?? -1));
            }
        }
        private int LoggedDoorMode = -1;

        private HashSet<int> LastCutDoors;

        /// <summary>Is the wall this door sits in cut away right now (cutaway mode)?</summary>
        private bool DoorIsCut(VMEntity door, HashSet<int> cutDoors)
        {
            if (cutDoors == null || cutDoors.Count == 0) return false;
            var pos = door.Position;
            if (pos == LotTilePos.OUT_OF_WORLD) return false;
            int dx = 0, dy = 0;
            switch (door.Direction)
            {
                case Direction.NORTH: dy = -1; break;
                case Direction.SOUTH: dy = 1; break;
                case Direction.EAST: dx = 1; break;
                case Direction.WEST: dx = -1; break;
            }
            // the wall belongs to the door's tile or the one next to it, front or back
            foreach (var step in new[] { 0, 1, -1 })
            {
                int x = pos.TileX + dx * step, y = pos.TileY + dy * step;
                if (x < 0 || y < 0 || x > 255 || y > 255) continue;
                if (cutDoors.Contains((pos.Level << 16) | (y << 8) | x)) return true;
            }
            return false;
        }

        /// <summary>Is there outdoors on either side of this door?</summary>
        private bool DoorLeadsOutside(VMEntity door)
        {
            var pos = door.Position;
            if (pos == LotTilePos.OUT_OF_WORLD) return false;
            int dx = 0, dy = 0;
            switch (door.Direction)
            {
                case Direction.NORTH: dy = -1; break;
                case Direction.SOUTH: dy = 1; break;
                case Direction.EAST: dx = 1; break;
                case Direction.WEST: dx = -1; break;
            }
            foreach (var step in new[] { 0, 1, -1 })
            {
                var x = pos.TileX + dx * step;
                var y = pos.TileY + dy * step;
                if (x < 0 || y < 0 || x >= vm.Context.Architecture.Width || y >= vm.Context.Architecture.Height) return true;
                var room = vm.Context.GetRoomAt(LotTilePos.FromBigTile((short)x, (short)y, pos.Level));
                if (room < vm.Context.RoomInfo.Length && vm.Context.RoomInfo[room].Room.IsOutside) return true;
            }
            return false;
        }

        private void UpdateCutaway(UpdateState state)
        {
            if (vm.Context.Blueprint != null)
            {
                World.State.DynamicCutaway = (WallsMode == 1);
                //first we need to cycle the rooms that are being cutaway. Keep this up even if we're in all-cut mode.
                var mouseTilePos = World.EstTileAtPosWithScroll(new Vector2(state.MouseState.X, state.MouseState.Y) / FSOEnvironment.DPIScaleFactor);
                var roomHover = vm.Context.GetRoomAt(LotTilePos.FromBigTile((short)(mouseTilePos.X), (short)(mouseTilePos.Y), World.State.Level));
                var outside = (vm.Context.RoomInfo[roomHover].Room.IsOutside);
                if (!outside && !CutRooms.Contains(roomHover))
                    CutRooms.Add(roomHover); //outside hover should not persist like with other rooms.
                while (CutRooms.Count > 3) CutRooms.Remove(CutRooms.ElementAt(0));

                if (LastWallMode != WallsMode)
                {
                    if (WallsMode == 0) //walls down
                    {
                        LastCuts = new bool[vm.Context.Architecture.Width * vm.Context.Architecture.Height];
                        vm.Context.Blueprint.Cutaway = LastCuts;
                        vm.Context.Blueprint.Changes.SetFlag(BlueprintGlobalChanges.WALL_CUT_CHANGED);
                        for (int i = 0; i < LastCuts.Length; i++) LastCuts[i] = true;
                    }
                    else if (WallsMode == 1)
                    {
                        MouseCutRect = new Rectangle();
                        LastCutRooms = new HashSet<uint>() { uint.MaxValue }; //must regenerate cuts
                    }
                    else //walls up or roof
                    {
                        LastCuts = new bool[vm.Context.Architecture.Width * vm.Context.Architecture.Height];
                        vm.Context.Blueprint.Cutaway = LastCuts;
                        vm.Context.Blueprint.Changes.SetFlag(BlueprintGlobalChanges.WALL_CUT_CHANGED);
                    }
                    LastWallMode = WallsMode;
                }

                if (WallsMode == 1)
                {
                    HashSet<uint> finalRooms;
                    int recut = 0;
                    // SimsRedux: cut the front walls of every room on this floor, like Sims 1, instead of
                    // cutting a hole around the mouse (that's what made walls vanish as the mouse moved)
                    if (FSOEnvironment.SoftwareKeyboard || ClassicCutaway)
                    {
                        MouseCutRect = new Rectangle();
                        finalRooms = new HashSet<uint>();
                        foreach (var room in vm.Context.RoomInfo)
                        {
                            if (!room.Room.IsOutside && room.Room.Floor == World.State.Level-1) finalRooms.Add(room.Room.RoomID);
                        }
                    }
                    else
                    {
                        if (RMBScroll || !MouseIsOn) return;
                        finalRooms = new HashSet<uint>(CutRooms);
                        var newCut = new Rectangle((int)(mouseTilePos.X - 2.5), (int)(mouseTilePos.Y - 2.5), 5, 5);
                        newCut.X -= VMArchitectureTools.CutCheckDir[(int)World.State.CutRotation][0] * 2;
                        newCut.Y -= VMArchitectureTools.CutCheckDir[(int)World.State.CutRotation][1] * 2;
                        if (newCut != MouseCutRect)
                        {
                            MouseCutRect = newCut;
                            recut = 1;
                        }
                    }

                    if (LastFloor != World.State.Level || LastRotation != World.State.CutRotation || !finalRooms.SetEquals(LastCutRooms))
                    {
                        LastCuts = VMArchitectureTools.GenerateRoomCut(vm.Context.Architecture, World.State.Level, World.State.CutRotation, finalRooms);
                        recut = 2;
                        LastFloor = World.State.Level;
                        LastRotation = World.State.CutRotation;
                    }
                    LastCutRooms = finalRooms;

                    if (recut > 0)
                    {
                        var finalCut = new bool[LastCuts.Length];
                        Array.Copy(LastCuts, finalCut, LastCuts.Length);
                        var notableChange = VMArchitectureTools.ApplyCutRectangle(vm.Context.Architecture, World.State.Level, finalCut, MouseCutRect);
                        if (recut > 1 || notableChange || LastRectCutNotable)
                        {
                            vm.Context.Blueprint.Cutaway = finalCut;
                            vm.Context.Blueprint.Changes.SetFlag(BlueprintGlobalChanges.WALL_CUT_CHANGED);
                        }
                        LastRectCutNotable = notableChange;
                    }
                }
            }
        }
    }
}
