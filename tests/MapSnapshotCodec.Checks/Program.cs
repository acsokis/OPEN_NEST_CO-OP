using System.IO.Compression;
using System.Text;
using OpenNestCoop.GameSync;

int passed = 0;
void Check(bool ok) { if (!ok) throw new Exception("Check failed"); passed++; }
void Reject(Action operation)
{
    try { operation(); }
    catch (InvalidDataException) { passed++; return; }
    throw new Exception("Invalid input accepted");
}
foreach (var text in new[] { "", "{\"MapTokens\":[]}", "irany: 052°; tavolsag: 5,48 km; körző; 猫" })
{
    var bytes = Encoding.UTF8.GetBytes(text);
    Check(MapSnapshotCodec.Decode(MapSnapshotCodec.Encode(bytes)).SequenceEqual(bytes));
}
var large = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{\"Id\":\"token\",\"Active\":true}", 10000)));
Check(MapSnapshotCodec.Decode(MapSnapshotCodec.Encode(large)).SequenceEqual(large));
Reject(() => MapSnapshotCodec.Encode(new byte[MapSnapshotCodec.MaxDecoded + 1]));
Reject(() => MapSnapshotCodec.Decode(new byte[MapSnapshotCodec.MaxCompressed + 1]));
Reject(() => MapSnapshotCodec.Decode(new byte[] { 1, 2, 3, 4, 5, 6 }));
using var output = new MemoryStream();
using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
    gzip.Write(new byte[MapSnapshotCodec.MaxDecoded + 1]);
Reject(() => MapSnapshotCodec.Decode(output.ToArray()));
var random = new byte[MapSnapshotCodec.MaxCompressed + 10000];
new Random(1234).NextBytes(random);
Reject(() => MapSnapshotCodec.Encode(random));
var cards = new CardReplicationState();
Check(cards.ShouldApply("card-a", "position-a"));
cards.Applied("card-a", "position-a");
Check(!cards.ShouldApply("card-a", "position-a")); // identical heartbeat: no native UI restore
Check(cards.ShouldApply("card-a", "position-b")); // actual movement still applies
Check(cards.Delete("card-a"));
Check(!cards.Delete("card-a")); // duplicate deletion is idempotent
Check(!cards.ShouldApply("card-a", "position-a")); // queued pre-close packet
Check(!cards.ShouldApply("card-a", "position-b")); // delayed movement packet
cards.Applied("card-a", "position-c");
Check(!cards.ShouldApply("card-a", "position-c")); // cannot resurrect by recording an upsert
Check(cards.ShouldApply("card-b", "new-card")); // other cards unaffected
Check(cards.Deleted.SequenceEqual(new[] { "card-a" }));
Check(!cards.Delete(""));
cards.Reset();
Check(cards.ShouldApply("card-a", "position-a")); // a new scene/session has independent lifecycle
Check(!cards.Deleted.Any());
Check(!SessionAuthority.AcceptHostState(true, false)); // guest cannot reset host
Check(!SessionAuthority.AcceptHostState(true, true)); // host never applies remote mission state
Check(!SessionAuthority.AcceptHostState(false, false)); // other guests cannot control this guest
Check(SessionAuthority.AcceptHostState(false, true)); // host navigation still replicates
Check(SessionAuthority.LeaveBeforeLocalNavigation(false, true, false));
Check(!SessionAuthority.LeaveBeforeLocalNavigation(false, true, true)); // applying host event keeps session
Check(!SessionAuthority.LeaveBeforeLocalNavigation(true, true, false)); // host stays in charge
Check(!SessionAuthority.LeaveBeforeLocalNavigation(false, false, false)); // single player unchanged
passed += ManagedChecks.Run(args);
Console.WriteLine($"{passed} snapshot, card lifecycle, authority and managed-state checks passed");
