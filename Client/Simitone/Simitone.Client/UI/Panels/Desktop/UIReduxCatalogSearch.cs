using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.Utils;
using System;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: a search box and the dropper button, just above the Buy / Build bar. Typing two
    /// letters or more shows every item whose name has them in it; the dropper (or the I key) picks
    /// something off the lot.
    /// </summary>
    public class UIReduxCatalogSearch : UIContainer
    {
        public const int BoxW = 170, BoxH = 20, DropperW = 76, TotalW = BoxW + 6 + DropperW;

        public event Action<string> OnSearch;
        public event Action OnDropper;

        private UITextBox Box;
        private UIReduxButton Dropper;
        private TextStyle Hint;
        private string Last = "";

        public bool DropperOn
        {
            get { return Dropper.Selected; }
            set { Dropper.Selected = value; }
        }

        public string Text { get { return Box.CurrentText ?? ""; } }

        public UIReduxCatalogSearch()
        {
            Box = new UITextBox();
            Box.BackgroundTextureReference = null;
            Box.SetSize(BoxW, BoxH);
            Box.TextMargin = new Rectangle(6, 1, 6, 1);
            Box.TextStyle = Box.TextStyle.Clone();
            Box.TextStyle.Color = Color.White;
            Box.TextStyle.Size = 10;
            Box.OnChange += (e) => Changed();
            Add(Box);

            Dropper = new UIReduxButton("Dropper (I)", DropperW, BoxH);
            Dropper.TextSize = 9;
            Dropper.Tab = true;
            Dropper.Position = new Vector2(BoxW + 6, 0);
            Dropper.OnButtonClick += (b) => OnDropper?.Invoke();
            Add(Dropper);

            Hint = TextStyle.DefaultLabel.Clone();
            Hint.Size = 10;
            Hint.Color = new Color(190, 200, 230);
        }

        private void Changed()
        {
            var t = (Box.CurrentText ?? "").Trim();
            if (t == Last) return;
            Last = t;
            OnSearch?.Invoke(t);
        }

        /// <summary>Empties the box (when leaving the search results), without searching again.</summary>
        public void ClearText()
        {
            if (string.IsNullOrEmpty(Box.CurrentText)) { Last = ""; return; }
            Box.CurrentText = "";
            Last = "";
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            // the box: a dark rounded field in the UI colour
            DrawLocalTexture(batch, ReduxTheme.Box(BoxW, BoxH, BoxH / 2, ReduxTheme.Deeper, ReduxTheme.Deep, ReduxTheme.Rim, 1), Vector2.Zero);
            base.Draw(batch);
            if (string.IsNullOrEmpty(Box.CurrentText))
                DrawLocalString(batch, "Search items...", new Vector2(8, 0), Hint, new Rectangle(0, 0, BoxW - 10, BoxH), TextAlignment.Left | TextAlignment.Middle);
        }
    }
}
