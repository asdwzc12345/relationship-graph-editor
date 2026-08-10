using System;
using System.Collections.Generic;

namespace RelationshipGraphNative
{
    internal sealed class GraphHistory
    {
        private const int MaximumEntries = 60;
        private const int MaximumCharacters = 16 * 1024 * 1024;
        private readonly List<string> _snapshots = new List<string>();
        private int _characters;

        public int Count { get { return _snapshots.Count; } }
        internal int StoredCharactersForTesting { get { return _characters; } }

        public void Clear()
        {
            _snapshots.Clear();
            _characters = 0;
        }

        public void Push(GraphDocument document)
        {
            if (document == null) return;
            PushSerialized(GraphSerialization.Serialize(document, false));
        }

        public void PushSerialized(string snapshot)
        {
            if (String.IsNullOrEmpty(snapshot)) return;
            _snapshots.Add(snapshot);
            _characters += snapshot.Length;
            while (_snapshots.Count > 1 && (_snapshots.Count > MaximumEntries || _characters > MaximumCharacters))
            {
                _characters -= _snapshots[0].Length;
                _snapshots.RemoveAt(0);
            }
        }

        public GraphDocument Pop()
        {
            if (_snapshots.Count == 0) throw new InvalidOperationException("撤销历史为空。");
            int index = _snapshots.Count - 1;
            string snapshot = _snapshots[index];
            _snapshots.RemoveAt(index);
            _characters -= snapshot.Length;
            return GraphSerialization.Deserialize(snapshot);
        }
    }
}
