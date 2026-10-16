using FSO.Client.UI.Framework;
using FSO.Client.UI.Controls;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels.LotControls
{
    /// <summary>
    /// SimsRedux: "Call Taxi" from the phone - a list of the lots a taxi can take the Sim to
    /// (home, other families' houses, community lots, Downtown, Vacation Island, Studio Town,
    /// Magic Town). Picking one and pressing Go calls OnPick with the lot's house number.
    /// </summary>
    public class UICallTaxiAlert : UIMobileDialog
    {
        public class Destination
        {
            public short House;
            public string Label;
            public Destination(short house, string label) { House = house; Label = label; }
        }

        public event Action<short> OnPick;

        private List<Destination> Places;
        private UITouchStringList PlaceList;
        private UIBigButton GoButton;
        private UIBigButton CancelButton;
        private int Selected = -1;
        private bool Answered;

        public UICallTaxiAlert(List<Destination> places)
        {
            Places = places ?? new List<Destination>();
            Caption = "Call Taxi";
            SetHeight(490);

            var panel = new UIContainer();
            panel.Position = new Microsoft.Xna.Framework.Vector2((Width - 1030) / 2, 110);
            Add(panel);

            PlaceList = new UITouchStringList();
            PlaceList.Size = new Microsoft.Xna.Framework.Vector2(690, 350);
            PlaceList.BackingList = Places.Select(x => x.Label).ToList();
            PlaceList.Refresh();
            PlaceList.OnSelectionChange += (i) =>
            {
                Selected = (i >= 0 && i < Places.Count) ? i : -1;
                GoButton.Disabled = Answered || Selected < 0;
            };
            panel.Add(PlaceList);

            CancelButton = new UIBigButton(false);
            CancelButton.Caption = "Cancel";
            CancelButton.Position = new Microsoft.Xna.Framework.Vector2(370 + 385, 135);
            CancelButton.Width = 275;
            CancelButton.OnButtonClick += (btn) =>
            {
                if (Answered) return;
                Answered = true;
                Close();
            };
            panel.Add(CancelButton);

            GoButton = new UIBigButton(true);
            GoButton.Caption = "Go";
            GoButton.Position = new Microsoft.Xna.Framework.Vector2(370 + 385, 255);
            GoButton.Width = 275;
            GoButton.Disabled = true;
            GoButton.OnButtonClick += (btn) =>
            {
                if (Answered || Selected < 0 || Selected >= Places.Count) return;
                Answered = true;
                GoButton.Disabled = true;
                CancelButton.Disabled = true;
                var house = Places[Selected].House;
                Close();
                OnPick?.Invoke(house);
            };
            panel.Add(GoButton);
        }
    }
}
