using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    private const string Channel = "modernization.documents.v2";
    public int MsgType => CoopRuntime.Net?.ChannelType(Channel) ?? 0;
    private static ModernizationStateSync _instance;
    private static readonly string[] Keys = { "auto.access", "notes", "intel", "radar.status", "radar.selection", "fire", "auto.pending", "auto.status", "map.panel" };
    private ReliableDocumentSync _replica;
    private readonly HashSet<string> _reported = new();
    private object _terminal;
    private Assembly _assembly;
    private Assembly _mapAssembly;
    private ManagedStateCodec _codec;
    private string _fingerprint = "", _scene = "";
    private float _timer;
    private IntPtr _mission;
    private bool _applying, _warned, _patched, _mapPatched;
    private bool _hostUnlocked;
    private float _auditAt;
    private int _sentPackets, _receivedPackets;
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
    private static bool HostOnlyKey(string key) => key == "auto.status" || key == "radar.status" || key == "auto.access" || key == "map.panel";
    private object MapPanel => _mapAssembly?.GetType("IronNestMapDisplay.MapPurchasePanel")?
        .GetField("Current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
    private static bool QueueMap(string wire)
    {
        var sync = _instance;
        if (sync?._replica?.Ready != true || !MapPanelCommand.TryParse(wire, out _)) return false;
        sync.SendCommand(wire); return true;
    }
    private static bool MapSelectPrefix(string __0)
    { if (Guest) QueueMap("map|select|" + __0); return true; } // local preview remains responsive
    private static bool MapAcquirePrefix(string __0, int __1, ref string __result)
    {
        if (!Guest) return true;
        __result = QueueMap("map|acquire|" + __1 + "|" + __0) ? "CO-OP REQUEST SENT" : "CO-OP SYNC NOT READY"; return false;
    }
    private static bool MapExternalPrefix(string __0, int __1, ref string __result)
    {
        if (!Guest) return true;
        __result = QueueMap("map|external|" + __1 + "|" + __0) ? "CO-OP REQUEST SENT" : "CO-OP SYNC NOT READY"; return false;
    }
    private static bool MapOrderPrefix(object __instance, string __0, string __1)
    {
        if (!Guest) return true;
        var panel = _instance?.MapPanel;
        if (panel != null && ReferenceEquals(Get(panel, "order"), __instance))
        {
            // The local UI reserves before invoking Start. Release that local
            // reservation; the host will reserve once in its own transaction.
            long reservation = (long)Get(panel, "orderReservation");
            Call(Get(panel, "purchases"), "ReleaseReservation", reservation); Set(panel, "orderReservation", 0L);
            Set(panel, "message", QueueMap("map|detent|" + __0 + "|" + __1) ? "CO-OP REQUEST SENT" : "CO-OP SYNC NOT READY");
        }
        return false;
    }
    private void BindMapPanel()
    {
        if (_mapPatched) return;
        _mapAssembly ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "IronNestMapDisplay");
        if (_mapAssembly == null) return;
        var panel = _mapAssembly.GetType("IronNestMapDisplay.MapPurchasePanel", true);
        var order = _mapAssembly.GetType("IronNestMapDisplay.DetentOrder", true);
        var methods = new[] { AccessTools.Method(panel, "Select"), AccessTools.Method(panel, "RequestAcquire"),
            AccessTools.Method(panel, "RequestExternal"), AccessTools.Method(order, "Start"), AccessTools.Method(order, "Tick") };
        if (methods.Any(m => m == null)) throw new MissingMethodException("Modernization purchase panel contract changed");
        var prefixes = new[] { nameof(MapSelectPrefix), nameof(MapAcquirePrefix), nameof(MapExternalPrefix), nameof(MapOrderPrefix), nameof(AllowExecution) };
        var harmony = new HarmonyLib.Harmony("opennest.modernization.map.panel");
        for (int i = 0; i < methods.Length; i++) harmony.Patch(methods[i], prefix: new HarmonyMethod(typeof(ModernizationStateSync), prefixes[i]));
        _mapPatched = true;
        CoopRuntime.LogSource?.LogInfo("[ModernizationSync] map purchase commands routed to host with explicit left/right lane");
    }
    private static bool AutoAccessPrefix(ref bool __result)
    {
        if (!Guest || _instance?._replica?.Ready != true) return true;
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
            _terminal = terminal;
            // Keep host revisions while recreating UI; a guest must obtain a
            // fresh snapshot before publishing a new terminal's default models.
            if (Guest) _replica?.Invalidate();
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
        BindMapPanel();
        _fingerprint = _assembly.ManifestModule.ModuleVersionId.ToString("N") + ":" + (_mapAssembly?.ManifestModule.ModuleVersionId.ToString("N") ?? "none");
        return true;
    }
    private string Capture(string key)
    {
        if (key == "map.panel")
        {
            var panel = MapPanel;
            if (panel == null) return "null";
            var status = (string[])Get(panel, "ExternalStatus");
            return JsonSerializer.Serialize(new[] { (string)Get(panel, "selected"), (string)Get(panel, "message"),
                (string)Get(panel, "orderStatus"), status[0], status[1] });
        }
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
        if (key == "map.panel")
        {
            if (payload == "null") return;
            var panel = MapPanel ?? throw new InvalidOperationException("Purchase panel not ready");
            var values = JsonSerializer.Deserialize<string[]>(payload);
            if (values?.Length != 5) throw new InvalidOperationException("Invalid purchase panel state");
            Set(panel, "selected", values[0]); Set(panel, "message", values[1]); Set(panel, "orderStatus", values[2]);
            var status = (string[])Get(panel, "ExternalStatus"); status[0] = values[3]; status[1] = values[4];
            if ((bool)panel.GetType().GetProperty("Alive", Flags).GetValue(panel)) Call(panel, "Render");
            return;
        }
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
    private void EnsureReplica()
    {
        if (_replica != null) return;
        var net = CoopRuntime.Net;
        _replica = new ReliableDocumentSync(net.IsHost, net.HostSteamId, Keys, Keys.Where(HostOnlyKey), Capture,
            (key, payload) => { _applying = true; try { Apply(key, payload); } finally { _applying = false; } },
            Send, ExecuteCommand, Report,
            new Dictionary<string, string[]> { ["auto.status"] = new[] { "auto.pending" }, ["radar.status"] = new[] { "radar.selection" } });
    }
    private void Send(ulong to, DocumentPacket packet)
    {
        var w = NetProtocol.Begin((MsgType)MsgType);
        w.Put(Scene); w.Put(_fingerprint);
        byte[] body = DocumentPacketCodec.Encode(packet); w.Put(body, 0, body.Length);
        CoopRuntime.Net.SendDirectFragmented(to, NetProtocol.Snapshot(w), true);
        _sentPackets++;
    }
    private void SendCommand(string command)
    { if (!_applying) _replica?.QueueCommand(command); }
    private void ExecuteCommand(string command)
    {
        if (Guest) throw new InvalidOperationException("Physical controls are host-only");
        if (command.StartsWith("map|", StringComparison.Ordinal)) { ExecuteMapCommand(command); return; }
        if (command == "toggle-auto") Call(_terminal, "ToggleAutoFire");
        else if (command == "toggle-radar") Call(_terminal, "ToggleRadar");
        else if (command == "auto-fire-on" || command == "auto-fire-off")
        {
            if (!(bool)_terminal.GetType().GetProperty("AutoUnlocked", Flags).GetValue(_terminal))
                throw new InvalidOperationException("Host FCS access is locked");
            Call(Get(_terminal, "autoFire"), "SetAutoFire", command == "auto-fire-on");
        }
        else throw new InvalidOperationException("Unknown Modernization control");
    }
    private void ExecuteMapCommand(string wire)
    {
        if (!MapPanelCommand.TryParse(wire, out var command)) throw new InvalidOperationException("Invalid purchase command");
        var panel = MapPanel ?? throw new InvalidOperationException("Purchase panel not ready");
        var policy = _mapAssembly.GetType("IronNestMapDisplay.PurchasePolicy", true);
        var shells = (IEnumerable)policy.GetField("Shells", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
        if (!shells.Cast<string>().Contains(command.Product, StringComparer.Ordinal)) throw new InvalidOperationException("Unknown shell product");
        if (command.Action == "select") { Call(panel, "Select", command.Product); return; }
        if (command.Action == "acquire" || command.Action == "external")
        {
            string result = (string)Call(panel, command.Action == "acquire" ? "RequestAcquire" : "RequestExternal", command.Product, command.Side);
            if (!string.IsNullOrEmpty(result)) throw new InvalidOperationException(result);
            return;
        }
        if (command.Gear == "N") return;
        string reason = (string)Call(panel, "Reason", command.Product, true);
        if (!string.IsNullOrEmpty(reason)) throw new InvalidOperationException(reason);
        if ((bool)panel.GetType().GetProperty("ShellHandlingBusy", Flags).GetValue(panel)) throw new InvalidOperationException("Shell handling busy");
        var purchases = Get(panel, "purchases");
        long reservation = (long)Call(purchases, "ReserveOrdinary");
        if (reservation == 0) throw new InvalidOperationException("Purchase reservation unavailable");
        Set(panel, "orderReservation", reservation); Set(panel, "selected", command.Product); Set(panel, "orderStatus", "");
        try { Call(Get(panel, "order"), "Start", command.Gear, command.Product, (double)UnityEngine.Time.time); }
        catch { Call(purchases, "ReleaseReservation", reservation); Set(panel, "orderReservation", 0L); throw; }
    }
    private void Report(string message)
    { if (_reported.Count < 32 && _reported.Add(message)) CoopRuntime.LogSource?.LogWarning("[ModernizationSync] " + message); }
    public void Tick(float dt)
    {
        var net = CoopRuntime.Net;
        if (net == null || (net.State != SessionState.Hosting && net.State != SessionState.Joined) || MsgType == 0) return;
        _timer += dt;
        if (_timer < .75f) return; _timer = 0;
        try
        {
            var mission = FireMission.Instance?.Pointer ?? IntPtr.Zero;
            if (_scene != Scene || _mission != mission) { Reset(); _scene = Scene; _mission = mission; }
            bool bound = !string.IsNullOrEmpty(Scene) && FireMission.Instance != null && Bind();
            if (!bound)
            {
                if (UnityEngine.Time.realtimeSinceStartup >= _auditAt)
                {
                    _auditAt = UnityEngine.Time.realtimeSinceStartup + 10;
                    CoopRuntime.LogSource?.LogInfo($"[ModernizationSync] waiting scene='{Scene}' mission={mission} terminal={_terminal != null}");
                }
                return;
            }
            EnsureReplica();
            _replica.Peers.Clear();
            foreach (var player in net.Roster) if (!player.IsLocal) _replica.Peers.Add(player.SteamId);
            _replica.Tick(UnityEngine.Time.realtimeSinceStartup);
            if (UnityEngine.Time.realtimeSinceStartup >= _auditAt)
            {
                _auditAt = UnityEngine.Time.realtimeSinceStartup + 10;
                CoopRuntime.LogSource?.LogInfo($"[ModernizationSync] role={(net.IsHost ? "host" : "guest")} ready={_replica.Ready} peers={_replica.Peers.Count} host={net.HostSteamId} tx={_sentPackets} rx={_receivedPackets} pending={_replica.PendingEdits}");
            }
        }
        catch (Exception ex) { Warn(ex); }
    }
    public void OnPacket(ulong from, byte[] data)
    {
        var net = CoopRuntime.Net;
        if (net == null || _terminal == null || _replica == null) return;
        try
        {
            var r = new NetDataReader(data); r.SkipBytes(NetProtocol.TypeLen);
            string scene = r.GetString(), fingerprint = r.GetString();
            if (scene != Scene || fingerprint != _fingerprint) { if (fingerprint != _fingerprint) Report("Incompatible Modernization versions; run the file checker on both PCs"); return; }
            if (!net.Roster.Any(p => !p.IsLocal && (ulong)p.SteamId == from)) return;
            _replica.Receive(from, DocumentPacketCodec.Decode(r.GetRemainingBytes()));
            _receivedPackets++;
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
    public void Reset() { _replica = null; _reported.Clear(); _terminal = null; _mission = IntPtr.Zero; _scene = ""; _warned = _hostUnlocked = false; _timer = 0; }
}
