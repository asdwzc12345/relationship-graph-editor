using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace RelationshipGraphNative
{
    internal enum SelectionArrangement { Bottom, HorizontalCenter, Horizontal, Vertical, Left, Right, VerticalCenter }

    internal static class GraphSelectionArrangement
    {
        private sealed class Item
        {
            public GraphNode Node;
            public GraphGroup Group;
            public RectangleF Bounds;
            public PointF Offset;
        }

        private static List<Item> Items(GraphDocument graph, ICollection<string> nodes, ICollection<string> groups)
        {
            List<Item> items = new List<Item>();
            foreach (GraphGroup group in graph.groups)
                if (groups.Contains(group.id) && !(group.groups ?? new List<string>()).Any(groups.Contains))
                    items.Add(new Item { Group = group, Bounds = new RectangleF(group.x, group.y, group.w, group.h) });
            foreach (GraphNode node in graph.nodes)
                if (nodes.Contains(node.id) && !(node.groups ?? new List<string>()).Any(groups.Contains))
                    items.Add(new Item { Node = node, Bounds = new RectangleF(node.x, node.y, node.w, node.h) });
            return items;
        }

        public static int Count(GraphDocument graph, ICollection<string> nodes, ICollection<string> groups)
        {
            return graph == null ? 0 : Items(graph, nodes, groups).Count;
        }

        public static bool Apply(GraphDocument graph, ICollection<string> nodes, ICollection<string> groups, SelectionArrangement mode)
        {
            List<Item> items = Items(graph, nodes, groups);
            bool distribute = mode == SelectionArrangement.Horizontal || mode == SelectionArrangement.Vertical;
            if (items.Count < (distribute ? 3 : 2)) return false;
            if (distribute)
            {
                bool horizontal = mode == SelectionArrangement.Horizontal;
                items = items.OrderBy(item => horizontal ? item.Bounds.Left : item.Bounds.Top).ToList();
                float start = horizontal ? items[0].Bounds.Left : items[0].Bounds.Top;
                float end = horizontal ? items[items.Count - 1].Bounds.Right : items[items.Count - 1].Bounds.Bottom;
                float total = items.Sum(item => horizontal ? item.Bounds.Width : item.Bounds.Height);
                float gap = (end - start - total) / (items.Count - 1);
                float position = start;
                for (int i = 0; i < items.Count; i++)
                {
                    Item item = items[i];
                    if (i > 0 && i < items.Count - 1)
                        item.Offset = horizontal ? new PointF(position - item.Bounds.Left, 0) : new PointF(0, position - item.Bounds.Top);
                    position += (horizontal ? item.Bounds.Width : item.Bounds.Height) + gap;
                }
            }
            else
            {
                float bottom = items.Max(item => item.Bounds.Bottom);
                float center = (items.Min(item => item.Bounds.Top) + bottom) / 2f;
                float left = items.Min(item => item.Bounds.Left);
                float right = items.Max(item => item.Bounds.Right);
                foreach (Item item in items)
                {
                    switch (mode)
                    {
                        case SelectionArrangement.Bottom: item.Offset = new PointF(0, bottom - item.Bounds.Bottom); break;
                        case SelectionArrangement.HorizontalCenter: item.Offset = new PointF(0, center - item.Bounds.Top - item.Bounds.Height / 2f); break;
                        case SelectionArrangement.Left: item.Offset = new PointF(left - item.Bounds.Left, 0); break;
                        case SelectionArrangement.Right: item.Offset = new PointF(right - item.Bounds.Right, 0); break;
                        case SelectionArrangement.VerticalCenter: item.Offset = new PointF((left + right - item.Bounds.Width) / 2f - item.Bounds.Left, 0); break;
                        default: return false;
                    }
                }
            }
            if (!items.Any(item => Math.Abs(item.Offset.X) > .001f || Math.Abs(item.Offset.Y) > .001f)) return false;
            // Resolve owners before moving anything: a group and its contents move only once.
            foreach (GraphNode node in graph.nodes)
            {
                Item owner = items.FirstOrDefault(item => item.Node == node) ?? items.FirstOrDefault(item => item.Group != null && node.groups != null && node.groups.Contains(item.Group.id));
                if (owner != null) { node.x += owner.Offset.X; node.y += owner.Offset.Y; }
            }
            foreach (GraphGroup group in graph.groups)
            {
                Item owner = items.FirstOrDefault(item => item.Group == group) ?? items.FirstOrDefault(item => item.Group != null && group.groups != null && group.groups.Contains(item.Group.id));
                if (owner != null) { group.x += owner.Offset.X; group.y += owner.Offset.Y; }
            }
            return true;
        }
    }
}
