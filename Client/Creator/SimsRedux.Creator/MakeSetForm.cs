using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SimsRedux.Creator.App
{
    /// <summary>
    /// SimsRedux: "Make a set" - tick several colours and the Creator makes one item per colour from the
    /// same texture, named "Name - Colour" (e.g. "Brick - Cream", "Brick - Brick Red").
    /// </summary>
    public sealed class MakeSetForm : Form
    {
        private readonly ListView List;
        private readonly ImageList Chips = new ImageList { ImageSize = new Size(22, 16), ColorDepth = ColorDepth.Depth32Bit };
        private readonly TextBox BaseName;
        private readonly List<Colours.Swatch> Swatches = new List<Colours.Swatch>(Colours.SetPresets);

        /// <summary>The base name typed in (each item gets " - Colour" added).</summary>
        public string SetName => BaseName.Text.Trim();

        /// <summary>The ticked colours.</summary>
        public List<Colours.Swatch> Chosen => List.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (Colours.Swatch)i.Tag).ToList();

        public MakeSetForm(string baseName, string kind)
        {
            Text = "Make a set of " + kind + "s";
            Width = 420; Height = 620;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            BackColor = Color.FromArgb(18, 22, 40);
            ForeColor = Color.FromArgb(225, 230, 255);
            Font = new Font("Segoe UI", 9.5f);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            root.Controls.Add(new Label { Text = "Name for the set:", AutoSize = true, ForeColor = ForeColor }, 0, 0);
            BaseName = new TextBox { Text = baseName, Dock = DockStyle.Top, BackColor = Color.FromArgb(8, 10, 26), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
            root.Controls.Add(BaseName, 0, 1);
            root.Controls.Add(new Label { Text = "Tick the colours to make (each becomes its own item):", AutoSize = true, Padding = new Padding(0, 8, 0, 4), ForeColor = ForeColor }, 0, 2);

            List = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None,
                SmallImageList = Chips, BackColor = Color.FromArgb(10, 12, 30), ForeColor = ForeColor, BorderStyle = BorderStyle.None
            };
            List.Columns.Add("Colour", 300);
            root.Controls.Add(List, 0, 3);
            FillList();

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var custom = Btn("Add colour...", false);
            custom.Click += (s, e) => AddCustom();
            var all = Btn("Tick all", false);
            all.Click += (s, e) => { foreach (ListViewItem i in List.Items) i.Checked = true; };
            var none = Btn("Untick all", false);
            none.Click += (s, e) => { foreach (ListViewItem i in List.Items) i.Checked = false; };
            var ok = Btn("Make them", true);
            ok.Click += (s, e) =>
            {
                if (SetName.Length == 0) { MessageBox.Show(this, "Give the set a name first.", Text); return; }
                if (Chosen.Count == 0) { MessageBox.Show(this, "Tick at least one colour.", Text); return; }
                DialogResult = DialogResult.OK;
            };
            var cancel = Btn("Cancel", false);
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            buttons.Controls.AddRange(new Control[] { custom, all, none, ok, cancel });
            root.Controls.Add(buttons, 0, 4);
            AcceptButton = ok; CancelButton = cancel;
        }

        private Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(3),
                BackColor = primary ? Color.FromArgb(20, 88, 176) : Color.FromArgb(32, 38, 70), ForeColor = Color.White
            };
            b.FlatAppearance.BorderColor = primary ? Color.FromArgb(120, 170, 240) : Color.FromArgb(80, 90, 140);
            return b;
        }

        private void FillList() => FillList(new HashSet<string>(Chosen.Select(c => c.Name)));

        private void FillList(HashSet<string> ticked)
        {
            List.BeginUpdate();
            List.Items.Clear();
            foreach (Image old in Chips.Images) old.Dispose();
            Chips.Images.Clear();
            foreach (var sw in Swatches)
            {
                var chip = new Bitmap(22, 16);
                using (var g = Graphics.FromImage(chip))
                {
                    g.Clear(Color.Transparent);
                    using (var b = new SolidBrush(Color.FromArgb(sw.R, sw.G, sw.B))) g.FillRectangle(b, 1, 1, 20, 14);
                    g.DrawRectangle(Pens.Gray, 1, 1, 19, 13);
                }
                Chips.Images.Add(chip);
                List.Items.Add(new ListViewItem(sw.Name + "   " + sw.Hex, Chips.Images.Count - 1) { Tag = sw, Checked = ticked.Contains(sw.Name) });
            }
            List.EndUpdate();
        }

        private void AddCustom()
        {
            using (var dlg = new ColorDialog { FullOpen = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var name = Prompt("Name for this colour:", Colours.ToHex(dlg.Color.R, dlg.Color.G, dlg.Color.B));
                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim();
                if (Swatches.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "There's already a colour called " + name + ".", Text); return; }
                var keep = new HashSet<string>(Chosen.Select(c => c.Name)) { name };
                Swatches.Add(new Colours.Swatch(name, dlg.Color.R, dlg.Color.G, dlg.Color.B));
                FillList(keep);
            }
        }

        private string Prompt(string question, string value)
        {
            using (var f = new Form { Text = Text, Width = 360, Height = 150, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = BackColor, ForeColor = ForeColor, Font = Font })
            {
                var lbl = new Label { Text = question, AutoSize = true, Left = 10, Top = 10, ForeColor = ForeColor };
                var box = new TextBox { Text = value, Left = 10, Top = 34, Width = 320, BackColor = Color.FromArgb(8, 10, 26), ForeColor = ForeColor };
                var ok = Btn("OK", true); ok.Left = 250; ok.Top = 66; ok.DialogResult = DialogResult.OK;
                f.Controls.AddRange(new Control[] { lbl, box, ok });
                f.AcceptButton = ok;
                return f.ShowDialog(this) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
