using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;

namespace RelationshipGraphNative
{
    internal enum GraphLayoutDirection
    {
        LeftToRight,
        TopToBottom
    }

    /// <summary>
    /// Options for the pure-data layout pass.  The defaults deliberately leave
    /// generous channels for labels and orthogonal routes.
    /// </summary>
    internal sealed class GraphLayoutOptions
    {
        public GraphLayoutDirection Direction { get; set; }
        public float OriginX { get; set; }
        public float OriginY { get; set; }
        public float LayerSpacing { get; set; }
        public float ItemSpacing { get; set; }
        public int MaximumItemsPerLayer { get; set; }
        public float GroupPadding { get; set; }
        public float GroupHeaderHeight { get; set; }
        public float MinimumGroupWidth { get; set; }
        public float MinimumGroupHeight { get; set; }
        public bool KeepExistingGroupSizeAsMinimum { get; set; }
        public bool TreatGroupsAsRoutingObstacles { get; set; }
        public float ObstaclePadding { get; set; }
        public float PortCornerPadding { get; set; }
        public float PortSpacing { get; set; }
        public float RoutingStubLength { get; set; }
        public float RoutingChannelSpacing { get; set; }
        public float RoutingSearchMargin { get; set; }
        public int MaximumRoutingObstacles { get; set; }
        public float BendPenalty { get; set; }
        public float LabelGap { get; set; }
        public float LabelPadding { get; set; }
        public float MinimumLabelWidth { get; set; }
        public float MaximumLabelWidth { get; set; }
        public float LabelHeight { get; set; }
        public float LabelCharacterWidth { get; set; }
        public Func<bool> CancellationRequested { get; set; }
        public Action<int, string> ProgressChanged { get; set; }

        public GraphLayoutOptions()
        {
            Direction = GraphLayoutDirection.LeftToRight;
            OriginX = 40f;
            OriginY = 60f;
            LayerSpacing = 150f;
            ItemSpacing = 48f;
            MaximumItemsPerLayer = 8;
            GroupPadding = 30f;
            GroupHeaderHeight = 38f;
            MinimumGroupWidth = 220f;
            MinimumGroupHeight = 140f;
            KeepExistingGroupSizeAsMinimum = false;
            TreatGroupsAsRoutingObstacles = true;
            ObstaclePadding = 12f;
            PortCornerPadding = 15f;
            PortSpacing = 14f;
            RoutingStubLength = 24f;
            RoutingChannelSpacing = 30f;
            RoutingSearchMargin = 260f;
            MaximumRoutingObstacles = 48;
            BendPenalty = 18f;
            LabelGap = 10f;
            LabelPadding = 5f;
            MinimumLabelWidth = 44f;
            MaximumLabelWidth = 600f;
            LabelHeight = 26f;
            LabelCharacterWidth = 14f;
        }

        internal void ThrowIfCancellationRequested()
        {
            if (CancellationRequested != null && CancellationRequested()) throw new OperationCanceledException("自动布局或路由已取消。");
        }

        internal void ReportProgress(int percentage, string message)
        {
            if (ProgressChanged != null) ProgressChanged(Math.Max(0, Math.Min(100, percentage)), message ?? "");
        }

        public static GraphLayoutOptions ForDocument(GraphDocument document)
        {
            GraphLayoutOptions options = new GraphLayoutOptions();
            if (document != null && document.meta != null && document.meta.diagramType == "flowchart")
                options.Direction = GraphLayoutDirection.TopToBottom;
            return options;
        }
    }

    /// <summary>
    /// Orthogonal route and label placement for one edge.  Points always include
    /// the allocated source and target port points.
    /// </summary>
    public sealed class GraphEdgeLayout
    {
        private readonly List<PointF> _points = new List<PointF>();

        public string EdgeId { get; internal set; }
        public string SourceSide { get; internal set; }
        public string TargetSide { get; internal set; }
        public PointF SourcePoint { get; internal set; }
        public PointF TargetPoint { get; internal set; }
        public int SourcePortIndex { get; internal set; }
        public int SourcePortCount { get; internal set; }
        public int TargetPortIndex { get; internal set; }
        public int TargetPortCount { get; internal set; }
        public IList<PointF> Points { get { return _points; } }
        public PointF LabelAnchorPoint { get; internal set; }
        public PointF LabelPoint { get; internal set; }
        public RectangleF LabelBounds { get; internal set; }
        public bool HasObstacleConflict { get; internal set; }
        public float Length { get; internal set; }
        public int BendCount { get; internal set; }
    }

    /// <summary>
    /// A detached result.  Calculating it never mutates the source document.
    /// MainForm may apply bounds as one undoable command, while GraphCanvas may
    /// consume edge routes without adding route points to the persisted schema.
    /// </summary>
    public sealed class GraphLayoutResult
    {
        private readonly Dictionary<string, RectangleF> _nodeBounds = new Dictionary<string, RectangleF>(StringComparer.Ordinal);
        private readonly Dictionary<string, RectangleF> _groupBounds = new Dictionary<string, RectangleF>(StringComparer.Ordinal);
        private readonly Dictionary<string, GraphEdgeLayout> _edges = new Dictionary<string, GraphEdgeLayout>(StringComparer.Ordinal);
        private readonly List<string> _warnings = new List<string>();

        public IDictionary<string, RectangleF> NodeBounds { get { return _nodeBounds; } }
        public IDictionary<string, RectangleF> GroupBounds { get { return _groupBounds; } }
        public IDictionary<string, GraphEdgeLayout> Edges { get { return _edges; } }
        public IList<string> Warnings { get { return _warnings; } }
        public RectangleF ContentBounds { get; internal set; }

        public bool TryGetNodeBounds(string id, out RectangleF bounds)
        {
            return _nodeBounds.TryGetValue(id ?? "", out bounds);
        }

        public bool TryGetGroupBounds(string id, out RectangleF bounds)
        {
            return _groupBounds.TryGetValue(id ?? "", out bounds);
        }

        public bool TryGetEdge(string id, out GraphEdgeLayout edge)
        {
            return _edges.TryGetValue(id ?? "", out edge);
        }
    }

    /// <summary>
    /// Compound layered layout, distributed ports, orthogonal obstacle routing,
    /// and label avoidance.  This type contains no Control/GDI resources and is
    /// safe to call from a worker thread with an immutable document snapshot.
    /// </summary>
    internal static class GraphLayout
    {
        private const float Epsilon = .01f;

        public static GraphLayoutResult Calculate(GraphDocument document)
        {
            return Calculate(document, GraphLayoutOptions.ForDocument(document));
        }

        public static GraphLayoutResult Calculate(GraphDocument document, GraphLayoutOptions options)
        {
            if (document == null) throw new ArgumentNullException("document");
            if (options == null) options = GraphLayoutOptions.ForDocument(document);

            LayoutContext context = new LayoutContext(document, options);
            options.ThrowIfCancellationRequested(); options.ReportProgress(5, "正在分析节点与分组…");
            context.BuildTree();
            options.ThrowIfCancellationRequested(); options.ReportProgress(24, "正在计算分层位置…");
            context.LayoutTree();
            context.EmitBounds();
            options.ThrowIfCancellationRequested(); options.ReportProgress(48, "正在分配端口并计算避障路线…");
            context.LayoutEdges();
            options.ThrowIfCancellationRequested(); options.ReportProgress(86, "正在避让关系标签…");
            context.PlaceLabels();
            context.UpdateContentBounds();
            options.ThrowIfCancellationRequested(); options.ReportProgress(100, "自动排版完成");
            return context.Result;
        }

        /// <summary>
        /// Calculates ports, orthogonal routes, and label positions without
        /// moving or resizing any node or group.  Bounds in the result mirror
        /// the document values exactly so this method can be used after manual
        /// placement or for a route-only refresh.
        /// </summary>
        public static GraphLayoutResult CalculateRoutes(GraphDocument document)
        {
            return CalculateRoutes(document, GraphLayoutOptions.ForDocument(document));
        }

        public static GraphLayoutResult CalculateRoutes(GraphDocument document, GraphLayoutOptions options)
        {
            if (document == null) throw new ArgumentNullException("document");
            if (options == null) options = GraphLayoutOptions.ForDocument(document);

            LayoutContext context = new LayoutContext(document, options);
            options.ThrowIfCancellationRequested(); options.ReportProgress(10, "正在读取当前布局…");
            context.MirrorCurrentBounds();
            options.ThrowIfCancellationRequested(); options.ReportProgress(35, "正在分配端口并计算避障路线…");
            context.LayoutEdges();
            options.ThrowIfCancellationRequested(); options.ReportProgress(85, "正在避让关系标签…");
            context.PlaceLabels();
            context.UpdateContentBounds();
            options.ThrowIfCancellationRequested(); options.ReportProgress(100, "自动路由完成");
            return context.Result;
        }

        private sealed class LayoutContext
        {
            private readonly GraphDocument _document;
            private readonly GraphLayoutOptions _options;
            private readonly LayoutItem _root;
            private readonly Dictionary<string, LayoutItem> _groups = new Dictionary<string, LayoutItem>(StringComparer.Ordinal);
            private readonly Dictionary<string, LayoutItem> _nodes = new Dictionary<string, LayoutItem>(StringComparer.Ordinal);
            private readonly Dictionary<string, LayoutItem> _endpoints = new Dictionary<string, LayoutItem>(StringComparer.Ordinal);
            private readonly List<LayoutItem> _groupOrder = new List<LayoutItem>();
            private readonly List<EdgeWork> _edgeWork = new List<EdgeWork>();
            private readonly List<Obstacle> _routingObstacles = new List<Obstacle>();
            private int _inputOrder;
            private bool _layoutCycleReported;
            private bool _multipleMembershipReported;

            public readonly GraphLayoutResult Result = new GraphLayoutResult();

