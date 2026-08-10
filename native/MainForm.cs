using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace RelationshipGraphNative
{
    public sealed class MainForm : Form
    {
        private readonly GraphCanvas _canvas = new GraphCanvas();
        private readonly Panel _inspector = new Panel();
        private readonly ToolStripStatusLabel _statusText = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _countText = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _zoomText = new ToolStripStatusLabel();
        private readonly ToolStripButton _undoButton = new ToolStripButton("撤销");
        private readonly ToolStripButton _redoButton = new ToolStripButton("重做");
        private readonly ToolStripComboBox _lineTypeBox = new ToolStripComboBox();
        private readonly ToolStripComboBox _directionBox = new ToolStripComboBox();
        private readonly ToolStripComboBox _depthBox = new ToolStripComboBox();
        private readonly ToolStripComboBox _themeBox = new ToolStripComboBox();
        private readonly ToolStripTextBox _searchBox = new ToolStripTextBox();
        private readonly Stack<GraphDocument> _undo = new Stack<GraphDocument>();
        private readonly Stack<GraphDocument> _redo = new Stack<GraphDocument>();
        private readonly Timer _inspectorSaveTimer = new Timer();
        private GraphDocument _pendingInspectorBefore;
        private Control _pendingInspectorSource;
        private string _pendingInspectorMessage = "";
        private string _selectionClipboardJson = "";
        private string _currentFile = "";
        private string _autosavePath;
        private string _themePreferencePath;
        private string _themeMode = "system";
        private bool _darkTheme;
        private bool _settingUi;
        private readonly bool _autosaveEnabled;
        private MenuStrip _menu;
        private ToolStrip _tools;
        private StatusStrip _status;
        private SplitContainer _split;
        private ToolStripMenuItem _systemThemeItem;
        private ToolStripMenuItem _lightThemeItem;
        private ToolStripMenuItem _darkThemeItem;

        public MainForm() : this(null, true) { }

        internal MainForm(GraphDocument initialGraph, bool autosaveEnabled)
        {
            _autosaveEnabled = autosaveEnabled;
            Text = "关系图编辑器";
            Icon = NativeAppIcon.Create();
            ShowIcon = true;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1050, 680);
            Size = new Size(1440, 900);
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(244, 247, 250);

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _autosavePath = Path.Combine(local, "Relationship Studio", "autosave-native.json");
            _themePreferencePath = Path.Combine(local, "Relationship Studio", "theme.txt");
            _themeMode = LoadThemePreference();

            MenuStrip menu = BuildMenu(); _menu = menu;
            ToolStrip tools = BuildToolbar(); _tools = tools;
            StatusStrip status = BuildStatus(); _status = status;
            SplitContainer split = new SplitContainer(); _split = split;
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel2;
            split.SplitterWidth = 5;
            split.Panel1.Controls.Add(_canvas);
            split.Panel2.Controls.Add(_inspector);
            _canvas.Dock = DockStyle.Fill;
            _inspector.Dock = DockStyle.Fill;
            _inspector.AutoScroll = true;
            _inspector.BackColor = Color.White;
            _inspector.Padding = new Padding(18);

            Controls.Add(split);
            Controls.Add(status);
            Controls.Add(tools);
            Controls.Add(menu);
            MainMenuStrip = menu;

            bool splitterInitialized = false;
            Action initializeSplitter = delegate
            {
                if (splitterInitialized) return;
                const int canvasMinimum = 600, inspectorMinimum = 270;
                int maximum = split.ClientSize.Width - inspectorMinimum - split.SplitterWidth;
                if (maximum < canvasMinimum) return;
                split.SplitterDistance = canvasMinimum;
                split.Panel1MinSize = canvasMinimum;
                split.Panel2MinSize = inspectorMinimum;
                split.SplitterDistance = Math.Max(canvasMinimum, Math.Min(maximum, split.ClientSize.Width - 325));
                splitterInitialized = true;
            };
            split.SizeChanged += delegate { initializeSplitter(); };
            Shown += delegate { initializeSplitter(); };

            _canvas.NewLineType = "curve";
            _canvas.FocusDirection = "all";
            _canvas.FocusDepth = 1;
            _inspectorSaveTimer.Interval = 450;
            _inspectorSaveTimer.Tick += delegate { FlushPendingInspectorChange(); };
            _canvas.SelectionChanged += delegate { FlushPendingInspectorChange(); RebuildInspector(); UpdateStatus(); };
            _canvas.GraphCommitted += CanvasGraphCommitted;
            _canvas.ViewChanged += delegate { UpdateStatus(); };
            _canvas.BlankDoubleClicked += delegate(object sender, CanvasPointEventArgs e) { AddNodeAt(e.WorldPoint); };
            FormClosing += delegate { FlushPendingInspectorChange(); SaveAutosave(); };
            FormClosed += delegate { _inspectorSaveTimer.Dispose(); };
            KeyDown += MainFormKeyDown;

            GraphDocument first = initialGraph ?? TryLoadAutosave() ?? GraphSerialization.LoadDefault();
            LoadDocument(first, "原生关系图已打开", true);
            ApplyTheme();
        }

        private MenuStrip BuildMenu()
        {
            MenuStrip menu = new MenuStrip();
            ToolStripMenuItem file = new ToolStripMenuItem("文件(&F)");
            file.DropDownItems.Add(MenuItem("新建空白图", Keys.Control | Keys.N, delegate { NewBlank(); }));
            file.DropDownItems.Add(MenuItem("恢复默认测试用图", Keys.None, delegate { LoadDocument(GraphSerialization.LoadDefault(), "已恢复《测试用图》", true); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("导入 JSON / 只读可视图…", Keys.Control | Keys.O, OpenGraph));
            file.DropDownItems.Add(MenuItem("保存 JSON…", Keys.Control | Keys.S, SaveJson));
            file.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem export = new ToolStripMenuItem("导出");
            export.DropDownItems.Add(MenuItem("飞书画板（draw.io，可编辑）…", Keys.None, ExportFeishuBoard));
            export.DropDownItems.Add(new ToolStripSeparator());
            export.DropDownItems.Add(MenuItem("只读可视图（HTML）…", Keys.None, ExportReadonly));
            export.DropDownItems.Add(MenuItem("SVG 矢量图…", Keys.None, ExportSvg));
            export.DropDownItems.Add(MenuItem("PNG 图片…", Keys.None, ExportPng));
            export.DropDownItems.Add(MenuItem("PDF 文档…", Keys.None, ExportPdf));
            file.DropDownItems.Add(export);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("退出", Keys.Alt | Keys.F4, Close));

            ToolStripMenuItem edit = new ToolStripMenuItem("编辑(&E)");
            edit.DropDownItems.Add(MenuItem("撤销", Keys.Control | Keys.Z, Undo));
            edit.DropDownItems.Add(MenuItem("重做", Keys.Control | Keys.Y, Redo));
            edit.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem copy = MenuItem("复制选中内容", Keys.None, delegate { CopySelected(true); }); copy.ShortcutKeyDisplayString = "Ctrl+C";
            ToolStripMenuItem paste = MenuItem("粘贴", Keys.None, delegate { PasteSelected(true); }); paste.ShortcutKeyDisplayString = "Ctrl+V";
            edit.DropDownItems.Add(copy); edit.DropDownItems.Add(paste);
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(MenuItem("新增分组", Keys.Control | Keys.G, AddGroup));
            edit.DropDownItems.Add(MenuItem("新增节点", Keys.Control | Keys.Shift | Keys.N, AddNode));
            edit.DropDownItems.Add(MenuItem("新增关系…", Keys.Control | Keys.L, AddRelation));
            edit.DropDownItems.Add(MenuItem("删除选中项", Keys.Delete, DeleteSelected));

            ToolStripMenuItem view = new ToolStripMenuItem("视图(&V)");
            view.DropDownItems.Add(MenuItem("适合窗口", Keys.Control | Keys.D0, delegate { _canvas.FitToView(); }));
            view.DropDownItems.Add(MenuItem("放大", Keys.Control | Keys.Oemplus, delegate { _canvas.ZoomBy(1.18f); }));
            view.DropDownItems.Add(MenuItem("缩小", Keys.Control | Keys.OemMinus, delegate { _canvas.ZoomBy(.85f); }));
            view.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem theme = new ToolStripMenuItem("界面主题");
            _systemThemeItem = MenuItem("跟随系统", Keys.None, delegate { ChangeTheme("system"); });
            _lightThemeItem = MenuItem("浅色", Keys.None, delegate { ChangeTheme("light"); });
            _darkThemeItem = MenuItem("深色", Keys.None, delegate { ChangeTheme("dark"); });
            theme.DropDownItems.Add(_systemThemeItem); theme.DropDownItems.Add(_lightThemeItem); theme.DropDownItems.Add(_darkThemeItem);
            view.DropDownItems.Add(theme);

            ToolStripMenuItem help = new ToolStripMenuItem("帮助(&H)");
            help.DropDownItems.Add(MenuItem("操作说明", Keys.F1, ShowHelp));
            help.DropDownItems.Add(MenuItem("关于", Keys.None, ShowAbout));
            menu.Items.Add(file); menu.Items.Add(edit); menu.Items.Add(view); menu.Items.Add(help);
            return menu;
        }

        private ToolStrip BuildToolbar()
        {
            ToolStrip tools = new ToolStrip();
            tools.GripStyle = ToolStripGripStyle.Hidden;
            tools.Padding = new Padding(7, 4, 7, 4);
            tools.AutoSize = true;
            _undoButton.Click += delegate { Undo(); }; _redoButton.Click += delegate { Redo(); };

            ToolStripButton addGroup = new ToolStripButton("＋分组"); addGroup.Click += delegate { AddGroup(); };
            ToolStripButton addNode = new ToolStripButton("＋节点"); addNode.Click += delegate { AddNode(); };
            ToolStripButton fit = new ToolStripButton("适合窗口"); fit.Click += delegate { _canvas.FitToView(); };
            ToolStripButton zoomOut = new ToolStripButton("－"); zoomOut.Click += delegate { _canvas.ZoomBy(.85f); };
            ToolStripButton zoomIn = new ToolStripButton("＋"); zoomIn.Click += delegate { _canvas.ZoomBy(1.18f); };

            _lineTypeBox.DropDownStyle = ComboBoxStyle.DropDownList; _lineTypeBox.Width = 72;
            _lineTypeBox.Items.AddRange(new object[] { "曲线", "直线", "折线" }); _lineTypeBox.SelectedIndex = 0;
            _lineTypeBox.SelectedIndexChanged += delegate { _canvas.NewLineType = LineTypeAt(_lineTypeBox.SelectedIndex); };
            _directionBox.DropDownStyle = ComboBoxStyle.DropDownList; _directionBox.Width = 82;
            _directionBox.Items.AddRange(new object[] { "上下游", "仅上游", "仅下游" }); _directionBox.SelectedIndex = 0;
            _directionBox.SelectedIndexChanged += delegate { _canvas.FocusDirection = _directionBox.SelectedIndex == 1 ? "upstream" : _directionBox.SelectedIndex == 2 ? "downstream" : "all"; _canvas.Invalidate(); };
            _depthBox.DropDownStyle = ComboBoxStyle.DropDownList; _depthBox.Width = 48;
            _depthBox.Items.AddRange(new object[] { "1层", "2层", "3层" }); _depthBox.SelectedIndex = 0;
            _depthBox.SelectedIndexChanged += delegate { _canvas.FocusDepth = _depthBox.SelectedIndex + 1; _canvas.Invalidate(); };
            _themeBox.DropDownStyle = ComboBoxStyle.DropDownList; _themeBox.Width = 86;
            _themeBox.Items.AddRange(new object[] { "跟随系统", "浅色", "深色" }); _themeBox.SelectedIndex = ThemeIndex(_themeMode);
            _themeBox.SelectedIndexChanged += delegate { if (!_settingUi) ChangeTheme(ThemeAt(_themeBox.SelectedIndex)); };
            _searchBox.Width = 145; _searchBox.ToolTipText = "输入节点或分组名称，按回车定位";
            _searchBox.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { FindEntity(); e.SuppressKeyPress = true; } };

            tools.Items.Add(_undoButton); tools.Items.Add(_redoButton); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(addGroup); tools.Items.Add(addNode); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel("新关系线型")); tools.Items.Add(_lineTypeBox); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel("高亮")); tools.Items.Add(_directionBox); tools.Items.Add(_depthBox); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(fit); tools.Items.Add(zoomOut); tools.Items.Add(zoomIn); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel("主题")); tools.Items.Add(_themeBox); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel("查找")); tools.Items.Add(_searchBox);
            return tools;
        }

        private StatusStrip BuildStatus()
        {
            StatusStrip status = new StatusStrip();
            _statusText.Spring = true; _statusText.TextAlign = ContentAlignment.MiddleLeft;
            status.Items.Add(_statusText); status.Items.Add(_countText); status.Items.Add(new ToolStripStatusLabel("  ")); status.Items.Add(_zoomText);
            return status;
        }

        private static ToolStripMenuItem MenuItem(string text, Keys keys, Action action)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text); item.ShortcutKeys = keys;
            item.Click += delegate { action(); }; return item;
        }

        private string LoadThemePreference()
        {
            if (!_autosaveEnabled) return "system";
            try
            {
                if (!File.Exists(_themePreferencePath)) return "system";
                return NormalizeThemeMode(File.ReadAllText(_themePreferencePath, Encoding.UTF8).Trim());
            }
            catch { return "system"; }
        }

        private void SaveThemePreference()
        {
            if (!_autosaveEnabled) return;
            try
            {
                string folder = Path.GetDirectoryName(_themePreferencePath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(_themePreferencePath, _themeMode, new UTF8Encoding(false));
            }
            catch { }
        }

        private void ChangeTheme(string mode)
        {
            FlushPendingInspectorChange();
            _themeMode = NormalizeThemeMode(mode);
            SaveThemePreference(); ApplyTheme();
            _statusText.Text = _themeMode == "system" ? "界面主题已设为跟随系统" : _themeMode == "dark" ? "界面已切换为深色主题" : "界面已切换为浅色主题";
        }

        private void ApplyTheme()
        {
            bool dark = _themeMode == "dark" || (_themeMode == "system" && NativeTheme.SystemUsesDarkTheme());
            _darkTheme = dark;
            BackColor = dark ? NativeTheme.DarkBackground : NativeTheme.LightBackground;
            NativeTheme.ApplyControlTree(this, dark);
            NativeTheme.ApplyToolStrip(_menu, dark); NativeTheme.ApplyToolStrip(_tools, dark); NativeTheme.ApplyToolStrip(_status, dark);
            if (_split != null)
            {
                _split.BackColor = dark ? NativeTheme.DarkBorder : Color.FromArgb(215, 222, 230);
                _split.Panel1.BackColor = dark ? NativeTheme.DarkBackground : NativeTheme.LightBackground;
                _split.Panel2.BackColor = dark ? NativeTheme.DarkSurface : Color.White;
            }
            _canvas.DarkTheme = dark;
            bool previous = _settingUi; _settingUi = true;
            _themeBox.SelectedIndex = ThemeIndex(_themeMode);
            _systemThemeItem.Checked = _themeMode == "system";
            _lightThemeItem.Checked = _themeMode == "light";
            _darkThemeItem.Checked = _themeMode == "dark";
            _settingUi = previous;
            NativeTheme.ApplyWindowDarkMode(this, dark);
            Invalidate(true);
        }

        internal void SetThemeForTesting(string mode) { _themeMode = NormalizeThemeMode(mode); ApplyTheme(); }
        internal bool DarkThemeForTesting { get { return _darkTheme && _canvas.DarkTheme; } }
        private static string NormalizeThemeMode(string mode) { return mode == "light" || mode == "dark" ? mode : "system"; }
        private static string ThemeAt(int index) { return index == 1 ? "light" : index == 2 ? "dark" : "system"; }
        private static int ThemeIndex(string mode) { return mode == "light" ? 1 : mode == "dark" ? 2 : 0; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); NativeTheme.ApplyWindowDarkMode(this, _darkTheme);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if ((message.Msg == 0x001A || message.Msg == 0x031A) && _themeMode == "system" && IsHandleCreated)
            {
                try { BeginInvoke((Action)delegate { ApplyTheme(); }); }
                catch { }
            }
        }

        private void CanvasGraphCommitted(object sender, GraphCommitEventArgs e)
        {
            if (e.Before != null) PushHistory(_undo, e.Before);
            _redo.Clear(); SaveAutosave(); RebuildInspector(); UpdateStatus();
            _statusText.Text = e.Message;
        }

        private void CommitChange(GraphDocument before, string message)
        {
            _canvas.Document.meta.updatedAt = DateTime.UtcNow.ToString("o");
            _canvas.RefreshDocument(); PushHistory(_undo, before); _redo.Clear(); SaveAutosave();
            RebuildInspector(); UpdateStatus(); _statusText.Text = message;
        }

        private void ApplyInspectorChange(Control source, Action apply, string message)
        {
            if (!_canvas.EditMode || _settingUi || source == null || apply == null) return;
            if (_pendingInspectorBefore != null && _pendingInspectorSource != source) FlushPendingInspectorChange();
            if (_pendingInspectorBefore == null)
            {
                _pendingInspectorBefore = GraphSerialization.Clone(_canvas.Document);
                _pendingInspectorSource = source;
            }
            apply();
            _canvas.Document.meta.updatedAt = DateTime.UtcNow.ToString("o");
            _pendingInspectorMessage = message;
            _canvas.Invalidate(); UpdateStatus();
            _statusText.Text = "已自动应用，正在保存…";
            _inspectorSaveTimer.Stop(); _inspectorSaveTimer.Start();
        }

        private void FlushPendingInspectorChange()
        {
            _inspectorSaveTimer.Stop();
            if (_pendingInspectorBefore == null) return;
            GraphDocument before = _pendingInspectorBefore; string message = _pendingInspectorMessage;
            _pendingInspectorBefore = null; _pendingInspectorSource = null; _pendingInspectorMessage = "";
            PushHistory(_undo, before); _redo.Clear(); SaveAutosave(); UpdateStatus();
            _statusText.Text = String.IsNullOrEmpty(message) ? "属性已自动保存" : message;
        }

        private void BindAutoText(TextBox box, bool allowEmpty, Action<string> apply, string message)
        {
            box.TextChanged += delegate
            {
                if (_settingUi || !_canvas.EditMode) return;
                string value = box.Text;
                if (!allowEmpty && String.IsNullOrWhiteSpace(value)) { _statusText.Text = "内容为空，未自动应用"; return; }
                ApplyInspectorChange(box, delegate { apply(allowEmpty ? value : value.Trim()); }, message);
            };
            box.LostFocus += delegate
            {
                FlushPendingInspectorChange();
                if (!allowEmpty && String.IsNullOrWhiteSpace(box.Text) && !IsDisposed)
                    try { BeginInvoke((Action)delegate { RebuildInspector(); }); } catch { }
            };
            if (!box.Multiline) box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FlushPendingInspectorChange(); _canvas.Focus(); }
            };
        }

        private void BindAutoCombo(ComboBox box, Action apply, string message)
        {
            box.SelectedIndexChanged += delegate { if (!_settingUi && _canvas.EditMode) ApplyInspectorChange(box, apply, message); };
            box.LostFocus += delegate { FlushPendingInspectorChange(); };
        }

        private static void PushHistory(Stack<GraphDocument> stack, GraphDocument graph)
        {
            stack.Push(GraphSerialization.Clone(graph));
            if (stack.Count <= 60) return;
            GraphDocument[] items = stack.Take(60).Reverse().ToArray(); stack.Clear();
            foreach (GraphDocument item in items) stack.Push(item);
        }

        private void LoadDocument(GraphDocument graph, string message, bool clearHistory)
        {
            FlushPendingInspectorChange();
            if (clearHistory) { _undo.Clear(); _redo.Clear(); }
            _canvas.Document = graph; _canvas.EditMode = true; _currentFile = ""; SaveAutosave(); ApplyTheme(); RebuildInspector(); UpdateStatus();
            _statusText.Text = message; UpdateTitle();
        }

        private GraphDocument TryLoadAutosave()
        {
            if (!_autosaveEnabled) return null;
            try { return File.Exists(_autosavePath) ? GraphSerialization.LoadFile(_autosavePath) : null; }
            catch { return null; }
        }

        private void SaveAutosave()
        {
            if (!_autosaveEnabled) return;
            try
            {
                if (_canvas.Document == null) return;
                string folder = Path.GetDirectoryName(_autosavePath); if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(_autosavePath, GraphSerialization.Serialize(_canvas.Document, false), new UTF8Encoding(false));
            }
            catch { }
        }

        private void NewBlank() { LoadDocument(GraphSerialization.CreateBlank("未命名关系图"), "已新建空白关系图", true); }

        private void OpenGraph()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入关系图"; dialog.Filter = "关系图文件 (*.json;*.html;*.htm)|*.json;*.html;*.htm|JSON (*.json)|*.json|只读可视图 (*.html;*.htm)|*.html;*.htm|所有文件 (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { LoadDocument(GraphSerialization.LoadFile(dialog.FileName), "已导入 " + Path.GetFileName(dialog.FileName), true); _currentFile = Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase) ? dialog.FileName : ""; UpdateTitle(); }
                catch (Exception error) { ShowError("无法导入该文件", error); }
            }
        }

        private void SaveJson()
        {
            FlushPendingInspectorChange();
            string file = _currentFile;
            if (String.IsNullOrEmpty(file))
            {
                using (SaveFileDialog dialog = SaveDialog("JSON 关系图 (*.json)|*.json", ".json"))
                { if (dialog.ShowDialog(this) != DialogResult.OK) return; file = dialog.FileName; }
            }
            try { NativeExport.SaveJson(_canvas.Document, file); _currentFile = file; UpdateTitle(); _statusText.Text = "JSON 已保存"; }
            catch (Exception error) { ShowError("无法保存文件", error); }
        }

        private void ExportReadonly() { ExportWithDialog("只读可视图 (*.html)|*.html", ".html", delegate(string file) { NativeExport.SaveReadonlyHtml(_canvas.Document, file); }, "只读可视图已导出，可直接分享或重新导入"); }
        private void ExportFeishuBoard() { ExportWithDialog("飞书画板文件 (*.drawio)|*.drawio", ".drawio", delegate(string file) { NativeExport.SaveDrawio(_canvas.Document, file); }, "飞书画板文件已导出；在飞书桌面端画板中选择导入即可编辑每个节点"); }
        private void ExportSvg() { ExportWithDialog("SVG 矢量图 (*.svg)|*.svg", ".svg", delegate(string file) { NativeExport.SaveSvg(_canvas.Document, file); }, "SVG 已导出"); }
        private void ExportPng() { ExportWithDialog("PNG 图片 (*.png)|*.png", ".png", delegate(string file) { NativeExport.SavePng(_canvas, file); }, "PNG 已导出"); }
        private void ExportPdf() { ExportWithDialog("PDF 文档 (*.pdf)|*.pdf", ".pdf", delegate(string file) { NativeExport.SavePdf(_canvas, file); }, "PDF 已导出"); }

        private void ExportWithDialog(string filter, string extension, Action<string> exporter, string success)
        {
            FlushPendingInspectorChange();
            using (SaveFileDialog dialog = SaveDialog(filter, extension))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { exporter(dialog.FileName); _statusText.Text = success; }
                catch (Exception error) { ShowError("导出失败", error); }
            }
        }

        private SaveFileDialog SaveDialog(string filter, string extension)
        {
            SaveFileDialog dialog = new SaveFileDialog(); dialog.Filter = filter; dialog.DefaultExt = extension.TrimStart('.'); dialog.AddExtension = true;
            dialog.FileName = SafeFileName(_canvas.Document.meta.title) + extension; return dialog;
        }

        private static string SafeFileName(string value)
        {
            string result = value ?? "关系图"; foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_'); return result;
        }

        private void Undo()
        {
            FlushPendingInspectorChange();
            if (_undo.Count == 0) return; PushHistory(_redo, _canvas.Document); _canvas.RestoreDocumentPreservingView(_undo.Pop());
            SaveAutosave(); ApplyTheme(); RebuildInspector(); UpdateStatus(); _statusText.Text = "已撤销"; UpdateTitle();
        }

        private void Redo()
        {
            FlushPendingInspectorChange();
            if (_redo.Count == 0) return; PushHistory(_undo, _canvas.Document); _canvas.RestoreDocumentPreservingView(_redo.Pop());
            SaveAutosave(); ApplyTheme(); RebuildInspector(); UpdateStatus(); _statusText.Text = "已重做"; UpdateTitle();
        }

        private void AddGroup()
        {
            FlushPendingInspectorChange();
            GraphDocument before = GraphSerialization.Clone(_canvas.Document);
            HashSet<string> ids = new HashSet<string>(_canvas.Document.groups.Select(delegate(GraphGroup item) { return item.id; }));
            RectangleF selectedBounds; bool wrapsSelection = TrySelectedContentBounds(out selectedBounds);
            float x, y, width, height;
            if (wrapsSelection)
            {
                const float horizontalPadding = 28f, titlePadding = 50f, bottomPadding = 28f;
                width = Math.Max(220f, selectedBounds.Width + horizontalPadding * 2f);
                height = Math.Max(150f, selectedBounds.Height + titlePadding + bottomPadding);
                x = selectedBounds.Left - (width - selectedBounds.Width) / 2f;
                y = selectedBounds.Top - titlePadding;
            }
            else
            {
                PointF center = _canvas.ViewCenterWorld; width = 300f; height = 260f;
                x = center.X - width / 2f; y = center.Y - height / 2f;
            }
            GraphGroup group = new GraphGroup { id = GraphSerialization.UniqueId("group", ids), label = "新分组", x = x, y = y, w = width, h = height };
            _canvas.Document.groups.Add(group); CommitChange(before, wrapsSelection ? "已为选中内容创建分组" : "分组已添加"); _canvas.SelectEntity("group", group.id);
        }

        private bool TrySelectedContentBounds(out RectangleF bounds)
        {
            bounds = RectangleF.Empty; bool found = false;
            HashSet<string> nodeIds = new HashSet<string>(_canvas.SelectedNodeIds, StringComparer.Ordinal);
            HashSet<string> groupIds = new HashSet<string>(_canvas.SelectedGroupIds, StringComparer.Ordinal);
            foreach (GraphNode node in _canvas.Document.nodes)
            {
                if (!nodeIds.Contains(node.id)) continue;
                RectangleF rect = new RectangleF(node.x, node.y, node.w, node.h); bounds = found ? RectangleF.Union(bounds, rect) : rect; found = true;
            }
            foreach (GraphGroup group in _canvas.Document.groups)
            {
                if (!groupIds.Contains(group.id)) continue;
                RectangleF rect = new RectangleF(group.x, group.y, group.w, group.h); bounds = found ? RectangleF.Union(bounds, rect) : rect; found = true;
            }
            return found;
        }

        private void AddNode()
        {
            AddNodeAt(_canvas.ViewCenterWorld, "节点已在屏幕中心添加");
        }

        private void AddNodeAt(PointF worldPoint) { AddNodeAt(worldPoint, "已在双击位置创建节点"); }

        private void AddNodeAt(PointF worldPoint, string commitMessage)
        {
            FlushPendingInspectorChange();
            if (!_canvas.EditMode) return;
            GraphDocument before = GraphSerialization.Clone(_canvas.Document);
            HashSet<string> ids = new HashSet<string>(_canvas.Document.nodes.Select(delegate(GraphNode item) { return item.id; }));
            GraphGroup group = null;
            for (int index = _canvas.Document.groups.Count - 1; index >= 0; index--)
            {
                GraphGroup candidate = _canvas.Document.groups[index];
                if (new RectangleF(candidate.x, candidate.y, candidate.w, candidate.h).Contains(worldPoint)) { group = candidate; break; }
            }
            const float width = 150f, height = 55f;
            GraphNode node = new GraphNode
            {
                id = GraphSerialization.UniqueId("node", ids), label = "新节点", type = "工作步骤", kind = "system",
                group = group == null ? "" : group.id,
                x = worldPoint.X - width / 2f,
                y = worldPoint.Y - height / 2f,
                w = width, h = height, note = ""
            };
            _canvas.Document.nodes.Add(node); CommitChange(before, commitMessage); _canvas.SelectEntity("node", node.id);
        }

        internal void AddGroupForTesting() { AddGroup(); }
        internal void AddNodeForTesting() { AddNode(); }
        internal void AddNodeAtForTesting(PointF worldPoint) { AddNodeAt(worldPoint); }
        internal GraphCanvas CanvasForTesting { get { return _canvas; } }

        private void AddRelation()
        {
            FlushPendingInspectorChange();
            if (_canvas.Document.nodes.Count + _canvas.Document.groups.Count < 2) { MessageBox.Show(this, "至少需要两个节点或分组。", "无法新增关系", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (RelationDialog dialog = new RelationDialog(_canvas.Document, _canvas.NewLineType, _darkTheme))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                GraphDocument before = GraphSerialization.Clone(_canvas.Document);
                HashSet<string> ids = new HashSet<string>(_canvas.Document.edges.Select(delegate(GraphEdge item) { return item.id; }));
                GraphEdge edge = dialog.CreateEdge(GraphSerialization.UniqueId("edge", ids));
                bool duplicate = _canvas.Document.edges.Any(delegate(GraphEdge item) { return GraphSerialization.EndpointKey(item.sourceType, item.source) == GraphSerialization.EndpointKey(edge.sourceType, edge.source) && GraphSerialization.EndpointKey(item.targetType, item.target) == GraphSerialization.EndpointKey(edge.targetType, edge.target); });
                if (duplicate) { MessageBox.Show(this, "这两个对象之间已经存在同方向关系。", "关系未新增", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                _canvas.Document.edges.Add(edge); CommitChange(before, "关系已添加"); _canvas.SelectEntity("edge", edge.id);
            }
        }

        private void DeleteSelected()
        {
            FlushPendingInspectorChange();
            if (!_canvas.EditMode || String.IsNullOrEmpty(_canvas.SelectedType)) return;
            GraphDocument before = GraphSerialization.Clone(_canvas.Document); bool changed = false;
            if (_canvas.SelectedType == "node" || _canvas.SelectedType == "group" || _canvas.SelectedType == "mixed")
            {
                HashSet<string> nodeIds = new HashSet<string>(_canvas.SelectedNodeIds);
                HashSet<string> groupIds = new HashSet<string>(_canvas.SelectedGroupIds);
                changed = _canvas.Document.nodes.RemoveAll(delegate(GraphNode item) { return nodeIds.Contains(item.id); }) > 0;
                changed = _canvas.Document.groups.RemoveAll(delegate(GraphGroup item) { return groupIds.Contains(item.id); }) > 0 || changed;
                _canvas.Document.edges.RemoveAll(delegate(GraphEdge edge)
                {
                    return (edge.sourceType == "group" ? groupIds.Contains(edge.source) : nodeIds.Contains(edge.source)) ||
                        (edge.targetType == "group" ? groupIds.Contains(edge.target) : nodeIds.Contains(edge.target));
                });
            }
            else if (_canvas.SelectedType == "edge") { string id = _canvas.SelectedId; changed = _canvas.Document.edges.RemoveAll(delegate(GraphEdge item) { return item.id == id; }) > 0; }
            if (!changed) return; _canvas.ClearSelection(); CommitChange(before, "已删除选中项");
        }

        private bool CopySelected(bool useSystemClipboard)
        {
            FlushPendingInspectorChange();
            GraphClipboardPayload payload = BuildClipboardPayload();
            if (payload == null)
            {
                _statusText.Text = _canvas.SelectedType == "edge" ? "关系线不能单独复制，请选择其两端节点" : "请先选择要复制的节点或分组";
                return false;
            }
            string json = GraphSerialization.SerializeClipboard(payload);
            _selectionClipboardJson = json;
            if (useSystemClipboard)
            {
                try
                {
                    DataObject data = new DataObject(); data.SetData(GraphSerialization.ClipboardFormat, false, json);
                    Clipboard.SetDataObject(data, true);
                }
                catch { }
            }
            _statusText.Text = "已复制 " + payload.groups.Count + " 个分组、" + payload.nodes.Count + " 个节点和 " + payload.edges.Count + " 条内部关系";
            return true;
        }

        private GraphClipboardPayload BuildClipboardPayload()
        {
            if (_canvas.Document == null || String.IsNullOrEmpty(_canvas.SelectedType) || _canvas.SelectedType == "edge") return null;
            GraphClipboardPayload payload = new GraphClipboardPayload
            {
                selectionVersion = 2,
                selectionType = _canvas.SelectedGroupIds.Count > 0 && _canvas.SelectedNodeIds.Count > 0 ? "mixed" : _canvas.SelectedGroupIds.Count > 1 ? "groups" : _canvas.SelectedGroupIds.Count == 1 ? "group" : "nodes",
                selectedGroupIds = _canvas.SelectedGroupIds.ToList(), selectedNodeIds = _canvas.SelectedNodeIds.ToList(),
                groups = new List<GraphGroup>(), nodes = new List<GraphNode>(), edges = new List<GraphEdge>()
            };
            HashSet<string> nodeIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> groupIds = new HashSet<string>(StringComparer.Ordinal);
            nodeIds.UnionWith(_canvas.SelectedNodeIds);
            groupIds.UnionWith(_canvas.SelectedGroupIds);
            foreach (GraphGroup child in _canvas.Document.groups)
                if (child.groups != null && child.groups.Any(delegate(string parentId) { return _canvas.SelectedGroupIds.Contains(parentId); })) groupIds.Add(child.id);
            payload.groups.AddRange(_canvas.Document.groups.Where(delegate(GraphGroup group) { return groupIds.Contains(group.id); }));
            foreach (GraphNode member in _canvas.Document.nodes.Where(delegate(GraphNode node) { return node.groups != null && node.groups.Any(delegate(string groupId) { return groupIds.Contains(groupId); }); })) nodeIds.Add(member.id);
            payload.nodes.AddRange(_canvas.Document.nodes.Where(delegate(GraphNode node) { return nodeIds.Contains(node.id); }));
            if (payload.groups.Count == 0 && payload.nodes.Count == 0)
            {
                return null;
            }
            payload.edges.AddRange(_canvas.Document.edges.Where(delegate(GraphEdge edge)
            {
                return ClipboardEndpointIncluded(edge.sourceType, edge.source, nodeIds, groupIds) && ClipboardEndpointIncluded(edge.targetType, edge.target, nodeIds, groupIds);
            }));
            return payload;
        }

        private static bool ClipboardEndpointIncluded(string type, string id, HashSet<string> nodeIds, HashSet<string> groupIds)
        {
            return type == "group" ? groupIds.Contains(id ?? "") : nodeIds.Contains(id ?? "");
        }

        private bool PasteSelected(bool useSystemClipboard)
        {
            FlushPendingInspectorChange();
            string json = "";
            if (useSystemClipboard)
            {
                try
                {
                    object data = Clipboard.GetData(GraphSerialization.ClipboardFormat);
                    json = data as string ?? "";
                }
                catch { }
            }
            if (String.IsNullOrEmpty(json)) json = _selectionClipboardJson;
            if (String.IsNullOrEmpty(json)) { _statusText.Text = "剪贴板中没有可粘贴的关系图内容"; return false; }

            try
            {
                GraphClipboardPayload payload = GraphSerialization.DeserializeClipboard(json);
                List<GraphGroup> sourceGroups = payload.groups.Where(delegate(GraphGroup item) { return item != null; }).ToList();
                List<GraphNode> sourceNodes = payload.nodes.Where(delegate(GraphNode item) { return item != null; }).ToList();
                List<GraphEdge> sourceEdges = payload.edges.Where(delegate(GraphEdge item) { return item != null; }).ToList();
                HashSet<string> sourceGroupIds = new HashSet<string>(sourceGroups.Select(delegate(GraphGroup item) { return item.id ?? ""; }), StringComparer.Ordinal);
                HashSet<string> selectedSourceGroupIds = payload.selectionVersion >= 2
                    ? new HashSet<string>(payload.selectedGroupIds, StringComparer.Ordinal)
                    : new HashSet<string>(sourceGroups.Where(delegate(GraphGroup item)
                    {
                        return item.groups == null || !item.groups.Any(delegate(string parentId) { return sourceGroupIds.Contains(parentId); });
                    }).Select(delegate(GraphGroup item) { return item.id ?? ""; }), StringComparer.Ordinal);
                HashSet<string> explicitlySelectedSourceNodes = new HashSet<string>(StringComparer.Ordinal);
                if (payload.selectionVersion >= 2) explicitlySelectedSourceNodes.UnionWith(payload.selectedNodeIds);
                else if (payload.selectionType == "nodes") explicitlySelectedSourceNodes.UnionWith(sourceNodes.Select(delegate(GraphNode item) { return item.id ?? ""; }));
                else if (payload.selectionType == "mixed") explicitlySelectedSourceNodes.UnionWith(sourceNodes.Where(delegate(GraphNode item)
                {
                    return item.groups == null ? !sourceGroupIds.Contains(item.group ?? "") : !item.groups.Any(delegate(string groupId) { return sourceGroupIds.Contains(groupId); });
                }).Select(delegate(GraphNode item) { return item.id ?? ""; }));
                if (sourceGroups.Count == 0 && sourceNodes.Count == 0) { _statusText.Text = "剪贴板中没有可粘贴的节点或分组"; return false; }
                if (_canvas.Document.groups.Count + sourceGroups.Count > GraphSerialization.MaxGroups || _canvas.Document.nodes.Count + sourceNodes.Count > GraphSerialization.MaxNodes || _canvas.Document.edges.Count + sourceEdges.Count > GraphSerialization.MaxEdges)
                    throw new InvalidOperationException("粘贴后关系图的数据量将超过程序上限。");
                _canvas.EditMode = true;

                GraphDocument before = GraphSerialization.Clone(_canvas.Document);
                float dx, dy; CalculatePasteOffset(sourceGroups, sourceNodes, out dx, out dy);
                HashSet<string> usedGroupIds = new HashSet<string>(_canvas.Document.groups.Select(delegate(GraphGroup item) { return item.id; }), StringComparer.Ordinal);
                HashSet<string> usedNodeIds = new HashSet<string>(_canvas.Document.nodes.Select(delegate(GraphNode item) { return item.id; }), StringComparer.Ordinal);
                HashSet<string> usedEdgeIds = new HashSet<string>(_canvas.Document.edges.Select(delegate(GraphEdge item) { return item.id; }), StringComparer.Ordinal);
                Dictionary<string, string> groupMap = new Dictionary<string, string>(StringComparer.Ordinal);
                Dictionary<string, string> nodeMap = new Dictionary<string, string>(StringComparer.Ordinal);
                List<string> pastedGroupIds = new List<string>(), pastedNodeIds = new List<string>(), pastedEdgeIds = new List<string>();

                foreach (GraphGroup source in sourceGroups)
                {
                    string oldId = source.id ?? ""; string newId = GraphSerialization.UniqueId(oldId + "_copy", usedGroupIds); usedGroupIds.Add(newId);
                    if (!groupMap.ContainsKey(oldId)) groupMap.Add(oldId, newId);
                    GraphGroup created = new GraphGroup { id = newId, label = source.label, x = source.x + dx, y = source.y + dy, w = source.w, h = source.h };
                    _canvas.Document.groups.Add(created); pastedGroupIds.Add(newId);
                }
                foreach (GraphNode source in sourceNodes)
                {
                    string oldId = source.id ?? ""; string newId = GraphSerialization.UniqueId(oldId + "_copy", usedNodeIds); usedNodeIds.Add(newId);
                    if (!nodeMap.ContainsKey(oldId)) nodeMap.Add(oldId, newId);
                    string groupId = "", mappedGroup;
                    if (groupMap.TryGetValue(source.group ?? "", out mappedGroup)) groupId = mappedGroup;
                    else if (_canvas.Document.groups.Any(delegate(GraphGroup group) { return group.id == (source.group ?? ""); })) groupId = source.group ?? "";
                    GraphNode created = new GraphNode { id = newId, label = source.label, type = source.type, kind = source.kind, group = groupId, x = source.x + dx, y = source.y + dy, w = source.w, h = source.h, note = source.note };
                    _canvas.Document.nodes.Add(created); pastedNodeIds.Add(newId);
                }
                foreach (GraphEdge source in sourceEdges)
                {
                    string sourceId = MapClipboardEndpoint(source.sourceType, source.source, nodeMap, groupMap);
                    string targetId = MapClipboardEndpoint(source.targetType, source.target, nodeMap, groupMap);
                    string sourceType = source.sourceType == "group" ? "group" : "node", targetType = source.targetType == "group" ? "group" : "node";
                    if (!EndpointExists(_canvas.Document, sourceType, sourceId) || !EndpointExists(_canvas.Document, targetType, targetId)) continue;
                    string newId = GraphSerialization.UniqueId((source.id ?? "edge") + "_copy", usedEdgeIds); usedEdgeIds.Add(newId);
                    GraphEdge created = new GraphEdge { id = newId, source = sourceId, target = targetId, sourceType = sourceType, targetType = targetType, label = source.label, category = source.category, lineType = source.lineType, sourceSide = source.sourceSide, targetSide = source.targetSide };
                    _canvas.Document.edges.Add(created); pastedEdgeIds.Add(newId);
                }
                CommitChange(before, "已粘贴 " + pastedNodeIds.Count + " 个节点、" + pastedGroupIds.Count + " 个分组和 " + pastedEdgeIds.Count + " 条关系");
                List<string> selectedPastedNodes = nodeMap.Where(delegate(KeyValuePair<string, string> pair) { return explicitlySelectedSourceNodes.Contains(pair.Key); }).Select(delegate(KeyValuePair<string, string> pair) { return pair.Value; }).ToList();
                List<string> selectedPastedGroups = payload.selectionType == "group" || payload.selectionType == "groups" || payload.selectionType == "mixed" ? groupMap.Where(delegate(KeyValuePair<string, string> pair) { return selectedSourceGroupIds.Contains(pair.Key); }).Select(delegate(KeyValuePair<string, string> pair) { return pair.Value; }).ToList() : new List<string>();
                _canvas.SelectObjects(selectedPastedNodes, selectedPastedGroups);
                return true;
            }
            catch (Exception error) { _statusText.Text = "无法粘贴：" + error.Message; return false; }
        }

        private void CalculatePasteOffset(List<GraphGroup> groups, List<GraphNode> nodes, out float dx, out float dy)
        {
            float left = Single.MaxValue, top = Single.MaxValue, right = Single.MinValue, bottom = Single.MinValue;
            foreach (GraphGroup group in groups)
            {
                left = Math.Min(left, group.x); top = Math.Min(top, group.y);
                right = Math.Max(right, group.x + group.w); bottom = Math.Max(bottom, group.y + group.h);
            }
            foreach (GraphNode node in nodes)
            {
                left = Math.Min(left, node.x); top = Math.Min(top, node.y);
                right = Math.Max(right, node.x + node.w); bottom = Math.Max(bottom, node.y + node.h);
            }
            PointF target = _canvas.ViewCenterWorld;
            dx = target.X - (left + right) / 2f;
            dy = target.Y - (top + bottom) / 2f;
        }

        private static string MapClipboardEndpoint(string type, string id, Dictionary<string, string> nodeMap, Dictionary<string, string> groupMap)
        {
            string value = id ?? "", mapped;
            if (type == "group" && groupMap.TryGetValue(value, out mapped)) return mapped;
            if (type != "group" && nodeMap.TryGetValue(value, out mapped)) return mapped;
            return value;
        }

        private static bool EndpointExists(GraphDocument graph, string type, string id)
        {
            return type == "group" ? graph.groups.Any(delegate(GraphGroup item) { return item.id == id; }) : graph.nodes.Any(delegate(GraphNode item) { return item.id == id; });
        }

        internal bool CopySelectionForTesting() { return CopySelected(false); }
        internal bool PasteSelectionForTesting() { return PasteSelected(false); }
        internal void UndoForTesting() { Undo(); }
        internal void RedoForTesting() { Redo(); }

        private void FindEntity()
        {
            string query = (_searchBox.Text ?? "").Trim(); if (query.Length == 0) return;
            GraphNode node = _canvas.Document.nodes.FirstOrDefault(delegate(GraphNode item) { return item.label.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.type.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0; });
            if (node != null) { _canvas.SelectEntity("node", node.id); _statusText.Text = "已定位节点：" + node.label; return; }
            GraphGroup group = _canvas.Document.groups.FirstOrDefault(delegate(GraphGroup item) { return item.label.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0; });
            if (group != null) { _canvas.SelectEntity("group", group.id); _statusText.Text = "已定位分组：" + group.label; return; }
            _statusText.Text = "没有找到“" + query + "”";
        }

        private void RebuildInspector()
        {
            if (_settingUi || _canvas.Document == null) return; _settingUi = true;
            _inspector.SuspendLayout();
            while (_inspector.Controls.Count > 0) _inspector.Controls[0].Dispose();
            int y = 18;
            AddInspectorTitle("属性", ref y);
            if (String.IsNullOrEmpty(_canvas.SelectedType)) BuildProjectInspector(ref y);
            else if (_canvas.SelectedType == "node" && _canvas.SelectedNodeIds.Count > 1) BuildMultiNodeInspector(ref y);
            else if (_canvas.SelectionCount > 1) BuildMultiObjectInspector(ref y);
            else if (_canvas.SelectedType == "node") BuildNodeInspector(ref y);
            else if (_canvas.SelectedType == "group") BuildGroupInspector(ref y);
            else if (_canvas.SelectedType == "edge") BuildEdgeInspector(ref y);
            ApplyInspectorAccessMode(_inspector, _canvas.EditMode);
            _inspector.ResumeLayout(); _settingUi = false;
            NativeTheme.ApplyControlTree(_inspector, _darkTheme);
        }

        private static void ApplyInspectorAccessMode(Control root, bool editable)
        {
            TextBoxBase text = root as TextBoxBase;
            if (text != null) text.ReadOnly = !editable;
            if (root is ComboBox || root is Button) root.Enabled = editable;
            foreach (Control child in root.Controls) ApplyInspectorAccessMode(child, editable);
        }

        private static bool InspectorContainsEditableControl(Control root)
        {
            TextBoxBase text = root as TextBoxBase;
            if (text != null && !text.ReadOnly) return true;
            if ((root is ComboBox || root is Button) && root.Enabled) return true;
            foreach (Control child in root.Controls) if (InspectorContainsEditableControl(child)) return true;
            return false;
        }

        internal bool InspectorEditableForTesting { get { return InspectorContainsEditableControl(_inspector); } }
        internal bool EditModeForTesting { get { return _canvas.EditMode; } }
        internal bool InspectorHasApplyButtonForTesting { get { return ContainsButtonText(_inspector, "应用"); } }
        internal bool SetSelectedNodeNameThroughInspectorForTesting(string value)
        {
            if (_canvas.SelectedType != "node") return false;
            GraphNode node = _canvas.Document.nodes.FirstOrDefault(delegate(GraphNode item) { return item.id == _canvas.SelectedId; }); if (node == null) return false;
            TextBox box = FindTextBoxWithValue(_inspector, node.label); if (box == null || box.ReadOnly) return false;
            box.Text = value; FlushPendingInspectorChange(); return node.label == value;
        }
        internal bool SetSelectedNodeColorThroughInspectorForTesting(string kind)
        {
            if (_canvas.SelectedType != "node") return false;
            GraphNode node = _canvas.Document.nodes.FirstOrDefault(delegate(GraphNode item) { return item.id == _canvas.SelectedId; }); if (node == null) return false;
            ComboBox box = FindColorCategoryBox(_inspector); if (box == null || !box.Enabled) return false;
            for (int i = 0; i < box.Items.Count; i++)
            {
                ColorCategoryChoice choice = box.Items[i] as ColorCategoryChoice;
                if (choice != null && choice.Kind == kind) { box.SelectedIndex = i; FlushPendingInspectorChange(); return node.kind == kind; }
            }
            return false;
        }
        internal bool ColorCategoryExamplesForTesting
        {
            get
            {
                ComboBox box = FindColorCategoryBox(_inspector); if (box == null || box.Items.Count != 6) return false;
                foreach (object item in box.Items) { ColorCategoryChoice choice = item as ColorCategoryChoice; if (choice == null || choice.SampleColor.IsEmpty || choice.Text.IndexOf("色", StringComparison.Ordinal) < 0) return false; }
                return true;
            }
        }
        internal bool EndpointInspectorSelectionsForTesting
        {
            get
            {
                List<ComboBox> boxes = new List<ComboBox>(); CollectEndpointBoxes(_inspector, boxes);
                return boxes.Count >= 2 && boxes.All(delegate(ComboBox box) { return box.Items.Count > 0 && box.SelectedIndex >= 0 && box.SelectedIndex < box.Items.Count; }) && boxes.Any(delegate(ComboBox box) { return box.SelectedIndex >= 3; });
            }
        }

        private static bool ContainsButtonText(Control root, string text)
        {
            Button button = root as Button; if (button != null && button.Text.IndexOf(text, StringComparison.Ordinal) >= 0) return true;
            foreach (Control child in root.Controls) if (ContainsButtonText(child, text)) return true; return false;
        }
        private static TextBox FindTextBoxWithValue(Control root, string value)
        {
            TextBox box = root as TextBox; if (box != null && box.Text == value) return box;
            foreach (Control child in root.Controls) { TextBox found = FindTextBoxWithValue(child, value); if (found != null) return found; } return null;
        }
        private static ComboBox FindColorCategoryBox(Control root)
        {
            ComboBox box = root as ComboBox; if (box != null && box.Items.Count > 0 && box.Items[0] is ColorCategoryChoice) return box;
            foreach (Control child in root.Controls) { ComboBox found = FindColorCategoryBox(child); if (found != null) return found; } return null;
        }
        private static void CollectEndpointBoxes(Control root, List<ComboBox> result)
        {
            ComboBox box = root as ComboBox; if (box != null && box.Items.Count > 0 && box.Items[0] is EndpointItem) result.Add(box);
            foreach (Control child in root.Controls) CollectEndpointBoxes(child, result);
        }

        private void BuildProjectInspector(ref int y)
        {
            AddMuted("未选择对象", ref y); AddParagraph("先单击对象进行选择；再次拖动已选择的节点或分组可移动。拖动未选择的节点或分组会创建关系。右键可清除全部选择。", ref y);
            TextBox title = AddTextField("图名称", _canvas.Document.meta.title, false, ref y);
            BindAutoText(title, false, delegate(string value) { _canvas.Document.meta.title = value; UpdateTitle(); }, "图名称已自动保存");
            AddMuted("画布：理论无限，可向任意方向平移和摆放内容。\n节点可同时属于多个分组，分组也可多层嵌套；所属关系按位置自动匹配。", ref y);
        }

        private void BuildNodeInspector(ref int y)
        {
            GraphNode node = _canvas.Document.nodes.FirstOrDefault(delegate(GraphNode item) { return item.id == _canvas.SelectedId; }); if (node == null) return;
            AddMuted("节点 · " + node.id, ref y);
            TextBox label = AddTextField("名称", node.label, false, ref y); TextBox type = AddTextField("类型", node.type, false, ref y);
            ComboBox kind = AddColorCategoryField(node.kind, ref y);
            AddMuted("仅影响节点配色，不影响节点类型、关系或功能。", ref y);
            AddLabel("所属分组（自动匹配，只读）", ref y); AddParagraph(AutomaticMembershipText(node.groups), ref y);
            TextBox note = AddTextField("备注", node.note, true, ref y);
            BindAutoText(label, false, delegate(string value) { node.label = value; }, "节点名称已自动保存");
            BindAutoText(type, false, delegate(string value) { node.type = value; }, "节点类型已自动保存");
            BindAutoCombo(kind, delegate { ColorCategoryChoice choice = kind.SelectedItem as ColorCategoryChoice; if (choice != null) node.kind = choice.Kind; }, "节点颜色分类已自动保存");
            BindAutoText(note, true, delegate(string value) { node.note = value; }, "节点备注已自动保存");
            AddParagraph(RelationSummary("node", node.id), ref y);
        }

        private void BuildMultiNodeInspector(ref int y)
        {
            List<string> ids = _canvas.SelectedNodeIds.ToList(); AddMuted("已选择 " + ids.Count + " 个节点", ref y);
            TextBox type = AddTextField("统一类型（留空则不修改）", "", false, ref y);
            BindAutoText(type, false, delegate(string value) { foreach (GraphNode node in _canvas.Document.nodes.Where(delegate(GraphNode item) { return ids.Contains(item.id); })) node.type = value; }, "多个节点类型已自动保存");
            AddMuted("所属分组由节点位置自动匹配，不支持手工或批量编辑。", ref y);
        }

        private void BuildMultiObjectInspector(ref int y)
        {
            AddMuted("已选择 " + _canvas.SelectedNodeIds.Count + " 个节点、" + _canvas.SelectedGroupIds.Count + " 个分组", ref y);
            AddParagraph("可整体移动、复制、粘贴或删除这些选中内容。多选状态下不会自动高亮邻居。", ref y);
        }

        private void BuildGroupInspector(ref int y)
        {
            GraphGroup group = _canvas.Document.groups.FirstOrDefault(delegate(GraphGroup item) { return item.id == _canvas.SelectedId; }); if (group == null) return;
            AddMuted("分组 · " + group.id, ref y); TextBox label = AddTextField("名称", group.label, false, ref y);
            BindAutoText(label, false, delegate(string value) { group.label = value; }, "分组名称已自动保存");
            AddLabel("所属分组（自动匹配，只读）", ref y); AddParagraph(AutomaticMembershipText(group.groups), ref y);
            AddParagraph("位置 " + (int)group.x + ", " + (int)group.y + "　大小 " + (int)group.w + " × " + (int)group.h + "\n" + RelationSummary("group", group.id), ref y);
        }

        private void BuildEdgeInspector(ref int y)
        {
            GraphEdge edge = _canvas.Document.edges.FirstOrDefault(delegate(GraphEdge item) { return item.id == _canvas.SelectedId; }); if (edge == null) return;
            AddMuted("关系 · " + edge.id, ref y); List<EndpointItem> endpoints = Endpoints();
            ComboBox source = AddEndpointField("起点", endpoints, GraphSerialization.EndpointKey(edge.sourceType, edge.source), ref y);
            ComboBox target = AddEndpointField("终点", endpoints, GraphSerialization.EndpointKey(edge.targetType, edge.target), ref y);
            TextBox label = AddTextField("名称（可留空）", edge.label, false, ref y);
            ComboBox category = AddComboField("关系类别", _canvas.Document.settings.relationTypes.Select(delegate(RelationType item) { return item.label; }).ToArray(), Math.Max(0, _canvas.Document.settings.relationTypes.FindIndex(delegate(RelationType item) { return item.id == edge.category; })), ref y);
            ComboBox line = AddComboField("线型", new[] { "曲线", "直线", "折线" }, LineTypeIndex(edge.lineType), ref y);
            AddMuted("线条颜色自动跟随起点节点的颜色分类；起点颜色改变时会同步更新。", ref y);
            Action applyEndpoints = delegate
            {
                EndpointItem s = source.SelectedItem as EndpointItem, t = target.SelectedItem as EndpointItem;
                if (s == null || t == null || s.Key == t.Key) { _statusText.Text = "起点和终点不能相同，未自动应用"; try { BeginInvoke((Action)delegate { RebuildInspector(); }); } catch { } return; }
                bool duplicate = _canvas.Document.edges.Any(delegate(GraphEdge item) { return item.id != edge.id && GraphSerialization.EndpointKey(item.sourceType, item.source) == s.Key && GraphSerialization.EndpointKey(item.targetType, item.target) == t.Key; });
                if (duplicate) { _statusText.Text = "同方向关系已存在，未自动应用"; try { BeginInvoke((Action)delegate { RebuildInspector(); }); } catch { } return; }
                ApplyInspectorChange(source.ContainsFocus ? (Control)source : target, delegate { edge.sourceType = s.Type; edge.source = s.Id; edge.targetType = t.Type; edge.target = t.Id; edge.sourceSide = ""; edge.targetSide = ""; }, "关系端点已自动保存");
            };
            source.SelectedIndexChanged += delegate { if (!_settingUi && _canvas.EditMode) applyEndpoints(); };
            target.SelectedIndexChanged += delegate { if (!_settingUi && _canvas.EditMode) applyEndpoints(); };
            source.LostFocus += delegate { FlushPendingInspectorChange(); }; target.LostFocus += delegate { FlushPendingInspectorChange(); };
            BindAutoText(label, true, delegate(string value) { edge.label = value.Trim(); }, "关系名称已自动保存");
            BindAutoCombo(category, delegate { edge.category = _canvas.Document.settings.relationTypes[Math.Max(0, category.SelectedIndex)].id; }, "关系类别已自动保存");
            BindAutoCombo(line, delegate { edge.lineType = LineTypeAt(line.SelectedIndex); }, "关系线型已自动保存");
        }

        private string AutomaticMembershipText(IEnumerable<string> membershipIds)
        {
            HashSet<string> ids = new HashSet<string>(membershipIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            if (ids.Count == 0) return "未分组";
            Dictionary<string, GraphGroup> groups = _canvas.Document.groups.ToDictionary(delegate(GraphGroup item) { return item.id; }, StringComparer.Ordinal);
            List<string> leaves = ids.Where(delegate(string id)
            {
                return !ids.Any(delegate(string otherId)
                {
                    GraphGroup other; return otherId != id && groups.TryGetValue(otherId, out other) && other.groups != null && other.groups.Contains(id);
                });
            }).ToList();
            List<string> paths = new List<string>();
            foreach (string leafId in leaves)
            {
                paths.AddRange(MembershipPathsToGroup(leafId, ids, groups, new HashSet<string>(StringComparer.Ordinal)));
            }
            return paths.Count == 0 ? "未分组" : String.Join("\n", paths.Distinct().ToArray());
        }

        private static List<string> MembershipPathsToGroup(string groupId, HashSet<string> allowedIds, Dictionary<string, GraphGroup> groups, HashSet<string> visiting)
        {
            GraphGroup group;
            if (!groups.TryGetValue(groupId, out group) || visiting.Contains(groupId)) return new List<string>();
            HashSet<string> nextVisiting = new HashSet<string>(visiting, StringComparer.Ordinal); nextVisiting.Add(groupId);
            List<string> candidates = (group.groups ?? new List<string>()).Where(delegate(string id) { return allowedIds.Contains(id) && groups.ContainsKey(id); }).ToList();
            List<string> directParents = candidates.Where(delegate(string parentId)
            {
                return !candidates.Any(delegate(string otherId)
                {
                    GraphGroup other; return otherId != parentId && groups.TryGetValue(otherId, out other) && other.groups != null && other.groups.Contains(parentId);
                });
            }).ToList();
            if (directParents.Count == 0) return new List<string> { group.label };
            List<string> result = new List<string>();
            foreach (string parentId in directParents)
                foreach (string parentPath in MembershipPathsToGroup(parentId, allowedIds, groups, nextVisiting)) result.Add(parentPath + " > " + group.label);
            return result;
        }

        internal string MembershipTextForTesting(IEnumerable<string> ids) { return AutomaticMembershipText(ids); }

        private ComboBox AddEndpointField(string label, List<EndpointItem> endpoints, string selectedKey, ref int y)
        {
            AddLabel(label, ref y); ComboBox box = new ComboBox(); box.DropDownStyle = ComboBoxStyle.DropDownList; box.SetBounds(18, y, Math.Max(190, _inspector.ClientSize.Width - 42), 28); box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _inspector.Controls.Add(box);
            box.Items.AddRange(endpoints.Cast<object>().ToArray());
            int index = endpoints.FindIndex(delegate(EndpointItem item) { return item.Key == selectedKey; });
            if (box.Items.Count > 0) box.SelectedIndex = Math.Max(0, Math.Min(box.Items.Count - 1, index));
            y += 36; return box;
        }

        private string RelationSummary(string type, string id)
        {
            int incoming = _canvas.Document.edges.Count(delegate(GraphEdge edge) { return edge.targetType == type && edge.target == id; });
            int outgoing = _canvas.Document.edges.Count(delegate(GraphEdge edge) { return edge.sourceType == type && edge.source == id; });
            return "上游关系 " + incoming + " 条　下游关系 " + outgoing + " 条";
        }

        private List<EndpointItem> Endpoints()
        {
            List<EndpointItem> result = new List<EndpointItem>();
            result.AddRange(_canvas.Document.nodes.Select(delegate(GraphNode item) { return new EndpointItem { Type = "node", Id = item.id, Label = "节点 · " + item.label }; }));
            result.AddRange(_canvas.Document.groups.Select(delegate(GraphGroup item) { return new EndpointItem { Type = "group", Id = item.id, Label = "分组 · " + item.label }; })); return result;
        }

        private void AddInspectorTitle(string text, ref int y) { Label label = NewLabel(text, 16f, FontStyle.Bold, Color.FromArgb(25, 35, 48)); label.SetBounds(18, y, 250, 30); _inspector.Controls.Add(label); y += 40; }
        private void AddLabel(string text, ref int y) { Label label = NewLabel(text, 9f, FontStyle.Regular, Color.FromArgb(76, 89, 105)); label.SetBounds(18, y, 250, 23); _inspector.Controls.Add(label); y += 24; }
        private void AddMuted(string text, ref int y) { Label label = NewLabel(text, 8.5f, FontStyle.Regular, Color.FromArgb(105, 118, 132)); label.SetBounds(18, y, Math.Max(190, _inspector.ClientSize.Width - 42), 38); label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; _inspector.Controls.Add(label); y += 46; }
        private void AddParagraph(string text, ref int y) { int lines = Math.Max(1, (text ?? "").Split('\n').Length); int height = Math.Max(76, Math.Min(240, lines * 22 + 12)); Label label = NewLabel(text, 9f, FontStyle.Regular, Color.FromArgb(55, 68, 84)); label.SetBounds(18, y, Math.Max(190, _inspector.ClientSize.Width - 42), height); label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; _inspector.Controls.Add(label); y += height + 8; }
        private static Label NewLabel(string text, float size, FontStyle style, Color color) { return new Label { Text = text, Font = new Font("Microsoft YaHei UI", size, style), ForeColor = color, AutoEllipsis = true }; }

        private TextBox AddTextField(string label, string value, bool multiline, ref int y)
        {
            AddLabel(label, ref y); TextBox box = new TextBox(); box.Text = value ?? ""; box.Multiline = multiline; box.ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None;
            int height = multiline ? 78 : 27; box.SetBounds(18, y, Math.Max(190, _inspector.ClientSize.Width - 42), height); box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; _inspector.Controls.Add(box); y += height + 10; return box;
        }

        private ComboBox AddComboField(string label, string[] choices, int selected, ref int y)
        {
            AddLabel(label, ref y); ComboBox box = new ComboBox(); box.DropDownStyle = ComboBoxStyle.DropDownList; box.Items.AddRange(choices.Cast<object>().ToArray()); if (box.Items.Count > 0) box.SelectedIndex = Math.Max(0, Math.Min(box.Items.Count - 1, selected));
            box.SetBounds(18, y, Math.Max(190, _inspector.ClientSize.Width - 42), 28); box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; _inspector.Controls.Add(box); y += 38; return box;
        }

        private ComboBox AddColorCategoryField(string selectedKind, ref int y)
        {
            AddLabel("颜色分类", ref y);
            ColorCategoryChoice[] choices =
            {
                new ColorCategoryChoice("system", "灰蓝色", Color.FromArgb(113, 137, 163)),
                new ColorCategoryChoice("resource", "蓝色", Color.FromArgb(75, 158, 234)),
                new ColorCategoryChoice("output", "绿色", Color.FromArgb(84, 173, 114)),
                new ColorCategoryChoice("content", "粉色", Color.FromArgb(215, 101, 164)),
                new ColorCategoryChoice("staff", "紫色", Color.FromArgb(139, 111, 188)),
                new ColorCategoryChoice("commercial", "橙色", Color.FromArgb(232, 150, 62))
            };
            ComboBox box = new ComboBox(); box.DropDownStyle = ComboBoxStyle.DropDownList; box.DrawMode = DrawMode.OwnerDrawFixed; box.ItemHeight = 24; box.Items.AddRange(choices.Cast<object>().ToArray());
            int selected = Array.FindIndex(choices, delegate(ColorCategoryChoice item) { return item.Kind == selectedKind; }); box.SelectedIndex = selected < 0 ? 0 : selected;
            box.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                e.DrawBackground(); if (e.Index < 0 || e.Index >= box.Items.Count) return;
                ColorCategoryChoice choice = (ColorCategoryChoice)box.Items[e.Index];
                Rectangle swatch = new Rectangle(e.Bounds.X + 7, e.Bounds.Y + 5, 20, Math.Max(10, e.Bounds.Height - 10));
                using (Brush fill = new SolidBrush(choice.SampleColor)) e.Graphics.FillRectangle(fill, swatch);
                using (Pen border = new Pen(_darkTheme ? Color.FromArgb(175, 187, 198) : Color.FromArgb(90, 103, 117))) e.Graphics.DrawRectangle(border, swatch);
                Color textColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected ? e.ForeColor : (_darkTheme ? NativeTheme.DarkText : NativeTheme.LightText);
                TextRenderer.DrawText(e.Graphics, choice.Text, box.Font, new Rectangle(e.Bounds.X + 34, e.Bounds.Y, e.Bounds.Width - 36, e.Bounds.Height), textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            box.SetBounds(18, y, Math.Max(190, _inspector.ClientSize.Width - 42), 30); box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; _inspector.Controls.Add(box); y += 40; return box;
        }

        private void UpdateStatus()
        {
            if (_canvas.Document == null) return; _countText.Text = _canvas.Document.groups.Count + " 分组　" + _canvas.Document.nodes.Count + " 节点　" + _canvas.Document.edges.Count + " 关系"; _zoomText.Text = "缩放 " + Math.Round(_canvas.Zoom * 100) + "%";
            _undoButton.Enabled = _undo.Count > 0; _redoButton.Enabled = _redo.Count > 0;
        }

        private void UpdateTitle() { if (_canvas.Document != null) Text = _canvas.Document.meta.title + " — 关系图编辑器" + (String.IsNullOrEmpty(_currentFile) ? "" : "  [" + Path.GetFileName(_currentFile) + "]"); }
        private static string LineTypeAt(int index) { return index == 1 ? "straight" : index == 2 ? "polyline" : "curve"; }
        private static int LineTypeIndex(string lineType) { return lineType == "straight" ? 1 : lineType == "polyline" ? 2 : 0; }

        private void MainFormKeyDown(object sender, KeyEventArgs e)
        {
            bool textInputFocused = TextInputHasFocus();
            if (e.Control && e.KeyCode == Keys.C && !textInputFocused) { CopySelected(true); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.V && !textInputFocused) { PasteSelected(true); e.Handled = true; e.SuppressKeyPress = true; }
            else if (ShouldDeleteSelection(e.KeyCode, textInputFocused)) { DeleteSelected(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F2) { _inspector.Focus(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { _canvas.CancelActiveGesture(); _canvas.ClearSelection(); }
            else if (e.Control && e.KeyCode == Keys.F) { _searchBox.Focus(); e.Handled = true; }
        }

        private bool TextInputHasFocus()
        {
            if (HasTextInputFocus(this)) return true;
            if (_tools != null) foreach (ToolStripItem item in _tools.Items)
            {
                ToolStripControlHost host = item as ToolStripControlHost;
                if (host != null && host.Control != null && host.Control.ContainsFocus) return true;
            }
            return false;
        }

        private static bool HasTextInputFocus(Control root)
        {
            if (root == null) return false;
            if (root.ContainsFocus && (root is TextBoxBase || root is ComboBox || root is UpDownBase)) return true;
            foreach (Control child in root.Controls) if (HasTextInputFocus(child)) return true;
            return false;
        }

        private static bool ShouldDeleteSelection(Keys key, bool textInputFocused) { return key == Keys.Delete && !textInputFocused; }
        internal static bool ShouldDeleteSelectionForTesting(Keys key, bool textInputFocused) { return ShouldDeleteSelection(key, textInputFocused); }

        private void ShowHelp()
        {
            MessageBox.Show(this, "关系图打开后始终可以直接编辑。\n\n操作方法：\n· 画布理论无限，可向任意方向平移和摆放内容\n· 适合窗口会自动显示当前全部内容\n· 有选中节点或分组时点击＋分组：自动创建包住选中内容的外层分组\n· 没有选中节点或分组时点击＋节点或＋分组：在当前屏幕中心创建\n· 单击对象：选择并显示上下游关系\n· 节点可同时属于多个分组，分组也可位于其他分组内形成多层结构\n· 节点和分组的所属关系按当前位置自动匹配，右侧只读显示层级路径\n· 移动外层分组时，内部子分组和节点会一起移动\n· 空白处按住左键拖动：同时框选节点和完整位于框内的分组\n· 框选多个对象时只显示选中项，不自动高亮邻居\n· Ctrl+C / Ctrl+V：复制、粘贴选中内容到当前屏幕中心\n· 撤销或重做只恢复内容，不改变当前缩放比例和画布位置\n· 拖动节点或分组时，可互相对齐并吸附等间距，画布会显示参考提示\n· 按住 Ctrl 拖动节点或分组：关闭所有吸附，自由摆放\n· 未选节点或分组拖向目标：按对象最终相对位置从上、右、下、左自动连线\n· 节点和分组均不显示连线圆圈；选中后拖动内部可移动\n· 选中分组后，鼠标移到边缘或四角会显示缩放光标，拖动即可调整大小\n· 双击线条：直接修改或清空关系名称\n· 双击节点左上角类型：直接编辑节点类型\n· 双击节点中央名称：直接编辑节点名称\n· 双击分组左上角标题：直接编辑分组名称\n· Ctrl/Shift 单击可多选\n· 右键单击清除当前全部选择\n· 按住右键拖动：十字光标平移整个画布\n· 删除键直接删除，不弹出二次确认\n· 鼠标滚轮缩放\n\n导入飞书画板：\n· 文件 → 导出 → 飞书画板（draw.io，可编辑）\n· 在飞书桌面端打开画板，选择导入该文件\n· 导入后节点、分组和连线均为独立可编辑对象", "操作说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAbout() { MessageBox.Show(this, "关系图编辑器 4.3.2\n\n纯原生 Windows 桌面程序\nC# / WinForms / GDI+\n\n不使用浏览器、不启动网页服务、不以 HTML 作为运行底层。\n\n有任何问题或建议，请联系WZC", "关于", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        private void ShowError(string title, Exception error) { MessageBox.Show(this, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    internal sealed class ColorCategoryChoice
    {
        public string Kind { get; private set; }
        public string Text { get; private set; }
        public Color SampleColor { get; private set; }
        public ColorCategoryChoice(string kind, string text, Color sampleColor) { Kind = kind; Text = text; SampleColor = sampleColor; }
        public override string ToString() { return Text; }
    }

    internal sealed class RelationDialog : Form
    {
        private readonly ComboBox _source = new ComboBox(); private readonly ComboBox _target = new ComboBox(); private readonly TextBox _label = new TextBox(); private readonly ComboBox _category = new ComboBox(); private readonly ComboBox _line = new ComboBox(); private readonly GraphDocument _graph;

        public RelationDialog(GraphDocument graph, string currentLineType, bool darkTheme)
        {
            _graph = graph; Text = "新增关系"; Icon = NativeAppIcon.Create(); ShowIcon = true; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ClientSize = new Size(430, 345); Font = new Font("Microsoft YaHei UI", 9f);
            List<EndpointItem> endpoints = new List<EndpointItem>(); endpoints.AddRange(graph.nodes.Select(delegate(GraphNode item) { return new EndpointItem { Type = "node", Id = item.id, Label = "节点 · " + item.label }; })); endpoints.AddRange(graph.groups.Select(delegate(GraphGroup item) { return new EndpointItem { Type = "group", Id = item.id, Label = "分组 · " + item.label }; }));
            AddLabel("起点", 22); SetupCombo(_source, 48); _source.DataSource = new List<EndpointItem>(endpoints);
            AddLabel("终点", 88); SetupCombo(_target, 114); _target.DataSource = new List<EndpointItem>(endpoints); if (_target.Items.Count > 1) _target.SelectedIndex = 1;
            AddLabel("关系名称（可留空）", 154); _label.SetBounds(22, 180, 386, 27); Controls.Add(_label);
            AddLabel("关系类别（线色跟随起点）", 220); SetupCombo(_category, 246); _category.Items.AddRange(graph.settings.relationTypes.Select(delegate(RelationType item) { return (object)item.label; }).ToArray()); _category.SelectedIndex = 0;
            AddLabel("线型", 286); _line.DropDownStyle = ComboBoxStyle.DropDownList; _line.Items.AddRange(new object[] { "曲线", "直线", "折线" }); _line.SelectedIndex = currentLineType == "straight" ? 1 : currentLineType == "polyline" ? 2 : 0; _line.SetBounds(74, 280, 105, 28); Controls.Add(_line);
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel }; cancel.SetBounds(233, 278, 82, 34); Controls.Add(cancel);
            Button ok = new Button { Text = "创建", DialogResult = DialogResult.OK, BackColor = Color.FromArgb(43, 108, 245), ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; ok.FlatAppearance.BorderSize = 0; ok.SetBounds(326, 278, 82, 34); ok.Click += ValidateBeforeClose; Controls.Add(ok); AcceptButton = ok; CancelButton = cancel;
            NativeTheme.ApplyControlTree(this, darkTheme);
            HandleCreated += delegate { NativeTheme.ApplyWindowDarkMode(this, darkTheme); };
        }

        private void AddLabel(string value, int y) { Label label = new Label { Text = value }; label.SetBounds(22, y, 386, 23); Controls.Add(label); }
        private void SetupCombo(ComboBox box, int y) { box.DropDownStyle = ComboBoxStyle.DropDownList; box.SetBounds(22, y, 386, 28); Controls.Add(box); }
        private void ValidateBeforeClose(object sender, EventArgs e) { EndpointItem source = _source.SelectedItem as EndpointItem, target = _target.SelectedItem as EndpointItem; if (source == null || target == null || source.Key == target.Key) { DialogResult = DialogResult.None; MessageBox.Show(this, "请选择两个不同的对象。", "无法创建", MessageBoxButtons.OK, MessageBoxIcon.Information); } }
        public GraphEdge CreateEdge(string id)
        {
            EndpointItem source = (EndpointItem)_source.SelectedItem, target = (EndpointItem)_target.SelectedItem;
            return new GraphEdge { id = id, sourceType = source.Type, source = source.Id, targetType = target.Type, target = target.Id, label = _label.Text.Trim(), category = _graph.settings.relationTypes[Math.Max(0, _category.SelectedIndex)].id, lineType = _line.SelectedIndex == 1 ? "straight" : _line.SelectedIndex == 2 ? "polyline" : "curve", sourceSide = "", targetSide = "" };
        }
    }
}
