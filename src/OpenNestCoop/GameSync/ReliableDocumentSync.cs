using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenNestCoop.GameSync;

internal enum DocumentOperation { SnapshotRequest, SnapshotState, SnapshotEnd, State, Propose, Acknowledge, Command, CommandAck }
internal sealed class DocumentPacket
{
    public DocumentOperation Operation;
    public string Epoch = "", Client = "", Key = "", Payload = "";
    public long Id, Revision;
    public bool Accepted;
}

internal static class DocumentPacketCodec
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, MaxDepth = 8 };
    internal static byte[] Encode(DocumentPacket packet) => MapSnapshotCodec.Encode(JsonSerializer.SerializeToUtf8Bytes(packet, Json));
    internal static DocumentPacket Decode(byte[] bytes)
    {
        var packet = JsonSerializer.Deserialize<DocumentPacket>(MapSnapshotCodec.Decode(bytes), Json);
        if (packet == null || !Enum.IsDefined(typeof(DocumentOperation), packet.Operation) || packet.Id < 0 || packet.Revision < 0
            || packet.Epoch == null || packet.Epoch.Length > 64 || packet.Client == null || packet.Client.Length > 64
            || packet.Key == null || packet.Key.Length > 128
            || packet.Payload == null || packet.Payload.Length > 2000000)
            throw new InvalidOperationException("Invalid document packet");
        return packet;
    }
}

