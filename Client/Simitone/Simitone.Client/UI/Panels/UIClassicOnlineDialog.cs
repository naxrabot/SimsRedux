using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// SimsRedux: the "Online" window on the neighbourhood screen.
    ///   Town: log in, pick the Sim you'll bring, and go to the server's town (click a house to travel there).
    ///   Host: pick it, then click one of your houses. Friends can join you on that lot.
    ///   Join: type your name and the host's address, pick one of your Sims, Join.
    /// </summary>
    public class UIClassicOnlineDialog : UIContainer
    {
        private static UIClassicOnlineDialog Open;
        private const int W = 640, H = 470;
        private static readonly Color TextColor = new Color(215, 215, 245);

        private TextStyle Body, Heading, TitleStyle, SmallStyle;
        private int Page; // 0 town, 1 host, 2 join
        private const int TownTab = 0, HostTab = 1, JoinTab = 2;
        private List<UIElement> TownLogin = new List<UIElement>();
        private List<UIElement> TownIn = new List<UIElement>();
        private UITextBox TownNameBox, TownPasswordBox;
        private UITS1TileButton LogoutBtn;
        private bool Busy;
        private List<UITS1TileButton> Tabs = new List<UITS1TileButton>();
        private List<UIElement> HostPage = new List<UIElement>();
        private List<UIElement> JoinPage = new List<UIElement>();
        private UITextBox NameBox, AddressBox;
        private List<Tuple<UITS1TileButton, uint>> SimButtons = new List<Tuple<UITS1TileButton, uint>>();
        private uint PickedSim;
        private int SimPageIndex;
        private List<Tuple<uint, string>> Sims = new List<Tuple<uint, string>>();
        private UITS1TileButton PrevSims, NextSims;
        private const int SimsPerPage = 8;

        public static void Show()
        {
            if (Open != null) return;
            Open = new UIClassicOnlineDialog();
            UIScreen.GlobalShowDialog(Open, true);
        }

        public UIClassicOnlineDialog()
        {
            Body = TextStyle.DefaultLabel.Clone(); Body.Size = 12; Body.Color = TextColor;
            SmallStyle = TextStyle.DefaultLabel.Clone(); SmallStyle.Size = 11; SmallStyle.Color = new Color(170, 170, 225);
            Heading = TextStyle.DefaultLabel.Clone(); Heading.Size = 13; Heading.Color = new Color(120, 255, 140);
            TitleStyle = TextStyle.DefaultLabel.Clone(); TitleStyle.Size = 18; TitleStyle.Color = TextColor;

            var tile = TS1UIGraphics.Get("cpanel/Buttons/ButtonTileDialog.bmp");
            int x = 16;
            foreach (var name in new[] { "Town", "Host", "Join" })
            {
                var index = Tabs.Count;
                var btn = new UITS1TileButton(tile, name, 110);
                btn.TextSize = 14;
                btn.Position = new Vector2(x, 50);
                btn.OnButtonClick += (b) => SetPage(index);
                Add(btn);
                Tabs.Add(btn);
                x += 116;
            }

            // ---- town ----
            TownNameBox = TextBox(150, 108, 220, ReduxSettings.GetString("townname", ""));
            TownPasswordBox = TextBox(150, 140, 220, "");
            TownPasswordBox.Password = true;
            var login = new UITS1TileButton(tile, "Log In", 130);
            login.TextSize = 14;
            login.Position = new Vector2(150, 180);
            login.OnButtonClick += (b) => TownLogin_Click();
            Add(login);
            TownLogin.Add(TownNameBox); TownLogin.Add(TownPasswordBox); TownLogin.Add(login);

            LogoutBtn = new UITS1TileButton(tile, "Log Out", 110);
            LogoutBtn.TextSize = 13;
            LogoutBtn.Position = new Vector2(W - 140, 104);
            LogoutBtn.OnButtonClick += (b) => { ReduxTown.Logout(); SetPage(TownTab); };
            Add(LogoutBtn);
            var goTown = new UITS1TileButton(tile, "Go to the Town", 170);
            goTown.TextSize = 15;
            goTown.Position = new Vector2(W - 330, H - 52);
            goTown.OnButtonClick += (b) => StartTown();
            Add(goTown);
            TownIn.Add(LogoutBtn); TownIn.Add(goTown);

            // ---- host ----
            var host = new UITS1TileButton(tile, "Host a House", 200);
            host.TextSize = 14;
            host.Position = new Vector2(30, 300);
            host.OnButtonClick += (b) => StartHost();
            Add(host); HostPage.Add(host);

            // ---- join ----
            NameBox = TextBox(150, 108, 220, ReduxSettings.GetString("onlinename", GlobalSettings.Default.LastUser ?? "Player"));
            AddressBox = TextBox(150, 140, 300, ReduxOnlineInfo.Upgrade(ReduxSettings.GetString("onlineaddress", ReduxOnlineInfo.DefaultAddress)));
            JoinPage.Add(NameBox); JoinPage.Add(AddressBox);

            LoadSims();
            for (int i = 0; i < SimsPerPage; i++)
            {
                var slot = i;
                var b = new UITS1TileButton(tile, "", 140);
                b.TextSize = 12;
                b.Position = new Vector2(30 + (i % 4) * 148, 205 + (i / 4) * 44);
                b.OnButtonClick += (btn) => PickSim(slot);
                Add(b); JoinPage.Add(b);
                SimButtons.Add(Tuple.Create(b, 0u));
            }
            PrevSims = new UITS1TileButton(tile, "<", 40); PrevSims.TextSize = 14;
            PrevSims.Position = new Vector2(30, 300);
            PrevSims.OnButtonClick += (b) => { SimPageIndex = Math.Max(0, SimPageIndex - 1); FillSims(); };
            NextSims = new UITS1TileButton(tile, ">", 40); NextSims.TextSize = 14;
            NextSims.Position = new Vector2(76, 300);
            NextSims.OnButtonClick += (b) => { if ((SimPageIndex + 1) * SimsPerPage < Sims.Count) SimPageIndex++; FillSims(); };
            Add(PrevSims); Add(NextSims); JoinPage.Add(PrevSims); JoinPage.Add(NextSims);

            var join = new UITS1TileButton(tile, "Join", 130);
            join.TextSize = 16;
            join.Position = new Vector2(W - 290, H - 52);
            join.OnButtonClick += (b) => StartJoin();
            Add(join); JoinPage.Add(join);

            var close = new UITS1TileButton(tile, "Close", 130);
            close.Position = new Vector2(W - 146, H - 52);
            close.OnButtonClick += (b) => Close();
            Add(close);

            FillSims();
            SetPage(TownTab);
        }

        private UITextBox TextBox(int x, int y, int w, string text)
        {
            var box = new UITextBox();
            box.Position = new Vector2(x, y);
            box.SetSize(w, 24);
            box.TextStyle = box.TextStyle.Clone();
            box.TextStyle.Color = Color.White;
            box.TextStyle.Size = 12;
            box.CurrentText = text ?? "";
            Add(box);
            return box;
        }

        /// <summary>Every Sim in a family that lives in a house, from the player's own neighbourhood.</summary>
        public static List<Tuple<uint, string>> ListSims()
        {
            var sims = new List<Tuple<uint, string>>();
            try
            {
                var neigh = Content.Get().Neighborhood;
                foreach (var fam in neigh.MainResource.List<FAMI>() ?? new List<FAMI>())
                {
                    if (fam.HouseNumber <= 0 || fam.FamilyGUIDs == null) continue;
                    var famName = neigh.MainResource.Get<FAMs>(fam.ChunkID)?.GetString(0) ?? "";
                    foreach (var guid in fam.FamilyGUIDs)
                    {
                        if (guid == 0) continue;
                        var obj = Content.Get().WorldObjects.Get(guid);
                        if (obj == null) continue;
                        string name = null;
                        try { name = obj.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0); } catch { }
                        if (string.IsNullOrWhiteSpace(name)) name = obj.OBJ.ChunkLabel;
                        sims.Add(Tuple.Create(guid, name + (famName.Length > 0 ? " " + famName : "")));
                    }
                }
            }
            catch (Exception e)
            {
                ReduxOnlineInfo.Log("Couldn't list your Sims: " + e.Message);
            }
            return sims;
        }

        private void LoadSims()
        {
            Sims = ListSims();
            var last = (uint)ReduxSettings.GetInt("onlinesim", 0);
            if (Sims.Any(s => s.Item1 == last)) PickedSim = last;
        }

        /// <summary>
        /// The Online button: "Connect to the online neighborhood?" - Yes goes straight to the town
        /// when logged in with a Sim picked before; otherwise this window opens to log in or pick one.
        /// </summary>
        public static void AskToConnect()
        {
            // SimsRedux: an out-of-date browser game plays single player only until it's updated
            var blocked = ReduxPlatform.OnlineBlocked();
            if (blocked != null) { UIClassicDialog.Ok("Online", blocked); return; }
            UIClassicDialog.YesNo("Online", "Connect to the online neighborhood?", () =>
            {
                var screen = UIScreen.Current as TS1GameScreen;
                if (screen == null) return;
                if (ReduxTown.LoggedIn)
                {
                    screen.HostAfterLoad = false;
                    screen.EnterTown();
                }
                else Show();
            });
        }

        private void FillSims()
        {
            for (int i = 0; i < SimButtons.Count; i++)
            {
                var index = SimPageIndex * SimsPerPage + i;
                var btn = SimButtons[i].Item1;
                if (index < Sims.Count)
                {
                    SimButtons[i] = Tuple.Create(btn, Sims[index].Item1);
                    btn.Caption = Sims[index].Item2;
                    btn.Visible = SimsShowing;
                    btn.Selected = Sims[index].Item1 == PickedSim;
                }
                else
                {
                    SimButtons[i] = Tuple.Create(btn, 0u);
                    btn.Visible = false;
                }
            }
            bool many = Sims.Count > SimsPerPage;
            PrevSims.Visible = NextSims.Visible = many && SimsShowing;
        }

        private void PickSim(int slot)
        {
            PickedSim = SimButtons[slot].Item2;
            FillSims();
        }

        /// <summary>The Sim buttons show on the Join page, and on the Town page once logged in.</summary>
        private bool SimsShowing { get { return Page == JoinTab; } }

        private void SetPage(int page)
        {
            Page = page;
            foreach (var e in HostPage) e.Visible = page == HostTab && ReduxPlatform.CanHost;
            foreach (var e in JoinPage) e.Visible = page == JoinTab;
            bool loggedIn = ReduxTown.LoggedIn;
            foreach (var e in TownLogin) e.Visible = page == TownTab && !loggedIn;
            foreach (var e in TownIn) e.Visible = page == TownTab && loggedIn;
            // a website login is ended from the website
            LogoutBtn.Visible = page == TownTab && loggedIn && ReduxSettings.GetString("towntoken", "").Length > 0;
            FillSims();
        }

        private void TownLogin_Click()
        {
            if (Busy) return;
            var name = (TownNameBox.CurrentText ?? "").Trim();
            var password = TownPasswordBox.CurrentText ?? "";
            if (name.Length == 0 || password.Length == 0) { UIClassicDialog.Ok("Log In", "Type your account name and password."); return; }
            Busy = true;
            ReduxTown.Login(name, password, (error) =>
            {
                Busy = false;
                TownPasswordBox.CurrentText = "";
                if (error != null) { UIClassicDialog.Ok(error.Contains("approval") ? "Waiting for Approval" : "Couldn't Log In", error); return; }
                SetPage(TownTab);
            });
        }

        private void StartTown()
        {
            var screen = UIScreen.Current as TS1GameScreen;
            if (screen == null) return;
            Close();
            screen.HostAfterLoad = false;
            screen.EnterTown();
        }

        private void StartHost()
        {
            var screen = UIScreen.Current as TS1GameScreen;
            if (screen == null) return;
            screen.HostAfterLoad = true;
            Close();
            UIClassicDialog.Ok("Host a House", "Now click one of your houses and play it. Once it has loaded, your friends can join you there.");
        }

        private void StartJoin()
        {
            var screen = UIScreen.Current as TS1GameScreen;
            if (screen == null) return;
            var name = (NameBox.CurrentText ?? "").Trim();
            var address = (AddressBox.CurrentText ?? "").Trim();
            if (name.Length == 0) { UIClassicDialog.Ok("Join", "Type your name first, so your friend knows who's visiting."); return; }
            if (PickedSim == 0) { UIClassicDialog.Ok("Join", "Pick the Sim you want to bring."); return; }
            ReduxSettings.Set("onlinename", name);
            ReduxSettings.Set("onlineaddress", address);
            ReduxSettings.Set("onlinesim", (int)PickedSim);
            Close();
            screen.HostAfterLoad = false;
            screen.JoinOnline(address, name, PickedSim);
        }

        private void Close()
        {
            if (Open == this) Open = null;
            UIScreen.RemoveDialog(this);
        }

        /// <summary>
        /// Also forget it when it's taken away some other way (a screen change clears all dialogs);
        /// otherwise Show() would think it's still open and never open it again.
        /// </summary>
        public override void Removed()
        {
            if (Open == this) Open = null;
            base.Removed();
        }

        public override void Update(UpdateState state)
        {
            Position = new Vector2((int)((GlobalSettings.Default.GraphicsWidth - W) / 2), (int)Math.Max(20, (GlobalSettings.Default.GraphicsHeight - H) / 2 - 40));
            if (state.NewKeys.Contains(Microsoft.Xna.Framework.Input.Keys.Escape)) { Close(); return; }
            base.Update(state);
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(0, 0, W, H);
        }

        private void Line(UISpriteBatch batch, string text, int y, TextStyle style)
        {
            DrawLocalString(batch, text, new Vector2(30, y), style, new Rectangle(0, 0, W - 60, 20), TextAlignment.Left | TextAlignment.Middle);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            if (!UIClassicDialog.DrawWindow(this, batch, W, H)) // (SimsRedux: the Redux window, or the original navy box)
            {
                DrawLocalTexture(batch, px, null, new Vector2(6, 6), new Vector2(W, H), Color.Black * 0.35f);
                DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(W, H), new Color(0, 0, 82));
                DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(W, 2), new Color(120, 120, 200));
                DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(2, H), new Color(120, 120, 200));
                DrawLocalTexture(batch, px, null, new Vector2(0, H - 2), new Vector2(W, 2), new Color(0, 0, 40));
                DrawLocalTexture(batch, px, null, new Vector2(W - 2, 0), new Vector2(2, H), new Color(0, 0, 40));
            }
            DrawLocalString(batch, "Play Online", new Vector2(18, 12), TitleStyle, new Rectangle(0, 0, 300, 30), TextAlignment.Left | TextAlignment.Middle);

            var tab = Tabs[Page];
            var r = new Rectangle((int)tab.X - 3, (int)tab.Y - 3, 116, 39);
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, 2), Color.Cyan);
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Bottom - 2), new Vector2(r.Width, 2), Color.Cyan);
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(2, r.Height), Color.Cyan);
            DrawLocalTexture(batch, px, null, new Vector2(r.Right - 2, r.Y), new Vector2(2, r.Height), Color.Cyan);

            DrawLocalTexture(batch, px, null, new Vector2(16, 98), new Vector2(W - 32, H - 160), new Color(0, 0, 50));

            if (Page == TownTab && !ReduxTown.LoggedIn)
            {
                Line(batch, "Account", 110, Heading);
                Line(batch, "Password", 142, Heading);
                DrawLocalString(batch, "Your Sims Redux account (the one you made on the website).", new Vector2(30, 230), Body, new Rectangle(0, 0, W - 60, 20), TextAlignment.Left | TextAlignment.Middle);
                DrawLocalString(batch, "Server: " + ReduxTown.Server, new Vector2(30, 252), SmallStyle, new Rectangle(0, 0, W - 60, 20), TextAlignment.Left | TextAlignment.Middle);
                DrawLocalTexture(batch, px, null, new Vector2(146, 106), new Vector2(228, 28), new Color(0, 0, 30));
                DrawLocalTexture(batch, px, null, new Vector2(146, 138), new Vector2(228, 28), new Color(0, 0, 30));
            }
            else if (Page == TownTab)
            {
                Line(batch, "Logged in as " + ReduxTown.Name, 110, Heading);
                Line(batch, "The town is shared by everyone on the server. Move one of your families into", 136, Body);
                Line(batch, "an empty lot or a house for sale, then click houses to go home or visit.", 156, Body);
                Line(batch, "Your own neighborhood stays as it is. Leave Town brings you back.", 176, Body);
            }
            else if (Page == HostTab && !ReduxPlatform.CanHost)
            {
                Line(batch, "Hosting", 108, Heading);
                Line(batch, "The browser version can't host a lot - browsers can't let others connect in.", 132, Body);
                Line(batch, "Host from the Windows version of The Sims Redux, then join it from here", 152, Body);
                Line(batch, "on the Join page. (Later, everyone will join the game's own server.)", 172, Body);
            }
            else if (Page == HostTab)
            {
                Line(batch, "Host a lot", 108, Heading);
                Line(batch, "Click Host a House, then pick one of your houses and play it as normal.", 132, Body);
                Line(batch, "While it's open, friends can join you there with a Sim of their own.", 152, Body);
                Line(batch, "They control their own Sim. You keep control of your family, the speed,", 172, Body);
                Line(batch, "buying and building. Going back to the neighborhood ends the visit.", 192, Body);
                Line(batch, "Friends join with:", 222, Heading);
                Line(batch, "localhost  (another copy of the game on this PC, for testing)", 244, SmallStyle);
                Line(batch, "this PC's IP address  (someone on your home network)", 262, SmallStyle);
                Line(batch, ReduxOnlineInfo.DefaultAddress + "  (over the internet, once the Cloudflare tunnel is set up)", 280, SmallStyle);
            }
            else
            {
                Line(batch, "Your name", 110, Heading);
                Line(batch, "Host address", 142, Heading);
                DrawLocalString(batch, "e.g. " + ReduxOnlineInfo.DefaultAddress + ", localhost or 192.168.1.20", new Vector2(150, 166), SmallStyle, new Rectangle(0, 0, 400, 16), TextAlignment.Left | TextAlignment.Middle);
                Line(batch, Sims.Count == 0 ? "You need a family living in a house first." : "Pick the Sim you'll bring", 182, Heading);
                // boxes behind the text fields
                DrawLocalTexture(batch, px, null, new Vector2(146, 106), new Vector2(228, 28), new Color(0, 0, 30));
                DrawLocalTexture(batch, px, null, new Vector2(146, 138), new Vector2(308, 28), new Color(0, 0, 30));
            }
            base.Draw(batch);
        }
    }
}
