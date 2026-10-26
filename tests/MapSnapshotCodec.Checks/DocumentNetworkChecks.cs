using OpenNestCoop.GameSync;

internal static class DocumentNetworkChecks
{
    private sealed class Envelope
    {
        internal ulong From, To;
        internal byte[] Bytes;
        internal DocumentPacket Packet => DocumentPacketCodec.Decode(Bytes);
    }
    private sealed class Network
    {
        internal readonly Dictionary<ulong, Dictionary<string, string>> Models = new();
        internal readonly Dictionary<ulong, ReliableDocumentSync> Replicas = new();
        internal readonly List<Envelope> Wire = new();
        internal readonly List<string> Warnings = new(), Commands = new();
        internal readonly Dictionary<ulong, int> FailApply = new();
        internal int HostApplies;
        internal static readonly string[] Keys = { "intel", "fire", "notes", "auto.status", "auto.pending" };
        internal Network()
        {
            Add(1, true); Add(2, false); Replicas[1].Peers.Add(2);
        }
        internal void Add(ulong id, bool host)
        {
            Models[id] = Keys.ToDictionary(k => k, k => host ? "host-" + k : "guest-default");
            Replicas[id] = new ReliableDocumentSync(host, 1, Keys, new[] { "auto.status" },
                key => Models[id][key], (key, payload) =>
                {
                    Models[id][key] = payload;
                    if (FailApply.TryGetValue(id, out int remaining) && remaining > 0)
                    { FailApply[id] = remaining - 1; throw new InvalidOperationException("injected failure AFTER model mutation"); }
                    if (id == 1) HostApplies++;
                    if (key == "auto.status") Models[id]["auto.pending"] = "derived:" + payload;
                },
                (to, packet) => Wire.Add(new Envelope { From = id, To = to, Bytes = DocumentPacketCodec.Encode(packet) }),
                command => Commands.Add(command + ":" + Models[id]["fire"]), message => Warnings.Add(id + ":" + message),
                new() { ["auto.status"] = new[] { "auto.pending" } });
        }
        internal Envelope Take(Func<Envelope, bool> predicate)
        {
            int index = Wire.FindIndex(e => predicate(e));
            if (index < 0) throw new Exception("Expected network packet missing");
            var packet = Wire[index]; Wire.RemoveAt(index); return packet;
        }
        internal void Deliver(Envelope envelope) => Replicas[envelope.To].Receive(envelope.From, envelope.Packet);
        internal void Pump()
        {
            int limit = 2000;
            while (Wire.Count > 0)
            { if (--limit == 0) throw new Exception("Network echo loop"); var packet = Wire[0]; Wire.RemoveAt(0); Deliver(packet); }
        }
        internal void Join()
        { Replicas[1].Tick(0); Replicas[2].Tick(0); Pump(); }
    }
    internal static int Run()
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("Document network: " + name); passed++; }
        var n = new Network(); n.Join();
        Check(n.Replicas[2].Ready, "initial snapshot barrier");
        Check(Network.Keys.All(k => n.Models[1][k] == n.Models[2][k]), "initial host values replace defaults");
        Check(n.Replicas[2].Revision("notes") > 0, "all initial documents have revisions");
        // A newer edit made while the first edit is in flight must survive both
        // a broadcast and its delayed ack. The next proposal carries that edit.
        n.Models[2]["intel"] = "first edit"; n.Replicas[2].Tick(1);
        var firstProposal = n.Take(e => e.Packet.Operation == DocumentOperation.Propose);
        n.Models[2]["intel"] = "second edit before acknowledgement";
        n.Deliver(firstProposal);
        n.Deliver(n.Take(e => e.Packet.Operation == DocumentOperation.State && e.Packet.Key == "intel"));
        var firstAck = n.Take(e => e.Packet.Operation == DocumentOperation.Acknowledge);
        n.Deliver(firstAck);
        Check(n.Models[2]["intel"] == "second edit before acknowledgement", "late ack cannot erase newer local edit");
        n.Replicas[2].Tick(2);
        Check(n.Replicas[2].PendingEdits == 1, "queued edit gets a new proposal");
        n.Deliver(firstAck);
        Check(n.Replicas[2].PendingEdits == 1, "duplicate old ack cannot clear a new proposal");
        n.Pump();
        Check(n.Models[1]["intel"] == n.Models[2]["intel"], "queued edit converges");
        n.Replicas[1].Tick(3); n.Replicas[2].Tick(3);
        Check(n.Wire.Count == 0, "no echo after canonical update");
        // Live apply fails after partially mutating native/model data. Do not
        // commit the revision or upload that partial model as a user edit.
        long previous = n.Replicas[2].Revision("notes");
        n.FailApply[2] = 1; n.Models[1]["notes"] = "new host notes"; n.Replicas[1].Tick(4); n.Pump();
        Check(n.Replicas[2].Revision("notes") == previous, "failed application cannot advance revision");
        n.Replicas[2].Tick(5); n.Pump();
        Check(n.Replicas[2].Revision("notes") > previous, "failed live state retries");
        Check(n.Replicas[2].PendingEdits == 0, "partial remote mutation is not a local edit");
        // Snapshot end may arrive first. New host changes arriving during the
        // transfer must survive the older snapshot, including empty documents.
        n = new Network(); n.Models[1]["notes"] = "";
        n.Replicas[2].Tick(0); n.Deliver(n.Take(e => e.Packet.Operation == DocumentOperation.SnapshotRequest));
        n.Deliver(n.Take(e => e.Packet.Operation == DocumentOperation.SnapshotEnd));
        Check(!n.Replicas[2].Ready, "end alone cannot complete missing snapshot documents");
        n.Models[1]["intel"] = "changed during join"; n.Replicas[1].Tick(1);
        n.Deliver(n.Take(e => e.Packet.Operation == DocumentOperation.State && e.Packet.Key == "intel" && e.Packet.Payload == "changed during join"));
        n.Pump();
        Check(n.Replicas[2].Ready && n.Models[2]["intel"] == "changed during join", "buffered live state wins over older snapshot");
        Check(n.Models[2]["notes"] == "" && n.Replicas[2].Revision("notes") > 0, "empty notes have explicit initial state");
        // A failed initial snapshot remains unready and is retried as a whole.
        n = new Network(); n.FailApply[2] = 1; n.Join();
        Check(!n.Replicas[2].Ready, "failed snapshot is not marked complete");
        n.Replicas[2].Tick(6); n.Pump();
        Check(n.Replicas[2].Ready, "snapshot retry recovers after model failure");
        // Data must be acknowledged before automatic control executes. Lost
        // acknowledgements and duplicated commands must not fire twice.
        n = new Network(); n.Join(); n.Models[2]["fire"] = "new fire plan";
        n.Replicas[2].QueueCommand("fire");
        Check(n.Commands.Count == 0 && n.Wire.All(e => e.Packet.Operation != DocumentOperation.Command), "control waits for plan acknowledgement");
        n.Pump(); n.Replicas[2].Tick(1);
        var commandPacket = n.Take(e => e.Packet.Operation == DocumentOperation.Command); n.Deliver(commandPacket);
        var lostAck = n.Take(e => e.Packet.Operation == DocumentOperation.CommandAck);
        Check(n.Commands.SequenceEqual(new[] { "fire:new fire plan" }), "host executes accepted plan once");
        n.Deliver(commandPacket); n.Pump(); n.Replicas[2].Tick(5); n.Pump();
        Check(n.Commands.Count == 1, "duplicate command does not repeat physical effect");
        n.Deliver(lostAck); Check(n.Commands.Count == 1, "duplicate command acknowledgement is harmless");
        // Reconnect with the same player id restarts local request numbers but
        // has a new client incarnation. Old requests/replies cannot be reused.
        var oldRequest = firstProposal;
        n = new Network(); n.Join(); n.Models[2]["intel"] = "before reconnect"; n.Replicas[2].Tick(1);
        oldRequest = n.Take(e => e.Packet.Operation == DocumentOperation.Propose); n.Deliver(oldRequest); n.Pump();
        var hostBefore = new Dictionary<string, string>(n.Models[1]);
        n.Add(2, false); n.Replicas[2].Tick(0); n.Pump();
        Check(n.Models[1].All(p => p.Value == hostBefore[p.Key]), "rejoin leaves host models intact");
        n.Models[2]["intel"] = "after reconnect"; n.Replicas[2].Tick(1); n.Pump();
        n.Deliver(oldRequest); n.Pump();
        Check(n.Models[1]["intel"] == "after reconnect", "old client proposal cannot overwrite rejoined state");
        // A stale conflicting document is rejected explicitly; queued firing is
        // cancelled instead of firing an obsolete or unaccepted plan.
        n = new Network(); n.Join(); n.Models[2]["fire"] = "guest stale plan"; n.Replicas[2].QueueCommand("fire");
        n.Models[1]["fire"] = "host concurrent plan"; n.Replicas[1].Tick(1); n.Pump(); n.Replicas[2].Tick(2); n.Pump();
        Check(n.Commands.Count == 0, "rejected plan cancels queued control");
        Check(n.Models[2]["fire"] == "host concurrent plan", "conflicting document returns host state");
        Check(n.Warnings.Any(w => w.Contains("Concurrent edit rejected")), "conflict is reported");
        // Model status changes can alter another editable model. Its pending
        // local edit must be overlaid instead of silently disappearing.
        n = new Network(); n.Join(); n.Models[2]["auto.pending"] = "new queued job"; n.Replicas[2].Tick(1);
        n.Models[1]["auto.status"] = "running"; n.Replicas[1].Tick(2);
        n.Deliver(n.Take(e => e.Packet.Operation == DocumentOperation.State && e.Packet.Key == "auto.status"));
        Check(n.Models[2]["auto.pending"] == "new queued job", "operational snapshot preserves pending local draft");
        n.Pump(); Check(n.Models[1]["auto.pending"] == "new queued job", "preserved draft reaches host");
        // Recover a final lost state even when no further user changes occur.
        n = new Network(); n.Join(); n.Models[1]["notes"] = "last update was lost"; n.Replicas[1].Tick(1);
        n.Wire.Clear(); n.Replicas[1].Tick(12); n.Pump();
        Check(n.Models[2]["notes"] == "last update was lost", "anti-entropy repairs a lost final update");
        // Several isolated peers, with seeded loss/duplicates/reordering. These
        // tests serialize/compress every production protocol packet; the models
        // are in-memory fixtures, not a claim about Unity/Steam gameplay.
        for (int seed = 0; seed < 12; seed++)
        {
            n = new Network(); n.Add(3, false); n.Replicas[1].Peers.Add(3);
            var random = new Random(seed);
            for (int frame = 0; frame < 1000; frame++)
            {
                double now = frame * .25;
                for (ulong id = 1; id <= 3; id++)
                {
                    if (frame > 20 && frame < 280 && n.Replicas[id].Ready && random.Next(12) == 0)
                    {
                        string key = Network.Keys[random.Next(3)];
                        n.Models[id][key] = "seed=" + seed + "/peer=" + id + "/edit=" + frame;
                    }
                    n.Replicas[id].Tick(now);
                }
                for (int packet = 0; packet < 20 && n.Wire.Count > 0; packet++)
                {
                    int index = random.Next(n.Wire.Count); var message = n.Wire[index]; n.Wire.RemoveAt(index);
                    if (random.Next(7) == 0) continue;
                    n.Deliver(message);
                    if (random.Next(5) == 0) n.Deliver(message);
                }
            }
            n.Pump();
            Check(n.Replicas[2].Ready && n.Replicas[3].Ready
                && n.Replicas[2].PendingEdits == 0 && n.Replicas[3].PendingEdits == 0
                && Network.Keys.All(key => n.Models[1][key] == n.Models[2][key] && n.Models[1][key] == n.Models[3][key]),
                "three-peer convergence under seeded packet faults " + seed);
        }
        return passed;
    }
}
