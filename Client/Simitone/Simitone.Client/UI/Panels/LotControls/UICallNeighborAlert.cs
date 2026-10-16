using FSO.Client.UI.Framework;
using FSO.Client.UI.Controls;
using FSO.Content;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Simitone.Client.UI.Panels.LotControls
{
    public class UICallNeighborAlert : UIMobileDialog
    {
        public UICallNeighborPanel NPanel;
        public short SelectedNeighbour
        {
            get
            {
                return NPanel.SelectedNeighbour;
            }
        }

        public event Action<int> OnResult;
        /// <summary>SimsRedux: "Call Taxi" was picked (the phone book is answered as cancelled first).</summary>
        public event Action OnTaxi;

        public UICallNeighborAlert(short callerNID, VM vm) : this(callerNID, vm, false)
        {
        }

        public UICallNeighborAlert(short callerNID, VM vm, bool offerTaxi)
        {
            Caption = "Call Neighbour";
            SetHeight(offerTaxi ? 610 : 490);
            NPanel = new UICallNeighborPanel(callerNID, vm, offerTaxi);
            NPanel.Position = new Microsoft.Xna.Framework.Vector2((Width - 1030) / 2, 110);
            NPanel.OnResult += (res) => { OnResult?.Invoke(res); Close(); };
            NPanel.OnTaxi += () => { OnTaxi?.Invoke(); Close(); };
            Add(NPanel);
        }
    }

    public class UICallNeighborPanel : UIContainer
    {
        public Dictionary<short, List<short>> NeighborsByFamilyID = new Dictionary<short, List<short>>();
        // SimsRedux: the families in the same order as the left list (a Dictionary's order isn't
        // something to index into)
        private List<short> FamilyOrder = new List<short>();
        public UITouchStringList FamilyList;
        public UITouchStringList NeighbourList;
        public UIAvatarSelectButton Icon;
        public UIBigButton CallButton;
        public UIBigButton CancelButton;
        public UIBigButton TaxiButton;
        public UILabel EmptyLabel;
        public int SelectedFamily = -1;
        public short SelectedNeighbour = -1;
        public VM VM;
        private bool Answered;

        public event Action<int> OnResult;
        public event Action OnTaxi;

        public UICallNeighborPanel(short callerNID, VM vm) : this(callerNID, vm, false)
        {
        }

        public UICallNeighborPanel(short callerNID, VM vm, bool offerTaxi)
        {
            VM = vm;
            var nb = Content.Get().Neighborhood;

            // SimsRedux: everyone the calling Sim knows, grouped by family. Anything missing (no
            // neighbour record, no person data, an unknown family) is skipped instead of crashing.
            try
            {
                var neigh = nb.GetNeighborByID(callerNID);
                var rels = neigh?.Relationships?.Keys?.ToList() ?? new List<int>();
                foreach (var to in rels)
                {
                    if (to == callerNID) continue;
                    var tn = nb.GetNeighborByID((short)to);
                    if (tn == null) continue;
                    var pd = tn.PersonData;
                    short family = (pd != null && pd.Length > (int)VMPersonDataVariable.TS1FamilyNumber) ? pd[(int)VMPersonDataVariable.TS1FamilyNumber] : (short)0;
                    short gender = (pd != null && pd.Length > (int)VMPersonDataVariable.Gender) ? pd[(int)VMPersonDataVariable.Gender] : (short)0; //can't call pets
                    if (family != 0 && gender < 2)
                    {
                        List<short> famList = null;
                        if (!NeighborsByFamilyID.TryGetValue(family, out famList))
                        {
                            famList = new List<short>();
                            NeighborsByFamilyID[family] = famList;
                            FamilyOrder.Add(family);
                        }

                        famList.Add((short)to);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] phone book: couldn't read who this Sim knows: " + e.Message);
                NeighborsByFamilyID.Clear();
                FamilyOrder.Clear();
            }

            FamilyList = new UITouchStringList();
            FamilyList.Size = new Microsoft.Xna.Framework.Vector2(320, 350);
            FamilyList.BackingList = FamilyOrder.Select(x => FamilyName(x)).ToList();
            FamilyList.Refresh();
            FamilyList.OnSelectionChange += FamilyList_OnSelectionChange;
            Add(FamilyList);

            NeighbourList = new UITouchStringList();
            NeighbourList.Size = new Microsoft.Xna.Framework.Vector2(320, 350);
            NeighbourList.Position = new Microsoft.Xna.Framework.Vector2(370, 0);
            NeighbourList.OnSelectionChange += NeighbourList_OnSelectionChange;
            Add(NeighbourList);

            if (FamilyOrder.Count == 0)
            {
                // SimsRedux: the Sim doesn't know anybody yet - say so (Cancel hangs up)
                EmptyLabel = new UILabel();
                EmptyLabel.CaptionStyle = EmptyLabel.CaptionStyle.Clone();
                EmptyLabel.CaptionStyle.Size = 19;
                EmptyLabel.CaptionStyle.Color = UIStyle.Current.Text;
                EmptyLabel.Wrapped = true;
                EmptyLabel.Alignment = TextAlignment.Center | TextAlignment.Middle;
                EmptyLabel.Position = new Microsoft.Xna.Framework.Vector2(20, 0);
                EmptyLabel.Size = new Microsoft.Xna.Framework.Vector2(650, 350);
                EmptyLabel.Caption = "Nobody to call yet.\n\nThis Sim hasn't met anyone - go and meet the neighbours first!";
                Add(EmptyLabel);
            }

            CancelButton = new UIBigButton(false);
            CancelButton.Caption = "Cancel";
            CancelButton.Position = new Microsoft.Xna.Framework.Vector2(370 + 385, 135);
            CancelButton.OnButtonClick += (btn) => { Answer(-1); };
            CancelButton.Width = 275;
            Add(CancelButton);

            CallButton = new UIBigButton(true);
            CallButton.Caption = "Call";
            CallButton.Position = new Microsoft.Xna.Framework.Vector2(370 + 385, 255);
            CallButton.OnButtonClick += (btn) => { if (SelectedNeighbour > 0) Answer(SelectedNeighbour); };
            CallButton.Width = 275;
            Add(CallButton);

            if (offerTaxi)
            {
                // SimsRedux: call a taxi to another lot instead
                TaxiButton = new UIBigButton(false);
                TaxiButton.Caption = "Call Taxi";
                TaxiButton.Position = new Microsoft.Xna.Framework.Vector2(370 + 385, 375);
                TaxiButton.OnButtonClick += (btn) =>
                {
                    if (Answered) return;
                    Answered = true;
                    DisableButtons();
                    OnTaxi?.Invoke();
                };
                TaxiButton.Width = 275;
                Add(TaxiButton);
            }

            NeighbourList_OnSelectionChange(-1);
        }

        private string FamilyName(short family)
        {
            try
            {
                var str = Content.Get().Neighborhood.GetFamilyString((ushort)family);
                var name = str?.GetString(0);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
            return "Family " + family;
        }

        private void DisableButtons()
        {
            CallButton.Disabled = true;
            CancelButton.Disabled = true;
            if (TaxiButton != null) TaxiButton.Disabled = true;
        }

        /// <summary>Answers the phone book once (a neighbour id, or -1 for cancel).</summary>
        private void Answer(int result)
        {
            if (Answered) return;
            Answered = true;
            DisableButtons();
            OnResult?.Invoke(result);
        }

        private void NeighbourList_OnSelectionChange(int obj)
        {
            if (Icon != null) { Remove(Icon); Icon = null; }
            List<short> people = null;
            if (obj < 0 || SelectedFamily < 0 || SelectedFamily >= FamilyOrder.Count
                || !NeighborsByFamilyID.TryGetValue(FamilyOrder[SelectedFamily], out people)
                || people == null || obj >= people.Count)
            {
                SelectedNeighbour = -1;
                CallButton.Disabled = true;
                return;
            }

            SelectedNeighbour = people[obj];
            CallButton.Disabled = Answered;

            try
            {
                var guid = Content.Get().Neighborhood.GetNeighborByID(SelectedNeighbour)?.GUID ?? 0;
                if (guid == 0) return;
                var temp = VM.Context.CreateObjectInstance(guid, LotTilePos.OUT_OF_WORLD, Direction.NORTH, true);
                if (temp?.BaseObject == null) return;
                Icon = new UIAvatarSelectButton(UIIconCache.GetObject(temp.BaseObject));
                Icon.Position = new Microsoft.Xna.Framework.Vector2(892, 60);
                Add(Icon);
                temp.Delete(VM.Context);
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] phone book: couldn't show that Sim's picture: " + e.Message);
            }
        }

        private void FamilyList_OnSelectionChange(int obj)
        {
            var nb = Content.Get().Neighborhood;
            //populate the rightmost list with the selected family
            SelectedFamily = (obj >= 0 && obj < FamilyOrder.Count) ? obj : -1;
            var names = new List<string>();
            List<short> people;
            if (SelectedFamily != -1 && NeighborsByFamilyID.TryGetValue(FamilyOrder[SelectedFamily], out people) && people != null)
            {
                names = people.Select(x => {
                        try
                        {
                            var n = nb.GetNeighborByID(x);
                            if (n == null) return "Unknown";
                            var gobj = Content.Get().WorldObjects.Get(n.GUID);
                            if (gobj == null) return string.IsNullOrEmpty(n.Name) ? "Unknown" : n.Name;
                            return gobj.Resource.Get<FSO.Files.Formats.IFF.Chunks.CTSS>(gobj.OBJ.CatalogStringsID)?.GetString(0) ?? "Unknown";
                        }
                        catch { return "Unknown"; }
                    }
                    ).ToList();
            }
            NeighbourList.BackingList = names;
            NeighbourList.Refresh();
            NeighbourList_OnSelectionChange(-1);
        }
    }
}