            public LayoutContext(GraphDocument document, GraphLayoutOptions options)
            {
                _document = document;
                _options = options;
                _root = new LayoutItem("root", "", "", RectangleF.Empty, -1);
                _root.IsRoot = true;
            }

            public void BuildTree()
            {
                IList<GraphGroup> graphGroups = _document.groups ?? new List<GraphGroup>();
                IList<GraphNode> graphNodes = _document.nodes ?? new List<GraphNode>();

                for (int index = 0; index < graphGroups.Count; index++)
                {
                    GraphGroup group = graphGroups[index];
                    if (group == null) continue;
                    string id = group.id ?? "";
                    if (_groups.ContainsKey(id))
                    {
                        Result.Warnings.Add("分组 ID 重复，自动布局只处理第一项：" + id);
                        continue;
                    }
                    RectangleF bounds = new RectangleF(SafeCoordinate(group.x), SafeCoordinate(group.y), SafeDimension(group.w, 260f, 120f), SafeDimension(group.h, 220f, 100f));
                    LayoutItem item = new LayoutItem(EndpointKey("group", id), "group", id, bounds, _inputOrder++);
                    item.SourceGroup = group;
                    _groups.Add(id, item);
                    _endpoints.Add(item.Key, item);
                    _groupOrder.Add(item);
                }

                for (int index = 0; index < _groupOrder.Count; index++)
                {
                    LayoutItem item = _groupOrder[index];
                    string parentId = LastValidGroupId(item.SourceGroup.groups, item.Id);
                    LayoutItem parent;
                    if (parentId.Length > 0 && _groups.TryGetValue(parentId, out parent)) item.CandidateParent = parent;
                }

                for (int index = 0; index < _groupOrder.Count; index++)
                {
                    LayoutItem item = _groupOrder[index];
                    LayoutItem parent = item.CandidateParent;
                    if (parent == null || ParentChainContains(parent, item))
                    {
                        if (parent != null) Result.Warnings.Add("检测到循环分组，已将分组放到顶层：" + item.Id);
                        parent = _root;
                    }
                    item.Parent = parent;
                    parent.Children.Add(item);
                }

                for (int index = 0; index < graphNodes.Count; index++)
                {
                    GraphNode node = graphNodes[index];
                    if (node == null) continue;
                    string id = node.id ?? "";
                    if (_nodes.ContainsKey(id))
                    {
                        Result.Warnings.Add("节点 ID 重复，自动布局只处理第一项：" + id);
                        continue;
                    }
                    RectangleF bounds = new RectangleF(SafeCoordinate(node.x), SafeCoordinate(node.y), SafeDimension(node.w, 150f, 105f), SafeDimension(node.h, 54f, 46f));
                    LayoutItem item = new LayoutItem(EndpointKey("node", id), "node", id, bounds, _inputOrder++);
                    item.SourceNode = node;
                    _nodes.Add(id, item);
                    _endpoints.Add(item.Key, item);

                    string parentId = node.group ?? "";
                    LayoutItem parent;
                    if (!_groups.TryGetValue(parentId, out parent))
                    {
                        parentId = LastValidGroupId(node.groups, "");
                        if (!_groups.TryGetValue(parentId, out parent)) parent = _root;
                    }
                    item.Parent = parent;
                    parent.Children.Add(item);

                    if (!_multipleMembershipReported && HasIndependentMemberships(node.groups, parent))
                    {
                        Result.Warnings.Add("存在多重分组节点；布局以 node.group（或最内层分组）作为主容器，次要重叠分组不会强制扩张。");
                        _multipleMembershipReported = true;
                    }
                }
            }

            public void MirrorCurrentBounds()
            {
                IList<GraphGroup> graphGroups = _document.groups ?? new List<GraphGroup>();
                IList<GraphNode> graphNodes = _document.nodes ?? new List<GraphNode>();
                for (int index = 0; index < graphGroups.Count; index++)
                {
                    GraphGroup group = graphGroups[index];
                    if (group == null) continue;
                    string id = group.id ?? "";
                    if (Result.GroupBounds.ContainsKey(id))
                    {
                        Result.Warnings.Add("分组 ID 重复，仅能为第一项生成路线边界：" + id);
                        continue;
                    }
                    Result.GroupBounds.Add(id, new RectangleF(group.x, group.y, group.w, group.h));
                }
                for (int index = 0; index < graphNodes.Count; index++)
                {
                    GraphNode node = graphNodes[index];
                    if (node == null) continue;
                    string id = node.id ?? "";
                    if (Result.NodeBounds.ContainsKey(id))
                    {
                        Result.Warnings.Add("节点 ID 重复，仅能为第一项生成路线边界：" + id);
                        continue;
                    }
                    Result.NodeBounds.Add(id, new RectangleF(node.x, node.y, node.w, node.h));
                }
            }

            public void LayoutTree()
            {
                for (int index = 0; index < _root.Children.Count; index++)
                {
                    LayoutItem child = _root.Children[index];
                    if (child.EntityType == "group") LayoutContainer(child);
                }
                PlaceChildren(_root);
            }

            private void LayoutContainer(LayoutItem container)
            {
                for (int index = 0; index < container.Children.Count; index++)
                {
                    LayoutItem child = container.Children[index];
                    if (child.EntityType == "group") LayoutContainer(child);
                }

                if (container.Children.Count > 0)
                {
                    PlaceChildren(container);
                    RectangleF content = ChildBounds(container.Children);
                    float shiftX = _options.GroupPadding - content.Left;
                    float shiftY = _options.GroupHeaderHeight + _options.GroupPadding - content.Top;
                    for (int index = 0; index < container.Children.Count; index++)
                    {
                        container.Children[index].X += shiftX;
                        container.Children[index].Y += shiftY;
                    }
                    float width = content.Width + _options.GroupPadding * 2f;
                    float height = content.Height + _options.GroupHeaderHeight + _options.GroupPadding * 2f;
                    container.Width = Math.Max(SafePositive(_options.MinimumGroupWidth, 220f), width);
                    container.Height = Math.Max(SafePositive(_options.MinimumGroupHeight, 140f), height);
                }
                else
                {
                    container.Width = Math.Max(SafePositive(_options.MinimumGroupWidth, 220f), 120f);
                    container.Height = Math.Max(SafePositive(_options.MinimumGroupHeight, 140f), 100f);
                }

                if (_options.KeepExistingGroupSizeAsMinimum)
                {
                    container.Width = Math.Max(container.Width, container.Existing.Width);
                    container.Height = Math.Max(container.Height, container.Existing.Height);
                }
            }

            private void PlaceChildren(LayoutItem container)
            {
                if (container.Children.Count == 0) return;
                List<LayerArc> arcs = BuildCollapsedArcs(container);
                Dictionary<LayoutItem, int> layers = AssignLayers(container.Children, arcs);
                List<List<LayoutItem>> orderedLayers = OrderLayers(container.Children, arcs, layers);
                PositionLayers(orderedLayers);
            }

            private List<LayerArc> BuildCollapsedArcs(LayoutItem container)
            {
                List<LayerArc> arcs = new List<LayerArc>();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                IList<GraphEdge> edges = _document.edges ?? new List<GraphEdge>();
                for (int index = 0; index < edges.Count; index++)
                {
                    GraphEdge edge = edges[index];
                    if (edge == null) continue;
                    LayoutItem source = MapEndpointToDirectChild(edge.sourceType, edge.source, container);
                    LayoutItem target = MapEndpointToDirectChild(edge.targetType, edge.target, container);
                    if (source == null || target == null || source == target) continue;
                    string pair = source.Key + "\0" + target.Key;
                    if (!seen.Add(pair)) continue;
                    arcs.Add(new LayerArc(source, target));
                }
                return arcs;
            }

            private LayoutItem MapEndpointToDirectChild(string type, string id, LayoutItem container)
            {
                LayoutItem item;
                if (!_endpoints.TryGetValue(EndpointKey(type, id), out item) || item == container) return null;
                while (item.Parent != null && item.Parent != container) item = item.Parent;
                return item.Parent == container ? item : null;
            }

            private Dictionary<LayoutItem, int> AssignLayers(List<LayoutItem> items, List<LayerArc> arcs)
            {
                Dictionary<LayoutItem, int> layer = new Dictionary<LayoutItem, int>();
                Dictionary<LayoutItem, int> indegree = new Dictionary<LayoutItem, int>();
                Dictionary<LayoutItem, List<LayoutItem>> outgoing = new Dictionary<LayoutItem, List<LayoutItem>>();
                HashSet<LayoutItem> remaining = new HashSet<LayoutItem>();
                List<LayoutItem> ready = new List<LayoutItem>();

                for (int index = 0; index < items.Count; index++)
                {
                    LayoutItem item = items[index];
                    layer[item] = 0;
                    indegree[item] = 0;
                    outgoing[item] = new List<LayoutItem>();
                    remaining.Add(item);
                }
                for (int index = 0; index < arcs.Count; index++)
                {
                    LayerArc arc = arcs[index];
                    outgoing[arc.Source].Add(arc.Target);
                    indegree[arc.Target] = indegree[arc.Target] + 1;
                }

                if (arcs.Count == 0)
                {
                    int maximum = Math.Max(1, _options.MaximumItemsPerLayer);
                    List<LayoutItem> stable = new List<LayoutItem>(items);
                    stable.Sort(CompareInputOrder);
                    for (int index = 0; index < stable.Count; index++) layer[stable[index]] = index / maximum;
                    return layer;
                }

                for (int index = 0; index < items.Count; index++) if (indegree[items[index]] == 0) ready.Add(items[index]);
                while (remaining.Count > 0)
                {
                    if (ready.Count == 0)
                    {
                        LayoutItem breaker = FirstByInputOrder(remaining);
                        indegree[breaker] = 0;
                        ready.Add(breaker);
                        if (!_layoutCycleReported)
                        {
                            Result.Warnings.Add("关系中存在环；分层时已确定性地选择一条回边，不会修改原关系方向。");
                            _layoutCycleReported = true;
                        }
                    }
                    ready.Sort(CompareInputOrder);
                    LayoutItem current = ready[0];
                    ready.RemoveAt(0);
                    if (!remaining.Remove(current)) continue;
                    List<LayoutItem> next = outgoing[current];
                    for (int index = 0; index < next.Count; index++)
                    {
                        LayoutItem target = next[index];
                        if (!remaining.Contains(target)) continue;
                        layer[target] = Math.Max(layer[target], layer[current] + 1);
                        indegree[target] = indegree[target] - 1;
                        if (indegree[target] <= 0) ready.Add(target);
                    }
                }
                return layer;
            }

