using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SimsRedux.Creator.App
{
    /// <summary>
    /// SimsRedux: a big window showing a built item at all three game zooms (Near, Medium, Far) side by
    /// side, with crisp pixels and a size picker. "Save pictures..." writes them out as PNG files to send.
    /// </summary>
    public sealed class BigPreviewForm : Form
    {
        private readonly List<(string name, Image32 img)> Shots = new List<(string, Image32)>();
        private readonly List<PixelBox> Boxes = new List<PixelBox>();
        private readonly string ItemName;

        public BigPreviewForm(string kind, byte[] built, string itemName)
        {
            ItemName = string.IsNullOrWhiteSpace(itemName) ? "item" : itemName;
            Text = "Bigger preview - " + ItemName + " (" + kind + ")";
            Width = 1200; Height = 800;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(18, 22, 40);
            ForeColor = Color.FromArgb(225, 230, 255);
            Font = new Font("Segoe UI", 9.5f);

            for (int z = 0; z < 3; z++)
            {
                try { Shots.Add((Previews.ZoomNames[z], Previews.Render(kind, built, z))); }
                catch (Exception ex) { Console.WriteLine("Preview zoom " + z + " failed: " + ex.Message); }
            }

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = false };
            top.Controls.Add(new Label { Text = "Size:", AutoSize = true, Padding = new Padding(0, 6, 4, 0), ForeColor = ForeColor });
            var size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80, BackColor = Color.FromArgb(8, 10, 26), ForeColor = ForeColor };
            size.Items.AddRange(new object[] { "1x", "2x", "3x", "4x" });
            size.SelectedIndex = 1;
            size.SelectedIndexChanged += (s, e) => SetScale(size.SelectedIndex + 1);
            top.Controls.Add(size);
            var save = new Button { Text = "Save pictures...", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(32, 38, 70), ForeColor = Color.White, Margin = new Padding(12, 3, 3, 3) };
            save.Click += (s, e) => SavePictures();
            top.Controls.Add(save);
            top.Controls.Add(new Label { Text = "Near is the closest zoom. Each picture is exactly as the game stores it.", AutoSize = true, Padding = new Padding(12, 6, 0, 0), ForeColor = Color.FromArgb(160, 170, 210) });

            var scroll = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(8), BackColor = Color.FromArgb(10, 12, 30) };
            foreach (var (name, img) in Shots)
            {
                var cell = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(8) };
                cell.Controls.Add(new Label { Text = name + "  (" + img.Width + "×" + img.Height + ")", AutoSize = true, ForeColor = ForeColor });
                var box = new PixelBox { BackColor = Color.FromArgb(10, 12, 30), Image = Pictures.ToBitmap(img), Tag = img };
                Boxes.Add(box);
                cell.Controls.Add(box);
                scroll.Controls.Add(cell);
            }
            Controls.Add(scroll);
            Controls.Add(top);
            SetScale(2);
        }

        private void SetScale(int k)
        {
            foreach (var box in Boxes)
            {
                var img = (Image32)box.Tag;
                box.Zoom = k;
                box.Size = new Size(img.Width * k, img.Height * k);
                box.Invalidate();
            }
        }

        private void SavePictures()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Where should the preview pictures go?" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var safe = CreatorPack.FileNameFor(ItemName);
                    foreach (var (name, img) in Shots)
                        img.SavePng(Path.Combine(dlg.SelectedPath, safe + "_" + name.ToLowerInvariant() + ".png"));
                    MessageBox.Show(this, "Saved " + Shots.Count + " pictures to " + dlg.SelectedPath, Text);
                }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't save them: " + ex.Message, Text); }
            }
        }
    }
}
