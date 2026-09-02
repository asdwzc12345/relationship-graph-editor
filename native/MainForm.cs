using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace RelationshipGraphNative
{
    public sealed class MainForm : Form
    {
        private const int InspectorLeft = 24;
        private const int InspectorRight = 24;
        private readonly GraphCanvas _canvas = new GraphCanvas();
        private readonly Panel _inspector = new Panel();
        private readonly Panel _flowchartPalette = new Panel();
        private readonly ToolStripStatusLabel _statusText = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _countText = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _zoomText = new ToolStripStatusLabel();
        private readonly ToolStripProgressBar _workProgress = new ToolStripProgressBar();
        private readonly ToolStripStatusLabel _cancelWork = new ToolStripStatusLabel("取消");
        private readonly ToolStripButton _undoButton = new ToolStripButton("撤销");
        private readonly ToolStripButton _redoButton = new ToolStripButton("重做");
        private readonly ToolStripComboBox _lineTypeBox = new ToolStripComboBox();
        private readonly ToolStripComboBox _directionBox = new ToolStripComboBox();
        private readonly ToolStripComboBox _depthBox = new ToolStripComboBox();
        private readonly ToolStripComboBox _themeBox = new ToolStripComboBox();
        private readonly ToolStripTextBox _searchBox = new ToolStripTextBox();
        private readonly GraphHistory _undo = new GraphHistory();
        private readonly GraphHistory _redo = new GraphHistory();
        private readonly Timer _inspectorSaveTimer = new Timer();
        private string _pendingInspectorBeforeJson = "";
        private Control _pendingInspectorSource;
        private string _pendingInspectorMessage = "";
        private string _selectionClipboardJson = "";
        private string _currentFile = "";
        private string _autosavePath;
        private AutosaveStore _autosaveStore;
        private RecentFileStore _recentFileStore;
        private readonly object _autosaveSync = new object();
        private Exception _lastAutosaveError;
        private BackgroundWorkQueue _backgroundWork;
        private DebouncedSaveQueue<GraphDocument> _autosaveQueue;
        private DebouncedSaveQueue<RoutingSnapshot> _routingQueue;
        private int _routingRevision;
        private long _activeWorkId;
        private Action<BackgroundWorkCompletedEventArgs> _activeWorkCompletion;
        private bool _servicesDisposed;
        private readonly int _uiThreadId;
        private string _themePreferencePath;
        private string _themeMode = "system";
        private bool _darkTheme;
        private bool _settingUi;
        private bool _isDirty;
        private string _savedDocumentFingerprint = "";
        private readonly bool _autosaveEnabled;
        private MenuStrip _menu;
        private ToolStrip _tools;
        private StatusStrip _status;
        private SplitContainer _split;
        private ToolStripMenuItem _systemThemeItem;
        private ToolStripMenuItem _lightThemeItem;
        private ToolStripMenuItem _darkThemeItem;
        private ToolStripMenuItem _miniMapItem;
        private ToolStripMenuItem _recentFilesItem;
        private ReplaceDialog _replaceDialog;

        private sealed class GraphSearchTarget
        {
            public string Type;
            public string Id;
            public string Label;
        }

        private sealed class RoutingSnapshot
        {
            public int Revision;
            public GraphDocument Document;
        }

        public MainForm() : this(null, true, null) { }

        internal MainForm(GraphDocument initialGraph, bool autosaveEnabled) : this(initialGraph, autosaveEnabled, null) { }

        internal MainForm(GraphDocument initialGraph, bool autosaveEnabled, string recentFileStorePath)
        {
            _uiThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            _autosaveEnabled = autosaveEnabled;
            Text = "关系图编辑器";
            AccessibleName = "关系图编辑器主窗口";
            Icon = NativeAppIcon.Create();
            ShowIcon = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1100, 700);
            Size = new Size(1440, 900);
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(244, 247, 250);

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _autosavePath = Path.Combine(local, "Relationship Studio", "autosave-native.json");
            _autosaveStore = new AutosaveStore(_autosavePath);
            _recentFileStore = new RecentFileStore(String.IsNullOrWhiteSpace(recentFileStorePath) ? Path.Combine(local, "Relationship Studio", "recent-files.txt") : recentFileStorePath);
            _themePreferencePath = Path.Combine(local, "Relationship Studio", "theme.txt");
            _themeMode = LoadThemePreference();
            _backgroundWork = new BackgroundWorkQueue("RelationshipGraph.UserWork");
            _backgroundWork.ProgressChanged += BackgroundWorkProgressChanged;
            _backgroundWork.WorkCompleted += BackgroundWorkCompleted;
            if (_autosaveEnabled)
            {
                _autosaveQueue = new DebouncedSaveQueue<GraphDocument>(TimeSpan.FromMilliseconds(900), SaveAutosaveSnapshot);
                _autosaveQueue.ProgressChanged += AutosaveProgressChanged;
                _autosaveQueue.SaveCompleted += AutosaveCompleted;
            }
            _routingQueue = new DebouncedSaveQueue<RoutingSnapshot>(TimeSpan.FromMilliseconds(350), CalculateRoutingSnapshot);

            MenuStrip menu = BuildMenu(); _menu = menu;
            ToolStrip tools = BuildToolbar(); _tools = tools;
            StatusStrip status = BuildStatus(); _status = status;
            SplitContainer split = new SplitContainer(); _split = split;
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel2;
            split.SplitterWidth = 7;
            split.Panel1.Controls.Add(_canvas);
            split.Panel1.Controls.Add(_flowchartPalette);
            split.Panel2.Controls.Add(_inspector);
            _canvas.Dock = DockStyle.Fill;
            _flowchartPalette.Dock = DockStyle.Left; _flowchartPalette.Width = 220; _flowchartPalette.AutoScroll = true; _flowchartPalette.Padding = new Padding(16, 20, 16, 20); _flowchartPalette.Visible = false;
            _flowchartPalette.AccessibleName = "流程图组件库";
            _inspector.Dock = DockStyle.Fill;
            _inspector.AccessibleName = "属性面板";
            _inspector.AutoScroll = true;
            _inspector.BackColor = Color.White;
            _inspector.Padding = new Padding(InspectorLeft, 24, InspectorRight, 24);

            Controls.Add(split);
            Controls.Add(status);
            Controls.Add(tools);
            Controls.Add(menu);
            MainMenuStrip = menu;

            Action initializeSplitter = delegate
            {
                const int canvasMinimum = 600, inspectorMinimum = 360;
                int available = split.ClientSize.Width - split.SplitterWidth;
                if (available < canvasMinimum + inspectorMinimum) return;
                split.Panel1MinSize = canvasMinimum;
                split.Panel2MinSize = inspectorMinimum;
                int preferredInspector = Math.Max(440, Math.Min(560, (int)Math.Round(split.ClientSize.Width * .25)));
                int inspectorWidth = Math.Max(inspectorMinimum, Math.Min(preferredInspector, available - canvasMinimum));
                split.SplitterDistance = available - inspectorWidth;
            };
            split.SizeChanged += delegate { initializeSplitter(); };
            Shown += delegate { initializeSplitter(); QueueRoutingRefresh(); };

            _canvas.NewLineType = "auto";
            _canvas.FocusDirection = "all";
            _canvas.FocusDepth = 1;
            _inspectorSaveTimer.Interval = 450;
            _inspectorSaveTimer.Tick += delegate { FlushPendingInspectorChange(); };
            _canvas.SelectionChanged += delegate { FlushPendingInspectorChange(); RebuildInspector(); UpdateStatus(); };
            _canvas.GraphCommitted += CanvasGraphCommitted;
            _canvas.ViewChanged += delegate { UpdateStatus(); };
            _canvas.BlankDoubleClicked += delegate(object sender, CanvasPointEventArgs e) { if (!IsFlowchart) AddNodeAt(e.WorldPoint); };
            _canvas.AllowDrop = true;
            _canvas.DragEnter += delegate(object sender, DragEventArgs e) { if (IsFlowchart && e.Data.GetDataPresent("FlowchartShape")) e.Effect = DragDropEffects.Copy; };
            _canvas.DragDrop += delegate(object sender, DragEventArgs e) { string shape = e.Data.GetData("FlowchartShape") as string; if (IsFlowchart && !String.IsNullOrEmpty(shape)) AddNodeAt(_canvas.ClientPointToWorld(_canvas.PointToClient(new Point(e.X, e.Y))), "已从组件库添加流程图形", shape); };
            FormClosing += MainFormClosing;
            FormClosed += delegate
            {
                DisposeServices();
            };
            KeyDown += MainFormKeyDown;

            string startupMessage = "原生关系图已打开";
            GraphDocument first = initialGraph;
            bool recoveredAutosave = false;
            if (first == null)
            {
                first = TryLoadAutosave(out startupMessage);
                recoveredAutosave = first != null;
                if (first == null)
                {
                    first = GraphSerialization.LoadDefault();
                    if (String.IsNullOrEmpty(startupMessage)) startupMessage = "原生关系图已打开";
                }
            }
            LoadDocument(first, startupMessage, true, "", recoveredAutosave, false);
            ApplyTheme();
        }

        private MenuStrip BuildMenu()
        {
            MenuStrip menu = new MenuStrip();
            ToolStripMenuItem file = new ToolStripMenuItem("文件(&F)");
            file.DropDownItems.Add(MenuItem("新建关系图", Keys.Control | Keys.N, NewRelationshipGraph));
            file.DropDownItems.Add(MenuItem("新建流程图", Keys.Control | Keys.Shift | Keys.N, NewFlowchart));
            file.DropDownItems.Add(MenuItem("恢复默认测试用图", Keys.None, RestoreDefault));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("导入 JSON / Draw.io / 只读可视图…", Keys.Control | Keys.O, OpenGraph));
            _recentFilesItem = new ToolStripMenuItem("最近文件");
            _recentFilesItem.DropDownOpening += delegate { RebuildRecentFilesMenu(); };
            file.DropDownItems.Add(_recentFilesItem);
            file.DropDownItems.Add(MenuItem("保存 JSON", Keys.Control | Keys.S, delegate { SaveJson(); }));
            file.DropDownItems.Add(MenuItem("另存为…", Keys.Control | Keys.Shift | Keys.S, delegate { SaveJson(true); }));
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
            edit.DropDownItems.Add(MenuItem("查找", Keys.Control | Keys.F, delegate { FocusSearch(); }));
            edit.DropDownItems.Add(MenuItem("替换…", Keys.Control | Keys.H, ShowReplaceDialog));
            edit.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem copy = MenuItem("复制选中内容", Keys.None, delegate { CopySelected(true); }); copy.ShortcutKeyDisplayString = "Ctrl+C";
            ToolStripMenuItem paste = MenuItem("粘贴", Keys.None, delegate { PasteSelected(true); }); paste.ShortcutKeyDisplayString = "Ctrl+V";
            edit.DropDownItems.Add(copy); edit.DropDownItems.Add(paste);
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(MenuItem("新增分组", Keys.Control | Keys.G, AddGroup));
            edit.DropDownItems.Add(MenuItem("新增节点", Keys.Insert, AddNode));
            edit.DropDownItems.Add(MenuItem("新增关系…", Keys.Control | Keys.L, AddRelation));
            edit.DropDownItems.Add(MenuItem("删除选中项", Keys.Delete, DeleteSelected));

            ToolStripMenuItem view = new ToolStripMenuItem("视图(&V)");
            view.DropDownItems.Add(MenuItem("自动排版", Keys.Control | Keys.Shift | Keys.L, RunAutomaticLayout));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(MenuItem("适合窗口", Keys.Control | Keys.D0, delegate { _canvas.FitToView(); }));
            view.DropDownItems.Add(MenuItem("放大", Keys.Control | Keys.Oemplus, delegate { _canvas.ZoomBy(1.18f); }));
            view.DropDownItems.Add(MenuItem("缩小", Keys.Control | Keys.OemMinus, delegate { _canvas.ZoomBy(.85f); }));
            view.DropDownItems.Add(new ToolStripSeparator());
            _miniMapItem = new ToolStripMenuItem("显示小地图") { Checked = true, CheckOnClick = true, ShortcutKeys = Keys.Control | Keys.M };
            _miniMapItem.Click += delegate { _canvas.ShowMiniMap = _miniMapItem.Checked; };
            view.DropDownItems.Add(_miniMapItem);
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
            tools.Padding = new Padding(10, 7, 10, 7);
            tools.AutoSize = true;
            tools.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
            _undoButton.Click += delegate { Undo(); }; _redoButton.Click += delegate { Redo(); };

            ToolStripButton addGroup = new ToolStripButton("＋分组"); addGroup.Click += delegate { AddGroup(); };
            ToolStripButton addNode = new ToolStripButton("＋节点"); addNode.Click += delegate { if (!IsFlowchart) AddNode(); else _statusText.Text = "请从左侧组件库选择或拖入流程图形"; };
            ToolStripButton autoLayout = new ToolStripButton("自动排版"); autoLayout.Click += delegate { RunAutomaticLayout(); };
            ToolStripButton fit = new ToolStripButton("适合窗口"); fit.Click += delegate { _canvas.FitToView(); };
            ToolStripButton zoomOut = new ToolStripButton("－"); zoomOut.Click += delegate { _canvas.ZoomBy(.85f); };
            ToolStripButton zoomIn = new ToolStripButton("＋"); zoomIn.Click += delegate { _canvas.ZoomBy(1.18f); };
            addGroup.AccessibleName = "新增分组"; addNode.AccessibleName = "新增节点";
            autoLayout.AccessibleName = "自动排版"; autoLayout.ToolTipText = "分层排列并自动避让连线与标签（Ctrl+Shift+L）";
            fit.AccessibleName = "适合窗口"; zoomOut.AccessibleName = "缩小画布"; zoomIn.AccessibleName = "放大画布";
            addGroup.ToolTipText = "在视野中心新增分组"; addNode.ToolTipText = "在视野中心新增节点";
            fit.ToolTipText = "显示全部内容（Ctrl+0）"; zoomOut.ToolTipText = "缩小"; zoomIn.ToolTipText = "放大";

            _lineTypeBox.AutoSize = false; _lineTypeBox.DropDownStyle = ComboBoxStyle.DropDownList; _lineTypeBox.Width = 100; _lineTypeBox.DropDownWidth = 120;
            _lineTypeBox.AccessibleName = "新关系线型";
            _lineTypeBox.Items.AddRange(new object[] { "自动避障", "曲线", "直线", "折线" }); _lineTypeBox.SelectedIndex = 0;
            _lineTypeBox.SelectedIndexChanged += delegate { _canvas.NewLineType = LineTypeAt(_lineTypeBox.SelectedIndex); };
            _directionBox.AutoSize = false; _directionBox.DropDownStyle = ComboBoxStyle.DropDownList; _directionBox.Width = 112; _directionBox.DropDownWidth = 132;
            _directionBox.AccessibleName = "关系高亮方向";
            _directionBox.Items.AddRange(new object[] { "上下游", "仅上游", "仅下游" }); _directionBox.SelectedIndex = 0;
            _directionBox.SelectedIndexChanged += delegate { _canvas.FocusDirection = _directionBox.SelectedIndex == 1 ? "upstream" : _directionBox.SelectedIndex == 2 ? "downstream" : "all"; _canvas.Invalidate(); };
            _depthBox.AutoSize = false; _depthBox.DropDownStyle = ComboBoxStyle.DropDownList; _depthBox.Width = 72; _depthBox.DropDownWidth = 86;
            _depthBox.AccessibleName = "关系高亮层数";
            _depthBox.Items.AddRange(new object[] { "1层", "2层", "3层" }); _depthBox.SelectedIndex = 0;
            _depthBox.SelectedIndexChanged += delegate { _canvas.FocusDepth = _depthBox.SelectedIndex + 1; _canvas.Invalidate(); };
            _themeBox.AutoSize = false; _themeBox.DropDownStyle = ComboBoxStyle.DropDownList; _themeBox.Width = 128; _themeBox.DropDownWidth = 148;
            _themeBox.AccessibleName = "界面主题";
            _themeBox.Items.AddRange(new object[] { "跟随系统", "浅色", "深色" }); _themeBox.SelectedIndex = ThemeIndex(_themeMode);
            _themeBox.SelectedIndexChanged += delegate { if (!_settingUi) ChangeTheme(ThemeAt(_themeBox.SelectedIndex)); };
            _searchBox.AutoSize = false; _searchBox.Width = 220; _searchBox.ToolTipText = "输入节点、分组或关系文字，按回车查找下一个";
            _searchBox.AccessibleName = "查找关系图对象";
            _searchBox.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { FindEntity(); e.SuppressKeyPress = true; } };

            tools.Items.Add(_undoButton); tools.Items.Add(_redoButton); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(addGroup); tools.Items.Add(addNode); tools.Items.Add(autoLayout); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel("新关系线型")); tools.Items.Add(_lineTypeBox); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel("高亮")); tools.Items.Add(_directionBox); tools.Items.Add(_depthBox); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(fit); tools.Items.Add(zoomOut); tools.Items.Add(zoomIn); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel("主题")); tools.Items.Add(_themeBox); tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel("查找")); tools.Items.Add(_searchBox);
            foreach (ToolStripItem item in tools.Items)
            {
                if (item is ToolStripButton) item.Padding = new Padding(5, 2, 5, 2);
                if (item is ToolStripLabel) item.Margin = new Padding(5, 1, 3, 2);
            }
            return tools;
        }

        private StatusStrip BuildStatus()
        {
            StatusStrip status = new StatusStrip();
            _statusText.Spring = true; _statusText.TextAlign = ContentAlignment.MiddleLeft;
            _workProgress.Minimum = 0; _workProgress.Maximum = 100; _workProgress.Width = 150; _workProgress.Visible = false;
            _workProgress.AccessibleName = "后台操作进度"; _workProgress.ToolTipText = "当前后台操作完成百分比";
            _cancelWork.IsLink = true; _cancelWork.Visible = false; _cancelWork.ToolTipText = "取消当前后台操作（Esc）"; _cancelWork.AccessibleName = "取消当前后台操作，快捷键 Escape";
            _cancelWork.Click += delegate
            {
                RequestBackgroundCancellation();
            };
            status.Items.Add(_statusText); status.Items.Add(_workProgress); status.Items.Add(_cancelWork);
            status.Items.Add(_countText); status.Items.Add(new ToolStripStatusLabel("  ")); status.Items.Add(_zoomText);
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

        private bool SaveThemePreference()
        {
            if (!_autosaveEnabled) return true;
            try
            {
                NativePersistence.WriteAllTextAtomic(_themePreferencePath, _themeMode, new UTF8Encoding(false), false);
                return true;
            }
            catch (Exception error)
            {
                ReportStatusSafely("主题偏好保存失败：" + PersistenceErrorMessage(error));
                return false;
            }
        }

        private void ChangeTheme(string mode)
        {
            FlushPendingInspectorChange();
            _themeMode = NormalizeThemeMode(mode);
            bool preferenceSaved = SaveThemePreference(); ApplyTheme();
            if (preferenceSaved) _statusText.Text = _themeMode == "system" ? "界面主题已设为跟随系统" : _themeMode == "dark" ? "界面已切换为深色主题" : "界面已切换为浅色主题";
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
            if (_flowchartPalette != null) { _flowchartPalette.BackColor = dark ? NativeTheme.DarkSurface : Color.White; NativeTheme.ApplyControlTree(_flowchartPalette, dark); }
            if (_replaceDialog != null && !_replaceDialog.IsDisposed)
            {
                NativeTheme.ApplyControlTree(_replaceDialog, dark);
                NativeTheme.ApplyWindowDarkMode(_replaceDialog, dark);
            }
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeServices();
            base.Dispose(disposing);
        }

        private void DisposeServices()
        {
            if (_servicesDisposed) return; _servicesDisposed = true;
            _inspectorSaveTimer.Dispose();
            if (_autosaveQueue != null) _autosaveQueue.Dispose();
            if (_routingQueue != null) _routingQueue.Dispose();
            if (_backgroundWork != null) _backgroundWork.Dispose();
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
            if (!String.IsNullOrEmpty(e.BeforeJson)) _undo.PushSerialized(e.BeforeJson);
            else if (e.Before != null) _undo.Push(e.Before);
            _redo.Clear(); MarkDirty(); bool autosaved = SaveAutosave(); RebuildInspector(); UpdateStatus();
            QueueRoutingRefresh();
            if (autosaved) _statusText.Text = e.Message;
        }

        private void CommitChange(string beforeJson, string message)
        {
            _canvas.Document.meta.updatedAt = DateTime.UtcNow.ToString("o");
            _canvas.RefreshDocument(); _undo.PushSerialized(beforeJson); _redo.Clear(); MarkDirty(); bool autosaved = SaveAutosave();
            RebuildInspector(); UpdateStatus(); QueueRoutingRefresh(); if (autosaved) _statusText.Text = message;
        }

        private void ApplyInspectorChange(Control source, Action apply, string message)
        {
            if (!_canvas.EditMode || _settingUi || source == null || apply == null) return;
            if (_pendingInspectorBeforeJson.Length > 0 && _pendingInspectorSource != source) FlushPendingInspectorChange();
            if (_pendingInspectorBeforeJson.Length == 0)
            {
                _pendingInspectorBeforeJson = GraphSerialization.Serialize(_canvas.Document, false);
                _pendingInspectorSource = source;
            }
            _canvas.ClearAutomaticRouting();
            apply();
            _canvas.Document.meta.updatedAt = DateTime.UtcNow.ToString("o");
            MarkDirty(); UpdateTitle();
            _pendingInspectorMessage = message;
            _canvas.Invalidate(); UpdateStatus();
            _statusText.Text = "已应用，正在后台更新恢复副本…";
            _inspectorSaveTimer.Stop(); _inspectorSaveTimer.Start();
        }

        private void FlushPendingInspectorChange()
        {
            _inspectorSaveTimer.Stop();
            if (_pendingInspectorBeforeJson.Length == 0) return;
            string beforeJson = _pendingInspectorBeforeJson; string message = _pendingInspectorMessage;
            _pendingInspectorBeforeJson = ""; _pendingInspectorSource = null; _pendingInspectorMessage = "";
            _undo.PushSerialized(beforeJson); _redo.Clear(); bool autosaved = SaveAutosave(); UpdateStatus();
            QueueRoutingRefresh();
            if (autosaved) _statusText.Text = String.IsNullOrEmpty(message) ? "属性已应用，恢复副本将在后台更新" : message;
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

        private void LoadDocument(GraphDocument graph, string message, bool clearHistory, string currentFile, bool dirty, bool saveAutosave)
        {
            FlushPendingInspectorChange();
            if (clearHistory) { _undo.Clear(); _redo.Clear(); }
            _canvas.Document = graph; _canvas.EditMode = true; _currentFile = currentFile ?? ""; _isDirty = dirty; RebuildFlowchartPalette();
            _savedDocumentFingerprint = dirty ? "" : DocumentFingerprint(_canvas.Document);
            bool autosaved = !saveAutosave || (dirty ? SaveAutosave() : ClearAutosaveSafely());
            ApplyTheme(); RebuildInspector(); UpdateStatus(); UpdateTitle();
            QueueRoutingRefresh();
            if (autosaved) _statusText.Text = message;
        }

        private GraphDocument TryLoadAutosave(out string message)
        {
            message = "";
            if (!_autosaveEnabled) return null;

            IList<AutosaveRecoveryCandidate> candidates;
            try { candidates = _autosaveStore.GetRecoveryCandidates(); }
            catch (Exception error)
            {
                message = "无法检查自动恢复文件：" + PersistenceErrorMessage(error) + "；已打开默认图";
                return null;
            }

            Exception lastError = null;
            foreach (AutosaveRecoveryCandidate candidate in candidates)
            {
                try
                {
                    GraphDocument graph = GraphSerialization.LoadFile(candidate.FileName);
                    message = String.Equals(candidate.FileName, _autosaveStore.PrimaryPath, StringComparison.OrdinalIgnoreCase)
                        ? "已恢复上次自动保存的关系图"
                        : "主自动恢复文件不可用，已从" + candidate.Description + "恢复";
                    return graph;
                }
                catch (Exception error) { lastError = error; }
            }

            if (lastError != null)
                message = "自动恢复文件均无法读取：" + PersistenceErrorMessage(lastError) + "；已打开默认图";
            return null;
        }

        private bool SaveAutosave()
        {
            if (!_autosaveEnabled) return true;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)delegate { SaveAutosave(); }); return true; }
                catch (Exception error) { ReportStatusSafely("自动保存无法调度：" + PersistenceErrorMessage(error)); return false; }
            }

            try
            {
                if (_canvas.Document == null) return true;
                GraphDocument snapshot = GraphSerialization.CreateImmutableSnapshot(_canvas.Document);
                lock (_autosaveSync) _lastAutosaveError = null;
                _autosaveQueue.QueueSave(snapshot);
                return true;
            }
            catch (Exception error)
            {
                lock (_autosaveSync) _lastAutosaveError = error;
                ReportStatusSafely("自动保存失败：" + PersistenceErrorMessage(error));
                return false;
            }
        }

        private void NewRelationshipGraph()
        {
            if (!EnsureCurrentDocumentCanBeReplaced()) return;
            GraphDocument graph = GraphSerialization.CreateBlank("未命名关系图"); graph.meta.diagramType = "relationship";
            LoadDocument(graph, "已新建空白关系图", true, "", false, true);
        }

        private void NewFlowchart()
        {
            if (!EnsureCurrentDocumentCanBeReplaced()) return;
            GraphDocument graph = GraphSerialization.CreateBlank("未命名流程图"); graph.meta.diagramType = "flowchart";
            LoadDocument(graph, "已新建空白流程图", true, "", false, true);
        }

        private bool IsFlowchart { get { return _canvas.Document != null && _canvas.Document.meta != null && _canvas.Document.meta.diagramType == "flowchart"; } }

        private void RestoreDefault()
        {
            if (!EnsureCurrentDocumentCanBeReplaced()) return;
            LoadDocument(GraphSerialization.LoadDefault(), "已恢复《测试用图》", true, "", true, true);
        }

        private void OpenGraph()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入关系图"; dialog.Filter = "关系图文件 (*.json;*.drawio;*.xml;*.html;*.htm)|*.json;*.drawio;*.xml;*.html;*.htm|Draw.io 图 (*.drawio;*.xml)|*.drawio;*.xml|JSON (*.json)|*.json|只读可视图 (*.html;*.htm)|*.html;*.htm|所有文件 (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                BeginOpenGraph(Path.GetFullPath(dialog.FileName));
            }
        }

        private void BeginOpenGraph(string selectedFile)
        {
            if (String.IsNullOrWhiteSpace(selectedFile)) return;
            string extension = Path.GetExtension(selectedFile);
            bool knownNonDrawio = extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
            if (knownNonDrawio) { StartGraphImport(selectedFile, null); return; }
            IList<DrawioPageInfo> pages = null;
            bool isDrawio = false;
            StartBackgroundWork("正在读取 Draw.io 页面", delegate(BackgroundWorkContext context)
            {
                context.ReportProgress(10, "正在读取 Draw.io 页面目录…");
                isDrawio = GraphSerialization.TryGetDrawioPageInfos(selectedFile, out pages);
                context.ThrowIfCancellationRequested();
                context.ReportProgress(100, isDrawio ? "Draw.io 页面目录读取完成" : "文件类型识别完成");
            }, delegate(BackgroundWorkCompletedEventArgs completed)
            {
                if (completed.Cancelled) { _statusText.Text = "Draw.io 导入已取消"; return; }
                if (completed.Error != null) { ShowError("无法读取 Draw.io 页面", completed.Error); return; }
                int? pageIndex = null;
                if (isDrawio && pages != null && pages.Count > 1)
                {
                    using (DrawioPageDialog pageDialog = new DrawioPageDialog(pages, _darkTheme))
                    {
                        if (pageDialog.ShowDialog(this) != DialogResult.OK) { _statusText.Text = "Draw.io 导入已取消"; return; }
                        pageIndex = pageDialog.SelectedPageIndex;
                    }
                }
                StartGraphImport(selectedFile, pageIndex);
            });
        }

        private void StartGraphImport(string selectedFile, int? drawioPageIndex)
        {
            if (!EnsureCurrentDocumentCanBeReplaced()) return;
            GraphDocument imported = null; string importNotice = "";
            StartBackgroundWork("正在导入 " + Path.GetFileName(selectedFile), delegate(BackgroundWorkContext context)
            {
                context.ReportProgress(5, "正在读取文件…");
                context.ThrowIfCancellationRequested();
                imported = GraphSerialization.LoadFile(selectedFile, drawioPageIndex, out importNotice);
                context.ThrowIfCancellationRequested();
                context.ReportProgress(100, "文件解析完成，正在更新画布…");
            }, delegate(BackgroundWorkCompletedEventArgs completed)
            {
                if (completed.Cancelled) { _statusText.Text = "导入已取消"; return; }
                if (completed.Error != null) { ShowError("无法导入该文件", completed.Error); return; }
                string currentFile = Path.GetExtension(selectedFile).Equals(".json", StringComparison.OrdinalIgnoreCase) ? selectedFile : "";
                string status = "已导入 " + Path.GetFileName(selectedFile);
                if (!String.IsNullOrWhiteSpace(importNotice)) status += "；" + importNotice;
                LoadDocument(imported, status, true, currentFile, String.IsNullOrEmpty(currentFile), true);
                RememberRecentFile(selectedFile);
            });
        }

        private void SaveAutosaveSnapshot(GraphDocument snapshot, BackgroundWorkContext context)
        {
            if (snapshot == null) return;
            context.ReportProgress(10, "正在准备自动恢复副本…");
            string warning = "";
            try
            {
                string json = GraphSerialization.Serialize(snapshot, false);
                context.ThrowIfCancellationRequested();
                lock (_autosaveSync)
                {
                    warning = _autosaveStore.Save(json);
                    _lastAutosaveError = null;
                }
            }
            catch (Exception error)
            {
                lock (_autosaveSync) _lastAutosaveError = error;
                throw;
            }
            context.ThrowIfCancellationRequested();
            context.ReportProgress(100, String.IsNullOrEmpty(warning) ? "自动恢复副本已更新" : "自动恢复副本已更新，但历史版本创建失败：" + warning);
        }

        private void AutosaveProgressChanged(object sender, BackgroundWorkProgressEventArgs e)
        {
            if (e.Percentage >= 100 && !String.IsNullOrWhiteSpace(e.Message) && e.Message.IndexOf("失败", StringComparison.Ordinal) >= 0)
                ReportStatusSafely(e.Message);
        }

        private void AutosaveCompleted(object sender, BackgroundWorkCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                lock (_autosaveSync) _lastAutosaveError = e.Error;
                ReportStatusSafely("自动恢复副本保存失败：" + PersistenceErrorMessage(e.Error));
            }
        }

        private void QueueRoutingRefresh()
        {
            if (_routingQueue == null || _canvas.Document == null || IsDisposed) return;
            _routingRevision++;
            _routingQueue.QueueSave(new RoutingSnapshot { Revision = _routingRevision, Document = GraphSerialization.CreateImmutableSnapshot(_canvas.Document) });
        }

        private void CalculateRoutingSnapshot(RoutingSnapshot snapshot, BackgroundWorkContext context)
        {
            if (snapshot == null || snapshot.Document == null) return;
            context.ThrowIfCancellationRequested();
            GraphLayoutOptions options = GraphLayoutOptions.ForDocument(snapshot.Document);
            options.CancellationRequested = delegate { return context.IsCancellationRequested; };
            GraphLayoutResult result = GraphLayout.CalculateRoutes(snapshot.Document, options);
            context.ThrowIfCancellationRequested();
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)delegate
                {
                    if (!IsDisposed && snapshot.Revision == _routingRevision) _canvas.SetAutomaticRouting(result);
                });
            }
            catch { }
        }

        private bool RememberRecentFile(string fileName)
        {
            if (_recentFileStore == null || String.IsNullOrWhiteSpace(fileName)) return false;
            if (!_recentFileStore.Add(fileName))
            {
                ReportStatusSafely("最近文件记录失败：" + PersistenceErrorMessage(_recentFileStore.LastError));
                return false;
            }
            RebuildRecentFilesMenu();
            return true;
        }

        private void RebuildRecentFilesMenu()
        {
            if (_recentFilesItem == null || _recentFileStore == null) return;
            _recentFilesItem.DropDownItems.Clear();
            IList<string> files = _recentFileStore.GetFiles();
            Exception recentReadError = _recentFileStore.LastError;
            for (int index = 0; index < files.Count; index++)
            {
                string path = files[index];
                string folder = Path.GetDirectoryName(path) ?? "";
                string text = (index < 9 ? "&" + (index + 1) : (index + 1).ToString()) + "  " + EscapeMenuText(Path.GetFileName(path));
                if (folder.Length > 0) text += "  —  " + EscapeMenuText(folder);
                ToolStripMenuItem item = new ToolStripMenuItem(text) { ToolTipText = path, AccessibleName = "打开最近文件 " + Path.GetFileName(path) };
                string selectedPath = path;
                item.Click += delegate { OpenRecentFile(selectedPath); };
                _recentFilesItem.DropDownItems.Add(item);
            }
            if (files.Count == 0 && recentReadError == null) _recentFilesItem.DropDownItems.Add(new ToolStripMenuItem("没有最近文件") { Enabled = false });
            else
            {
                if (files.Count > 0)
                {
                    _recentFilesItem.DropDownItems.Add(new ToolStripSeparator());
                    ToolStripMenuItem clear = new ToolStripMenuItem("清空最近文件");
                    clear.Click += delegate
                    {
                        if (!_recentFileStore.Clear() && _recentFileStore.LastError != null) ReportStatusSafely("最近文件清空失败：" + PersistenceErrorMessage(_recentFileStore.LastError));
                        RebuildRecentFilesMenu();
                    };
                    _recentFilesItem.DropDownItems.Add(clear);
                }
            }
            if (recentReadError != null)
            {
                _recentFilesItem.DropDownItems.Add(new ToolStripMenuItem("最近文件读取失败") { Enabled = false, ToolTipText = PersistenceErrorMessage(recentReadError) });
                ReportStatusSafely("最近文件读取失败：" + PersistenceErrorMessage(recentReadError));
            }
        }

        private static string EscapeMenuText(string value) { return (value ?? "").Replace("&", "&&"); }

        private void OpenRecentFile(string fileName)
        {
            if (String.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
            {
                if (_recentFileStore != null) _recentFileStore.Remove(fileName);
                RebuildRecentFilesMenu(); _statusText.Text = "最近文件已不存在，已从列表移除"; return;
            }
            BeginOpenGraph(Path.GetFullPath(fileName));
        }

        private bool SaveJson()
        {
            return SaveJson(false);
        }

        private bool SaveJson(bool forceSaveAs)
        {
            FinishPendingCanvasWork();
            string file = forceSaveAs ? "" : _currentFile;
            if (String.IsNullOrEmpty(file))
            {
                using (SaveFileDialog dialog = SaveDialog("JSON 关系图 (*.json)|*.json", ".json"))
                {
                    if (forceSaveAs && !String.IsNullOrEmpty(_currentFile))
                    {
                        dialog.InitialDirectory = Path.GetDirectoryName(_currentFile);
                        dialog.FileName = Path.GetFileName(_currentFile);
                    }
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false; file = dialog.FileName;
                }
            }
            return SaveJsonToPath(file);
        }

        private bool SaveJsonToPath(string file)
        {
            try
            {
                NativeExport.SaveJson(_canvas.Document, file);
                _currentFile = file; _savedDocumentFingerprint = DocumentFingerprint(_canvas.Document); SetDirty(false); UpdateTitle();
                bool recentRecorded = RememberRecentFile(file);
                bool recoveryCleared = ClearAutosaveSafely();
                if (recoveryCleared) _statusText.Text = recentRecorded ? "JSON 已保存" : "JSON 已保存，但最近文件记录失败";
                return true;
            }
            catch (Exception error) { ShowError("无法保存文件", error); return false; }
        }

        internal bool SaveJsonToFileForTesting(string file) { FinishPendingCanvasWork(); return SaveJsonToPath(file); }
        internal string CurrentFileForTesting { get { return _currentFile; } }

        private bool EnsureCurrentDocumentCanBeReplaced()
        {
            bool discardChanges;
            if (!EnsureCurrentDocumentCanBeReplaced(out discardChanges)) return false;
            return !discardChanges || ClearAutosaveSafely();
        }

        private bool EnsureCurrentDocumentCanBeReplaced(out bool discardChanges)
        {
            discardChanges = false;
            FinishPendingCanvasWork();
            if (!_autosaveEnabled || !_isDirty || _canvas.Document == null) return true;

            string title = _canvas.Document.meta == null || String.IsNullOrWhiteSpace(_canvas.Document.meta.title)
                ? "当前关系图"
                : "“" + _canvas.Document.meta.title + "”";
            DialogResult choice = MessageBox.Show(
                this,
                title + "包含尚未保存到 JSON 文件的更改。\r\n\r\n是否现在保存？",
                "保存更改",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1);
            if (choice == DialogResult.Yes) return SaveJson();
            if (choice == DialogResult.No) { discardChanges = true; return true; }
            return false;
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_activeWorkId != 0 && _backgroundWork != null)
            {
                _backgroundWork.Cancel(_activeWorkId);
                e.Cancel = true;
                _statusText.Text = "正在取消后台操作；完成后可安全关闭程序";
                return;
            }
            bool discardChanges;
            if (!EnsureCurrentDocumentCanBeReplaced(out discardChanges)) { e.Cancel = true; return; }
            if (discardChanges)
            {
                if (!ClearAutosaveSafely())
                {
                    e.Cancel = true;
                    MessageBox.Show(this, "无法清除自动恢复文件，已取消关闭。", "无法关闭", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }
            if (_isDirty)
            {
                SaveAutosave();
                bool flushed = _autosaveQueue == null || _autosaveQueue.Flush(TimeSpan.FromSeconds(5));
                Exception backgroundSaveError; lock (_autosaveSync) backgroundSaveError = _lastAutosaveError;
                if (AutosaveNeedsSynchronousFallback(flushed, backgroundSaveError))
                {
                    try
                    {
                        string json = GraphSerialization.Serialize(GraphSerialization.CreateImmutableSnapshot(_canvas.Document), false);
                        lock (_autosaveSync) { _autosaveStore.Save(json); _lastAutosaveError = null; }
                    }
                    catch (Exception error)
                    {
                        e.Cancel = true;
                        ShowError("关闭前自动恢复保存失败，已取消关闭", error);
                    }
                }
            }
        }

        private void MarkDirty() { SetDirty(true); }

        private void RefreshDirtyFromSavePoint()
        {
            SetDirty(String.IsNullOrEmpty(_savedDocumentFingerprint) || !String.Equals(_savedDocumentFingerprint, DocumentFingerprint(_canvas.Document), StringComparison.Ordinal));
        }

        private static string DocumentFingerprint(GraphDocument graph)
        {
            if (graph == null) return "";
            byte[] json = Encoding.UTF8.GetBytes(GraphSerialization.Serialize(graph, false));
            using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(json));
        }

        private void FinishPendingCanvasWork()
        {
            _canvas.CancelActiveGesture();
            _canvas.CommitPendingEdit();
            FlushPendingInspectorChange();
        }

        private void SetDirty(bool dirty)
        {
            if (_isDirty == dirty) return;
            _isDirty = dirty;
            UpdateTitle();
        }

        private void ReportStatusSafely(string message)
        {
            if (IsDisposed || Disposing) return;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != _uiThreadId)
            {
                if (!IsHandleCreated) return;
                try { BeginInvoke((Action<string>)ReportStatusSafely, message); }
                catch { }
                return;
            }
            _statusText.Text = message;
        }

        private bool ClearAutosaveSafely()
        {
            if (!_autosaveEnabled) return true;
            try
            {
                if (_autosaveQueue != null)
                {
                    _autosaveQueue.CancelAll();
                    _autosaveQueue.Flush(TimeSpan.FromSeconds(3));
                }
                lock (_autosaveSync) _autosaveStore.ClearAll();
                lock (_autosaveSync) _lastAutosaveError = null;
                return true;
            }
            catch (Exception error) { ReportStatusSafely("自动恢复数据清理失败：" + PersistenceErrorMessage(error)); return false; }
        }

        private bool StartBackgroundWork(string name, Action<BackgroundWorkContext> action, Action<BackgroundWorkCompletedEventArgs> completed)
        {
            if (_backgroundWork == null || action == null) return false;
            if (_activeWorkId != 0)
            {
                _statusText.Text = "已有后台操作正在进行，可先取消或等待完成";
                return false;
            }
            _activeWorkCompletion = completed;
            try
            {
                _activeWorkId = _backgroundWork.Enqueue(name, action);
                _workProgress.Value = 0; _workProgress.Visible = true; _cancelWork.Visible = true;
                _statusText.Text = name + "…（按 Esc 取消）";
                SetEditingEnabled(false);
                return true;
            }
            catch (Exception error)
            {
                _activeWorkId = 0; _activeWorkCompletion = null;
                ShowError("无法启动后台操作", error); return false;
            }
        }

        private static bool AutosaveNeedsSynchronousFallback(bool flushed, Exception saveError) { return !flushed || saveError != null; }
        internal static bool AutosaveNeedsSynchronousFallbackForTesting(bool flushed, Exception saveError) { return AutosaveNeedsSynchronousFallback(flushed, saveError); }

        private void BackgroundWorkProgressChanged(object sender, BackgroundWorkProgressEventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke((Action<BackgroundWorkProgressEventArgs>)ApplyBackgroundProgress, e); }
            catch { }
        }

        private void ApplyBackgroundProgress(BackgroundWorkProgressEventArgs e)
        {
            if (e == null || e.WorkId != _activeWorkId || IsDisposed) return;
            _workProgress.Value = Math.Max(_workProgress.Minimum, Math.Min(_workProgress.Maximum, e.Percentage));
            if (!String.IsNullOrWhiteSpace(e.Message)) _statusText.Text = e.Message + "（按 Esc 取消）";
        }

        private void BackgroundWorkCompleted(object sender, BackgroundWorkCompletedEventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke((Action<BackgroundWorkCompletedEventArgs>)FinishBackgroundWork, e); }
            catch { }
        }

        private void FinishBackgroundWork(BackgroundWorkCompletedEventArgs e)
        {
            if (e == null || e.WorkId != _activeWorkId || IsDisposed) return;
            Action<BackgroundWorkCompletedEventArgs> callback = _activeWorkCompletion;
            _activeWorkCompletion = null; _activeWorkId = 0;
            _workProgress.Visible = false; _cancelWork.Visible = false; _workProgress.Value = 0;
            SetEditingEnabled(true);
            if (callback != null) callback(e);
            else if (e.Cancelled) _statusText.Text = e.Name + "已取消";
            else if (e.Error != null) ShowError(e.Name + "失败", e.Error);
        }

        private void SetEditingEnabled(bool enabled)
        {
            if (_menu != null) _menu.Enabled = enabled;
            if (_tools != null) _tools.Enabled = enabled;
            _canvas.Enabled = enabled;
            _inspector.Enabled = enabled;
            _cancelWork.Enabled = !enabled;
        }

        private bool RequestBackgroundCancellation()
        {
            if (_activeWorkId == 0 || _backgroundWork == null || !_backgroundWork.Cancel(_activeWorkId)) return false;
            _statusText.Text = "正在取消后台操作…"; return true;
        }

        private static string PersistenceErrorMessage(Exception error)
        {
            string message = error == null ? "未知错误" : error.Message;
            if (String.IsNullOrWhiteSpace(message)) message = error == null ? "未知错误" : error.GetType().Name;
            message = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return message.Length <= 180 ? message : message.Substring(0, 177) + "...";
        }

        private void ExportReadonly() { ExportWithDialog("只读可视图 (*.html)|*.html", ".html", delegate(GraphDocument graph, bool dark, string file) { NativeExport.SaveReadonlyHtml(graph, file); }, "只读可视图已导出，可直接分享或重新导入"); }
        private void ExportFeishuBoard() { ExportWithDialog("飞书画板文件 (*.drawio)|*.drawio", ".drawio", delegate(GraphDocument graph, bool dark, string file) { NativeExport.SaveDrawio(graph, file); }, "飞书画板文件已导出；在飞书桌面端画板中选择导入即可编辑每个节点"); }
        private void ExportSvg() { ExportWithDialog("SVG 矢量图 (*.svg)|*.svg", ".svg", delegate(GraphDocument graph, bool dark, string file) { NativeExport.SaveSvg(graph, file); }, "SVG 已导出"); }
        private void ExportPng() { ExportWithDialog("PNG 图片 (*.png)|*.png", ".png", delegate(GraphDocument graph, bool dark, string file) { NativeExport.SavePng(graph, dark, file); }, "PNG 已导出"); }
        private void ExportPdf() { ExportWithDialog("PDF 文档 (*.pdf)|*.pdf", ".pdf", delegate(GraphDocument graph, bool dark, string file) { NativeExport.SavePdf(graph, file); }, "PDF 已导出"); }

        private void ExportWithDialog(string filter, string extension, Action<GraphDocument, bool, string> exporter, string success)
        {
            FinishPendingCanvasWork();
            using (SaveFileDialog dialog = SaveDialog(filter, extension))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string targetFile = Path.GetFullPath(dialog.FileName);
                GraphDocument snapshot = GraphSerialization.CreateImmutableSnapshot(_canvas.Document);
                bool darkTheme = _darkTheme;
                bool exportPublished = false;
                StartBackgroundWork("正在导出 " + Path.GetFileName(targetFile), delegate(BackgroundWorkContext context)
                {
                    string folder = Path.GetDirectoryName(targetFile);
                    string temporary = Path.Combine(folder, "." + Path.GetFileName(targetFile) + "." + Guid.NewGuid().ToString("N") + ".export-work");
                    try
                    {
                        context.ReportProgress(10, "正在生成导出内容…");
                        context.ThrowIfCancellationRequested();
                        exporter(snapshot, darkTheme, temporary);
                        context.ThrowIfCancellationRequested();
                        context.ReportProgress(78, "正在安全写入目标文件…");
                        PublishPreparedExport(temporary, targetFile, context);
                        exportPublished = true;
                        context.ReportProgress(100, "导出完成");
                    }
                    finally
                    {
                        try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                    }
                }, delegate(BackgroundWorkCompletedEventArgs completed)
                {
                    if (completed.Cancelled)
                    {
                        _statusText.Text = exportPublished ? "导出已在取消请求生效前完成，目标文件已更新" : "导出已取消，原目标文件保持不变";
                        return;
                    }
                    if (completed.Error != null) { ShowError("导出失败", completed.Error); return; }
                    _statusText.Text = success;
                });
            }
        }

        private static void PublishPreparedExport(string sourceFile, string targetFile, BackgroundWorkContext context)
        {
            FileInfo info = new FileInfo(sourceFile);
            long total = Math.Max(1L, info.Length);
            NativePersistence.WriteStreamAtomic(targetFile, false, delegate(Stream destination)
            {
                using (FileStream source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[131072]; long copied = 0;
                    while (true)
                    {
                        context.ThrowIfCancellationRequested();
                        int count = source.Read(buffer, 0, buffer.Length); if (count <= 0) break;
                        destination.Write(buffer, 0, count); copied += count;
                        context.ReportProgress(78 + (int)Math.Min(20, copied * 20L / total), "正在安全写入目标文件…");
                    }
                }
            }, delegate { context.ThrowIfCancellationRequested(); });
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

        private static bool PathsEqual(string first, string second)
        {
            if (String.IsNullOrWhiteSpace(first) || String.IsNullOrWhiteSpace(second)) return false;
            return String.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }

        private void Undo()
        {
            FinishPendingCanvasWork();
            if (_undo.Count == 0) return; _redo.Push(_canvas.Document); _canvas.RestoreDocumentPreservingView(_undo.Pop());
            RefreshDirtyFromSavePoint(); bool autosaved = _isDirty ? SaveAutosave() : ClearAutosaveSafely(); ApplyTheme(); RebuildInspector(); UpdateStatus(); QueueRoutingRefresh(); if (autosaved) _statusText.Text = "已撤销"; UpdateTitle();
        }

        private void Redo()
        {
            FinishPendingCanvasWork();
            if (_redo.Count == 0) return; _undo.Push(_canvas.Document); _canvas.RestoreDocumentPreservingView(_redo.Pop());
            RefreshDirtyFromSavePoint(); bool autosaved = _isDirty ? SaveAutosave() : ClearAutosaveSafely(); ApplyTheme(); RebuildInspector(); UpdateStatus(); QueueRoutingRefresh(); if (autosaved) _statusText.Text = "已重做"; UpdateTitle();
        }

        private void RunAutomaticLayout()
        {
            FinishPendingCanvasWork();
            if (_canvas.Document == null || (_canvas.Document.nodes.Count == 0 && _canvas.Document.groups.Count == 0))
            {
                _statusText.Text = "当前关系图没有可排版的对象"; return;
            }
            string beforeJson = GraphSerialization.Serialize(_canvas.Document, false);
            GraphDocument snapshot = GraphSerialization.CreateImmutableSnapshot(_canvas.Document);
            GraphLayoutResult result = null;
            StartBackgroundWork("正在自动排版", delegate(BackgroundWorkContext context)
            {
                context.ReportProgress(8, "正在分析关系层级…");
                context.ThrowIfCancellationRequested();
                GraphLayoutOptions options = GraphLayoutOptions.ForDocument(snapshot);
                options.CancellationRequested = delegate { return context.IsCancellationRequested; };
                options.ProgressChanged = delegate(int percentage, string message) { context.ReportProgress(percentage, message); };
                result = GraphLayout.Calculate(snapshot, options);
                context.ThrowIfCancellationRequested();
                context.ReportProgress(100, "排版与避障计算完成，正在更新画布…");
            }, delegate(BackgroundWorkCompletedEventArgs completed)
            {
                if (completed.Cancelled) { _statusText.Text = "自动排版已取消，画布未改变"; return; }
                if (completed.Error != null) { ShowError("自动排版失败", completed.Error); return; }
                if (!_canvas.ApplyAutomaticLayout(result, beforeJson)) { _statusText.Text = "自动排版没有生成可应用的结果"; return; }
                int conflicts = result.Edges.Values.Count(delegate(GraphEdgeLayout edge) { return edge.HasObstacleConflict; });
                string message = "自动排版完成：端口已错开，连线和标签已避让";
                if (conflicts > 0) message += "；" + conflicts + " 条关系使用了安全回退路径";
                if (result.Warnings.Count > 0) message += "；另有 " + result.Warnings.Count + " 条布局提示";
                _statusText.Text = message;
                UpdateStatus();
            });
        }

        internal void RunAutomaticLayoutForTesting() { RunAutomaticLayout(); }

        private void AddGroup()
        {
            FinishPendingCanvasWork();
            string beforeJson = GraphSerialization.Serialize(_canvas.Document, false);
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
            _canvas.Document.groups.Add(group); CommitChange(beforeJson, wrapsSelection ? "已为选中内容创建分组" : "分组已添加"); _canvas.SelectEntity("group", group.id);
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
            AddNode("process");
        }

        private void AddNode(string shape) { AddNodeAt(_canvas.ViewCenterWorld, "流程图形已在屏幕中心添加", shape); }

        private void AddNodeAt(PointF worldPoint) { AddNodeAt(worldPoint, "已在双击位置创建节点"); }

        private void AddNodeAt(PointF worldPoint, string commitMessage) { AddNodeAt(worldPoint, commitMessage, "process"); }

        private void AddNodeAt(PointF worldPoint, string commitMessage, string shape)
        {
            FinishPendingCanvasWork();
            if (!_canvas.EditMode) return;
            string beforeJson = GraphSerialization.Serialize(_canvas.Document, false);
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
                id = GraphSerialization.UniqueId("node", ids), label = shape == "decision" ? "判断条件" : shape == "terminator" ? "开始 / 结束" : shape == "data" ? "输入 / 输出" : shape == "document" ? "文档" : "处理步骤", type = "节点类型", kind = shape == "terminator" ? "output" : shape == "decision" ? "commercial" : shape == "data" ? "resource" : shape == "document" ? "content" : "system", shape = GraphSerialization.NormalizeNodeShape(shape),
                group = group == null ? "" : group.id,
                x = worldPoint.X - width / 2f,
                y = worldPoint.Y - height / 2f,
                w = width, h = height, note = ""
            };
            _canvas.Document.nodes.Add(node); CommitChange(beforeJson, commitMessage); _canvas.SelectEntity("node", node.id);
        }

        internal void AddGroupForTesting() { AddGroup(); }
        internal void AddNodeForTesting() { AddNode(); }
        internal void AddNodeAtForTesting(PointF worldPoint) { AddNodeAt(worldPoint); }
        internal GraphCanvas CanvasForTesting { get { return _canvas; } }
        internal bool FlowchartPaletteAccessibleForTesting
        {
            get
            {
                List<Button> buttons = _flowchartPalette.Controls.OfType<Button>().ToList();
                return buttons.Count == 5 && buttons.All(delegate(Button button) { return !String.IsNullOrWhiteSpace(button.AccessibleName) && button.TabStop; });
            }
        }
        internal bool AddFlowchartShapeUsingKeyboardForTesting(string shape)
        {
            Button button = _flowchartPalette.Controls.OfType<Button>().FirstOrDefault(delegate(Button item) { return String.Equals(item.Tag as string, shape, StringComparison.Ordinal); });
            if (button == null) return false; int before = _canvas.Document.nodes.Count;
            System.Reflection.MethodInfo click = button.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (click == null) return false; click.Invoke(button, new object[] { EventArgs.Empty });
            return _canvas.Document.nodes.Count == before + 1;
        }
        internal bool ToolbarAccessibleForTesting
        {
            get
            {
                return !String.IsNullOrWhiteSpace(_lineTypeBox.AccessibleName) && !String.IsNullOrWhiteSpace(_directionBox.AccessibleName) &&
                    !String.IsNullOrWhiteSpace(_depthBox.AccessibleName) && !String.IsNullOrWhiteSpace(_themeBox.AccessibleName) && !String.IsNullOrWhiteSpace(_searchBox.AccessibleName);
            }
        }

        private void AddRelation()
        {
            FinishPendingCanvasWork();
            if (_canvas.Document.nodes.Count + _canvas.Document.groups.Count < 2) { MessageBox.Show(this, "至少需要两个节点或分组。", "无法新增关系", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (RelationDialog dialog = new RelationDialog(_canvas.Document, _canvas.NewLineType, _darkTheme))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string beforeJson = GraphSerialization.Serialize(_canvas.Document, false);
                HashSet<string> ids = new HashSet<string>(_canvas.Document.edges.Select(delegate(GraphEdge item) { return item.id; }));
                GraphEdge edge = dialog.CreateEdge(GraphSerialization.UniqueId("edge", ids));
                bool duplicate = _canvas.Document.edges.Any(delegate(GraphEdge item) { return GraphSerialization.EndpointKey(item.sourceType, item.source) == GraphSerialization.EndpointKey(edge.sourceType, edge.source) && GraphSerialization.EndpointKey(item.targetType, item.target) == GraphSerialization.EndpointKey(edge.targetType, edge.target); });
                if (duplicate) { MessageBox.Show(this, "这两个对象之间已经存在同方向关系。", "关系未新增", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                _canvas.Document.edges.Add(edge); CommitChange(beforeJson, "关系已添加"); _canvas.SelectEntity("edge", edge.id);
            }
        }

        private void DeleteSelected()
        {
            FinishPendingCanvasWork();
            if (!_canvas.EditMode || String.IsNullOrEmpty(_canvas.SelectedType)) return;
            string beforeJson = GraphSerialization.Serialize(_canvas.Document, false); bool changed = false;
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
            if (!changed) return; _canvas.ClearSelection(); CommitChange(beforeJson, "已删除选中项");
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
            FinishPendingCanvasWork();
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

            string beforeJson = "";
            string previousSelectionType = _canvas.SelectedType, previousSelectionId = _canvas.SelectedId;
            List<string> previousNodeSelection = _canvas.SelectedNodeIds.ToList(), previousGroupSelection = _canvas.SelectedGroupIds.ToList();
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

                beforeJson = GraphSerialization.Serialize(_canvas.Document, false);
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
                    GraphNode created = new GraphNode { id = newId, label = source.label, type = source.type, kind = source.kind, shape = source.shape, group = groupId, x = source.x + dx, y = source.y + dy, w = source.w, h = source.h, note = source.note };
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
                List<string> selectedPastedNodes = nodeMap.Where(delegate(KeyValuePair<string, string> pair) { return explicitlySelectedSourceNodes.Contains(pair.Key); }).Select(delegate(KeyValuePair<string, string> pair) { return pair.Value; }).ToList();
                List<string> selectedPastedGroups = payload.selectionType == "group" || payload.selectionType == "groups" || payload.selectionType == "mixed" ? groupMap.Where(delegate(KeyValuePair<string, string> pair) { return selectedSourceGroupIds.Contains(pair.Key); }).Select(delegate(KeyValuePair<string, string> pair) { return pair.Value; }).ToList() : new List<string>();
                _canvas.RefreshDocument();
                _canvas.SelectObjects(selectedPastedNodes, selectedPastedGroups);
                CommitChange(beforeJson, "已粘贴 " + pastedNodeIds.Count + " 个节点、" + pastedGroupIds.Count + " 个分组和 " + pastedEdgeIds.Count + " 条关系");
                return true;
            }
            catch (Exception error)
            {
                if (beforeJson.Length > 0)
                {
                    try
                    {
                        _canvas.RestoreDocumentPreservingView(GraphSerialization.Deserialize(beforeJson));
                        if (previousSelectionType == "edge") _canvas.SelectEntity("edge", previousSelectionId);
                        else _canvas.SelectObjects(previousNodeSelection, previousGroupSelection);
                    }
                    catch { }
                }
                _statusText.Text = "无法粘贴：" + error.Message; return false;
            }
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

        private void FocusSearch()
        {
            _searchBox.Focus(); _searchBox.SelectAll();
        }

        private List<GraphSearchTarget> SearchTargets(string query)
        {
            List<GraphSearchTarget> results = new List<GraphSearchTarget>();
            if (_canvas.Document == null || String.IsNullOrWhiteSpace(query)) return results;
            foreach (GraphNode node in _canvas.Document.nodes)
                if (GraphTextReplacement.Contains(node.label, query) || GraphTextReplacement.Contains(node.type, query) || GraphTextReplacement.Contains(node.note, query))
                    results.Add(new GraphSearchTarget { Type = "node", Id = node.id, Label = node.label });
            foreach (GraphGroup group in _canvas.Document.groups)
                if (GraphTextReplacement.Contains(group.label, query))
                    results.Add(new GraphSearchTarget { Type = "group", Id = group.id, Label = group.label });
            foreach (GraphEdge edge in _canvas.Document.edges)
                if (GraphTextReplacement.Contains(edge.label, query))
                    results.Add(new GraphSearchTarget { Type = "edge", Id = edge.id, Label = String.IsNullOrWhiteSpace(edge.label) ? "未命名关系" : edge.label });
            return results;
        }

        private void FindEntity()
        {
            FindEntity(_searchBox.Text ?? "");
        }

        private void FindEntity(string query)
        {
            if (String.IsNullOrWhiteSpace(query))
            {
                _statusText.Text = "请输入要查找的内容";
                SetReplaceDialogStatus("请输入要查找的内容。", true);
                return;
            }
            _searchBox.Text = query;
            List<GraphSearchTarget> results = SearchTargets(query);
            if (results.Count == 0)
            {
                _statusText.Text = "没有找到“" + query + "”";
                SetReplaceDialogStatus("没有找到“" + query + "”。", true);
                return;
            }
            int current = results.FindIndex(delegate(GraphSearchTarget item)
            {
                return item.Type == _canvas.SelectedType && item.Id == _canvas.SelectedId;
            });
            int next = current < 0 ? 0 : (current + 1) % results.Count;
            GraphSearchTarget target = results[next];
            _canvas.SelectEntity(target.Type, target.Id);
            _canvas.EnsureEntityVisible(target.Type, target.Id);
            _statusText.Text = "已定位" + EntityTypeName(target.Type) + "：" + target.Label + "（" + (next + 1) + "/" + results.Count + "）";
            SetReplaceDialogStatus("已找到第 " + (next + 1) + " 个，共 " + results.Count + " 个对象。", false);
        }

        internal void FindEntityForTesting(string query) { FindEntity(query); }

        private static string EntityTypeName(string type)
        {
            return type == "node" ? "节点" : type == "group" ? "分组" : "关系";
        }

        private void ShowReplaceDialog()
        {
            FinishPendingCanvasWork();
            string initialQuery = ReplacementInitialQuery();
            if (_replaceDialog != null && !_replaceDialog.IsDisposed)
            {
                _canvas.ReplaceModeActive = true;
                _replaceDialog.Prepare(initialQuery);
                return;
            }
            _canvas.ReplaceModeActive = true;
            _replaceDialog = new ReplaceDialog(initialQuery);
            _replaceDialog.FindNextRequested += FindEntity;
            _replaceDialog.ReplaceSelectionRequested += delegate(string query, string replacement) { ReplaceSelectedText(query, replacement); };
            _replaceDialog.ReplaceAllRequested += delegate(string query, string replacement) { ReplaceAllText(query, replacement); };
            _replaceDialog.FormClosed += delegate { _canvas.ReplaceModeActive = false; _replaceDialog = null; };
            _replaceDialog.Show(this);
            NativeTheme.ApplyControlTree(_replaceDialog, _darkTheme);
            NativeTheme.ApplyWindowDarkMode(_replaceDialog, _darkTheme);
        }

        private string ReplacementInitialQuery()
        {
            if (_canvas.SelectionCount == 1)
            {
                if (_canvas.SelectedType == "node")
                {
                    GraphNode node = _canvas.Document.nodes.FirstOrDefault(delegate(GraphNode item) { return item.id == _canvas.SelectedId; });
                    if (node != null && !String.IsNullOrWhiteSpace(node.label)) return node.label;
                }
                else if (_canvas.SelectedType == "group")
                {
                    GraphGroup group = _canvas.Document.groups.FirstOrDefault(delegate(GraphGroup item) { return item.id == _canvas.SelectedId; });
                    if (group != null && !String.IsNullOrWhiteSpace(group.label)) return group.label;
                }
                else if (_canvas.SelectedType == "edge")
                {
                    GraphEdge edge = _canvas.Document.edges.FirstOrDefault(delegate(GraphEdge item) { return item.id == _canvas.SelectedId; });
                    if (edge != null && !String.IsNullOrWhiteSpace(edge.label)) return edge.label;
                }
            }
            return _searchBox.Text ?? "";
        }

        internal string ReplacementInitialQueryForTesting() { return ReplacementInitialQuery(); }

        private GraphTextReplacementResult ReplaceSelectedText(string query, string replacement)
        {
            GraphTextReplacementResult empty = new GraphTextReplacementResult();
            if (!ValidateReplacementRequest(query)) return empty;
            FinishPendingCanvasWork();
            if (_canvas.SelectionCount == 0)
            {
                _statusText.Text = "请先在画布中选择要替换的对象";
                SetReplaceDialogStatus("当前没有选中对象；请先单选、框选或多选。", true);
                return empty;
            }
            string beforeJson = GraphSerialization.Serialize(_canvas.Document, false);
            string edgeId = _canvas.SelectedType == "edge" ? _canvas.SelectedId : "";
            GraphTextReplacementResult result = GraphTextReplacement.ReplaceSelection(_canvas.Document, query, replacement,
                _canvas.SelectedNodeIds, _canvas.SelectedGroupIds, edgeId);
            FinishReplacement(result, beforeJson, "当前所选");
            return result;
        }

        private GraphTextReplacementResult ReplaceAllText(string query, string replacement)
        {
            GraphTextReplacementResult empty = new GraphTextReplacementResult();
            if (!ValidateReplacementRequest(query)) return empty;
            FinishPendingCanvasWork();
            string beforeJson = GraphSerialization.Serialize(_canvas.Document, false);
            GraphTextReplacementResult result = GraphTextReplacement.ReplaceAll(_canvas.Document, query, replacement);
            FinishReplacement(result, beforeJson, "整张图");
            return result;
        }

        private bool ValidateReplacementRequest(string query)
        {
            if (!String.IsNullOrWhiteSpace(query)) { _searchBox.Text = query; return true; }
            _statusText.Text = "请输入要查找的内容";
            SetReplaceDialogStatus("查找内容不能为空。", true);
            return false;
        }

        private void FinishReplacement(GraphTextReplacementResult result, string beforeJson, string scope)
        {
            if (result.AppliedOccurrences > 0)
            {
                string message = scope + "已替换 " + result.AppliedOccurrences + " 处（" + result.ChangedFields + " 个文本字段）";
                if (result.SkippedOccurrences > 0) message += "，另有 " + result.SkippedOccurrences + " 处因名称不能为空而跳过";
                CommitChange(beforeJson, message);
                SetReplaceDialogStatus(message + "。可按 Ctrl+Z 撤销。", false);
                return;
            }
            string noChange = result.SkippedOccurrences > 0
                ? "匹配内容会使名称变为空，已跳过 " + result.SkippedOccurrences + " 处。"
                : scope + "没有可替换的匹配内容。";
            _statusText.Text = noChange; SetReplaceDialogStatus(noChange, true);
        }

        private void SetReplaceDialogStatus(string message, bool error)
        {
            if (_replaceDialog != null && !_replaceDialog.IsDisposed) _replaceDialog.SetStatus(message, error);
        }

        internal GraphTextReplacementResult ReplaceSelectedTextForTesting(string query, string replacement) { return ReplaceSelectedText(query, replacement); }
        internal GraphTextReplacementResult ReplaceAllTextForTesting(string query, string replacement) { return ReplaceAllText(query, replacement); }

        private void RebuildInspector()
        {
            if (_settingUi || _canvas.Document == null) return; _settingUi = true;
            _inspector.SuspendLayout();
            while (_inspector.Controls.Count > 0) _inspector.Controls[0].Dispose();
            int y = 24;
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
        internal bool InspectorTextVisibleForTesting
        {
            get
            {
                foreach (Label label in _inspector.Controls.OfType<Label>())
                {
                    TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak;
                    Size measured = TextRenderer.MeasureText(label.Text ?? "", label.Font, new Size(Math.Max(1, label.ClientSize.Width), 100000), flags);
                    if (measured.Height + 2 > label.ClientSize.Height) return false;
                }
                return true;
            }
        }
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

        private void RebuildFlowchartPalette()
        {
            while (_flowchartPalette.Controls.Count > 0) _flowchartPalette.Controls[0].Dispose();
            _flowchartPalette.Visible = IsFlowchart; if (!IsFlowchart) return;
            Label title = new Label { Text = "流程图组件", Font = new Font(Font, FontStyle.Bold), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            title.SetBounds(16, 16, 188, 32); _flowchartPalette.Controls.Add(title);
            int y = 58; AddPaletteItem("开始 / 结束", "terminator", ref y); AddPaletteItem("处理步骤", "process", ref y); AddPaletteItem("判断 / 分支", "decision", ref y); AddPaletteItem("输入 / 输出", "data", ref y); AddPaletteItem("文档", "document", ref y);
            NativeTheme.ApplyControlTree(_flowchartPalette, _darkTheme); _flowchartPalette.BringToFront();
        }

        private void AddPaletteItem(string text, string shape, ref int y)
        {
            Button button = new Button(); button.Text = ""; button.Tag = shape; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
            button.AccessibleName = text; button.AccessibleDescription = "流程图组件。按 Enter 可添加到当前视野中心，也可用鼠标拖到画布。"; button.TabStop = true;
            button.SetBounds(16, y, 188, 52); button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            button.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) button.DoDragDrop(new DataObject("FlowchartShape", shape), DragDropEffects.Copy); };
            button.Click += delegate { if (IsFlowchart) AddNodeAt(_canvas.ViewCenterWorld, "已从组件库添加流程图形", shape); };
            button.Paint += delegate(object sender, PaintEventArgs e) { DrawPaletteShape(e.Graphics, button.ClientRectangle, shape, text); };
            _flowchartPalette.Controls.Add(button); y += 64;
        }

        private static void DrawPaletteShape(Graphics graphics, Rectangle bounds, string shape, string text)
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(20, 5, Math.Max(40, bounds.Width - 40), Math.Max(30, bounds.Height - 10));
            Color fill = shape == "terminator" ? Color.FromArgb(207, 241, 216) : shape == "decision" ? Color.FromArgb(255, 226, 190) : shape == "data" ? Color.FromArgb(205, 229, 252) : shape == "document" ? Color.FromArgb(249, 213, 234) : Color.FromArgb(222, 229, 237);
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                if (shape == "terminator") path.AddEllipse(r);
                else if (shape == "decision") path.AddPolygon(new[] { new PointF(r.Left + r.Width / 2, r.Top), new PointF(r.Right, r.Top + r.Height / 2), new PointF(r.Left + r.Width / 2, r.Bottom), new PointF(r.Left, r.Top + r.Height / 2) });
                else if (shape == "data") path.AddPolygon(new[] { new PointF(r.Left + 14, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right - 14, r.Bottom), new PointF(r.Left, r.Bottom) });
                else if (shape == "document") { path.AddLines(new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom - 7), new PointF(r.Right - r.Width / 4, r.Bottom), new PointF(r.Left + r.Width / 4, r.Bottom - 7), new PointF(r.Left, r.Bottom), new PointF(r.Left, r.Top) }); path.CloseFigure(); }
                else path.AddRectangle(r);
                using (Brush brush = new SolidBrush(fill)) graphics.FillPath(brush, path); using (Pen pen = new Pen(Color.FromArgb(90, 105, 120), 1.4f)) graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(graphics, text, SystemFonts.MessageBoxFont, Rectangle.Round(r), Color.FromArgb(30, 40, 52), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void BuildProjectInspector(ref int y)
        {
            AddMuted("未选择对象", ref y); AddParagraph("先单击对象进行选择；再次拖动已选择的节点或分组可移动。拖动未选择的节点或分组会创建关系。右键可清除全部选择。", ref y);
            TextBox title = AddTextField("图名称", _canvas.Document.meta.title, false, ref y);
            BindAutoText(title, false, delegate(string value) { _canvas.Document.meta.title = value; UpdateTitle(); }, "图名称已应用，恢复副本将在后台更新");
            AddMuted("画布不受默认尺寸边界限制，可向任意方向平移和摆放内容。\n节点可同时属于多个分组，分组也可多层嵌套；所属关系按位置自动匹配。", ref y);
        }

        private void BuildNodeInspector(ref int y)
        {
            GraphNode node = _canvas.Document.nodes.FirstOrDefault(delegate(GraphNode item) { return item.id == _canvas.SelectedId; }); if (node == null) return;
            AddMuted("节点 · " + node.id, ref y);
            TextBox label = AddTextField("名称", node.label, false, ref y); TextBox type = IsFlowchart ? null : AddTextField("类型", node.type, false, ref y);
            ComboBox kind = AddColorCategoryField(node.kind, ref y);
            ComboBox shape = IsFlowchart ? AddComboField("流程图形", new[] { "处理步骤", "开始 / 结束", "判断 / 分支", "输入 / 输出", "文档" }, ShapeIndex(node.shape), ref y) : null;
            AddMuted("仅影响节点配色，不影响节点类型、关系或功能。", ref y);
            AddLabel("所属分组（自动匹配，只读）", ref y); AddParagraph(AutomaticMembershipText(node.groups), ref y);
            TextBox note = AddTextField("备注", node.note, true, ref y);
            BindAutoText(label, false, delegate(string value) { node.label = value; }, "节点名称已应用，恢复副本将在后台更新");
            if (type != null) BindAutoText(type, false, delegate(string value) { node.type = value; }, "节点类型已应用，恢复副本将在后台更新");
            BindAutoCombo(kind, delegate { ColorCategoryChoice choice = kind.SelectedItem as ColorCategoryChoice; if (choice != null) node.kind = choice.Kind; }, "节点颜色分类已应用，恢复副本将在后台更新");
            if (shape != null) BindAutoCombo(shape, delegate { node.shape = ShapeAt(shape.SelectedIndex); }, "流程图形已应用，恢复副本将在后台更新");
            BindAutoText(note, true, delegate(string value) { node.note = value; }, "节点备注已应用，恢复副本将在后台更新");
            AddParagraph(RelationSummary("node", node.id), ref y);
        }

        private void BuildMultiNodeInspector(ref int y)
        {
            List<string> ids = _canvas.SelectedNodeIds.ToList(); AddMuted("已选择 " + ids.Count + " 个节点", ref y);
            TextBox type = AddTextField("统一类型（留空则不修改）", "", false, ref y);
            BindAutoText(type, false, delegate(string value) { foreach (GraphNode node in _canvas.Document.nodes.Where(delegate(GraphNode item) { return ids.Contains(item.id); })) node.type = value; }, "多个节点类型已应用，恢复副本将在后台更新");
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
            BindAutoText(label, false, delegate(string value) { group.label = value; }, "分组名称已应用，恢复副本将在后台更新");
            AddLabel("所属分组（自动匹配，只读）", ref y); AddParagraph(AutomaticMembershipText(group.groups), ref y);
            AddParagraph("位置 " + (int)group.x + ", " + (int)group.y + "　大小 " + (int)group.w + " × " + (int)group.h + "\n" + RelationSummary("group", group.id), ref y);
        }

        private void BuildEdgeInspector(ref int y)
        {
            GraphEdge edge = _canvas.Document.edges.FirstOrDefault(delegate(GraphEdge item) { return item.id == _canvas.SelectedId; }); if (edge == null) return;
            AddMuted("关系 · " + edge.id, ref y); List<EndpointItem> endpoints = Endpoints();
            ComboBox source = AddEndpointField("起点", endpoints, GraphSerialization.EndpointKey(edge.sourceType, edge.source), ref y);
            ComboBox target = AddEndpointField("终点", endpoints, GraphSerialization.EndpointKey(edge.targetType, edge.target), ref y);
            Button swap = new Button { Text = "交换起点和终点", AccessibleName = "交换关系方向" };
            swap.SetBounds(InspectorLeft, y, InspectorContentWidth(), 36); swap.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            swap.Click += delegate { SwapSelectedEdgeDirection(); }; _inspector.Controls.Add(swap); y += 50;
            TextBox label = AddTextField("名称（可留空）", edge.label, false, ref y);
            ComboBox category = AddComboField("关系类别", _canvas.Document.settings.relationTypes.Select(delegate(RelationType item) { return item.label; }).ToArray(), Math.Max(0, _canvas.Document.settings.relationTypes.FindIndex(delegate(RelationType item) { return item.id == edge.category; })), ref y);
            ComboBox line = AddComboField("线型", new[] { "自动避障", "曲线", "直线", "折线" }, LineTypeIndex(edge.lineType), ref y);
            AddMuted("线条颜色自动跟随起点节点的颜色分类；起点颜色改变时会同步更新。", ref y);
            Action applyEndpoints = delegate
            {
                EndpointItem s = source.SelectedItem as EndpointItem, t = target.SelectedItem as EndpointItem;
                if (s == null || t == null || s.Key == t.Key) { _statusText.Text = "起点和终点不能相同，未自动应用"; try { BeginInvoke((Action)delegate { RebuildInspector(); }); } catch { } return; }
                bool duplicate = _canvas.Document.edges.Any(delegate(GraphEdge item) { return item.id != edge.id && GraphSerialization.EndpointKey(item.sourceType, item.source) == s.Key && GraphSerialization.EndpointKey(item.targetType, item.target) == t.Key; });
                if (duplicate) { _statusText.Text = "同方向关系已存在，未自动应用"; try { BeginInvoke((Action)delegate { RebuildInspector(); }); } catch { } return; }
                ApplyInspectorChange(source.ContainsFocus ? (Control)source : target, delegate { edge.sourceType = s.Type; edge.source = s.Id; edge.targetType = t.Type; edge.target = t.Id; edge.sourceSide = ""; edge.targetSide = ""; }, "关系端点已应用，恢复副本将在后台更新");
            };
            source.SelectedIndexChanged += delegate { if (!_settingUi && _canvas.EditMode) applyEndpoints(); };
            target.SelectedIndexChanged += delegate { if (!_settingUi && _canvas.EditMode) applyEndpoints(); };
            source.LostFocus += delegate { FlushPendingInspectorChange(); }; target.LostFocus += delegate { FlushPendingInspectorChange(); };
            BindAutoText(label, true, delegate(string value) { edge.label = value.Trim(); }, "关系名称已应用，恢复副本将在后台更新");
            BindAutoCombo(category, delegate { edge.category = _canvas.Document.settings.relationTypes[Math.Max(0, category.SelectedIndex)].id; }, "关系类别已应用，恢复副本将在后台更新");
            BindAutoCombo(line, delegate { edge.lineType = LineTypeAt(line.SelectedIndex); }, "关系线型已应用，恢复副本将在后台更新");
        }

        private bool SwapSelectedEdgeDirection()
        {
            FinishPendingCanvasWork();
            if (_canvas.Document == null || _canvas.SelectedType != "edge") return false;
            GraphEdge edge = _canvas.Document.edges.FirstOrDefault(delegate(GraphEdge item) { return item.id == _canvas.SelectedId; });
            if (edge == null) return false;
            string nextSourceKey = GraphSerialization.EndpointKey(edge.targetType, edge.target);
            string nextTargetKey = GraphSerialization.EndpointKey(edge.sourceType, edge.source);
            bool duplicate = _canvas.Document.edges.Any(delegate(GraphEdge item)
            {
                return item.id != edge.id && GraphSerialization.EndpointKey(item.sourceType, item.source) == nextSourceKey && GraphSerialization.EndpointKey(item.targetType, item.target) == nextTargetKey;
            });
            if (duplicate) { _statusText.Text = "反向关系已经存在，未交换方向"; return false; }
            string beforeJson = GraphSerialization.Serialize(_canvas.Document, false);
            string sourceType = edge.sourceType, source = edge.source;
            edge.sourceType = edge.targetType; edge.source = edge.target;
            edge.targetType = sourceType; edge.target = source;
            edge.sourceSide = ""; edge.targetSide = "";
            CommitChange(beforeJson, "关系方向已交换");
            _canvas.SelectEntity("edge", edge.id);
            return true;
        }

        internal bool SwapSelectedEdgeDirectionForTesting() { return SwapSelectedEdgeDirection(); }

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
            AddLabel(label, ref y); ComboBox box = new ComboBox(); box.DropDownStyle = ComboBoxStyle.DropDownList; box.Font = Font; box.SetBounds(InspectorLeft, y, InspectorContentWidth(), 34); box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            box.AccessibleName = label;
            _inspector.Controls.Add(box);
            box.Items.AddRange(endpoints.Cast<object>().ToArray());
            int index = endpoints.FindIndex(delegate(EndpointItem item) { return item.Key == selectedKey; });
            if (box.Items.Count > 0) box.SelectedIndex = Math.Max(0, Math.Min(box.Items.Count - 1, index));
            y += 48; return box;
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

        private void AddInspectorTitle(string text, ref int y)
        {
            Label label = NewLabel(text, 17f, FontStyle.Bold, Color.FromArgb(25, 35, 48));
            int width = InspectorContentWidth();
            int height = MeasureInspectorTextHeight(label, width, 30, false);
            label.SetBounds(InspectorLeft, y, width, height);
            label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _inspector.Controls.Add(label);
            y += height + 14;
        }

        private void AddLabel(string text, ref int y)
        {
            Label label = NewLabel(text, 10f, FontStyle.Regular, Color.FromArgb(76, 89, 105));
            int width = InspectorContentWidth();
            int height = MeasureInspectorTextHeight(label, width, 23, false);
            label.SetBounds(InspectorLeft, y, width, height);
            label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _inspector.Controls.Add(label);
            y += height + 4;
        }

        private void AddMuted(string text, ref int y)
        {
            Label label = NewLabel(text, 9.25f, FontStyle.Regular, Color.FromArgb(105, 118, 132));
            label.AutoEllipsis = false;
            int width = InspectorContentWidth();
            int height = MeasureInspectorTextHeight(label, width, 38, true);
            label.SetBounds(InspectorLeft, y, width, height);
            label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _inspector.Controls.Add(label);
            y += height + 12;
        }

        private void AddParagraph(string text, ref int y)
        {
            Label label = NewLabel(text, 10f, FontStyle.Regular, Color.FromArgb(55, 68, 84));
            label.AutoEllipsis = false;
            int width = InspectorContentWidth();
            int height = MeasureInspectorTextHeight(label, width, 76, true);
            label.SetBounds(InspectorLeft, y, width, height);
            label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _inspector.Controls.Add(label);
            y += height + 12;
        }

        private int InspectorContentWidth()
        {
            return Math.Max(260, _inspector.ClientSize.Width - InspectorLeft - InspectorRight);
        }

        private static int MeasureInspectorTextHeight(Label label, int width, int minimumHeight, bool wrap)
        {
            TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
            flags |= wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine;
            Size measured = TextRenderer.MeasureText(label.Text ?? "", label.Font, new Size(Math.Max(1, width), 100000), flags);
            return Math.Max(minimumHeight, measured.Height + 4);
        }
        private static Label NewLabel(string text, float size, FontStyle style, Color color) { return new Label { Text = text, Font = new Font("Microsoft YaHei UI", size, style), ForeColor = color, AutoEllipsis = true }; }

        private TextBox AddTextField(string label, string value, bool multiline, ref int y)
        {
            AddLabel(label, ref y); TextBox box = new TextBox(); box.Text = value ?? ""; box.Multiline = multiline; box.ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None; box.Font = Font;
            box.AccessibleName = label;
            box.MaxLength = label == "图名称" ? 80 : label.IndexOf("备注", StringComparison.Ordinal) >= 0 ? 300 : label.IndexOf("类型", StringComparison.Ordinal) >= 0 ? 30 : 40;
            box.AutoSize = false; int height = multiline ? 120 : Math.Max(34, box.Font.Height + 12); box.SetBounds(InspectorLeft, y, InspectorContentWidth(), height); box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; _inspector.Controls.Add(box); y += height + 16; return box;
        }

        private ComboBox AddComboField(string label, string[] choices, int selected, ref int y)
        {
            AddLabel(label, ref y); ComboBox box = new ComboBox(); box.DropDownStyle = ComboBoxStyle.DropDownList; box.Font = Font; box.Items.AddRange(choices.Cast<object>().ToArray()); if (box.Items.Count > 0) box.SelectedIndex = Math.Max(0, Math.Min(box.Items.Count - 1, selected));
            box.AccessibleName = label;
            box.SetBounds(InspectorLeft, y, InspectorContentWidth(), 34); box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; _inspector.Controls.Add(box); y += 48; return box;
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
            ComboBox box = new ComboBox(); box.DropDownStyle = ComboBoxStyle.DropDownList; box.Font = Font; box.DrawMode = DrawMode.OwnerDrawFixed; box.ItemHeight = 30; box.Items.AddRange(choices.Cast<object>().ToArray());
            box.AccessibleName = "颜色分类";
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
            box.SetBounds(InspectorLeft, y, InspectorContentWidth(), 36); box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; _inspector.Controls.Add(box); y += 50; return box;
        }

        private void UpdateStatus()
        {
            if (_canvas.Document == null) return; _countText.Text = _canvas.Document.groups.Count + " 分组　" + _canvas.Document.nodes.Count + " 节点　" + _canvas.Document.edges.Count + " 关系"; _zoomText.Text = "缩放 " + Math.Round(_canvas.Zoom * 100) + "%";
            _undoButton.Enabled = _undo.Count > 0; _redoButton.Enabled = _redo.Count > 0;
        }

        private void UpdateTitle() { if (_canvas.Document != null) Text = _canvas.Document.meta.title + (_isDirty ? " *" : "") + " — 关系图编辑器" + (String.IsNullOrEmpty(_currentFile) ? "" : "  [" + Path.GetFileName(_currentFile) + "]"); }
        internal bool DirtyForTesting { get { return _isDirty; } }
        internal string AutosavePathForTesting { get { return _autosavePath; } }
        private static string LineTypeAt(int index) { return index == 0 ? "auto" : index == 2 ? "straight" : index == 3 ? "polyline" : "curve"; }
        private static int ShapeIndex(string shape) { return shape == "terminator" ? 1 : shape == "decision" ? 2 : shape == "data" ? 3 : shape == "document" ? 4 : 0; }
        private static string ShapeAt(int index) { return index == 1 ? "terminator" : index == 2 ? "decision" : index == 3 ? "data" : index == 4 ? "document" : "process"; }
        private static int LineTypeIndex(string lineType) { return lineType == "auto" ? 0 : lineType == "straight" ? 2 : lineType == "polyline" ? 3 : 1; }

        private void MainFormKeyDown(object sender, KeyEventArgs e)
        {
            if (_activeWorkId != 0 && e.KeyCode == Keys.Escape)
            {
                RequestBackgroundCancellation();
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
            bool textInputFocused = TextInputHasFocus();
            if (e.Control && e.KeyCode == Keys.C && !textInputFocused) { CopySelected(true); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.V && !textInputFocused) { PasteSelected(true); e.Handled = true; e.SuppressKeyPress = true; }
            else if (ShouldDeleteSelection(e.KeyCode, textInputFocused)) { DeleteSelected(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F2 && !textInputFocused) { _canvas.BeginSelectedLabelEdit(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { _canvas.CancelActiveGesture(); _canvas.ClearSelection(); }
            else if (e.Control && e.KeyCode == Keys.F) { FocusSearch(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.H) { ShowReplaceDialog(); e.Handled = true; e.SuppressKeyPress = true; }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (_activeWorkId != 0 && (keyData & Keys.KeyCode) == Keys.Escape)
            {
                RequestBackgroundCancellation(); return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
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
            string help = String.Join("\n", new[]
            {
                "关系图打开后始终可以直接编辑。", "", "高频操作：",
                "· Ctrl+Shift+L：自动分层排版，并完成端口错开、连线避障和标签避让",
                "· 右下角小地图：点击或拖动快速导航；可在“视图”菜单中关闭",
                "· Tab / Shift+Tab：切换对象；方向键：移动；Shift+方向键：快速移动",
                "· Alt+方向键：选择相邻对象；Ctrl+方向键：平移视野",
                "· Enter 或 F2：编辑当前对象名称",
                "· Ctrl+F：查找并自动把目标移入视野；Ctrl+H：查找和替换",
                "· Ctrl+O / Ctrl+S / Ctrl+Shift+S：导入、保存、另存为",
                "", "画布操作：",
                "· 双击空白处新增节点；有选中内容时点击＋分组会自动包住选中内容",
                "· 先选中再拖动可移动对象；Ctrl 拖动关闭吸附并自由摆放",
                "· 拖动未选中的节点或分组到目标可创建关系",
                "· Ctrl/Shift 单击多选；空白处左键拖动框选；Delete 删除；Ctrl+Z 撤销",
                "· 选中分组后拖动边缘或四角调整大小；右键拖动画布；滚轮缩放",
                "· 右侧属性即时应用；恢复副本在后台防抖保存，JSON 文件仍以 Ctrl+S 保存",
                "", "Draw.io 与导出：",
                "· 多页 Draw.io 会先显示页面列表，由你选择要导入的页面",
                "· 文件 → 导出支持 Draw.io、只读 HTML、SVG、PNG 和 PDF",
                "· 大文件导入和导出在后台执行，状态栏显示进度并提供取消入口"
            });
            MessageBox.Show(this, help, "操作说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAbout() { MessageBox.Show(this, "关系图编辑器 5.0.0\n\n纯原生 Windows 桌面程序\nC# / WinForms / GDI+\n\n不使用浏览器、不启动网页服务、不以 HTML 作为运行底层。\n\n有任何问题或建议，请联系WZC", "关于", MessageBoxButtons.OK, MessageBoxIcon.Information); }
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

    internal sealed class DrawioPageDialog : Form
    {
        private readonly ListBox _pages = new ListBox();
        private readonly IList<DrawioPageInfo> _items;

        public DrawioPageDialog(IList<DrawioPageInfo> pages, bool darkTheme)
        {
            _items = pages ?? new List<DrawioPageInfo>();
            Text = "选择 Draw.io 页面"; Icon = NativeAppIcon.Create(); ShowIcon = true;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96f, 96f);
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ClientSize = new Size(520, 410);
            Font = new Font("Microsoft YaHei UI", 9.5f);
            Label intro = new Label { Text = "该文件包含多个页面。请选择要导入的页面：", AutoSize = false, AccessibleName = "页面选择说明" };
            intro.SetBounds(22, 20, 476, 28); Controls.Add(intro);
            _pages.SetBounds(22, 54, 476, 286); _pages.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _pages.AccessibleName = "Draw.io 页面列表";
            for (int index = 0; index < _items.Count; index++) _pages.Items.Add((index + 1) + ". " + _items[index].DisplayName);
            if (_pages.Items.Count > 0) _pages.SelectedIndex = 0;
            _pages.DoubleClick += delegate { if (_pages.SelectedIndex >= 0) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(_pages);
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel }; cancel.SetBounds(322, 354, 82, 36); Controls.Add(cancel);
            Button import = new Button { Text = "导入所选页", DialogResult = DialogResult.OK, BackColor = Color.FromArgb(43, 108, 245), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            import.FlatAppearance.BorderSize = 0; import.SetBounds(414, 354, 84, 36); import.Enabled = _pages.Items.Count > 0; Controls.Add(import);
            AcceptButton = import; CancelButton = cancel;
            NativeTheme.ApplyControlTree(this, darkTheme);
            HandleCreated += delegate { NativeTheme.ApplyWindowDarkMode(this, darkTheme); };
        }

        public int SelectedPageIndex
        {
            get
            {
                int selected = _pages.SelectedIndex;
                return selected >= 0 && selected < _items.Count ? _items[selected].Index : 0;
            }
        }
    }

    internal sealed class RelationDialog : Form
    {
        private readonly ComboBox _source = new ComboBox(); private readonly ComboBox _target = new ComboBox(); private readonly TextBox _label = new TextBox(); private readonly ComboBox _category = new ComboBox(); private readonly ComboBox _line = new ComboBox(); private readonly GraphDocument _graph;

        public RelationDialog(GraphDocument graph, string currentLineType, bool darkTheme)
        {
            _graph = graph; Text = "新增关系"; Icon = NativeAppIcon.Create(); ShowIcon = true; AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96f, 96f); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ClientSize = new Size(430, 345); Font = new Font("Microsoft YaHei UI", 9f);
            List<EndpointItem> endpoints = new List<EndpointItem>(); endpoints.AddRange(graph.nodes.Select(delegate(GraphNode item) { return new EndpointItem { Type = "node", Id = item.id, Label = "节点 · " + item.label }; })); endpoints.AddRange(graph.groups.Select(delegate(GraphGroup item) { return new EndpointItem { Type = "group", Id = item.id, Label = "分组 · " + item.label }; }));
            AddLabel("起点", 22); SetupCombo(_source, 48); _source.DataSource = new List<EndpointItem>(endpoints);
            AddLabel("终点", 88); SetupCombo(_target, 114); _target.DataSource = new List<EndpointItem>(endpoints); if (_target.Items.Count > 1) _target.SelectedIndex = 1;
            _source.AccessibleName = "关系起点"; _target.AccessibleName = "关系终点";
            AddLabel("关系名称（可留空）", 154); _label.SetBounds(22, 180, 386, 27); Controls.Add(_label);
            _label.MaxLength = 40; _label.AccessibleName = "关系名称";
            AddLabel("关系类别（线色跟随起点）", 220); SetupCombo(_category, 246); _category.Items.AddRange(graph.settings.relationTypes.Select(delegate(RelationType item) { return (object)item.label; }).ToArray()); _category.SelectedIndex = 0;
            AddLabel("线型", 286); _line.DropDownStyle = ComboBoxStyle.DropDownList; _line.Items.AddRange(new object[] { "自动避障", "曲线", "直线", "折线" }); _line.SelectedIndex = currentLineType == "auto" ? 0 : currentLineType == "straight" ? 2 : currentLineType == "polyline" ? 3 : 1; _line.SetBounds(74, 280, 105, 28); Controls.Add(_line);
            _category.AccessibleName = "关系类别"; _line.AccessibleName = "关系线型";
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
            return new GraphEdge { id = id, sourceType = source.Type, source = source.Id, targetType = target.Type, target = target.Id, label = _label.Text.Trim(), category = _graph.settings.relationTypes[Math.Max(0, _category.SelectedIndex)].id, lineType = _line.SelectedIndex == 0 ? "auto" : _line.SelectedIndex == 2 ? "straight" : _line.SelectedIndex == 3 ? "polyline" : "curve", sourceSide = "", targetSide = "" };
        }
    }
}