            private List<List<LayoutItem>> OrderLayers(List<LayoutItem> items, List<LayerArc> arcs, Dictionary<LayoutItem, int> layerByItem)
            {
                int maximumLayer = 0;
                for (int index = 0; index < items.Count; index++) maximumLayer = Math.Max(maximumLayer, layerByItem[items[index]]);
                List<List<LayoutItem>> layers = new List<List<LayoutItem>>();
                for (int index = 0; index <= maximumLayer; index++) layers.Add(new List<LayoutItem>());
                for (int index = 0; index < items.Count; index++) layers[layerByItem[items[index]]].Add(items[index]);
                for (int index = 0; index < layers.Count; index++) layers[index].Sort(CompareInputOrder);

                Dictionary<LayoutItem, List<LayoutItem>> predecessors = new Dictionary<LayoutItem, List<LayoutItem>>();
                Dictionary<LayoutItem, List<LayoutItem>> successors = new Dictionary<LayoutItem, List<LayoutItem>>();
                for (int index = 0; index < items.Count; index++)
                {
                    predecessors[items[index]] = new List<LayoutItem>();
                    successors[items[index]] = new List<LayoutItem>();
                }
                for (int index = 0; index < arcs.Count; index++)
                {
                    predecessors[arcs[index].Target].Add(arcs[index].Source);
                    successors[arcs[index].Source].Add(arcs[index].Target);
                }

                for (int pass = 0; pass < 4; pass++)
                {
                    Dictionary<LayoutItem, int> positions = LayerPositions(layers);
                    for (int layer = 1; layer < layers.Count; layer++)
                        SortByBarycenter(layers[layer], predecessors, positions);
                    positions = LayerPositions(layers);
                    for (int layer = layers.Count - 2; layer >= 0; layer--)
                        SortByBarycenter(layers[layer], successors, positions);
                }
                return layers;
            }

            private void SortByBarycenter(List<LayoutItem> layer, Dictionary<LayoutItem, List<LayoutItem>> neighbors, Dictionary<LayoutItem, int> positions)
            {
                Dictionary<LayoutItem, float> values = new Dictionary<LayoutItem, float>();
                for (int index = 0; index < layer.Count; index++)
                {
                    LayoutItem item = layer[index];
                    List<LayoutItem> adjacent = neighbors[item];
                    float total = 0f;
                    int count = 0;
                    for (int neighborIndex = 0; neighborIndex < adjacent.Count; neighborIndex++)
                    {
                        int position;
                        if (positions.TryGetValue(adjacent[neighborIndex], out position))
                        {
                            total += position;
                            count++;
                        }
                    }
                    values[item] = count == 0 ? index : total / count;
                }
                layer.Sort(delegate(LayoutItem first, LayoutItem second)
                {
                    int comparison = values[first].CompareTo(values[second]);
                    return comparison != 0 ? comparison : CompareInputOrder(first, second);
                });
            }

            private void PositionLayers(List<List<LayoutItem>> layers)
            {
                float itemSpacing = SafePositive(_options.ItemSpacing, 48f);
                float layerSpacing = SafePositive(_options.LayerSpacing, 150f);
                float maximumBreadth = 0f;
                float[] breadths = new float[layers.Count];
                float[] primarySizes = new float[layers.Count];
                for (int layer = 0; layer < layers.Count; layer++)
                {
                    float breadth = 0f;
                    float primary = 0f;
                    for (int index = 0; index < layers[layer].Count; index++)
                    {
                        LayoutItem item = layers[layer][index];
                        breadth += SecondarySize(item);
                        if (index > 0) breadth += itemSpacing;
                        primary = Math.Max(primary, PrimarySize(item));
                    }
                    breadths[layer] = breadth;
                    primarySizes[layer] = primary;
                    maximumBreadth = Math.Max(maximumBreadth, breadth);
                }

                float primaryPosition = 0f;
                for (int layer = 0; layer < layers.Count; layer++)
                {
                    float secondaryPosition = (maximumBreadth - breadths[layer]) / 2f;
                    for (int index = 0; index < layers[layer].Count; index++)
                    {
                        LayoutItem item = layers[layer][index];
                        if (_options.Direction == GraphLayoutDirection.LeftToRight)
                        {
                            item.X = primaryPosition + (primarySizes[layer] - item.Width) / 2f;
                            item.Y = secondaryPosition;
                        }
                        else
                        {
                            item.X = secondaryPosition;
                            item.Y = primaryPosition + (primarySizes[layer] - item.Height) / 2f;
                        }
                        secondaryPosition += SecondarySize(item) + itemSpacing;
                    }
                    primaryPosition += primarySizes[layer] + layerSpacing;
                }
            }

            public void EmitBounds()
            {
                EmitChildren(_root, SafeCoordinate(_options.OriginX), SafeCoordinate(_options.OriginY));
            }

            private void EmitChildren(LayoutItem container, float parentX, float parentY)
            {
                for (int index = 0; index < container.Children.Count; index++)
                {
                    LayoutItem item = container.Children[index];
                    float x = parentX + item.X;
                    float y = parentY + item.Y;
                    RectangleF bounds = new RectangleF(x, y, item.Width, item.Height);
                    if (item.EntityType == "node") Result.NodeBounds[item.Id] = bounds;
                    else
                    {
                        Result.GroupBounds[item.Id] = bounds;
                        EmitChildren(item, x, y);
                    }
                }
            }

            public void LayoutEdges()
            {
                _options.ThrowIfCancellationRequested();
                BuildEdgeWork();
                AllocatePorts();
                BuildRoutingObstacles();
                for (int index = 0; index < _edgeWork.Count; index++)
                {
                    if ((index & 15) == 0)
                    {
                        _options.ThrowIfCancellationRequested();
                        _options.ReportProgress(48 + (int)(Math.Min(1f, index / (float)Math.Max(1, _edgeWork.Count)) * 36f), "正在计算第 " + (index + 1) + " / " + _edgeWork.Count + " 条关系路线…");
                    }
                    RouteEdge(_edgeWork[index]);
                }
                foreach (EdgeWork work in _edgeWork) { _options.ThrowIfCancellationRequested(); ReduceCrossings(work); }
            }

            private void BuildEdgeWork()
            {
                IList<GraphEdge> edges = _document.edges ?? new List<GraphEdge>();
                HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < edges.Count; index++)
                {
                    GraphEdge edge = edges[index];
                    if (edge == null) continue;
                    string id = edge.id ?? "";
                    if (!usedIds.Add(id))
                    {
                        Result.Warnings.Add("关系 ID 重复，自动路由只处理第一项：" + id);
                        continue;
                    }
                    RectangleF sourceBounds;
                    RectangleF targetBounds;
                    string sourceKey = EndpointKey(edge.sourceType, edge.source);
                    string targetKey = EndpointKey(edge.targetType, edge.target);
                    if (!TryGetBounds(sourceKey, out sourceBounds) || !TryGetBounds(targetKey, out targetBounds) ||
                        !IsUsableBounds(sourceBounds) || !IsUsableBounds(targetBounds) || sourceKey == targetKey)
                    {
                        Result.Warnings.Add("关系端点无效，未生成路线：" + id);
                        continue;
                    }
                    EdgeWork work = new EdgeWork(edge, index, sourceKey, targetKey, sourceBounds, targetBounds);
                    ChooseSides(work);
                    _edgeWork.Add(work);
                }
            }

            private void ChooseSides(EdgeWork work)
            {
                PointF source = Center(work.SourceBounds);
                PointF target = Center(work.TargetBounds);
                float dx = target.X - source.X;
                float dy = target.Y - source.Y;
                if (_options.Direction == GraphLayoutDirection.LeftToRight)
                {
                    if (Math.Abs(dx) >= Math.Abs(dy) * .45f)
                    {
                        work.SourceSide = dx >= 0f ? "right" : "left";
                        work.TargetSide = dx >= 0f ? "left" : "right";
                    }
                    else
                    {
                        work.SourceSide = dy >= 0f ? "bottom" : "top";
                        work.TargetSide = dy >= 0f ? "top" : "bottom";
                    }
                }
                else
                {
                    if (Math.Abs(dy) >= Math.Abs(dx) * .45f)
                    {
                        work.SourceSide = dy >= 0f ? "bottom" : "top";
                        work.TargetSide = dy >= 0f ? "top" : "bottom";
                    }
                    else
                    {
                        work.SourceSide = dx >= 0f ? "right" : "left";
                        work.TargetSide = dx >= 0f ? "left" : "right";
                    }
                }
            }

