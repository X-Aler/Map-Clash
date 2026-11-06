using System.Collections.Generic;

namespace MapClash
{
    public sealed partial class MatchSimulation
    {
        // The compact index avoids allocating a tree node for every advancing frontier cell.
        // Its order follows deterministic ownership mutations; dictionary enumeration is never used.
        private sealed class BorderIndex
        {
            private readonly List<int> _cells = new List<int>();
            private readonly Dictionary<int, int> _positions = new Dictionary<int, int>();

            public void Add(int cell)
            {
                if (_positions.ContainsKey(cell))
                {
                    return;
                }

                _positions.Add(cell, _cells.Count);
                _cells.Add(cell);
            }

            public void Remove(int cell)
            {
                if (!_positions.TryGetValue(cell, out var index))
                {
                    return;
                }

                _positions.Remove(cell);
                var lastIndex = _cells.Count - 1;
                if (index != lastIndex)
                {
                    var lastCell = _cells[lastIndex];
                    _cells[index] = lastCell;
                    _positions[lastCell] = index;
                }

                _cells.RemoveAt(lastIndex);
            }

            public void Clear()
            {
                _cells.Clear();
                _positions.Clear();
            }

            public List<int>.Enumerator GetEnumerator()
            {
                return _cells.GetEnumerator();
            }
        }
    }
}
