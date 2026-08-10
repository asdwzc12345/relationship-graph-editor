using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace RelationshipGraphNative
{
    public sealed class GraphDocument
    {
        public int version { get; set; }
        public GraphMeta meta { get; set; }
        public GraphSettings settings { get; set; }
        public List<GraphGroup> groups { get; set; }
        public List<GraphNode> nodes { get; set; }
        public List<GraphEdge> edges { get; set; }
    }

    public sealed class GraphMeta
    {
        public string title { get; set; }
        public float canvasWidth { get; set; }
        public float canvasHeight { get; set; }
        public string updatedAt { get; set; }
    }

    public sealed class GraphSettings
    {
        public string theme { get; set; }
        public Dictionary<string, string> colors { get; set; }
        public List<RelationType> relationTypes { get; set; }
    }

    public sealed class RelationType
    {
        public string id { get; set; }
        public string label { get; set; }
        public string color { get; set; }
    }

    public sealed class GraphGroup
    {
        public string id { get; set; }
        public string label { get; set; }
        public List<string> groups { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float w { get; set; }
        public float h { get; set; }
    }

    public sealed class GraphNode
    {
        public string id { get; set; }
        public string label { get; set; }
        public string type { get; set; }
        public string kind { get; set; }
        public string group { get; set; }
        public List<string> groups { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float w { get; set; }
        public float h { get; set; }
        public string note { get; set; }
    }

    public sealed class GraphEdge
    {
        public string id { get; set; }
        public string source { get; set; }
        public string target { get; set; }
        public string sourceType { get; set; }
        public string targetType { get; set; }
        public string label { get; set; }
        public string category { get; set; }
        public string lineType { get; set; }
        public string sourceSide { get; set; }
        public string targetSide { get; set; }
    }

    public sealed class GraphClipboardPayload
    {
        public string format { get; set; }
        public int selectionVersion { get; set; }
        public string selectionType { get; set; }
        public List<string> selectedGroupIds { get; set; }
        public List<string> selectedNodeIds { get; set; }
        public List<GraphGroup> groups { get; set; }
        public List<GraphNode> nodes { get; set; }
        public List<GraphEdge> edges { get; set; }
    }

    public sealed class EndpointItem
    {
        public string Type;
        public string Id;
        public string Label;
        public override string ToString() { return Label; }
        public string Key { get { return Type + ":" + Id; } }
    }

    public static class GraphSerialization
    {
        public const int SchemaVersion = 3;
        public const string ClipboardFormat = "relationship-graph-native-selection-v1";
        public const int MaxGroups = 400;
        public const int MaxNodes = 1500;
        public const int MaxEdges = 7500;
        public const float MaxItemDimension = 1000000f;
        public const float MaxCoordinate = 100000000f;
        private static readonly JavaScriptSerializer Serializer = CreateSerializer();

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;
            serializer.RecursionLimit = 200;
            return serializer;
        }

        public static GraphDocument Clone(GraphDocument graph)
        {
            return Deserialize(Serialize(graph, false));
        }

        public static string SerializeClipboard(GraphClipboardPayload payload)
        {
            if (payload == null) throw new InvalidDataException("没有可复制的内容。");
            payload.format = ClipboardFormat;
            if (payload.groups == null) payload.groups = new List<GraphGroup>();
            if (payload.nodes == null) payload.nodes = new List<GraphNode>();
            if (payload.edges == null) payload.edges = new List<GraphEdge>();
            if (payload.selectedGroupIds == null) payload.selectedGroupIds = new List<string>();
            if (payload.selectedNodeIds == null) payload.selectedNodeIds = new List<string>();
            return Serializer.Serialize(payload);
        }

        public static GraphClipboardPayload DeserializeClipboard(string json)
        {
            if (String.IsNullOrWhiteSpace(json) || json.Length > 16 * 1024 * 1024) throw new InvalidDataException("剪贴板中没有可粘贴的关系图内容。");
            GraphClipboardPayload payload = Serializer.Deserialize<GraphClipboardPayload>(json);
            if (payload == null || payload.format != ClipboardFormat) throw new InvalidDataException("剪贴板中的内容不是关系图选中项。");
            if (payload.groups == null) payload.groups = new List<GraphGroup>();
            if (payload.nodes == null) payload.nodes = new List<GraphNode>();
            if (payload.edges == null) payload.edges = new List<GraphEdge>();
            if (payload.selectedGroupIds == null) payload.selectedGroupIds = new List<string>();
            if (payload.selectedNodeIds == null) payload.selectedNodeIds = new List<string>();
            if (payload.groups.Count > MaxGroups || payload.nodes.Count > MaxNodes || payload.edges.Count > MaxEdges)
                throw new InvalidDataException("剪贴板中的关系图内容超过程序上限。");
            return payload;
        }

        public static string Serialize(GraphDocument graph, bool pretty)
        {
            string json = Serializer.Serialize(graph);
            return pretty ? PrettyJson(json) : json;
        }

        public static GraphDocument Deserialize(string json)
        {
            GraphDocument graph = Serializer.Deserialize<GraphDocument>(json);
            return Normalize(graph);
        }

        public static GraphDocument LoadFile(string fileName)
        {
            if (new FileInfo(fileName).Length > 64L * 1024L * 1024L) throw new InvalidDataException("关系图文件超过 64 MB，无法安全导入。");
            string text = File.ReadAllText(fileName, Encoding.UTF8);
            string trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (Path.GetExtension(fileName).Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(fileName).Equals(".htm", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                return Deserialize(ExtractReadonlyJson(text));
            }
            return Deserialize(text);
        }

        public static string ExtractReadonlyJson(string html)
        {
            MatchCollection scripts = Regex.Matches(html, "<script\\b([^>]*)>([\\s\\S]*?)</script\\s*>", RegexOptions.IgnoreCase);
            foreach (Match script in scripts)
            {
                string attributes = script.Groups[1].Value;
                if (!Regex.IsMatch(attributes, "\\bid\\s*=\\s*([\"'])graphData\\1", RegexOptions.IgnoreCase)) continue;
                if (!Regex.IsMatch(attributes, "\\btype\\s*=\\s*([\"'])application/json\\1", RegexOptions.IgnoreCase)) continue;
                string json = script.Groups[2].Value.Trim();
                if (json.Length == 0) throw new InvalidDataException("只读可视图中没有关系图数据。");
                return json;
            }
            throw new InvalidDataException("该 HTML 不是本工具生成的只读可视图。");
        }

        public static GraphDocument LoadDefault()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("RelationshipGraphNative.Data.default.json"))
            {
                if (stream == null) return CreateBlank("测试用图");
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    return Deserialize(reader.ReadToEnd());
            }
        }

        public static GraphDocument CreateBlank(string title)
        {
            GraphDocument graph = new GraphDocument();
            graph.version = SchemaVersion;
            graph.meta = new GraphMeta { title = Clean(title, "未命名关系图", 80), canvasWidth = 1380, canvasHeight = 760, updatedAt = DateTime.UtcNow.ToString("o") };
            graph.settings = DefaultSettings();
            graph.groups = new List<GraphGroup>();
            graph.nodes = new List<GraphNode>();
            graph.edges = new List<GraphEdge>();
            return graph;
        }

        public static GraphSettings DefaultSettings()
        {
            return new GraphSettings
            {
                theme = "system",
                colors = new Dictionary<string, string>(),
                relationTypes = new List<RelationType>
                {
                    new RelationType { id = "core", label = "主要流程", color = "#e8963e" },
                    new RelationType { id = "economy", label = "资源支持", color = "#4b9eea" },
                    new RelationType { id = "content", label = "目标与记录", color = "#d765a4" },
                    new RelationType { id = "growth", label = "反馈改进", color = "#54ad72" }
                }
            };
        }

        public static GraphDocument Normalize(GraphDocument graph)
        {
            if (graph == null) throw new InvalidDataException("关系图文件为空。");
            if (graph.groups == null || graph.nodes == null || graph.edges == null) throw new InvalidDataException("关系图缺少 groups、nodes 或 edges 数据。");
            if (graph.groups.Count > MaxGroups || graph.nodes.Count > MaxNodes || graph.edges.Count > MaxEdges)
                throw new InvalidDataException("关系图的数据量超过程序上限。");

            graph.version = SchemaVersion;
            if (graph.meta == null) graph.meta = new GraphMeta();
            graph.meta.title = Clean(graph.meta.title, "未命名关系图", 80);
            graph.meta.canvasWidth = Clamp(Finite(graph.meta.canvasWidth, 1380), 600, 20000);
            graph.meta.canvasHeight = Clamp(Finite(graph.meta.canvasHeight, 760), 400, 20000);
            graph.meta.updatedAt = String.IsNullOrWhiteSpace(graph.meta.updatedAt) ? DateTime.UtcNow.ToString("o") : graph.meta.updatedAt;

            if (graph.settings == null) graph.settings = DefaultSettings();
            if (graph.settings.colors == null) graph.settings.colors = new Dictionary<string, string>();
            if (graph.settings.relationTypes == null || graph.settings.relationTypes.Count == 0) graph.settings.relationTypes = DefaultSettings().relationTypes;
            List<RelationType> originalRelationTypes = graph.settings.relationTypes.Where(delegate(RelationType item) { return item != null; }).ToList();
            graph.settings.relationTypes = NormalizeRelationTypes(graph.settings.relationTypes);
            Dictionary<string, string> relationIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < Math.Min(originalRelationTypes.Count, graph.settings.relationTypes.Count); i++)
                if (!relationIdMap.ContainsKey(originalRelationTypes[i].id ?? "")) relationIdMap[originalRelationTypes[i].id ?? ""] = graph.settings.relationTypes[i].id;

            HashSet<string> groupIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> groupIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < graph.groups.Count; i++)
            {
                GraphGroup group = graph.groups[i] ?? new GraphGroup();
                string originalId = group.id ?? "";
                group.id = UniqueId(CleanId(originalId, "group_" + (i + 1)), groupIds);
                groupIds.Add(group.id);
                if (!groupIdMap.ContainsKey(originalId)) groupIdMap[originalId] = group.id;
                group.label = Clean(group.label, "分组 " + (i + 1), 40);
                group.groups = new List<string>();
                group.w = Clamp(Finite(group.w, 260), 120, MaxItemDimension);
                group.h = Clamp(Finite(group.h, 220), 100, MaxItemDimension);
                group.x = Clamp(Finite(group.x, 20), -MaxCoordinate, MaxCoordinate);
                group.y = Clamp(Finite(group.y, 40), -MaxCoordinate, MaxCoordinate);
                graph.groups[i] = group;
            }

            HashSet<string> nodeIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> nodeIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                GraphNode node = graph.nodes[i] ?? new GraphNode();
                string originalId = node.id ?? "";
                string originalGroup = node.group ?? "";
                node.id = UniqueId(CleanId(originalId, "node_" + (i + 1)), nodeIds);
                nodeIds.Add(node.id);
                if (!nodeIdMap.ContainsKey(originalId)) nodeIdMap[originalId] = node.id;
                node.label = Clean(node.label, "节点 " + (i + 1), 40);
                node.type = Clean(node.type, KindLabel(node.kind), 30);
                node.kind = NormalizeKind(node.kind, node.type);
                string mappedGroup;
                node.group = groupIdMap.TryGetValue(originalGroup, out mappedGroup) ? mappedGroup : (groupIds.Contains(originalGroup) ? originalGroup : "");
                node.groups = new List<string>();
                node.w = Clamp(Finite(node.w, 150), 105, MaxItemDimension);
                node.h = Clamp(Finite(node.h, 54), 46, MaxItemDimension);
                node.x = Clamp(Finite(node.x, 40), -MaxCoordinate, MaxCoordinate);
                node.y = Clamp(Finite(node.y, 80), -MaxCoordinate, MaxCoordinate);
                node.note = Clean(node.note, "", 300);
                graph.nodes[i] = node;
            }

            UpdateAutomaticMemberships(graph);

            HashSet<string> edgeIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> edgePairs = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> relationIds = new HashSet<string>(graph.settings.relationTypes.Select(delegate(RelationType item) { return item.id; }), StringComparer.Ordinal);
            List<GraphEdge> normalizedEdges = new List<GraphEdge>();
            for (int i = 0; i < graph.edges.Count; i++)
            {
                GraphEdge edge = graph.edges[i] ?? new GraphEdge();
                edge.id = UniqueId(CleanId(edge.id, "edge_" + (i + 1)), edgeIds);
                edgeIds.Add(edge.id);
                edge.source = RemapEndpointId(edge.sourceType, edge.source, nodeIdMap, groupIdMap);
                edge.target = RemapEndpointId(edge.targetType, edge.target, nodeIdMap, groupIdMap);
                edge.sourceType = ResolveEndpointType(edge.sourceType, edge.source, nodeIds, groupIds);
                edge.targetType = ResolveEndpointType(edge.targetType, edge.target, nodeIds, groupIds);
                if (edge.sourceType.Length == 0 || edge.targetType.Length == 0) continue;
                if (edge.sourceType == edge.targetType && edge.source == edge.target) continue;
                string pair = EndpointKey(edge.sourceType, edge.source) + "\0" + EndpointKey(edge.targetType, edge.target);
                if (edgePairs.Contains(pair)) continue;
                edgePairs.Add(pair);
                edge.label = Clean(edge.label, "", 40);
                string mappedCategory;
                edge.category = relationIds.Contains(edge.category ?? "") ? edge.category : (relationIdMap.TryGetValue(edge.category ?? "", out mappedCategory) ? mappedCategory : graph.settings.relationTypes[0].id);
                edge.lineType = NormalizeLineType(edge.lineType);
                edge.sourceSide = NormalizeSide(edge.sourceSide);
                edge.targetSide = NormalizeSide(edge.targetSide);
                normalizedEdges.Add(edge);
            }
            graph.edges = normalizedEdges;
            return graph;
        }

        public static void UpdateAutomaticMemberships(GraphDocument graph)
        {
            if (graph == null || graph.groups == null || graph.nodes == null) return;
            foreach (GraphGroup child in graph.groups)
            {
                RectangleF childRect = GroupRect(child);
                child.groups = graph.groups
                    .Where(delegate(GraphGroup parent) { return parent != child && StrictlyContains(GroupRect(parent), childRect); })
                    .OrderByDescending(delegate(GraphGroup parent) { return parent.w * parent.h; })
                    .ThenBy(delegate(GraphGroup parent) { return parent.id; }, StringComparer.Ordinal)
                    .Select(delegate(GraphGroup parent) { return parent.id; }).ToList();
            }
            foreach (GraphNode node in graph.nodes)
            {
                PointF center = new PointF(node.x + node.w / 2f, node.y + node.h / 2f);
                node.groups = graph.groups
                    .Where(delegate(GraphGroup group) { return GroupRect(group).Contains(center); })
                    .OrderByDescending(delegate(GraphGroup group) { return group.w * group.h; })
                    .ThenBy(delegate(GraphGroup group) { return group.id; }, StringComparer.Ordinal)
                    .Select(delegate(GraphGroup group) { return group.id; }).ToList();
                node.group = node.groups.LastOrDefault() ?? "";
            }
        }

        private static RectangleF GroupRect(GraphGroup group) { return new RectangleF(group.x, group.y, group.w, group.h); }

        private static bool StrictlyContains(RectangleF parent, RectangleF child)
        {
            const float tolerance = .1f;
            bool contains = parent.Left <= child.Left + tolerance && parent.Top <= child.Top + tolerance &&
                parent.Right >= child.Right - tolerance && parent.Bottom >= child.Bottom - tolerance;
            return contains && parent.Width * parent.Height > child.Width * child.Height + 1f;
        }

        private static List<RelationType> NormalizeRelationTypes(List<RelationType> source)
        {
            List<RelationType> result = new List<RelationType>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RelationType input in source)
            {
                if (input == null) continue;
                string id = Regex.IsMatch(input.id ?? "", "^[a-zA-Z][a-zA-Z0-9_-]{0,31}$") ? input.id : "type";
                id = UniqueId(id, ids);
                ids.Add(id);
                result.Add(new RelationType { id = id, label = Clean(input.label, "关系类型", 30), color = NormalizeColor(input.color, "#e8963e") });
            }
            if (result.Count == 0) return DefaultSettings().relationTypes;
            return result;
        }

        public static string EndpointKey(string type, string id) { return (type == "group" ? "group" : "node") + ":" + (id ?? ""); }
        public static string NormalizeLineType(string value) { return value == "straight" || value == "polyline" || value == "curve" ? value : "curve"; }
        public static string NormalizeSide(string value) { return value == "top" || value == "right" || value == "bottom" || value == "left" ? value : ""; }

        public static string UniqueId(string preferred, ICollection<string> used)
        {
            string baseId = CleanId(preferred, "item");
            string candidate = baseId;
            int number = 2;
            while (used.Contains(candidate)) { candidate = baseId + "_" + number; number++; }
            return candidate;
        }

        private static string ResolveEndpointType(string requested, string id, HashSet<string> nodeIds, HashSet<string> groupIds)
        {
            if (requested == "group" && groupIds.Contains(id ?? "")) return "group";
            if (requested == "node" && nodeIds.Contains(id ?? "")) return "node";
            if (nodeIds.Contains(id ?? "")) return "node";
            if (groupIds.Contains(id ?? "")) return "group";
            return "";
        }

        private static string RemapEndpointId(string requested, string id, Dictionary<string, string> nodeMap, Dictionary<string, string> groupMap)
        {
            string value = id ?? "", mapped;
            if (requested == "group" && groupMap.TryGetValue(value, out mapped)) return mapped;
            if (requested == "node" && nodeMap.TryGetValue(value, out mapped)) return mapped;
            if (nodeMap.TryGetValue(value, out mapped)) return mapped;
            if (groupMap.TryGetValue(value, out mapped)) return mapped;
            return value;
        }

        private static string NormalizeKind(string kind, string type)
        {
            string[] kinds = { "resource", "system", "output", "content", "staff", "commercial" };
            if (kinds.Contains(kind ?? "")) return kind;
            long hash = Math.Abs((long)(type ?? "").GetHashCode());
            return kinds[(int)(hash % kinds.Length)];
        }

        private static string KindLabel(string kind)
        {
            if (kind == "resource") return "输入信息";
            if (kind == "output") return "结果产出";
            if (kind == "content") return "内容信息";
            if (kind == "staff") return "人员岗位";
            if (kind == "commercial") return "商业信息";
            return "工作步骤";
        }

        private static float Finite(float value, float fallback) { return Single.IsNaN(value) || Single.IsInfinity(value) ? fallback : value; }
        private static float Clamp(float value, float minimum, float maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }
        private static string Clean(string value, string fallback, int max)
        {
            string result = (value ?? "").Trim();
            if (result.Length == 0) result = fallback;
            return result.Length > max ? result.Substring(0, max) : result;
        }
        private static string CleanId(string value, string fallback)
        {
            string result = Regex.Replace((value ?? "").Trim(), "[^a-zA-Z0-9_\\-]", "_");
            if (result.Length == 0) result = fallback;
            return result.Length > 100 ? result.Substring(0, 100) : result;
        }
        private static string NormalizeColor(string value, string fallback) { return Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$") ? value : fallback; }

        private static string PrettyJson(string json)
        {
            StringBuilder output = new StringBuilder();
            bool quoted = false;
            bool escaped = false;
            int indent = 0;
            foreach (char character in json)
            {
                if (quoted)
                {
                    output.Append(character);
                    if (escaped) escaped = false;
                    else if (character == '\\') escaped = true;
                    else if (character == '"') quoted = false;
                    continue;
                }
                if (character == '"') { quoted = true; output.Append(character); continue; }
                if (character == '{' || character == '[') { output.Append(character).AppendLine(); indent++; output.Append(new string(' ', indent * 2)); }
                else if (character == '}' || character == ']') { output.AppendLine(); indent--; output.Append(new string(' ', indent * 2)).Append(character); }
                else if (character == ',') { output.Append(character).AppendLine().Append(new string(' ', indent * 2)); }
                else if (character == ':') output.Append(": ");
                else if (!Char.IsWhiteSpace(character)) output.Append(character);
            }
            return output.ToString();
        }
    }
}
