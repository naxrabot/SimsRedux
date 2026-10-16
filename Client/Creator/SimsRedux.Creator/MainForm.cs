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
    /// Items on Test can be opened again (double-click) to change them, and "Rebuild all" re-makes every
    /// Test item from its texture with the current Creator (after the builders get better).
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

        // packages opened just to reopen an item (without a scan); closed with the window
        private readonly Dictionary<string, Sims3Package> OpenedForItems = new Dictionary<string, Sims3Package>(StringComparer.OrdinalIgnoreCase);
        private readonly object PackageLock = new object();

        // the item being made
        private Entry Current;
        private Image32 CurrentImage;   // the texture as it is
        private Color? Tint;            // a colour picked for it (grey Sims 3 layers need one)
        private Button TintBtn;
        private CreatorItem Editing;    // a Test item being changed (null = making a new one)

        /// <summary>The picture that gets built: the texture, coloured in if a colour was picked.</summary>
        private Image32 Working => Tint.HasValue && CurrentImage != null ? Colours.Colourise(CurrentImage, Tint.Value.R, Tint.Value.G, Tint.Value.B) : CurrentImage;
        private byte[] Built;
        private string BuiltKind;
        private CreatorPack Pack;
        private bool Busy;

        // controls
        private TextBox Sims3Box, ReduxBox, SearchBox, NameBox, DescBox;
        private ComboBox ShapeFilter;
        private ListView Browser, PackList;
        private ImageList Thumbs;
        private PictureBox SourcePic;
        private PixelBox PreviewPic;
        private RadioButton KindFloor, KindWall, KindPicture;
        private NumericUpDown PriceBox;
        private Button ScanBtn, AddBtn, PreviewBtn, BigBtn, SetBtn, NewBtn;
        private Label Status, SourceInfo, EditInfo;
        private SplitContainer Split;
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
            RestoreWindow();
            BuildUi();
            LoadPack();
            ThumbTimer = new System.Windows.Forms.Timer { Interval = 120 };
            ThumbTimer.Tick += (s, e) => PumpThumbs();
            ThumbTimer.Start();
            Shown += (s, e) => ReopenLastItem();
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
            Split = new SplitContainer { Dock = DockStyle.Fill, BackColor = BackColor };
            Shown += (s, e) =>
            {
                try { Split.SplitterDistance = (int)(Split.Width * Math.Max(0.3, Math.Min(0.8, Settings.SplitFraction))); } catch { }
            };
            root.Controls.Add(Split, 0, 1);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            filters.Controls.Add(MakeLabel("Search:"));
            SearchBox = new TextBox { Width = 220, BackColor = Color.FromArgb(8, 10, 26), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle, Text = Settings.Search ?? "" };
            SearchBox.TextChanged += (s, e) => { ApplyFilter(); Settings.Search = SearchBox.Text; };
            filters.Controls.Add(SearchBox);
            ShapeFilter = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(8, 10, 26), ForeColor = ForeColor };
            ShapeFilter.Items.AddRange(new object[] { "Usable textures only", "Usable - square (floors)", "Usable - tall (walls)", "Grey (pick a colour)", "All textures (128px and up)", "Everything" });
            ShapeFilter.SelectedIndex = Settings.ViewIndex >= 0 && Settings.ViewIndex < ShapeFilter.Items.Count ? Settings.ViewIndex : 0;
            ShapeFilter.SelectedIndexChanged += (s, e) => { ApplyFilter(); Settings.ViewIndex = ShapeFilter.SelectedIndex; };
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
            Split.Panel1.Controls.Add(left);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 10, Padding = new Padding(8, 0, 0, 0) };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // row 0: editing banner (only while changing a Test item)
            var editRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Visible = false, BackColor = Color.FromArgb(70, 56, 12) };
            EditInfo = new Label { AutoSize = true, Padding = new Padding(4, 6, 6, 4), ForeColor = Color.FromArgb(255, 236, 170) };
            NewBtn = MakeButton("Stop editing (make a new item)", false);
            NewBtn.Click += (s, e) => StopEditing();
            editRow.Controls.Add(EditInfo); editRow.Controls.Add(NewBtn);
            right.Controls.Add(editRow, 0, 0); right.SetColumnSpan(editRow, 2);
            EditInfo.Tag = editRow;

            SourcePic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Height = 200, BackColor = Color.FromArgb(10, 12, 30) };
            right.Controls.Add(SourcePic, 0, 1); right.SetColumnSpan(SourcePic, 2);
            SourceInfo = MakeLabel("Pick a texture on the left, or open a picture file.");
            right.Controls.Add(SourceInfo, 0, 2); right.SetColumnSpan(SourceInfo, 2);

            var kinds = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            KindFloor = new RadioButton { Text = "Floor", AutoSize = true, Checked = true, ForeColor = ForeColor };
            KindWall = new RadioButton { Text = "Wall", AutoSize = true, ForeColor = ForeColor };
            KindPicture = new RadioButton { Text = "Picture / poster", AutoSize = true, ForeColor = ForeColor };
            foreach (var rb in new[] { KindFloor, KindWall, KindPicture })
            {
                rb.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) { ClearBuilt(); if (!Filling) SuggestPrice(); } };
                kinds.Controls.Add(rb);
            }
            right.Controls.Add(MakeLabel("Make into:"), 0, 3); right.Controls.Add(kinds, 1, 3);
            NameBox = MakeText(); right.Controls.Add(MakeLabel("Name:"), 0, 4); right.Controls.Add(NameBox, 1, 4);
            NameBox.TextChanged += (s, e) => { if (!Filling) NameBox.Tag = null; };
            PriceBox = new NumericUpDown { Minimum = 0, Maximum = 99999, Value = 10, Width = 100, BackColor = Color.FromArgb(8, 10, 26), ForeColor = ForeColor };
            var priceRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
            priceRow.Controls.Add(PriceBox);
            TintBtn = MakeButton("Colour...", false);
            TintBtn.Click += (s, e) => PickTint();
            var noTint = MakeButton("No colour", false);
            noTint.Click += (s, e) => SetTint(null);
            priceRow.Controls.Add(TintBtn); priceRow.Controls.Add(noTint);
            right.Controls.Add(MakeLabel("Price (§):"), 0, 5); right.Controls.Add(priceRow, 1, 5);
            DescBox = MakeText(); DescBox.Multiline = true; DescBox.Height = 54;
            right.Controls.Add(MakeLabel("Description:"), 0, 6); right.Controls.Add(DescBox, 1, 6);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            PreviewBtn = MakeButton("Preview in game look", false);
            PreviewBtn.Click += (s, e) => BuildPreview();
            BigBtn = MakeButton("Bigger preview...", false);
            BigBtn.Click += (s, e) => ShowBigPreview();
            SetBtn = MakeButton("Make a set...", false);
            SetBtn.Click += (s, e) => MakeSet();
            AddBtn = MakeButton("Add to Test", true);
            AddBtn.Click += (s, e) => AddToTest();
            buttons.Controls.Add(PreviewBtn); buttons.Controls.Add(BigBtn); buttons.Controls.Add(SetBtn); buttons.Controls.Add(AddBtn);
            right.Controls.Add(buttons, 0, 7); right.SetColumnSpan(buttons, 2);
            PreviewPic = new PixelBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 12, 30), Cursor = Cursors.Hand };
            PreviewPic.DoubleClick += (s, e) => ShowBigPreview();
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // editing banner
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 180)); // source
            for (int i = 0; i < 6; i++) right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // preview
            right.Controls.Add(PreviewPic, 0, 8); right.SetColumnSpan(PreviewPic, 2);
            Split.Panel2.Controls.Add(right);

            // the Test pack
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            PackList = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true,
                BackColor = Color.FromArgb(10, 12, 30), ForeColor = ForeColor, BorderStyle = BorderStyle.None
            };
            PackList.Columns.Add("On the Test server (double-click to change)", 300);
            PackList.Columns.Add("Kind", 80);
            PackList.Columns.Add("Price", 70);
            PackList.Columns.Add("Colour", 80);
            PackList.Columns.Add("File", 340);
            PackList.DoubleClick += (s, e) => { var sel = SelectedItems(); if (sel.Count > 0) BeginEdit(sel[0]); };
            bottom.Controls.Add(PackList, 0, 0);
            var packButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
            var editBtn = MakeButton("Change...", false);
            editBtn.Click += (s, e) => { var sel = SelectedItems(); if (sel.Count > 0) BeginEdit(sel[0]); else MessageBox.Show(this, "Select an item to change first.", Text); };
            var removeBtn = MakeButton("Remove", false);
            removeBtn.Click += (s, e) => RemoveSelected();
            var rebuildBtn = MakeButton("Rebuild all", false);
            rebuildBtn.Click += (s, e) => RebuildAll();
            var promoteBtn = MakeButton("Promote to Live...", true);
            promoteBtn.Click += (s, e) => PromoteSelected();
            var folderBtn = MakeButton("Open folder", false);
            folderBtn.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", Settings.CreatorFolder); } catch { } };
            packButtons.Controls.AddRange(new Control[] { editBtn, removeBtn, rebuildBtn, promoteBtn, folderBtn });
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

        private static void SetPicture(PictureBox box, Image img)
        {
            var old = box.Image;
            box.Image = img;
            if (old != null && !ReferenceEquals(old, img)) old.Dispose();
        }

        // ------------------------------------------------------------------
        // Remembering the window and the last item
        // ------------------------------------------------------------------

        private void RestoreWindow()
        {
            if (Settings.WindowWidth < 400 || Settings.WindowHeight < 300) return;
            var r = new Rectangle(Settings.WindowX, Settings.WindowY, Settings.WindowWidth, Settings.WindowHeight);
            // only if it's still on a screen (a second monitor may have gone)
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, 200, 40)))) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
            if (Settings.WindowMaximized) WindowState = FormWindowState.Maximized;
        }

        private void RememberWindow()
        {
            Settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Settings.WindowX = r.X; Settings.WindowY = r.Y; Settings.WindowWidth = r.Width; Settings.WindowHeight = r.Height;
            if (Split != null && Split.Width > 0) Settings.SplitFraction = Split.SplitterDistance / (double)Split.Width;
        }

        /// <summary>Stores the item being made, so it's back next time the Creator opens.</summary>
        private void RememberItem()
        {
            var last = Settings.LastItem;
            var src = CurrentSource();
            last.Kind = Kind;
            last.Name = NameBox.Text.Trim();
            last.Price = (int)PriceBox.Value;
            last.Description = DescBox.Text.Trim();
            last.Package = src?.Package ?? "";
            last.Instance = src?.Instance?.ToString("X16") ?? "";
            last.File = src?.File ?? "";
            last.Tint = src?.Tint ?? "";
            Settings.Save();
        }

        private void ReopenLastItem()
        {
            var last = Settings.LastItem;
            if (last == null || (last.Package.Length == 0 && last.File.Length == 0)) return;
            var src = new ItemSource
            {
                Package = last.Package.Length > 0 ? last.Package : null,
                Instance = ulong.TryParse(last.Instance, System.Globalization.NumberStyles.HexNumber, null, out var inst) ? inst : (ulong?)null,
                File = last.File.Length > 0 ? last.File : null,
                Tint = last.Tint.Length > 0 ? last.Tint : null,
            };
            try
            {
                Cursor = Cursors.WaitCursor;
                if (!TryLoadSource(src, out var entry, out var img, out _)) return;
                PickEntry(entry, img);
                FillFields(last.Kind, last.Name, last.Price, last.Description, src.Tint);
                SetStatus("Reopened the last item you were making (\"" + last.Name + "\").");
            }
            catch { }
            finally { Cursor = Cursors.Default; }
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
            lock (PackageLock)
            {
                foreach (var p in Packages)
                {
                    // (the texture being worked on stays open; it's closed with the window)
                    if (Current?.Package == p) OpenedForItems[p.Path] = p;
                    else p.Dispose();
                }
                Packages.Clear();
            }
            All.RemoveAll(e => e.Texture != null && e != Current);
            ClearThumbs();
            ApplyFilter();
            ScanBtn.Enabled = false;
            SetStatus("Looking for Sims 3 packages...");
            Task.Run(() =>
            {
                List<string> files;
                try { files = Sims3Library.FindPackages(folder).ToList(); }
                catch (Exception ex) { SetStatus("Couldn't search that folder: " + ex.Message); BeginInvoke(new Action(() => ScanBtn.Enabled = true)); return; }
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
                            lock (PackageLock) Packages.Add(pkg);
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
        // Finding an item's texture again (for editing, rebuilding and reopening)
        // ------------------------------------------------------------------

        /// <summary>Where the texture being worked on came from (with its colour), or null if nothing's picked.</summary>
        private ItemSource CurrentSource()
        {
            if (Current == null) return null;
            var src = Current.Texture != null
                ? new ItemSource { Package = Current.Texture.PackagePath, Instance = Current.Texture.Resource.Instance }
                : new ItemSource { File = Current.FilePath };
            if (Tint.HasValue) src.Tint = Colours.ToHex(Tint.Value.R, Tint.Value.G, Tint.Value.B);
            return src;
        }

        /// <summary>
        /// Finds an item's texture: the picture file, or the texture in its Sims 3 package (already scanned,
        /// or opened just for this - no scan needed). Older items only know the package's file name, so it is
        /// looked for in the Sims 3 folder. Safe to call from a background thread.
        /// </summary>
        private bool TryLoadSource(ItemSource src, out Entry entry, out Image32 img, out string why)
        {
            entry = null; img = null; why = null;
            if (src == null || (!src.IsFile && !src.IsPackage)) { why = "it doesn't say which texture it was made from"; return false; }
            if (src.IsFile)
            {
                if (!File.Exists(src.File)) { why = "the picture " + src.File + " isn't there any more"; return false; }
                img = Pictures.LoadFile(src.File);
                entry = new Entry { FilePath = src.File, Label = Path.GetFileNameWithoutExtension(src.File), Width = img.Width, Height = img.Height };
                return true;
            }
            var pkg = FindPackage(src.Package);
            if (pkg == null) { why = "the Sims 3 package " + Path.GetFileName(src.Package) + " wasn't found (check the Sims 3 folder)"; return false; }
            Sims3Texture tex;
            lock (pkg) tex = pkg.ListTextures().FirstOrDefault(t => t.Resource.Instance == src.Instance.Value);
            if (tex == null) { why = "the texture isn't in " + Path.GetFileName(pkg.Path) + " any more"; return false; }
            // (on the window's own thread, reuse the browser's entry so its sorting is kept)
            entry = (!InvokeRequired ? All.FirstOrDefault(e => e.Texture != null && e.Package == pkg && e.Texture.Resource.Instance == tex.Resource.Instance) : null)
                    ?? new Entry { Texture = tex, Package = pkg, Label = tex.DisplayName, Width = tex.Width, Height = tex.Height };
            lock (pkg) img = pkg.LoadTexture(tex);
            return true;
        }

        private Sims3Package FindPackage(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName)) return null;
            var name = Path.GetFileName(pathOrName);
            lock (PackageLock)
            {
                // already open (from a scan, or for another item)?
                var open = Packages.FirstOrDefault(p => p.Path.Equals(pathOrName, StringComparison.OrdinalIgnoreCase));
                if (open != null) return open;
                if (OpenedForItems.TryGetValue(pathOrName, out var mine)) return mine;
                string full = null;
                if (Path.IsPathRooted(pathOrName) && File.Exists(pathOrName)) full = pathOrName;
                else
                {
                    // just a file name (older items): already scanned, or somewhere in the Sims 3 folder
                    open = Packages.FirstOrDefault(p => Path.GetFileName(p.Path).Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (open != null) return open;
                    var folder = Settings.Sims3Folder;
                    if (Directory.Exists(folder))
                        try { full = Sims3Library.FindPackages(folder).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)); } catch { }
                }
                if (full == null) return null;
                if (OpenedForItems.TryGetValue(full, out mine)) return mine;
                try
                {
                    var pkg = Sims3Package.Open(full);
                    OpenedForItems[full] = pkg;
                    if (!full.Equals(pathOrName, StringComparison.OrdinalIgnoreCase)) OpenedForItems[pathOrName] = pkg;
                    return pkg;
                }
                catch { return null; }
            }
        }

        // ------------------------------------------------------------------
        // Making an item
        // ------------------------------------------------------------------

        private bool Filling; // (true while the form is being filled in by code)

        private void ClearBuilt()
        {
            Built = null;
            if (PreviewPic != null) PreviewPic.Image = null;
        }

        private void PickEntry(Entry e, Image32 img = null)
        {
            try
            {
                Current = e;
                CurrentImage = img ?? LoadImage(e);
                ClearBuilt();
                Tint = null;
                TintBtn.BackColor = Color.FromArgb(32, 38, 70);
                SetPicture(SourcePic, Pictures.ToBitmap(CurrentImage));
                if (e.Kind == TextureKind.Greyscale) SetStatus("This one is grey - Sims 3 colours it in while playing. Press Colour... to pick its colour.");
                SourceInfo.Text = (e.Label ?? "") + "   " + CurrentImage.Width + "×" + CurrentImage.Height
                    + (e.Texture != null ? "   from " + Path.GetFileName(e.Texture.PackagePath) : "");
                if (e.Texture != null && Editing == null)
                {
                    // a guess from the shape: square = floor, tall = wall
                    if (CurrentImage.Height >= CurrentImage.Width * 1.5) KindWall.Checked = true;
                    else if (CurrentImage.Width == CurrentImage.Height) KindFloor.Checked = true;
                }
                if (Editing == null && (string.IsNullOrWhiteSpace(NameBox.Text) || NameBox.Tag as string == "auto"))
                {
                    Filling = true;
                    NameBox.Text = Tidy(e.Label);
                    NameBox.Tag = "auto";
                    Filling = false;
                }
                if (Editing == null) SuggestPrice();
                RememberItem();
            }
            catch (Exception ex) { SetStatus("Couldn't read that texture: " + ex.Message); }
        }

        /// <summary>Fills in kind, name, price, description and colour (without the usual guesses kicking in).</summary>
        private void FillFields(string kind, string name, int price, string desc, string tintHex)
        {
            Filling = true;
            try
            {
                (kind == "wall" ? KindWall : kind == "picture" ? KindPicture : KindFloor).Checked = true;
                NameBox.Text = name ?? "";
                NameBox.Tag = null;
                if (price >= 0) PriceBox.Value = Math.Max(PriceBox.Minimum, Math.Min(PriceBox.Maximum, price));
                DescBox.Text = desc ?? "";
            }
            finally { Filling = false; }
            if (Colours.TryParseHex(tintHex, out var r, out var g, out var b)) SetTint(Color.FromArgb(r, g, b));
            else SetTint(null);
        }

        private static string Tidy(string label)
        {
            if (string.IsNullOrEmpty(label) || label.StartsWith("0x")) return "";
            var s = label.Replace('_', ' ').Trim();
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        private string Kind => KindWall.Checked ? "wall" : KindPicture.Checked ? "picture" : "floor";

        private static int DefaultPrice(string kind) => kind == "floor" ? 10 : kind == "wall" ? 14 : 120;

        private void SuggestPrice()
        {
            if (PriceBox == null) return;
            PriceBox.Value = DefaultPrice(Kind);
        }

        private byte[] PosterTemplate()
        {
            var path = Path.Combine(Settings.ModsFolder, "Overrides", "PaintingPosterSuperstar.iff");
            if (!File.Exists(path)) throw new FileNotFoundException("The poster template wasn't found at " + path + ". Check the Game content folder.");
            return File.ReadAllBytes(path);
        }

        /// <summary>Makes the game file for one item (safe on a background thread).</summary>
        private byte[] BuildItem(string kind, Image32 source, string name, int price, string desc, byte[] posterTemplate)
        {
            if (kind == "floor") return FloorBuilder.Build(source, name, price, desc);
            if (kind == "wall") return WallBuilder.Build(source, name, price, desc);
            return PictureBuilder.Build(source, name, price, desc, posterTemplate);
        }

        private bool BuildCurrent()
        {
            if (CurrentImage == null) { MessageBox.Show(this, "Pick a texture or open a picture first.", Text); return false; }
            var name = NameBox.Text.Trim();
            if (name.Length == 0) { MessageBox.Show(this, "Give it a name first.", Text); NameBox.Focus(); return false; }
            try
            {
                Cursor = Cursors.WaitCursor;
                var kind = Kind;
                Built = BuildItem(kind, Working, name, (int)PriceBox.Value, DescBox.Text.Trim(), kind == "picture" ? PosterTemplate() : null);
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
            if (CurrentImage != null) SetPicture(SourcePic, Pictures.ToBitmap(Working));
            ClearBuilt();
            if (Current != null && !Filling) RememberItem();
        }

        private void BuildPreview()
        {
            if (BuildCurrent()) { SetStatus("This is how it will look in the game (closest zoom). Double-click it, or press Bigger preview..., to see every zoom."); RememberItem(); }
        }

        private void ShowBigPreview()
        {
            if ((Built == null || BuiltKind != Kind) && !BuildCurrent()) return;
            using (var f = new BigPreviewForm(BuiltKind, Built, NameBox.Text.Trim())) f.ShowDialog(this);
        }

        private void AddToTest()
        {
            if (Pack == null) { MessageBox.Show(this, "The creator folder couldn't be opened - check the Game content folder.", Text); return; }
            var name = NameBox.Text.Trim();
            var kind = Kind;
            if (Editing == null)
            {
                var same = Pack.Items.FirstOrDefault(i => i.Kind == kind && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (same != null && MessageBox.Show(this, "There's already a " + kind + " called \"" + same.Name + "\" on Test.\n\nReplace it with this one?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            }
            if (!BuildCurrent()) return;
            try
            {
                CreatorItem item;
                if (Editing != null && Pack.Items.Contains(Editing))
                {
                    item = Pack.Update(Editing, BuiltKind, name, (int)PriceBox.Value, DescBox.Text.Trim(), Built, CurrentSource());
                    SetStatus("Saved the changes to \"" + item.Name + "\" (" + item.File + "). Rebuild the game to try it on the Test server.");
                }
                else
                {
                    item = Pack.Add(BuiltKind, name, (int)PriceBox.Value, DescBox.Text.Trim(), Built, CurrentSource());
                    SetStatus("Added \"" + item.Name + "\" to the Test content (" + item.File + "). Rebuild the game to try it on the Test server.");
                }
                Pack.Save();
                FillPackList();
                RememberItem();
                if (Editing != null) ShowEditing();
            }
            catch (InvalidOperationException ex) { MessageBox.Show(this, ex.Message + "\n\nPick a different name.", Text); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't save it: " + ex.Message, Text); }
        }

        // ------------------------------------------------------------------
        // Changing an item that's on Test
        // ------------------------------------------------------------------

        private void BeginEdit(CreatorItem item)
        {
            if (item == null) return;
            ItemSource src = item.ReadSource();
            Entry entry = null; Image32 img = null; string why = null;
            Cursor = Cursors.WaitCursor;
            try { TryLoadSource(src, out entry, out img, out why); }
            catch (Exception ex) { why = ex.Message; }
            finally { Cursor = Cursors.Default; }

            Editing = item;
            if (entry != null) PickEntry(entry, img);
            else
            {
                Current = null; CurrentImage = null;
                SetPicture(SourcePic, null);
                ClearBuilt();
                SourceInfo.Text = "Its texture couldn't be found - pick another texture or picture for it.";
            }
            FillFields(item.Kind, item.Name, item.Price, item.Description, src.Tint);
            ShowEditing();
            if (entry != null) { BuildCurrent(); SetStatus("Changing \"" + item.Name + "\". Change anything, then press Save changes."); }
            else MessageBox.Show(this, "\"" + item.Name + "\" was opened, but its texture couldn't be found: " + why + ".\n\nPick another texture (or Open picture...) for it, then press Save changes.", Text);
        }

        private void ShowEditing()
        {
            var row = (Control)EditInfo.Tag;
            if (Editing == null) { row.Visible = false; AddBtn.Text = "Add to Test"; return; }
            EditInfo.Text = "Changing \"" + Editing.Name + "\" (" + Editing.Kind + ") on Test";
            row.Visible = true;
            AddBtn.Text = "Save changes";
        }

        private void StopEditing()
        {
            Editing = null;
            ShowEditing();
            SetStatus("Making a new item. (The one you were changing is still on Test.)");
        }

        /// <summary>
        /// Re-makes every Test item from its texture with this version of the Creator (handy after the
        /// builders improve, e.g. once walls are tuned). Items whose texture can't be found are left alone.
        /// </summary>
        private async void RebuildAll()
        {
            if (Pack == null || Pack.Items.Count == 0 || Busy) return;
            if (MessageBox.Show(this, "Re-make all " + Pack.Items.Count + " Test items from their textures with this version of the Creator?\n\nNames, prices, descriptions and colours stay the same.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Busy = true; UseWaitCursor = true;
            int ok = 0;
            var failed = new List<string>();
            byte[] template = null;
            try { template = PosterTemplate(); } catch { }
            var items = Pack.Items.ToList();
            for (int n = 0; n < items.Count; n++)
            {
                var item = items[n];
                SetStatus("Rebuilding " + (n + 1) + " of " + items.Count + ": " + item.Name + "...");
                var src = item.ReadSource();
                try
                {
                    var data = await Task.Run(() =>
                    {
                        if (!TryLoadSource(src, out _, out var img, out var why)) throw new Exception(why);
                        if (src.Tint != null && Colours.TryParseHex(src.Tint, out var r, out var g, out var b)) img = Colours.Colourise(img, r, g, b);
                        if (item.Kind == "picture" && template == null) throw new Exception("the poster template is missing");
                        return BuildItem(item.Kind, img, item.Name, item.Price, item.Description, template);
                    });
                    // (older items get their full source info written while we're here)
                    var full = src;
                    if (src.IsPackage && !Path.IsPathRooted(src.Package))
                    {
                        var pkg = FindPackage(src.Package);
                        if (pkg != null) full = new ItemSource { Package = pkg.Path, Instance = src.Instance, Tint = src.Tint };
                    }
                    Pack.Update(item, item.Kind, item.Name, item.Price, item.Description, data, full);
                    ok++;
                }
                catch (Exception ex) { failed.Add(item.Name + " - " + ex.Message); }
            }
            try { Pack.Save(); } catch (Exception ex) { failed.Add("saving the list - " + ex.Message); }
            FillPackList();
            Busy = false; UseWaitCursor = false;
            SetStatus("Rebuilt " + ok + " of " + items.Count + " Test items. Rebuild the game to see them.");
            if (failed.Count > 0)
                MessageBox.Show(this, "These couldn't be rebuilt (they were left as they were):\n\n" + string.Join("\n", failed.Take(20)) + (failed.Count > 20 ? "\n..." : ""), Text);
            if (Editing != null && Pack.Items.Contains(Editing)) { ClearBuilt(); }
        }

        // ------------------------------------------------------------------
        // Make a set: one texture in several colours
        // ------------------------------------------------------------------

        private async void MakeSet()
        {
            if (CurrentImage == null) { MessageBox.Show(this, "Pick a texture or open a picture first.", Text); return; }
            if (Pack == null || Busy) return;
            var kind = Kind;
            var baseName = NameBox.Text.Trim();
            if (Editing != null) baseName = baseName.Split(new[] { " - " }, StringSplitOptions.None)[0];
            List<Colours.Swatch> chosen;
            string setName;
            using (var dlg = new MakeSetForm(baseName, kind))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                chosen = dlg.Chosen; setName = dlg.SetName;
            }
            var names = chosen.Select(c => setName + " - " + c.Name).ToList();
            var clashes = names.Where(nm => Pack.Items.Any(i => i.Kind == kind && i.Name.Equals(nm, StringComparison.OrdinalIgnoreCase))).ToList();
            if (clashes.Count > 0 && MessageBox.Show(this, clashes.Count + " of these are already on Test and will be replaced:\n\n" + string.Join("\n", clashes.Take(12)) + "\n\nGo ahead?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            byte[] template = null;
            if (kind == "picture") { try { template = PosterTemplate(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); return; } }
            var image = CurrentImage;
            var baseSrc = CurrentSource();
            int price = (int)PriceBox.Value;
            var desc = DescBox.Text.Trim();
            Busy = true; UseWaitCursor = true;
            int made = 0;
            try
            {
                for (int n = 0; n < chosen.Count; n++)
                {
                    var c = chosen[n];
                    var nm = names[n];
                    SetStatus("Making " + (n + 1) + " of " + chosen.Count + ": " + nm + "...");
                    var data = await Task.Run(() => BuildItem(kind, Colours.Colourise(image, c.R, c.G, c.B), nm, price, desc, template));
                    var src = new ItemSource { Package = baseSrc.Package, Instance = baseSrc.Instance, File = baseSrc.File, Tint = c.Hex };
                    Pack.Add(kind, nm, price, desc, data, src);
                    made++;
                }
                Pack.Save();
                SetStatus("Made " + made + " " + kind + "s in the \"" + setName + "\" set and added them to Test.");
            }
            catch (Exception ex) { MessageBox.Show(this, "Stopped after " + made + ": " + ex.Message, Text); try { Pack.Save(); } catch { } }
            finally { Busy = false; UseWaitCursor = false; FillPackList(); }
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
            Editing = null;
            if (EditInfo != null) ShowEditing();
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
                    var tint = item.ReadSource().Tint ?? "";
                    var row = new ListViewItem(new[] { item.Name, item.Kind, "§" + item.Price, tint, item.File }) { Tag = item };
                    if (item == Editing) row.Font = new Font(PackList.Font, FontStyle.Bold);
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
            if (Editing != null && items.Contains(Editing)) { Editing = null; ShowEditing(); }
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
                if (Editing != null && items.Contains(Editing)) { Editing = null; ShowEditing(); }
                File.WriteAllText(manifest, string.Join("\n", lines) + "\n");
                Pack.Save();
                FillPackList();
                SetStatus("Promoted " + items.Count + " item(s) to Live. Rebuild the game and send out the update.");
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't promote: " + ex.Message, Text); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { RememberWindow(); if (Current != null) RememberItem(); else Settings.Save(); } catch { }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ScanCancel?.Cancel();
            ThumbTimer?.Stop();
            lock (PackageLock)
            {
                foreach (var p in Packages) p.Dispose();
                foreach (var p in OpenedForItems.Values.Distinct()) p.Dispose();
            }
            base.OnFormClosed(e);
        }
    }
}
