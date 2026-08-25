using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace RelationshipGraphNative
{
    public sealed class GraphCommitEventArgs : EventArgs
    {
        private GraphDocument _before;
        public string BeforeJson { get; private set; }
        public GraphDocument Before
        {
            get
            {
                if (_before == null && !String.IsNullOrEmpty(BeforeJson)) _before = GraphSerialization.Deserialize(BeforeJson);
                return _before;
            }
        }
        public string Message { get; private set; }
        public GraphCommitEventArgs(string beforeJson, string message) { BeforeJson = beforeJson; Message = message; }
        public GraphCommitEventArgs(GraphDocument before, string message)
        {
            _before = before;
            BeforeJson = before == null ? null : GraphSerialization.Serialize(before, false);
            Message = message;
        }
    }

    public sealed class CanvasPointEventArgs : EventArgs
    {
        public PointF WorldPoint { get; private set; }
        public CanvasPointEventArgs(PointF worldPoint) { WorldPoint = worldPoint; }
    }

    internal enum CanvasGesture
    {
        None, Pan, SelectBox, MoveNodes, MoveGroup, MoveSelection, ResizeGroup, Link
    }

    internal sealed class EqualSpacingHint
    {
        public bool Horizontal;
        public RectangleF First;
        public RectangleF Middle;
        public RectangleF Last;
        public int MovingIndex;
        public float TargetDelta;
        public float Gap;
        public float Difference;
    }

    internal sealed class CachedEdgeGeometry : IDisposable
    {
        public string SourceType;
        public string SourceId;
        public string TargetType;
        public string TargetId;
        public string SourceSide;
        public string TargetSide;
        public string LineType;
        public RectangleF SourceRect;
        public RectangleF TargetRect;
        public GraphicsPath Path;
        public RectangleF Bounds;
        public PointF ArrowEnd;
        public PointF ArrowPrevious;
        public PointF LabelPoint;
        public bool HasArrow;

        public bool Matches(GraphEdge edge, RectangleF sourceRect, RectangleF targetRect)
        {
            return edge != null && SourceRect.Equals(sourceRect) && TargetRect.Equals(targetRect) &&
                String.Equals(SourceType, edge.sourceType, StringComparison.Ordinal) &&
                String.Equals(SourceId, edge.source, StringComparison.Ordinal) &&
                String.Equals(TargetType, edge.targetType, StringComparison.Ordinal) &&
                String.Equals(TargetId, edge.target, StringComparison.Ordinal) &&
                String.Equals(SourceSide, edge.sourceSide, StringComparison.Ordinal) &&
                String.Equals(TargetSide, edge.targetSide, StringComparison.Ordinal) &&
                String.Equals(LineType, edge.lineType, StringComparison.Ordinal);
        }

        public void Dispose()
        {
            if (Path != null) { Path.Dispose(); Path = null; }
        }
    }

    public sealed class GraphCanvas : Control
    {
        private const float MinimumSafeZoom = 0.00000001f;
        private const float ManualMinimumZoom = 0.01f;
        private const float MaximumFitZoom = 2.5f;
        private const float MaximumZoom = 4f;
        private const float MaximumViewOffset = (GraphSerialization.MaxCoordinate + GraphSerialization.MaxItemDimension) * MaximumZoom + GraphSerialization.MaxItemDimension;
        private GraphDocument _document;
        private readonly Dictionary<string, GraphNode> _nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, GraphGroup> _groups = new Dictionary<string, GraphGroup>(StringComparer.Ordinal);
        private readonly Dictionary<string, CachedEdgeGeometry> _edgeGeometry = new Dictionary<string, CachedEdgeGeometry>(StringComparer.Ordinal);
        private List<GraphGroup> _groupDrawOrder;
        private float _zoom = 1f;
        private float _offsetX = 20f;
        private float _offsetY = 20f;
        private bool _fitToViewActive;
        private CanvasGesture _gesture;
        private Point _mouseDown;
        private Point _lastMouse;
        private PointF _worldDown;
        private PointF _worldCurrent;
        private bool _gestureMoved;
        private string _gestureBeforeJson;
        private readonly Dictionary<string, PointF> _nodeStarts = new Dictionary<string, PointF>();
        private readonly Dictionary<string, RectangleF> _groupStarts = new Dictionary<string, RectangleF>();
        private readonly List<RectangleF> _alignmentCandidates = new List<RectangleF>();
        private RectangleF _groupStart;
        private string _gestureEntityType = "";
        private string _gestureEntityId = "";
        private string _resizeHandle = "";
        private string _sourceSide = "";
        private bool _sourceSideLocked;
        private bool _rightButtonPan;
        private bool _collapseMultiOnClick;
        private bool _controlToggleOnClick;
        private bool _controlNodeWasSelected;
        private Rectangle _selectionScreen;
        private float _alignmentGuideX = Single.NaN;
        private float _alignmentGuideY = Single.NaN;
        private EqualSpacingHint _horizontalSpacingHint;
        private EqualSpacingHint _verticalSpacingHint;
        private HashSet<string> _gestureFocusEntities;
        private HashSet<string> _gestureFocusEdges;
        private HashSet<string> _gestureInactiveSameNameNodes;
        private readonly HashSet<string> _selectedNodes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _selectedGroups = new HashSet<string>(StringComparer.Ordinal);
        private string _selectedType = "";
        private string _selectedId = "";
        private string _focusDirection;
        private int _focusDepth;
        private bool _darkTheme;
        private TextBox _inlineEditor;
        private Font _inlineEditorFont;
        private string _inlineEditType = "";
        private string _inlineEditId = "";
        private string _inlineEditField = "";
        private bool _finishingInlineEdit;
        private Font _ownedCanvasFont;
        private Font _nodeTypeFont;
        private Font _nodeLabelFont;
        private Font _spacingFont;
        private StringFormat _nodeLabelFormat;
        private StringFormat _entityHeaderFormat;
        private bool _disposingResources;
        private readonly Timer _replaceModeTimer = new Timer();
        private bool _replaceModeActive;
        private bool _replacePulse;

        public event EventHandler SelectionChanged;
        public event EventHandler<GraphCommitEventArgs> GraphCommitted;
        public event EventHandler ViewChanged;
        public event EventHandler<CanvasPointEventArgs> BlankDoubleClicked;

        public GraphCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            TabStop = true;
            BackColor = Color.FromArgb(244, 247, 250);
            // Canvas geometry is expressed in world pixels. Pixel fonts keep text and
            // node proportions stable when Windows changes the monitor DPI.
            _ownedCanvasFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            Font = _ownedCanvasFont;
            _replaceModeTimer.Interval = 420;
            _replaceModeTimer.Tick += delegate { _replacePulse = !_replacePulse; Invalidate(); };
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
        }

        public PointF ClientPointToWorld(Point point) { return ScreenToWorld(point); }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            DisposeDrawingResources();
            if (!_disposingResources) Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposingResources = true;
                if (_inlineEditor != null)
                {
                    TextBox editor = _inlineEditor;
                    _inlineEditor = null;
                    editor.Dispose();
                }
                DisposeInlineEditorFont();
                DisposeDrawingResources();
                ClearEdgeGeometryCache();
                _replaceModeTimer.Stop(); _replaceModeTimer.Dispose();
                if (_ownedCanvasFont != null) { _ownedCanvasFont.Dispose(); _ownedCanvasFont = null; }
            }
            base.Dispose(disposing);
        }

        private void EnsureDrawingResources()
        {
            if (_nodeTypeFont == null) _nodeTypeFont = new Font(Font.FontFamily, 10.4f, FontStyle.Regular, GraphicsUnit.Pixel);
            if (_nodeLabelFont == null) _nodeLabelFont = new Font(Font.FontFamily, 12.3f, FontStyle.Bold, GraphicsUnit.Pixel);
            if (_spacingFont == null) _spacingFont = new Font(Font.FontFamily, 10.4f, FontStyle.Bold, GraphicsUnit.Pixel);
            if (_nodeLabelFormat == null) _nodeLabelFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            if (_entityHeaderFormat == null) _entityHeaderFormat = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        }

        private void DisposeDrawingResources()
        {
            if (_nodeTypeFont != null) { _nodeTypeFont.Dispose(); _nodeTypeFont = null; }
            if (_nodeLabelFont != null) { _nodeLabelFont.Dispose(); _nodeLabelFont = null; }
            if (_spacingFont != null) { _spacingFont.Dispose(); _spacingFont = null; }
            if (_nodeLabelFormat != null) { _nodeLabelFormat.Dispose(); _nodeLabelFormat = null; }
            if (_entityHeaderFormat != null) { _entityHeaderFormat.Dispose(); _entityHeaderFormat = null; }
        }

        private void DisposeInlineEditorFont()
        {
            if (_inlineEditorFont != null) { _inlineEditorFont.Dispose(); _inlineEditorFont = null; }
        }

        private void ClearEdgeGeometryCache()
        {
            foreach (CachedEdgeGeometry geometry in _edgeGeometry.Values) geometry.Dispose();
            _edgeGeometry.Clear();
        }

        public GraphDocument Document
        {
            get { return _document; }
            set
            {
                CommitPendingEdit();
                _document = GraphSerialization.Normalize(value);
                RebuildIndexes();
                ClearSelection(false);
                FitToView();
                Invalidate();
            }
        }

        public bool EditMode { get; set; }
        public string NewLineType { get; set; }
        public string FocusDirection
        {
            get { return _focusDirection; }
            set { if (_focusDirection != value) { _focusDirection = value; ClearGestureFocusCache(); Invalidate(); } }
        }
        public int FocusDepth
        {
            get { return _focusDepth; }
            set { if (_focusDepth != value) { _focusDepth = value; ClearGestureFocusCache(); Invalidate(); } }
        }
        public string SelectedType { get { return _selectedType; } }
        public string SelectedId { get { return _selectedId; } }
        public ICollection<string> SelectedNodeIds { get { return _selectedNodes; } }
        public ICollection<string> SelectedGroupIds { get { return _selectedGroups; } }
        public int SelectionCount { get { return _selectedNodes.Count + _selectedGroups.Count + (_selectedType == "edge" ? 1 : 0); } }
        public bool ReplaceModeActive
        {
            get { return _replaceModeActive; }
            set
            {
                if (_replaceModeActive == value) return;
                _replaceModeActive = value; _replacePulse = value;
                if (value) _replaceModeTimer.Start(); else _replaceModeTimer.Stop();
                Invalidate();
            }
        }
        public float Zoom { get { return _zoom; } }
        public PointF ViewOffset { get { return new PointF(_offsetX, _offsetY); } }
        public PointF ViewCenterWorld
        {
            get
            {
                float zoom = SafeZoom(_zoom), offsetX = ClampViewOffset(_offsetX), offsetY = ClampViewOffset(_offsetY);
                return new PointF((ClientSize.Width / 2f - offsetX) / zoom, (ClientSize.Height / 2f - offsetY) / zoom);
            }
        }
        public bool DarkTheme
        {
            get { return _darkTheme; }
            set
            {
                if (_darkTheme == value) return;
                _darkTheme = value;
                BackColor = value ? NativeTheme.DarkBackground : NativeTheme.LightBackground;
                if (_inlineEditor != null)
                {
                    _inlineEditor.BackColor = value ? NativeTheme.DarkInput : Color.White;
                    _inlineEditor.ForeColor = value ? NativeTheme.DarkText : NativeTheme.LightText;
                }
                Invalidate();
            }
        }
        internal bool InlineEditorActiveForTesting { get { return _inlineEditor != null; } }
        internal string InlineEditFieldForTesting { get { return _inlineEditField; } }
        internal bool GestureSnapshotAvailableForTesting { get { return _gestureBeforeJson != null; } }
        internal void SetInlineEditorTextForTesting(string value) { if (_inlineEditor != null) _inlineEditor.Text = value; }
        internal void CommitPendingEdit() { FinishInlineEdit(true); }
        internal void CommitInlineEditForTesting() { CommitPendingEdit(); }

        public void RefreshDocument()
        {
            if (_document == null) return;
            _document = GraphSerialization.Normalize(_document);
            RebuildIndexes();
            EnsureSelection();
            Invalidate();
        }

        public void RestoreDocumentPreservingView(GraphDocument value)
        {
            FinishInlineEdit(false);
            _document = GraphSerialization.Normalize(value);
            RebuildIndexes();
            ClearSelection(false);
            Invalidate();
        }

        public void SelectEntity(string type, string id)
        {
            if (type == "node" && _nodes.ContainsKey(id))
            {
                _selectedNodes.Clear(); _selectedGroups.Clear();
                _selectedNodes.Add(id);
                _selectedType = "node";
                _selectedId = id;
            }
            else if (type == "group" && _groups.ContainsKey(id))
            {
                _selectedNodes.Clear(); _selectedGroups.Clear();
                _selectedGroups.Add(id);
                _selectedType = "group";
                _selectedId = id;
            }
            else if (type == "edge" && _document.edges.Any(delegate(GraphEdge edge) { return edge.id == id; }))
            {
                _selectedNodes.Clear(); _selectedGroups.Clear();
                _selectedType = "edge";
                _selectedId = id;
            }
            else ClearSelection(false);
            RaiseSelectionChanged();
            Invalidate();
        }

        public void SelectNodes(IEnumerable<string> ids)
        {
            _selectedNodes.Clear(); _selectedGroups.Clear();
            if (ids != null) foreach (string id in ids) if (!String.IsNullOrEmpty(id) && _nodes.ContainsKey(id)) _selectedNodes.Add(id);
            _selectedType = _selectedNodes.Count == 0 ? "" : "node";
            _selectedId = _selectedNodes.LastOrDefault() ?? "";
            RaiseSelectionChanged();
            Invalidate();
        }

        public void SelectObjects(IEnumerable<string> nodeIds, IEnumerable<string> groupIds)
        {
            _selectedNodes.Clear(); _selectedGroups.Clear();
            if (nodeIds != null) foreach (string id in nodeIds) if (!String.IsNullOrEmpty(id) && _nodes.ContainsKey(id)) _selectedNodes.Add(id);
            if (groupIds != null) foreach (string id in groupIds) if (!String.IsNullOrEmpty(id) && _groups.ContainsKey(id)) _selectedGroups.Add(id);
            UpdateSelectionIdentity();
            RaiseSelectionChanged();
            Invalidate();
        }

        public void ClearSelection() { ClearSelection(true); }

        private void ClearSelection(bool notify)
        {
            _selectedNodes.Clear(); _selectedGroups.Clear();
            _selectedType = "";
            _selectedId = "";
            if (notify) RaiseSelectionChanged();
            Invalidate();
        }

        private void UpdateSelectionIdentity()
        {
            if (_selectedNodes.Count > 0 && _selectedGroups.Count > 0) { _selectedType = "mixed"; _selectedId = ""; }
            else if (_selectedNodes.Count > 0) { _selectedType = "node"; _selectedId = _selectedNodes.LastOrDefault() ?? ""; }
            else if (_selectedGroups.Count > 0) { _selectedType = "group"; _selectedId = _selectedGroups.LastOrDefault() ?? ""; }
            else { _selectedType = ""; _selectedId = ""; }
        }

        public void FitToView()
        {
            if (_document == null || ClientSize.Width < 20 || ClientSize.Height < 20) return;
            RectangleF bounds = ContentBounds(30f);
            if (!IsFiniteRectangle(bounds)) bounds = DefaultContentBounds();
            _zoom = CalculateFitZoom(bounds, MaximumFitZoom);
            double centerX = (double)bounds.Left + bounds.Width / 2d, centerY = (double)bounds.Top + bounds.Height / 2d;
            _offsetX = ClampViewOffset(ClientSize.Width / 2d - centerX * _zoom);
            _offsetY = ClampViewOffset(ClientSize.Height / 2d - centerY * _zoom);
            _fitToViewActive = true;
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        private float CalculateFitZoom(RectangleF bounds, float maximum)
        {
            double availableWidth = Math.Max(100, ClientSize.Width - 40);
            double availableHeight = Math.Max(100, ClientSize.Height - 40);
            double width = IsFinite(bounds.Width) && bounds.Width > 0 ? bounds.Width : 1d;
            double height = IsFinite(bounds.Height) && bounds.Height > 0 ? bounds.Height : 1d;
            double safeMaximum = IsFinite(maximum) && maximum > 0 ? maximum : MaximumFitZoom;
            double fit = Math.Min(availableWidth / width, availableHeight / height);
            if (Double.IsNaN(fit) || Double.IsInfinity(fit) || fit <= 0) fit = MinimumSafeZoom;
            return (float)Math.Max(MinimumSafeZoom, Math.Min(safeMaximum, fit));
        }

        private float ManualZoomFloor()
        {
            if (_document == null || ClientSize.Width < 20 || ClientSize.Height < 20) return ManualMinimumZoom;
            return Math.Min(ManualMinimumZoom, CalculateFitZoom(ContentBounds(30f), ManualMinimumZoom));
        }

        public void ZoomBy(float factor)
        {
            ZoomAt(new Point(ClientSize.Width / 2, ClientSize.Height / 2), factor);
        }

        public void CancelActiveGesture()
        {
            if (_gesture == CanvasGesture.None) return;
            if (_gestureMoved && _gestureBeforeJson != null && _gesture != CanvasGesture.Pan && _gesture != CanvasGesture.SelectBox)
            {
                _document = GraphSerialization.Deserialize(_gestureBeforeJson);
                RebuildIndexes();
            }
            EndGesture();
            Invalidate();
            RaiseSelectionChanged();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_document != null && _fitToViewActive) FitToView();
            PositionInlineEditor();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Escape) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && _gesture != CanvasGesture.None)
            {
                CancelActiveGesture();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            FinishInlineEdit(true);
            base.OnMouseWheel(e);
            ZoomAt(e.Location, e.Delta > 0 ? 1.12f : 0.89f);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (!EditMode || e.Button != MouseButtons.Left || _document == null) return;
            PointF world = ScreenToWorld(e.Location);
            GraphNode node = HitNode(world);
            if (node != null)
            {
                CancelActiveGesture();
                if (!_nodes.TryGetValue(node.id, out node)) return;
                RectangleF typeArea = NodeTypeEditArea(node), labelArea = NodeLabelEditArea(node);
                if (typeArea.Contains(world)) BeginInlineEdit("node", node.id, "type", node.type, typeArea);
                else if (labelArea.Contains(world)) BeginInlineEdit("node", node.id, "label", node.label, labelArea);
                return;
            }
            GraphEdge edge = HitEdge(world);
            if (edge != null)
            {
                string edgeId = edge.id; CancelActiveGesture();
                edge = _document.edges.FirstOrDefault(delegate(GraphEdge item) { return item.id == edgeId; }); if (edge == null) return;
                SelectEntity("edge", edge.id);
                BeginInlineEdit("edge", edge.id, "label", edge.label, EdgeLabelEditArea(edge)); return;
            }
            GraphGroup group = HitGroup(world);
            if (group != null && GroupLabelEditArea(group).Contains(world))
            {
                string groupId = group.id; CancelActiveGesture();
                if (!_groups.TryGetValue(groupId, out group)) return;
                BeginInlineEdit("group", group.id, "label", group.label, GroupLabelEditArea(group)); return;
            }
            CancelActiveGesture();
            if (BlankDoubleClicked != null) BlankDoubleClicked(this, new CanvasPointEventArgs(world));
        }

        private void ZoomAt(Point screenPoint, float factor)
        {
            if (Single.IsNaN(factor) || Single.IsInfinity(factor) || factor <= 0) return;
            PointF world = ScreenToWorld(screenPoint);
            double requested = (double)SafeZoom(_zoom) * factor;
            float next = (float)Math.Max(ManualZoomFloor(), Math.Min(MaximumZoom, requested));
            _zoom = SafeZoom(next);
            _offsetX = ClampViewOffset(screenPoint.X - (double)world.X * _zoom);
            _offsetY = ClampViewOffset(screenPoint.Y - (double)world.Y * _zoom);
            _fitToViewActive = false;
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            FinishInlineEdit(true);
            Focus();
            if (_document == null) return;
            if (e.Button == MouseButtons.Right)
            {
                CancelActiveGesture();
                _mouseDown = e.Location;
                _lastMouse = e.Location;
                _worldDown = ScreenToWorld(e.Location);
                _worldCurrent = _worldDown;
                _gestureMoved = false;
                _gesture = CanvasGesture.Pan;
                _rightButtonPan = true;
                Cursor = Cursors.Cross;
                Capture = true;
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            _mouseDown = e.Location;
            _lastMouse = e.Location;
            _worldDown = ScreenToWorld(e.Location);
            _worldCurrent = _worldDown;
            _gestureMoved = false;
            _gestureBeforeJson = null;
            _collapseMultiOnClick = false;
            _controlToggleOnClick = false;
            _controlNodeWasSelected = false;

            if (EditMode)
            {
                string resize;
                if (_selectedType == "group" && _selectedGroups.Count == 1 && HitResizeHandle(_worldDown, out resize))
                {
                    _gesture = CanvasGesture.ResizeGroup;
                    _gestureEntityType = "group";
                    _gestureEntityId = _selectedId;
                    _resizeHandle = resize;
                    GraphGroup selectedGroup = _groups[_selectedId];
                    _groupStart = RectOf(selectedGroup);
                    Cursor = ResizeCursor(resize);
                    Capture = true;
                    return;
                }
            }

            GraphNode node = HitNode(_worldDown);
            if (node != null)
            {
                if (!EditMode) { SelectEntity("node", node.id); return; }
                bool control = (ModifierKeys & Keys.Control) == Keys.Control;
                bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
                if (control)
                {
                    _controlNodeWasSelected = _selectedNodes.Contains(node.id);
                    if (!_controlNodeWasSelected) _selectedNodes.Add(node.id);
                    UpdateSelectionIdentity();
                    RaiseSelectionChanged();
                    _controlToggleOnClick = true;
                    _gesture = CanvasGesture.MoveNodes;
                    BeginNodeMove(node.id);
                    Capture = true;
                    Invalidate();
                    return;
                }
                if (shift)
                {
                    if (_selectedNodes.Contains(node.id)) _selectedNodes.Remove(node.id); else _selectedNodes.Add(node.id);
                    UpdateSelectionIdentity();
                    RaiseSelectionChanged(); Invalidate(); return;
                }
                if (_selectedNodes.Contains(node.id))
                {
                    _gesture = SelectionCount > 1 ? CanvasGesture.MoveSelection : CanvasGesture.MoveNodes;
                    _collapseMultiOnClick = SelectionCount > 1;
                    if (_gesture == CanvasGesture.MoveSelection) BeginSelectionMove("node", node.id); else BeginNodeMove(node.id);
                }
                else
                {
                    _selectedNodes.Clear(); _selectedGroups.Clear();
                    _selectedNodes.Add(node.id);
                    _selectedType = "node";
                    _selectedId = node.id;
                    RaiseSelectionChanged();
                    _gesture = CanvasGesture.Link;
                    _gestureEntityType = "node";
                    _gestureEntityId = node.id;
                    _sourceSide = NearestSide(RectOf(node), _worldDown);
                    _sourceSideLocked = false;
                }
                Capture = true;
                Invalidate();
                return;
            }

            GraphEdge edgeHit = HitEdge(_worldDown);
            if (edgeHit != null)
            {
                SelectEntity("edge", edgeHit.id);
                return;
            }

            GraphGroup group = HitGroup(_worldDown);
            if (group != null)
            {
                if (!EditMode) { SelectEntity("group", group.id); return; }
                bool alreadySelected = _selectedGroups.Contains(group.id);
                if (!alreadySelected)
                {
                    _selectedNodes.Clear(); _selectedGroups.Clear(); _selectedGroups.Add(group.id);
                    _selectedType = "group";
                    _selectedId = group.id;
                    RaiseSelectionChanged();
                }
                _gestureEntityType = "group";
                _gestureEntityId = group.id;
                if (alreadySelected)
                {
                    if (SelectionCount > 1)
                    {
                        _gesture = CanvasGesture.MoveSelection;
                        BeginSelectionMove("group", group.id);
                    }
                    else
                    {
                        _gesture = CanvasGesture.MoveGroup;
                        BeginGroupMove(group.id);
                    }
                }
                else
                {
                    _gesture = CanvasGesture.Link;
                    _sourceSide = NearestSide(RectOf(group), _worldDown);
                    _sourceSideLocked = false;
                }
                Capture = true;
                Invalidate();
                return;
            }

            if (EditMode || (ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                _gesture = CanvasGesture.SelectBox;
                _selectionScreen = new Rectangle(e.X, e.Y, 0, 0);
            }
            else _gesture = CanvasGesture.Pan;
            Capture = true;
        }

        private void BeginNodeMove(string primaryId)
        {
            _gestureEntityType = "node";
            _gestureEntityId = primaryId;
            _alignmentGuideX = Single.NaN; _alignmentGuideY = Single.NaN;
            _horizontalSpacingHint = null; _verticalSpacingHint = null;
            _nodeStarts.Clear();
            foreach (string id in _selectedNodes)
            {
                GraphNode node;
                if (_nodes.TryGetValue(id, out node)) _nodeStarts[id] = new PointF(node.x, node.y);
            }
            CacheAlignmentCandidates();
        }

        private void BeginSelectionMove(string primaryType, string primaryId)
        {
            _gestureEntityType = primaryType; _gestureEntityId = primaryId;
            _alignmentGuideX = Single.NaN; _alignmentGuideY = Single.NaN;
            _horizontalSpacingHint = null; _verticalSpacingHint = null;
            _nodeStarts.Clear(); _groupStarts.Clear();
            HashSet<string> movingGroupIds = new HashSet<string>(_selectedGroups, StringComparer.Ordinal);
            foreach (GraphGroup child in _document.groups)
                if (child.groups != null && child.groups.Any(delegate(string parentId) { return _selectedGroups.Contains(parentId); })) movingGroupIds.Add(child.id);
            foreach (string id in movingGroupIds)
            {
                GraphGroup group;
                if (!_groups.TryGetValue(id, out group)) continue;
                _groupStarts[id] = RectOf(group);
            }
            foreach (GraphNode member in _document.nodes.Where(delegate(GraphNode item) { return item.groups != null && item.groups.Any(delegate(string groupId) { return _selectedGroups.Contains(groupId); }); }))
                _nodeStarts[member.id] = new PointF(member.x, member.y);
            foreach (string id in _selectedNodes)
            {
                GraphNode node;
                if (_nodes.TryGetValue(id, out node)) _nodeStarts[id] = new PointF(node.x, node.y);
            }
            CacheAlignmentCandidates();
        }

        private void ApplySelectionMove(float dx, float dy, bool freePlacement)
        {
            if (_nodeStarts.Count == 0 && _groupStarts.Count == 0) return;
            List<RectangleF> starts = new List<RectangleF>();
            starts.AddRange(_groupStarts.Values);
            foreach (KeyValuePair<string, PointF> pair in _nodeStarts)
            {
                GraphNode node = _nodes[pair.Key];
                starts.Add(new RectangleF(pair.Value.X, pair.Value.Y, node.w, node.h));
            }
            float left = starts.Min(delegate(RectangleF rect) { return rect.Left; });
            float top = starts.Min(delegate(RectangleF rect) { return rect.Top; });
            float right = starts.Max(delegate(RectangleF rect) { return rect.Right; });
            float bottom = starts.Max(delegate(RectangleF rect) { return rect.Bottom; });
            if (freePlacement)
            {
                _alignmentGuideX = Single.NaN; _alignmentGuideY = Single.NaN;
                _horizontalSpacingHint = null; _verticalSpacingHint = null;
            }
            else
            {
                dx = Snap(dx); dy = Snap(dy);
                ApplyNodeAlignment(left, top, right, bottom, ref dx, ref dy);
                ApplyEqualSpacing(left, top, right, bottom, ref dx, ref dy);
            }
            foreach (KeyValuePair<string, RectangleF> pair in _groupStarts)
            {
                GraphGroup group = _groups[pair.Key];
                group.x = ClampWorldCoordinate((double)pair.Value.X + dx);
                group.y = ClampWorldCoordinate((double)pair.Value.Y + dy);
            }
            foreach (KeyValuePair<string, PointF> pair in _nodeStarts)
            {
                GraphNode node = _nodes[pair.Key];
                node.x = ClampWorldCoordinate((double)pair.Value.X + dx);
                node.y = ClampWorldCoordinate((double)pair.Value.Y + dy);
            }
        }

        internal void MoveSelectedObjectsForTesting(float dx, float dy, bool freePlacement)
        {
            if (SelectionCount <= 1 || (_selectedNodes.Count == 0 && _selectedGroups.Count == 0)) return;
            string primaryType = _selectedNodes.Count > 0 ? "node" : "group";
            string primaryId = _selectedNodes.Count > 0 ? _selectedNodes.First() : _selectedGroups.First();
            BeginSelectionMove(primaryType, primaryId); ApplySelectionMove(dx, dy, freePlacement); RefreshAutomaticMemberships();
        }

        private void BeginGroupMove(string groupId)
        {
            _gestureEntityType = "group"; _gestureEntityId = groupId;
            _groupStart = RectOf(_groups[groupId]);
            _alignmentGuideX = Single.NaN; _alignmentGuideY = Single.NaN;
            _horizontalSpacingHint = null; _verticalSpacingHint = null;
            _nodeStarts.Clear(); _groupStarts.Clear();
            foreach (GraphGroup child in _document.groups)
                if (child.id != groupId && child.groups != null && child.groups.Contains(groupId)) _groupStarts[child.id] = RectOf(child);
            foreach (GraphNode member in _document.nodes)
                if (member.groups != null && member.groups.Contains(groupId)) _nodeStarts[member.id] = new PointF(member.x, member.y);
            CacheAlignmentCandidates();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_document == null) return;
            if (_gesture == CanvasGesture.None) { UpdateHoverCursor(e.Location); return; }
            _worldCurrent = ScreenToWorld(e.Location);
            float screenDistance = Distance(e.Location, _mouseDown);
            if (screenDistance >= 7f) _gestureMoved = true;

            if (_gesture == CanvasGesture.Pan)
            {
                int deltaX = e.X - _lastMouse.X, deltaY = e.Y - _lastMouse.Y;
                _offsetX = ClampViewOffset((double)_offsetX + deltaX);
                _offsetY = ClampViewOffset((double)_offsetY + deltaY);
                if (deltaX != 0 || deltaY != 0) _fitToViewActive = false;
                _lastMouse = e.Location;
            }
            else if (_gesture == CanvasGesture.SelectBox)
            {
                _selectionScreen = NormalizeRectangle(_mouseDown, e.Location);
            }
            else if (_gesture == CanvasGesture.MoveNodes && _gestureMoved)
            {
                EnsureGestureSnapshot();
                ApplyNodeMove(_worldCurrent.X - _worldDown.X, _worldCurrent.Y - _worldDown.Y, IsFreePlacement(ModifierKeys));
            }
            else if (_gesture == CanvasGesture.MoveGroup && _gestureMoved)
            {
                EnsureGestureSnapshot();
                ApplyGroupMove(_worldCurrent.X - _worldDown.X, _worldCurrent.Y - _worldDown.Y, IsFreePlacement(ModifierKeys));
            }
            else if (_gesture == CanvasGesture.MoveSelection && _gestureMoved)
            {
                EnsureGestureSnapshot();
                ApplySelectionMove(_worldCurrent.X - _worldDown.X, _worldCurrent.Y - _worldDown.Y, IsFreePlacement(ModifierKeys));
            }
            else if (_gesture == CanvasGesture.ResizeGroup && _gestureMoved)
            {
                EnsureGestureSnapshot();
                ApplyGroupResize(_worldCurrent.X - _worldDown.X, _worldCurrent.Y - _worldDown.Y);
            }
            else if (_gesture == CanvasGesture.Link && _gestureMoved && !_sourceSideLocked)
                _sourceSide = DirectionSide(_worldDown, _worldCurrent, _sourceSide);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_gesture == CanvasGesture.None) Cursor = Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_gesture == CanvasGesture.None) return;
            if ((_rightButtonPan && e.Button != MouseButtons.Right) || (!_rightButtonPan && e.Button != MouseButtons.Left)) return;
            _worldCurrent = ScreenToWorld(e.Location);
            CanvasGesture completed = _gesture;
            string beforeJson = _gestureBeforeJson;
            bool moved = _gestureMoved;

            if (completed == CanvasGesture.SelectBox && moved) ApplySelectionBox();
            else if (completed == CanvasGesture.SelectBox && !moved) ClearSelection();
            else if (completed == CanvasGesture.Pan && !moved) ClearSelection();
            else if (completed == CanvasGesture.MoveNodes)
            {
                if (moved)
                {
                    RefreshAutomaticMemberships();
                    RepairAutomaticSides();
                    CommitGesture(beforeJson, _selectedNodes.Count > 1 ? "多个节点位置已保存" : "节点位置已保存");
                }
                else if (_controlToggleOnClick)
                {
                    if (_controlNodeWasSelected) _selectedNodes.Remove(_gestureEntityId);
                    UpdateSelectionIdentity();
                    RaiseSelectionChanged();
                }
                else if (_collapseMultiOnClick)
                {
                    _selectedNodes.Clear(); _selectedGroups.Clear();
                    _selectedNodes.Add(_gestureEntityId);
                    _selectedType = "node";
                    _selectedId = _gestureEntityId;
                    RaiseSelectionChanged();
                }
            }
            else if (completed == CanvasGesture.MoveSelection)
            {
                if (moved)
                {
                    RefreshAutomaticMemberships();
                    RepairAutomaticSides();
                    CommitGesture(beforeJson, "多个节点和分组位置已保存");
                }
                else if (_collapseMultiOnClick)
                {
                    if (_gestureEntityType == "node") SelectEntity("node", _gestureEntityId);
                    else SelectEntity("group", _gestureEntityId);
                }
            }
            else if (completed == CanvasGesture.MoveGroup && moved) { RefreshAutomaticMemberships(); RepairAutomaticSides(); CommitGesture(beforeJson, "分组及组内节点位置已保存"); }
            else if (completed == CanvasGesture.ResizeGroup && moved) { RefreshAutomaticMemberships(); RepairAutomaticSides(); CommitGesture(beforeJson, "分组范围已保存"); }
            else if (completed == CanvasGesture.Link && moved) CompleteLink();

            EndGesture();
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (_gesture != CanvasGesture.None && !Capture) CancelActiveGesture();
        }

        private void EndGesture()
        {
            _gesture = CanvasGesture.None;
            _gestureBeforeJson = null;
            _gestureMoved = false;
            _resizeHandle = "";
            _sourceSide = "";
            _sourceSideLocked = false;
            _rightButtonPan = false;
            _controlToggleOnClick = false; _controlNodeWasSelected = false;
            _alignmentGuideX = Single.NaN; _alignmentGuideY = Single.NaN;
            _horizontalSpacingHint = null; _verticalSpacingHint = null;
            _nodeStarts.Clear();
            _groupStarts.Clear();
            _alignmentCandidates.Clear();
            _gestureEntityType = "";
            _gestureEntityId = "";
            _collapseMultiOnClick = false;
            ClearGestureFocusCache();
            Cursor = Cursors.Default;
            _selectionScreen = Rectangle.Empty;
            if (Capture) Capture = false;
        }

        private void BeginInlineEdit(string entityType, string entityId, string field, string value, RectangleF worldArea)
        {
            FinishInlineEdit(true);
            _inlineEditType = entityType; _inlineEditId = entityId; _inlineEditField = field;
            TextBox editor = new TextBox(); _inlineEditor = editor;
            editor.Text = value ?? ""; editor.MaxLength = field == "type" ? 30 : 40; editor.BorderStyle = BorderStyle.FixedSingle;
            editor.TextAlign = field == "label" && (entityType == "node" || entityType == "edge") ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            _inlineEditorFont = new Font(Font.FontFamily, field == "label" && entityType == "node" ? 10f : 9f, field == "label" && entityType == "node" ? FontStyle.Bold : FontStyle.Regular);
            editor.Font = _inlineEditorFont;
            editor.Tag = worldArea;
            editor.BackColor = _darkTheme ? NativeTheme.DarkInput : Color.White;
            editor.ForeColor = _darkTheme ? NativeTheme.DarkText : NativeTheme.LightText;
            editor.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; FinishInlineEdit(true); }
                else if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; FinishInlineEdit(false); }
            };
            editor.LostFocus += delegate { FinishInlineEdit(true); };
            Controls.Add(editor); PositionInlineEditor(); editor.BringToFront(); editor.SelectAll(); editor.Focus();
        }

        private void PositionInlineEditor()
        {
            if (_inlineEditor == null || !(_inlineEditor.Tag is RectangleF)) return;
            RectangleF world = (RectangleF)_inlineEditor.Tag;
            if (!IsFiniteRectangle(world)) return;
            float zoom = SafeZoom(_zoom), offsetX = ClampViewOffset(_offsetX), offsetY = ClampViewOffset(_offsetY);
            int x = (int)Math.Round(world.X * zoom + offsetX), y = (int)Math.Round(world.Y * zoom + offsetY);
            int width = Math.Max(80, (int)Math.Round(world.Width * zoom)), height = Math.Max(25, (int)Math.Round(world.Height * zoom));
            _inlineEditor.SetBounds(x, y, width, height);
        }

        private void FinishInlineEdit(bool commit)
        {
            if (_inlineEditor == null || _finishingInlineEdit) return;
            _finishingInlineEdit = true;
            TextBox editor = _inlineEditor; string value = (editor.Text ?? "").Trim();
            string entityType = _inlineEditType, entityId = _inlineEditId, field = _inlineEditField;
            _inlineEditor = null; _inlineEditType = ""; _inlineEditId = ""; _inlineEditField = "";
            editor.Dispose();
            DisposeInlineEditorFont();
            if (commit && _document != null)
            {
                string beforeJson = null; bool changed = false;
                if (entityType == "node")
                {
                    GraphNode node;
                    if (_nodes.TryGetValue(entityId, out node))
                    {
                        if (field == "type") { value = value.Length == 0 ? "节点类型" : value; if (node.type != value) { beforeJson = GraphSerialization.Serialize(_document, false); node.type = value; changed = true; } }
                        else { value = value.Length == 0 ? "未命名节点" : value; if (node.label != value) { beforeJson = GraphSerialization.Serialize(_document, false); node.label = value; changed = true; } }
                    }
                }
                else if (entityType == "group")
                {
                    GraphGroup group;
                    value = value.Length == 0 ? "未命名分组" : value;
                    if (_groups.TryGetValue(entityId, out group) && group.label != value) { beforeJson = GraphSerialization.Serialize(_document, false); group.label = value; changed = true; }
                }
                else if (entityType == "edge")
                {
                    GraphEdge edge = _document.edges.FirstOrDefault(delegate(GraphEdge item) { return item.id == entityId; });
                    if (edge != null && edge.label != value) { beforeJson = GraphSerialization.Serialize(_document, false); edge.label = value; changed = true; }
                }
                if (changed) CommitGesture(beforeJson, field == "type" ? "节点类型已修改" : entityType == "group" ? "分组名称已修改" : entityType == "edge" ? "关系名称已修改" : "节点名称已修改");
            }
            _finishingInlineEdit = false;
            Focus(); Invalidate();
        }

        private void CommitGesture(string beforeJson, string message)
        {
            _document.meta.updatedAt = DateTime.UtcNow.ToString("o");
            if (GraphCommitted != null) GraphCommitted(this, new GraphCommitEventArgs(beforeJson, message));
            RaiseSelectionChanged();
        }

        private void EnsureGestureSnapshot()
        {
            if (_gestureBeforeJson == null && _document != null)
                _gestureBeforeJson = GraphSerialization.Serialize(_document, false);
        }

        private void CompleteLink()
        {
            EndpointItem target = HitEndpoint(_worldCurrent, _gestureEntityType, _gestureEntityId);
            if (target == null) return;
            string sourceKey = GraphSerialization.EndpointKey(_gestureEntityType, _gestureEntityId);
            if (sourceKey == target.Key) return;
            bool duplicate = _document.edges.Any(delegate(GraphEdge edge)
            {
                return GraphSerialization.EndpointKey(edge.sourceType, edge.source) == sourceKey &&
                    GraphSerialization.EndpointKey(edge.targetType, edge.target) == target.Key;
            });
            if (duplicate)
            {
                GraphEdge existing = _document.edges.First(delegate(GraphEdge edge)
                {
                    return GraphSerialization.EndpointKey(edge.sourceType, edge.source) == sourceKey &&
                        GraphSerialization.EndpointKey(edge.targetType, edge.target) == target.Key;
                });
                SelectEntity("edge", existing.id);
                return;
            }
            EnsureGestureSnapshot();
            HashSet<string> ids = new HashSet<string>(_document.edges.Select(delegate(GraphEdge edge) { return edge.id; }));
            RectangleF sourceRect = GetEndpointRect(_gestureEntityType, _gestureEntityId);
            RectangleF targetRect = GetEndpointRect(target.Type, target.Id);
            string sourceSide = ConnectionSide(sourceRect, targetRect);
            string targetSide = ConnectionSide(targetRect, sourceRect);
            GraphEdge created = new GraphEdge
            {
                id = GraphSerialization.UniqueId("edge", ids),
                sourceType = _gestureEntityType,
                source = _gestureEntityId,
                targetType = target.Type,
                target = target.Id,
                sourceSide = sourceSide,
                targetSide = targetSide,
                lineType = GraphSerialization.NormalizeLineType(NewLineType),
                category = _document.settings.relationTypes[0].id,
                label = ""
            };
            _document.edges.Add(created);
            _selectedNodes.Clear(); _selectedGroups.Clear();
            _selectedType = "edge";
            _selectedId = created.id;
            CommitGesture(_gestureBeforeJson, "关系已创建");
        }

        private void ApplyNodeMove(float dx, float dy, bool freePlacement)
        {
            if (_nodeStarts.Count == 0) return;
            float left = _nodeStarts.Min(delegate(KeyValuePair<string, PointF> pair) { return pair.Value.X; });
            float top = _nodeStarts.Min(delegate(KeyValuePair<string, PointF> pair) { return pair.Value.Y; });
            float right = _nodeStarts.Max(delegate(KeyValuePair<string, PointF> pair) { return pair.Value.X + _nodes[pair.Key].w; });
            float bottom = _nodeStarts.Max(delegate(KeyValuePair<string, PointF> pair) { return pair.Value.Y + _nodes[pair.Key].h; });
            if (freePlacement)
            {
                _alignmentGuideX = Single.NaN; _alignmentGuideY = Single.NaN;
                _horizontalSpacingHint = null; _verticalSpacingHint = null;
            }
            else
            {
                dx = Snap(dx); dy = Snap(dy);
                ApplyNodeAlignment(left, top, right, bottom, ref dx, ref dy);
                ApplyEqualSpacing(left, top, right, bottom, ref dx, ref dy);
            }
            foreach (KeyValuePair<string, PointF> pair in _nodeStarts)
            {
                GraphNode node = _nodes[pair.Key];
                node.x = ClampWorldCoordinate((double)pair.Value.X + dx);
                node.y = ClampWorldCoordinate((double)pair.Value.Y + dy);
            }
        }

        internal void MoveSelectedNodesForTesting(float dx, float dy, bool freePlacement)
        {
            if (_selectedType != "node" || _selectedNodes.Count == 0) return;
            BeginNodeMove(_selectedId); ApplyNodeMove(dx, dy, freePlacement); RefreshAutomaticMemberships();
        }

        private static bool IsFreePlacement(Keys modifierKeys) { return (modifierKeys & Keys.Control) == Keys.Control; }
        internal static bool IsFreePlacementForTesting(Keys modifierKeys) { return IsFreePlacement(modifierKeys); }

        private void ApplyEqualSpacing(float left, float top, float right, float bottom, ref float dx, ref float dy)
        {
            _horizontalSpacingHint = null; _verticalSpacingHint = null;
            RectangleF start = RectangleF.FromLTRB(left, top, right, bottom);
            float threshold = 10f / Math.Max(.1f, SafeZoom(_zoom));
            EqualSpacingHint horizontal = FindEqualSpacing(start, dx, dy, true, threshold);
            if (horizontal != null)
            {
                dx = horizontal.TargetDelta; _alignmentGuideX = Single.NaN;
            }
            EqualSpacingHint vertical = FindEqualSpacing(start, dx, dy, false, threshold);
            if (vertical != null)
            {
                dy = vertical.TargetDelta; _alignmentGuideY = Single.NaN;
            }
            RectangleF finalMoving = OffsetRectangle(start, dx, dy);
            if (horizontal != null && Math.Abs(dx - horizontal.TargetDelta) < .1f) _horizontalSpacingHint = FinalizeSpacingHint(horizontal, finalMoving);
            if (vertical != null && Math.Abs(dy - vertical.TargetDelta) < .1f) _verticalSpacingHint = FinalizeSpacingHint(vertical, finalMoving);
        }

        private EqualSpacingHint FindEqualSpacing(RectangleF movingStart, float dx, float dy, bool horizontal, float threshold)
        {
            RectangleF moving = OffsetRectangle(movingStart, dx, dy);
            RectangleF nearestBefore = RectangleF.Empty, secondBefore = RectangleF.Empty;
            RectangleF nearestAfter = RectangleF.Empty, secondAfter = RectangleF.Empty;
            float nearestBeforeEnd = Single.NegativeInfinity, secondBeforeEnd = Single.NegativeInfinity;
            float nearestAfterStart = Single.PositiveInfinity, secondAfterStart = Single.PositiveInfinity;
            bool hasNearestBefore = false, hasSecondBefore = false, hasNearestAfter = false, hasSecondAfter = false;
            foreach (RectangleF rect in AlignmentCandidates())
            {
                if (!OrthogonallyCompatible(moving, rect, horizontal)) continue;
                float end = AxisEnd(rect, horizontal);
                if (end <= AxisStart(moving, horizontal) + threshold)
                {
                    if (!hasNearestBefore || end > nearestBeforeEnd)
                    {
                        secondBefore = nearestBefore; secondBeforeEnd = nearestBeforeEnd; hasSecondBefore = hasNearestBefore;
                        nearestBefore = rect; nearestBeforeEnd = end; hasNearestBefore = true;
                    }
                    else if (!hasSecondBefore || end > secondBeforeEnd)
                    {
                        secondBefore = rect; secondBeforeEnd = end; hasSecondBefore = true;
                    }
                }
                float start = AxisStart(rect, horizontal);
                if (start >= AxisEnd(moving, horizontal) - threshold)
                {
                    if (!hasNearestAfter || start < nearestAfterStart)
                    {
                        secondAfter = nearestAfter; secondAfterStart = nearestAfterStart; hasSecondAfter = hasNearestAfter;
                        nearestAfter = rect; nearestAfterStart = start; hasNearestAfter = true;
                    }
                    else if (!hasSecondAfter || start < secondAfterStart)
                    {
                        secondAfter = rect; secondAfterStart = start; hasSecondAfter = true;
                    }
                }
            }
            EqualSpacingHint best = null;
            if (hasSecondBefore) ConsiderSpacing(ref best, secondBefore, nearestBefore, moving, 2, movingStart, dx, dy, horizontal, threshold);
            if (hasSecondAfter) ConsiderSpacing(ref best, moving, nearestAfter, secondAfter, 0, movingStart, dx, dy, horizontal, threshold);
            if (hasNearestBefore && hasNearestAfter) ConsiderSpacing(ref best, nearestBefore, moving, nearestAfter, 1, movingStart, dx, dy, horizontal, threshold);
            return best;
        }

        private void ConsiderSpacing(ref EqualSpacingHint best, RectangleF first, RectangleF middle, RectangleF last, int movingIndex, RectangleF movingStart, float dx, float dy, bool horizontal, float threshold)
        {
            if (!OrthogonallyCompatible(first, middle, horizontal) || !OrthogonallyCompatible(middle, last, horizontal)) return;
            float length = AxisLength(movingStart, horizontal), targetStart, gap;
            if (movingIndex == 2)
            {
                gap = AxisStart(middle, horizontal) - AxisEnd(first, horizontal);
                targetStart = AxisEnd(middle, horizontal) + gap;
            }
            else if (movingIndex == 0)
            {
                gap = AxisStart(last, horizontal) - AxisEnd(middle, horizontal);
                targetStart = AxisStart(middle, horizontal) - gap - length;
            }
            else
            {
                float available = AxisStart(last, horizontal) - AxisEnd(first, horizontal) - length;
                gap = available / 2f; targetStart = AxisEnd(first, horizontal) + gap;
            }
            if (gap < 8f) return;
            float originalStart = AxisStart(movingStart, horizontal), targetDelta = targetStart - originalStart;
            float currentDelta = horizontal ? dx : dy, difference = Math.Abs(targetDelta - currentDelta);
            if (difference > threshold) return;
            if (best == null || difference < best.Difference)
                best = new EqualSpacingHint { Horizontal = horizontal, First = first, Middle = middle, Last = last, MovingIndex = movingIndex, TargetDelta = targetDelta, Gap = gap, Difference = difference };
        }

        private static EqualSpacingHint FinalizeSpacingHint(EqualSpacingHint source, RectangleF moving)
        {
            EqualSpacingHint hint = new EqualSpacingHint { Horizontal = source.Horizontal, First = source.First, Middle = source.Middle, Last = source.Last, MovingIndex = source.MovingIndex, TargetDelta = source.TargetDelta, Gap = source.Gap, Difference = source.Difference };
            if (hint.MovingIndex == 0) hint.First = moving; else if (hint.MovingIndex == 1) hint.Middle = moving; else hint.Last = moving;
            return hint;
        }

        private static bool OrthogonallyCompatible(RectangleF first, RectangleF second, bool horizontal)
        {
            float start = horizontal ? Math.Max(first.Top, second.Top) : Math.Max(first.Left, second.Left);
            float end = horizontal ? Math.Min(first.Bottom, second.Bottom) : Math.Min(first.Right, second.Right);
            float minimum = horizontal ? Math.Min(first.Height, second.Height) : Math.Min(first.Width, second.Width);
            return end - start >= Math.Min(18f, minimum * .35f);
        }

        private static float AxisStart(RectangleF rect, bool horizontal) { return horizontal ? rect.Left : rect.Top; }
        private static float AxisEnd(RectangleF rect, bool horizontal) { return horizontal ? rect.Right : rect.Bottom; }
        private static float AxisLength(RectangleF rect, bool horizontal) { return horizontal ? rect.Width : rect.Height; }
        private static RectangleF OffsetRectangle(RectangleF rect, float dx, float dy) { return new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height); }

        private void ApplyNodeAlignment(float left, float top, float right, float bottom, ref float dx, ref float dy)
        {
            _alignmentGuideX = Single.NaN; _alignmentGuideY = Single.NaN;
            float threshold = 10f / Math.Max(.1f, SafeZoom(_zoom)), aligned, guide;
            if (TryAlignNodeAxis(true, left, right, dx, threshold, out aligned, out guide)) { dx = aligned; _alignmentGuideX = guide; }
            if (TryAlignNodeAxis(false, top, bottom, dy, threshold, out aligned, out guide)) { dy = aligned; _alignmentGuideY = guide; }
        }

        private bool TryAlignNodeAxis(bool horizontal, float movingStart, float movingEnd, float delta, float threshold, out float alignedDelta, out float guide)
        {
            float movingFirst = movingStart + delta, movingMiddle = (movingStart + movingEnd) / 2f + delta, movingLast = movingEnd + delta;
            float bestDistance = threshold + .001f, bestAdjustment = 0, bestGuide = Single.NaN;
            foreach (RectangleF rect in AlignmentCandidates())
            {
                float start = horizontal ? rect.Left : rect.Top, end = horizontal ? rect.Right : rect.Bottom;
                ConsiderAlignment(start, movingFirst, ref bestDistance, ref bestAdjustment, ref bestGuide);
                ConsiderAlignment((start + end) / 2f, movingMiddle, ref bestDistance, ref bestAdjustment, ref bestGuide);
                ConsiderAlignment(end, movingLast, ref bestDistance, ref bestAdjustment, ref bestGuide);
            }
            if (Single.IsNaN(bestGuide)) { alignedDelta = delta; guide = Single.NaN; return false; }
            alignedDelta = delta + bestAdjustment; guide = bestGuide; return true;
        }

        private static void ConsiderAlignment(float target, float moving, ref float bestDistance, ref float bestAdjustment, ref float bestGuide)
        {
            float adjustment = target - moving, distance = Math.Abs(adjustment);
            if (distance >= bestDistance) return;
            bestDistance = distance; bestAdjustment = adjustment; bestGuide = target;
        }

        private void CacheAlignmentCandidates()
        {
            _alignmentCandidates.Clear();
            HashSet<string> movingGroups = new HashSet<string>(StringComparer.Ordinal);
            movingGroups.UnionWith(_groupStarts.Keys);
            if (_gestureEntityType == "group" && _gestureEntityId.Length > 0) movingGroups.Add(_gestureEntityId);
            if (_gestureEntityType == "node")
            {
                foreach (string id in _selectedNodes)
                {
                    GraphNode selected;
                    if (_nodes.TryGetValue(id, out selected) && selected.groups != null) movingGroups.UnionWith(selected.groups);
                }
            }
            foreach (GraphNode node in _document.nodes)
            {
                if (_nodeStarts.ContainsKey(node.id) || _selectedNodes.Contains(node.id)) continue;
                _alignmentCandidates.Add(RectOf(node));
            }
            foreach (GraphGroup group in _document.groups)
            {
                if (movingGroups.Contains(group.id)) continue;
                _alignmentCandidates.Add(RectOf(group));
            }
        }

        private List<RectangleF> AlignmentCandidates() { return _alignmentCandidates; }

        private void ApplyGroupMove(float dx, float dy, bool freePlacement)
        {
            GraphGroup group = _groups[_gestureEntityId];
            if (freePlacement)
            {
                _alignmentGuideX = Single.NaN; _alignmentGuideY = Single.NaN;
                _horizontalSpacingHint = null; _verticalSpacingHint = null;
            }
            else
            {
                dx = Snap(dx); dy = Snap(dy);
                ApplyNodeAlignment(_groupStart.Left, _groupStart.Top, _groupStart.Right, _groupStart.Bottom, ref dx, ref dy);
                ApplyEqualSpacing(_groupStart.Left, _groupStart.Top, _groupStart.Right, _groupStart.Bottom, ref dx, ref dy);
            }
            group.x = ClampWorldCoordinate((double)_groupStart.X + dx);
            group.y = ClampWorldCoordinate((double)_groupStart.Y + dy);
            foreach (KeyValuePair<string, RectangleF> pair in _groupStarts)
            {
                GraphGroup child = _groups[pair.Key];
                child.x = ClampWorldCoordinate((double)pair.Value.X + dx);
                child.y = ClampWorldCoordinate((double)pair.Value.Y + dy);
            }
            foreach (KeyValuePair<string, PointF> pair in _nodeStarts)
            {
                _nodes[pair.Key].x = ClampWorldCoordinate((double)pair.Value.X + dx);
                _nodes[pair.Key].y = ClampWorldCoordinate((double)pair.Value.Y + dy);
            }
        }

        internal void MoveSelectedGroupForTesting(float dx, float dy, bool freePlacement)
        {
            if (_selectedType != "group" || !_groups.ContainsKey(_selectedId)) return;
            BeginGroupMove(_selectedId); ApplyGroupMove(dx, dy, freePlacement); RefreshAutomaticMemberships();
        }

        private void ApplyGroupResize(float dx, float dy)
        {
            GraphGroup group = _groups[_gestureEntityId];
            float left = _groupStart.Left, top = _groupStart.Top, right = _groupStart.Right, bottom = _groupStart.Bottom;
            if (_resizeHandle.Contains("w")) left = Math.Min(right - 120, _groupStart.Left + Snap(dx));
            if (_resizeHandle.Contains("e")) right = Math.Max(left + 120, _groupStart.Right + Snap(dx));
            if (_resizeHandle.Contains("n")) top = Math.Min(bottom - 100, _groupStart.Top + Snap(dy));
            if (_resizeHandle.Contains("s")) bottom = Math.Max(top + 100, _groupStart.Bottom + Snap(dy));
            List<GraphNode> members = _document.nodes.Where(delegate(GraphNode node) { return node.groups != null && node.groups.Contains(group.id); }).ToList();
            List<GraphGroup> childGroups = _document.groups.Where(delegate(GraphGroup child) { return child.groups != null && child.groups.Contains(group.id); }).ToList();
            if (members.Count > 0)
            {
                left = Math.Min(left, members.Min(delegate(GraphNode node) { return node.x - 12; }));
                top = Math.Min(top, members.Min(delegate(GraphNode node) { return node.y - 38; }));
                right = Math.Max(right, members.Max(delegate(GraphNode node) { return node.x + node.w + 12; }));
                bottom = Math.Max(bottom, members.Max(delegate(GraphNode node) { return node.y + node.h + 12; }));
            }
            if (childGroups.Count > 0)
            {
                left = Math.Min(left, childGroups.Min(delegate(GraphGroup child) { return child.x - 12; }));
                top = Math.Min(top, childGroups.Min(delegate(GraphGroup child) { return child.y - 38; }));
                right = Math.Max(right, childGroups.Max(delegate(GraphGroup child) { return child.x + child.w + 12; }));
                bottom = Math.Max(bottom, childGroups.Max(delegate(GraphGroup child) { return child.y + child.h + 12; }));
            }
            group.x = ClampWorldCoordinate(left); group.y = ClampWorldCoordinate(top);
            group.w = ClampItemDimension((double)right - left, 120f);
            group.h = ClampItemDimension((double)bottom - top, 100f);
            _groupDrawOrder = null;
        }

        private void RefreshAutomaticMemberships()
        {
            GraphSerialization.UpdateAutomaticMemberships(_document);
        }

        private void ApplySelectionBox()
        {
            RectangleF world = ScreenRectangleToWorld(_selectionScreen);
            bool additive = (ModifierKeys & Keys.Control) == Keys.Control;
            if (!additive) { _selectedNodes.Clear(); _selectedGroups.Clear(); }
            foreach (GraphNode node in _document.nodes)
                if (world.IntersectsWith(RectOf(node))) _selectedNodes.Add(node.id);
            foreach (GraphGroup group in _document.groups)
                if (world.Contains(RectOf(group))) _selectedGroups.Add(group.id);
            UpdateSelectionIdentity();
            RaiseSelectionChanged();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(_darkTheme ? Color.FromArgb(25, 31, 38) : Color.White);
            if (_document == null) return;
            DrawGraph(e.Graphics, _zoom, _offsetX, _offsetY, true);
            if (_gesture == CanvasGesture.SelectBox && !_selectionScreen.IsEmpty)
            {
                using (Brush brush = new SolidBrush(Color.FromArgb(45, 54, 122, 246))) e.Graphics.FillRectangle(brush, _selectionScreen);
                using (Pen pen = new Pen(Color.FromArgb(54, 122, 246), 1.5f)) { pen.DashStyle = DashStyle.Dash; e.Graphics.DrawRectangle(pen, _selectionScreen); }
            }
        }

        public Bitmap ExportBitmap(int maximumDimension)
        {
            RectangleF bounds = ContentBounds(36f);
            if (!IsFiniteRectangle(bounds)) bounds = DefaultContentBounds();
            int safeMaximumDimension = Math.Max(1, maximumDimension);
            double area = Math.Max(1d, (double)bounds.Width * bounds.Height);
            float scale = Math.Min(2f, safeMaximumDimension / Math.Max(bounds.Width, bounds.Height));
            scale = SafeZoom(Math.Min(scale, (float)Math.Sqrt(32000000d / area)));
            int width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
            int height = Math.Max(1, (int)Math.Round(bounds.Height * scale));
            Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(_darkTheme ? Color.FromArgb(25, 31, 38) : Color.White);
                    DrawGraph(graphics, scale, -bounds.X * scale, -bounds.Y * scale, false);
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private void DrawGraph(Graphics graphics, float zoom, float offsetX, float offsetY, bool interactive)
        {
            zoom = SafeZoom(zoom);
            offsetX = ClampViewOffset(offsetX);
            offsetY = ClampViewOffset(offsetY);
            EnsureDrawingResources();
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                graphics.TranslateTransform(offsetX, offsetY);
                graphics.ScaleTransform(zoom, zoom);
                float unit = 1f / zoom;
                RectangleF visible = interactive ? VisibleWorldRectangle(zoom, offsetX, offsetY) : RectangleF.Empty;
                RectangleF paddedVisible = visible;
                if (interactive) paddedVisible.Inflate(16f * unit, 16f * unit);
                HashSet<string> focusEntities;
                HashSet<string> focusEdges;
                HashSet<string> inactiveSameNameNodes;
                bool focused = interactive && ShouldHighlightNeighbors();
                if (focused) GetFocusForDrawing(out focusEntities, out focusEdges, out inactiveSameNameNodes);
                else
                {
                    focusEntities = new HashSet<string>();
                    focusEdges = new HashSet<string>();
                    inactiveSameNameNodes = new HashSet<string>();
                }

                foreach (GraphGroup group in GroupsBackToFront())
                {
                    RectangleF groupRect = RectOf(group);
                    if (!IsFiniteRectangle(groupRect)) continue;
                    if (interactive && !paddedVisible.IntersectsWith(groupRect)) continue;
                    string key = GraphSerialization.EndpointKey("group", group.id);
                    bool selected = interactive && _selectedGroups.Contains(group.id);
                    bool related = focused && focusEntities.Contains(key) && !selected;
                    bool dim = focused && !focusEntities.Contains(key);
                    Color border = selected ? Color.FromArgb(76, 139, 245) : related ? Color.FromArgb(74, 190, 126) : (_darkTheme ? Color.FromArgb(101, 116, 130) : Color.FromArgb(155, 166, 178));
                    using (GraphicsPath shape = RoundRect(groupRect, 12))
                    {
                        using (Brush fill = new SolidBrush(Color.FromArgb(dim ? 18 : (_darkTheme ? 100 : 50), _darkTheme ? Color.FromArgb(48, 57, 67) : Color.FromArgb(215, 222, 230)))) graphics.FillPath(fill, shape);
                        using (Pen pen = new Pen(Color.FromArgb(dim ? 35 : 220, border), (selected || related ? 3f : 1.4f) * unit))
                        {
                            if (!selected && !related) pen.DashStyle = DashStyle.Dash;
                            graphics.DrawPath(pen, shape);
                        }
                    }
                    using (Brush text = new SolidBrush(Color.FromArgb(dim ? 40 : 230, _darkTheme ? Color.FromArgb(215, 224, 233) : Color.FromArgb(45, 57, 70))))
                        graphics.DrawString(group.label, Font, text, new RectangleF(group.x + 12, group.y + 5, Math.Max(1, group.w - 24), Math.Min(24, Math.Max(1, group.h - 10))), _entityHeaderFormat);
                }

                foreach (GraphEdge edge in _document.edges)
                {
                    bool selected = interactive && _selectedType == "edge" && _selectedId == edge.id;
                    bool related = focusEdges.Contains(edge.id);
                    if (focused && !related) continue;
                    RectangleF sourceRect = GetEndpointRect(edge.sourceType, edge.source), targetRect = GetEndpointRect(edge.targetType, edge.target);
                    if (!IsFiniteRectangle(sourceRect) || !IsFiniteRectangle(targetRect)) continue;
                    if (interactive && !EdgeEnvelopeIntersectsVisible(edge, paddedVisible)) continue;
                    CachedEdgeGeometry geometry = GetEdgeGeometry(edge);
                    if (interactive && !EdgeIntersectsVisible(edge, geometry.Bounds, paddedVisible, unit)) continue;
                    float alpha = focused && !related ? 0.08f : 1f;
                    Color color = EdgeSourceColor(edge);
                    using (Pen pen = new Pen(Color.FromArgb((int)(255 * alpha), color), (selected || related ? 3f : 1.8f) * unit))
                    {
                        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                        graphics.DrawPath(pen, geometry.Path);
                        DrawArrow(graphics, geometry, color, unit, alpha);
                        if (!String.IsNullOrWhiteSpace(edge.label)) DrawEdgeLabel(graphics, edge.label, geometry.LabelPoint, unit, focused && !related);
                    }
                }

                foreach (GraphNode node in _document.nodes)
                {
                    RectangleF nodeRect = RectOf(node);
                    if (!IsFiniteRectangle(nodeRect)) continue;
                    if (interactive && !paddedVisible.IntersectsWith(nodeRect)) continue;
                    string key = GraphSerialization.EndpointKey("node", node.id);
                    bool selected = interactive && _selectedNodes.Contains(node.id);
                    bool primary = interactive && _selectedType == "node" && _selectedId == node.id;
                    bool inactiveSameNameNode = interactive && inactiveSameNameNodes.Contains(node.id);
                    bool sameNameHighlighted = interactive && HasSelectedNodeName(node) && !inactiveSameNameNode;
                    bool related = focused && focusEntities.Contains(key) && !primary && !sameNameHighlighted && !inactiveSameNameNode;
                    bool dim = focused && (!focusEntities.Contains(key) || inactiveSameNameNode);
                    Color fillColor = _darkTheme ? DarkNodeColor(node.kind) : NodeColor(node.kind);
                    using (GraphicsPath shape = NodeShape(node))
                    {
                        using (Brush fill = new SolidBrush(Color.FromArgb(dim ? 30 : 235, fillColor))) graphics.FillPath(fill, shape);
                        Color border = primary || sameNameHighlighted ? Color.FromArgb(76, 139, 245) : related ? Color.FromArgb(74, 190, 126) : (_darkTheme ? Color.FromArgb(125, 140, 154) : Color.FromArgb(105, 119, 133));
                        using (Pen pen = new Pen(Color.FromArgb(dim ? 40 : 255, border), (primary ? 4f : sameNameHighlighted ? 3.4f : selected || related ? 3f : 1.4f) * unit)) graphics.DrawPath(pen, shape);
                    }
                    bool flowchart = _document.meta != null && _document.meta.diagramType == "flowchart";
                    if (!flowchart) using (Brush small = new SolidBrush(Color.FromArgb(dim ? 45 : 210, _darkTheme ? Color.FromArgb(190, 201, 211) : Color.FromArgb(50, 62, 75))))
                        graphics.DrawString(node.type ?? "节点类型", _nodeTypeFont, small, new RectangleF(node.x + 9, node.y + 2, Math.Max(1, node.w - 18), Math.Min(18, Math.Max(1, node.h - 4))), _entityHeaderFormat);
                    using (Brush text = new SolidBrush(Color.FromArgb(dim ? 45 : 245, _darkTheme ? Color.FromArgb(242, 245, 248) : Color.FromArgb(25, 35, 48))))
                        graphics.DrawString(node.label, _nodeLabelFont, text, flowchart ? new RectangleF(node.x + 10, node.y + 6, node.w - 20, node.h - 12) : new RectangleF(node.x + 6, node.y + 17, node.w - 12, node.h - 18), _nodeLabelFormat);
                }

                if (interactive && _replaceModeActive && SelectionCount > 0) DrawReplaceModeHighlight(graphics, unit);

                if (interactive && EditMode)
                {
                    if ((_gesture == CanvasGesture.MoveNodes || _gesture == CanvasGesture.MoveGroup || _gesture == CanvasGesture.MoveSelection) && _gestureMoved) { DrawAlignmentGuides(graphics, unit); DrawEqualSpacingHints(graphics, unit); }
                    DrawSelectionHandles(graphics, unit);
                    if (_gesture == CanvasGesture.Link && _gestureMoved) DrawLinkPreview(graphics, unit);
                }
            }
            finally { graphics.Restore(state); }
        }

        private void DrawReplaceModeHighlight(Graphics graphics, float unit)
        {
            Color accent = _darkTheme ? Color.FromArgb(255, 183, 77) : Color.FromArgb(238, 126, 34);
            int outerAlpha = _replacePulse ? 180 : 75;
            float outerWidth = (_replacePulse ? 9f : 6f) * unit;
            using (Pen outer = new Pen(Color.FromArgb(outerAlpha, accent), outerWidth))
            using (Pen inner = new Pen(Color.FromArgb(245, accent), 2.4f * unit))
            {
                outer.LineJoin = LineJoin.Round; outer.StartCap = LineCap.Round; outer.EndCap = LineCap.Round;
                inner.LineJoin = LineJoin.Round; inner.StartCap = LineCap.Round; inner.EndCap = LineCap.Round;
                inner.DashStyle = DashStyle.Dash; inner.DashOffset = _replacePulse ? 0f : 4f;
                foreach (string groupId in _selectedGroups)
                {
                    GraphGroup group;
                    if (!_groups.TryGetValue(groupId, out group)) continue;
                    using (GraphicsPath shape = RoundRect(RectOf(group), 12)) { graphics.DrawPath(outer, shape); graphics.DrawPath(inner, shape); }
                }
                foreach (string nodeId in _selectedNodes)
                {
                    GraphNode node;
                    if (!_nodes.TryGetValue(nodeId, out node)) continue;
                    using (GraphicsPath shape = NodeShape(node)) { graphics.DrawPath(outer, shape); graphics.DrawPath(inner, shape); }
                }
                if (_selectedType == "edge" && !String.IsNullOrEmpty(_selectedId))
                {
                    GraphEdge edge = _document.edges.FirstOrDefault(delegate(GraphEdge item) { return item.id == _selectedId; });
                    if (edge != null)
                    {
                        CachedEdgeGeometry geometry = GetEdgeGeometry(edge);
                        graphics.DrawPath(outer, geometry.Path); graphics.DrawPath(inner, geometry.Path);
                    }
                }
            }
        }

        internal bool ReplaceModeHighlightActiveForTesting { get { return _replaceModeActive && SelectionCount > 0; } }

        private bool EdgeEnvelopeIntersectsVisible(GraphEdge edge, RectangleF visible)
        {
            RectangleF source = GetEndpointRect(edge.sourceType, edge.source), target = GetEndpointRect(edge.targetType, edge.target);
            if (!IsFiniteRectangle(source) || !IsFiniteRectangle(target) || !IsFiniteRectangle(visible)) return false;
            RectangleF envelope = RectangleF.Union(source, target);
            float labelReach = String.IsNullOrWhiteSpace(edge.label) ? 0f : Math.Max(24f, (edge.label.Length + 2) * Font.Size);
            envelope.Inflate(150f + labelReach, 150f + labelReach);
            return envelope.IntersectsWith(visible);
        }

        private bool EdgeIntersectsVisible(GraphEdge edge, RectangleF bounds, RectangleF visible, float unit)
        {
            if (!HasFiniteRectangleValues(bounds) || !IsFiniteRectangle(visible) || !IsFinite(unit)) return false;
            RectangleF expanded = bounds;
            float labelReach = String.IsNullOrWhiteSpace(edge.label) ? 0f : Math.Max(24f, (edge.label.Length + 2) * Font.Size);
            float margin = Math.Max(2f * unit, labelReach);
            if (margin > 0) expanded.Inflate(margin, margin);
            return expanded.IntersectsWith(visible);
        }

        private void DrawAlignmentGuides(Graphics graphics, float unit)
        {
            RectangleF visible = VisibleWorldRectangle();
            using (Pen pen = new Pen(Color.FromArgb(225, 232, 84, 135), 1.4f * unit))
            {
                pen.DashStyle = DashStyle.Dash;
                if (IsFinite(_alignmentGuideX)) graphics.DrawLine(pen, _alignmentGuideX, visible.Top, _alignmentGuideX, visible.Bottom);
                if (IsFinite(_alignmentGuideY)) graphics.DrawLine(pen, visible.Left, _alignmentGuideY, visible.Right, _alignmentGuideY);
            }
        }

        private void DrawEqualSpacingHints(Graphics graphics, float unit)
        {
            if (_horizontalSpacingHint != null) DrawEqualSpacingHint(graphics, _horizontalSpacingHint, unit);
            if (_verticalSpacingHint != null) DrawEqualSpacingHint(graphics, _verticalSpacingHint, unit);
        }

        private void DrawEqualSpacingHint(Graphics graphics, EqualSpacingHint hint, float unit)
        {
            RectangleF visible = VisibleWorldRectangle();
            Color color = Color.FromArgb(235, 38, 174, 112);
            using (Pen pen = new Pen(color, 1.5f * unit))
            using (Brush brush = new SolidBrush(color))
            {
                if (hint.Horizontal)
                {
                    float y = Math.Max(hint.First.Bottom, Math.Max(hint.Middle.Bottom, hint.Last.Bottom)) + 13f * unit;
                    if (y > visible.Bottom - 8f * unit) y = Math.Min(hint.First.Top, Math.Min(hint.Middle.Top, hint.Last.Top)) - 13f * unit;
                    DrawSpacingSegment(graphics, pen, hint.First.Right, hint.Middle.Left, y, true, unit);
                    DrawSpacingSegment(graphics, pen, hint.Middle.Right, hint.Last.Left, y, true, unit);
                    graphics.DrawString("等距 " + Math.Round(hint.Gap), _spacingFont, brush, hint.Middle.Right + 4f * unit, y + 3f * unit);
                }
                else
                {
                    float x = Math.Max(hint.First.Right, Math.Max(hint.Middle.Right, hint.Last.Right)) + 13f * unit;
                    if (x > visible.Right - 8f * unit) x = Math.Min(hint.First.Left, Math.Min(hint.Middle.Left, hint.Last.Left)) - 13f * unit;
                    DrawSpacingSegment(graphics, pen, hint.First.Bottom, hint.Middle.Top, x, false, unit);
                    DrawSpacingSegment(graphics, pen, hint.Middle.Bottom, hint.Last.Top, x, false, unit);
                    graphics.DrawString("等距 " + Math.Round(hint.Gap), _spacingFont, brush, x + 3f * unit, hint.Middle.Bottom + 4f * unit);
                }
            }
        }

        private static void DrawSpacingSegment(Graphics graphics, Pen pen, float start, float end, float fixedAxis, bool horizontal, float unit)
        {
            float tick = 4f * unit;
            if (horizontal)
            {
                graphics.DrawLine(pen, start, fixedAxis, end, fixedAxis);
                graphics.DrawLine(pen, start, fixedAxis - tick, start, fixedAxis + tick); graphics.DrawLine(pen, end, fixedAxis - tick, end, fixedAxis + tick);
            }
            else
            {
                graphics.DrawLine(pen, fixedAxis, start, fixedAxis, end);
                graphics.DrawLine(pen, fixedAxis - tick, start, fixedAxis + tick, start); graphics.DrawLine(pen, fixedAxis - tick, end, fixedAxis + tick, end);
            }
        }

        internal float AlignmentGuideXForTesting { get { return _alignmentGuideX; } }
        internal float AlignmentGuideYForTesting { get { return _alignmentGuideY; } }
        internal bool HorizontalSpacingHintActiveForTesting { get { return _horizontalSpacingHint != null; } }
        internal bool VerticalSpacingHintActiveForTesting { get { return _verticalSpacingHint != null; } }
        internal float SpacingHintGapForTesting { get { return _horizontalSpacingHint != null ? _horizontalSpacingHint.Gap : _verticalSpacingHint != null ? _verticalSpacingHint.Gap : Single.NaN; } }

        private void DrawSelectionHandles(Graphics graphics, float unit)
        {
            if (_selectedType != "group" || _selectedGroups.Count != 1 || _selectedNodes.Count != 0 || _selectedId.Length == 0) return;
            RectangleF rect = GetEndpointRect(_selectedType, _selectedId);
            if (!IsFiniteRectangle(rect)) return;
            if (_selectedType == "group")
            {
                foreach (KeyValuePair<string, PointF> item in ResizePoints(rect))
                {
                    float size = 8f * unit;
                    using (Brush fill = new SolidBrush(_darkTheme ? Color.FromArgb(30, 37, 45) : Color.White)) graphics.FillRectangle(fill, item.Value.X - size / 2, item.Value.Y - size / 2, size, size);
                    using (Pen pen = new Pen(Color.FromArgb(43, 108, 245), 1.4f * unit)) graphics.DrawRectangle(pen, item.Value.X - size / 2, item.Value.Y - size / 2, size, size);
                }
            }
        }

        private void DrawLinkPreview(Graphics graphics, float unit)
        {
            RectangleF sourceRect = GetEndpointRect(_gestureEntityType, _gestureEntityId);
            if (!IsFiniteRectangle(sourceRect) || !IsFinite(_worldCurrent.X) || !IsFinite(_worldCurrent.Y)) return;
            EndpointItem target = HitEndpoint(_worldCurrent, _gestureEntityType, _gestureEntityId);
            string sourceSide = _sourceSide;
            PointF end;
            if (target == null) end = _worldCurrent;
            else
            {
                RectangleF targetRect = GetEndpointRect(target.Type, target.Id);
                sourceSide = ConnectionSide(sourceRect, targetRect);
                end = GetPortPoint(targetRect, ConnectionSide(targetRect, sourceRect));
            }
            PointF start = GetPortPoint(sourceRect, sourceSide);
            using (Pen pen = new Pen(Color.FromArgb(43, 108, 245), 2.2f * unit))
            {
                pen.DashStyle = DashStyle.Dash;
                graphics.DrawLine(pen, start, end);
            }
        }

        private void GetFocusForDrawing(out HashSet<string> entities, out HashSet<string> edgeIds, out HashSet<string> inactiveSameNameNodes)
        {
            if (_gesture != CanvasGesture.None && _gestureFocusEntities != null)
            {
                entities = _gestureFocusEntities;
                edgeIds = _gestureFocusEdges;
                inactiveSameNameNodes = _gestureInactiveSameNameNodes;
                return;
            }
            ComputeFocus(out entities, out edgeIds);
            inactiveSameNameNodes = ComputeInactiveSameNameNodeIds();
            if (_gesture != CanvasGesture.None)
            {
                _gestureFocusEntities = entities;
                _gestureFocusEdges = edgeIds;
                _gestureInactiveSameNameNodes = inactiveSameNameNodes;
            }
        }

        private void ClearGestureFocusCache()
        {
            _gestureFocusEntities = null;
            _gestureFocusEdges = null;
            _gestureInactiveSameNameNodes = null;
        }

        private void ComputeFocus(out HashSet<string> entities, out HashSet<string> edgeIds)
        {
            entities = new HashSet<string>(); edgeIds = new HashSet<string>();
            if (!ShouldHighlightNeighbors()) return;
            if (_selectedType == "edge")
            {
                GraphEdge selected = _document.edges.FirstOrDefault(delegate(GraphEdge edge) { return edge.id == _selectedId; });
                if (selected != null)
                {
                    edgeIds.Add(selected.id);
                    entities.Add(GraphSerialization.EndpointKey(selected.sourceType, selected.source));
                    entities.Add(GraphSerialization.EndpointKey(selected.targetType, selected.target));
                }
                return;
            }
            List<string> roots = new List<string>();
            if (_selectedType == "node" && _nodes.ContainsKey(_selectedId))
            {
                string selectedLabel = _nodes[_selectedId].label ?? "";
                roots.AddRange(_document.nodes.Where(delegate(GraphNode node) { return String.Equals(node.label ?? "", selectedLabel, StringComparison.Ordinal); })
                    .Select(delegate(GraphNode node) { return GraphSerialization.EndpointKey("node", node.id); }));
            }
            else roots.Add(GraphSerialization.EndpointKey(_selectedType, _selectedId));
            foreach (string root in roots) entities.Add(root);
            int depth = Math.Max(1, Math.Min(3, FocusDepth <= 0 ? 1 : FocusDepth));
            string direction = String.IsNullOrEmpty(FocusDirection) ? "all" : FocusDirection;
            Dictionary<string, List<KeyValuePair<string, string>>> adjacent = new Dictionary<string, List<KeyValuePair<string, string>>>();
            Action<string, string, string> add = delegate(string from, string to, string edgeId)
            {
                if (!adjacent.ContainsKey(from)) adjacent[from] = new List<KeyValuePair<string, string>>();
                adjacent[from].Add(new KeyValuePair<string, string>(to, edgeId));
            };
            foreach (GraphEdge edge in _document.edges)
            {
                string source = GraphSerialization.EndpointKey(edge.sourceType, edge.source);
                string target = GraphSerialization.EndpointKey(edge.targetType, edge.target);
                if (direction == "all" || direction == "downstream") add(source, target, edge.id);
                if (direction == "all" || direction == "upstream") add(target, source, edge.id);
            }
            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            Dictionary<string, int> seen = new Dictionary<string, int>();
            foreach (string root in roots) { queue.Enqueue(new KeyValuePair<string, int>(root, 0)); seen[root] = 0; }
            while (queue.Count > 0)
            {
                KeyValuePair<string, int> current = queue.Dequeue();
                if (current.Value >= depth || !adjacent.ContainsKey(current.Key)) continue;
                foreach (KeyValuePair<string, string> next in adjacent[current.Key])
                {
                    entities.Add(next.Key); edgeIds.Add(next.Value);
                    int level = current.Value + 1;
                    if (!seen.ContainsKey(next.Key) || seen[next.Key] > level) { seen[next.Key] = level; queue.Enqueue(new KeyValuePair<string, int>(next.Key, level)); }
                }
            }
        }

        private bool ShouldHighlightNeighbors()
        {
            return _selectedType.Length > 0 && SelectionCount == 1;
        }

        internal bool NeighborHighlightActiveForTesting { get { return ShouldHighlightNeighbors(); } }

        private bool IsSameNameHighlightedNode(GraphNode node)
        {
            return HasSelectedNodeName(node) && !IsInactiveSameNameNode(node);
        }

        private bool HasSelectedNodeName(GraphNode node)
        {
            GraphNode selected;
            return node != null && _selectedType == "node" && _selectedNodes.Count == 1 && _nodes.TryGetValue(_selectedId, out selected) &&
                String.Equals(node.label ?? "", selected.label ?? "", StringComparison.Ordinal);
        }

        private HashSet<string> ComputeInactiveSameNameNodeIds()
        {
            HashSet<string> inactive = new HashSet<string>(StringComparer.Ordinal);
            GraphNode selected;
            if (_selectedType != "node" || _selectedNodes.Count != 1 || !_nodes.TryGetValue(_selectedId, out selected)) return inactive;
            string direction = String.IsNullOrEmpty(FocusDirection) ? "all" : FocusDirection;
            if (direction != "upstream" && direction != "downstream") return inactive;
            HashSet<string> active = new HashSet<string>(StringComparer.Ordinal);
            foreach (GraphEdge edge in _document.edges)
            {
                string sourceKey = GraphSerialization.EndpointKey(edge.sourceType, edge.source);
                string targetKey = GraphSerialization.EndpointKey(edge.targetType, edge.target);
                if (sourceKey == targetKey) continue;
                active.Add(direction == "downstream" ? sourceKey : targetKey);
            }
            string selectedLabel = selected.label ?? "";
            foreach (GraphNode node in _document.nodes)
            {
                if (!String.Equals(node.label ?? "", selectedLabel, StringComparison.Ordinal)) continue;
                if (!active.Contains(GraphSerialization.EndpointKey("node", node.id))) inactive.Add(node.id);
            }
            return inactive;
        }

        private bool IsInactiveSameNameNode(GraphNode node)
        {
            GraphNode selected;
            if (node == null || _selectedType != "node" || _selectedNodes.Count != 1 || !_nodes.TryGetValue(_selectedId, out selected)) return false;
            if (!String.Equals(node.label ?? "", selected.label ?? "", StringComparison.Ordinal)) return false;
            string direction = String.IsNullOrEmpty(FocusDirection) ? "all" : FocusDirection;
            if (direction != "upstream" && direction != "downstream") return false;
            string nodeKey = GraphSerialization.EndpointKey("node", node.id);
            foreach (GraphEdge edge in _document.edges)
            {
                string sourceKey = GraphSerialization.EndpointKey(edge.sourceType, edge.source);
                string targetKey = GraphSerialization.EndpointKey(edge.targetType, edge.target);
                if (sourceKey == targetKey) continue;
                if (direction == "downstream" && sourceKey == nodeKey) return false;
                if (direction == "upstream" && targetKey == nodeKey) return false;
            }
            return true;
        }

        internal bool SameNameHighlightedForTesting(string id) { GraphNode node; return _nodes.TryGetValue(id, out node) && IsSameNameHighlightedNode(node); }
        internal bool InactiveSameNameNodeForTesting(string id) { GraphNode node; return _nodes.TryGetValue(id, out node) && IsInactiveSameNameNode(node); }
        internal HashSet<string> FocusedEntitiesForTesting() { HashSet<string> entities, edges; ComputeFocus(out entities, out edges); return entities; }
        internal HashSet<string> FocusedEdgesForTesting() { HashSet<string> entities, edges; ComputeFocus(out entities, out edges); return edges; }

        private CachedEdgeGeometry GetEdgeGeometry(GraphEdge edge)
        {
            RectangleF sourceRect = GetEndpointRect(edge.sourceType, edge.source);
            RectangleF targetRect = GetEndpointRect(edge.targetType, edge.target);
            string cacheKey = edge.id ?? "";
            CachedEdgeGeometry geometry;
            if (_edgeGeometry.TryGetValue(cacheKey, out geometry) && geometry.Matches(edge, sourceRect, targetRect)) return geometry;
            if (geometry != null)
            {
                _edgeGeometry.Remove(cacheKey);
                geometry.Dispose();
            }
            GraphicsPath path = null;
            try
            {
                path = CreateEdgePath(edge, sourceRect, targetRect);
                geometry = new CachedEdgeGeometry
                {
                    SourceType = edge.sourceType,
                    SourceId = edge.source,
                    TargetType = edge.targetType,
                    TargetId = edge.target,
                    SourceSide = edge.sourceSide,
                    TargetSide = edge.targetSide,
                    LineType = edge.lineType,
                    SourceRect = sourceRect,
                    TargetRect = targetRect,
                    Path = path,
                    Bounds = path.GetBounds()
                };
                PopulateEdgeGeometry(geometry);
                _edgeGeometry[cacheKey] = geometry;
                path = null;
                return geometry;
            }
            finally
            {
                if (path != null) path.Dispose();
            }
        }

        private GraphicsPath BuildEdgePath(GraphEdge edge)
        {
            RectangleF sourceRect = GetEndpointRect(edge.sourceType, edge.source);
            RectangleF targetRect = GetEndpointRect(edge.targetType, edge.target);
            return CreateEdgePath(edge, sourceRect, targetRect);
        }

        private static GraphicsPath CreateEdgePath(GraphEdge edge, RectangleF sourceRect, RectangleF targetRect)
        {
            string sourceSide = String.IsNullOrEmpty(edge.sourceSide) ? ConnectionSide(sourceRect, targetRect) : edge.sourceSide;
            string targetSide = String.IsNullOrEmpty(edge.targetSide) ? ConnectionSide(targetRect, sourceRect) : edge.targetSide;
            PointF source = GetPortPoint(sourceRect, sourceSide);
            PointF target = GetPortPoint(targetRect, targetSide);
            GraphicsPath path = new GraphicsPath();
            try
            {
                if (edge.lineType == "straight") path.AddLine(source, target);
                else if (edge.lineType == "polyline")
                {
                    if (sourceSide == "left" || sourceSide == "right")
                    {
                        float mid = (source.X + target.X) / 2f;
                        path.AddLines(new[] { source, new PointF(mid, source.Y), new PointF(mid, target.Y), target });
                    }
                    else
                    {
                        float mid = (source.Y + target.Y) / 2f;
                        path.AddLines(new[] { source, new PointF(source.X, mid), new PointF(target.X, mid), target });
                    }
                }
                else
                {
                    PointF sourceVector = SideVector(sourceSide);
                    PointF targetVector = SideVector(targetSide);
                    float distance = Math.Max(45f, Math.Min(140f, Distance(source, target) * 0.42f));
                    path.AddBezier(source, new PointF(source.X + sourceVector.X * distance, source.Y + sourceVector.Y * distance), new PointF(target.X + targetVector.X * distance, target.Y + targetVector.Y * distance), target);
                }
                return path;
            }
            catch
            {
                path.Dispose();
                throw;
            }
        }

        private static void PopulateEdgeGeometry(CachedEdgeGeometry geometry)
        {
            geometry.LabelPoint = PathPointAtFraction(geometry.Path, .5f);
            using (GraphicsPath flattened = (GraphicsPath)geometry.Path.Clone())
            {
                flattened.Flatten(null, 1.2f);
                PointF[] points = flattened.PathPoints;
                if (points.Length < 2) return;
                geometry.ArrowEnd = points[points.Length - 1];
                geometry.ArrowPrevious = points[points.Length - 2];
                geometry.HasArrow = true;
            }
        }

        private void DrawArrow(Graphics graphics, CachedEdgeGeometry geometry, Color color, float unit, float alpha)
        {
            if (!geometry.HasArrow) return;
            PointF end = geometry.ArrowEnd, previous = geometry.ArrowPrevious;
            float angle = (float)Math.Atan2(end.Y - previous.Y, end.X - previous.X);
            float size = 8f * unit;
            PointF left = new PointF(end.X - (float)Math.Cos(angle - 0.55) * size, end.Y - (float)Math.Sin(angle - 0.55) * size);
            PointF right = new PointF(end.X - (float)Math.Cos(angle + 0.55) * size, end.Y - (float)Math.Sin(angle + 0.55) * size);
            using (Brush brush = new SolidBrush(Color.FromArgb((int)(255 * alpha), color))) graphics.FillPolygon(brush, new[] { end, left, right });
        }

        private void DrawEdgeLabel(Graphics graphics, string label, PointF point, float unit, bool dim)
        {
            SizeF size = graphics.MeasureString(label, Font);
            RectangleF box = new RectangleF(point.X - size.Width / 2 - 4 * unit, point.Y - size.Height / 2 - 2 * unit, size.Width + 8 * unit, size.Height + 4 * unit);
            using (Brush back = new SolidBrush(Color.FromArgb(dim ? 35 : 225, _darkTheme ? Color.FromArgb(25, 31, 38) : Color.White))) graphics.FillRectangle(back, box);
            using (Brush text = new SolidBrush(Color.FromArgb(dim ? 40 : 230, _darkTheme ? Color.FromArgb(222, 230, 238) : Color.FromArgb(45, 55, 67)))) graphics.DrawString(label, Font, text, point.X - size.Width / 2, point.Y - size.Height / 2);
        }

        private static PointF PathPointAtFraction(GraphicsPath path, float fraction)
        {
            using (GraphicsPath flattened = (GraphicsPath)path.Clone())
            {
                flattened.Flatten(null, .6f);
                PointF[] points = flattened.PathPoints;
                if (points.Length == 0) return PointF.Empty;
                if (points.Length == 1) return points[0];
                float total = 0;
                for (int i = 1; i < points.Length; i++) total += Distance(points[i - 1], points[i]);
                if (total <= .001f) return points[0];
                float target = total * Math.Max(0, Math.Min(1, fraction)), travelled = 0;
                for (int i = 1; i < points.Length; i++)
                {
                    float segment = Distance(points[i - 1], points[i]);
                    if (travelled + segment >= target && segment > .001f)
                    {
                        float ratio = (target - travelled) / segment;
                        return new PointF(points[i - 1].X + (points[i].X - points[i - 1].X) * ratio, points[i - 1].Y + (points[i].Y - points[i - 1].Y) * ratio);
                    }
                    travelled += segment;
                }
                return points[points.Length - 1];
            }
        }

        internal PointF EdgeLabelPointForTesting(GraphEdge edge)
        {
            return GetEdgeGeometry(edge).LabelPoint;
        }

        private RectangleF EdgeLabelEditArea(GraphEdge edge)
        {
            PointF point = EdgeLabelPointForTesting(edge);
            float width = Math.Min(280, Math.Max(150, (edge.label ?? "").Length * 15 + 50)), height = 32;
            float x = point.X - width / 2f;
            float y = point.Y - height / 2f;
            return new RectangleF(x, y, width, height);
        }

        private bool HitResizeHandle(PointF world, out string handle)
        {
            handle = "";
            if (_selectedType != "group" || _selectedGroups.Count != 1 || !_groups.ContainsKey(_selectedId)) return false;
            RectangleF rect = RectOf(_groups[_selectedId]);
            if (!IsFiniteRectangle(rect) || !IsFinite(world.X) || !IsFinite(world.Y)) return false;
            float tolerance = 9f / SafeZoom(_zoom);
            foreach (KeyValuePair<string, PointF> point in ResizePoints(rect))
            {
                if (Distance(world, point.Value) <= tolerance) { handle = point.Key; return true; }
            }
            if (world.Y >= rect.Top - tolerance && world.Y <= rect.Bottom + tolerance)
            {
                if (Math.Abs(world.X - rect.Left) <= tolerance) { handle = "w"; return true; }
                if (Math.Abs(world.X - rect.Right) <= tolerance) { handle = "e"; return true; }
            }
            if (world.X >= rect.Left - tolerance && world.X <= rect.Right + tolerance)
            {
                if (Math.Abs(world.Y - rect.Top) <= tolerance) { handle = "n"; return true; }
                if (Math.Abs(world.Y - rect.Bottom) <= tolerance) { handle = "s"; return true; }
            }
            return false;
        }

        private void UpdateHoverCursor(Point screenPoint)
        {
            string handle;
            Cursor = EditMode && HitResizeHandle(ScreenToWorld(screenPoint), out handle) ? ResizeCursor(handle) : Cursors.Default;
        }

        private static Cursor ResizeCursor(string handle)
        {
            if (handle == "n" || handle == "s") return Cursors.SizeNS;
            if (handle == "e" || handle == "w") return Cursors.SizeWE;
            if (handle == "ne" || handle == "sw") return Cursors.SizeNESW;
            return Cursors.SizeNWSE;
        }

        internal string ResizeHandleAtForTesting(PointF world) { string handle; return HitResizeHandle(world, out handle) ? handle : ""; }
        internal Cursor ResizeCursorForTesting(string handle) { return ResizeCursor(handle); }
        internal bool LinkHandlesVisibleForSelectionForTesting { get { return false; } }

        private GraphNode HitNode(PointF world)
        {
            for (int i = _document.nodes.Count - 1; i >= 0; i--) if (RectOf(_document.nodes[i]).Contains(world)) return _document.nodes[i];
            return null;
        }

        private GraphGroup HitGroup(PointF world)
        {
            foreach (GraphGroup group in GroupsBackToFront().Reverse()) if (RectOf(group).Contains(world)) return group;
            return null;
        }

        private GraphGroup TopGroupAt(PointF world) { return HitGroup(world); }

        private GraphEdge HitEdge(PointF world)
        {
            if (!IsFinite(world.X) || !IsFinite(world.Y)) return null;
            float zoom = SafeZoom(_zoom), offsetX = ClampViewOffset(_offsetX), offsetY = ClampViewOffset(_offsetY);
            float tolerance = 12f / zoom;
            PointF screen = new PointF(world.X * zoom + offsetX, world.Y * zoom + offsetY);
            using (Pen pen = new Pen(Color.Black, 12f))
            using (Matrix transform = new Matrix(zoom, 0f, 0f, zoom, offsetX, offsetY))
            {
                for (int i = _document.edges.Count - 1; i >= 0; i--)
                {
                    GraphEdge edge = _document.edges[i];
                    RectangleF source = GetEndpointRect(edge.sourceType, edge.source), target = GetEndpointRect(edge.targetType, edge.target);
                    if (!IsFiniteRectangle(source) || !IsFiniteRectangle(target)) continue;
                    RectangleF envelope = RectangleF.Union(source, target);
                    envelope.Inflate(150f + tolerance, 150f + tolerance);
                    if (!envelope.Contains(world)) continue;
                    CachedEdgeGeometry geometry = GetEdgeGeometry(edge);
                    RectangleF bounds = geometry.Bounds;
                    bounds.Inflate(tolerance, tolerance);
                    if (!bounds.Contains(world)) continue;
                    using (GraphicsPath screenPath = (GraphicsPath)geometry.Path.Clone())
                    {
                        screenPath.Transform(transform);
                        if (screenPath.IsOutlineVisible(screen, pen)) return edge;
                    }
                }
            }
            return null;
        }

        internal string HitEdgeIdForTesting(PointF world)
        {
            GraphEdge edge = HitEdge(world);
            return edge == null ? "" : edge.id;
        }

        private EndpointItem HitEndpoint(PointF world, string excludedType, string excludedId)
        {
            GraphNode node = HitNode(world);
            if (node != null && !(excludedType == "node" && excludedId == node.id)) return new EndpointItem { Type = "node", Id = node.id, Label = node.label };
            foreach (GraphGroup group in GroupsBackToFront().Reverse())
            {
                if (excludedType == "group" && excludedId == group.id) continue;
                if (RectOf(group).Contains(world)) return new EndpointItem { Type = "group", Id = group.id, Label = group.label };
            }
            return null;
        }

        private IEnumerable<GraphGroup> GroupsBackToFront()
        {
            if (_groupDrawOrder == null)
                _groupDrawOrder = _document.groups.OrderByDescending(delegate(GraphGroup group) { return group.w * group.h; }).ToList();
            return _groupDrawOrder;
        }

        internal string[] GroupDrawOrderForTesting() { return GroupsBackToFront().Select(delegate(GraphGroup group) { return group.id; }).ToArray(); }
        internal string HitGroupIdForTesting(PointF world) { GraphGroup group = HitGroup(world); return group == null ? "" : group.id; }
        internal string HitEndpointKeyForTesting(PointF world) { EndpointItem item = HitEndpoint(world, "", ""); return item == null ? "" : item.Key; }

        private void RebuildIndexes()
        {
            ClearEdgeGeometryCache();
            _groupDrawOrder = null;
            _alignmentCandidates.Clear();
            ClearGestureFocusCache();
            _nodes.Clear(); _groups.Clear();
            foreach (GraphNode node in _document.nodes) _nodes[node.id] = node;
            foreach (GraphGroup group in _document.groups) _groups[group.id] = group;
            RepairAutomaticSides();
        }

        private void RepairAutomaticSides()
        {
            if (_document == null) return;
            foreach (GraphEdge edge in _document.edges)
            {
                RectangleF sourceRect = GetEndpointRect(edge.sourceType, edge.source), targetRect = GetEndpointRect(edge.targetType, edge.target);
                if (!IsFiniteRectangle(sourceRect) || !IsFiniteRectangle(targetRect)) continue;
                edge.sourceSide = ConnectionSide(sourceRect, targetRect);
                edge.targetSide = ConnectionSide(targetRect, sourceRect);
            }
        }

        private void EnsureSelection()
        {
            _selectedNodes.RemoveWhere(delegate(string id) { return !_nodes.ContainsKey(id); });
            _selectedGroups.RemoveWhere(delegate(string id) { return !_groups.ContainsKey(id); });
            if (_selectedType == "node" || _selectedType == "group" || _selectedType == "mixed") UpdateSelectionIdentity();
            if (_selectedType == "edge" && !_document.edges.Any(delegate(GraphEdge edge) { return edge.id == _selectedId; })) ClearSelection(false);
        }

        private RectangleF GetEndpointRect(string type, string id)
        {
            if (type == "group" && _groups.ContainsKey(id)) return RectOf(_groups[id]);
            if (_nodes.ContainsKey(id)) return RectOf(_nodes[id]);
            return RectangleF.Empty;
        }

        private Color EdgeSourceColor(GraphEdge edge)
        {
            if (edge != null && edge.sourceType == "group") return _darkTheme ? Color.FromArgb(101, 116, 130) : Color.FromArgb(155, 166, 178);
            GraphNode source;
            return edge != null && _nodes.TryGetValue(edge.source ?? "", out source) ? NodeAccentColor(source.kind) : Color.FromArgb(113, 137, 163);
        }

        internal Color EdgeSourceColorForTesting(GraphEdge edge) { return EdgeSourceColor(edge); }

        private static Color NodeAccentColor(string kind)
        {
            if (kind == "resource") return Color.FromArgb(75, 158, 234);
            if (kind == "output") return Color.FromArgb(84, 173, 114);
            if (kind == "content") return Color.FromArgb(215, 101, 164);
            if (kind == "staff") return Color.FromArgb(139, 111, 188);
            if (kind == "commercial") return Color.FromArgb(232, 150, 62);
            return Color.FromArgb(113, 137, 163);
        }

        private static Color NodeColor(string kind)
        {
            if (kind == "resource") return Color.FromArgb(215, 235, 251);
            if (kind == "output") return Color.FromArgb(216, 241, 225);
            if (kind == "content") return Color.FromArgb(247, 222, 238);
            if (kind == "staff") return Color.FromArgb(231, 224, 247);
            if (kind == "commercial") return Color.FromArgb(250, 231, 207);
            return Color.FromArgb(224, 232, 242);
        }

        private static Color DarkNodeColor(string kind)
        {
            if (kind == "resource") return Color.FromArgb(45, 76, 100);
            if (kind == "output") return Color.FromArgb(43, 82, 59);
            if (kind == "content") return Color.FromArgb(88, 53, 79);
            if (kind == "staff") return Color.FromArgb(70, 58, 94);
            if (kind == "commercial") return Color.FromArgb(94, 72, 46);
            return Color.FromArgb(53, 66, 81);
        }

        private RectangleF ContentBounds(float padding)
        {
            RectangleF bounds = RectangleF.Empty;
            bool hasContent = false;
            foreach (GraphGroup group in _document.groups) IncludeBounds(ref bounds, ref hasContent, RectOf(group));
            foreach (GraphNode node in _document.nodes) IncludeBounds(ref bounds, ref hasContent, RectOf(node));
            foreach (GraphEdge edge in _document.edges)
            {
                RectangleF source = GetEndpointRect(edge.sourceType, edge.source), target = GetEndpointRect(edge.targetType, edge.target);
                if (!IsFiniteRectangle(source) || !IsFiniteRectangle(target)) continue;
                IncludeBounds(ref bounds, ref hasContent, GetEdgeGeometry(edge).Bounds);
            }
            if (!hasContent) bounds = DefaultContentBounds();
            if (IsFinite(padding) && padding > 0) bounds.Inflate(Math.Min(padding, GraphSerialization.MaxItemDimension), Math.Min(padding, GraphSerialization.MaxItemDimension));
            if (!IsFiniteRectangle(bounds)) bounds = DefaultContentBounds();
            return new RectangleF(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        }

        private RectangleF DefaultContentBounds()
        {
            float width = _document != null && _document.meta != null && IsFinite(_document.meta.canvasWidth) && _document.meta.canvasWidth > 0 ? _document.meta.canvasWidth : 1200f;
            float height = _document != null && _document.meta != null && IsFinite(_document.meta.canvasHeight) && _document.meta.canvasHeight > 0 ? _document.meta.canvasHeight : 760f;
            return new RectangleF(0, 0, Math.Min(width, GraphSerialization.MaxItemDimension), Math.Min(height, GraphSerialization.MaxItemDimension));
        }

        private static void IncludeBounds(ref RectangleF bounds, ref bool hasContent, RectangleF item)
        {
            if (!IsFiniteRectangle(item)) return;
            RectangleF combined = hasContent ? RectangleF.Union(bounds, item) : item;
            if (!IsFiniteRectangle(combined)) return;
            bounds = combined;
            hasContent = true;
        }

        internal RectangleF ContentBoundsForTesting(float padding) { return ContentBounds(padding); }

        private RectangleF VisibleWorldRectangle()
        {
            return VisibleWorldRectangle(_zoom, _offsetX, _offsetY);
        }

        private RectangleF VisibleWorldRectangle(float zoom, float offsetX, float offsetY)
        {
            float safeZoom = SafeZoom(zoom);
            float safeOffsetX = ClampViewOffset(offsetX), safeOffsetY = ClampViewOffset(offsetY);
            return new RectangleF(-safeOffsetX / safeZoom, -safeOffsetY / safeZoom, ClientSize.Width / safeZoom, ClientSize.Height / safeZoom);
        }

        private PointF ScreenToWorld(Point point)
        {
            float zoom = SafeZoom(_zoom), offsetX = ClampViewOffset(_offsetX), offsetY = ClampViewOffset(_offsetY);
            return new PointF((point.X - offsetX) / zoom, (point.Y - offsetY) / zoom);
        }
        private RectangleF ScreenRectangleToWorld(Rectangle rect)
        {
            PointF start = ScreenToWorld(rect.Location), end = ScreenToWorld(new Point(rect.Right, rect.Bottom));
            return new RectangleF(start.X, start.Y, end.X - start.X, end.Y - start.Y);
        }
        private static Rectangle NormalizeRectangle(Point a, Point b) { return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)); }
        private static RectangleF RectOf(GraphNode node) { return new RectangleF(node.x, node.y, node.w, node.h); }
        private static RectangleF RectOf(GraphGroup group) { return new RectangleF(group.x, group.y, group.w, group.h); }
        private static bool IsFinite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        private static bool IsFiniteRectangle(RectangleF rect)
        {
            return HasFiniteRectangleValues(rect) && rect.Width > 0 && rect.Height > 0;
        }
        private static bool HasFiniteRectangleValues(RectangleF rect) { return IsFinite(rect.X) && IsFinite(rect.Y) && IsFinite(rect.Width) && IsFinite(rect.Height); }
        private static float SafeZoom(float value)
        {
            if (!IsFinite(value) || value <= 0) return 1f;
            return Math.Max(MinimumSafeZoom, Math.Min(MaximumZoom, value));
        }
        private static float ClampViewOffset(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return 0f;
            return (float)Math.Max(-MaximumViewOffset, Math.Min(MaximumViewOffset, value));
        }
        private static float ClampWorldCoordinate(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return 0f;
            return (float)Math.Max(-GraphSerialization.MaxCoordinate, Math.Min(GraphSerialization.MaxCoordinate, value));
        }
        private static float ClampItemDimension(double value, float minimum)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return minimum;
            return (float)Math.Max(minimum, Math.Min(GraphSerialization.MaxItemDimension, value));
        }
        private static RectangleF NodeTypeEditArea(GraphNode node)
        {
            float width = Math.Min(node.w - 12, Math.Max(48, (node.type ?? "节点类型").Length * 13 + 18));
            return new RectangleF(node.x + 5, node.y + 2, width, Math.Min(22, node.h));
        }
        private static RectangleF NodeLabelEditArea(GraphNode node) { return new RectangleF(node.x + 5, node.y + 20, node.w - 10, Math.Max(24, node.h - 22)); }
        private static RectangleF GroupLabelEditArea(GraphGroup group)
        {
            float width = Math.Min(group.w - 12, Math.Max(100, (group.label ?? "分组").Length * 16 + 28));
            return new RectangleF(group.x + 6, group.y + 4, width, 30);
        }
        private static float Snap(float value) { return (float)Math.Round(value / 5f) * 5f; }
        private static float Distance(Point a, Point b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }
        private static float Distance(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }
        private static string DirectionSide(PointF start, PointF end, string fallback)
        {
            float dx = end.X - start.X, dy = end.Y - start.Y;
            if (Math.Abs(dx) + Math.Abs(dy) < 2) return fallback;
            return Math.Abs(dx) >= Math.Abs(dy) ? (dx >= 0 ? "right" : "left") : (dy >= 0 ? "bottom" : "top");
        }
        private static string ConnectionSide(RectangleF from, RectangleF to)
        {
            float horizontalOverlap = Math.Min(from.Right, to.Right) - Math.Max(from.Left, to.Left);
            float verticalOverlap = Math.Min(from.Bottom, to.Bottom) - Math.Max(from.Top, to.Top);
            if (horizontalOverlap > 0)
            {
                if (to.Top >= from.Bottom) return "bottom";
                if (to.Bottom <= from.Top) return "top";
            }
            if (verticalOverlap > 0)
            {
                if (to.Left >= from.Right) return "right";
                if (to.Right <= from.Left) return "left";
            }
            return DirectionSide(Center(from), Center(to), "right");
        }
        private static string NearestSide(RectangleF rect, PointF point)
        {
            Dictionary<string, float> distances = new Dictionary<string, float>();
            distances["top"] = Distance(point, GetPortPoint(rect, "top")); distances["right"] = Distance(point, GetPortPoint(rect, "right"));
            distances["bottom"] = Distance(point, GetPortPoint(rect, "bottom")); distances["left"] = Distance(point, GetPortPoint(rect, "left"));
            return distances.OrderBy(delegate(KeyValuePair<string, float> pair) { return pair.Value; }).First().Key;
        }
        internal static string NearestSideForTesting(RectangleF rect, PointF point) { return NearestSide(rect, point); }
        private static PointF Center(RectangleF rect) { return new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f); }
        private static PointF GetPortPoint(RectangleF rect, string side)
        {
            if (side == "top") return new PointF(rect.X + rect.Width / 2f, rect.Top);
            if (side == "bottom") return new PointF(rect.X + rect.Width / 2f, rect.Bottom);
            if (side == "left") return new PointF(rect.Left, rect.Y + rect.Height / 2f);
            return new PointF(rect.Right, rect.Y + rect.Height / 2f);
        }
        private static PointF SideVector(string side)
        {
            if (side == "top") return new PointF(0, -1); if (side == "bottom") return new PointF(0, 1);
            if (side == "left") return new PointF(-1, 0); return new PointF(1, 0);
        }
        private static Dictionary<string, PointF> ResizePoints(RectangleF rect)
        {
            Dictionary<string, PointF> points = new Dictionary<string, PointF>();
            points["nw"] = new PointF(rect.Left, rect.Top); points["ne"] = new PointF(rect.Right, rect.Top);
            points["se"] = new PointF(rect.Right, rect.Bottom); points["sw"] = new PointF(rect.Left, rect.Bottom); return points;
        }
        private static GraphicsPath NodeShape(GraphNode node)
        {
            RectangleF r = RectOf(node); GraphicsPath path = new GraphicsPath();
            switch (GraphSerialization.NormalizeNodeShape(node.shape))
            {
                case "terminator": return RoundRect(r, r.Height / 2f);
                case "decision": path.AddPolygon(new[] { new PointF(r.Left + r.Width / 2f, r.Top), new PointF(r.Right, r.Top + r.Height / 2f), new PointF(r.Left + r.Width / 2f, r.Bottom), new PointF(r.Left, r.Top + r.Height / 2f) }); return path;
                case "data": float inset = Math.Min(18f, r.Width / 6f); path.AddPolygon(new[] { new PointF(r.Left + inset, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right - inset, r.Bottom), new PointF(r.Left, r.Bottom) }); return path;
                case "document": path.AddLines(new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom - 8), new PointF(r.Right - r.Width * .25f, r.Bottom), new PointF(r.Left + r.Width * .25f, r.Bottom - 8), new PointF(r.Left, r.Bottom), new PointF(r.Left, r.Top) }); path.CloseFigure(); return path;
                default: path.Dispose(); return RoundRect(r, 4);
            }
        }

        private static GraphicsPath RoundRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath(); float diameter = radius * 2;
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90); path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
        }
        private void RaiseSelectionChanged()
        {
            ClearGestureFocusCache();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }
    }
}
