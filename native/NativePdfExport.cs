using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RelationshipGraphNative
{
    internal static class NativePdfExport
    {
        public static byte[] Build(GraphDocument source)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Write(source, stream);
                return stream.ToArray();
            }
        }

        public static void Write(GraphDocument source, Stream destination)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");
            if (!destination.CanWrite) throw new ArgumentException("PDF 目标流不可写。", "destination");
            if (destination.CanSeek && destination.Position != 0) throw new ArgumentException("PDF 目标流必须从起始位置写入。", "destination");

            GraphDocument graph = GraphSerialization.Normalize(GraphSerialization.Clone(source));
            Dictionary<string, GraphNode> nodes = graph.nodes.ToDictionary(delegate(GraphNode item) { return item.id; }, StringComparer.Ordinal);
            Dictionary<string, GraphGroup> groups = graph.groups.ToDictionary(delegate(GraphGroup item) { return item.id; }, StringComparer.Ordinal);
            RectangleF bounds = CalculateBounds(graph, nodes, groups);
            ValidateBounds(bounds);
            double pageScale = Math.Min(1d, Math.Min(14400d / Math.Max(1d, bounds.Width), 14400d / Math.Max(1d, bounds.Height)));
            RequireFinite(pageScale, "PDF 页面缩放");
            if (pageScale <= 0) throw new InvalidDataException("PDF 页面缩放必须大于 0。");
            double pageWidth = Math.Max(1d, (double)bounds.Width * pageScale), pageHeight = Math.Max(1d, (double)bounds.Height * pageScale);
            RequireFinite(pageWidth, "PDF 页面宽度"); RequireFinite(pageHeight, "PDF 页面高度");
            WritePdfDocument(destination, graph, nodes, groups, bounds, pageScale, pageWidth, pageHeight);
        }

        private static void WriteContent(TextWriter content, GraphDocument graph, Dictionary<string, GraphNode> nodes, Dictionary<string, GraphGroup> groups, RectangleF bounds, double pageScale)
        {
            double translateX = -(double)bounds.X * pageScale;
            double translateY = ((double)bounds.Y + bounds.Height) * pageScale;
            RequireFinite(translateX, "PDF 水平偏移"); RequireFinite(translateY, "PDF 垂直偏移");
            content.Write("q\n");
            content.Write(Number(pageScale)); content.Write(" 0 0 -"); content.Write(Number(pageScale)); content.Write(' ');
            content.Write(Number(translateX)); content.Write(' '); content.Write(Number(translateY)); content.Write(" cm\n");
            SetFill(content, "#ffffff");
            content.Write(Number(bounds.X)); content.Write(' '); content.Write(Number(bounds.Y)); content.Write(' ');
            content.Write(Number(bounds.Width)); content.Write(' '); content.Write(Number(bounds.Height)); content.Write(" re f\n");

            foreach (GraphGroup group in graph.groups.OrderByDescending(delegate(GraphGroup item) { return item.w * item.h; }))
            {
                using (GraphicsPath shape = RoundRect(new RectangleF(group.x, group.y, group.w, group.h), 12f))
                {
                    SetFill(content, "#f4f7fa"); AppendPath(content, shape); content.Write("f\n");
                    SetStroke(content, "#9ba6b2"); content.Write("1.4 w [7 5] 0 d\n"); AppendPath(content, shape); content.Write("S\n[] 0 d\n");
                }
                using (GraphicsPath label = TextPath(group.label, new RectangleF(group.x + 12, group.y + 5, Math.Max(1, group.w - 24), 28), 10.5f, FontStyle.Bold, StringAlignment.Near, StringAlignment.Center))
                {
                    SetFill(content, "#2d3946"); AppendPath(content, label); content.Write("f\n");
                }
            }

            foreach (GraphEdge edge in graph.edges)
            {
                RectangleF sourceRect, targetRect;
                if (!TryRect(edge.sourceType, edge.source, nodes, groups, out sourceRect) || !TryRect(edge.targetType, edge.target, nodes, groups, out targetRect)) continue;
                string sourceSide = String.IsNullOrEmpty(edge.sourceSide) ? ConnectionSide(sourceRect, targetRect) : edge.sourceSide;
                string targetSide = String.IsNullOrEmpty(edge.targetSide) ? ConnectionSide(targetRect, sourceRect) : edge.targetSide;
                PointF sourcePoint = Port(sourceRect, sourceSide), targetPoint = Port(targetRect, targetSide);
                string color = SourceAccentColor(edge, nodes);
                using (GraphicsPath path = EdgePath(edge.lineType, sourceSide, targetSide, sourcePoint, targetPoint))
                {
                    SetStroke(content, color); content.Write("2 w 1 J 1 j\n"); AppendPath(content, path); content.Write("S\n");
                    using (GraphicsPath arrow = ArrowPath(path, 8f))
                    {
                        SetFill(content, color); AppendPath(content, arrow); content.Write("f\n");
                    }
                }

                if (!String.IsNullOrWhiteSpace(edge.label))
                {
                    PointF midpoint = EdgePathMidpoint(edge.lineType, sourceSide, targetSide, sourcePoint, targetPoint);
                    float width = Math.Max(44f, edge.label.Length * 14f + 14f);
                    RectangleF box = new RectangleF(midpoint.X - width / 2f, midpoint.Y - 13f, width, 25f);
                    using (GraphicsPath shape = RoundRect(box, 7f))
                    {
                        SetFill(content, "#ffffff"); AppendPath(content, shape); content.Write("f\n");
                        SetStroke(content, "#d9e0e7"); content.Write("1 w\n"); AppendPath(content, shape); content.Write("S\n");
                    }
                    using (GraphicsPath label = TextPath(edge.label, box, 8.8f, FontStyle.Regular, StringAlignment.Center, StringAlignment.Center))
                    {
                        SetFill(content, "#384657"); AppendPath(content, label); content.Write("f\n");
                    }
                }
            }

            foreach (GraphNode node in graph.nodes)
            {
                RectangleF rect = new RectangleF(node.x, node.y, node.w, node.h);
                using (GraphicsPath shape = RoundRect(rect, 9f))
                {
                    SetFill(content, NodeColor(node.kind)); AppendPath(content, shape); content.Write("f\n");
                    SetStroke(content, "#697785"); content.Write("1.4 w\n"); AppendPath(content, shape); content.Write("S\n");
                }
                using (GraphicsPath type = TextPath(node.type ?? "节点", new RectangleF(node.x + 9, node.y + 3, Math.Max(1, node.w - 18), 15), 7.8f, FontStyle.Regular, StringAlignment.Near, StringAlignment.Center))
                {
                    SetFill(content, "#425064"); AppendPath(content, type); content.Write("f\n");
                }
                using (GraphicsPath label = TextPath(node.label ?? "", new RectangleF(node.x + 6, node.y + 17, Math.Max(1, node.w - 12), Math.Max(1, node.h - 18)), 9.2f, FontStyle.Bold, StringAlignment.Center, StringAlignment.Center))
                {
                    SetFill(content, "#17202b"); AppendPath(content, label); content.Write("f\n");
                }
            }
            content.Write("Q\n");
        }

        private static void WritePdfDocument(Stream destination, GraphDocument graph, Dictionary<string, GraphNode> nodes, Dictionary<string, GraphGroup> groups, RectangleF bounds, double pageScale, double pageWidth, double pageHeight)
        {
            CountingWriteStream output = new CountingWriteStream(destination);
            long[] offsets = new long[6];
            WriteAscii(output, "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");

            offsets[1] = output.BytesWritten;
            WriteAscii(output, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            offsets[2] = output.BytesWritten;
            WriteAscii(output, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
            offsets[3] = output.BytesWritten;
            WriteAscii(output, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + Number(pageWidth) + " " + Number(pageHeight) + "] /Resources << >> /Contents 4 0 R >>\nendobj\n");
            offsets[4] = output.BytesWritten;
            // An indirect length keeps the production path streaming without seeking or back-patching.
            WriteAscii(output, "4 0 obj\n<< /Length 5 0 R /Filter /FlateDecode >>\nstream\n");
            long contentStart = output.BytesWritten;
            WriteCompressedContent(output, delegate(TextWriter writer) { WriteContent(writer, graph, nodes, groups, bounds, pageScale); });
            long contentLength = output.BytesWritten - contentStart;
            WriteAscii(output, "\nendstream\nendobj\n");

            offsets[5] = output.BytesWritten;
            WriteAscii(output, "5 0 obj\n" + contentLength.ToString(CultureInfo.InvariantCulture) + "\nendobj\n");
            long xref = output.BytesWritten;
            ValidateXrefOffset(xref);
            WriteAscii(output, "xref\n0 6\n0000000000 65535 f \n");
            for (int i = 1; i <= 5; i++) WriteAscii(output, XrefOffset(offsets[i]) + " 00000 n \n");
            WriteAscii(output, "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
            output.Flush();
        }

        private static void WriteCompressedContent(Stream destination, Action<TextWriter> writer)
        {
            // .NET Framework DeflateStream emits RFC 1951 data; PDF FlateDecode expects an RFC 1950 zlib stream.
            destination.WriteByte(0x78);
            destination.WriteByte(0x9c);
            uint checksum;
            using (DeflateStream deflate = new DeflateStream(destination, CompressionMode.Compress, true))
            {
                using (Adler32WriteStream checkedStream = new Adler32WriteStream(deflate, true))
                {
                    using (StreamWriter textWriter = new StreamWriter(checkedStream, new UTF8Encoding(false), 8192, true))
                        writer(textWriter);
                    checksum = checkedStream.Checksum;
                }
            }
            WriteUInt32BigEndian(destination, checksum);
        }

        private static void AppendPath(TextWriter output, GraphicsPath path)
        {
            PointF[] points = path.PathPoints; byte[] types = path.PathTypes;
            int index = 0;
            while (index < points.Length)
            {
                int kind = types[index] & 7;
                if (kind == 0)
                {
                    output.Write(Number(points[index].X)); output.Write(' '); output.Write(Number(points[index].Y)); output.Write(" m\n");
                    if ((types[index] & 128) != 0) output.Write("h\n");
                    index++;
                }
                else if (kind == 3 && index + 2 < points.Length)
                {
                    output.Write(Number(points[index].X)); output.Write(' '); output.Write(Number(points[index].Y)); output.Write(' ');
                    output.Write(Number(points[index + 1].X)); output.Write(' '); output.Write(Number(points[index + 1].Y)); output.Write(' ');
                    output.Write(Number(points[index + 2].X)); output.Write(' '); output.Write(Number(points[index + 2].Y)); output.Write(" c\n");
                    if ((types[index + 2] & 128) != 0) output.Write("h\n");
                    index += 3;
                }
                else
                {
                    output.Write(Number(points[index].X)); output.Write(' '); output.Write(Number(points[index].Y)); output.Write(" l\n");
                    if ((types[index] & 128) != 0) output.Write("h\n");
                    index++;
                }
            }
        }

        private static GraphicsPath TextPath(string value, RectangleF layout, float size, FontStyle style, StringAlignment horizontal, StringAlignment vertical)
        {
            GraphicsPath path = new GraphicsPath();
            if (String.IsNullOrEmpty(value)) return path;
            FontFamily family = null;
            try
            {
                try { family = new FontFamily("Microsoft YaHei UI"); }
                catch { family = new FontFamily(GenericFontFamilies.SansSerif); }
                using (StringFormat format = new StringFormat { Alignment = horizontal, LineAlignment = vertical, Trimming = StringTrimming.EllipsisCharacter })
                    path.AddString(value, family, (int)style, size, layout, format);
            }
            finally { if (family != null) family.Dispose(); }
            return path;
        }

        private static GraphicsPath RoundRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(Math.Min(rect.Width, rect.Height), radius * 2f);
            if (diameter <= .1f) { path.AddRectangle(rect); return path; }
            RectangleF arc = new RectangleF(rect.X, rect.Y, diameter, diameter);
            path.AddArc(arc, 180, 90); arc.X = rect.Right - diameter; path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter; path.AddArc(arc, 0, 90); arc.X = rect.Left; path.AddArc(arc, 90, 90);
            path.CloseFigure(); return path;
        }

        private static GraphicsPath EdgePath(string lineType, string sourceSide, string targetSide, PointF source, PointF target)
        {
            GraphicsPath path = new GraphicsPath();
            if (lineType == "straight") path.AddLine(source, target);
            else if (lineType == "polyline")
            {
                if (sourceSide == "left" || sourceSide == "right")
                {
                    float middle = (source.X + target.X) / 2f;
                    path.AddLines(new[] { source, new PointF(middle, source.Y), new PointF(middle, target.Y), target });
                }
                else
                {
                    float middle = (source.Y + target.Y) / 2f;
                    path.AddLines(new[] { source, new PointF(source.X, middle), new PointF(target.X, middle), target });
                }
            }
            else
            {
                PointF sourceVector = Vector(sourceSide), targetVector = Vector(targetSide);
                float distance = Math.Max(45f, Math.Min(140f, Distance(source, target) * .42f));
                path.AddBezier(source, new PointF(source.X + sourceVector.X * distance, source.Y + sourceVector.Y * distance), new PointF(target.X + targetVector.X * distance, target.Y + targetVector.Y * distance), target);
            }
            return path;
        }

        private static GraphicsPath ArrowPath(GraphicsPath source, float size)
        {
            GraphicsPath flattened = (GraphicsPath)source.Clone();
            try
            {
                flattened.Flatten(null, 1.2f); PointF[] points = flattened.PathPoints;
                GraphicsPath arrow = new GraphicsPath();
                if (points.Length < 2) return arrow;
                PointF end = points[points.Length - 1], previous = points[points.Length - 2];
                float angle = (float)Math.Atan2(end.Y - previous.Y, end.X - previous.X);
                PointF left = new PointF(end.X - (float)Math.Cos(angle - .55f) * size, end.Y - (float)Math.Sin(angle - .55f) * size);
                PointF right = new PointF(end.X - (float)Math.Cos(angle + .55f) * size, end.Y - (float)Math.Sin(angle + .55f) * size);
                arrow.AddPolygon(new[] { end, left, right }); return arrow;
            }
            finally { flattened.Dispose(); }
        }

        private static PointF EdgePathMidpoint(string lineType, string sourceSide, string targetSide, PointF source, PointF target)
        {
            using (GraphicsPath path = EdgePath(lineType, sourceSide, targetSide, source, target))
            using (GraphicsPath flattened = (GraphicsPath)path.Clone())
            {
                flattened.Flatten(null, .6f); PointF[] points = flattened.PathPoints;
                if (points.Length == 0) return PointF.Empty;
                float total = 0; for (int i = 1; i < points.Length; i++) total += Distance(points[i - 1], points[i]);
                if (total <= .001f) return points[0];
                float targetDistance = total / 2f, travelled = 0;
                for (int i = 1; i < points.Length; i++)
                {
                    float segment = Distance(points[i - 1], points[i]);
                    if (travelled + segment >= targetDistance && segment > .001f)
                    {
                        float ratio = (targetDistance - travelled) / segment;
                        return new PointF(points[i - 1].X + (points[i].X - points[i - 1].X) * ratio, points[i - 1].Y + (points[i].Y - points[i - 1].Y) * ratio);
                    }
                    travelled += segment;
                }
                return points[points.Length - 1];
            }
        }

        private static RectangleF CalculateBounds(GraphDocument graph, Dictionary<string, GraphNode> nodes, Dictionary<string, GraphGroup> groups)
        {
            bool hasContent = false; float minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (GraphGroup group in graph.groups) IncludeRect(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new RectangleF(group.x, group.y, group.w, group.h));
            foreach (GraphNode node in graph.nodes) IncludeRect(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new RectangleF(node.x, node.y, node.w, node.h));
            foreach (GraphEdge edge in graph.edges)
            {
                RectangleF sourceRect, targetRect;
                if (!TryRect(edge.sourceType, edge.source, nodes, groups, out sourceRect) || !TryRect(edge.targetType, edge.target, nodes, groups, out targetRect)) continue;
                string sourceSide = String.IsNullOrEmpty(edge.sourceSide) ? ConnectionSide(sourceRect, targetRect) : edge.sourceSide;
                string targetSide = String.IsNullOrEmpty(edge.targetSide) ? ConnectionSide(targetRect, sourceRect) : edge.targetSide;
                PointF sourcePoint = Port(sourceRect, sourceSide), targetPoint = Port(targetRect, targetSide);
                using (GraphicsPath path = EdgePath(edge.lineType, sourceSide, targetSide, sourcePoint, targetPoint)) IncludeRect(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, path.GetBounds());
                if (!String.IsNullOrWhiteSpace(edge.label))
                {
                    PointF midpoint = EdgePathMidpoint(edge.lineType, sourceSide, targetSide, sourcePoint, targetPoint);
                    float width = Math.Max(44f, edge.label.Length * 14f + 14f);
                    IncludeRect(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new RectangleF(midpoint.X - width / 2f, midpoint.Y - 13f, width, 25f));
                }
            }
            if (!hasContent) return new RectangleF(0, 0, graph.meta.canvasWidth, graph.meta.canvasHeight);
            const float padding = 36f;
            return new RectangleF(minX - padding, minY - padding, Math.Max(1f, maxX - minX + padding * 2f), Math.Max(1f, maxY - minY + padding * 2f));
        }

        private static void IncludeRect(ref bool hasContent, ref float minX, ref float minY, ref float maxX, ref float maxY, RectangleF rect)
        {
            if (!hasContent) { minX = rect.Left; minY = rect.Top; maxX = rect.Right; maxY = rect.Bottom; hasContent = true; return; }
            minX = Math.Min(minX, rect.Left); minY = Math.Min(minY, rect.Top); maxX = Math.Max(maxX, rect.Right); maxY = Math.Max(maxY, rect.Bottom);
        }

        private static void ValidateBounds(RectangleF bounds)
        {
            RequireFinite(bounds.X, "PDF 内容左边界"); RequireFinite(bounds.Y, "PDF 内容上边界");
            RequireFinite(bounds.Width, "PDF 内容宽度"); RequireFinite(bounds.Height, "PDF 内容高度");
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidDataException("PDF 内容边界必须具有正的宽度和高度。");
            RequireFinite((double)bounds.X + bounds.Width, "PDF 内容右边界");
            RequireFinite((double)bounds.Y + bounds.Height, "PDF 内容下边界");
        }

        private static bool TryRect(string type, string id, Dictionary<string, GraphNode> nodes, Dictionary<string, GraphGroup> groups, out RectangleF rect)
        {
            GraphGroup group; GraphNode node;
            if (type == "group" && groups.TryGetValue(id ?? "", out group)) { rect = new RectangleF(group.x, group.y, group.w, group.h); return true; }
            if (nodes.TryGetValue(id ?? "", out node)) { rect = new RectangleF(node.x, node.y, node.w, node.h); return true; }
            rect = RectangleF.Empty; return false;
        }

        private static string ConnectionSide(RectangleF from, RectangleF to)
        {
            PointF a = Center(from), b = Center(to); float dx = b.X - a.X, dy = b.Y - a.Y;
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? "right" : "left";
            return dy >= 0 ? "bottom" : "top";
        }

        private static PointF Port(RectangleF rect, string side)
        {
            if (side == "top") return new PointF(rect.X + rect.Width / 2f, rect.Top);
            if (side == "bottom") return new PointF(rect.X + rect.Width / 2f, rect.Bottom);
            if (side == "left") return new PointF(rect.Left, rect.Y + rect.Height / 2f);
            return new PointF(rect.Right, rect.Y + rect.Height / 2f);
        }

        private static PointF Vector(string side)
        {
            if (side == "top") return new PointF(0, -1); if (side == "bottom") return new PointF(0, 1);
            if (side == "left") return new PointF(-1, 0); return new PointF(1, 0);
        }

        private static PointF Center(RectangleF rect) { return new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f); }
        private static float Distance(PointF first, PointF second) { float x = first.X - second.X, y = first.Y - second.Y; return (float)Math.Sqrt(x * x + y * y); }

        private static string Number(float value)
        {
            RequireFinite(value, "PDF 数值");
            if (value == 0f) return "0";
            return ExpandScientificNotation(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static string Number(double value)
        {
            RequireFinite(value, "PDF 数值");
            if (value == 0d) return "0";
            return ExpandScientificNotation(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static string ExpandScientificNotation(string value)
        {
            int exponentMarker = value.IndexOf('E');
            if (exponentMarker < 0) exponentMarker = value.IndexOf('e');
            if (exponentMarker < 0) return value;

            bool negative = value[0] == '-';
            int mantissaStart = negative || value[0] == '+' ? 1 : 0;
            string mantissa = value.Substring(mantissaStart, exponentMarker - mantissaStart);
            int decimalPoint = mantissa.IndexOf('.');
            string digits = decimalPoint < 0 ? mantissa : mantissa.Remove(decimalPoint, 1);
            int decimalPosition = (decimalPoint < 0 ? digits.Length : decimalPoint)
                + Int32.Parse(value.Substring(exponentMarker + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            string expanded;
            if (decimalPosition <= 0) expanded = "0." + new string('0', -decimalPosition) + digits;
            else if (decimalPosition >= digits.Length) expanded = digits + new string('0', decimalPosition - digits.Length);
            else expanded = digits.Insert(decimalPosition, ".");
            return negative ? "-" + expanded : expanded;
        }

        private static void RequireFinite(float value, string name)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value)) throw new InvalidDataException(name + "不是有限数值。");
        }

        private static void RequireFinite(double value, string name)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) throw new InvalidDataException(name + "不是有限数值。");
        }

        private static void SetFill(TextWriter output, string value) { AppendColor(output, value); output.Write(" rg\n"); }
        private static void SetStroke(TextWriter output, string value) { AppendColor(output, value); output.Write(" RG\n"); }
        private static void AppendColor(TextWriter output, string value)
        {
            Color color = ColorTranslator.FromHtml(value);
            output.Write(Number(color.R / 255f)); output.Write(' '); output.Write(Number(color.G / 255f)); output.Write(' '); output.Write(Number(color.B / 255f));
        }

        private static string NodeColor(string kind)
        {
            if (kind == "resource") return "#dcecff"; if (kind == "output") return "#dff3e7"; if (kind == "content") return "#f6e2ef";
            if (kind == "staff") return "#eee5fb"; if (kind == "commercial") return "#fff0d9"; return "#e6ebf0";
        }

        private static string SourceAccentColor(GraphEdge edge, Dictionary<string, GraphNode> nodes)
        {
            if (edge != null && edge.sourceType == "group") return "#9ba6b2";
            GraphNode source;
            return edge != null && nodes.TryGetValue(edge.source ?? "", out source) ? NodeAccentColor(source.kind) : "#7189a3";
        }

        private static string NodeAccentColor(string kind)
        {
            if (kind == "resource") return "#4b9eea"; if (kind == "output") return "#54ad72"; if (kind == "content") return "#d765a4";
            if (kind == "staff") return "#8b6fbc"; if (kind == "commercial") return "#e8963e"; return "#7189a3";
        }

        private static string XrefOffset(long value)
        {
            ValidateXrefOffset(value);
            return value.ToString("0000000000", CultureInfo.InvariantCulture);
        }

        private static void ValidateXrefOffset(long value)
        {
            if (value < 0 || value > 9999999999L) throw new InvalidDataException("PDF 文件过大，无法使用传统交叉引用表。");
        }

        private static void WriteUInt32BigEndian(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static void WriteAscii(Stream stream, string value)
        {
            byte[] bytes = Encoding.GetEncoding(1252).GetBytes(value); stream.Write(bytes, 0, bytes.Length);
        }

        private sealed class CountingWriteStream : Stream
        {
            private readonly Stream _destination;

            public CountingWriteStream(Stream destination) { _destination = destination; }
            public long BytesWritten { get; private set; }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { return BytesWritten; } set { throw new NotSupportedException(); } }
            public override void Flush() { _destination.Flush(); }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count)
            {
                _destination.Write(buffer, offset, count);
                BytesWritten = checked(BytesWritten + count);
            }
            public override void WriteByte(byte value)
            {
                _destination.WriteByte(value);
                BytesWritten = checked(BytesWritten + 1);
            }
        }

        private sealed class Adler32WriteStream : Stream
        {
            private const uint Modulus = 65521;
            private readonly Stream _destination;
            private readonly bool _leaveOpen;
            private uint _a = 1;
            private uint _b;

            public Adler32WriteStream(Stream destination, bool leaveOpen)
            {
                if (destination == null) throw new ArgumentNullException("destination");
                _destination = destination;
                _leaveOpen = leaveOpen;
            }

            public uint Checksum { get { return (_b << 16) | _a; } }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { _destination.Flush(); }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (buffer == null) throw new ArgumentNullException("buffer");
                if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException("offset");
                _destination.Write(buffer, offset, count);
                Update(buffer, offset, count);
            }
            public override void WriteByte(byte value)
            {
                _destination.WriteByte(value);
                _a = (_a + value) % Modulus;
                _b = (_b + _a) % Modulus;
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing && !_leaveOpen) _destination.Dispose();
                base.Dispose(disposing);
            }

            private void Update(byte[] buffer, int offset, int count)
            {
                while (count > 0)
                {
                    int block = Math.Min(count, 5552);
                    int end = offset + block;
                    for (int i = offset; i < end; i++) { _a += buffer[i]; _b += _a; }
                    _a %= Modulus; _b %= Modulus;
                    offset = end; count -= block;
                }
            }
        }
    }
}