// This exact coordinator runs in the game and in the single-machine fault tests.
// It has no Unity, Steam or static role state, so host and guests are isolated.
internal sealed class ReliableDocumentSync
{
    private sealed class Pending
    {
        internal DocumentPacket Packet;
        internal string Latest, Observed;
        internal double RetryAt;
    }
    private readonly bool _host;
    private readonly string[] _keys;
    private readonly HashSet<string> _hostOnly;
    private readonly Func<string, string> _read;
    private readonly Action<string, string> _apply;
    private readonly Action<ulong, DocumentPacket> _send;
    private readonly Action<string> _command, _warn;
    private readonly Dictionary<string, string[]> _dependencies;
    private readonly SharedDocumentStore _confirmed = new();
    private readonly Dictionary<string, string> _baseline = new();
    private readonly Dictionary<string, Pending> _pending = new();
    private readonly Dictionary<string, DocumentPacket> _deferred = new(), _snapshot = new();
    private readonly Dictionary<string, DocumentPacket> _failed = new();
    private readonly Dictionary<ulong, Dictionary<long, DocumentPacket>> _replies = new();
    private readonly Dictionary<ulong, string> _clients = new();
    private readonly Dictionary<ulong, List<DocumentPacket>> _snapshots = new();
    private readonly HashSet<string> _retiredClients = new();
    private readonly string _client = Guid.NewGuid().ToString("N");
    private readonly Queue<string> _commands = new();
    private DocumentPacket _pendingCommand;
    private long _sequence, _snapshotId;
    private string _epoch = "", _snapshotEpoch = "";
    private bool _snapshotEnded;
    private double _now, _snapshotRetry, _commandRetry, _heartbeat;
    internal readonly HashSet<ulong> Peers = new();
    internal bool Ready { get; private set; }
    internal event Action<string, bool> CommandCompleted;
    internal int PendingEdits => _pending.Count;
    internal long Revision(string key) => _confirmed.Get(key).Revision;
    internal ReliableDocumentSync(bool host, ulong hostId, string[] keys, IEnumerable<string> hostOnly,
        Func<string, string> read, Action<string, string> apply, Action<ulong, DocumentPacket> send,
        Action<string> command, Action<string> warn, Dictionary<string, string[]> dependencies = null)
    {
        _host = host; HostId = hostId; _keys = keys; _hostOnly = new HashSet<string>(hostOnly, StringComparer.Ordinal);
        _read = read; _apply = apply; _send = send; _command = command; _warn = warn;
        _dependencies = dependencies ?? new();
        if (host) { _epoch = Guid.NewGuid().ToString("N"); Ready = true; }
    }
    internal ulong HostId { get; }
    internal void Invalidate()
    {
        if (_host) return;
        Ready = false; _snapshotRetry = 0; _pending.Clear(); _commands.Clear(); _pendingCommand = null;
        _failed.Clear(); _deferred.Clear(); _snapshot.Clear(); _snapshotId = 0; _snapshotEpoch = ""; _snapshotEnded = false;
    }
    private void Send(ulong peer, DocumentPacket packet)
    {
        packet.Client = _host ? (_clients.TryGetValue(peer, out string token) ? token : "") : _client;
        _send(peer, packet);
    }
    private Dictionary<string, string> Capture()
    {
        var capture = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in _keys) capture[key] = _read(key);
        return capture;
    }
    private DocumentPacket State(string key, SharedDocumentStore.Entry entry) => new()
    { Operation = DocumentOperation.State, Epoch = _epoch, Key = key, Revision = entry.Revision, Payload = entry.Payload };
    private void PollHost()
    {
        // Capture every model first: a failed capture cannot mark a partial
        // document set as sent or prevent the unchanged value from retrying.
        var capture = Capture();
        foreach (var key in _keys)
        {
            long before = _confirmed.Get(key).Revision;
            var entry = _confirmed.Set(key, capture[key]);
            if (entry.Revision == before) continue;
            foreach (ulong peer in Peers) if (_clients.ContainsKey(peer)) Send(peer, State(key, entry));
        }
    }
    private void PollGuest(IEnumerable<string> keys = null)
    {
        var capture = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys ?? _keys)
            if (!_hostOnly.Contains(key) && !_failed.ContainsKey(key)) capture[key] = _read(key);
        foreach (var pair in capture)
        {
            string key = pair.Key, current = pair.Value;
            if (_pending.TryGetValue(key, out var pending))
            {
                if (pending.Observed != current) { pending.Latest = current; pending.Observed = current; }
                continue;
            }
            if (_baseline.TryGetValue(key, out var previous) && previous == current) continue;
            var packet = new DocumentPacket { Operation = DocumentOperation.Propose, Epoch = _epoch,
                Id = ++_sequence, Key = key, Revision = _confirmed.Get(key).Revision, Payload = current };
            _pending[key] = new Pending { Packet = packet, Latest = current, Observed = current, RetryAt = _now + 3 };
            Send(HostId, packet);
        }
    }
    internal void Tick(double now)
    {
        _now = now;
        if (_host)
        {
            PollHost();
            if (now >= _heartbeat)
            {
                _heartbeat = now + 10;
                foreach (ulong peer in Peers)
                    if (_clients.ContainsKey(peer))
                        foreach (string key in _keys) Send(peer, State(key, _confirmed.Get(key)));
            }
            return;
        }
        if (!Ready)
        {
            if (now < _snapshotRetry) return;
            _snapshotId = ++_sequence; _snapshot.Clear(); _deferred.Clear(); _snapshotEnded = false; _snapshotEpoch = "";
            _snapshotRetry = now + 5;
            Send(HostId, new DocumentPacket { Operation = DocumentOperation.SnapshotRequest, Id = _snapshotId });
            return;
        }
        PollGuest();
        foreach (var pair in _failed.ToArray()) Receive(HostId, pair.Value);
        foreach (var pending in _pending.Values)
            if (now >= pending.RetryAt) { pending.RetryAt = now + 3; Send(HostId, pending.Packet); }
        if (_pendingCommand != null)
        {
            if (now >= _commandRetry) { _commandRetry = now + 3; Send(HostId, _pendingCommand); }
        }
        else if (_pending.Count == 0 && _commands.Count != 0)
        {
            _pendingCommand = new DocumentPacket { Operation = DocumentOperation.Command, Epoch = _epoch,
                Id = ++_sequence, Key = _commands.Dequeue() };
            _commandRetry = now + 3; Send(HostId, _pendingCommand);
        }
    }
    internal void QueueCommand(string command)
    {
        if (_host) { _command(command); PollHost(); return; }
        if (!Ready) { _warn("Control ignored until the initial model snapshot is complete"); return; }
        if (_commands.Count >= 32) { _warn("Too many queued controls"); return; }
        _commands.Enqueue(command); Tick(_now);
    }
    private void Remember(ulong from, long id, DocumentPacket reply)
    {
        if (!_replies.TryGetValue(from, out var replies)) _replies[from] = replies = new();
        // Do not evict a command's deduplication record and later execute it
        // again. A bounded session asks for reconnect instead of replaying it.
        if (replies.Count >= 8192) throw new InvalidOperationException("Document request history full; reconnect required");
        replies.Add(id, reply);
    }
    internal void Receive(ulong from, DocumentPacket packet)
    {
        try { ReceiveCore(from, packet); }
        catch (Exception ex)
        {
            if (!_host && packet.Operation == DocumentOperation.State && _keys.Contains(packet.Key)
                && (!_failed.TryGetValue(packet.Key, out var previous) || packet.Revision >= previous.Revision)) _failed[packet.Key] = packet;
            _warn("Document processing failed (revision not acknowledged): " + ex.Message);
        }
    }
    private void ReceiveCore(ulong from, DocumentPacket packet)
    {
        if (_host) { ReceiveHost(from, packet); return; }
        if (from != HostId || packet.Client != _client) return;
        if (packet.Operation == DocumentOperation.SnapshotState || packet.Operation == DocumentOperation.SnapshotEnd)
        {
            if (Ready || packet.Id != _snapshotId || _snapshotId == 0 || packet.Epoch.Length == 0) return;
            if (_snapshotEpoch.Length != 0 && packet.Epoch != _snapshotEpoch) return;
            _snapshotEpoch = packet.Epoch;
            if (packet.Operation == DocumentOperation.SnapshotState && _keys.Contains(packet.Key)) _snapshot[packet.Key] = packet;
            if (packet.Operation == DocumentOperation.SnapshotEnd) _snapshotEnded = true;
            CompleteSnapshot(); return;
        }
        if (!Ready)
        {
            if (packet.Operation == DocumentOperation.State && _keys.Contains(packet.Key)) Defer(packet);
            return;
        }
        if (packet.Epoch != _epoch) return;
        if (packet.Operation == DocumentOperation.CommandAck)
        {
            if (_pendingCommand?.Id != packet.Id) return;
            string command = _pendingCommand.Key;
            _pendingCommand = null;
            if (!packet.Accepted) { _commands.Clear(); _warn("Host rejected control"); }
            CommandCompleted?.Invoke(command, packet.Accepted);
            return;
        }
        if (!_keys.Contains(packet.Key)) return;
        // Only models affected by this packet need observing. Re-serializing
        // every F1-F3 model on every incoming document causes frame stalls.
        PollGuest(_dependencies.TryGetValue(packet.Key, out var affected) ? affected.Concat(new[] { packet.Key }) : new[] { packet.Key });
        if (packet.Operation == DocumentOperation.State)
        {
            if (packet.Revision <= _confirmed.Get(packet.Key).Revision) { _failed.Remove(packet.Key); return; }
            if (_pending.ContainsKey(packet.Key)) { Defer(packet); _failed.Remove(packet.Key); return; }
            ApplyConfirmed(packet); _failed.Remove(packet.Key); return;
        }
        if (packet.Operation != DocumentOperation.Acknowledge || !_pending.TryGetValue(packet.Key, out var pending)
            || pending.Packet.Id != packet.Id) return;
        var canonical = packet;
        if (_deferred.TryGetValue(packet.Key, out var newer) && newer.Revision > canonical.Revision) canonical = newer;
        bool queued = pending.Latest != pending.Packet.Payload;
        bool preserveQueued = packet.Accepted && queued && canonical.Revision == packet.Revision;
        if (preserveQueued)
        {
            // The ack confirms the SENT edit, not the newer local edit. Leave
            // the latter visible and propose it against the acknowledged base.
            _confirmed.Receive(packet.Key, packet.Revision, packet.Payload);
            _baseline[packet.Key] = packet.Payload;
        }
        else ApplyConfirmed(canonical); // throws -> pending ack remains retryable; no revision is committed.
        _pending.Remove(packet.Key); _deferred.Remove(packet.Key);
        if (!packet.Accepted || (queued && !preserveQueued))
        {
            _commands.Clear(); _warn("Concurrent edit rejected for '" + packet.Key + "'; host state restored");
        }
    }
    private void Defer(DocumentPacket packet)
    {
        if (!_deferred.TryGetValue(packet.Key, out var previous) || packet.Revision > previous.Revision) _deferred[packet.Key] = packet;
    }
    private void CompleteSnapshot()
    {
        if (!_snapshotEnded || _snapshot.Count != _keys.Length) return;
        foreach (string key in _keys) _apply(key, _snapshot[key].Payload);
        // Record revisions only after ALL model applications and final captures
        // succeed. An exception keeps initialization incomplete and retryable.
        var capture = Capture();
        _confirmed.Reset(); _baseline.Clear();
        foreach (string key in _keys)
        { var packet = _snapshot[key]; _confirmed.Receive(key, packet.Revision, packet.Payload); _baseline[key] = capture[key]; }
        _epoch = _snapshotEpoch;
        foreach (var packet in _deferred.Values.ToArray())
            if (packet.Epoch == _epoch && packet.Revision > _confirmed.Get(packet.Key).Revision) ApplyConfirmed(packet);
        Ready = true; _snapshot.Clear(); _deferred.Clear();
    }
    private void ApplyConfirmed(DocumentPacket packet)
    {
        var overlays = new Dictionary<string, string>();
        if (_dependencies.TryGetValue(packet.Key, out var dependencies))
            foreach (string dependency in dependencies)
                if (_pending.TryGetValue(dependency, out var pending)) overlays[dependency] = pending.Latest;
        _apply(packet.Key, packet.Payload);
        foreach (var overlay in overlays) _apply(overlay.Key, overlay.Value);
        string capture = _read(packet.Key);
        var related = new Dictionary<string, string>();
        if (dependencies != null)
            foreach (string dependency in dependencies)
                if (!_pending.ContainsKey(dependency)) related[dependency] = _read(dependency);
        _confirmed.Receive(packet.Key, packet.Revision, packet.Payload);
        _baseline[packet.Key] = capture;
        foreach (var pair in related) _baseline[pair.Key] = pair.Value;
    }
    private void ReceiveHost(ulong from, DocumentPacket packet)
    {
        if (!Peers.Contains(from)) return;
        if (packet.Operation == DocumentOperation.SnapshotRequest)
        {
            if (packet.Client.Length == 0) return;
            if (_clients.TryGetValue(from, out string previous) && previous != packet.Client)
            {
                if (_retiredClients.Contains(from + ":" + packet.Client)) return;
                _retiredClients.Add(from + ":" + previous); _replies.Remove(from); _snapshots.Remove(from);
            }
            _clients[from] = packet.Client;
            if (_snapshots.TryGetValue(from, out var cachedSnapshot))
            {
                if (packet.Id < cachedSnapshot[0].Id) return;
                if (packet.Id == cachedSnapshot[0].Id) { foreach (var part in cachedSnapshot) Send(from, part); return; }
            }
            PollHost();
            var snapshot = new List<DocumentPacket>();
            foreach (string key in _keys)
            {
                var state = State(key, _confirmed.Get(key)); state.Operation = DocumentOperation.SnapshotState; state.Id = packet.Id;
                snapshot.Add(state);
            }
            snapshot.Add(new DocumentPacket { Operation = DocumentOperation.SnapshotEnd, Epoch = _epoch, Id = packet.Id });
            _snapshots[from] = snapshot;
            foreach (var part in snapshot) Send(from, part);
            return;
        }
        if (packet.Epoch != _epoch || packet.Id <= 0 || !_clients.TryGetValue(from, out string client) || client != packet.Client) return;
        if (packet.Operation != DocumentOperation.Propose && packet.Operation != DocumentOperation.Command) return;
        if (_replies.TryGetValue(from, out var replies) && replies.TryGetValue(packet.Id, out var cached)) { Send(from, cached); return; }
        if (_replies.TryGetValue(from, out replies) && replies.Count >= 8192) throw new InvalidOperationException("Reconnect required");
        PollHost();
        if (packet.Operation == DocumentOperation.Command)
        {
            // Reserve the id BEFORE invoking a physical action. Even a partially
            // failed command must not fire/purchase again on a retransmission.
            var reply = new DocumentPacket { Operation = DocumentOperation.CommandAck, Epoch = _epoch, Id = packet.Id };
            Remember(from, packet.Id, reply);
            try { _command(packet.Key); reply.Accepted = true; }
            catch (Exception ex) { _warn("Control failed: " + ex.Message); }
            PollHost(); Send(from, reply); return;
        }
        if (!_keys.Contains(packet.Key) || _hostOnly.Contains(packet.Key)) return;
        var initial = _confirmed.Get(packet.Key);
        var ack = State(packet.Key, initial); ack.Operation = DocumentOperation.Acknowledge; ack.Id = packet.Id;
        Remember(from, packet.Id, ack);
        bool accepted = false;
        if (_confirmed.Get(packet.Key).Revision == packet.Revision)
        {
            try { _apply(packet.Key, packet.Payload); accepted = true; }
            catch (Exception ex) { _warn("Model apply failed: " + ex.Message); }
            PollHost();
        }
        var entry = _confirmed.Get(packet.Key);
        ack.Revision = entry.Revision; ack.Payload = entry.Payload; ack.Accepted = accepted;
        Send(from, ack);
    }
}
