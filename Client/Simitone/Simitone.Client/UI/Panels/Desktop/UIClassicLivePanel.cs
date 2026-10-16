using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the original Sims 1 Live mode bar, drawn on the classic bottom bar.
    ///   - the mood meter (green bars up, red bars down) with the selected Sim's face in the middle
    ///   - the other family members' faces (click one to control that Sim)
    ///   - the Mood / Personality / Interests / Job / House / Relationships / Inventory buttons
    ///   - the page for the chosen button (motives, personality points, skills, ...)
    /// Coordinates are in the classic control panel's layout (the wing is at 0,0).
    /// </summary>
    public class UIClassicLivePanel : UIContainer
    {
        private enum Page { Mood, Personality, Interests, Job, House, Relationships, Inventory }

        private TS1GameScreen Game;
        private Texture2D Gadget, GreenBars, RedBars, Px;
        private Texture2D PersOff, BarsOn, JobOn, JobOff, IntOff, SkillMark;
        private Page Current = Page.Mood;
        private Dictionary<Page, UITS1Button> PageButtons = new Dictionary<Page, UITS1Button>();
        private UIElement Hosted;

        // layout (measured from the original art)
        private const int GX = 224, GY = 83;           // mood meter + family faces (LiveGadget.bmp, 108x100)
        private const int CX = 440, CY = 83, CH = 100; // page contents
        private static readonly Rectangle SelectedSlot = new Rectangle(9, 33, 29, 35);
        private static readonly Rectangle[] FamilySlots = new Rectangle[]
        {
            new Rectangle(45, 5, 25, 28), new Rectangle(77, 5, 25, 28),
            new Rectangle(45, 36, 25, 28), new Rectangle(77, 36, 25, 28),
            new Rectangle(45, 67, 25, 28), new Rectangle(77, 67, 25, 28),
        };

        private static readonly Color TextColor = new Color(220, 220, 250);
        private static readonly Color DimText = new Color(150, 150, 205);
        private static readonly Color SlotBack = new Color(0, 0, 40);
        private TextStyle Small, Title;

        private VMAvatar Selected;
        private List<VMAvatar> Others = new List<VMAvatar>();
        private Texture2D SelectedHead;
        private List<Texture2D> OtherHeads = new List<Texture2D>();
        private int HoverSlot = -1;

        public UIClassicLivePanel(TS1GameScreen game)
        {
            Game = game;
            Gadget = TS1UIGraphics.Get("cpanel/Backgrounds/LiveGadget.bmp");
            GreenBars = TS1UIGraphics.Get("cpanel/Greenbars.bmp");
            RedBars = TS1UIGraphics.Get("cpanel/Redbars.bmp");
            PersOff = TS1UIGraphics.Get("cpanel/Backgrounds/PersBkg.bmp");
            BarsOn = TS1UIGraphics.Get("cpanel/InterestBars.bmp");
            IntOff = TS1UIGraphics.Get("cpanel/Backgrounds/InterestBarsBackground.bmp");
            JobOn = TS1UIGraphics.Get("cpanel/JobSubBars.bmp");
            JobOff = TS1UIGraphics.Get("cpanel/Backgrounds/JobSubBarsOff.bmp");
            SkillMark = TS1UIGraphics.Get("cpanel/SkillsHilite.bmp");
            Px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);

            Small = TextStyle.DefaultLabel.Clone();
            Small.Size = 10;
            Small.Color = TextColor;
            Title = TextStyle.DefaultLabel.Clone();
            Title.Size = 12;
            Title.Color = TextColor;

            // page buttons: the big Mood button, then two columns of three
            AddPageButton(Page.Mood, "cpanel/Buttons/Mood.bmp", 336, 113);
            AddPageButton(Page.Personality, "cpanel/Buttons/Personality.bmp", 372, 86);
            AddPageButton(Page.Interests, "cpanel/Buttons/Interest.bmp", 372, 117);
            AddPageButton(Page.Job, "cpanel/Buttons/Job.bmp", 372, 148);
            AddPageButton(Page.House, "cpanel/Buttons/House.bmp", 401, 86);
            AddPageButton(Page.Relationships, "cpanel/Buttons/Relationship.bmp", 401, 117);
            AddPageButton(Page.Inventory, "cpanel/Buttons/Gift.bmp", 401, 148);
            SetPage(Page.Mood);

            // clicking a family face takes control of that Sim
            for (int i = 0; i < FamilySlots.Length; i++)
            {
                var slot = i;
                var r = FamilySlots[i];
                ListenForMouse(new Rectangle(GX + r.X, GY + r.Y, r.Width, r.Height), (type, state) =>
                {
                    if (!Visible) { HoverSlot = -1; return; }
                    if (type == UIMouseEventType.MouseOver) HoverSlot = slot;
                    else if (type == UIMouseEventType.MouseOut) { if (HoverSlot == slot) HoverSlot = -1; }
                    else if (type == UIMouseEventType.MouseUp && HoverSlot == slot) SelectFamily(slot); // (not when dragged off the face)
                });
            }
        }

        private void AddPageButton(Page page, string art, int x, int y)
        {
            var btn = new UITS1Button(TS1UIGraphics.Get(art), 4);
            btn.Position = new Vector2(x, y);
            btn.OnButtonClick += (b) => SetPage(page);
            Add(btn);
            PageButtons[page] = btn;
        }

        private void SetPage(Page page)
        {
            Current = page;
            foreach (var pair in PageButtons) pair.Value.Selected = pair.Key == page;

            // relationships and inventory: the classic pages (they draw themselves on the bar).
            // They're swapped in on the next update, not inside the click: changing the panel while
            // the click is being handed out let the click carry on through to the lot.
            PagePending = true;
        }

        private bool PagePending;
        private void SwapHostedPage()
        {
            PagePending = false;
            if (Hosted != null) { Remove(Hosted); Hosted = null; }
            UIElement sub = null;
            if (Current == Page.Relationships) sub = new UIClassicRelationshipPage(Game);
            else if (Current == Page.Inventory) sub = new UIClassicInventoryPage(Game);
            if (sub != null)
            {
                Add(sub);
                Hosted = sub;
            }
        }

        private void SelectFamily(int slot)
        {
            if (slot < 0 || slot >= Others.Count) return;
            FSO.HIT.HITVM.Get().PlaySoundEvent(UISounds.QueueAdd);
            Game.vm.SendCommand(new VMNetChangeControlCmd() { TargetID = Others[slot].ObjectID });
        }

        public override void Update(UpdateState state)
        {
            if (PagePending) SwapHostedPage();
            base.Update(state);
            if (Game.vm == null) return;

            // family faces (made here, not while drawing: making a face renders the 3D head)
            Selected = Game.SelectedAvatar;
            Others.Clear();
            OtherHeads.Clear();
            try
            {
                var family = Game.vm.TS1State?.CurrentFamily;
                if (family != null)
                {
                    foreach (var ava in Game.vm.Context.ObjectQueries.Avatars)
                    {
                        if (!family.RuntimeSubset.Contains(ava.Object.OBJ.GUID)) continue;
                        var avatar = ava as VMAvatar;
                        if (avatar == null || avatar == Selected) continue;
                        if (Others.Count >= FamilySlots.Length) break;
                        Others.Add(avatar);
                        OtherHeads.Add(UIIconCache.GetObject(avatar));
                    }
                }
                SelectedHead = (Selected == null) ? null : UIIconCache.GetObject(Selected);
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Live panel faces: " + e.Message);
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            DrawGadget(batch);

            if (Selected != null)
            {
                switch (Current)
                {
                    case Page.Mood: DrawMotives(batch); break;
                    case Page.Personality: DrawPersonality(batch); break;
                    case Page.Interests: DrawInterests(batch); break;
                    case Page.Job: DrawJob(batch); break;
                    case Page.House: DrawHouse(batch); break;
                }
            }
            base.Draw(batch);
        }

        // ------------------------------------------------------------------
        // Mood meter and faces
        // ------------------------------------------------------------------

        private void DrawGadget(UISpriteBatch batch)
        {
            if (ReduxTheme.Redux) { DrawReduxGadget(batch); return; }
            var g = new Vector2(GX, GY);
            // dark slots behind the frame (unlit bars, empty face slots)
            Fill(batch, new Rectangle(GX + 9, GY + 4, 29, 92), SlotBack);
            foreach (var r in FamilySlots) Fill(batch, Offset(r), SlotBack);

            // mood: 5 green bars above the face (good mood), 5 red bars below (bad mood).
            // The bars nearest the face light up first.
            int mood = (Selected == null) ? 0 : Selected.GetMotiveData(VMMotive.Mood);
            int lit = (int)Math.Ceiling(Math.Min(100, Math.Abs(mood)) / 20.0);
            if (mood > 0 && GreenBars.Width > 1)
            {
                int top = 25 - lit * 5; // bars are 5px apart, the biggest one is at the bottom
                DrawLocalTexture(batch, GreenBars, new Rectangle(0, top, GreenBars.Width, 25 - top), g + new Vector2(11, 4 + top));
            }
            else if (mood < 0 && RedBars.Width > 1)
            {
                int height = lit * 5;
                DrawLocalTexture(batch, RedBars, new Rectangle(0, 0, RedBars.Width, height), g + new Vector2(11, 73));
            }

            // faces
            if (SelectedHead != null) DrawFace(batch, SelectedHead, Offset(SelectedSlot));
            for (int i = 0; i < OtherHeads.Count; i++)
                if (OtherHeads[i] != null) DrawFace(batch, OtherHeads[i], Offset(FamilySlots[i]));

            DrawLocalTexture(batch, Gadget, g);
            DrawHoverName(batch);
        }

        /// <summary>The name of the family member under the mouse.</summary>
        private void DrawHoverName(UISpriteBatch batch)
        {
            if (HoverSlot >= 0 && HoverSlot < Others.Count)
            {
                var r = Offset(FamilySlots[HoverSlot]);
                Fill(batch, new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, 1), Color.Cyan);
                Fill(batch, new Rectangle(r.X - 1, r.Bottom, r.Width + 2, 1), Color.Cyan);
                Fill(batch, new Rectangle(r.X - 1, r.Y, 1, r.Height), Color.Cyan);
                Fill(batch, new Rectangle(r.Right, r.Y, 1, r.Height), Color.Cyan);
                var name = Others[HoverSlot].Name ?? "";
                var w = 8 + name.Length * 6;
                var box = new Rectangle(r.X, r.Y - 18, w, 15);
                Fill(batch, box, new Color(0, 0, 82));
                DrawLocalString(batch, name, new Vector2(box.X, box.Y), Small, new Rectangle(0, 0, box.Width, box.Height), TextAlignment.Center | TextAlignment.Middle);
            }
        }

        /// <summary>
        /// Redux: the same mood meter and family faces as the original, on smooth panels in the UI colour.
        /// Mood: up to 4 green bars above the face (good mood) or 4 red bars below (bad mood).
        /// </summary>
        private void DrawReduxGadget(UISpriteBatch batch)
        {
            // soft dark backing behind the meter and faces
            DrawLocalTexture(batch, ReduxTheme.Box(152, 95, 8, Color.Black * 0.2f, Color.Black * 0.2f, Color.Transparent, 0), new Vector2(GX + 6, GY + 2));

            // mood meter well
            DrawLocalTexture(batch, ReduxTheme.Box(29, 92, 6, ReduxTheme.Deeper, ReduxTheme.Edge, Color.Transparent, 0, true), new Vector2(GX + 9, GY + 4));
            int mood = (Selected == null) ? 0 : Selected.GetMotiveData(VMMotive.Mood);
            int lit = (int)Math.Ceiling(Math.Min(100, Math.Abs(mood)) / 25.0);
            var green = new Color(123, 224, 107);
            var red = new Color(240, 102, 79);
            var off = Color.White * 0.08f;
            for (int i = 0; i < 4; i++)
            {
                // green: the bars nearest the face light first (bottom up); red: top down
                bool g = mood > 0 && i >= 4 - lit;
                bool r = mood < 0 && i < lit;
                Fill(batch, new Rectangle(GX + 13, GY + 7 + i * 6, 21, 5), g ? green : off);
                Fill(batch, new Rectangle(GX + 13, GY + 71 + i * 6, 21, 5), r ? red : off);
            }

            // the selected Sim: a glowing frame
            var sel = Offset(SelectedSlot);
            DrawLocalTexture(batch, ReduxTheme.Box(sel.Width, sel.Height, 6, ReduxTheme.Gloss, ReduxTheme.Deep, ReduxTheme.Glow, 2), new Vector2(sel.X, sel.Y));
            if (SelectedHead != null) DrawFace(batch, SelectedHead, new Rectangle(sel.X + 2, sel.Y + 2, sel.Width - 4, sel.Height - 4));

            // the rest of the family
            for (int i = 0; i < FamilySlots.Length; i++)
            {
                var r = Offset(FamilySlots[i]);
                bool here = i < OtherHeads.Count && OtherHeads[i] != null;
                var box = here
                    ? ReduxTheme.Box(r.Width, r.Height, 5, ReduxTheme.Gloss, ReduxTheme.Deep, ReduxTheme.Rim, 1)
                    : ReduxTheme.Box(r.Width, r.Height, 5, ReduxTheme.Deeper * 0.5f, ReduxTheme.Deeper * 0.5f, ReduxTheme.Rim * 0.35f, 1);
                DrawLocalTexture(batch, box, new Vector2(r.X, r.Y));
                if (here) DrawFace(batch, OtherHeads[i], new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2));
            }

            DrawHoverName(batch);
        }

        private static Dictionary<Texture2D, Rectangle> HeadBounds = new Dictionary<Texture2D, Rectangle>();

        /// <summary>Where the head actually is in its picture (the picture has empty space around it).</summary>
        private static Rectangle FindHead(Texture2D head)
        {
            Rectangle r;
            if (HeadBounds.TryGetValue(head, out r)) return r;
            r = new Rectangle(0, 0, head.Width, head.Height);
            try
            {
                var data = new Color[head.Width * head.Height];
                head.GetData(data);
                int x0 = head.Width, y0 = head.Height, x1 = -1, y1 = -1;
                for (int y = 0; y < head.Height; y++)
                    for (int x = 0; x < head.Width; x++)
                        if (data[x + y * head.Width].A > 24)
                        {
                            if (x < x0) x0 = x; if (x > x1) x1 = x;
                            if (y < y0) y0 = y; if (y > y1) y1 = y;
                        }
                if (x1 > x0 + 4 && y1 > y0 + 4) r = new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            }
            catch { }
            HeadBounds[head] = r;
            return r;
        }

        private void DrawFace(UISpriteBatch batch, Texture2D head, Rectangle slot)
        {
            // SimsRedux: fit the whole head in the slot, centred (the camera sees it from the side, off-centre)
            var b = FindHead(head);
            float scale = Math.Min((slot.Width - 2) / (float)b.Width, (slot.Height - 2) / (float)b.Height);
            var pos = new Vector2(slot.X + (slot.Width - b.Width * scale) / 2f, slot.Y + (slot.Height - b.Height * scale) / 2f);
            DrawLocalTexture(batch, head, b, pos, new Vector2(scale, scale));
        }

        // ------------------------------------------------------------------
        // Pages
        // ------------------------------------------------------------------

        private static readonly VMMotive[] Motives = new VMMotive[]
        {
            VMMotive.Hunger, VMMotive.Comfort, VMMotive.Hygiene, VMMotive.Bladder,
            VMMotive.Energy, VMMotive.Fun, VMMotive.Social, VMMotive.Room
        };

        private void DrawMotives(UISpriteBatch batch)
        {
            for (int i = 0; i < 8; i++)
            {
                int col = i / 4, row = i % 4;
                int x = CX + col * 215, y = CY + 9 + row * 22;
                var name = GameFacade.Strings.GetString("f102", (i + 1).ToString());
                DrawLocalString(batch, name, new Vector2(x, y), Small, new Rectangle(0, 0, 70, 14), TextAlignment.Right | TextAlignment.Middle);
                DrawMotiveBar(batch, new Rectangle(x + 76, y + 1, 110, 12), Selected.GetMotiveData(Motives[i]));
            }
        }

        private static readonly VMPersonDataVariable[] PersonalityVars = new VMPersonDataVariable[]
        {
            VMPersonDataVariable.NeatPersonality, VMPersonDataVariable.OutgoingPersonality,
            VMPersonDataVariable.ActivePersonality, VMPersonDataVariable.PlayfulPersonality,
            VMPersonDataVariable.NicePersonality
        };
        private static readonly string[] PersonalityNames = new string[] { "Neat", "Outgoing", "Active", "Playful", "Nice" };

        private void DrawPersonality(UISpriteBatch batch)
        {
            for (int i = 0; i < 5; i++)
            {
                int y = CY + 6 + i * 18;
                DrawLocalString(batch, PersonalityNames[i], new Vector2(CX, y), Small, new Rectangle(0, 0, 70, 14), TextAlignment.Right | TextAlignment.Middle);
                DrawPoints(batch, PersOff, BarsOn, new Vector2(CX + 78, y), Points(Selected.GetPersonData(PersonalityVars[i])), 14);
            }
        }

        private static readonly VMPersonDataVariable[] InterestVars = new VMPersonDataVariable[]
        {
            VMPersonDataVariable.Interest_TravelToys, VMPersonDataVariable.Interest_ViolenceAliens,
            VMPersonDataVariable.Interest_PoliticsPets, VMPersonDataVariable.Interest_SixtiesSchool,
            VMPersonDataVariable.Interest_Weather, VMPersonDataVariable.Interest_Sports,
            VMPersonDataVariable.Interest_Music, VMPersonDataVariable.Interest_Outdoors
        };
        private static readonly string[] AdultInterests = new string[] { "Travel", "Violence", "Politics", "The60s", "Weather", "Sports", "Music", "Outdoors" };
        private static readonly string[] ChildInterests = new string[] { "Toys", "Aliens", "Pets", "School", "Weather", "Sports", "Music", "Outdoors" };

        private void DrawInterests(UISpriteBatch batch)
        {
            var names = Selected.IsChild ? ChildInterests : AdultInterests;
            for (int i = 0; i < 8; i++)
            {
                int col = i / 4, row = i % 4;
                int x = CX + col * 190, y = CY + 4 + row * 24;
                var icon = TS1UIGraphics.Get("cpanel/Buttons/Interest" + names[i] + ".bmp");
                if (icon.Width >= 3) // (3 frames side by side)
                {
                    int fw = icon.Width / 3;
                    DrawLocalTexture(batch, icon, new Rectangle(0, 0, fw, icon.Height), new Vector2(x, y), new Vector2(22f / fw, 22f / icon.Height));
                }
                var label = (names[i] == "The60s") ? "The 60s" : names[i];
                DrawLocalString(batch, label, new Vector2(x + 26, y), Small, new Rectangle(0, 0, 70, 22), TextAlignment.Left | TextAlignment.Middle);
                DrawPoints(batch, IntOff, BarsOn, new Vector2(x + 100, y + 3), Points(Selected.GetPersonData(InterestVars[i])), 16);
            }
        }

        private static readonly VMPersonDataVariable[] SkillVars = new VMPersonDataVariable[]
        {
            VMPersonDataVariable.CookingSkill, VMPersonDataVariable.MechanicalSkill,
            VMPersonDataVariable.CharismaSkill, VMPersonDataVariable.BodySkill,
            VMPersonDataVariable.LogicSkill, VMPersonDataVariable.CreativitySkill
        };
        private static readonly string[] SkillNames = new string[] { "Cooking", "Mechanical", "Charisma", "Body", "Logic", "Creativity" };

        private void DrawJob(UISpriteBatch batch)
        {
            var type = Selected.GetPersonData(VMPersonDataVariable.JobType);
            var level = Selected.GetPersonData(VMPersonDataVariable.JobPromotionLevel);
            var performance = Selected.GetPersonData(VMPersonDataVariable.JobPerformance);
            var job = Content.Get().Jobs.GetJob((ushort)type);
            FSO.Files.Formats.IFF.Chunks.JobLevel myLevel = null;
            if (job != null && level >= 0 && level < job.JobLevels.Length) myLevel = job.JobLevels[level];

            // left: job, salary, hours, performance
            if (myLevel == null)
            {
                DrawLocalString(batch, "Unemployed", new Vector2(CX, CY + 8), Title, new Rectangle(0, 0, 200, 16), TextAlignment.Left | TextAlignment.Middle);
                DrawLocalString(batch, "Look in the newspaper or", new Vector2(CX, CY + 32), Small, new Rectangle(0, 0, 210, 14), TextAlignment.Left | TextAlignment.Middle);
                DrawLocalString(batch, "on the computer for a job.", new Vector2(CX, CY + 48), Small, new Rectangle(0, 0, 210, 14), TextAlignment.Left | TextAlignment.Middle);
            }
            else
            {
                DrawLocalString(batch, myLevel.JobName, new Vector2(CX, CY + 6), Title, new Rectangle(0, 0, 210, 16), TextAlignment.Left | TextAlignment.Middle);
                DrawLocalString(batch, "Salary: §" + myLevel.Salary, new Vector2(CX, CY + 26), Small, new Rectangle(0, 0, 210, 14), TextAlignment.Left | TextAlignment.Middle);
                DrawLocalString(batch, "Hours: " + ToTime(myLevel.StartTime) + " - " + ToTime(myLevel.EndTime), new Vector2(CX, CY + 42), Small, new Rectangle(0, 0, 210, 14), TextAlignment.Left | TextAlignment.Middle);
                var friendsNeeded = myLevel.MinRequired[0];
                var friends = Game.ActiveFamily?.FamilyFriends ?? 0;
                DrawLocalString(batch, "Family friends: " + friends + (friendsNeeded > 0 ? " (need " + friendsNeeded + ")" : ""), new Vector2(CX, CY + 58), Small, new Rectangle(0, 0, 210, 14), TextAlignment.Left | TextAlignment.Middle);
                DrawLocalString(batch, "Performance", new Vector2(CX, CY + 76), Small, new Rectangle(0, 0, 70, 14), TextAlignment.Left | TextAlignment.Middle);
                DrawMotiveBar(batch, new Rectangle(CX + 76, CY + 77, 110, 12), performance);
            }

            // right: skills, with a small mark at the level needed for the next promotion
            for (int i = 0; i < 6; i++)
            {
                int col = i / 3, row = i % 3;
                int x = CX + 230 + col * 150, y = CY + 12 + row * 26;
                DrawLocalString(batch, SkillNames[i], new Vector2(x, y), Small, new Rectangle(0, 0, 70, 12), TextAlignment.Right | TextAlignment.Middle);
                var pos = new Vector2(x + 76, y + 1);
                DrawPoints(batch, JobOff, JobOn, pos, Points(Selected.GetPersonData(SkillVars[i])), 11);
                if (myLevel != null && SkillMark.Width > 1)
                {
                    var need = Math.Max(0, Math.Min(10, myLevel.MinRequired[i + 1] / 100));
                    if (need > 0) DrawLocalTexture(batch, SkillMark, pos + new Vector2(need * 4 - 4, 0));
                }
            }
        }

        private void DrawHouse(UISpriteBatch batch)
        {
            var family = Game.ActiveFamily;
            if (family == null)
            {
                DrawLocalString(batch, "This lot has no family.", new Vector2(CX, CY + 8), Title, new Rectangle(0, 0, 300, 16), TextAlignment.Left | TextAlignment.Middle);
                return;
            }
            var lines = new string[]
            {
                "Home Value:  §" + family.ValueInArch.ToString("##,#0"),
                "Family Funds:  §" + family.Budget.ToString("##,#0"),
                "Total Worth:  §" + (family.ValueInArch + family.Budget).ToString("##,#0"),
                "Family Friends:  " + family.FamilyFriends,
            };
            for (int i = 0; i < lines.Length; i++)
                DrawLocalString(batch, lines[i], new Vector2(CX, CY + 6 + i * 18), i == 0 ? Title : Small, new Rectangle(0, 0, 220, 16), TextAlignment.Left | TextAlignment.Middle);

            DrawLocalString(batch, "Room", new Vector2(CX + 240, CY + 9), Small, new Rectangle(0, 0, 50, 14), TextAlignment.Right | TextAlignment.Middle);
            DrawMotiveBar(batch, new Rectangle(CX + 296, CY + 10, 110, 12), Selected.GetMotiveData(VMMotive.Room));
        }

        // ------------------------------------------------------------------
        // Drawing helpers
        // ------------------------------------------------------------------

        private static int Points(int value)
        {
            return Math.Max(0, Math.Min(10, (int)Math.Round(value / 100.0)));
        }

        /// <summary>A row of 10 points: the "off" picture, then the lit part of the "on" picture over it.</summary>
        private void DrawPoints(UISpriteBatch batch, Texture2D off, Texture2D on, Vector2 pos, int points, int height)
        {
            if (off.Width > 1) DrawLocalTexture(batch, off, new Rectangle(0, 0, off.Width, Math.Min(height, off.Height)), pos);
            if (on.Width > 1 && points > 0)
            {
                int w = Math.Min(on.Width, points * 4);
                int h = Math.Min(height, on.Height);
                DrawLocalTexture(batch, on, new Rectangle(0, (on.Height - h) / 2, w, h), pos);
            }
        }

        /// <summary>Sims 1 style motive bar: shrinks as it empties and turns from green to yellow to red.</summary>
        private void DrawMotiveBar(UISpriteBatch batch, Rectangle r, int value)
        {
            value = Math.Max(-100, Math.Min(100, value));
            if (ReduxTheme.Redux)
            {
                // Redux: a sunken groove with a glossy fill
                DrawLocalTexture(batch, ReduxTheme.Box(r.Width + 2, r.Height + 1, 3, ReduxTheme.Deeper, ReduxTheme.Edge, Color.Transparent, 0, true), new Vector2(r.X - 1, r.Y - 1));
                var inside = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 3);
                int fw = (int)Math.Round(inside.Width * (value + 100) / 200f);
                if (fw <= 0) return;
                float k = value / 100f;
                Color fc = (k >= 0) ? Color.Lerp(new Color(242, 207, 62), new Color(123, 224, 107), k)
                                    : Color.Lerp(new Color(242, 207, 62), new Color(240, 102, 79), -k);
                Fill(batch, new Rectangle(inside.X, inside.Y, fw, inside.Height), fc);
                Fill(batch, new Rectangle(inside.X, inside.Y, fw, inside.Height / 2), Color.Lerp(fc, Color.White, 0.35f));
                Fill(batch, new Rectangle(inside.X, inside.Bottom - 2, fw, 2), Color.Lerp(fc, Color.Black, 0.35f));
                return;
            }
            Fill(batch, r, new Color(0, 0, 30));
            Fill(batch, new Rectangle(r.X, r.Y, r.Width, 1), new Color(40, 40, 120));
            var inner = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            int w = (int)Math.Round(inner.Width * (value + 100) / 200f);
            if (w <= 0) return;
            float t = value / 100f;
            Color c = (t >= 0) ? Color.Lerp(new Color(230, 230, 40), new Color(40, 220, 60), t)
                               : Color.Lerp(new Color(230, 230, 40), new Color(220, 30, 30), -t);
            Fill(batch, new Rectangle(inner.X, inner.Y, w, inner.Height), c);
            Fill(batch, new Rectangle(inner.X, inner.Y, w, 2), Color.Lerp(c, Color.White, 0.35f));
            Fill(batch, new Rectangle(inner.X, inner.Bottom - 2, w, 2), Color.Lerp(c, Color.Black, 0.3f));
        }

        private void Fill(UISpriteBatch batch, Rectangle r, Color c)
        {
            DrawLocalTexture(batch, Px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), c);
        }

        private Rectangle Offset(Rectangle r)
        {
            return new Rectangle(GX + r.X, GY + r.Y, r.Width, r.Height);
        }

        private static string ToTime(int time)
        {
            return ((time > 12) ? (time - 12) : (time == 0 ? 12 : time)) + ((time >= 12) ? " PM" : " AM");
        }
    }
}
