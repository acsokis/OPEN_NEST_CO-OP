using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using LiteNetLib.Utils;
using OpenNestCoop.Core;
using OpenNestCoop.Net;
using UnityEngine;

namespace OpenNestCoop.GameSync;

// Both players express a desired manual state. Host snapshots cannot roll back
// an unacknowledged guest interaction; automatic master power stays host-owned.
public sealed class WorldControlsSync : ISyncedModule
{
    private const string Channel = "world.controls.v2";
    public int MsgType => CoopRuntime.Net?.ChannelType(Channel) ?? 0;
    private static WorldControlsSync _instance;
    private float _poll, _scan;
    private string _scene = "";
    private IntPtr _mission;
    private WorldControlIntent _intent = new();
    private ReliableDocumentSync _replica;
    private readonly Dictionary<string, Component> _objects = new();
    private readonly Dictionary<string, byte> _authoritative = new();
    private readonly HashSet<string> _warnings = new();
    public WorldControlsSync() { _instance = this; }
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => CoopSyncRegistry.PendingRegister(false, () => new WorldControlsSync(), Channel);
    private static string Path(Transform transform)
    {
        string path = "";
        for (var current = transform; current != null; current = current.parent) path = current.name + "/" + path;
        return path;
    }
    private static bool Door(AnimatorBoolToggler toggler)
    {
        if (toggler == null || toggler.parameterName != "IsOpen") return false;
        string path = Path(toggler.transform);
        return new[] { "Door", "Gate", "Hatch", "Stair", "Ladder" }.Any(word => path.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    // Do not also forward a toggle click: an old delayed click would invert the
    // already-applied desired state. Other gun/engine buttons keep their route.
    internal static bool OwnsTarget(LookAtTarget target)
    {
        if (_instance == null || target == null) return false;
        foreach (var toggler in target.GetComponentsInParent<AnimatorBoolToggler>(true)) if (Door(toggler)) return true;
        foreach (var toggler in target.GetComponentsInChildren<AnimatorBoolToggler>(true)) if (Door(toggler)) return true;
        return target.GetComponentInParent<SwingLightFlickerController>() != null || target.GetComponentInChildren<SwingLightFlickerController>(true) != null
            || target.GetComponentInParent<SwingLightFlicker>() != null || target.GetComponentInChildren<SwingLightFlicker>(true) != null;
    }
    private static string Key(Component component)
    {
        string kind = component is AnimatorBoolToggler ? "door" : component is SwingLightFlickerController ? "light.controller" : "light";
        int index = 0;
        if (component is AnimatorBoolToggler)
            foreach (var other in component.GetComponents<AnimatorBoolToggler>()) { if (other.Pointer == component.Pointer) break; index++; }
        return Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(kind + ":" + index + ":" + Path(component.transform))));
    }
    private void Scan()
    {
        _objects.Clear();
        foreach (var toggler in UnityEngine.Object.FindObjectsOfType<AnimatorBoolToggler>(true)) if (Door(toggler)) _objects[Key(toggler)] = toggler;
        foreach (var light in UnityEngine.Object.FindObjectsOfType<SwingLightFlickerController>(true)) _objects[Key(light)] = light;
        foreach (var light in UnityEngine.Object.FindObjectsOfType<SwingLightFlicker>(true)) _objects[Key(light)] = light;
    }
    private static byte Read(Component component)
    {
        if (component is AnimatorBoolToggler door) return (byte)(door.GetBool() ? 1 : 0);
        if (component is SwingLightFlickerController controller) return (byte)((controller._switchOn ? 1 : 0) | (controller._masterPowerOn ? 2 : 0));
        var light = (SwingLightFlicker)component;
        return (byte)((light._manualOverrideOff ? 1 : 0) | (light._emergencyPlayerOverride ? 2 : 0) | (light._masterPowerOn ? 4 : 0));
    }
    private static byte Mask(Component component) => (byte)(component is SwingLightFlicker ? 3 : 1);
    private static void Apply(Component component, byte value)
    {
        if (Read(component) == value) return;
        if (component is AnimatorBoolToggler door) { door.SetBool((value & 1) != 0); return; }
        if (component is SwingLightFlickerController controller) { controller.SetSwitch((value & 1) != 0, false); controller.SetMasterPower((value & 2) != 0, false); return; }
        var light = (SwingLightFlicker)component;
        light.SetMasterPowerState((value & 4) != 0, false); light.SetManualOverride((value & 1) != 0); light.SetEmergencyManualOn((value & 2) != 0);
    }
    private string Capture(string unused)
    {
        var values = new SortedDictionary<string, byte>(StringComparer.Ordinal);
        foreach (var pair in _objects) if (pair.Value != null) values[pair.Key] = Read(pair.Value);
        return JsonSerializer.Serialize(values);
    }
    private void Observe()
    {
        if (_replica?.Ready != true || CoopRuntime.Net.IsHost) return;
        foreach (var pair in _objects)
            if (pair.Value != null && _intent.Observe(pair.Key, Read(pair.Value), Mask(pair.Value), out var command))
                _replica.QueueCommand(command.Wire);
    }
    private void Restore(string unused, string json)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, byte>>(json);
        if (values == null || values.Count > 4096) throw new InvalidOperationException("Invalid control snapshot");
        Observe(); // sample the user's latest click BEFORE consuming a host heartbeat
        foreach (var pair in values)
        {
            _authoritative[pair.Key] = pair.Value;
            if (!_objects.TryGetValue(pair.Key, out var component) || component == null) continue;
            Apply(component, _intent.Desired(pair.Key, pair.Value, Mask(component)));
            _intent.Applied(pair.Key, Read(component));
        }
    }
    private void Execute(string wire)
    {
        if (!WorldControlCommand.Parse(wire, out var command) || !_objects.TryGetValue(command.Key, out var component) || component == null)
            throw new InvalidOperationException("Unknown world control");
        byte mask = Mask(component), before = Read(component);
        Apply(component, (byte)((before & ~mask) | (command.Value & mask)));
        CoopRuntime.LogSource?.LogInfo("[WorldControls] guest desired state applied: " + component.name + " value=" + Read(component));
    }
    private void CommandDone(string wire, bool accepted)
    {
        if (!WorldControlCommand.Parse(wire, out var command)) return;
        _intent.Acknowledge(command, accepted);
        if (accepted)
        { if (_objects.TryGetValue(command.Key, out var component) && component != null) _intent.Applied(command.Key, Read(component)); }
        else
            foreach (var pair in _authoritative)
                if (_objects.TryGetValue(pair.Key, out var component) && component != null) { Apply(component, pair.Value); _intent.Applied(pair.Key, Read(component)); }
    }
    private void Report(string warning) { if (_warnings.Count < 32 && _warnings.Add(warning)) CoopRuntime.LogSource?.LogWarning("[WorldControls] " + warning); }
    private void Send(ulong to, DocumentPacket packet)
    {
        var writer = NetProtocol.Begin((MsgType)MsgType); writer.Put(_scene);
        byte[] bytes = DocumentPacketCodec.Encode(packet); writer.Put(bytes, 0, bytes.Length);
        CoopRuntime.Net.SendDirectFragmented(to, NetProtocol.Snapshot(writer), true);
    }
    public void Tick(float dt)
    {
        var net = CoopRuntime.Net;
        if (net == null || (net.State != SessionState.Hosting && net.State != SessionState.Joined) || MsgType == 0) return;
        _poll += dt; _scan -= dt; if (_poll < .2f) return; _poll = 0;
        try
        {
            string scene = MissionManager.Instance?.CurrentMissionSceneName ?? ""; var mission = FireMission.Instance?.Pointer ?? IntPtr.Zero;
            if (_scene != scene || _mission != mission) { Reset(); _scene = scene; _mission = mission; }
            if (_scan <= 0) { Scan(); _scan = 4; }
            if (_replica == null)
            {
                _replica = new ReliableDocumentSync(net.IsHost, net.HostSteamId, new[] { "controls" }, new[] { "controls" }, Capture, Restore, Send, Execute, Report);
                _replica.CommandCompleted += CommandDone;
            }
            _replica.Peers.Clear(); foreach (var peer in net.Roster) if (!peer.IsLocal) _replica.Peers.Add(peer.SteamId);
            Observe(); _replica.Tick(Time.realtimeSinceStartup);
        }
        catch (Exception ex) { Report(ex.Message); }
    }
    public void OnPacket(ulong from, byte[] data)
    {
        var net = CoopRuntime.Net;
        if (net == null || _replica == null || !net.Roster.Any(peer => !peer.IsLocal && (ulong)peer.SteamId == from)) return;
        try { var reader = new NetDataReader(data); reader.SkipBytes(NetProtocol.TypeLen); if (reader.GetString() == _scene) _replica.Receive(from, DocumentPacketCodec.Decode(reader.GetRemainingBytes())); }
        catch (Exception ex) { Report(ex.Message); }
    }
    public void OnSessionStarted() => Reset();
    public void OnSessionEnded() => Reset();
    public void Reset() { _replica = null; _intent = new WorldControlIntent(); _objects.Clear(); _authoritative.Clear(); _warnings.Clear(); _scene = ""; _mission = IntPtr.Zero; _poll = _scan = 0; }
}
