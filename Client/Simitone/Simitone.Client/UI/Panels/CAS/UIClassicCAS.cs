using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.LotView.Model;
using Simitone.Client.UI.Model;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels.CAS
{
    /// <summary>
    /// SimsRedux: Classic (original Sims 1 style) Create-a-Family / Create-a-Sim screens,
    /// built from the player's own UIGraphics.far art.
    ///
    /// PHASE 2: wired to TS1CASScreen. Adds/edits/deletes family members, shows a live
    /// 3D preview of the sim, and saves the family to the neighbourhood.
    ///
    /// All positions are in the original 800x600 layout and the whole panel is centred.
    /// </summary>
    public class UIClassicCAS : UIContainer
    {
        private TS1CASScreen Screen;

        // ---- shared ----
        private Texture2D FamilyBkg;
        private Texture2D SimBkg;
        private bool SimPage;
        private bool PickPage;

        // ---- pick a family page (PickBkg) ----
        private Texture2D PickBkg, PickRowBkg;
        private UILabel PickTitle;
        private UITS1Button PickCancelBtn, PickAddBtn, PickDeleteBtn, PickMoveInBtn;
        private List<FAMI> Families = new List<FAMI>();
        private List<PickRow> Rows = new List<PickRow>();
        private int PickSelection = -1;
        private int PickScroll = 0;
        private int LastWheel = int.MinValue;
        private const int RowX = 42, RowY = 76, RowW = 690, RowH = 68, VisibleRows = 6;

        private class PickRow
        {
            public FAMI Family;
            public string Name;
            public List<Texture2D> Faces = new List<Texture2D>();
        }

        // ---- family page ----
        private UITextBox FamilyName;
        private UITS1Button AddBtn, EditBtn, DeleteBtn, FamDoneBtn, FamCancelBtn;
        private List<UIPreviewSim> MemberSims = new List<UIPreviewSim>();
        private List<UILabel> MemberLabels = new List<UILabel>();
        private int SelectedMember = -1;
        private int ShownMemberCount = -1;
        public const int MaxFamily = 8;

        // family member grid (inside the big dark box of DsgnFamBkg: 75,100 - 585,410)
        private const int GridX = 85, GridY = 106, SlotW = 122, SlotH = 150, Cols = 4;
        private static readonly Vector2 MemberSimSize = new Vector2(110, 150);

        // ---- sim page ----
        private UITextBox SimName;
        private UITextEdit Bio;
        private UITS1Button ChildBtn, AdultBtn, MaleBtn, FemaleBtn;
        private UITS1Button LightBtn, MediumBtn, DarkBtn;
        private UITS1Button HeadLeft, HeadRight, BodyLeft, BodyRight;
        private UITS1Button SimDoneBtn, SimCancelBtn;
        private Texture2D PersLED, PointsLED;
        private UILabel[] TraitLabels = new UILabel[5];
        private UIPreviewSim Preview;
        private int EditIndex = -1;

        public static readonly string[] TraitNames = { "Neat", "Outgoing", "Active", "Playful", "Nice" };
        public int[] Personality = new int[5];
        public const int MaxPoints = 25;

        public bool Male = true;
        public bool Child = false;
        public string Skin = "lgt";
        public int HeadIndex, BodyIndex;

        // Personality LED slots (measured from CreateACharBack)
        private const int LedX = 228;
        private const int LedWidth = 60;       // 10 LEDs
        private static readonly int[] LedY = { 132, 164, 196, 228, 260 };
        private const int LedHeight = 23;
        private const int PointsX = 118, PointsY = 341;

        // Preview box (inside the light panel of CreateACharBack: 573,117 - 755,380)
        private static readonly Vector2 PreviewPos = new Vector2(573, 117);
        private static readonly Vector2 PreviewSize = new Vector2(182, 263);

        public UIClassicCAS(TS1CASScreen screen)
        {
            Screen = screen;
            FamilyBkg = TS1UIGraphics.Get("Nbhd/DsgnFamBkg.bmp");
            SimBkg = TS1UIGraphics.Get("Nbhd/CreateACharBack.bmp");
            PersLED = TS1UIGraphics.Get("Nbhd/DesignCharPersLED.bmp");
            PointsLED = TS1UIGraphics.Get("Nbhd/CACPointRemainingBars.bmp");

            BuildPickPage();
            BuildFamilyPage();
            BuildSimPage();
            SetPage(false);
            Recentre();
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private UITS1Button Btn(string path, int x, int y, int frames = 4, bool cutout = false)
        {
            var tex = cutout ? TS1UIGraphics.GetCutout(path, frames) : TS1UIGraphics.Get(path);
            var btn = new UITS1Button(tex, frames);
            btn.Position = new Vector2(x, y);
            Add(btn);
            return btn;
        }

        private UITextBox NameBox(int x, int y, int w, int h)
        {
            var box = new UITextBox();
            box.Position = new Vector2(x, y);
            box.SetSize(w, h);
            box.Alignment = TextAlignment.Center;
            box.TextStyle = box.TextStyle.Clone();
            box.TextStyle.Color = Color.White;
            box.TextStyle.Size = 14;
            Add(box);
            return box;
        }

        private UILabel Label(string text, int x, int y, int size)
        {
            var label = new UILabel();
            label.Caption = text;
            label.Position = new Vector2(x, y);
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = Color.White;
            Add(label);
            return label;
        }

        // ------------------------------------------------------------------
        // Pick a Family page
        // ------------------------------------------------------------------

        private UITS1Button AutoBtn(string path, int x, int y)
        {
            var tex = TS1UIGraphics.Get(path);
            var btn = new UITS1Button(tex, TS1UIGraphics.GuessFrames(tex));
            btn.Position = new Vector2(x, y);
            Add(btn);
            return btn;
        }

        private void BuildPickPage()
        {
            PickBkg = TS1UIGraphics.Get("Nbhd/PickBkg.bmp");
            PickRowBkg = TS1UIGraphics.Get("Nbhd/FamilyPickItemBkg.bmp");

            PickTitle = Label("SELECT A FAMILY", 0, 10, 20);
            PickTitle.Size = new Vector2(800, 30);
            PickTitle.Alignment = TextAlignment.Center;

            PickCancelBtn = Btn("Nbhd/PAFCancel.bmp", 142, 535, 6, true); // 6-frame sheet with a baked-in background
            PickAddBtn = AutoBtn("Nbhd/PAFAddFamily.bmp", 325, 532);
            PickDeleteBtn = AutoBtn("Nbhd/PAFDeleteFamily.bmp", 415, 532);
            PickMoveInBtn = AutoBtn("Nbhd/PAFMoveIn.bmp", 507, 532);

            PickCancelBtn.OnButtonClick += (b) => Screen.ClassicBackToNeighborhood();
            PickAddBtn.OnButtonClick += (b) => Screen.SetMode(UICASMode.FamilyEdit);
            PickDeleteBtn.OnButtonClick += (b) =>
            {
                if (PickSelection >= 0 && PickSelection < Families.Count)
                    Screen.ClassicDeleteFamily(Families[PickSelection]);
            };
            PickMoveInBtn.OnButtonClick += (b) =>
            {
                if (PickSelection >= 0 && PickSelection < Families.Count)
                    Screen.ClassicMoveIn(Families[PickSelection]);
            };

            ListenForMouse(new Rectangle(RowX, RowY, RowW, RowH * VisibleRows), OnPickMouse);
        }

        /// <summary>Called by TS1CASScreen with the families that can move in.</summary>
        public void UpdateFamilies(List<FAMI> families, VM vm)
        {
            Families = families;
            Rows.Clear();
            foreach (var family in families)
            {
                var row = new PickRow() { Family = family };
                var fams = family.ChunkParent.Get<FAMs>(family.ChunkID);
                row.Name = FamilyName_(fams, family);
                if (vm != null)
                {
                    foreach (var guid in family.FamilyGUIDs)
                    {
                        try
                        {
                            var sim = vm.Context.CreateObjectInstance(guid, LotTilePos.OUT_OF_WORLD, Direction.NORTH).BaseObject;
                            sim.Tick();
                            row.Faces.Add(UIIconCache.GetObject(sim));
                            sim.Delete(true, vm.Context);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("[SimsRedux] Couldn't load family face: " + e.Message);
                        }
                    }
                }
                Rows.Add(row);
            }
            PickSelection = -1;
            PickScroll = 0;
        }

        private static string FamilyName_(FAMs fams, FAMI family)
        {
            string name = null;
            if (fams != null)
            {
                name = fams.GetString(0);
                if (string.IsNullOrWhiteSpace(name))
                {
                    // fall back to any language that has the name
                    foreach (var set in fams.LanguageSets)
                    {
                        if (set?.Strings != null && set.Strings.Length > 0 && !string.IsNullOrWhiteSpace(set.Strings[0]?.Value))
                        {
                            name = set.Strings[0].Value;
                            break;
                        }
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(name)) name = "Family " + family.FamilyNumber;
            return name;
        }

        /// <summary>Called by TS1CASScreen when the family picker opens.</summary>
        public void ShowPickPage()
        {
            PickSelection = -1;
            PickPage = true;
            SimPage = false;
            ApplyVisibility();
        }

        private void OnPickMouse(UIMouseEventType type, UpdateState state)
        {
            // the three click areas (pick list, family grid, personality LEDs) overlap and only one
            // of them gets each click, so whichever gets it passes it on to the page that's showing
            if (!PickPage) { if (SimPage) OnLedMouse(type, state); else OnFamilyMouse(type, state); return; }
            if (!PickPage || type != UIMouseEventType.MouseDown) return;
            var pos = GetMousePosition(state.MouseState);
            int index = (int)((pos.Y - RowY) / RowH) + PickScroll;
            PickSelection = (index >= 0 && index < Rows.Count) ? index : -1;
        }

        private void UpdatePickPage(UpdateState state)
        {
            PickMoveInBtn.Disabled = PickSelection < 0;
            PickDeleteBtn.Disabled = PickSelection < 0;

            // mouse wheel scrolls the list when there are more than 6 families
            int wheel = state.MouseState.ScrollWheelValue;
            if (LastWheel != int.MinValue && wheel != LastWheel)
            {
                PickScroll += (wheel < LastWheel) ? 1 : -1;
            }
            LastWheel = wheel;
            PickScroll = Math.Max(0, Math.Min(Math.Max(0, Rows.Count - VisibleRows), PickScroll));
        }

        private void DrawPickRows(UISpriteBatch batch)
        {
            for (int i = 0; i < VisibleRows; i++)
            {
                int index = i + PickScroll;
                int y = RowY + i * RowH;
                int state = (index == PickSelection) ? 2 : 0;
                // row background sheet has 3 states stacked vertically
                int rowH = PickRowBkg.Height / 3;
                DrawLocalTexture(batch, PickRowBkg, new Rectangle(0, state * rowH, PickRowBkg.Width, rowH), new Vector2(RowX, y));

                if (index >= Rows.Count) continue;
                var row = Rows[index];
                var fam = row.Family;

                DrawText(batch, row.Name, new Vector2(RowX + 18, y + 4), 14);
                DrawText(batch, "\u00A7" + fam.Budget.ToString("N0"), new Vector2(RowX + 545, y + 26), 12);
                DrawText(batch, fam.FamilyFriends.ToString(), new Vector2(RowX + 625, y + 26), 12);

                for (int f = 0; f < row.Faces.Count; f++)
                {
                    var face = row.Faces[f];
                    if (face == null) continue;
                    var pos = new Vector2(RowX + 109 + f * 48, y + 20);
                    float scale = 42f / Math.Max(1, Math.Max(face.Width, face.Height));
                    DrawLocalTexture(batch, face, null, pos, new Vector2(scale, scale));
                }
            }
        }

        private Dictionary<int, TextStyle> TextStyles = new Dictionary<int, TextStyle>();
        private void DrawText(UISpriteBatch batch, string text, Vector2 pos, int size)
        {
            TextStyle style;
            if (!TextStyles.TryGetValue(size, out style))
            {
                style = PickTitle.CaptionStyle.Clone();
                style.Size = size;
                style.Color = Color.White;
                TextStyles[size] = style;
            }
            DrawLocalString(batch, text, pos, style);
        }

        private void BuildFamilyPage()
        {
            FamilyName = NameBox(272, 50, 258, 28);

            AddBtn = Btn("Nbhd/AddPersonBtn.bmp", 597, 103);
            DeleteBtn = Btn("Nbhd/DeletePersonBtn.bmp", 597, 210);
            EditBtn = Btn("Nbhd/EditPersonBtn.bmp", 597, 312);

            FamCancelBtn = Btn("Nbhd/DesignFamCancel.bmp", 420, 535, 6, true);
            FamDoneBtn = Btn("Nbhd/DesignFamDone.bmp", 610, 535, 6, true);

            AddBtn.OnButtonClick += (b) => OpenSimEditor(-1);
            EditBtn.OnButtonClick += (b) => { if (SelectedMember >= 0) OpenSimEditor(SelectedMember); };
            DeleteBtn.OnButtonClick += (b) =>
            {
                if (SelectedMember < 0) return;
                Screen.ClassicDeleteMember(SelectedMember);
                SelectedMember = -1;
                ShownMemberCount = -1; // rebuild on next update
            };
            FamCancelBtn.OnButtonClick += (b) => Screen.ClassicCancelFamily();
            FamDoneBtn.OnButtonClick += (b) => Screen.ClassicSaveFamily(FamilyName.CurrentText.Trim());

            ListenForMouse(new Rectangle(GridX, GridY, SlotW * Cols, SlotH * 2), OnFamilyMouse);
        }

        private void BuildSimPage()
        {
            SimName = NameBox(272, 50, 258, 28);

            // circle widget: age on top, skin in the middle row, gender on the bottom
            ChildBtn = Btn("Nbhd/DesignCharChildBtn.bmp", 405, 108);
            AdultBtn = Btn("Nbhd/DesignCharAdultBtn.bmp", 500, 102);
            LightBtn = Btn("Nbhd/DesignCharLightBtn.bmp", 400, 202);
            MediumBtn = Btn("Nbhd/DesignCharMediumBtn.bmp", 454, 202);
            DarkBtn = Btn("Nbhd/DesignCharDarkBtn.bmp", 500, 202);
            MaleBtn = Btn("Nbhd/DesignCharMaleBtn.bmp", 400, 275);
            FemaleBtn = Btn("Nbhd/DesignCharFemaleBtn.bmp", 500, 275);

            ChildBtn.OnButtonClick += (b) => ChangeType(Male, true, Skin);
            AdultBtn.OnButtonClick += (b) => ChangeType(Male, false, Skin);
            MaleBtn.OnButtonClick += (b) => ChangeType(true, Child, Skin);
            FemaleBtn.OnButtonClick += (b) => ChangeType(false, Child, Skin);
            LightBtn.OnButtonClick += (b) => ChangeType(Male, Child, "lgt");
            MediumBtn.OnButtonClick += (b) => ChangeType(Male, Child, "med");
            DarkBtn.OnButtonClick += (b) => ChangeType(Male, Child, "drk");

            // the 3D preview of the sim
            Preview = new UIPreviewSim();
            Preview.Position = PreviewPos;
            Preview.Size = PreviewSize;
            Preview.AutoRotate = true;
            Add(Preview);

            // head / body arrows around the preview box (added after the preview so they draw on top)
            HeadLeft = Btn("Nbhd/DesignCharSkinsLeftBtn.bmp", 580, 130);
            HeadRight = Btn("Nbhd/DesignCharSkinsRightBtn.bmp", 730, 130);
            BodyLeft = Btn("Nbhd/DesignCharSkinsLeftBtn.bmp", 580, 300);
            BodyRight = Btn("Nbhd/DesignCharSkinsRightBtn.bmp", 730, 300);
            HeadLeft.OnButtonClick += (b) => { HeadIndex--; UpdatePreview(); };
            HeadRight.OnButtonClick += (b) => { HeadIndex++; UpdatePreview(); };
            BodyLeft.OnButtonClick += (b) => { BodyIndex--; UpdatePreview(); };
            BodyRight.OnButtonClick += (b) => { BodyIndex++; UpdatePreview(); };

            for (int i = 0; i < 5; i++)
                TraitLabels[i] = Label(TraitNames[i], 98, LedY[i] + 2, 12);
            ListenForMouse(new Rectangle(LedX - 6, LedY[0], LedWidth + 6, LedY[4] + LedHeight - LedY[0]), OnLedMouse);

            Bio = new UITextEdit();
            Bio.Position = new Vector2(30, 430);
            Bio.SetSize(740, 82);
            Bio.TextStyle = Bio.TextStyle.Clone();
            Bio.TextStyle.Color = Color.White;
            Bio.TextStyle.Size = 12;
            Bio.MaxLines = 5;
            Add(Bio);

            SimCancelBtn = Btn("Nbhd/DesignCharCancelBtn.bmp", 420, 535, 6, true);
            SimDoneBtn = Btn("Nbhd/DesignCharDoneBtn.bmp", 610, 535, 6, true);
            SimCancelBtn.OnButtonClick += (b) => SetPage(false);
            SimDoneBtn.OnButtonClick += (b) => AcceptSim();

            UpdateSelection();
        }

        // ------------------------------------------------------------------
        // Family page
        // ------------------------------------------------------------------

        /// <summary>Called by TS1CASScreen whenever the family editor opens.</summary>
        public void ShowFamilyPage()
        {
            SelectedMember = -1;
            if (Screen.WIPFamily.Count == 0) FamilyName.CurrentText = "";
            ShownMemberCount = -1;
            SetPage(false);
        }

        private void RefreshMembers()
        {
            foreach (var sim in MemberSims) Remove(sim);
            foreach (var label in MemberLabels) Remove(label);
            MemberSims.Clear();
            MemberLabels.Clear();

            var family = Screen.WIPFamily;
            for (int i = 0; i < family.Count; i++)
            {
                var m = family[i];
                int col = i % Cols, row = i / Cols;
                var slot = new Vector2(GridX + col * SlotW, GridY + row * SlotH);

                var sim = new UIPreviewSim();
                sim.Size = MemberSimSize;
                sim.Position = slot + new Vector2((SlotW - MemberSimSize.X) / 2, 0);
                sim.AutoRotate = true;
                sim.TimeOffset = i * 1500000; // so they don't all turn in sync
                ApplyMember(sim, m);
                AddAt(0, sim);
                MemberSims.Add(sim);

                var label = Label(m.Name, (int)slot.X, (int)slot.Y + 132, 11);
                label.Size = new Vector2(SlotW, 16);
                label.Alignment = TextAlignment.Center;
                MemberLabels.Add(label);
            }
            ShownMemberCount = family.Count;
            UpdateFamilyButtons();
        }

        private void OnFamilyMouse(UIMouseEventType type, UpdateState state)
        {
            if (PickPage) { OnPickMouse(type, state); return; }
            if (SimPage) { OnLedMouse(type, state); return; }
            if (SimPage || PickPage || type != UIMouseEventType.MouseDown) return;
            var pos = GetMousePosition(state.MouseState);
            int col = (int)((pos.X - GridX) / SlotW);
            int row = (int)((pos.Y - GridY) / SlotH);
            if (col < 0 || col >= Cols || row < 0 || row > 1) return;
            int index = row * Cols + col;
            SelectedMember = (index < Screen.WIPFamily.Count) ? index : -1;
            UpdateFamilyButtons();
        }

        private void UpdateFamilyButtons()
        {
            int count = Screen.WIPFamily.Count;
            AddBtn.Disabled = count >= MaxFamily;
            EditBtn.Disabled = SelectedMember < 0;
            DeleteBtn.Disabled = SelectedMember < 0;
            FamDoneBtn.Disabled = count == 0 || FamilyName.CurrentText.Trim().Length == 0;
        }

        // ------------------------------------------------------------------
        // Sim page
        // ------------------------------------------------------------------

        private void OpenSimEditor(int index)
        {
            EditIndex = index;
            if (index < 0)
            {
                SimName.CurrentText = "";
                Bio.CurrentText = "";
                Personality = new int[5];
                Male = true; Child = false; Skin = "lgt";
                HeadIndex = 0; BodyIndex = 0;
                LoadType();
            }
            else
            {
                var m = Screen.WIPFamily[index];
                SimName.CurrentText = m.Name ?? "";
                Bio.CurrentText = m.Bio ?? "";
                for (int i = 0; i < 5; i++) Personality[i] = m.Personality[i] / 100;
                Male = (m.Gender & 1) == 0;
                Child = m.Gender > 1;
                Skin = m.SkinColor;
                LoadType();
                HeadIndex = Math.Max(0, Screen.ActiveHeads.IndexOf(m.Head));
                BodyIndex = Math.Max(0, Screen.ActiveBodies.IndexOf(m.Body));
            }
            UpdateSelection();
            UpdatePreview();
            SetPage(true);
        }

        private string TypeCode
        {
            get { return (Male ? "m" : "f") + (Child ? "c" : "a"); }
        }

        private void ChangeType(bool male, bool child, string skin)
        {
            bool typeChanged = male != Male || child != Child;
            if (!typeChanged && skin == Skin) return;
            Male = male; Child = child; Skin = skin;
            LoadType();
            if (typeChanged) { HeadIndex = 0; BodyIndex = 0; }
            UpdateSelection();
            UpdatePreview();
        }

        private void LoadType()
        {
            Screen.ClassicPopulate(TypeCode, Skin);
        }

        private int Wrap(int i, int count)
        {
            if (count <= 0) return 0;
            return ((i % count) + count) % count;
        }

        private void UpdatePreview()
        {
            if (Screen.ActiveHeads == null || Screen.ActiveHeads.Count == 0) return;
            HeadIndex = Wrap(HeadIndex, Screen.ActiveHeads.Count);
            BodyIndex = Wrap(BodyIndex, Screen.ActiveBodies.Count);
            SetSkeleton(Preview, Child);
            var body = Screen.BuildBodyOutfit(BodyIndex);
            Preview.Avatar.Body = body;
            Preview.Avatar.Handgroup = body;
            Preview.Avatar.Head = Screen.BuildHeadOutfit(HeadIndex);
        }

        private void AcceptSim()
        {
            if (SimName.CurrentText.Trim().Length == 0) return;
            int b = Wrap(BodyIndex, Screen.ActiveBodies.Count);
            int h = Wrap(HeadIndex, Screen.ActiveHeads.Count);
            var member = new CASFamilyMember()
            {
                Name = SimName.CurrentText.Trim(),
                Bio = Bio.CurrentText,
                Body = Screen.ActiveBodies[b],
                BodyTex = Screen.ActiveBodyTex[b],
                HandgroupTex = Screen.ActiveHandgroupTex[b],
                Head = Screen.ActiveHeads[h],
                HeadTex = Screen.ActiveHeadTex[h],
                Gender = (short)((Male ? 0 : 1) | (Child ? 2 : 0)),
                Personality = Personality.Select(x => (short)(x * 100)).ToArray(),
                SkinColor = Skin
            };
            Screen.ClassicCommitMember(member, EditIndex);
            SelectedMember = -1;
            SetPage(false);
            ShownMemberCount = -1; // rebuild on next update
        }

        private void UpdateSelection()
        {
            ChildBtn.Selected = Child;
            AdultBtn.Selected = !Child;
            MaleBtn.Selected = Male;
            FemaleBtn.Selected = !Male;
            LightBtn.Selected = Skin == "lgt";
            MediumBtn.Selected = Skin == "med";
            DarkBtn.Selected = Skin == "drk";
        }

        public int PointsUsed
        {
            get { return Personality.Sum(); }
        }

        private void OnLedMouse(UIMouseEventType type, UpdateState state)
        {
            if (!SimPage || PickPage) return;
            if (type != UIMouseEventType.MouseDown) return;
            var pos = GetMousePosition(state.MouseState);
            if (pos.X < LedX - 6 || pos.X > LedX + LedWidth + 2) return; // only on the LEDs themselves
            for (int i = 0; i < 5; i++)
            {
                if (pos.Y < LedY[i] || pos.Y > LedY[i] + LedHeight) continue;
                // clicking left of the first LED clears the trait
                int wanted = (int)Math.Ceiling((pos.X - LedX) / (LedWidth / 10f));
                wanted = Math.Max(0, Math.Min(10, wanted));
                // can't spend more than the 25-point budget
                int available = MaxPoints - (PointsUsed - Personality[i]);
                Personality[i] = Math.Min(wanted, available);
                return;
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void SetSkeleton(UIPreviewSim sim, bool child)
        {
            var skel = Content.Get().AvatarSkeletons.Get(child ? "child.skel" : "adult.skel");
            sim.Avatar.Skeleton = skel.Clone();
            sim.Avatar.BaseSkeleton = skel.Clone();
            sim.Avatar.ReloadSkeleton();
        }

        private void ApplyMember(UIPreviewSim sim, CASFamilyMember m)
        {
            SetSkeleton(sim, m.Gender > 1);
            var code = (m.Gender > 1) ? "u" : ((m.Gender == 0) ? "m" : "f");
            var hg = m.HandgroupTex;
            var body = new Outfit() { TS1AppearanceID = m.Body + ".apr", TS1TextureID = m.BodyTex };
            body.LiteralHandgroup = new HandGroup()
            {
                TS1HandSet = true,
                LightSkin = new HandSet()
                {
                    LeftHand = new Hand()
                    {
                        Idle = new Gesture() { Name = "h" + code + "lo.apr", TexName = "huao" + hg },
                        Pointing = new Gesture() { Name = "h" + code + "lp.apr", TexName = "huap" + hg },
                        Fist = new Gesture() { Name = "h" + code + "lc.apr", TexName = "huac" + hg }
                    },
                    RightHand = new Hand()
                    {
                        Idle = new Gesture() { Name = "h" + code + "ro.apr", TexName = "huao" + hg },
                        Pointing = new Gesture() { Name = "h" + code + "rp.apr", TexName = "huap" + hg },
                        Fist = new Gesture() { Name = "h" + code + "rc.apr", TexName = "huac" + hg }
                    }
                }
            };
            sim.Avatar.Body = body;
            sim.Avatar.Handgroup = body;
            sim.Avatar.Head = new Outfit() { TS1AppearanceID = m.Head + ".apr", TS1TextureID = m.HeadTex };
        }

        private void SetPage(bool sim)
        {
            SimPage = sim;
            PickPage = false;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            bool sim = SimPage && !PickPage;
            bool fam = !SimPage && !PickPage;
            bool pick = PickPage;

            PickTitle.Visible = pick;
            PickCancelBtn.Visible = PickAddBtn.Visible = PickDeleteBtn.Visible = PickMoveInBtn.Visible = pick;

            FamilyName.Visible = fam;
            AddBtn.Visible = EditBtn.Visible = DeleteBtn.Visible = fam;
            FamDoneBtn.Visible = FamCancelBtn.Visible = fam;
            foreach (var s in MemberSims) s.Visible = fam;
            foreach (var l in MemberLabels) l.Visible = fam;

            SimName.Visible = sim;
            Bio.Visible = sim;
            Preview.Visible = sim;
            ChildBtn.Visible = AdultBtn.Visible = MaleBtn.Visible = FemaleBtn.Visible = sim;
            LightBtn.Visible = MediumBtn.Visible = DarkBtn.Visible = sim;
            HeadLeft.Visible = HeadRight.Visible = BodyLeft.Visible = BodyRight.Visible = sim;
            SimDoneBtn.Visible = SimCancelBtn.Visible = sim;
            foreach (var label in TraitLabels) label.Visible = sim;
        }

        private void Recentre()
        {
            Position = new Vector2((int)((GlobalSettings.Default.GraphicsWidth - 800) / 2),
                                   (int)((GlobalSettings.Default.GraphicsHeight - 600) / 2));
        }

        public override void Update(UpdateState state)
        {
            if (!Visible) return;
            Recentre();
            if (PickPage) UpdatePickPage(state);
            else if (!SimPage)
            {
                if (ShownMemberCount < 0 || ShownMemberCount != Screen.WIPFamily.Count) RefreshMembers();
                UpdateFamilyButtons();
            }
            else SimDoneBtn.Disabled = SimName.CurrentText.Trim().Length == 0;
            base.Update(state);
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;

            // black out everything outside the 800x600 layout
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            DrawLocalTexture(batch, px, null, new Vector2(-2000, -2000), new Vector2(4800, 4600), Color.Black);

            DrawLocalTexture(batch, PickPage ? PickBkg : (SimPage ? SimBkg : FamilyBkg), Vector2.Zero);

            if (PickPage)
            {
                DrawPickRows(batch);
            }
            else if (SimPage)
            {
                // lit personality LEDs
                float ledW = PersLED.Width / 10f;
                for (int i = 0; i < 5; i++)
                {
                    int w = (int)(ledW * Personality[i]);
                    if (w > 0)
                        DrawLocalTexture(batch, PersLED, new Rectangle(0, 0, w, PersLED.Height), new Vector2(LedX, LedY[i] - 1));
                }

                // remaining points
                int remaining = MaxPoints - PointsUsed;
                int rw = (int)(PointsLED.Width / (float)MaxPoints * remaining);
                if (rw > 0)
                    DrawLocalTexture(batch, PointsLED, new Rectangle(0, 0, rw, PointsLED.Height), new Vector2(PointsX, PointsY));
            }
            else if (SelectedMember >= 0)
            {
                // highlight the selected family member
                int col = SelectedMember % Cols, row = SelectedMember / Cols;
                var slot = new Vector2(GridX + col * SlotW + 4, GridY + row * SlotH + 2);
                DrawLocalTexture(batch, px, null, slot, new Vector2(SlotW - 8, SlotH - 4), new Color(0, 255, 255, 40));
            }

            base.Draw(batch);
        }
    }
}
