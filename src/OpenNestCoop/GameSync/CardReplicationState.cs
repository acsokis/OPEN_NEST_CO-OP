using System;
using System.Collections.Generic;

namespace OpenNestCoop.GameSync;

// Scene/session-scoped deletion wins over all delayed or repeated upserts.
internal sealed class CardReplicationState
{
    private readonly HashSet<string> _deleted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _applied = new(StringComparer.Ordinal);
    internal IEnumerable<string> Deleted => _deleted;
    internal bool IsDeleted(string id) => _deleted.Contains(id);
    internal bool Delete(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        _applied.Remove(id);
        return _deleted.Add(id);
    }
    internal bool ShouldApply(string id, string payload) => !IsDeleted(id)
        && (!_applied.TryGetValue(id, out var previous) || previous != payload);
    internal void Applied(string id, string payload)
    {
        if (!IsDeleted(id)) _applied[id] = payload;
    }
    internal void Reset() { _deleted.Clear(); _applied.Clear(); }
}
