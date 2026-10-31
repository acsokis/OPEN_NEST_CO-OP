using System;
using System.Collections.Generic;

namespace OpenNestCoop.GameSync;

// Preserve per-gun order while another gun may continue. Native ignored presses
// are retried only within a bounded window; no unbounded network work queue.
internal sealed class MagazineActionQueue
{
    private sealed class Item { internal string Gun; internal byte Action; internal double Expires; internal Action Applied; }
    private readonly List<Item> _items = new();
    internal int Count => _items.Count;
    internal bool Add(string gun, byte action, double now, Action applied)
    {
        if (_items.Count >= 128) return false;
        _items.Add(new Item { Gun = gun, Action = action, Expires = now + 8, Applied = applied }); return true;
    }
    internal void Tick(double now, Func<string, byte, bool> tryApply, Action<string> expired)
    {
        var blocked = new HashSet<string>();
        for (int index = 0; index < _items.Count;)
        {
            var item = _items[index];
            if (now >= item.Expires) { _items.RemoveAt(index); expired(item.Gun); continue; }
            if (blocked.Contains(item.Gun)) { index++; continue; }
            if (tryApply(item.Gun, item.Action)) { _items.RemoveAt(index); item.Applied?.Invoke(); }
            else { blocked.Add(item.Gun); index++; }
        }
    }
    internal void Reset() => _items.Clear();
    internal static bool AcceptedMove(bool wasRotating, bool isRotating) => !wasRotating && isRotating;
}
