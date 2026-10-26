using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenNestCoop.GameSync;

internal sealed class WorldControlCommand
{
    internal string Key;
    internal byte Value;
    internal long Ticket;
    internal string Wire => "set|" + Key + "|" + Value + "|" + Ticket.ToString(CultureInfo.InvariantCulture);
    internal static bool Parse(string wire, out WorldControlCommand command)
    {
        command = null;
        if (wire == null || wire.Length > 128) return false;
        var parts = wire.Split('|');
        if (parts.Length != 4 || parts[0] != "set" || parts[1].Length != 64 || !byte.TryParse(parts[2], out byte value)
            || value > 3 || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out long ticket) || ticket <= 0) return false;
        foreach (char c in parts[1]) if (!(c >= '0' && c <= '9') && !(c >= 'A' && c <= 'F')) return false;
        command = new WorldControlCommand { Key = parts[1], Value = value, Ticket = ticket }; return true;
    }
}
internal sealed class WorldControlIntent
{
    private readonly Dictionary<string, byte> _baseline = new();
    private readonly Dictionary<string, WorldControlCommand> _pending = new();
    private long _sequence;
    internal void Applied(string key, byte value) => _baseline[key] = value;
    internal bool Observe(string key, byte current, byte mask, out WorldControlCommand command)
    {
        command = null;
        if (!_baseline.TryGetValue(key, out byte baseline)) return false;
        byte expected = _pending.TryGetValue(key, out var pending) ? pending.Value : baseline;
        if ((current & mask) == (expected & mask)) return false;
        command = new WorldControlCommand { Key = key, Value = (byte)(current & mask), Ticket = ++_sequence };
        _pending[key] = command; return true;
    }
    internal byte Desired(string key, byte authoritative, byte mask) => _pending.TryGetValue(key, out var pending)
        ? (byte)((authoritative & ~mask) | (pending.Value & mask)) : authoritative;
    internal void Acknowledge(WorldControlCommand command, bool accepted)
    {
        if (!accepted) { _pending.Clear(); return; }
        if (_pending.TryGetValue(command.Key, out var pending) && pending.Ticket == command.Ticket) _pending.Remove(command.Key);
    }
}
