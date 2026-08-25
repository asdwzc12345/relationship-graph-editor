using System;
using System.Collections.Generic;
using System.Text;

namespace RelationshipGraphNative
{
    internal sealed class GraphTextReplacementResult
    {
        public int AppliedOccurrences;
        public int ChangedFields;
        public int SkippedOccurrences;
    }

    internal static class GraphTextReplacement
    {
        public static bool Contains(string value, string query)
        {
            return !String.IsNullOrEmpty(value) && !String.IsNullOrEmpty(query) &&
                value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        public static GraphTextReplacementResult ReplaceAll(GraphDocument graph, string query, string replacement)
        {
            return Replace(graph, query, replacement, null, null, "", true);
        }

        public static GraphTextReplacementResult ReplaceSelection(GraphDocument graph, string query, string replacement,
            ICollection<string> selectedNodeIds, ICollection<string> selectedGroupIds, string selectedEdgeId)
        {
            return Replace(graph, query, replacement, selectedNodeIds, selectedGroupIds, selectedEdgeId, false);
        }

        private static GraphTextReplacementResult Replace(GraphDocument graph, string query, string replacement,
            ICollection<string> selectedNodeIds, ICollection<string> selectedGroupIds, string selectedEdgeId, bool replaceAll)
        {
            GraphTextReplacementResult result = new GraphTextReplacementResult();
            if (graph == null || String.IsNullOrWhiteSpace(query)) return result;
            replacement = replacement ?? "";
            HashSet<string> nodeIds = selectedNodeIds == null ? new HashSet<string>() : new HashSet<string>(selectedNodeIds);
            HashSet<string> groupIds = selectedGroupIds == null ? new HashSet<string>() : new HashSet<string>(selectedGroupIds);

            foreach (GraphNode node in graph.nodes)
            {
                if (!replaceAll && !nodeIds.Contains(node.id)) continue;
                ReplaceRequiredField(delegate { return node.label; }, delegate(string value) { node.label = value; }, query, replacement, result);
                ReplaceRequiredField(delegate { return node.type; }, delegate(string value) { node.type = value; }, query, replacement, result);
                ReplaceOptionalField(delegate { return node.note; }, delegate(string value) { node.note = value; }, query, replacement, result);
            }
            foreach (GraphGroup group in graph.groups)
            {
                if (!replaceAll && !groupIds.Contains(group.id)) continue;
                ReplaceRequiredField(delegate { return group.label; }, delegate(string value) { group.label = value; }, query, replacement, result);
            }
            foreach (GraphEdge edge in graph.edges)
            {
                if (!replaceAll && !String.Equals(edge.id, selectedEdgeId, StringComparison.Ordinal)) continue;
                ReplaceOptionalField(delegate { return edge.label; }, delegate(string value) { edge.label = value; }, query, replacement, result);
            }
            return result;
        }

        private static void ReplaceRequiredField(Func<string> read, Action<string> write, string query, string replacement, GraphTextReplacementResult result)
        {
            int occurrences;
            string original = read() ?? "";
            string changed = ReplaceText(original, query, replacement, out occurrences);
            if (occurrences == 0 || String.Equals(original, changed, StringComparison.Ordinal)) return;
            if (String.IsNullOrWhiteSpace(changed))
            {
                result.SkippedOccurrences += occurrences;
                return;
            }
            write(changed); result.AppliedOccurrences += occurrences; result.ChangedFields++;
        }

        private static void ReplaceOptionalField(Func<string> read, Action<string> write, string query, string replacement, GraphTextReplacementResult result)
        {
            int occurrences;
            string original = read() ?? "";
            string changed = ReplaceText(original, query, replacement, out occurrences);
            if (occurrences == 0 || String.Equals(original, changed, StringComparison.Ordinal)) return;
            write(changed); result.AppliedOccurrences += occurrences; result.ChangedFields++;
        }

        internal static string ReplaceText(string value, string query, string replacement, out int occurrences)
        {
            occurrences = 0;
            value = value ?? ""; replacement = replacement ?? "";
            if (String.IsNullOrEmpty(query)) return value;
            int position = 0, match;
            StringBuilder output = new StringBuilder(value.Length);
            while ((match = value.IndexOf(query, position, StringComparison.CurrentCultureIgnoreCase)) >= 0)
            {
                output.Append(value, position, match - position);
                output.Append(replacement);
                position = match + query.Length;
                occurrences++;
            }
            if (occurrences == 0) return value;
            output.Append(value, position, value.Length - position);
            return output.ToString();
        }
    }
}
