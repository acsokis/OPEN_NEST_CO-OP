using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using OpenNestCoop.GameSync;

internal static class ManagedChecks
{
    private sealed record Fixture(Guid Id, double Bearing, List<string> Notes);
    internal static int Run(string[] args)
    {
        int count = 0;
        void Check(bool ok) { if (!ok) throw new Exception("Managed-state check failed"); count++; }
        var codec = new ManagedStateCodec(t => t == typeof(Fixture));
        var fixture = new Fixture(Guid.NewGuid(), double.NaN, new() { "052°", "notes / körző" });
        var copy = (Fixture)codec.Decode(codec.Encode(fixture, typeof(Fixture)), typeof(Fixture));
        Check(copy.Id == fixture.Id && double.IsNaN(copy.Bearing) && copy.Notes.SequenceEqual(fixture.Notes));
        try { codec.Decode("{\"Fields\":{}}", typeof(Fixture)); throw new Exception("Schema accepted"); }
        catch (InvalidOperationException) { count++; }
        try { codec.Encode(new object(), typeof(object)); throw new Exception("Unknown type accepted"); }
        catch (InvalidOperationException) { count++; }
        var store = new SharedDocumentStore();
        Check(store.Get("intel").Revision == 0);
        Check(store.Propose("intel", 0, "first"));
        long revision = store.Get("intel").Revision;
        Check(!store.Propose("intel", 0, "stale"));
        Check(store.Get("intel").Payload == "first");
        Check(store.Set("intel", "first").Revision == revision);
        Check(!store.Receive("intel", revision, "echo"));
        Check(store.Propose("intel", revision, "edit by another player"));
        store.Reset(); Check(store.Get("intel").Revision == 0);
        if (args.Length == 0) return count;
        string game = Path.GetFullPath(args[0]);
        var dirs = new[] { "Mods", "UserLibs", "MelonLoader/net6", "MelonLoader/Il2CppAssemblies" }.Select(p => Path.Combine(game, p)).ToArray();
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (var dir in dirs) { var path = Path.Combine(dir, name.Name + ".dll"); if (File.Exists(path)) return context.LoadFromAssemblyPath(path); }
            return null;
        };
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Mods/IronNestHandheld.dll"));
        codec = new ManagedStateCodec(t => t.Assembly == assembly && t.Namespace == "IronNestBearing");
        foreach (string name in new[] { "PositionAsset", "PositionSolution", "HandheldFireControl", "HandheldAutoQueue", "HandheldMovingTrack" })
        {
            var type = assembly.GetType("IronNestBearing." + name, true);
            var value = Activator.CreateInstance(type);
            string encoded = codec.Encode(value, type);
            Check(codec.Encode(codec.Decode(encoded, type), type) == encoded);
        }
        // Real private record constructors/readonly fields are exercised without
        // starting Unity or including any third-party source in this repository.
        var terminal = assembly.GetType("IronNestBearing.HandheldTerminal", true);
        foreach (string nested in new[] { "MovingCard", "FireEdit", "AutoEdit" })
        {
            var type = terminal.GetNestedType(nested, BindingFlags.NonPublic);
            object value;
            if (nested == "MovingCard") value = Activator.CreateInstance(type, true);
            else
            {
                var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).First(c => c.GetParameters().Length > 1);
                var values = ctor.GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
                value = ctor.Invoke(values);
            }
            string encoded = codec.Encode(value, type);
            Check(codec.Encode(codec.Decode(encoded, type), type) == encoded);
        }
        return count;
    }
}
