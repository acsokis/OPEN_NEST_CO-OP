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
passed += DocumentNetworkChecks.Run();
passed += await LocalTransportChecks.Run();
var magazine = new MagazineActionQueue(); var magazineApplied = new List<string>();
Check(!MagazineActionQueue.AcceptedMove(true, true)); // native ignored busy press
Check(!MagazineActionQueue.AcceptedMove(false, false)); // native rejected press
Check(MagazineActionQueue.AcceptedMove(false, true));
Check(magazine.Add("left", 2, 0, () => magazineApplied.Add("L1")));
Check(magazine.Add("left", 2, 0, () => magazineApplied.Add("L2")));
Check(magazine.Add("right", 2, 0, () => magazineApplied.Add("R1")));
magazine.Tick(1, (gun, action) => gun == "right", _ => throw new Exception("Early expiry"));
Check(magazineApplied.SequenceEqual(new[] { "R1" }) && magazine.Count == 2);
int magazineTurns = 0;
magazine.Tick(2, (_, _) => ++magazineTurns == 1, _ => throw new Exception("Early expiry"));
Check(magazineApplied.SequenceEqual(new[] { "R1", "L1" }) && magazine.Count == 1);
magazine.Tick(3, (_, _) => true, _ => throw new Exception("Early expiry"));
Check(magazineApplied.Last() == "L2" && magazine.Count == 0);
int magazineExpired = 0; magazine.Add("left", 2, 0, null);
magazine.Tick(8, (_, _) => throw new Exception("Expired action executed"), _ => magazineExpired++);
Check(magazineExpired == 1 && magazine.Count == 0);
for (int i = 0; i < 128; i++) if (!magazine.Add("left", 2, 0, null)) throw new Exception("Magazine queue rejected within capacity");
Check(!magazine.Add("left", 2, 0, null));
magazine.Reset(); Check(magazine.Count == 0);
var hostOrder = GunKeyOrder.Canonical(new[] { "Turret/GunLeft", "Turret/GunRight" });
var reversedGuestOrder = GunKeyOrder.Canonical(new[] { "Turret/GunRight", "Turret/GunLeft" });
Check(hostOrder.SequenceEqual(reversedGuestOrder));
Check(reversedGuestOrder[1] == "Turret/GunRight");
try { GunKeyOrder.Canonical(new[] { "same", "same" }); throw new Exception("Ambiguous gun identity accepted"); }
catch (InvalidOperationException) { passed++; }
Check(MapPanelCommand.TryParse("map|detent|R2|AP", out var rightLoad) && rightLoad.Side == 1 && rightLoad.Gear == "R2");
Check(MapPanelCommand.TryParse("map|detent|L1|HE", out var leftAssign) && leftAssign.Side == 0 && leftAssign.Gear == "L1");
Check(MapPanelCommand.TryParse("map|acquire|1|AP", out var rightAcquire) && rightAcquire.Side == 1);
Check(MapPanelCommand.TryParse("map|select|HE", out var selection) && selection.Product == "HE");
foreach (string invalid in new[] { "map|acquire|2|AP", "map|acquire|-1|AP", "map|detent|R3|AP", "map|select|AP|extra", "map|detent|R1|", "map|execute|1|AP", "map|acquire|1|x;y" })
    Check(!MapPanelCommand.TryParse(invalid, out _));
var controls = new WorldControlIntent(); string door = new string('A', 64);
Check(!controls.Observe(door, 1, 1, out _)); // joining defaults never overwrite host
controls.Applied(door, 0);
Check(controls.Observe(door, 1, 1, out var opening));
Check(controls.Desired(door, 0, 1) == 1); // exact user regression: old host heartbeat must not close it
Check(controls.Observe(door, 0, 1, out var closing));
controls.Acknowledge(opening, true);
Check(controls.Desired(door, 1, 1) == 0); // old opening ack cannot erase the newer close
controls.Acknowledge(closing, true); controls.Applied(door, 0);
Check(!controls.Observe(door, 0, 1, out _));
controls.Applied(door, 2); // master power bit remains host-owned
Check(controls.Observe(door, 3, 1, out var lightSwitch) && lightSwitch.Value == 1);
Check(controls.Desired(door, 2, 1) == 3);
Check(WorldControlCommand.Parse(lightSwitch.Wire, out var decodedSwitch) && decodedSwitch.Ticket == lightSwitch.Ticket);
Check(!WorldControlCommand.Parse("set|" + door + "|1|-1", out _));
var sound = new ScoutAudio { Key = "root/sfx_deploy_scout", Sequence = 1, Playing = false };
Check(!ScoutStateCodec.PlaySound(true, -1, sound, false)); // late join must not replay an ended historical flyover
Check(ScoutStateCodec.PlaySound(false, 0, sound, false)); // short live one-shot survives between polls
Check(!ScoutStateCodec.PlaySound(false, 1, sound, false)); // duplicate pulse cannot play twice
sound.Playing = true; Check(ScoutStateCodec.PlaySound(true, -1, sound, false));
Console.WriteLine($"{passed} snapshot, card lifecycle, authority and managed-state checks passed");