            private void AllocatePorts()
            {
                Dictionary<string, List<PortRequest>> requests = new Dictionary<string, List<PortRequest>>(StringComparer.Ordinal);
                for (int index = 0; index < _edgeWork.Count; index++)
                {
                    if ((index & 15) == 0)
                    {
                        _options.ThrowIfCancellationRequested();
                        _options.ReportProgress(86 + (int)(Math.Min(1f, index / (float)Math.Max(1, _edgeWork.Count)) * 12f), "正在放置第 " + (index + 1) + " / " + _edgeWork.Count + " 个关系标签…");
                    }
                    EdgeWork work = _edgeWork[index];
                    AddPortRequest(requests, new PortRequest(work, true));
                    AddPortRequest(requests, new PortRequest(work, false));
                }
                foreach (KeyValuePair<string, List<PortRequest>> pair in requests)
                {
                    List<PortRequest> list = pair.Value;
                    list.Sort(delegate(PortRequest first, PortRequest second)
                    {
                        int comparison = first.OppositeCoordinate.CompareTo(second.OppositeCoordinate);
                        return comparison != 0 ? comparison : first.Work.InputOrder.CompareTo(second.Work.InputOrder);
                    });
                    for (int index = 0; index < list.Count; index++)
                    {
                        PortRequest request = list[index];
                        RectangleF bounds = request.Source ? request.Work.SourceBounds : request.Work.TargetBounds;
                        string side = request.Source ? request.Work.SourceSide : request.Work.TargetSide;
                        PointF point = SharedPortPoint(bounds, side);
                        // Preserve a shared trunk only for the same origin. Other origins
                        // receive separate entry points, keeping the best aligned one centered.
                        List<string> origins = list.Select(item => item.Work.SourceKey).Distinct().ToList();
                        if (origins.Count > 1)
                        {
                            float center = side == "left" || side == "right" ? bounds.Top + bounds.Height / 2f : bounds.Left + bounds.Width / 2f;
                            PortRequest anchor = list.OrderBy(item => Math.Abs(item.OppositeCoordinate - center)).First();
                            int anchorIndex = origins.IndexOf(anchor.Work.SourceKey);
                            int rank = origins.IndexOf(request.Work.SourceKey) - anchorIndex;
                            float length = side == "left" || side == "right" ? bounds.Height : bounds.Width;
                            float step = Math.Min(14f, Math.Max(0f, length / 2f - 6f) / Math.Max(1, Math.Max(anchorIndex, origins.Count - 1 - anchorIndex)));
                            if (side == "left" || side == "right") point.Y += rank * step;
                            else point.X += rank * step;
                        }
                        if (request.Source)
                        {
                            request.Work.SourcePoint = point;
                            request.Work.SourcePortIndex = index;
                            request.Work.SourcePortCount = list.Count;
                        }
                        else
                        {
                            request.Work.TargetPoint = point;
                            request.Work.TargetPortIndex = index;
                            request.Work.TargetPortCount = list.Count;
                        }
                    }
                }
            }

            private void AddPortRequest(Dictionary<string, List<PortRequest>> requests, PortRequest request)
            {
                string endpoint = request.Source ? request.Work.SourceKey : request.Work.TargetKey;
                string side = request.Source ? request.Work.SourceSide : request.Work.TargetSide;
                string key = endpoint + "|" + side;
                List<PortRequest> list;
                if (!requests.TryGetValue(key, out list))
                {
                    list = new List<PortRequest>();
                    requests.Add(key, list);
                }
                PointF opposite = Center(request.Source ? request.Work.TargetBounds : request.Work.SourceBounds);
                request.OppositeCoordinate = side == "left" || side == "right" ? opposite.Y : opposite.X;
                list.Add(request);
            }

            private static PointF SharedPortPoint(RectangleF bounds, string side)
            {
                // A side has one shared center port; branches may share their trunk.
                if (side == "left") return new PointF(bounds.Left, bounds.Top + bounds.Height / 2f);
                if (side == "right") return new PointF(bounds.Right, bounds.Top + bounds.Height / 2f);
                if (side == "top") return new PointF(bounds.Left + bounds.Width / 2f, bounds.Top);
                return new PointF(bounds.Left + bounds.Width / 2f, bounds.Bottom);
            }

