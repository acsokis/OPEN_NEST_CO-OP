using System;
using System.Collections.Generic;

namespace OpenNestCoop.GameSync;

internal sealed class SharedDocumentStore
{
    internal sealed class Entry
    {
        internal readonly long Revision;
        internal readonly string Payload;
        internal Entry(long revision, string payload) { Revision = revision; Payload = payload; }
    }
    private long _clock;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    internal IEnumerable<KeyValuePair<string, Entry>> Entries => _entries;
    internal Entry Get(string key) => _entries.TryGetValue(key, out var entry) ? entry : new Entry(0, "");
    internal Entry Set(string key, string payload)
    {
        var previous = Get(key);
        if (previous.Payload == payload) return previous;
        return _entries[key] = new Entry(++_clock, payload);
    }
    internal bool Propose(string key, long expected, string payload)
    {
        if (Get(key).Revision != expected) return false;
        Set(key, payload); return true;
    }
    internal bool Receive(string key, long revision, string payload)
    {
        if (revision <= Get(key).Revision) return false;
        _clock = Math.Max(_clock, revision);
        _entries[key] = new Entry(revision, payload); return true;
    }
    internal void Reset() { _entries.Clear(); _clock = 0; }
}
