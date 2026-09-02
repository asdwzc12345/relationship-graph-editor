using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace RelationshipGraphNative
{
    public sealed class DrawioPageInfo
    {
        public int Index { get; private set; }
        public string Name { get; private set; }
        public string DisplayName { get; private set; }
        public bool IsBareModel { get; private set; }

        internal DrawioPageInfo(int index, string name, string displayName, bool isBareModel)
        {
            Index = index;
            Name = name ?? "";
            DisplayName = displayName ?? "";
            IsBareModel = isBareModel;
        }
    }

    internal static class NativeDrawioImport
    {
        private const int MaxInflatedBytes = 64 * 1024 * 1024;
        private const int MaxCells = 20000;
        private const int MaxLabelPayload = 65536;
        private const int MaxPageNameLength = 80;
        private const int MaxParentDepth = 512;

        private static readonly Regex LegacyLabelPattern = new Regex(
            "<font\\b[^>]*>(?<type>[\\s\\S]*?)</font>\\s*<br\\s*/?>\\s*<b\\b[^>]*>(?<label>[\\s\\S]*?)</b>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex KnownHtmlTagPattern = new Regex("</?(?:b|strong|i|em|u|font|div|p|span|br|ul|ol|li|table|tbody|tr|td|h[1-6])\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex BreakPattern = new Regex("<br\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex BlockEndPattern = new Regex("</(?:div|p|li|tr|h[1-6])\\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex TagPattern = new Regex("<[^>]*>", RegexOptions.CultureInvariant);
        private static readonly Regex HorizontalSpacePattern = new Regex("[ \\t\\f\\v]+", RegexOptions.CultureInvariant);
        private static readonly Regex AroundNewlinePattern = new Regex(" *\\n *", RegexOptions.CultureInvariant);
        private static readonly Regex BlankLinePattern = new Regex("\\n{3,}", RegexOptions.CultureInvariant);

        public static bool LooksLikeDrawio(string fileName, string text)
        {
            string name = fileName ?? "";
            if (name.EndsWith(".drawio", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".dio", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".drawio.xml", StringComparison.OrdinalIgnoreCase)) return true;
            if (String.IsNullOrWhiteSpace(text)) return false;
            int start = 0;
            while (start < text.Length && (Char.IsWhiteSpace(text[start]) || text[start] == '\uFEFF')) start++;
            if (start >= text.Length || text[start] != '<') return false;
            int elementStart = SkipXmlPreamble(text, start);
            return StartsWithAt(text, elementStart, "<mxfile") || StartsWithAt(text, elementStart, "<mxGraphModel");
        }

        public static GraphDocument Import(string text, string fallbackTitle, out string notice)
        {
            DrawioSource source = ReadSource(text);
            return ImportPageCore(source, fallbackTitle, 0, false, out notice);
        }

        public static GraphDocument Import(string text, string fallbackTitle, int pageIndex, out string notice)
        {
            return ImportPage(text, fallbackTitle, pageIndex, out notice);
        }

        public static IList<DrawioPageInfo> GetPageInfos(string text)
        {
            return GetPageInfos(text, "");
        }

        public static IList<DrawioPageInfo> GetPageInfos(string text, string fallbackTitle)
        {
            DrawioSource source = ReadSource(text);
            List<DrawioPageInfo> result = new List<DrawioPageInfo>();
            if (source.IsBareModel)
            {
                string name = CleanPageName(fallbackTitle);
                result.Add(new DrawioPageInfo(0, name, name.Length > 0 ? name : "单页图", true));
            }
            else
            {
                for (int index = 0; index < source.Pages.Count; index++)
                {
                    string name = CleanPageName(GetAttributeInsensitive(source.Pages[index], "name"));
                    string displayName = name.Length > 0 ? name : "第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 页";
                    result.Add(new DrawioPageInfo(index, name, displayName, false));
                }
            }
            return result.AsReadOnly();
        }

        public static GraphDocument ImportPage(string text, string fallbackTitle, int pageIndex, out string notice)
        {
            DrawioSource source = ReadSource(text);
            ValidatePageIndex(pageIndex, source.PageCount);
            return ImportPageCore(source, fallbackTitle, pageIndex, true, out notice);
        }

        private static DrawioSource ReadSource(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Draw.io 文件为空，无法导入。");
            EnsureWithinLimit(text);

            XmlDocument document = LoadXml(text, "Draw.io 文件格式无效或已损坏。");
            XmlElement root = document.DocumentElement;
            if (root == null) throw new InvalidDataException("Draw.io 文件为空，无法导入。");

            if (IsElement(root, "mxGraphModel"))
            {
                return new DrawioSource(root, null);
            }
            if (IsElement(root, "mxfile"))
            {
                List<XmlElement> pages = ChildElements(root, "diagram");
                if (pages.Count == 0) throw new InvalidDataException("Draw.io 文件中没有可导入的页面。");
                return new DrawioSource(null, pages);
            }
            throw new InvalidDataException("该文件不是有效的 Draw.io 图。");
        }

        private static void ValidatePageIndex(int pageIndex, int pageCount)
        {
            if (pageIndex >= 0 && pageIndex < pageCount) return;
            string range = pageCount == 1 ? "0" : "0 到 " + (pageCount - 1).ToString(CultureInfo.InvariantCulture);
            throw new ArgumentOutOfRangeException("pageIndex", pageIndex,
                "Draw.io 页面索引超出范围；该文件共有 " + pageCount.ToString(CultureInfo.InvariantCulture) + " 个页面，有效索引为 " + range + "。");
        }

        private static GraphDocument ImportPageCore(DrawioSource source, string fallbackTitle, int pageIndex, bool explicitlySelected, out string notice)
        {
            ValidatePageIndex(pageIndex, source.PageCount);
            notice = "";
            XmlElement model;
            string title = fallbackTitle;
            if (source.IsBareModel)
            {
                model = source.BareModel;
            }
            else
            {
                XmlElement page = source.Pages[pageIndex];
                string pageName = CleanPageName(GetAttributeInsensitive(page, "name"));
                if (pageName.Length > 0) title = pageName;
                model = ReadPageModel(page);
                if (source.Pages.Count > 1)
                {
                    string namedPage = pageName.Length == 0 ? "" : "“" + pageName + "”";
                    int remaining = source.Pages.Count - 1;
                    if (explicitlySelected)
                    {
                        notice = "该 Draw.io 文件包含 " + source.Pages.Count.ToString(CultureInfo.InvariantCulture) + " 个页面，已导入第 " +
                            (pageIndex + 1).ToString(CultureInfo.InvariantCulture) + " 页" + namedPage + "（索引 " +
                            pageIndex.ToString(CultureInfo.InvariantCulture) + "），其余 " + remaining.ToString(CultureInfo.InvariantCulture) + " 个页面未导入。";
                    }
                    else
                    {
                        notice = "该 Draw.io 文件包含 " + source.Pages.Count.ToString(CultureInfo.InvariantCulture) + " 个页面，已导入第一页" + namedPage + "，其余 " +
                            remaining.ToString(CultureInfo.InvariantCulture) + " 个页面已忽略。";
                    }
                }
            }

            int skippedEdges;
            GraphDocument graph = ImportModel(model, title, out skippedEdges);
            if (skippedEdges > 0)
            {
                if (notice.Length > 0) notice += " ";
                notice += "另有 " + skippedEdges.ToString(CultureInfo.InvariantCulture) + " 条无法转换的连线已忽略。";
            }
            return graph;
        }

        private static XmlElement ReadPageModel(XmlElement page)
        {
            XmlElement directModel = FirstDescendant(page, "mxGraphModel");
            if (directModel != null) return directModel;

            string payload = (page.InnerText ?? "").Trim();
            if (payload.Length == 0) throw new InvalidDataException("Draw.io 页面内容为空，无法导入。");
            EnsureWithinLimit(payload);

            string xml = payload;
            if (!LooksLikeXml(xml))
            {
                if (xml.StartsWith("&lt;", StringComparison.OrdinalIgnoreCase))
                {
                    xml = WebUtility.HtmlDecode(xml);
                    EnsureWithinLimit(xml);
                }
                else if (xml.StartsWith("%3C", StringComparison.OrdinalIgnoreCase))
                {
                    xml = PercentDecodeUtf8(Encoding.UTF8.GetBytes(xml));
                }
                else
                {
                    xml = DecodeCompressedPage(xml);
                }
            }

            XmlDocument inner = LoadXml(xml, "Draw.io 页面内容无效或已损坏。");
            XmlElement model = inner.DocumentElement;
            if (model != null && IsElement(model, "mxGraphModel")) return model;
            model = model == null ? null : FirstDescendant(model, "mxGraphModel");
            if (model == null) throw new InvalidDataException("Draw.io 页面中缺少 mxGraphModel 数据。");
            return model;
        }

        private static string DecodeCompressedPage(string payload)
        {
            string compact = RemoveBase64Whitespace(payload).Replace('-', '+').Replace('_', '/');
            int remainder = compact.Length % 4;
            if (remainder == 1) throw new InvalidDataException("Draw.io 页面压缩数据无效。");
            if (remainder != 0) compact = compact.PadRight(compact.Length + (4 - remainder), '=');

            byte[] compressed;
            try { compressed = Convert.FromBase64String(compact); }
            catch (FormatException error) { throw new InvalidDataException("Draw.io 页面压缩数据无效。", error); }
            if (compressed.Length == 0) throw new InvalidDataException("Draw.io 页面压缩数据为空。");

            byte[] encoded;
            using (MemoryStream source = new MemoryStream(compressed, false))
            using (DeflateStream inflater = new DeflateStream(source, CompressionMode.Decompress, false))
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                while (true)
                {
                    int count;
                    try { count = inflater.Read(buffer, 0, buffer.Length); }
                    catch (InvalidDataException error) { throw new InvalidDataException("Draw.io 页面压缩数据无效。", error); }
                    catch (IOException error) { throw new InvalidDataException("Draw.io 页面压缩数据无法读取。", error); }
                    if (count <= 0) break;
                    if (output.Length + count > MaxInflatedBytes)
                        throw new InvalidDataException("Draw.io 页面解压后超过 64 MB，无法安全导入。");
                    output.Write(buffer, 0, count);
                }
                encoded = output.ToArray();
            }
            if (encoded.Length == 0) throw new InvalidDataException("Draw.io 页面解压后为空。");
            return PercentDecodeUtf8(encoded);
        }

        private static string PercentDecodeUtf8(byte[] encoded)
        {
            if (encoded == null || encoded.Length == 0) throw new InvalidDataException("Draw.io 页面内容为空。");
            if (encoded.Length > MaxInflatedBytes) throw new InvalidDataException("Draw.io 页面解压后超过 64 MB，无法安全导入。");
            byte[] decoded = new byte[encoded.Length];
            int output = 0;
            for (int index = 0; index < encoded.Length; index++)
            {
                if (encoded[index] == (byte)'%')
                {
                    if (index + 2 >= encoded.Length) throw new InvalidDataException("Draw.io 页面中的百分号编码无效。");
                    int high = HexValue(encoded[index + 1]), low = HexValue(encoded[index + 2]);
                    if (high < 0 || low < 0) throw new InvalidDataException("Draw.io 页面中的百分号编码无效。");
                    decoded[output++] = (byte)((high << 4) | low);
                    index += 2;
                }
                else
                {
                    decoded[output++] = encoded[index];
                }
            }
            try { return new UTF8Encoding(false, true).GetString(decoded, 0, output); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Draw.io 页面不是有效的 UTF-8 内容。", error); }
        }

        private static GraphDocument ImportModel(XmlElement model, string title, out int skippedEdges)
        {
            skippedEdges = 0;
            Dictionary<string, CellRecord> cellsById;
            List<CellRecord> cells = ReadCells(model, out cellsById);
            List<CellRecord> groupCells = new List<CellRecord>();
            List<CellRecord> nodeCells = new List<CellRecord>();
            List<CellRecord> edgeCells = new List<CellRecord>();
            Dictionary<CellRecord, string> auxiliaryEdgeLabels = new Dictionary<CellRecord, string>();

            foreach (CellRecord cell in cells)
            {
                string entity = GetRecordAttribute(cell, "rgEntity").Trim().ToLowerInvariant();
                bool hasEndpoints = GetRecordAttribute(cell, "source").Length > 0 && GetRecordAttribute(cell, "target").Length > 0;
                bool isEdge = entity == "edge" || IsTrue(GetRecordAttribute(cell, "edge")) || hasEndpoints;
                bool isGroupStyle = IsGroupStyle(cell.Style);
                bool isVertex = entity == "node" || entity == "group" || IsTrue(GetRecordAttribute(cell, "vertex")) || isGroupStyle;
                if (isEdge) edgeCells.Add(cell);
                else if (isVertex && !IsAuxiliaryVertex(cell))
                {
                    if (entity == "group" || (entity != "node" && isGroupStyle)) groupCells.Add(cell);
                    else nodeCells.Add(cell);
                }
            }

            foreach (CellRecord cell in cells)
            {
                if (!IsEdgeLabel(cell) || String.IsNullOrEmpty(cell.ParentId)) continue;
                CellRecord parent;
                if (!cellsById.TryGetValue(cell.ParentId, out parent)) continue;
                string label = DisplayText(cell);
                if (label.Length > 0 && !auxiliaryEdgeLabels.ContainsKey(parent)) auxiliaryEdgeLabels.Add(parent, label);
            }

            if (groupCells.Count > GraphSerialization.MaxGroups || nodeCells.Count > GraphSerialization.MaxNodes || edgeCells.Count > GraphSerialization.MaxEdges)
                throw new InvalidDataException("Draw.io 图的数据量超过程序上限。");

            GraphDocument graph = GraphSerialization.CreateBlank(String.IsNullOrWhiteSpace(title) ? "未命名关系图" : title);
            float shiftX = ReadFloatAttribute(model, "rgShiftX", 0), shiftY = ReadFloatAttribute(model, "rgShiftY", 0);
            float canvasWidth = ReadFloatAttribute(model, "rgCanvasWidth", graph.meta.canvasWidth);
            float canvasHeight = ReadFloatAttribute(model, "rgCanvasHeight", graph.meta.canvasHeight);
            if (canvasWidth > 0) graph.meta.canvasWidth = canvasWidth;
            if (canvasHeight > 0) graph.meta.canvasHeight = canvasHeight;
            string diagramType = GetAttributeInsensitive(model, "rgDiagramType");
            bool hasDiagramTypeMetadata = diagramType == "flowchart" || diagramType == "relationship";
            if (hasDiagramTypeMetadata) graph.meta.diagramType = diagramType;

            Dictionary<CellRecord, Coordinates> coordinateCache = new Dictionary<CellRecord, Coordinates>();
            Dictionary<CellRecord, EntityReference> entities = new Dictionary<CellRecord, EntityReference>();
            HashSet<string> usedGroupIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> usedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> usedEdgeIds = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 0; index < groupCells.Count; index++)
            {
                CellRecord cell = groupCells[index];
                Coordinates position = ResolveCoordinates(cell, cellsById, coordinateCache, new HashSet<CellRecord>(), 0);
                string id = UniqueImportedId(GetRecordAttribute(cell, "rgId"), cell.Id, "group_" + (index + 1), usedGroupIds);
                GraphGroup group = new GraphGroup
                {
                    id = id,
                    label = DisplayText(cell),
                    groups = new List<string>(),
                    x = position.X - shiftX,
                    y = position.Y - shiftY,
                    w = ReadGeometry(cell, "width", 260),
                    h = ReadGeometry(cell, "height", 220)
                };
                graph.groups.Add(group);
                entities[cell] = new EntityReference("group", id);
            }

            for (int index = 0; index < nodeCells.Count; index++)
            {
                CellRecord cell = nodeCells[index];
                Coordinates position = ResolveCoordinates(cell, cellsById, coordinateCache, new HashSet<CellRecord>(), 0);
                string visibleType, visibleLabel;
                ParseNodeDisplay(cell, out visibleType, out visibleLabel);
                string metadataType = GetRecordAttribute(cell, "rgType");
                if (metadataType.Length == 0) metadataType = GetWrapperAttribute(cell, "type");
                string type = String.IsNullOrWhiteSpace(visibleType) ? metadataType : visibleType;
                string shape = MapShape(GetRecordAttribute(cell, "rgShape"), cell.Style);
                string kind = MapKind(GetRecordAttribute(cell, "rgKind"), cell.Style, shape, type);
                if (String.IsNullOrWhiteSpace(type)) type = KindLabel(kind);
                string id = UniqueImportedId(GetRecordAttribute(cell, "rgId"), cell.Id, "node_" + (index + 1), usedNodeIds);
                GraphNode node = new GraphNode
                {
                    id = id,
                    label = visibleLabel,
                    type = type,
                    kind = kind,
                    shape = shape,
                    group = FindAncestorGroup(cell, cellsById, entities),
                    groups = new List<string>(),
                    x = position.X - shiftX,
                    y = position.Y - shiftY,
                    w = ReadGeometry(cell, "width", 150),
                    h = ReadGeometry(cell, "height", 54),
                    note = GetRecordAttribute(cell, "rgNote")
                };
                graph.nodes.Add(node);
                entities[cell] = new EntityReference("node", id);
            }

            for (int index = 0; index < edgeCells.Count; index++)
            {
                CellRecord cell = edgeCells[index];
                string sourceId = GetRecordAttribute(cell, "source"), targetId = GetRecordAttribute(cell, "target");
                EntityReference source = ResolveEntity(sourceId, cellsById, entities);
                EntityReference target = ResolveEntity(targetId, cellsById, entities);
                if (source == null || target == null) continue;
                string id = UniqueImportedId(GetRecordAttribute(cell, "rgId"), cell.Id, "edge_" + (index + 1), usedEdgeIds);
                string category = GetRecordAttribute(cell, "rgCategory");
                string edgeLabel = DisplayText(cell);
                if (edgeLabel.Length == 0) auxiliaryEdgeLabels.TryGetValue(cell, out edgeLabel);
                string sourceSide = ValidSide(GetRecordAttribute(cell, "rgSourceSide"));
                string targetSide = ValidSide(GetRecordAttribute(cell, "rgTargetSide"));
                if (sourceSide.Length == 0) sourceSide = MapPortSide(cell.Style, "exit", sourceId, cellsById);
                if (targetSide.Length == 0) targetSide = MapPortSide(cell.Style, "entry", targetId, cellsById);
                graph.edges.Add(new GraphEdge
                {
                    id = id,
                    source = source.Id,
                    target = target.Id,
                    sourceType = source.Type,
                    targetType = target.Type,
                    label = edgeLabel ?? "",
                    category = String.IsNullOrWhiteSpace(category) ? "core" : category,
                    lineType = MapLineType(GetRecordAttribute(cell, "rgLineType"), cell.Style),
                    sourceSide = sourceSide,
                    targetSide = targetSide
                });
            }

            if (!hasDiagramTypeMetadata && graph.meta.diagramType != "flowchart")
            {
                foreach (GraphNode node in graph.nodes)
                    if (node.shape != "process") { graph.meta.diagramType = "flowchart"; break; }
            }
            if (graph.groups.Count == 0 && graph.nodes.Count == 0 && graph.edges.Count == 0)
                throw new InvalidDataException("Draw.io 图中没有可导入的节点、分组或关系。");
            GraphDocument normalized = GraphSerialization.Normalize(graph);
            skippedEdges = Math.Max(0, edgeCells.Count - normalized.edges.Count);
            return normalized;
        }

        private static List<CellRecord> ReadCells(XmlElement model, out Dictionary<string, CellRecord> cellsById)
        {
            List<CellRecord> result = new List<CellRecord>();
            cellsById = new Dictionary<string, CellRecord>(StringComparer.Ordinal);
            XmlNodeList descendants = model.GetElementsByTagName("*");
            for (int index = 0; index < descendants.Count; index++)
            {
                XmlElement cell = descendants[index] as XmlElement;
                if (cell == null || !IsElement(cell, "mxCell")) continue;
                if (result.Count >= MaxCells) throw new InvalidDataException("Draw.io 图形单元过多，无法安全导入。");
                XmlElement wrapper = cell.ParentNode as XmlElement;
                if (wrapper == null || (!IsElement(wrapper, "object") && !IsElement(wrapper, "UserObject"))) wrapper = null;
                CellRecord record = new CellRecord(cell, wrapper);
                result.Add(record);
                AddCellAlias(cellsById, record.Id, record);
                AddCellAlias(cellsById, GetAttributeInsensitive(cell, "id"), record);
                if (wrapper != null) AddCellAlias(cellsById, GetAttributeInsensitive(wrapper, "id"), record);
            }
            return result;
        }

        private static void AddCellAlias(Dictionary<string, CellRecord> cells, string id, CellRecord record)
        {
            if (String.IsNullOrEmpty(id) || cells.ContainsKey(id)) return;
            cells.Add(id, record);
        }

        private static Coordinates ResolveCoordinates(CellRecord cell, Dictionary<string, CellRecord> cellsById,
            Dictionary<CellRecord, Coordinates> cache, HashSet<CellRecord> visiting, int depth)
        {
            Coordinates cached;
            if (cache.TryGetValue(cell, out cached)) return cached;
            if (depth > MaxParentDepth) throw new InvalidDataException("Draw.io 图形层级过深，无法安全导入。");
            if (!visiting.Add(cell)) throw new InvalidDataException("Draw.io 图形层级存在循环引用，无法导入。");
            Coordinates result = new Coordinates(ReadGeometry(cell, "x", 0), ReadGeometry(cell, "y", 0));
            CellRecord parent;
            if (!String.IsNullOrEmpty(cell.ParentId) && cellsById.TryGetValue(cell.ParentId, out parent))
            {
                Coordinates parentPosition = ResolveCoordinates(parent, cellsById, cache, visiting, depth + 1);
                result = new Coordinates(result.X + parentPosition.X, result.Y + parentPosition.Y);
            }
            visiting.Remove(cell);
            cache[cell] = result;
            return result;
        }

        private static EntityReference ResolveEntity(string id, Dictionary<string, CellRecord> cellsById,
            Dictionary<CellRecord, EntityReference> entities)
        {
            CellRecord cell;
            if (String.IsNullOrEmpty(id) || !cellsById.TryGetValue(id, out cell)) return null;
            HashSet<CellRecord> seen = new HashSet<CellRecord>();
            for (int depth = 0; depth <= MaxParentDepth && cell != null && seen.Add(cell); depth++)
            {
                EntityReference entity;
                if (entities.TryGetValue(cell, out entity)) return entity;
                if (String.IsNullOrEmpty(cell.ParentId) || !cellsById.TryGetValue(cell.ParentId, out cell)) return null;
            }
            return null;
        }

        private static string FindAncestorGroup(CellRecord cell, Dictionary<string, CellRecord> cellsById,
            Dictionary<CellRecord, EntityReference> entities)
        {
            CellRecord parent;
            if (String.IsNullOrEmpty(cell.ParentId) || !cellsById.TryGetValue(cell.ParentId, out parent)) return "";
            HashSet<CellRecord> seen = new HashSet<CellRecord>();
            for (int depth = 0; depth <= MaxParentDepth && parent != null && seen.Add(parent); depth++)
            {
                EntityReference entity;
                if (entities.TryGetValue(parent, out entity) && entity.Type == "group") return entity.Id;
                if (String.IsNullOrEmpty(parent.ParentId) || !cellsById.TryGetValue(parent.ParentId, out parent)) return "";
            }
            return "";
        }

        private static bool IsAuxiliaryVertex(CellRecord cell)
        {
            if (IsEdgeLabel(cell)) return true;
            XmlElement geometry = cell.Geometry;
            return geometry != null && IsTrue(GetAttributeInsensitive(geometry, "relative"));
        }

        private static bool IsEdgeLabel(CellRecord cell)
        {
            return cell.Style.HasToken("edgeLabel") || cell.Style.Raw.IndexOf("edgeLabel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsGroupStyle(StyleInfo style)
        {
            return style.HasToken("group") || style.HasToken("swimlane") ||
                String.Equals(style.Get("shape"), "swimlane", StringComparison.OrdinalIgnoreCase) ||
                IsTrue(style.Get("container"));
        }

        private static string MapLineType(string metadata, StyleInfo style)
        {
            string requested = (metadata ?? "").Trim().ToLowerInvariant();
            if (requested == "auto" || requested == "straight" || requested == "polyline" || requested == "curve") return requested;
            if (IsTrue(style.Get("curved"))) return "curve";
            string edgeStyle = style.Get("edgeStyle").ToLowerInvariant();
            if (edgeStyle.IndexOf("orthogonal", StringComparison.Ordinal) >= 0 || edgeStyle.IndexOf("elbow", StringComparison.Ordinal) >= 0 ||
                edgeStyle.IndexOf("segment", StringComparison.Ordinal) >= 0 || edgeStyle.IndexOf("entityrelation", StringComparison.Ordinal) >= 0 ||
                IsTrue(style.Get("orthogonalLoop"))) return "polyline";
            return "straight";
        }

        private static string MapPortSide(StyleInfo style, string prefix, string endpointId, Dictionary<string, CellRecord> cellsById)
        {
            float x, y;
            bool hasX = TryParseFloat(style.Get(prefix + "X"), out x), hasY = TryParseFloat(style.Get(prefix + "Y"), out y);
            string side = SideFromRelativePosition(hasX, x, hasY, y);
            if (side.Length > 0) return side;
            CellRecord endpoint;
            if (!String.IsNullOrEmpty(endpointId) && cellsById.TryGetValue(endpointId, out endpoint) && endpoint.Geometry != null &&
                IsTrue(GetAttributeInsensitive(endpoint.Geometry, "relative")))
            {
                hasX = TryParseFloat(GetAttributeInsensitive(endpoint.Geometry, "x"), out x);
                hasY = TryParseFloat(GetAttributeInsensitive(endpoint.Geometry, "y"), out y);
                return SideFromRelativePosition(hasX, x, hasY, y);
            }
            return "";
        }

        private static string SideFromRelativePosition(bool hasX, float x, bool hasY, float y)
        {
            float horizontal = hasX ? Math.Abs(x - .5f) : -1, vertical = hasY ? Math.Abs(y - .5f) : -1;
            if (hasX && (x <= .001f || x >= .999f) && horizontal >= vertical) return x <= .001f ? "left" : "right";
            if (hasY && (y <= .001f || y >= .999f)) return y <= .001f ? "top" : "bottom";
            if (hasX && (x <= .001f || x >= .999f)) return x <= .001f ? "left" : "right";
            return "";
        }

        private static string ValidSide(string value)
        {
            string side = (value ?? "").Trim().ToLowerInvariant();
            return side == "top" || side == "right" || side == "bottom" || side == "left" ? side : "";
        }

        private static string MapShape(string metadata, StyleInfo style)
        {
            string requested = (metadata ?? "").Trim().ToLowerInvariant();
            if (requested == "terminator" || requested == "process" || requested == "decision" || requested == "data" || requested == "document") return requested;
            string shape = style.Get("shape").ToLowerInvariant();
            string combined = shape + ";" + style.Raw.ToLowerInvariant();
            if (combined.IndexOf("rhombus", StringComparison.Ordinal) >= 0 || combined.IndexOf("diamond", StringComparison.Ordinal) >= 0 || combined.IndexOf("decision", StringComparison.Ordinal) >= 0) return "decision";
            if (combined.IndexOf("document", StringComparison.Ordinal) >= 0) return "document";
            if (combined.IndexOf("parallelogram", StringComparison.Ordinal) >= 0 || combined.IndexOf("flowchart.data", StringComparison.Ordinal) >= 0 || shape == "data") return "data";
            if (combined.IndexOf("terminator", StringComparison.Ordinal) >= 0 || combined.IndexOf("start_", StringComparison.Ordinal) >= 0 || shape == "ellipse") return "terminator";
            return "process";
        }

        private static string MapKind(string metadata, StyleInfo style, string shape, string type)
        {
            string requested = (metadata ?? "").Trim().ToLowerInvariant();
            if (IsKnownKind(requested)) return requested;
            string fill = style.Get("fillColor").Trim().ToLowerInvariant();
            if (fill == "#dcecff" || fill == "#dae8fc") return "resource";
            if (fill == "#dff3e7" || fill == "#d5e8d4") return "output";
            if (fill == "#f6e2ef" || fill == "#f8cecc") return "content";
            if (fill == "#eee5fb" || fill == "#e1d5e7") return "staff";
            if (fill == "#fff0d9" || fill == "#ffe6cc") return "commercial";
            string raw = style.Raw.ToLowerInvariant();
            if (raw.IndexOf("actor", StringComparison.Ordinal) >= 0 || raw.IndexOf("person", StringComparison.Ordinal) >= 0) return "staff";
            string loweredType = (type ?? "").ToLowerInvariant();
            if (loweredType.IndexOf("人员", StringComparison.Ordinal) >= 0 || loweredType.IndexOf("岗位", StringComparison.Ordinal) >= 0 || loweredType.IndexOf("角色", StringComparison.Ordinal) >= 0) return "staff";
            if (shape == "data") return "resource";
            if (shape == "document") return "content";
            if (shape == "terminator") return "output";
            if (shape == "decision") return "commercial";
            return "system";
        }

        private static bool IsKnownKind(string kind)
        {
            return kind == "resource" || kind == "system" || kind == "output" || kind == "content" || kind == "staff" || kind == "commercial";
        }

        private static string KindLabel(string kind)
        {
            if (kind == "resource") return "输入信息";
            if (kind == "output") return "结果产出";
            if (kind == "content") return "内容信息";
            if (kind == "staff") return "人员岗位";
            if (kind == "commercial") return "商业信息";
            return "节点类型";
        }

        private static void ParseNodeDisplay(CellRecord cell, out string type, out string label)
        {
            type = "";
            label = "";
            string value = GetVisibleValue(cell);
            string limited = LimitPayload(value);
            Match legacy = LegacyLabelPattern.Match(limited);
            if (legacy.Success)
            {
                type = HtmlToText(legacy.Groups["type"].Value);
                label = HtmlToText(legacy.Groups["label"].Value);
            }
            else
            {
                label = DisplayText(cell);
            }
        }

        private static string DisplayText(CellRecord cell)
        {
            string value = GetVisibleValue(cell);
            if (IsTrue(cell.Style.Get("html")) || LegacyLabelPattern.IsMatch(value ?? "") || KnownHtmlTagPattern.IsMatch(value ?? ""))
                return HtmlToText(value);
            return PlainText(value);
        }

        private static string HtmlToText(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            string text = LimitPayload(value).Replace("\r\n", "\n").Replace('\r', '\n');
            text = BreakPattern.Replace(text, "\n");
            text = BlockEndPattern.Replace(text, "\n");
            text = TagPattern.Replace(text, "");
            text = WebUtility.HtmlDecode(text) ?? "";
            return NormalizeText(text);
        }

        private static string PlainText(string value)
        {
            return NormalizeText(LimitPayload(value));
        }

        private static string CleanPageName(string value)
        {
            string limited = LimitPayload(value);
            string name = KnownHtmlTagPattern.IsMatch(limited) ? HtmlToText(limited) : PlainText(limited);
            name = name.Replace('\n', ' ');
            name = HorizontalSpacePattern.Replace(name, " ").Trim();
            if (name.Length > MaxPageNameLength) name = name.Substring(0, MaxPageNameLength).Trim();
            return name;
        }

        private static string NormalizeText(string value)
        {
            string text = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            text = text.Replace('\u00A0', ' ').Replace("\0", "");
            text = HorizontalSpacePattern.Replace(text, " ");
            text = AroundNewlinePattern.Replace(text, "\n");
            text = BlankLinePattern.Replace(text, "\n\n");
            return text.Trim();
        }

        private static int SkipXmlPreamble(string text, int start)
        {
            int position = start;
            if (StartsWithAt(text, position, "<?xml"))
            {
                int declarationEnd = text.IndexOf("?>", position, StringComparison.Ordinal);
                if (declarationEnd < 0) return position;
                position = declarationEnd + 2;
            }
            while (true)
            {
                while (position < text.Length && Char.IsWhiteSpace(text[position])) position++;
                if (!StartsWithAt(text, position, "<!--")) break;
                int commentEnd = text.IndexOf("-->", position, StringComparison.Ordinal);
                if (commentEnd < 0) return position;
                position = commentEnd + 3;
            }
            while (position < text.Length && Char.IsWhiteSpace(text[position])) position++;
            return position;
        }

        private static bool StartsWithAt(string text, int start, string value)
        {
            return start >= 0 && start + value.Length <= text.Length && String.Compare(text, start, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static string LimitPayload(string value)
        {
            if (String.IsNullOrEmpty(value) || value.Length <= MaxLabelPayload) return value ?? "";
            return value.Substring(0, MaxLabelPayload);
        }

        private static string GetVisibleValue(CellRecord cell)
        {
            if (cell.Wrapper != null)
            {
                string label;
                if (TryGetAttributeInsensitive(cell.Wrapper, "label", out label)) return label;
                if (TryGetAttributeInsensitive(cell.Wrapper, "value", out label)) return label;
                if (TryGetAttributeInsensitive(cell.Wrapper, "name", out label)) return label;
            }
            return GetAttributeInsensitive(cell.Cell, "value");
        }

        private static string GetRecordAttribute(CellRecord cell, string name)
        {
            string value = GetAttributeInsensitive(cell.Cell, name);
            if (value.Length > 0) return value;
            return cell.Wrapper == null ? "" : GetAttributeInsensitive(cell.Wrapper, name);
        }

        private static string GetWrapperAttribute(CellRecord cell, string name)
        {
            return cell.Wrapper == null ? "" : GetAttributeInsensitive(cell.Wrapper, name);
        }

        private static string UniqueImportedId(string metadataId, string cellId, string fallback, HashSet<string> used)
        {
            string candidate = !String.IsNullOrWhiteSpace(metadataId) ? metadataId : (!String.IsNullOrWhiteSpace(cellId) ? cellId : fallback);
            string id = GraphSerialization.UniqueId(candidate, used);
            used.Add(id);
            return id;
        }

        private static float ReadGeometry(CellRecord cell, string name, float fallback)
        {
            return cell.Geometry == null ? fallback : ReadFloatAttribute(cell.Geometry, name, fallback);
        }

        private static float ReadFloatAttribute(XmlElement element, string name, float fallback)
        {
            float value;
            return TryParseFloat(GetAttributeInsensitive(element, name), out value) ? value : fallback;
        }

        private static bool TryParseFloat(string value, out float result)
        {
            if (!Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) || Single.IsNaN(result) || Single.IsInfinity(result))
            {
                result = 0;
                return false;
            }
            return true;
        }

        private static bool IsTrue(string value)
        {
            return value == "1" || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeXml(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            int index = 0;
            while (index < value.Length && (Char.IsWhiteSpace(value[index]) || value[index] == '\uFEFF')) index++;
            return index < value.Length && value[index] == '<';
        }

        private static XmlDocument LoadXml(string xml, string message)
        {
            EnsureWithinLimit(xml);
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Prohibit;
            settings.XmlResolver = null;
            settings.MaxCharactersInDocument = MaxInflatedBytes;
            settings.MaxCharactersFromEntities = 1024;
            settings.IgnoreProcessingInstructions = false;
            settings.IgnoreComments = false;
            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            try
            {
                using (StringReader input = new StringReader(xml))
                using (XmlReader reader = XmlReader.Create(input, settings)) document.Load(reader);
                return document;
            }
            catch (XmlException error) { throw new InvalidDataException(message, error); }
            catch (InvalidOperationException error) { throw new InvalidDataException(message, error); }
        }

        private static void EnsureWithinLimit(string text)
        {
            if (text == null) return;
            if (text.Length > MaxInflatedBytes || Encoding.UTF8.GetByteCount(text) > MaxInflatedBytes)
                throw new InvalidDataException("Draw.io 内容超过 64 MB，无法安全导入。");
        }

        private static string RemoveBase64Whitespace(string value)
        {
            StringBuilder result = null;
            for (int index = 0; index < value.Length; index++)
            {
                if (!Char.IsWhiteSpace(value[index]))
                {
                    if (result != null) result.Append(value[index]);
                }
                else if (result == null)
                {
                    result = new StringBuilder(value.Length);
                    result.Append(value, 0, index);
                }
            }
            return result == null ? value : result.ToString();
        }

        private static int HexValue(byte value)
        {
            if (value >= (byte)'0' && value <= (byte)'9') return value - (byte)'0';
            if (value >= (byte)'A' && value <= (byte)'F') return value - (byte)'A' + 10;
            if (value >= (byte)'a' && value <= (byte)'f') return value - (byte)'a' + 10;
            return -1;
        }

        private static bool IsElement(XmlElement element, string localName)
        {
            return element != null && String.Equals(element.LocalName, localName, StringComparison.OrdinalIgnoreCase);
        }

        private static List<XmlElement> ChildElements(XmlElement parent, string localName)
        {
            List<XmlElement> result = new List<XmlElement>();
            foreach (XmlNode child in parent.ChildNodes)
            {
                XmlElement element = child as XmlElement;
                if (IsElement(element, localName)) result.Add(element);
            }
            return result;
        }

        private static XmlElement FirstDescendant(XmlElement parent, string localName)
        {
            if (parent == null) return null;
            XmlNodeList descendants = parent.GetElementsByTagName("*");
            for (int index = 0; index < descendants.Count; index++)
            {
                XmlElement element = descendants[index] as XmlElement;
                if (IsElement(element, localName)) return element;
            }
            return null;
        }

        private static string GetAttributeInsensitive(XmlElement element, string name)
        {
            string value;
            return TryGetAttributeInsensitive(element, name, out value) ? value : "";
        }

        private static bool TryGetAttributeInsensitive(XmlElement element, string name, out string value)
        {
            value = "";
            if (element == null) return false;
            if (element.HasAttribute(name)) { value = element.GetAttribute(name); return true; }
            foreach (XmlAttribute attribute in element.Attributes)
            {
                if (String.Equals(attribute.LocalName, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = attribute.Value ?? "";
                    return true;
                }
            }
            return false;
        }

        private sealed class DrawioSource
        {
            public readonly XmlElement BareModel;
            public readonly List<XmlElement> Pages;

            public DrawioSource(XmlElement bareModel, List<XmlElement> pages)
            {
                BareModel = bareModel;
                Pages = pages;
            }

            public bool IsBareModel { get { return BareModel != null; } }
            public int PageCount { get { return IsBareModel ? 1 : Pages.Count; } }
        }

        private sealed class CellRecord
        {
            public readonly XmlElement Cell;
            public readonly XmlElement Wrapper;
            public readonly XmlElement Geometry;
            public readonly string Id;
            public readonly string ParentId;
            public readonly StyleInfo Style;

            public CellRecord(XmlElement cell, XmlElement wrapper)
            {
                Cell = cell;
                Wrapper = wrapper;
                Geometry = null;
                foreach (XmlNode child in cell.ChildNodes)
                {
                    XmlElement element = child as XmlElement;
                    if (IsElement(element, "mxGeometry")) { Geometry = element; break; }
                }
                string wrapperId = wrapper == null ? "" : GetAttributeInsensitive(wrapper, "id");
                Id = wrapperId.Length > 0 ? wrapperId : GetAttributeInsensitive(cell, "id");
                ParentId = GetAttributeInsensitive(cell, "parent");
                if (ParentId.Length == 0 && wrapper != null) ParentId = GetAttributeInsensitive(wrapper, "parent");
                string style = GetAttributeInsensitive(cell, "style");
                if (style.Length == 0 && wrapper != null) style = GetAttributeInsensitive(wrapper, "style");
                Style = new StyleInfo(style);
            }
        }

        private sealed class StyleInfo
        {
            private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly string Raw;

            public StyleInfo(string style)
            {
                Raw = style ?? "";
                string[] pieces = Raw.Split(';');
                foreach (string pieceValue in pieces)
                {
                    string piece = pieceValue.Trim();
                    if (piece.Length == 0) continue;
                    int equals = piece.IndexOf('=');
                    if (equals < 0) tokens.Add(piece);
                    else
                    {
                        string key = piece.Substring(0, equals).Trim();
                        if (key.Length > 0) values[key] = piece.Substring(equals + 1).Trim();
                    }
                }
            }

            public string Get(string key)
            {
                string value;
                return values.TryGetValue(key, out value) ? value : "";
            }

            public bool HasToken(string token) { return tokens.Contains(token); }
        }

        private struct Coordinates
        {
            public readonly float X;
            public readonly float Y;
            public Coordinates(float x, float y) { X = x; Y = y; }
        }

        private sealed class EntityReference
        {
            public readonly string Type;
            public readonly string Id;
            public EntityReference(string type, string id) { Type = type; Id = id; }
        }
    }
}
