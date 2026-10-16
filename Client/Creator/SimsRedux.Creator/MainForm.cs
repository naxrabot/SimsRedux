using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimsRedux.Creator.App
{
    /// <summary>
    /// SimsRedux: the Sims Redux Creator's window.
    ///   1. Pick your Sims 3 folder and press Scan: every texture in its packages shows as a thumbnail
    ///      (or press "Open picture..." to use any picture file).
    ///   2. Click one, choose Floor, Wall or Picture, give it a name and price, press Preview.
    ///   3. "Add to Test" writes it into Content\redux\creator - loaded on the Test server only.
    ///   4. Once it's good, "Promote to Live" moves it into Content\redux\mods for everyone.
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly Settings Settings = Settings.Load();

        // the texture browser
        private sealed class Entry
        {
            public Sims3Texture Texture;   // from a Sims 3 package (or null)
            public Sims3Package Package;
            public string FilePath;        // or a picture file
            public string Label;
            public int Width, Height;
            public TextureKind Kind = TextureKind.Usable; // (sorted while scanning)
        }
        private readonly List<Entry> All = new List<Entry>();
        private List<Entry> Listed = new List<Entry>();
        private readonly List<Sims3Package> Packages = new List<Sims3Package>();
        private readonly ConcurrentDictionary<Entry, int> ThumbIndex = new ConcurrentDictionary<Entry, int>();
        private readonly ConcurrentQueue<Entry> ThumbQueue = new ConcurrentQueue<Entry>();
        private readonly HashSet<Entry> ThumbQueued = new HashSet<Entry>();
        private CancellationTokenSource ScanCancel;
        private const int ThumbSize = 96;

        // the item being made
        private Entry Current;
        private Image32 CurrentImage;   // the texture as it is
        private Color? Tint;            // a colour picked for it (grey Sims 3 layers need one)
        private Button TintBtn;

        /// <summary>The picture that gets built: the texture, coloured in if a colour was picked.</summary>
        private Image32 Working => Tint.HasValue && CurrentImage != null ? Colourise(CurrentImage, Tint.Value) : CurrentImage;
        private byte[] Built;
        private string BuiltKind;
        private CreatorPack Pack;

        // controls
        private TextBox Sims3Box, ReduxBox, SearchBox, NameBox, DescBox;
        private ComboBox ShapeFilter;
        private ListView Browser, PackList;
        private ImageList Thumbs;
        private PictureBox SourcePic, PreviewPic;
        private RadioButton KindFloor, KindWall, KindPicture;
        private NumericUpDown PriceBox;
        private Button ScanBtn, AddBtn, PreviewBtn;
        private Label Status, SourceInfo;
        private System.Windows.Forms.Timer ThumbTimer;

        public MainForm()
        {
            Text = "Sims Redux Creator";
            Width = 1280; Height = 860;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 680);
            BackColor = Color.FromArgb(18, 22, 40);
            ForeColor = Color.FromArgb(225, 230, 255);
            Font = new Font("Segoe UI", 9.5f);
            BuildUi();
            LoadPack();
            ThumbTimer = new System.Windows.Forms.Timer { Interval = 120 };
            ThumbTimer.Tick += (s, e) => PumpThumbs();
            ThumbTimer.Start();
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // folders
            var folders = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true };
            folders.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folders.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folders.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Sims3Box = FolderRow(folders, 0, "Sims 3 folder:", Settings.Sims3Folder, (p) => { Settings.Sims3Folder = p; Settings.Save(); });
            ScanBtn = MakeButton("Scan", true);
            ScanBtn.Click += (s, e) => Scan();
            folders.Controls.Add(ScanBtn, 3, 0);
            ReduxBox = FolderRow(folders, 1, "Game content (Content\\redux):", Settings.ReduxFolder, (p) => { Settings.ReduxFolder = p; Settings.Save(); LoadPack(); });
            root.Controls.Add(folders, 0, 0);

            // browser | editor
            var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = BackColor };
            Shown += (s, e) => { try { split.SplitterDistance = (int)(split.Width * 0.58); } catch { } };
            root.Controls.Add(split, 0, 1);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            filters.Controls.Add(MakeLabel("Search:"));
            SearchBox = new TextBox { Width = 220, BackColor = Color.FromArgb(8, 10, 26), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
            SearchBox.TextChanged += (s, e) => ApplyFilter();
            filters.Controls.Add(SearchBox);
            ShapeFilter = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(8, 10, 26), ForeColor = ForeColor };
            ShapeFilter.Items.AddRange(new object[] { "Usable textures only", "Usable - square (floors)", "Usable - tall (walls)", "Grey (pick a colour)", "All textures (128px and up)", "Everything" });
            ShapeFilter.SelectedIndex = 0;
            ShapeFilter.SelectedIndexChanged += (s, e) => ApplyFilter();
            filters.Controls.Add(ShapeFilter);
            var openBtn = MakeButton("Open picture...", false);
            openBtn.Click += (s, e) => OpenPictureFile();
            filters.Controls.Add(openBtn);
            left.Controls.Add(filters, 0, 0);

            Thumbs = new ImageList { ImageSize = new Size(ThumbSize, ThumbSize), ColorDepth = ColorDepth.Depth32Bit };
            Thumbs.Images.Add(new Bitmap(ThumbSize, ThumbSize)); // 0 = still loading
            Browser = new ListView
            {
                Dock = DockStyle.Fill, View = View.LargeIcon, VirtualMode = true, LargeImageList = Thumbs, MultiSelect = false,
                BackColor = Color.FromArgb(10, 12, 30), ForeColor = ForeColor, BorderStyle = BorderStyle.None, HideSelection = false
            };
            Browser.RetrieveVirtualItem += Browser_RetrieveVirtualItem;
            Browser.SelectedIndexChanged += (s, e) => { if (Browser.SelectedIndices.Count > 0) PickEntry(Listed[Browser.SelectedIndices[0]]); };
            left.Controls.Add(Browser, 0, 1);
            split.Panel1.Controls.Add(left);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(8, 0, 0, 0) };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            SourcePic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Height = 200, BackColor = Color.FromArgb(10, 12, 30) };
            right.Controls.Add(SourcePic, 0, 0); right.SetColumnSpan(SourcePic, 2);
            SourceInfo = MakeLabel("Pick a texture on the left, or open a picture file.");
            right.Controls.Add(SourceInfo, 0, 1); right.SetColumnSpan(SourceInfo, 2);

            var kinds = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            KindFloor = new RadioButton { Text = "Floor", AutoSize = true, Checked = true, ForeColor = ForeColor };
            KindWall = new RadioButton { Text = "Wall", AutoSize = true, ForeColor = ForeColor };
            KindPicture = new RadioButton { Text = "Picture / poster", AutoSize = true, ForeColor = ForeColor };
            foreach (var rb in new[] { KindFloor, KindWall, KindPicture })
            {
                rb.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) { Built = null; PreviewPic.Image = null; SuggestPrice(); } };
                kinds.Controls.Add(rb);
            }
            right.Controls.Add(MakeLabel("Make into:"), 0, 2); right.Controls.Add(kinds, 1, 2);
            NameBox = MakeText(); right.Controls.Add(MakeLabel("Name:"), 0, 3); right.Controls.Add(NameBox, 1, 3);
            PriceBox = new NumericUpDown { Minimum = 0, Maximum = 99999, Value = 10, Width = 100, BackColor = Color.FromArgb(8, 10, 26), ForeColor = ForeColor };
            var priceRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
            priceRow.Controls.Add(PriceBox);
            TintBtn = MakeButton("Colour...", false);
            TintBtn.Click += (s, e) => PickTint();
            var noTint = MakeButton("No colour", false);
            noTint.Click += (s, e) => SetTint(null);
            priceRow.Controls.Add(TintBtn); priceRow.Controls.Add(noTint);
            right.Controls.Add(MakeLabel("Price (§):"), 0, 4); right.Controls.Add(priceRow, 1, 4);
            DescBox = MakeText(); DescBox.Multiline = true; DescBox.Height = 54;
            right.Controls.Add(MakeLabel("Description:"), 0, 5); right.Controls.Add(DescBox, 1, 5);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            PreviewBtn = MakeButton("Preview in game look", false);
            PreviewBtn.Click += (s, e) => BuildPreview();
            AddBtn = MakeButton("Add to Test", true);
            AddBtn.Click += (s, e) => AddToTest();
            buttons.Controls.Add(PreviewBtn); buttons.Controls.Add(AddBtn);
            right.Controls.Add(buttons, 0, 6); right.SetColumnSpan(buttons, 2);
            PreviewPic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(10, 12, 30) };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 210)); // source
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // preview
            right.Controls.Add(PreviewPic, 0, 7); right.SetColumnSpan(PreviewPic, 2);
            split.Panel2.Controls.Add(right);

            // the Test pack
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            PackList = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true,
                BackColor = Color.FromArgb(10, 12, 30), ForeColor = ForeColor, BorderStyle = BorderStyle.None
            };
            PackList.Columns.Add("On the Test server", 260);
            PackList.Columns.Add("Kind", 80);
            PackList.Columns.Add("Price", 70);
            PackList.Columns.Add("File", 380);
            bottom.Controls.Add(PackList, 0, 0);
            var packButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
            var removeBtn = MakeButton("Remove", false);
            removeBtn.Click += (s, e) => RemoveSelected();
            var promoteBtn = MakeButton("Promote to Live...", true);
            promoteBtn.Click += (s, e) => PromoteSelected();
            var folderBtn = MakeButton("Open folder", false);
            folderBtn.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", Settings.CreatorFolder); } catch { } };
            packButtons.Controls.Add(removeBtn); packButtons.Controls.Add(promoteBtn); packButtons.Controls.Add(folderBtn);
            bottom.Controls.Add(packButtons, 1, 0);
            root.Controls.Add(bottom, 0, 2);

            Status = MakeLabel("Ready. Pick your Sims 3 folder and press Scan.");
            root.Controls.Add(Status, 0, 3);
        }

        private TextBox FolderRow(TableLayoutPanel grid, int row, string label, string value, Action<string> changed)
        {
            grid.Controls.Add(MakeLabel(label), 0, row);
            var box = MakeText();
            box.Text = value;
            box.Dock = DockStyle.Fill;
            box.Leave += (s, e) => changed(box.Text.Trim());
            grid.Controls.Add(box, 1, row);
            var browse = MakeButton("Browse...", false);
            browse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { SelectedPath = Directory.Exists(box.Text) ? box.Text : "" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    box.Text = dlg.SelectedPath;
                    changed(box.Text);
                }
            };
            grid.Controls.Add(browse, 2, row);
            return box;
        }

        private Label MakeLabel(string text) => new Label { Text = text, AutoSize = true, Padding = new Padding(0, 6, 6, 0), ForeColor = ForeColor };

        private TextBox MakeText() => new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(8, 10, 26), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };

        private Button MakeButton(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(3),
                BackColor = primary ? Color.FromArgb(20, 88, 176) : Color.FromArgb(32, 38, 70), ForeColor = Color.White
            };
            b.FlatAppearance.BorderColor = primary ? Color.FromArgb(120, 170, 240) : Color.FromArgb(80, 90, 140);
            return b;
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text))); return; }
            Status.Text = text;
        }

        // ------------------------------------------------------------------
        // Scanning Sims 3
        // ------------------------------------------------------------------

        private void Scan()
        {
            var folder = Sims3Box.Text.Trim();
            Settings.Sims3Folder = folder; Settings.Save();
            if (!Directory.Exists(folder)) { MessageBox.Show(this, "That Sims 3 folder doesn't exist.", Text); return; }
            ScanCancel?.Cancel();
            var cancel = ScanCancel = new CancellationTokenSource();
            foreach (var p in Packages) p.Dispose();
            Packages.Clear();
            All.RemoveAll(e => e.Texture != null);
            ClearThumbs();
            ApplyFilter();
            ScanBtn.Enabled = false;
            SetStatus("Looking for Sims 3 packages...");
            Task.Run(() =>
            {
                List<string> files;
                try { files = Sims3Library.FindPackages(folder).ToList(); }
                catch (Exception ex) { SetStatus("Couldn't search that folder: " + ex.Message); return; }
                int done = 0, found = 0;
                foreach (var file in files)
                {
                    if (cancel.IsCancellationRequested) return;
                    done++;
                    try
                    {
                        var pkg = Sims3Package.Open(file);
                        var batch = new List<Entry>();
                        foreach (var t in pkg.ListTextures())
                            batch.Add(new Entry { Texture = t, Package = pkg, Label = t.DisplayName, Width = t.Width, Height = t.Height });
                        if (batch.Count == 0) { pkg.Dispose(); continue; }
                        SetStatus("Sorting " + batch.Count + " textures in " + Path.GetFileName(file) + " (" + done + " of " + files.Count + ")...");
                        SortBatch(pkg, batch, cancel.Token);
                        found += batch.Count;
                        BeginInvoke(new Action(() =>
                        {
                            if (cancel.IsCancellationRequested) { pkg.Dispose(); return; }
                            Packages.Add(pkg);
                            All.AddRange(batch);
                            ApplyFilter();
                        }));
                    }
                    catch (Exception ex) { Console.WriteLine("Skipped " + file + ": " + ex.Message); }
                    SetStatus("Scanning " + done + " of " + files.Count + " packages - " + found + " textures so far...");
                }
                SaveSortCache();
                SetStatus("Found " + found + " textures in " + files.Count + " packages (" + All.Count(x => x.Kind == TextureKind.Usable && Math.Min(x.Width, x.Height) >= 128) + " usable). Click one to make it into a floor, wall or picture.");
                BeginInvoke(new Action(() => ScanBtn.Enabled = true));
            });
        }

        // ------------------------------------------------------------------
        // Sorting out the usable textures (remembered between scans)
        // ------------------------------------------------------------------

        private static string SortCachePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimsReduxCreator", "sorted.txt");
        private ConcurrentDictionary<string, TextureKind> SortCache;

        // (the "v3" changes whenever the sorting rules do, so old answers aren't reused)
        private static string SortKey(Entry e) => "v3|" + e.Texture.PackagePath + "|" + e.Texture.Resource.Instance.ToString("X16") + "|" + e.Width + "x" + e.Height;

        private void LoadSortCache()
        {
            if (SortCache != null) return;
            SortCache = new ConcurrentDictionary<string, TextureKind>();
            try
            {
                if (!File.Exists(SortCachePath)) return;
                foreach (var line in File.ReadAllLines(SortCachePath))
                {
                    int i = line.LastIndexOf('=');
                    if (i > 0 && int.TryParse(line.Substring(i + 1), out var k)) SortCache[line.Substring(0, i)] = (TextureKind)k;
                }
            }
            catch { }
        }

        private void SaveSortCache()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SortCachePath));
                File.WriteAllLines(SortCachePath, SortCache.Select(p => p.Key + "=" + (int)p.Value));
            }
            catch { }
        }

        /// <summary>
        /// Sorts a package's textures: by name first (free), then by looking at the picture - several at a
        /// time (the package file is read one at a time, the decoding and looking happen side by side).
        /// </summary>
        private void SortBatch(Sims3Package pkg, List<Entry> batch, CancellationToken cancel)
        {
            LoadSortCache();
            var toLook = new List<Entry>();
            foreach (var e in batch)
            {
                e.Kind = TextureSorter.ByName(e.Texture.Name);
                if (e.Kind != TextureKind.Usable || Math.Min(e.Width, e.Height) < 128) continue;
                if (SortCache.TryGetValue(SortKey(e), out var known)) e.Kind = known;
                else toLook.Add(e);
            }
            try
            {
                Parallel.ForEach(toLook, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1), CancellationToken = cancel }, e =>
                {
                    try
                    {
                        byte[] data;
                        lock (pkg) data = pkg.Read(e.Texture.Resource);
                        e.Kind = TextureSorter.ByLook(Dds.Decode(data));
                    }
                    catch { e.Kind = TextureKind.Blank; } // (can't be read: not usable)
                    SortCache[SortKey(e)] = e.Kind;
                });
            }
            catch (OperationCanceledException) { }
        }

        private void OpenPictureFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.dds|All files|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var img = Pictures.LoadFile(dlg.FileName);
                    var e = new Entry { FilePath = dlg.FileName, Label = Path.GetFileNameWithoutExtension(dlg.FileName), Width = img.Width, Height = img.Height };
                    All.Insert(0, e);
                    ApplyFilter();
                    PickEntry(e, img);
                }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't open that picture: " + ex.Message, Text); }
            }
        }

        private void ApplyFilter()
        {
            var q = (SearchBox.Text ?? "").Trim();
            int shape = ShapeFilter.SelectedIndex;
            Listed = All.Where(e =>
            {
                if (q.Length > 0 && (e.Label ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) return false;
                if (e.FilePath != null || shape == 5) return true;
                if (Math.Min(e.Width, e.Height) < 128) return false;
                if (shape == 4) return true;
                if (shape == 3) return e.Kind == TextureKind.Greyscale;
                // the "usable" views: real pictures only (helper maps, masks, effects and blanks are sorted out)
                if (e.Kind != TextureKind.Usable) return false;
                if (shape == 1) return e.Width == e.Height;
                if (shape == 2) return e.Height >= e.Width * 1.5;
                return true;
            }).ToList();
            Browser.VirtualListSize = Listed.Count;
            Browser.Invalidate();
        }

        // ------------------------------------------------------------------
        // Thumbnails (decoded in the background, a few at a time)
        // ------------------------------------------------------------------

        private void ClearThumbs()
        {
            ThumbIndex.Clear();
            lock (ThumbQueued) ThumbQueued.Clear();
            while (ThumbQueue.TryDequeue(out _)) { }
            while (Thumbs.Images.Count > 1) { var img = Thumbs.Images[Thumbs.Images.Count - 1]; Thumbs.Images.RemoveAt(Thumbs.Images.Count - 1); img.Dispose(); }
        }

        private void Browser_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= Listed.Count) { e.Item = new ListViewItem("?"); return; }
            var entry = Listed[e.ItemIndex];
            int idx;
            if (!ThumbIndex.TryGetValue(entry, out idx))
            {
                idx = 0;
                lock (ThumbQueued) { if (ThumbQueued.Add(entry)) ThumbQueue.Enqueue(entry); }
            }
            var label = entry.Label ?? "";
            if (label.Length > 22) label = label.Substring(0, 21) + "…";
            e.Item = new ListViewItem(label + "\n" + entry.Width + "×" + entry.Height, idx);
        }

        private int ThumbWorkers;
        private void PumpThumbs()
        {
            while (ThumbWorkers < 3 && ThumbQueue.TryDequeue(out var entry))
            {
                Interlocked.Increment(ref ThumbWorkers);
                Task.Run(() =>
                {
                    Bitmap thumb = null;
                    try { thumb = Pictures.Thumbnail(LoadImage(entry), ThumbSize); }
                    catch { }
                    finally { Interlocked.Decrement(ref ThumbWorkers); }
                    if (thumb == null) return;
                    BeginInvoke(new Action(() =>
                    {
                        Thumbs.Images.Add(thumb);
                        ThumbIndex[entry] = Thumbs.Images.Count - 1;
                        int i = Listed.IndexOf(entry);
                        if (i >= 0 && i < Browser.VirtualListSize) Browser.RedrawItems(i, i, true);
                    }));
                });
            }
        }

        private Image32 LoadImage(Entry e)
        {
            if (e.FilePath != null) return Pictures.LoadFile(e.FilePath);
            lock (e.Package) return e.Package.LoadTexture(e.Texture);
        }

        // ------------------------------------------------------------------
        // Making an item
        // ------------------------------------------------------------------

        private void PickEntry(Entry e, Image32 img = null)
        {
            try
            {
                Current = e;
                CurrentImage = img ?? LoadImage(e);
                Built = null;
                PreviewPic.Image = null;
                Tint = null;
                TintBtn.BackColor = Color.FromArgb(32, 38, 70);
                SourcePic.Image = Pictures.ToBitmap(CurrentImage);
                if (e.Kind == TextureKind.Greyscale) SetStatus("This one is grey - Sims 3 colours it in while playing. Press Colour... to pick its colour.");
                SourceInfo.Text = (e.Label ?? "") + "   " + CurrentImage.Width + "×" + CurrentImage.Height
                    + (e.Texture != null ? "   from " + Path.GetFileName(e.Texture.PackagePath) : "");
                if (e.Texture != null)
                {
                    // a guess from the shape: square = floor, tall = wall
                    if (CurrentImage.Height >= CurrentImage.Width * 1.5) KindWall.Checked = true;
                    else if (CurrentImage.Width == CurrentImage.Height) KindFloor.Checked = true;
                }
                if (string.IsNullOrWhiteSpace(NameBox.Text) || NameBox.Tag as string == "auto")
                {
                    NameBox.Text = Tidy(e.Label);
                    NameBox.Tag = "auto";
                }
                SuggestPrice();
            }
            catch (Exception ex) { SetStatus("Couldn't read that texture: " + ex.Message); }
        }

        private static string Tidy(string label)
        {
            if (string.IsNullOrEmpty(label) || label.StartsWith("0x")) return "";
            var s = label.Replace('_', ' ').Trim();
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        private string Kind => KindWall.Checked ? "wall" : KindPicture.Checked ? "picture" : "floor";

        private void SuggestPrice()
        {
            if (PriceBox == null) return;
            PriceBox.Value = Kind == "floor" ? 10 : Kind == "wall" ? 14 : 120;
        }

        private byte[] PosterTemplate()
        {
            var path = Path.Combine(Settings.ModsFolder, "Overrides", "PaintingPosterSuperstar.iff");
            if (!File.Exists(path)) throw new FileNotFoundException("The poster template wasn't found at " + path + ". Check the Game content folder.");
            return File.ReadAllBytes(path);
        }

        private bool BuildCurrent()
        {
            if (CurrentImage == null) { MessageBox.Show(this, "Pick a texture or open a picture first.", Text); return false; }
            var source = Working;
            var name = NameBox.Text.Trim();
            if (name.Length == 0) { MessageBox.Show(this, "Give it a name first.", Text); NameBox.Focus(); return false; }
            try
            {
                Cursor = Cursors.WaitCursor;
                var kind = Kind;
                int price = (int)PriceBox.Value;
                var desc = DescBox.Text.Trim();
                if (kind == "floor") Built = FloorBuilder.Build(source, name, price, desc);
                else if (kind == "wall") Built = WallBuilder.Build(source, name, price, desc);
                else Built = PictureBuilder.Build(source, name, price, desc, PosterTemplate());
                BuiltKind = kind;
                PreviewPic.Image = Pictures.ToBitmap(Previews.Render(kind, Built));
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't make it: " + ex.Message, Text);
                return false;
            }
            finally { Cursor = Cursors.Default; }
        }

        private void PickTint()
        {
            if (CurrentImage == null) return;
            using (var dlg = new ColorDialog { FullOpen = true, Color = Tint ?? Color.FromArgb(180, 140, 100) })
                if (dlg.ShowDialog(this) == DialogResult.OK) SetTint(dlg.Color);
        }

        private void SetTint(Color? c)
        {
            Tint = c;
            TintBtn.BackColor = c ?? Color.FromArgb(32, 38, 70);
            if (CurrentImage != null) SourcePic.Image = Pictures.ToBitmap(Working);
            Built = null;
            PreviewPic.Image = null;
        }

        /// <summary>
        /// Colours a texture in: its average brightness becomes the picked colour, lighter and darker parts
        /// stay lighter and darker (how Sims 3 colours its grey shading layers).
        /// </summary>
        private static Image32 Colourise(Image32 src, Color c)
        {
            var o = new Image32(src.Width, src.Height);
            double sum = 0; int n = 0;
            for (int i = 0; i < src.Rgba.Length; i += 4)
            {
                if (src.Rgba[i + 3] < 128) continue;
                sum += 0.299 * src.Rgba[i] + 0.587 * src.Rgba[i + 1] + 0.114 * src.Rgba[i + 2]; n++;
            }
            double mean = n == 0 ? 128 : Math.Max(1, sum / n);
            for (int i = 0; i < src.Rgba.Length; i += 4)
            {
                double l = 0.299 * src.Rgba[i] + 0.587 * src.Rgba[i + 1] + 0.114 * src.Rgba[i + 2];
                double k = l / mean;
                o.Rgba[i] = (byte)Math.Max(0, Math.Min(255, c.R * k));
                o.Rgba[i + 1] = (byte)Math.Max(0, Math.Min(255, c.G * k));
                o.Rgba[i + 2] = (byte)Math.Max(0, Math.Min(255, c.B * k));
                o.Rgba[i + 3] = src.Rgba[i + 3];
            }
            return o;
        }

        private void BuildPreview()
        {
            if (BuildCurrent()) SetStatus("This is how it will look in the game (closest zoom).");
        }

        private void AddToTest()
        {
            if (!BuildCurrent() || Pack == null) return;
            try
            {
                var source = Current?.Texture != null
                    ? Path.GetFileName(Current.Texture.PackagePath) + " " + Current.Texture.Resource.Instance.ToString("X16")
                    : Current?.FilePath ?? "";
                if (Tint.HasValue) source += " colour #" + Tint.Value.R.ToString("X2") + Tint.Value.G.ToString("X2") + Tint.Value.B.ToString("X2");
                var item = Pack.Add(BuiltKind, NameBox.Text.Trim(), (int)PriceBox.Value, DescBox.Text.Trim(), source, Built);
                Pack.Save();
                FillPackList();
                SetStatus("Added \"" + item.Name + "\" to the Test content (" + item.File + "). Rebuild the game to try it on the Test server.");
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't save it: " + ex.Message, Text); }
        }

        // ------------------------------------------------------------------
        // The Test pack, and promoting items to Live
        // ------------------------------------------------------------------

        private void LoadPack()
        {
            try
            {
                Directory.CreateDirectory(Settings.CreatorFolder);
                Pack = CreatorPack.Open(Settings.CreatorFolder);
            }
            catch (Exception ex)
            {
                Pack = null;
                SetStatus("Couldn't open the creator folder: " + ex.Message);
            }
            FillPackList();
        }

        private void FillPackList()
        {
            if (PackList == null) return;
            PackList.BeginUpdate();
            PackList.Items.Clear();
            if (Pack != null)
                foreach (var item in Pack.Items)
                {
                    var row = new ListViewItem(new[] { item.Name, item.Kind, "§" + item.Price, item.File }) { Tag = item };
                    PackList.Items.Add(row);
                }
            PackList.EndUpdate();
        }

        private List<CreatorItem> SelectedItems() =>
            PackList.SelectedItems.Cast<ListViewItem>().Select(r => r.Tag as CreatorItem).Where(i => i != null).ToList();

        private void RemoveSelected()
        {
            var items = SelectedItems();
            if (items.Count == 0 || Pack == null) return;
            if (MessageBox.Show(this, "Remove " + items.Count + " item(s) from the Test content?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (var i in items) Pack.Remove(i);
            Pack.Save();
            FillPackList();
        }

        /// <summary>
        /// Moves items from Content\redux\creator (Test) into Content\redux\mods (everyone): the file moves
        /// to the same place under mods\, and mods\manifest.txt gets its line.
        /// </summary>
        private void PromoteSelected()
        {
            var items = SelectedItems();
            if (items.Count == 0 || Pack == null) { MessageBox.Show(this, "Select the items to promote first.", Text); return; }
            if (MessageBox.Show(this, "Move " + items.Count + " item(s) to Live?\n\nEveryone gets them with the next game update.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                var manifest = Path.Combine(Settings.ModsFolder, "manifest.txt");
                var lines = File.Exists(manifest) ? File.ReadAllLines(manifest).ToList() : new List<string> { "# SimsRedux: mods that ship with the game (one file per line, relative to this folder)" };
                foreach (var item in items)
                {
                    var from = Path.Combine(Settings.CreatorFolder, item.File.Replace('/', Path.DirectorySeparatorChar));
                    var to = Path.Combine(Settings.ModsFolder, item.File.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(from)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(from, to, true);
                    if (!lines.Any(l => l.Trim().Equals(item.File, StringComparison.OrdinalIgnoreCase))) lines.Add(item.File);
                    Pack.Remove(item);
                }
                File.WriteAllText(manifest, string.Join("\n", lines) + "\n");
                Pack.Save();
                FillPackList();
                SetStatus("Promoted " + items.Count + " item(s) to Live. Rebuild the game and send out the update.");
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't promote: " + ex.Message, Text); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ScanCancel?.Cancel();
            ThumbTimer?.Stop();
            foreach (var p in Packages) p.Dispose();
            base.OnFormClosed(e);
        }
    }
}
