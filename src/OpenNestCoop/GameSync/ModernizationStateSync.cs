using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using LiteNetLib.Utils;
using OpenNestCoop.Core;
using OpenNestCoop.Net;

namespace OpenNestCoop.GameSync;

// Optional interoperability with the installed Modernization assembly.
// Only known data models are reflected; packets never name CLR types or methods.
public sealed class ModernizationStateSync : ISyncedModule
{
    private const string Channel = "modernization.documents.v1";
    public int MsgType => CoopRuntime.Net?.ChannelType(Channel) ?? 0;
    private static ModernizationStateSync _instance;
    private static readonly string[] Keys = { "auto.access", "notes", "intel", "radar.status", "radar.selection", "fire", "auto.pending", "auto.status" };
    private readonly SharedDocumentStore _store = new();
    private readonly Dictionary<string, string> _baseline = new();
    private readonly HashSet<string> _inflight = new();
    private object _terminal;
    private Assembly _assembly;
    private ManagedStateCodec _codec;
    private string _fingerprint = "", _scene = "";
    private float _timer, _retry;
    private bool _ready, _applying, _warned, _patched;
    private bool _hostUnlocked;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => CoopSyncRegistry.PendingRegister(false, () => new ModernizationStateSync(), Channel);
    private static FieldInfo Field(object obj, string name) => obj.GetType().GetField(name, Flags)
        ?? throw new MissingFieldException(obj.GetType().FullName, name);
    private static object Get(object obj, string name) => Field(obj, name).GetValue(obj);
    private static void Set(object obj, string name, object value) => Field(obj, name).SetValue(obj, value);
    private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethods(Flags)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(obj, args);
    private string Encode(object value, Type type) => _codec.Encode(value, type);
    private object Decode(string value, Type type) => _codec.Decode(value, type);
    private static string Scene => MissionManager.Instance?.CurrentMissionSceneName ?? "";
    private static bool Guest => CoopRuntime.Net?.State == SessionState.Joined && !CoopRuntime.Net.IsHost;
    private static bool AllowExecution() => !Guest;
    private static bool ToggleAutoPrefix()
    {
        if (!Guest) return true;
        _instance?.SendCommand("toggle-auto"); return false;
    }
    private static bool ToggleRadarPrefix()
    {
        if (!Guest) return true;
        _instance?.SendCommand("toggle-radar"); return false;
    }
    private static bool HostOnlyKey(string key) => key == "auto.status" || key == "radar.status" || key == "auto.access";
    private static bool AutoAccessPrefix(ref bool __result)
    {
        if (!Guest || _instance == null || !_instance._ready) return true;
        __result = _instance._hostUnlocked; return false;
    }
    private static bool SetAutoFirePrefix(bool __0)
    {
        if (!Guest) return true;
        _instance?.SendCommand(__0 ? "auto-fire-on" : "auto-fire-off"); return false;
    }
    private bool Bind()
    {
        _instance = this;
        _assembly ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "IronNestHandheld");
        if (_assembly == null) return false;
        var melon = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "MelonLoader");
        var registered = melon?.GetType("MelonLoader.MelonBase")?.GetProperty("RegisteredMelons", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IEnumerable;
        if (registered == null) return false;
        object terminal = null;
        foreach (var mod in registered)
            if (mod.GetType().FullName == "IronNestBearing.HandheldMod") terminal = Get(mod, "terminal");
        if (terminal == null) return false;
        if (!ReferenceEquals(terminal, _terminal))
        {
            _terminal = terminal; _ready = false;
            // Recreating host UI must not restart authoritative revisions while
            // guests still hold documents from this same mission.
            if (Guest) _store.Reset();
            _baseline.Clear(); _inflight.Clear(); _retry = 0;
        }
        _fingerprint = _assembly.ManifestModule.ModuleVersionId.ToString("N");
        _codec ??= new ManagedStateCodec(t => t.Assembly == _assembly && t.Namespace == "IronNestBearing");
        if (!_patched)
        {
            var harmony = new HarmonyLib.Harmony("opennest.modernization.documents");
            foreach (var pair in new[] { ("IronNestBearing.HandheldAutoQueue", "Tick"), ("IronNestBearing.HandheldAutoPort", "TickControls"), ("IronNestBearing.HandheldRadar", "Tick") })
            {
                var method = AccessTools.Method(_assembly.GetType(pair.Item1, true), pair.Item2)
                    ?? throw new MissingMethodException(pair.Item1, pair.Item2);
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(ModernizationStateSync), nameof(AllowExecution)));
            }
            harmony.Patch(AccessTools.Method(terminal.GetType(), "ToggleAutoFire"),
                prefix: new HarmonyMethod(typeof(ModernizationStateSync), nameof(ToggleAutoPrefix)));
            harmony.Patch(AccessTools.Method(terminal.GetType(), "ToggleRadar"),
                prefix: new HarmonyMethod(typeof(ModernizationStateSync), nameof(ToggleRadarPrefix)));
            harmony.Patch(AccessTools.Method(_assembly.GetType("IronNestBearing.HandheldAutoQueue", true), "SetAutoFire"),
                prefix: new HarmonyMethod(typeof(ModernizationStateSync), nameof(SetAutoFirePrefix)));
            harmony.Patch(terminal.GetType().GetProperty("AutoUnlocked", Flags).GetMethod,
                prefix: new HarmonyMethod(typeof(ModernizationStateSync), nameof(AutoAccessPrefix)));
            _patched = true;
            CoopRuntime.LogSource?.LogInfo("[ModernizationSync] model bridge ready MVID=" + _fingerprint);
        }
        return true;
    }
    private string Capture(string key)
    {
        if (key == "auto.access") return Guest ? (_hostUnlocked ? "1" : "0")
            : ((bool)_terminal.GetType().GetProperty("AutoUnlocked", Flags).GetValue(_terminal) ? "1" : "0");
        if (key == "notes")
        {
            var sections = NotepadSection.CaptureMissionState(); var rows = new List<string[]>();
            if (sections != null) foreach (var section in sections)
                if (section != null) rows.Add(new[] { section.SectionTag ?? "", section.Text ?? "", section.HasAddedFirstNote ? "1" : "0" });
            return JsonSerializer.Serialize(rows.OrderBy(row => row[0], StringComparer.Ordinal).ToArray());
        }
        object solution = Get(_terminal, "solution"), auto = Get(_terminal, "autoFire");
        if (key.StartsWith("radar.", StringComparison.Ordinal))
        {
            var radar = Get(_terminal, "radar"); var view = Get(radar, "View");
            if (key == "radar.selection")
            { var selected = Get(view, "Selected"); return selected == null ? "" : (string)Get(selected, "Id"); }
            return JsonSerializer.Serialize(new[] { "View", "Contacts", "<Enabled>k__BackingField", "<Origin>k__BackingField", "<NativeOrigin>k__BackingField", "<OriginSource>k__BackingField" }
                .Select(name => Encode(Get(radar, name), Field(radar, name).FieldType)).ToArray());
        }
        if (key == "intel")
        {
            var assets = Get(solution, "Assets"); var moving = Get(_terminal, "movingCards");
            var selected = Get(solution, "Selected");
            var bindings = ((IEnumerable)Get(solution, "BindingSelection")).Cast<object>().Select(a => (Guid)Get(a, "Identity")).ToArray();
            return JsonSerializer.Serialize(new[] { Encode(assets, assets.GetType()), Encode(moving, moving.GetType()),
                selected == null ? "" : ((Guid)Get(selected, "Identity")).ToString(), JsonSerializer.Serialize(bindings), (string)Get(_terminal, "pendingMovingReport"),
                Encode(Get(_terminal, "quickCardIds"), Field(_terminal, "quickCardIds").FieldType) });
        }
        if (key == "fire") { var state = Call(_terminal, "CaptureFire"); return Encode(state, state.GetType()); }
        if (key == "auto.pending") { var drafts = Call(auto, "PendingSnapshot"); return Encode(drafts, drafts.GetType()); }
        // Full operational job state flows host -> guest only. Guests can propose
        // pending draft edits, but cannot overwrite purchases/fired flags on host.
        return Encode(auto, auto.GetType());
    }
    private object Canonical(object asset, IList assets)
    {
        if (asset == null) return null;
        var identity = (Guid)Get(asset, "Identity");
        return assets.Cast<object>().FirstOrDefault(a => (Guid)Get(a, "Identity") == identity);
    }
    private void Apply(string key, string payload)
    {
        if (key == "auto.access") { _hostUnlocked = payload == "1"; return; }
        if (key == "notes")
        {
            var rows = JsonSerializer.Deserialize<string[][]>(payload);
            if (rows == null || rows.Length > 256) throw new InvalidOperationException("Invalid note sections");
            var sections = new Il2CppSystem.Collections.Generic.List<NotepadSectionSaveData>();
            foreach (var row in rows)
            {
                if (row?.Length != 3 || row[0].Length > 512 || row[1].Length > 200000) throw new InvalidOperationException("Invalid note section");
                sections.Add(new NotepadSectionSaveData { SectionTag = row[0], Text = row[1], HasAddedFirstNote = row[2] == "1" });
            }
            NotepadSection.RestoreMissionState(sections); return;
        }
        object solution = Get(_terminal, "solution"), auto = Get(_terminal, "autoFire");
        var assets = (IList)Get(solution, "Assets");
        if (key.StartsWith("radar.", StringComparison.Ordinal))
        {
            var radar = Get(_terminal, "radar");
            if (key == "radar.selection")
            { var view = Get(radar, "View"); Set(view, "Selected", ((IEnumerable)Get(view, "Assets")).Cast<object>().FirstOrDefault(a => (string)Get(a, "Id") == payload)); }
            else if (Guest)
            {
                var names = new[] { "View", "Contacts", "<Enabled>k__BackingField", "<Origin>k__BackingField", "<NativeOrigin>k__BackingField", "<OriginSource>k__BackingField" };
                var parts = JsonSerializer.Deserialize<string[]>(payload);
                if (parts?.Length != names.Length) throw new InvalidOperationException("Invalid radar document");
                var previousSelection = Get(Get(radar, "View"), "Selected");
                string id = previousSelection == null ? "" : (string)Get(previousSelection, "Id");
                for (int i = 0; i < names.Length; i++) Set(radar, names[i], Decode(parts[i], Field(radar, names[i]).FieldType));
                var view = Get(radar, "View"); var radarAssets = (IList)Get(view, "Assets");
                Set(view, "Selected", radarAssets.Cast<object>().FirstOrDefault(a => (string)Get(a, "Id") == id));
                foreach (DictionaryEntry pair in (IDictionary)Get(radar, "Contacts"))
                    Set(pair.Value, "Card", Canonical(Get(pair.Value, "Card"), radarAssets));
            }
        }
        else if (key == "intel")
        {
            string[] parts = JsonSerializer.Deserialize<string[]>(payload);
            if (parts?.Length != 6) throw new InvalidOperationException("Invalid intel document");
            var incoming = (IList)Decode(parts[0], assets.GetType());
            var moving = (IList)Get(_terminal, "movingCards");
            var decoded = (IList)Decode(parts[1], moving.GetType());
            var ids = JsonSerializer.Deserialize<Guid[]>(parts[3]);
            var quick = Decode(parts[5], Field(_terminal, "quickCardIds").FieldType);
            var retained = new List<object>();
            foreach (var asset in incoming)
            {
                var existing = Canonical(asset, assets) ?? asset;
                if (!ReferenceEquals(existing, asset))
                    foreach (var field in asset.GetType().GetFields(Flags)) if (!field.IsStatic) field.SetValue(existing, field.GetValue(asset));
                retained.Add(existing);
            }
            assets.Clear(); foreach (var asset in retained) assets.Add(asset);
            moving.Clear();
            foreach (var contact in decoded)
            { var card = Canonical(Get(contact, "Card"), assets); if (card != null) { Set(contact, "Card", card); moving.Add(contact); } }
            Set(solution, "Selected", assets.Cast<object>().FirstOrDefault(a => ((Guid)Get(a, "Identity")).ToString() == parts[2]));
            var bindings = (IList)Get(solution, "BindingSelection"); bindings.Clear();
            foreach (var asset in assets) if (ids.Contains((Guid)Get(asset, "Identity"))) bindings.Add(asset);
            Set(_terminal, "pendingMovingReport", parts[4]);
            Set(_terminal, "quickCardIds", quick);
        }
        else if (key == "fire")
        {
            var type = _terminal.GetType().GetMethod("CaptureFire", Flags).ReturnType;
            var state = Decode(payload, type);
            var lead = type.GetProperty("Lead", Flags).GetValue(state);
            if (lead != null)
            {
                var contactField = lead.GetType().GetField("<Contact>k__BackingField", Flags);
                var contact = contactField.GetValue(lead);
                if (contact != null)
                {
                    var card = Canonical(Get(contact, "Card"), assets);
                    var actual = ((IEnumerable)Get(_terminal, "movingCards")).Cast<object>().FirstOrDefault(c => ReferenceEquals(Get(c, "Card"), card));
                    contactField.SetValue(lead, actual);
                    if (actual != null) lead.GetType().GetField("<Report>k__BackingField", Flags).SetValue(lead, Get(actual, "Track"));
                    lead.GetType().GetField("<Origin>k__BackingField", Flags).SetValue(lead, Canonical(lead.GetType().GetProperty("Origin").GetValue(lead), assets));
                }
            }
            if (!Guest) Call(_terminal, "RestoreFireEdit", state);
            else
            {
                // Mirror plans without binding a second physical gun/aim driver.
                var fire = type.GetProperty("Fire").GetValue(state); Set(_terminal, "fire", fire);
                Call(fire, "RestoreTargets", solution, type.GetProperty("Targets").GetValue(state));
            }
        }
        else if (key == "auto.pending")
        {
            var drafts = Call(auto, "PendingSnapshot");
            Call(auto, "RestorePending", Decode(payload, drafts.GetType()));
        }
        else if (key == "auto.status" && Guest)
        {
            var incoming = Decode(payload, auto.GetType());
            // Preserve the queue instance referenced by UI callbacks.
            foreach (var field in auto.GetType().GetFields(Flags)) if (!field.IsStatic) field.SetValue(auto, field.GetValue(incoming));
        }
        Set(_terminal, "dirty", true);
    }
    private void PublishLocal()
    {
        var net = CoopRuntime.Net;
        foreach (var key in Keys)
        {
            if (!net.IsHost && HostOnlyKey(key)) continue;
            string value = Capture(key);
            if (_baseline.TryGetValue(key, out string previous) && previous == value) continue;
            if (!net.IsHost && _inflight.Contains(key)) continue;
            _baseline[key] = value;
            if (net.IsHost) { var entry = _store.Set(key, value); Broadcast(key, entry); }
            else { _inflight.Add(key); Send(net.HostSteamId, 1, key, _store.Get(key).Revision, value); }
        }
    }
    private void Send(ulong to, byte op, string key, long revision, string payload)
    {
        var w = NetProtocol.Begin((MsgType)MsgType);
        w.Put(op); w.Put(Scene); w.Put(_fingerprint); w.Put(key); w.Put(revision);
        byte[] body = MapSnapshotCodec.Encode(Encoding.UTF8.GetBytes(payload)); w.Put(body, 0, body.Length);
        CoopRuntime.Net.SendDirectFragmented(to, NetProtocol.Snapshot(w), true);
    }
    private void Broadcast(string key, SharedDocumentStore.Entry entry)
    { foreach (var player in CoopRuntime.Net.Roster) if (!player.IsLocal) Send(player.SteamId, 2, key, entry.Revision, entry.Payload); }
    private void SendCommand(string command)
    { if (_ready && !_applying) { PublishLocal(); Send(CoopRuntime.Net.HostSteamId, 4, command, 0, ""); } }
    public void Tick(float dt)
    {
        var net = CoopRuntime.Net;
        if (net == null || (net.State != SessionState.Hosting && net.State != SessionState.Joined) || MsgType == 0) return;
        _timer += dt; _retry -= dt;
        if (_timer < .75f) return; _timer = 0;
        try
        {
            if (_scene != Scene) { Reset(); _scene = Scene; }
            if (string.IsNullOrEmpty(Scene) || FireMission.Instance == null || !Bind()) return;
            if (net.IsHost) { _ready = true; PublishLocal(); }
            else if (!_ready) { if (_retry <= 0) { Send(net.HostSteamId, 0, "", 0, ""); _retry = 8; } }
            else PublishLocal();
        }
        catch (Exception ex) { Warn(ex); }
    }
    public void OnPacket(ulong from, byte[] data)
    {
        var net = CoopRuntime.Net;
        if (net == null || _terminal == null) return;
        try
        {
            var r = new NetDataReader(data); r.SkipBytes(NetProtocol.TypeLen);
            byte op = r.GetByte(); string scene = r.GetString(), fingerprint = r.GetString(), key = r.GetString(); long revision = r.GetLong();
            if (scene != Scene || fingerprint != _fingerprint) { if (fingerprint != _fingerprint) CoopRuntime.LogSource?.LogWarning("[ModernizationSync] incompatible Modernization versions; run the file checker on both PCs"); return; }
            if (!net.Roster.Any(p => !p.IsLocal && (ulong)p.SteamId == from)) return;
            string payload = Encoding.UTF8.GetString(MapSnapshotCodec.Decode(r.GetRemainingBytes()));
            if (net.IsHost)
            {
                if (op == 0)
                {
                    PublishLocal(); foreach (var pair in _store.Entries) Send(from, 2, pair.Key, pair.Value.Revision, pair.Value.Payload);
                    Send(from, 3, "", 0, ""); return;
                }
                if (op == 4 && (key == "toggle-auto" || key == "toggle-radar"))
                { Call(_terminal, key == "toggle-auto" ? "ToggleAutoFire" : "ToggleRadar"); PublishLocal(); return; }
                if (op == 4 && (key == "auto-fire-on" || key == "auto-fire-off"))
                { Call(Get(_terminal, "autoFire"), "SetAutoFire", key == "auto-fire-on"); PublishLocal(); return; }
                if (op != 1 || !Keys.Contains(key) || HostOnlyKey(key)) return;
                PublishLocal();
                if (_store.Get(key).Revision == revision)
                {
                    _applying = true; try { Apply(key, payload); } finally { _applying = false; }
                    _baseline.Remove(key); PublishLocal();
                }
                // Acknowledge even unchanged or rejected edits; clients never
                // retry stale defaults over a newer host document.
                var entry = _store.Get(key); Send(from, 2, key, entry.Revision, entry.Payload);
            }
            else if (from == net.HostSteamId)
            {
                if (op == 3) { foreach (var k in Keys) _baseline[k] = Capture(k); _ready = true; return; }
                if (op != 2 || !Keys.Contains(key)) return;
                if (_ready) PublishLocal();
                bool acknowledged = _inflight.Remove(key);
                bool current = revision >= _store.Get(key).Revision;
                if (_store.Receive(key, revision, payload) || (acknowledged && current))
                {
                    _applying = true; try { Apply(key, payload); } finally { _applying = false; }
                    _baseline[key] = Capture(key);
                    if (key == "auto.status" && !_inflight.Contains("auto.pending")) _baseline["auto.pending"] = Capture("auto.pending");
                    if (key == "radar.status" && !_inflight.Contains("radar.selection")) _baseline["radar.selection"] = Capture("radar.selection");
                }
            }
        }
        catch (Exception ex) { Warn(ex); }
    }
    private void Warn(Exception ex)
    {
        if (_warned) return; _warned = true;
        CoopRuntime.LogSource?.LogWarning("[ModernizationSync] unavailable/unsupported model: " + (ex.InnerException?.Message ?? ex.Message));
    }
    public void OnSessionStarted() => Reset();
    public void OnSessionEnded() => Reset();
    public void Reset() { _store.Reset(); _baseline.Clear(); _inflight.Clear(); _terminal = null; _scene = ""; _ready = _warned = _hostUnlocked = false; _timer = _retry = 0; }
}
