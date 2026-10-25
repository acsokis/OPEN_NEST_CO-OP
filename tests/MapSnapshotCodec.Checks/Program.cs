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
Console.WriteLine($"{passed} map snapshot codec checks passed");