            private List<PointF> SharedBranchPath(EdgeWork work, List<Obstacle> obstacles)
            {
                PointF source = work.SourcePoint, target = work.TargetPoint;
                for (int lane = 0; lane < 17; lane++)
                {
                float fraction = lane == 0 ? .5f : lane == 1 ? .25f : lane == 2 ? .75f : .5f + ((lane % 2 == 1 ? -1 : 1) * ((lane + 1) / 2) / 18f);
                List<PointF> points = null;
                if ((work.SourceSide == "right" && work.TargetSide == "left" && target.X > source.X)
                    || (work.SourceSide == "left" && work.TargetSide == "right" && target.X < source.X))
                {
                    float middle = source.X + (target.X - source.X) * fraction;
                    points = new List<PointF>(new[] { source, new PointF(middle, source.Y), new PointF(middle, target.Y), target });
                }
                else if ((work.SourceSide == "bottom" && work.TargetSide == "top" && target.Y > source.Y)
                    || (work.SourceSide == "top" && work.TargetSide == "bottom" && target.Y < source.Y))
                {
                    float middle = source.Y + (target.Y - source.Y) * fraction;
                    points = new List<PointF>(new[] { source, new PointF(source.X, middle), new PointF(target.X, middle), target });
                }
                if (points == null) return null;
                points = SimplifyPath(points);
                if (PathClear(points, obstacles) && !HasAmbiguousOverlap(work, points)) return points;
                }
                return null;
            }
            private bool HasAmbiguousOverlap(EdgeWork work, IList<PointF> points)
            {
                foreach (EdgeWork other in _edgeWork)
                {
                    if (other == work || other.Route == null || other.SourceKey == work.SourceKey) continue;
                    for (int i = 1; i < points.Count; i++)
                        for (int j = 1; j < other.Route.Points.Count; j++)
                        {
                            PointF a = points[i - 1], b = points[i], c = other.Route.Points[j - 1], d = other.Route.Points[j];
                            bool horizontal = Math.Abs(a.Y - b.Y) < Epsilon && Math.Abs(c.Y - d.Y) < Epsilon && Math.Abs(a.Y - c.Y) < Epsilon;
                            bool vertical = Math.Abs(a.X - b.X) < Epsilon && Math.Abs(c.X - d.X) < Epsilon && Math.Abs(a.X - c.X) < Epsilon;
                            if (horizontal && Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) - Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) > Epsilon) return true;
                            if (vertical && Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) - Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) > Epsilon) return true;
                        }
                }
                return false;
            }
            private int CrossingCount(EdgeWork work, IList<PointF> points)
            {
                int count = 0;
                foreach (EdgeWork other in _edgeWork)
                {
                    if (other == work || other.Route == null || other.SourceKey == work.SourceKey) continue;
                    IList<PointF> line = other.Route.Points;
                    for (int i = 1; i < points.Count; i++)
                        for (int j = 1; j < line.Count; j++)
                        {
                            PointF a = points[i - 1], b = points[i], c = line[j - 1], d = line[j];
                            bool horizontal = Math.Abs(a.Y - b.Y) < Epsilon;
                            if (horizontal == (Math.Abs(c.Y - d.Y) < Epsilon)) continue;
                            PointF intersection = horizontal ? new PointF(c.X, a.Y) : new PointF(a.X, c.Y);
                            if (intersection.X < Math.Min(a.X, b.X) - Epsilon || intersection.X > Math.Max(a.X, b.X) + Epsilon || intersection.Y < Math.Min(a.Y, b.Y) - Epsilon || intersection.Y > Math.Max(a.Y, b.Y) + Epsilon
                                || intersection.X < Math.Min(c.X, d.X) - Epsilon || intersection.X > Math.Max(c.X, d.X) + Epsilon || intersection.Y < Math.Min(c.Y, d.Y) - Epsilon || intersection.Y > Math.Max(c.Y, d.Y) + Epsilon) continue;
                            if ((SamePoint(intersection, points[0]) || SamePoint(intersection, points[points.Count - 1])) && (SamePoint(intersection, line[0]) || SamePoint(intersection, line[line.Count - 1]))) continue;
                            count++;
                        }
                }
                return count;
            }

            private void ReduceCrossings(EdgeWork work)
            {
                GraphEdgeLayout route = work.Route;
                if (route == null || route.Points.Count == 2 || route.HasObstacleConflict || CrossingCount(work, route.Points) == 0) return;
                List<Obstacle> obstacles = ObstaclesFor(work);
                foreach (EdgeWork other in _edgeWork)
                {
                    if (other == work || other.Route == null || other.SourceKey == work.SourceKey) continue;
                    for (int i = 1; i < other.Route.Points.Count; i++)
                    {
                        PointF a = other.Route.Points[i - 1], b = other.Route.Points[i];
                        RectangleF barrier = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                        barrier.Inflate(5f, 5f);
                        obstacles.Add(new Obstacle("line:" + other.Edge.id + ":" + i, barrier, false));
                    }
                }
                string[] sourceSides = new[] { work.SourceSide, "right", "bottom", "left", "top" }.Distinct().ToArray();
                string[] targetSides = new[] { work.TargetSide, "left", "top", "right", "bottom" }.Distinct().ToArray();
                List<PointF> best = null;
                string bestSource = work.SourceSide, bestTarget = work.TargetSide;
                float bestScore = Single.MaxValue;
                foreach (string sourceSide in sourceSides)
                    foreach (string targetSide in targetSides)
                    {
                        _options.ThrowIfCancellationRequested();
                        PointF source = sourceSide == work.SourceSide ? work.SourcePoint : SharedPortPoint(work.SourceBounds, sourceSide);
                        PointF target = targetSide == work.TargetSide ? work.TargetPoint : SharedPortPoint(work.TargetBounds, targetSide);
                        PointF start = OffsetBySide(source, sourceSide, 12f), end = OffsetBySide(target, targetSide, 12f);
                        if (!SegmentClear(source, start, obstacles) || !SegmentClear(end, target, obstacles)) continue;
                        // A bounded candidate search keeps dense graphs responsive.
                        List<PointF> core = FindCandidatePath(start, end, obstacles);
                        if (core == null) continue;
                        List<PointF> candidate = new List<PointF>(); candidate.Add(source); AppendWithoutDuplicate(candidate, core); candidate.Add(target);
                        candidate = SimplifyPath(candidate);
                        if (!PathClear(candidate, obstacles) || HasAmbiguousOverlap(work, candidate) || CrossingCount(work, candidate) != 0) continue;
                        float score = PathLength(candidate) + candidate.Count * 18f + (sourceSide == work.SourceSide ? 0 : 12f) + (targetSide == work.TargetSide ? 0 : 12f);
                        if (score < bestScore) { best = candidate; bestScore = score; bestSource = sourceSide; bestTarget = targetSide; }
                    }
                if (best == null) return;
                route.Points.Clear(); foreach (PointF point in best) route.Points.Add(point);
                route.SourceSide = bestSource; route.TargetSide = bestTarget;
                route.SourcePoint = best[0]; route.TargetPoint = best[best.Count - 1];
                if (bestSource != work.SourceSide) { route.SourcePortIndex = 0; route.SourcePortCount = 1; }
                if (bestTarget != work.TargetSide) { route.TargetPortIndex = 0; route.TargetPortCount = 1; }
                work.SourceSide = bestSource; work.TargetSide = bestTarget; work.SourcePoint = route.SourcePoint; work.TargetPoint = route.TargetPoint;
                route.Length = PathLength(best); route.BendCount = Math.Max(0, best.Count - 2);
            }
            private void BuildRoutingObstacles()
            {
                foreach (KeyValuePair<string, RectangleF> pair in Result.NodeBounds)
                    _routingObstacles.Add(new Obstacle(EndpointKey("node", pair.Key), Inflated(pair.Value, _options.ObstaclePadding), false));
                if (_options.TreatGroupsAsRoutingObstacles)
                    foreach (KeyValuePair<string, RectangleF> pair in Result.GroupBounds)
                        _routingObstacles.Add(new Obstacle(EndpointKey("group", pair.Key), Inflated(pair.Value, _options.ObstaclePadding), true));
            }

            private void RouteEdge(EdgeWork work)
            {
                List<Obstacle> obstacles = ObstaclesFor(work);
                PointF sourceStub = OffsetBySide(work.SourcePoint, work.SourceSide, SafePositive(_options.RoutingStubLength, 24f));
                PointF targetStub = OffsetBySide(work.TargetPoint, work.TargetSide, SafePositive(_options.RoutingStubLength, 24f));
                if (!SegmentClear(work.SourcePoint, sourceStub, obstacles)) sourceStub = work.SourcePoint;
                if (!SegmentClear(targetStub, work.TargetPoint, obstacles)) targetStub = work.TargetPoint;

                bool safe;
                List<PointF> core = FindOrthogonalPath(sourceStub, targetStub, obstacles, out safe);
                List<PointF> points = new List<PointF>();
                points.Add(work.SourcePoint);
                if (!SamePoint(work.SourcePoint, sourceStub)) points.Add(sourceStub);
                AppendWithoutDuplicate(points, core);
                if (!SamePoint(targetStub, work.TargetPoint)) points.Add(work.TargetPoint);
                points = SimplifyPath(points);
                List<PointF> sharedBranch = SharedBranchPath(work, obstacles);
                if (sharedBranch != null) { points = sharedBranch; safe = true; }
                safe = safe && PathClear(points, obstacles);

                GraphEdgeLayout route = new GraphEdgeLayout();
                route.EdgeId = work.Edge.id ?? "";
                route.SourceSide = work.SourceSide;
                route.TargetSide = work.TargetSide;
                route.SourcePoint = work.SourcePoint;
                route.TargetPoint = work.TargetPoint;
                route.SourcePortIndex = work.SourcePortIndex;
                route.SourcePortCount = work.SourcePortCount;
                route.TargetPortIndex = work.TargetPortIndex;
                route.TargetPortCount = work.TargetPortCount;
                route.HasObstacleConflict = !safe;
                route.Length = PathLength(points);
                route.BendCount = Math.Max(0, points.Count - 2);
                for (int index = 0; index < points.Count; index++) route.Points.Add(points[index]);
                Result.Edges[route.EdgeId] = route;
                work.Route = route;
                if (!safe) Result.Warnings.Add("未能为关系找到完全无障碍的正交路线：" + route.EdgeId);
            }

            private List<Obstacle> ObstaclesFor(EdgeWork work)
            {
                List<Obstacle> result = new List<Obstacle>();
                PointF sourceCenter = Center(work.SourceBounds);
                PointF targetCenter = Center(work.TargetBounds);
                for (int index = 0; index < _routingObstacles.Count; index++)
                {
                    Obstacle obstacle = _routingObstacles[index];
                    if (obstacle.Key == work.SourceKey || obstacle.Key == work.TargetKey) continue;
                    if (obstacle.IsGroup && (ContainsInterior(obstacle.Bounds, sourceCenter) || ContainsInterior(obstacle.Bounds, targetCenter))) continue;
                    result.Add(obstacle);
                }
                // Endpoint rectangles are kept without extra padding.  Starting on
                // their boundary is legal, but a detour may not turn back through
                // either endpoint after leaving its allocated port.
                result.Add(new Obstacle(work.SourceKey, work.SourceBounds, work.SourceKey.StartsWith("group:", StringComparison.Ordinal)));
                result.Add(new Obstacle(work.TargetKey, work.TargetBounds, work.TargetKey.StartsWith("group:", StringComparison.Ordinal)));
                return result;
            }

            private List<PointF> FindOrthogonalPath(PointF start, PointF end, List<Obstacle> obstacles, out bool safe)
            {
                if (SamePoint(start, end))
                {
                    safe = true;
                    return new List<PointF>(new PointF[] { start, end });
                }

                List<Obstacle> relevant = RelevantObstacles(start, end, obstacles);
                List<PointF> path = FindCandidatePath(start, end, relevant);
                if (path != null && PathClear(path, obstacles))
                {
                    safe = true;
                    return path;
                }

                if (path != null)
                {
                    AddCollidingObstacles(path, obstacles, relevant);
                    path = FindCandidatePath(start, end, relevant);
                    if (path != null && PathClear(path, obstacles))
                    {
                        safe = true;
                        return path;
                    }
                }

                List<PointF> gridPath = FindGridPath(start, end, relevant);
                if (gridPath != null && PathClear(gridPath, obstacles))
                {
                    safe = true;
                    return gridPath;
                }

                path = BestEffortPath(start, end, obstacles);
                safe = PathClear(path, obstacles);
                return path;
            }

            private List<PointF> FindCandidatePath(PointF start, PointF end, List<Obstacle> obstacles)
            {
                List<List<PointF>> candidates = new List<List<PointF>>();
                if (NearlyEqual(start.X, end.X) || NearlyEqual(start.Y, end.Y)) candidates.Add(new List<PointF>(new PointF[] { start, end }));
                candidates.Add(new List<PointF>(new PointF[] { start, new PointF(end.X, start.Y), end }));
                candidates.Add(new List<PointF>(new PointF[] { start, new PointF(start.X, end.Y), end }));

                List<float> xChannels = new List<float>();
                List<float> yChannels = new List<float>();
                AddUnique(xChannels, (start.X + end.X) / 2f);
                AddUnique(yChannels, (start.Y + end.Y) / 2f);
                float channel = SafePositive(_options.RoutingChannelSpacing, 30f);
                AddUnique(xChannels, Math.Min(start.X, end.X) - channel);
                AddUnique(xChannels, Math.Max(start.X, end.X) + channel);
                AddUnique(yChannels, Math.Min(start.Y, end.Y) - channel);
                AddUnique(yChannels, Math.Max(start.Y, end.Y) + channel);
                for (int index = 0; index < obstacles.Count; index++)
                {
                    AddUnique(xChannels, obstacles[index].Bounds.Left);
                    AddUnique(xChannels, obstacles[index].Bounds.Right);
                    AddUnique(yChannels, obstacles[index].Bounds.Top);
                    AddUnique(yChannels, obstacles[index].Bounds.Bottom);
                }
                for (int index = 0; index < xChannels.Count; index++)
                {
                    float x = xChannels[index];
                    candidates.Add(new List<PointF>(new PointF[] { start, new PointF(x, start.Y), new PointF(x, end.Y), end }));
                }
                for (int index = 0; index < yChannels.Count; index++)
                {
                    float y = yChannels[index];
                    candidates.Add(new List<PointF>(new PointF[] { start, new PointF(start.X, y), new PointF(end.X, y), end }));
                }

                List<PointF> best = null;
                float bestScore = Single.MaxValue;
                for (int index = 0; index < candidates.Count; index++)
                {
                    List<PointF> candidate = SimplifyPath(candidates[index]);
                    if (!PathClear(candidate, obstacles)) continue;
                    float score = PathLength(candidate) + Math.Max(0, candidate.Count - 2) * SafePositive(_options.BendPenalty, 18f);
                    if (score < bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }
                return best;
            }

            private List<PointF> FindGridPath(PointF start, PointF end, List<Obstacle> obstacles)
            {
                List<float> xs = new List<float>();
                List<float> ys = new List<float>();
                AddUnique(xs, start.X);
                AddUnique(xs, end.X);
                AddUnique(ys, start.Y);
                AddUnique(ys, end.Y);
                float channel = SafePositive(_options.RoutingChannelSpacing, 30f);
                AddUnique(xs, Math.Min(start.X, end.X) - channel);
                AddUnique(xs, Math.Max(start.X, end.X) + channel);
                AddUnique(ys, Math.Min(start.Y, end.Y) - channel);
                AddUnique(ys, Math.Max(start.Y, end.Y) + channel);
                for (int index = 0; index < obstacles.Count; index++)
                {
                    AddUnique(xs, obstacles[index].Bounds.Left);
                    AddUnique(xs, obstacles[index].Bounds.Right);
                    AddUnique(ys, obstacles[index].Bounds.Top);
                    AddUnique(ys, obstacles[index].Bounds.Bottom);
                }
                xs.Sort();
                ys.Sort();
                if (xs.Count == 0 || ys.Count == 0 || xs.Count * ys.Count > 20000) return null;

                int startX = FindCoordinate(xs, start.X);
                int startY = FindCoordinate(ys, start.Y);
                int endX = FindCoordinate(xs, end.X);
                int endY = FindCoordinate(ys, end.Y);
                if (startX < 0 || startY < 0 || endX < 0 || endY < 0) return null;
                int width = xs.Count;
                int total = xs.Count * ys.Count;
                float[] distance = new float[total];
                int[] previous = new int[total];
                bool[] closed = new bool[total];
                for (int index = 0; index < total; index++) { distance[index] = Single.MaxValue; previous[index] = -1; }
                int startState = startY * width + startX;
                int endState = endY * width + endX;
                distance[startState] = 0f;
                MinHeap heap = new MinHeap();
                heap.Push(new HeapEntry(startState, Manhattan(start, end), 0f));
                int[] dx = new int[] { -1, 1, 0, 0 };
                int[] dy = new int[] { 0, 0, -1, 1 };

                int expandedStates = 0;
                while (heap.Count > 0)
                {
                    if ((expandedStates++ & 127) == 0) _options.ThrowIfCancellationRequested();
                    HeapEntry entry = heap.Pop();
                    int state = entry.State;
                    if (closed[state] || entry.Distance > distance[state] + Epsilon) continue;
                    if (state == endState) break;
                    closed[state] = true;
                    int xIndex = state % width;
                    int yIndex = state / width;
                    PointF point = new PointF(xs[xIndex], ys[yIndex]);
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int nextX = xIndex + dx[direction];
                        int nextY = yIndex + dy[direction];
                        if (nextX < 0 || nextX >= xs.Count || nextY < 0 || nextY >= ys.Count) continue;
                        PointF nextPoint = new PointF(xs[nextX], ys[nextY]);
                        if (PointBlocked(nextPoint, obstacles) || !SegmentClear(point, nextPoint, obstacles)) continue;
                        int nextState = nextY * width + nextX;
                        if (closed[nextState]) continue;
                        float nextDistance = distance[state] + Manhattan(point, nextPoint);
                        if (nextDistance + Epsilon >= distance[nextState]) continue;
                        distance[nextState] = nextDistance;
                        previous[nextState] = state;
                        float priority = nextDistance + Manhattan(nextPoint, end);
                        heap.Push(new HeapEntry(nextState, priority, nextDistance));
                    }
                }
                if (distance[endState] == Single.MaxValue) return null;
                List<PointF> reversed = new List<PointF>();
                int cursor = endState;
                while (cursor >= 0)
                {
                    reversed.Add(new PointF(xs[cursor % width], ys[cursor / width]));
                    if (cursor == startState) break;
                    cursor = previous[cursor];
                }
                if (reversed.Count == 0 || cursor != startState) return null;
                reversed.Reverse();
                return SimplifyPath(reversed);
            }

            private List<PointF> BestEffortPath(PointF start, PointF end, List<Obstacle> obstacles)
            {
                List<List<PointF>> candidates = new List<List<PointF>>();
                candidates.Add(new List<PointF>(new PointF[] { start, new PointF(end.X, start.Y), end }));
                candidates.Add(new List<PointF>(new PointF[] { start, new PointF(start.X, end.Y), end }));
                if (obstacles.Count > 0)
                {
                    float left = Single.MaxValue, right = Single.MinValue, top = Single.MaxValue, bottom = Single.MinValue;
                    for (int index = 0; index < obstacles.Count; index++)
                    {
                        left = Math.Min(left, obstacles[index].Bounds.Left);
                        right = Math.Max(right, obstacles[index].Bounds.Right);
                        top = Math.Min(top, obstacles[index].Bounds.Top);
                        bottom = Math.Max(bottom, obstacles[index].Bounds.Bottom);
                    }
                    float margin = SafePositive(_options.RoutingChannelSpacing, 30f);
                    candidates.Add(new List<PointF>(new PointF[] { start, new PointF(left - margin, start.Y), new PointF(left - margin, end.Y), end }));
                    candidates.Add(new List<PointF>(new PointF[] { start, new PointF(right + margin, start.Y), new PointF(right + margin, end.Y), end }));
                    candidates.Add(new List<PointF>(new PointF[] { start, new PointF(start.X, top - margin), new PointF(end.X, top - margin), end }));
                    candidates.Add(new List<PointF>(new PointF[] { start, new PointF(start.X, bottom + margin), new PointF(end.X, bottom + margin), end }));
                }
                List<PointF> best = null;
                float bestScore = Single.MaxValue;
                for (int index = 0; index < candidates.Count; index++)
                {
                    List<PointF> candidate = SimplifyPath(candidates[index]);
                    float score = CountPathConflicts(candidate, obstacles) * 1000000f + PathLength(candidate) + candidate.Count * SafePositive(_options.BendPenalty, 18f);
                    if (score < bestScore) { bestScore = score; best = candidate; }
                }
                return best ?? new List<PointF>(new PointF[] { start, end });
            }

            private List<Obstacle> RelevantObstacles(PointF start, PointF end, List<Obstacle> obstacles)
            {
                RectangleF corridor = RectangleFromPoints(start, end);
                corridor.Inflate(SafePositive(_options.RoutingSearchMargin, 260f), SafePositive(_options.RoutingSearchMargin, 260f));
                List<Obstacle> relevant = new List<Obstacle>();
                for (int index = 0; index < obstacles.Count; index++) if (corridor.IntersectsWith(obstacles[index].Bounds)) relevant.Add(obstacles[index]);
                relevant.Sort(delegate(Obstacle first, Obstacle second)
                {
                    int comparison = DistanceToRectangle(RectangleFromPoints(start, end), first.Bounds).CompareTo(DistanceToRectangle(RectangleFromPoints(start, end), second.Bounds));
                    return comparison != 0 ? comparison : String.CompareOrdinal(first.Key, second.Key);
                });
                int maximum = Math.Max(4, _options.MaximumRoutingObstacles);
                if (relevant.Count > maximum) relevant.RemoveRange(maximum, relevant.Count - maximum);
                return relevant;
            }

            private void AddCollidingObstacles(List<PointF> path, List<Obstacle> all, List<Obstacle> relevant)
            {
                HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < relevant.Count; index++) keys.Add(relevant[index].Key);
                int maximum = Math.Max(4, _options.MaximumRoutingObstacles);
                for (int index = 0; index < all.Count && relevant.Count < maximum; index++)
                {
                    if (keys.Contains(all[index].Key) || !PathHitsObstacle(path, all[index].Bounds)) continue;
                    relevant.Add(all[index]);
                    keys.Add(all[index].Key);
                }
            }

            public void PlaceLabels()
            {
                List<RectangleF> occupied = new List<RectangleF>();
                foreach (KeyValuePair<string, RectangleF> pair in Result.NodeBounds) occupied.Add(Inflated(pair.Value, _options.LabelPadding));
                foreach (KeyValuePair<string, RectangleF> pair in Result.GroupBounds)
                {
                    RectangleF group = pair.Value;
                    occupied.Add(Inflated(new RectangleF(group.Left, group.Top, group.Width, Math.Min(group.Height, _options.GroupHeaderHeight)), _options.LabelPadding));
                }

                for (int index = 0; index < _edgeWork.Count; index++)
                {
                    EdgeWork work = _edgeWork[index];
                    if (work.Route == null || work.Route.Points.Count == 0) continue;
                    PointF midpoint = PointAtFraction(work.Route.Points, .5f);
                    work.Route.LabelAnchorPoint = midpoint;
                    string label = work.Edge.label ?? "";
                    if (label.Trim().Length == 0)
                    {
                        work.Route.LabelPoint = midpoint;
                        work.Route.LabelBounds = RectangleF.Empty;
                        continue;
                    }

                    float width = Math.Max(SafePositive(_options.MinimumLabelWidth, 44f), Math.Min(SafePositive(_options.MaximumLabelWidth, 600f), label.Length * SafePositive(_options.LabelCharacterWidth, 14f) + 18f));
                    float height = SafePositive(_options.LabelHeight, 26f);
                    List<LabelCandidate> candidates = LabelCandidates(work.Route.Points, width, height);
                    LabelCandidate best = null;
                    float bestScore = Single.MaxValue;
                    for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                    {
                        LabelCandidate candidate = candidates[candidateIndex];
                        float score = LabelScore(candidate, work, occupied, midpoint);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = candidate;
                        }
                    }
                    if (best == null)
                    {
                        best = new LabelCandidate(midpoint, midpoint, new RectangleF(midpoint.X - width / 2f, midpoint.Y - height / 2f, width, height));
                    }
                    work.Route.LabelAnchorPoint = best.Anchor;
                    work.Route.LabelPoint = best.Center;
                    work.Route.LabelBounds = best.Bounds;
                    occupied.Add(Inflated(best.Bounds, _options.LabelPadding));
                }
            }

            private List<LabelCandidate> LabelCandidates(IList<PointF> points, float width, float height)
            {
                List<LabelCandidate> result = new List<LabelCandidate>();
                float gap = SafePositive(_options.LabelGap, 10f);
                for (int index = 1; index < points.Count; index++)
                {
                    PointF first = points[index - 1];
                    PointF second = points[index];
                    PointF anchor = new PointF((first.X + second.X) / 2f, (first.Y + second.Y) / 2f);
                    if (NearlyEqual(first.Y, second.Y))
                    {
                        AddLabelCandidate(result, anchor, new PointF(anchor.X, anchor.Y - height / 2f - gap), width, height);
                        AddLabelCandidate(result, anchor, new PointF(anchor.X, anchor.Y + height / 2f + gap), width, height);
                    }
                    else
                    {
                        AddLabelCandidate(result, anchor, new PointF(anchor.X - width / 2f - gap, anchor.Y), width, height);
                        AddLabelCandidate(result, anchor, new PointF(anchor.X + width / 2f + gap, anchor.Y), width, height);
                    }
                }
                float[] fractions = new float[] { .5f, .35f, .65f, .2f, .8f };
                for (int index = 0; index < fractions.Length; index++)
                {
                    PointF anchor = PointAtFraction(points, fractions[index]);
                    AddLabelCandidate(result, anchor, new PointF(anchor.X, anchor.Y - height / 2f - gap), width, height);
                    AddLabelCandidate(result, anchor, new PointF(anchor.X, anchor.Y + height / 2f + gap), width, height);
                    AddLabelCandidate(result, anchor, new PointF(anchor.X - width / 2f - gap, anchor.Y), width, height);
                    AddLabelCandidate(result, anchor, new PointF(anchor.X + width / 2f + gap, anchor.Y), width, height);
                }
                return result;
            }

            private void AddLabelCandidate(List<LabelCandidate> list, PointF anchor, PointF center, float width, float height)
            {
                list.Add(new LabelCandidate(anchor, center, new RectangleF(center.X - width / 2f, center.Y - height / 2f, width, height)));
            }

            private float LabelScore(LabelCandidate candidate, EdgeWork current, List<RectangleF> occupied, PointF preferred)
            {
                float score = Distance(candidate.Center, preferred) * .15f;
                for (int index = 0; index < occupied.Count; index++)
                {
                    RectangleF intersection = RectangleF.Intersect(candidate.Bounds, occupied[index]);
                    if (!intersection.IsEmpty) score += 10000f + intersection.Width * intersection.Height * 20f;
                }
                for (int edgeIndex = 0; edgeIndex < _edgeWork.Count; edgeIndex++)
                {
                    EdgeWork other = _edgeWork[edgeIndex];
                    if (other == current || other.Route == null) continue;
                    if (PathIntersectsRectangle(other.Route.Points, candidate.Bounds)) score += 1400f;
                }
                if (PathIntersectsRectangle(current.Route.Points, candidate.Bounds)) score += 120f;
                return score;
            }

            public void UpdateContentBounds()
            {
                bool hasBounds = false;
                RectangleF bounds = RectangleF.Empty;
                foreach (KeyValuePair<string, RectangleF> pair in Result.GroupBounds) Union(ref bounds, ref hasBounds, pair.Value);
                foreach (KeyValuePair<string, RectangleF> pair in Result.NodeBounds) Union(ref bounds, ref hasBounds, pair.Value);
                foreach (KeyValuePair<string, GraphEdgeLayout> pair in Result.Edges)
                {
                    GraphEdgeLayout edge = pair.Value;
                    for (int index = 0; index < edge.Points.Count; index++) Union(ref bounds, ref hasBounds, new RectangleF(edge.Points[index].X, edge.Points[index].Y, .1f, .1f));
                    if (!edge.LabelBounds.IsEmpty) Union(ref bounds, ref hasBounds, edge.LabelBounds);
                }
                Result.ContentBounds = hasBounds ? bounds : new RectangleF(_options.OriginX, _options.OriginY, 0f, 0f);
            }

            private bool TryGetBounds(string key, out RectangleF bounds)
            {
                if (key.StartsWith("group:", StringComparison.Ordinal)) return Result.GroupBounds.TryGetValue(key.Substring(6), out bounds);
                if (key.StartsWith("node:", StringComparison.Ordinal)) return Result.NodeBounds.TryGetValue(key.Substring(5), out bounds);
                bounds = RectangleF.Empty;
                return false;
            }

            private string LastValidGroupId(IList<string> ids, string excluded)
            {
                if (ids == null) return "";
                for (int index = ids.Count - 1; index >= 0; index--)
                {
                    string id = ids[index] ?? "";
                    if (id != excluded && _groups.ContainsKey(id)) return id;
                }
                return "";
            }

            private bool HasIndependentMemberships(IList<string> ids, LayoutItem primary)
            {
                if (ids == null || ids.Count < 2) return false;
                int valid = 0;
                for (int index = 0; index < ids.Count; index++) if (_groups.ContainsKey(ids[index] ?? "")) valid++;
                if (valid < 2) return false;
                LayoutItem cursor = primary;
                HashSet<string> ancestors = new HashSet<string>(StringComparer.Ordinal);
                while (cursor != null && !cursor.IsRoot)
                {
                    ancestors.Add(cursor.Id);
                    cursor = cursor.Parent;
                }
                for (int index = 0; index < ids.Count; index++)
                {
                    string id = ids[index] ?? "";
                    if (_groups.ContainsKey(id) && !ancestors.Contains(id)) return true;
                }
                return false;
            }

            private static bool ParentChainContains(LayoutItem parent, LayoutItem target)
            {
                HashSet<LayoutItem> seen = new HashSet<LayoutItem>();
                LayoutItem cursor = parent;
                while (cursor != null && seen.Add(cursor))
                {
                    if (cursor == target) return true;
                    cursor = cursor.CandidateParent;
                }
                return false;
            }

            private static RectangleF ChildBounds(List<LayoutItem> children)
            {
                if (children.Count == 0) return RectangleF.Empty;
                float left = Single.MaxValue, top = Single.MaxValue, right = Single.MinValue, bottom = Single.MinValue;
                for (int index = 0; index < children.Count; index++)
                {
                    LayoutItem item = children[index];
                    left = Math.Min(left, item.X);
                    top = Math.Min(top, item.Y);
                    right = Math.Max(right, item.X + item.Width);
                    bottom = Math.Max(bottom, item.Y + item.Height);
                }
                return RectangleF.FromLTRB(left, top, right, bottom);
            }

            private float PrimarySize(LayoutItem item) { return _options.Direction == GraphLayoutDirection.LeftToRight ? item.Width : item.Height; }
            private float SecondarySize(LayoutItem item) { return _options.Direction == GraphLayoutDirection.LeftToRight ? item.Height : item.Width; }

            private static Dictionary<LayoutItem, int> LayerPositions(List<List<LayoutItem>> layers)
            {
                Dictionary<LayoutItem, int> result = new Dictionary<LayoutItem, int>();
                for (int layer = 0; layer < layers.Count; layer++)
                    for (int index = 0; index < layers[layer].Count; index++) result[layers[layer][index]] = index;
                return result;
            }

            private static LayoutItem FirstByInputOrder(IEnumerable<LayoutItem> items)
            {
                LayoutItem first = null;
                foreach (LayoutItem item in items) if (first == null || CompareInputOrder(item, first) < 0) first = item;
                return first;
            }

            private static int CompareInputOrder(LayoutItem first, LayoutItem second)
            {
                int comparison = first.InputOrder.CompareTo(second.InputOrder);
                return comparison != 0 ? comparison : String.CompareOrdinal(first.Key, second.Key);
            }
        }

        private sealed class LayoutItem
        {
            public readonly string Key;
            public readonly string EntityType;
            public readonly string Id;
            public readonly RectangleF Existing;
            public readonly int InputOrder;
            public readonly List<LayoutItem> Children = new List<LayoutItem>();
            public GraphGroup SourceGroup;
            public GraphNode SourceNode;
            public LayoutItem CandidateParent;
            public LayoutItem Parent;
            public bool IsRoot;
            public float X;
            public float Y;
            public float Width;
            public float Height;

            public LayoutItem(string key, string entityType, string id, RectangleF existing, int inputOrder)
            {
                Key = key;
                EntityType = entityType;
                Id = id;
                Existing = existing;
                InputOrder = inputOrder;
                Width = existing.Width;
                Height = existing.Height;
            }
        }

        private sealed class LayerArc
        {
            public readonly LayoutItem Source;
            public readonly LayoutItem Target;
            public LayerArc(LayoutItem source, LayoutItem target) { Source = source; Target = target; }
        }

        private sealed class EdgeWork
        {
            public readonly GraphEdge Edge;
            public readonly int InputOrder;
            public readonly string SourceKey;
            public readonly string TargetKey;
            public readonly RectangleF SourceBounds;
            public readonly RectangleF TargetBounds;
            public string SourceSide;
            public string TargetSide;
            public PointF SourcePoint;
            public PointF TargetPoint;
            public int SourcePortIndex;
            public int SourcePortCount;
            public int TargetPortIndex;
            public int TargetPortCount;
            public GraphEdgeLayout Route;

            public EdgeWork(GraphEdge edge, int inputOrder, string sourceKey, string targetKey, RectangleF sourceBounds, RectangleF targetBounds)
            {
                Edge = edge;
                InputOrder = inputOrder;
                SourceKey = sourceKey;
                TargetKey = targetKey;
                SourceBounds = sourceBounds;
                TargetBounds = targetBounds;
            }
        }

        private sealed class PortRequest
        {
            public readonly EdgeWork Work;
            public readonly bool Source;
            public float OppositeCoordinate;
            public PortRequest(EdgeWork work, bool source) { Work = work; Source = source; }
        }

        private sealed class Obstacle
        {
            public readonly string Key;
            public readonly RectangleF Bounds;
            public readonly bool IsGroup;
            public Obstacle(string key, RectangleF bounds, bool isGroup) { Key = key; Bounds = bounds; IsGroup = isGroup; }
        }

        private sealed class LabelCandidate
        {
            public readonly PointF Anchor;
            public readonly PointF Center;
            public readonly RectangleF Bounds;
            public LabelCandidate(PointF anchor, PointF center, RectangleF bounds) { Anchor = anchor; Center = center; Bounds = bounds; }
        }

        private struct HeapEntry
        {
            public readonly int State;
            public readonly float Priority;
            public readonly float Distance;
            public HeapEntry(int state, float priority, float distance) { State = state; Priority = priority; Distance = distance; }
        }

        private sealed class MinHeap
        {
            private readonly List<HeapEntry> _items = new List<HeapEntry>();
            public int Count { get { return _items.Count; } }

            public void Push(HeapEntry value)
            {
                _items.Add(value);
                int index = _items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Less(_items[index], _items[parent])) break;
                    HeapEntry temporary = _items[index]; _items[index] = _items[parent]; _items[parent] = temporary;
                    index = parent;
                }
            }

            public HeapEntry Pop()
            {
                HeapEntry result = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                int index = 0;
                while (index < _items.Count)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    if (left >= _items.Count) break;
                    int smallest = right < _items.Count && Less(_items[right], _items[left]) ? right : left;
                    if (!Less(_items[smallest], _items[index])) break;
                    HeapEntry temporary = _items[index]; _items[index] = _items[smallest]; _items[smallest] = temporary;
                    index = smallest;
                }
                return result;
            }

            private static bool Less(HeapEntry first, HeapEntry second)
            {
                int comparison = first.Priority.CompareTo(second.Priority);
                return comparison != 0 ? comparison < 0 : first.State < second.State;
            }
        }

        private static string EndpointKey(string type, string id)
        {
            return (type == "group" ? "group" : "node") + ":" + (id ?? "");
        }

        private static float SafeCoordinate(float value)
        {
            return Single.IsNaN(value) || Single.IsInfinity(value) ? 0f : Math.Max(-GraphSerialization.MaxCoordinate, Math.Min(GraphSerialization.MaxCoordinate, value));
        }

        private static float SafeDimension(float value, float fallback, float minimum)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value) || value <= 0f) value = fallback;
            return Math.Max(minimum, Math.Min(GraphSerialization.MaxItemDimension, value));
        }

        private static float SafePositive(float value, float fallback)
        {
            return Single.IsNaN(value) || Single.IsInfinity(value) || value <= 0f ? fallback : value;
        }

        private static bool IsUsableBounds(RectangleF bounds)
        {
            return !Single.IsNaN(bounds.X) && !Single.IsInfinity(bounds.X) &&
                !Single.IsNaN(bounds.Y) && !Single.IsInfinity(bounds.Y) &&
                !Single.IsNaN(bounds.Width) && !Single.IsInfinity(bounds.Width) &&
                !Single.IsNaN(bounds.Height) && !Single.IsInfinity(bounds.Height) &&
                bounds.Width > 0f && bounds.Height > 0f;
        }

        private static PointF Center(RectangleF bounds) { return new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f); }
        private static float Distance(PointF first, PointF second) { float dx = first.X - second.X, dy = first.Y - second.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }
        private static float Manhattan(PointF first, PointF second) { return Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y); }
        private static bool NearlyEqual(float first, float second) { return Math.Abs(first - second) <= Epsilon; }
        private static bool SamePoint(PointF first, PointF second) { return NearlyEqual(first.X, second.X) && NearlyEqual(first.Y, second.Y); }

        private static PointF OffsetBySide(PointF point, string side, float distance)
        {
            if (side == "left") return new PointF(point.X - distance, point.Y);
            if (side == "right") return new PointF(point.X + distance, point.Y);
            if (side == "top") return new PointF(point.X, point.Y - distance);
            return new PointF(point.X, point.Y + distance);
        }

        private static RectangleF Inflated(RectangleF bounds, float amount)
        {
            RectangleF result = bounds;
            float safe = Single.IsNaN(amount) || Single.IsInfinity(amount) ? 0f : Math.Max(0f, amount);
            result.Inflate(safe, safe);
            return result;
        }

        private static RectangleF RectangleFromPoints(PointF first, PointF second)
        {
            return RectangleF.FromLTRB(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
        }

        private static bool ContainsInterior(RectangleF bounds, PointF point)
        {
            return point.X > bounds.Left + Epsilon && point.X < bounds.Right - Epsilon && point.Y > bounds.Top + Epsilon && point.Y < bounds.Bottom - Epsilon;
        }

        private static bool PointBlocked(PointF point, List<Obstacle> obstacles)
        {
            for (int index = 0; index < obstacles.Count; index++) if (ContainsInterior(obstacles[index].Bounds, point)) return true;
            return false;
        }

        private static bool SegmentClear(PointF first, PointF second, List<Obstacle> obstacles)
        {
            if (!NearlyEqual(first.X, second.X) && !NearlyEqual(first.Y, second.Y)) return false;
            for (int index = 0; index < obstacles.Count; index++) if (SegmentHitsRectangle(first, second, obstacles[index].Bounds)) return false;
            return true;
        }

        private static bool SegmentHitsRectangle(PointF first, PointF second, RectangleF bounds)
        {
            if (NearlyEqual(first.Y, second.Y))
            {
                float y = first.Y;
                if (y <= bounds.Top + Epsilon || y >= bounds.Bottom - Epsilon) return false;
                float left = Math.Min(first.X, second.X), right = Math.Max(first.X, second.X);
                return right > bounds.Left + Epsilon && left < bounds.Right - Epsilon;
            }
            if (NearlyEqual(first.X, second.X))
            {
                float x = first.X;
                if (x <= bounds.Left + Epsilon || x >= bounds.Right - Epsilon) return false;
                float top = Math.Min(first.Y, second.Y), bottom = Math.Max(first.Y, second.Y);
                return bottom > bounds.Top + Epsilon && top < bounds.Bottom - Epsilon;
            }
            return true;
        }

        private static bool PathClear(IList<PointF> points, List<Obstacle> obstacles)
        {
            if (points == null || points.Count < 2) return true;
            for (int index = 1; index < points.Count; index++) if (!SegmentClear(points[index - 1], points[index], obstacles)) return false;
            return true;
        }

        private static bool PathHitsObstacle(IList<PointF> points, RectangleF bounds)
        {
            if (points == null) return false;
            for (int index = 1; index < points.Count; index++) if (SegmentHitsRectangle(points[index - 1], points[index], bounds)) return true;
            return false;
        }

        private static int CountPathConflicts(IList<PointF> points, List<Obstacle> obstacles)
        {
            int count = 0;
            for (int index = 0; index < obstacles.Count; index++) if (PathHitsObstacle(points, obstacles[index].Bounds)) count++;
            return count;
        }

        private static List<PointF> SimplifyPath(IList<PointF> source)
        {
            List<PointF> points = new List<PointF>();
            if (source == null) return points;
            for (int index = 0; index < source.Count; index++)
            {
                PointF point = source[index];
                if (points.Count == 0 || !SamePoint(points[points.Count - 1], point)) points.Add(point);
            }
            int cursor = 1;
            while (cursor < points.Count - 1)
            {
                PointF previous = points[cursor - 1], current = points[cursor], next = points[cursor + 1];
                if ((NearlyEqual(previous.X, current.X) && NearlyEqual(current.X, next.X)) || (NearlyEqual(previous.Y, current.Y) && NearlyEqual(current.Y, next.Y))) points.RemoveAt(cursor);
                else cursor++;
            }
            return points;
        }

        private static void AppendWithoutDuplicate(List<PointF> target, IList<PointF> source)
        {
            if (source == null) return;
            for (int index = 0; index < source.Count; index++) if (target.Count == 0 || !SamePoint(target[target.Count - 1], source[index])) target.Add(source[index]);
        }

        private static float PathLength(IList<PointF> points)
        {
            float length = 0f;
            if (points == null) return length;
            for (int index = 1; index < points.Count; index++) length += Manhattan(points[index - 1], points[index]);
            return length;
        }

        private static PointF PointAtFraction(IList<PointF> points, float fraction)
        {
            if (points == null || points.Count == 0) return PointF.Empty;
            if (points.Count == 1) return points[0];
            float total = PathLength(points);
            if (total <= Epsilon) return points[0];
            float target = total * Math.Max(0f, Math.Min(1f, fraction));
            float travelled = 0f;
            for (int index = 1; index < points.Count; index++)
            {
                float segment = Manhattan(points[index - 1], points[index]);
                if (travelled + segment >= target && segment > Epsilon)
                {
                    float ratio = (target - travelled) / segment;
                    return new PointF(points[index - 1].X + (points[index].X - points[index - 1].X) * ratio, points[index - 1].Y + (points[index].Y - points[index - 1].Y) * ratio);
                }
                travelled += segment;
            }
            return points[points.Count - 1];
        }

        private static bool PathIntersectsRectangle(IList<PointF> points, RectangleF bounds)
        {
            if (points == null) return false;
            for (int index = 1; index < points.Count; index++)
            {
                PointF first = points[index - 1], second = points[index];
                if (NearlyEqual(first.Y, second.Y))
                {
                    float y = first.Y;
                    if (y >= bounds.Top && y <= bounds.Bottom && Math.Max(first.X, second.X) >= bounds.Left && Math.Min(first.X, second.X) <= bounds.Right) return true;
                }
                else if (NearlyEqual(first.X, second.X))
                {
                    float x = first.X;
                    if (x >= bounds.Left && x <= bounds.Right && Math.Max(first.Y, second.Y) >= bounds.Top && Math.Min(first.Y, second.Y) <= bounds.Bottom) return true;
                }
            }
            return false;
        }

        private static void AddUnique(List<float> values, float value)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value)) return;
            for (int index = 0; index < values.Count; index++) if (NearlyEqual(values[index], value)) return;
            values.Add(value);
        }

        private static int FindCoordinate(List<float> values, float value)
        {
            for (int index = 0; index < values.Count; index++) if (NearlyEqual(values[index], value)) return index;
            return -1;
        }

        private static float DistanceToRectangle(RectangleF first, RectangleF second)
        {
            float dx = first.Right < second.Left ? second.Left - first.Right : second.Right < first.Left ? first.Left - second.Right : 0f;
            float dy = first.Bottom < second.Top ? second.Top - first.Bottom : second.Bottom < first.Top ? first.Top - second.Bottom : 0f;
            return dx + dy;
        }

        private static void Union(ref RectangleF current, ref bool hasCurrent, RectangleF value)
        {
            if (Single.IsNaN(value.X) || Single.IsNaN(value.Y) || Single.IsNaN(value.Width) || Single.IsNaN(value.Height)) return;
            if (!hasCurrent) { current = value; hasCurrent = true; }
            else current = RectangleF.Union(current, value);
        }
    }
}
