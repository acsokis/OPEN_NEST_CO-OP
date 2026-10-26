using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenNestCoop.GameSync;
using OpenNestCoop.Net;

internal static class LocalTransportChecks
{
    internal static async Task<int> Run()
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("Loopback: " + name); passed++; }
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using var host = new LocalTransport();
        int accepted = 0; host.ClientConnected += () => Interlocked.Increment(ref accepted);
        Check(host.StartHost(port), "host starts on loopback only");
        async Task Wait(Func<bool> done, Action pump = null)
        {
            var deadline = Stopwatch.StartNew();
            while (!done()) { pump?.Invoke(); if (deadline.Elapsed > TimeSpan.FromSeconds(5)) throw new Exception("Loopback timeout"); await Task.Delay(2); }
        }
        var guest = new LocalTransport();
        try
        {
            Check(guest.Connect(port), "guest connects"); await Wait(() => Volatile.Read(ref accepted) == 1);
            var models = new[] { new Dictionary<string, string> { ["intel"] = "host measurement", ["fire"] = "host plan" },
                new Dictionary<string, string> { ["intel"] = "default", ["fire"] = "default" } };
            int fired = 0;
            var warnings = new List<string>();
            var keys = new[] { "intel", "fire" };
            var server = new ReliableDocumentSync(true, 1, keys, Array.Empty<string>(), k => models[0][k],
                (k, p) => models[0][k] = p, (to, p) => host.Send(to, DocumentPacketCodec.Encode(p), true),
                _ => fired++, warnings.Add);
            server.Peers.Add(2);
            ReliableDocumentSync NewClient() => new(false, 1, keys, Array.Empty<string>(), k => models[1][k],
                (k, p) => models[1][k] = p, (to, p) =>
                {
                    byte[] bytes = DocumentPacketCodec.Encode(p); guest.Send(to, bytes, true);
                    if (p.Operation == DocumentOperation.Command) guest.Send(to, bytes, true);
                }, _ => throw new Exception("Guest executed a physical command"), warnings.Add);
            var client = NewClient(); var elapsed = Stopwatch.StartNew();
            void Pump()
            {
                server.Tick(elapsed.Elapsed.TotalSeconds); client.Tick(elapsed.Elapsed.TotalSeconds);
                while (host.Poll(out ulong from, out byte[] bytes)) server.Receive(from, DocumentPacketCodec.Decode(bytes));
                while (guest.Poll(out ulong from, out byte[] bytes)) client.Receive(from, DocumentPacketCodec.Decode(bytes));
            }
            await Wait(() => client.Ready, Pump);
            Check(models[0].All(p => models[1][p.Key] == p.Value), "real TCP initial model transfer");
            models[1]["intel"] = "first"; client.Tick(elapsed.Elapsed.TotalSeconds);
            models[1]["intel"] = "second before ack";
            await Wait(() => server.Revision("intel") == client.Revision("intel") && client.PendingEdits == 0
                && models[0]["intel"] == "second before ack", Pump);
            Check(models[1]["intel"] == models[0]["intel"], "inflight edits converge over real TCP");
            models[1]["fire"] = "plan from guest"; client.QueueCommand("fire");
            await Wait(() => fired == 1, Pump);
            for (int i = 0; i < 20; i++) { Pump(); await Task.Delay(2); }
            Check(fired == 1 && models[0]["fire"] == "plan from guest", "duplicated TCP control runs once after plan sync");
            string before = models[0]["intel"];
            guest.Dispose(); guest = new LocalTransport();
            Check(guest.Connect(port), "same peer reconnects"); await Wait(() => Volatile.Read(ref accepted) == 2);
            models[1]["intel"] = "new instance defaults"; client = NewClient();
            await Wait(() => client.Ready, Pump);
            Check(models[0]["intel"] == before && models[1]["intel"] == before, "TCP rejoin preserves host state");
            // Replace still-connected clients. A retired reader must never read
            // the new socket or deliver an old queued packet as a new client's.
            for (int cycle = 0; cycle < 6; cycle++)
            {
                var old = guest;
                for (int i = 0; i < 20; i++) old.Send(1, Encoding.UTF8.GetBytes("retired generation"), true);
                await Task.Delay(20);
                guest = new LocalTransport(); Check(guest.Connect(port), "replacement connection " + cycle);
                int expected = cycle + 3; await Wait(() => Volatile.Read(ref accepted) == expected);
                string marker = "current generation " + cycle;
                guest.Send(1, Encoding.UTF8.GetBytes(marker), true);
                bool received = false;
                await Wait(() => received, () =>
                {
                    while (host.Poll(out _, out byte[] bytes))
                    {
                        if (Encoding.UTF8.GetString(bytes) != marker) throw new Exception("Retired TCP generation leaked into current connection");
                        received = true;
                    }
                });
                Check(received, "only current-generation data delivered " + cycle); old.Dispose();
            }
        }
        finally { guest.Dispose(); }
        return passed;
    }
}
